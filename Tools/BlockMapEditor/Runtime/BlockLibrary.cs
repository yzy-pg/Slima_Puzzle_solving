using System.Collections.Generic;
using BlockMap.Gameplay;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 方块库 —— 从 Resources 里自动收集所有 BlockDefinition，不写死任何常量。
    /// 加素材的流程：把两张贴图放进工程 → Create → BlockMap → Block Definition → 填 id/名字/贴图
    /// → 把这个 .asset 放到任意 Resources/BlockDefinitions/ 下 → 运行时自动出现（打包后同样有效）。
    /// </summary>
    public static class BlockLibrary
    {
        public const string ResourceFolder = "BlockDefinitions";

        /// <summary>
        /// 玩法标记方块：在地图编辑器里像门一样当特殊方块刷，但它们是"标记"不是地形 ——
        ///   · 烘焙静态几何时会被跳过（不会变成挡路的立方体）
        ///   · 运行时由 BlockMapGameRoot 读格位：起点放角色、终点当通关判定，并画一个可见标记
        /// 想让它们有真实外观：把 Resources/BlockModels/&lt;id&gt;.prefab 放进去即可（会自动接上并优先使用）。
        /// </summary>
        public static readonly string[] MarkerBlockIds = { "Spawn", "Spawn_Synchronous", "Spawn_Asynchronous", "Exit" };

        public static bool IsMarkerBlock(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            for (int i = 0; i < MarkerBlockIds.Length; i++)
            {
                if (MarkerBlockIds[i] == id) return true;
            }
            return false;
        }

        static List<BlockDefinition> _blocks;
        static Dictionary<string, BlockDefinition> _byId;

        public static IList<BlockDefinition> All
        {
            get { EnsureLoaded(); return _blocks; }
        }

        public static int Count { get { EnsureLoaded(); return _blocks.Count; } }

        public static BlockDefinition Get(int index)
        {
            EnsureLoaded();
            return (index >= 0 && index < _blocks.Count) ? _blocks[index] : null;
        }

        public static BlockDefinition Get(string id)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var def) ? def : null;
        }

        public static int IndexOf(string id)
        {
            EnsureLoaded();
            for (int i = 0; i < _blocks.Count; i++)
                if (_blocks[i].id == id) return i;
            return -1;
        }

        /// <summary>运行期临时注册（例如示例方块）。id 重复则不覆盖。</summary>
        public static void Register(BlockDefinition def)
        {
            EnsureLoaded();
            if (def == null || _byId.ContainsKey(def.id)) return;
            _blocks.Add(def);
            _byId[def.id] = def;
        }

        public static void Reload()
        {
            _blocks = null;
            _byId = null;
            EnsureLoaded();
        }

        static void EnsureLoaded()
        {
            if (_blocks != null) return;

            _blocks = new List<BlockDefinition>();
            _byId = new Dictionary<string, BlockDefinition>();

            // 方块定义资产：递归扫描整个 Resources，允许把 .asset 定义放在任意子目录
            //（约定命名流程不需要它，这是给需要精细控制的方块用的）
            var found = Resources.LoadAll<BlockDefinition>("");
            foreach (var def in found)
            {
                if (def == null || !def.IsValid) continue;
                if (_byId.ContainsKey(def.id))
                {
                    Debug.LogWarning($"[BlockMap] 方块 id 重复，已忽略：{def.id} ({def.name})");
                    continue;
                }
                _blocks.Add(def);
                _byId[def.id] = def;
            }

            // 约定命名（id 一律取自"文件名"，不含扩展名）：
            //     <id>_panel.png + <id>_frame.png  → 自动包边（机壳效果）
            //     <id>_simple.png                  → 普通单图，不包边
            // 放在 Resources 下的**任意子目录**都行，推荐每个方块一个文件夹：
            //     Resources/<id>/<id>_panel.png + <id>_frame.png
            // 也兼容旧布局：Resources/BlockTextures/<id>_panel.png
            // ⚠️ Resources API 拿不到子目录路径，所以 id 必须靠文件名约定；文件夹名只为人眼组织。
            var panels = new Dictionary<string, Texture2D>();
            var frames = new Dictionary<string, Texture2D>();
            var simples = new Dictionary<string, Texture2D>();
            foreach (var tex in Resources.LoadAll<Texture2D>(""))
            {
                if (tex == null) continue;
                string n = tex.name;
                if (n.EndsWith("_panel")) panels[n.Substring(0, n.Length - 6)] = tex;
                else if (n.EndsWith("_frame")) frames[n.Substring(0, n.Length - 6)] = tex;
                else if (n.EndsWith("_simple")) simples[n.Substring(0, n.Length - 7)] = tex;
            }

            var textureIds = new HashSet<string>(panels.Keys);
            textureIds.UnionWith(simples.Keys);
            foreach (var tid in textureIds)
            {
                if (_byId.ContainsKey(tid)) continue;              // 已有同名 .asset，优先用 .asset
                panels.TryGetValue(tid, out var panel);
                frames.TryGetValue(tid, out var frame);
                simples.TryGetValue(tid, out var simple);
                if (panel == null && simple == null) continue;

                // 只有面板、没有边框 → 会变成一个"没有贴图"的方块。
                // 直接跳过并明确报错，免得编辑器里出现选得到却画不出来的方块。
                if (panel != null && frame == null && simple == null)
                {
                    Debug.LogWarning($"[BlockMap] 跳过方块「{tid}」：缺边框贴图。\n" +
                        $"  自动包边需要成对同 id：{tid}_panel.png + {tid}_frame.png\n" +
                        $"  只想要单图平铺的话，请改用 {tid}_simple.png");
                    continue;
                }

                var def = ScriptableObject.CreateInstance<BlockDefinition>();
                def.id = tid;
                def.displayName = tid;
                def.panelTexture = panel;
                def.frameTexture = frame;
                def.simpleTexture = simple;

                // ★ 灯：id 含 "Lamp" 的方块自动发光 → 运行时在格子上生成暖色点光源
                //   （实时光只对"受光材质"有效；地形若还是 BlockUnlit 就不受光，这时用 *_Lit 提亮贴图达到同样观感）
                if (tid.IndexOf("Lamp", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    def.emitLight = true;
                    def.lightColor = new Color(1f, 0.86f, 0.6f);
                    def.lightRange = 7f;
                    def.lightIntensity = 1.4f;
                }

                if (IsMarkerBlock(tid))
                {
                    // 起点 / 终点：是「标记」而不是地形
                    def.interactive = true;                        // 烘焙静态几何时跳过；运行时由 BlockMapGameRoot 读格位
                    def.renderMode = BlockRenderMode.Transparent;   // 半透明：编辑时看得见位置，又不会挡住地形
                    def.tint = new Color(1f, 1f, 1f, 0.45f);
                }
                _blocks.Add(def);
                _byId[def.id] = def;
            }

            // ★ 模型索引：Resources/…/BlockModels/<id>.prefab（也支持直接放 .fbx / .obj 等模型文件）
            //   把模型丢进去就会被自动识别成"特殊物件" —— 不用建 .asset、不用手填任何字段。
            //   · 已有同名贴图定义的 → 直接给它补上模型（贴图当占位显示）
            //   · 只有模型没贴图的   → 自动用一块洋红半透明占位标记，编辑器里照样能刷
            Texture2D markerTex = null;
            foreach (var go in Resources.LoadAll<GameObject>("BlockModels"))
            {
                if (go == null || string.IsNullOrEmpty(go.name)) continue;
                string mid = go.name;

                if (_byId.TryGetValue(mid, out var existed))
                {
                    // Dedicated artwork prefabs take precedence over an older imported model with the same ID.
                    if (existed.previewPrefab != null && existed.previewPrefab.GetComponent<GameplayMechanismArt>() != null &&
                        go.GetComponent<GameplayMechanismArt>() == null) continue;
                    existed.previewPrefab = go;
                    existed.interactive = true;
                    continue;
                }

                if (markerTex == null)
                {
                    markerTex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                    var cols = new Color32[64];
                    for (int i = 0; i < cols.Length; i++) cols[i] = new Color32(255, 80, 220, 140);
                    markerTex.SetPixels32(cols);
                    markerTex.filterMode = FilterMode.Point;
                    markerTex.wrapMode = TextureWrapMode.Clamp;
                    markerTex.Apply();
                    markerTex.name = "BlockMap_Marker";
                }

                var md = ScriptableObject.CreateInstance<BlockDefinition>();
                md.id = mid;
                md.displayName = mid;
                md.simpleTexture = markerTex;
                md.previewPrefab = go;
                md.interactive = true;
                _blocks.Add(md);
                _byId[md.id] = md;
            }

            // 玩法配置只描述“这个 id 做什么”。已有美术定义会被补上玩法属性；
            // 没有美术的玩法块会得到一个运行时灰盒定义，策划仍可直接在地编里使用。
            BlockGameplayCatalog.AddDefinitions(_blocks, _byId);

            // These marker variants select how the two split slimes respond to movement input.
            BlockDefinition spawn = Get("Spawn");
            foreach (string markerId in new[] { "Spawn_Synchronous", "Spawn_Asynchronous" })
            {
                if (_byId.ContainsKey(markerId)) continue;
                var marker = ScriptableObject.CreateInstance<BlockDefinition>();
                marker.id = markerId;
                marker.displayName = markerId == "Spawn_Synchronous" ? "同步出生点" : "异步出生点";
                marker.simpleTexture = spawn != null ? spawn.simpleTexture : CreateMarkerTexture();
                if (spawn != null)
                {
                    marker.panelTexture = spawn.panelTexture;
                    marker.frameTexture = spawn.frameTexture;
                    marker.atlasOverride = spawn.atlasOverride;
                }
                marker.interactive = true;
                marker.renderMode = BlockRenderMode.Transparent;
                marker.tint = markerId == "Spawn_Synchronous"
                    ? new Color(0.25f, 0.9f, 0.55f, 0.65f) : new Color(0.2f, 0.75f, 1f, 0.65f);
                _blocks.Add(marker);
                _byId.Add(markerId, marker);
            }

            // 静态地形、交互模型、_Lamp 灯具分别分组，组内按 id 排序。
            _blocks.Sort((a, b) =>
            {
                int groupA = a.IsLamp ? 2 : (a.interactive || a.previewPrefab != null ? 1 : 0);
                int groupB = b.IsLamp ? 2 : (b.interactive || b.previewPrefab != null ? 1 : 0);
                int c = groupA.CompareTo(groupB);
                return c != 0 ? c : string.CompareOrdinal(a.id, b.id);
            });

            if (_blocks.Count == 0)
                Register(CreateFallbackBlock());
        }

        static Texture2D CreateMarkerTexture()
        {
            var texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            var pixels = new Color32[64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 80, 220, 140);
            texture.SetPixels32(pixels);
            texture.filterMode = FilterMode.Point;
            texture.Apply();
            return texture;
        }

        /// <summary>一个方块都没有时，用程序生成一个带自动包边的示例方块，保证工具开箱可用。</summary>
        static BlockDefinition CreateFallbackBlock()
        {
            var def = ScriptableObject.CreateInstance<BlockDefinition>();
            def.id = "demo_block";
            def.displayName = "示例方块（自动生成）";
            def.panelTexture = MakeSolid(new Color32(214, 208, 228, 255));
            def.frameTexture = MakeFrame(16, new Color32(96, 88, 128, 255));
            return def;
        }

        static Texture2D MakeSolid(Color32 c)
        {
            var t = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            var px = new Color32[16 * 16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels32(px); t.Apply();
            t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        static Texture2D MakeFrame(int size, Color32 edge)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            var clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            int band = Mathf.Max(1, size / 8);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool border = x < band || y < band || x >= size - band || y >= size - band;
                    if (border) px[y * size + x] = edge;
                }
            t.SetPixels32(px); t.Apply();
            t.filterMode = FilterMode.Point; t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }
    }
}
