using System.Collections.Generic;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 把 BlockWorld 的数据变成网格：每个 chunk 一个 GameObject（MeshFilter + MeshRenderer）。
    /// 每个面按 8 位上下文选图集里的一格（自动包边），并按方块类型拆成 submesh（每种类型一个材质）。
    /// 明暗用顶点色写死（顶 1.0 / 侧 0.8、0.65 / 底 0.5），不依赖实时光照 —— 这是 Minecraft 观感的来源。
    /// </summary>
    [RequireComponent(typeof(BlockWorld))]
    public class BlockChunkRenderer : MonoBehaviour
    {
        [Tooltip("方块边长（世界单位）。1 = 一个方块 1 米")]
        public float blockSize = 1f;

        [Tooltip("勾上则重建网格后顺带刷新碰撞体（地图编辑器不需要，可关）")]
        public bool generateColliders = false;

        [Tooltip("烘焙静态几何时勾上：跳过标记为「交互道具」的方块，避免把可交互物件焊进静态资产")]
        public bool skipInteractiveBlocks = false;

        BlockWorld _world;
        readonly Dictionary<Vector3Int, GameObject> _chunks = new Dictionary<Vector3Int, GameObject>();
        readonly List<Vector3Int> _dirtyBuffer = new List<Vector3Int>();
        readonly Dictionary<BlockDefinition, Material> _materials = new Dictionary<BlockDefinition, Material>();

        // 每个面用到的：(法线, 面内 u 轴, 面内 v 轴)
        static readonly (Vector3 n, Vector3 u, Vector3 v)[] Faces =
        {
            (Vector3.up,      Vector3.right,  Vector3.forward), // +Y 顶
            (Vector3.down,    Vector3.right, -Vector3.forward), // -Y 底
            (Vector3.right,  -Vector3.forward, Vector3.up),     // +X
            (Vector3.left,    Vector3.forward, Vector3.up),     // -X
            (Vector3.forward, Vector3.right,   Vector3.up),     // +Z
            (Vector3.back,   -Vector3.right,   Vector3.up),     // -Z
        };

        void Awake() => _world = GetComponent<BlockWorld>();

        /// <summary>
        /// 懒解析。编辑模式下用 AddComponent 添加组件时 Awake 不会执行，
        /// 所以任何入口（包括编辑器脚本直接调 RebuildAll）都要先过一遍这里。
        /// </summary>
        void EnsureWorld()
        {
            if (_world == null) _world = GetComponent<BlockWorld>();
        }

        void OnEnable()
        {
            EnsureWorld();
            _world.ChunkDirty += OnChunkDirty;
        }

        void OnDisable()
        {
            if (_world != null) _world.ChunkDirty -= OnChunkDirty;
        }

        void Start()
        {
            foreach (var c in _world.AllChunks()) RebuildChunk(c);
        }

        void LateUpdate()
        {
            _world.ConsumeDirty(_dirtyBuffer);
            if (_dirtyBuffer.Count == 0) return;
            foreach (var c in _dirtyBuffer) RebuildChunk(c);
        }

        /// <summary>
        /// 把场景里所有"烘焙发光体"的光叠加进顶点色（像素噪波 + 五层阶梯 + 高饱和过渡 + 光锥散射）。
        /// 顶点色会被地形材质乘进颜色（toon shader 的 input.color.rgb），所以这是"发光体自己提亮"。
        /// 在每次重建时调用 → 烘焙结果不会被覆盖；运行时不参与任何实时光照计算。
        /// </summary>
        void ApplyBakedLightAreas(List<Vector3> verts, List<Color> cols)
        {
            var areas = FindObjectsOfType<BlockMap.Tools.BlockMapBakedLightArea>();
            if (areas == null || areas.Length == 0) return;

            for (int i = 0; i < verts.Count && i < cols.Count; i++)
            {
                Vector3 wp = transform.TransformPoint(verts[i]);
                Color add = Color.black;

                for (int a = 0; a < areas.Length; a++)
                {
                    var area = areas[a];
                    if (area == null) continue;

                    Vector3 lp = area.transform.InverseTransformPoint(wp);
                    float len = Mathf.Max(0.01f, area.EffectiveRange);
                    if (lp.z < 0f || lp.z > len) continue;

                    // 光锥散射：越远光斑越大 → 等价于把采样点按散射倍率收缩
                    float widen = Mathf.Max(1f, area.ScatterFactorAt(lp.z));
                    Color c = area.CellLight(wp, new Vector2(lp.x, lp.y) / widen, lp.z, out float level);
                    if (level <= 0.001f) continue;

                    add += new Color(c.r * level, c.g * level, c.b * level, 1f);
                }

                if (add.maxColorComponent <= 0.0001f) continue;

                Color src = cols[i];
                cols[i] = new Color(
                    Mathf.Clamp01(src.r + add.r * (1f - src.r)),
                    Mathf.Clamp01(src.g + add.g * (1f - src.g)),
                    Mathf.Clamp01(src.b + add.b * (1f - src.b)),
                    src.a);
            }
        }

        /// <summary>销毁 chunk 对象和运行时网格；编辑模式使用 DestroyImmediate。</summary>
        static void DestroySafe(Object go)
        {
            if (go == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { Object.DestroyImmediate(go); return; }
#endif
            Object.Destroy(go);
        }

        void OnChunkDirty(Vector3Int chunk) { /* 由 LateUpdate 统一处理 */ }

        public Material GetMaterial(BlockDefinition def)
        {
            if (def == null) return null;
            if (_materials.TryGetValue(def, out var mat) && mat != null) return mat;

            // 按方块的渲染方式挑 shader（都放在 Resources 里，保证打包不被剥离）
            // ★ 不透明地形优先用"受光"的卡通 shader：这样才能读环境光、并被实时灯（Lamp 的点光源）照亮。
            //   旧版一律用 BlockMap/BlockUnlit（不受光）→ 看起来像 2D 平涂，启动也读不到环境光。
            // Transparent keeps its legacy shader ID so baked materials also gain Toon lighting.
            string shaderName = def.renderMode == BlockRenderMode.Transparent ? "BlockMap/BlockUnlitTransparent"
                              : def.renderMode == BlockRenderMode.Animated ? "BlockMap/BlockAnimated"
                              : "Slima/Toon (Cel + Pixel)";
            var shader = Shader.Find(shaderName);
            if (shader == null) shader = Resources.Load<Shader>(shaderName);

            // 主材质不可用时，使用包内兼容材质。
            if (shader == null)
            {
                string fallback = def.renderMode == BlockRenderMode.Transparent ? "BlockMap/BlockUnlitTransparent"
                                : def.renderMode == BlockRenderMode.Animated ? "BlockMap/BlockAnimated"
                                : "BlockMap/BlockUnlit";
                shader = Shader.Find(fallback);
                if (shader == null) shader = Resources.Load<Shader>(fallback);
            }
            if (shader == null) shader = Shader.Find("Unlit/Transparent Cutout");

            mat = new Material(shader) { name = "BlockMap_" + def.id };
            mat.mainTexture = def.GetTexture();
            // 卡通 shader 的接口（[MainTexture] 已把 mainTexture 映射到 _BaseMap，这里显式设一遍更稳）
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", def.GetTexture());
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap",
                def.emissionMap != null ? def.emissionMap : Texture2D.blackTexture);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", def.emissionColor);
            if (mat.HasProperty("_EmissionStrength")) mat.SetFloat("_EmissionStrength", def.emissionStrength);
            if (def.IsLamp && mat.HasProperty("_NoiseWorldSize")) mat.SetFloat("_NoiseWorldSize", 0.04f);
            // 地形由独立的全屏遮罩描边；关闭反转壳和顶点吸附，光照方格由 Shader 单独采样。
            if (mat.HasProperty("_OutlineWidth")) mat.SetFloat("_OutlineWidth", 0f);
            if (mat.HasProperty("_PixelSnap")) mat.SetFloat("_PixelSnap", 0f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", def.cutoff);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", def.doubleSided ? 0f : 2f);
            if (mat.HasProperty("_Frames")) mat.SetFloat("_Frames", Mathf.Max(1, def.animFrames));
            if (mat.HasProperty("_Speed")) mat.SetFloat("_Speed", def.animSpeed);
            // 半透明最后画，避免被不透明方块挡住
            if (def.renderMode == BlockRenderMode.Transparent) mat.renderQueue = 3000;
            _materials[def] = mat;
            return mat;
        }

        /// <summary>
        /// 渲染判定专用："视觉上算不算实心"。
        /// 带模型的物件（门/箱子/家具…）与「交互/标记方块」**都不算**实心 —— 否则会把邻居的面剔掉、
        /// 也会让邻居的包边认定"这边有邻居"，结果门下方/周围的地板看起来是透明的空洞。
        /// 透明邻居只剔除同种透明块之间的内部面，不能剔掉后方不透明地形的贴图面。
        /// 注意：只用于渲染；BlockWorld.IsSolid 仍然认为它有数据，所以射线照样能点到它。
        /// </summary>
        bool VisuallySolid(Vector3Int p, BlockDefinition source)
        {
            if (_world == null || !_world.IsSolid(p)) return false;
            var d = BlockLibrary.Get(_world.GetIndex(p));
            if (d == null) return false;
            return !d.interactive && d.previewPrefab == null &&
                (d.renderMode != BlockRenderMode.Transparent || d == source);
        }
        public void RebuildAll()
        {
            EnsureWorld();
            var alive = new HashSet<Vector3Int>(_world.AllChunks());
            var dead = new List<Vector3Int>();
            foreach (var kv in _chunks) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var c in dead)
            {
                if (_chunks[c] != null) DestroySafe(_chunks[c]);
                _chunks.Remove(c);
            }
            foreach (var c in alive) RebuildChunk(c);
        }

        void RebuildChunk(Vector3Int chunk)
        {
            var blocks = new List<KeyValuePair<Vector3Int, int>>(_world.BlocksInChunk(chunk));
            if (blocks.Count == 0)
            {
                if (_chunks.TryGetValue(chunk, out var stale) && stale != null) DestroySafe(stale);
                _chunks.Remove(chunk);
                return;
            }

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var subTris = new List<List<int>>();
            var subDefs = new List<BlockDefinition>();

            foreach (var kv in blocks)
            {
                var def = BlockLibrary.Get(kv.Value);
                if (def == null) continue;
                // ① 烘焙模式（菜单 ②）：交互道具与特殊模型都不烘进静态几何（由生成器实例化 Prefab）
                if (skipInteractiveBlocks && (def.interactive || def.previewPrefab != null)) continue;
                // ② 平时的方块几何：配了模型的方块也不再画立方体 ——
                //    否则那层占位标记立方体会把模型整个包在里面，看起来就是一个粉红色方块。
                if (def.previewPrefab != null ||
                    Gameplay.BlockGameplayCatalog.Get(def.id)?.Kind == Gameplay.GameplayBlockKind.Door) continue;
                int sub = subDefs.IndexOf(def);
                if (sub < 0) { subDefs.Add(def); subTris.Add(new List<int>()); sub = subDefs.Count - 1; }

                int tileSize = (def.UseAutoTile && def.atlasOverride != null) ? Mathf.Max(1, def.atlasOverride.width / AutoTileAtlas.Sheet)
                             : (def.panelTexture != null ? Mathf.Min(def.panelTexture.width, def.panelTexture.height)
                             : (def.simpleTexture != null ? Mathf.Min(def.simpleTexture.width, def.simpleTexture.height) : 16));

                for (int f = 0; f < Faces.Length; f++)
                {
                    var (n, u, v) = Faces[f];
                    var nb = kv.Key + Vector3Int.RoundToInt(n);
                    if (VisuallySolid(nb, def)) continue;                // 透明邻居不能挡掉后方贴图

                    Rect uvRect = def.UseAutoTile
                        ? AutoTileAtlas.TileRect(TileIndexForFace(kv.Key, kv.Value, u, v), tileSize)
                        : new Rect(0, 0, 1, 1);

                    float shade = n.y > 0.5f ? 1.0f : (n.y < -0.5f ? 0.5f : (Mathf.Abs(n.x) > 0.5f ? 0.65f : 0.8f));
                    Color col = new Color(shade * def.tint.r, shade * def.tint.g, shade * def.tint.b, def.tint.a);

                    // 面必须沿自己的法线推到方块表面（+ 半个方块），
                    // 否则 6 个面全部穿过方块中心互相交叉 → 就是之前那团"炸面/六角星"。
                    var center = (Vector3)kv.Key * blockSize + Vector3.one * (blockSize * 0.5f)
                                 + n * (blockSize * 0.5f);
                    float h = blockSize * 0.5f;
                    Vector3 a = center - u * h - v * h;
                    Vector3 b = center - u * h + v * h;
                    Vector3 c = center + u * h + v * h;
                    Vector3 d = center + u * h - v * h;

                    int b0 = verts.Count;
                    verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                    uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
                    uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
                    uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
                    uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
                    cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);

                    var tris = subTris[sub];
                    // Unity 正面 = 从外侧看为顺时针。实测之前的分支反了（表现为能看到方块内侧、
                    // 自适应边框长在看不见的面上），这里按"叉积应指向方块内部"来判定。
                    bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f;
                    if (flip)
                    {
                        tris.Add(b0 + 0); tris.Add(b0 + 2); tris.Add(b0 + 1);
                        tris.Add(b0 + 0); tris.Add(b0 + 3); tris.Add(b0 + 2);
                    }
                    else
                    {
                        tris.Add(b0 + 0); tris.Add(b0 + 1); tris.Add(b0 + 2);
                        tris.Add(b0 + 0); tris.Add(b0 + 2); tris.Add(b0 + 3);
                    }
                }
            }

            if (verts.Count == 0)
            {
                if (_chunks.TryGetValue(chunk, out var empty) && empty != null) DestroySafe(empty);
                _chunks.Remove(chunk);
                return;
            }

            var mesh = new Mesh { name = $"Chunk_{chunk.x}_{chunk.y}_{chunk.z}" };
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            // ★ 发光体（Tools 工具）烤进顶点色：必须在**每次重建**时叠加，
            //   否则这里随后的 SetColors 会把烘焙结果覆盖掉 —— 表现就是"风格化不显示"。
            ApplyBakedLightAreas(verts, cols);

            mesh.SetColors(cols);
            mesh.subMeshCount = subTris.Count;
            for (int i = 0; i < subTris.Count; i++) mesh.SetTriangles(subTris[i], i);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (!_chunks.TryGetValue(chunk, out var go) || go == null)
            {
                go = new GameObject($"Chunk_{chunk.x}_{chunk.y}_{chunk.z}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                _chunks[chunk] = go;
            }

            var mf = go.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) DestroySafe(mf.sharedMesh);
            mf.sharedMesh = mesh;

            var mr = go.GetComponent<MeshRenderer>();
            var mats = new Material[subDefs.Count];
            for (int i = 0; i < subDefs.Count; i++) mats[i] = GetMaterial(subDefs[i]);
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            GetComponent<BlockMapGameRoot>()?.ApplyAtmosphereTo(mr);

            if (generateColliders)
            {
                var mc = go.GetComponent<MeshCollider>();
                if (mc == null) mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = null;
                mc.sharedMesh = mesh;
            }
        }

        /// <summary>算这一个面的 8 位上下文 → 图集格号。规则与 AutoTileAtlas.Index 一致。</summary>
        int TileIndexForFace(Vector3Int pos, int index, Vector3 u, Vector3 v)
        {
            var uStep = Vector3Int.RoundToInt(u);
            var vStep = Vector3Int.RoundToInt(v);

            bool up = _world.IsSame(pos + vStep, index);
            bool down = _world.IsSame(pos - vStep, index);
            bool right = _world.IsSame(pos + uStep, index);
            bool left = _world.IsSame(pos - uStep, index);
            bool topLeft = up && left && _world.IsSame(pos - uStep + vStep, index);
            bool topRight = up && right && _world.IsSame(pos + uStep + vStep, index);
            bool bottomLeft = down && left && _world.IsSame(pos - uStep - vStep, index);
            bool bottomRight = down && right && _world.IsSame(pos + uStep - vStep, index);

            return AutoTileAtlas.Index(up, down, left, right, topLeft, topRight, bottomLeft, bottomRight);
        }
    }
}
