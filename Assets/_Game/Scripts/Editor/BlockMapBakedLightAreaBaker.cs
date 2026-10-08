using System.Collections.Generic;
using BlockMap.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockMap.EditorTools
{
    /// <summary>
    /// 烘焙发光体的 Menu 工具（都在 Tools 列表里，不进 BlockMap 方块库）：
    ///   ① 新建发光体（矩形渐变）
    ///   ② 把发光体范围烘焙到方块（像素抖动渐变）—— 把范围内的方块换成它的 _Lit 提亮版
    /// </summary>
    public static class BlockMapBakedLightAreaBaker
    {
        [MenuItem("Tools/Slima/地图编辑器/新建发光体（矩形渐变，烘焙用）", false, 450)]
        public static void CreateArea()
        {
            var go = new GameObject("BakedLightArea");
            var area = go.AddComponent<BlockMapBakedLightArea>();

            // 编辑器预览灯：立刻能看到范围/强度，方便摆位置。
            // Play 时由组件自动关闭，不参与任何运行时灯光计算。
            area.SyncPreviewLight();
            if (go.transform.localScale == Vector3.one)
                go.transform.localScale = new Vector3(area.baseSizeHint.x, area.baseSizeHint.y, 8f);

            // 不加碰撞体：那样 Unity 会在场景里再画一个绿框，看起来像"两个矩形"
            // 选中它请用下面的「选中场景里所有发光体」菜单，或在 Hierarchy 里点。
            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Create BakedLightArea");
            EditorSceneManager.MarkSceneDirty(go.scene);

            Debug.Log("[BlockMap] 已新建发光体：场景里黄色线框 = 矩形范围（Size）。\n" +
                      "调 Size / Brightness / Dither Block，然后跑「把发光体范围烘焙到方块」。");
            EditorUtility.DisplayDialog("BlockMap",
                "已新建发光体 ✓\n\n" +
                "· 黄色线框 = 矩形范围（Size 控制）\n" +
                "· 渐变（Brightness）= 范围内的提亮分布，用像素格疏密表达\n" +
                "   （左端 = 边缘，右端 = 中心）\n\n" +
                "调好后跑「把发光体范围烘焙到方块（像素抖动渐变）」。", "好");
        }

        [MenuItem("Tools/Slima/地图编辑器/选中场景里所有发光体", false, 452)]
        public static void SelectAllAreas()
        {
            var areas = Object.FindObjectsOfType<BlockMapBakedLightArea>();
            var objs = new List<Object>();
            foreach (BlockMapBakedLightArea a in areas) objs.Add(a.gameObject);

            Selection.objects = objs.ToArray();
            if (objs.Count > 0) SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"[BlockMap] 已选中 {objs.Count} 个发光体");
        }

        [MenuItem("Tools/Slima/地图编辑器/把发光体范围烘焙到方块（像素抖动渐变）", false, 451)]
        public static void BakeAllAreas()
        {
            BlockWorld world = Object.FindObjectOfType<BlockWorld>();
            if (world == null)
            {
                EditorUtility.DisplayDialog("BlockMap", "当前场景里没有 BlockWorld（请打开已经烘好的关卡场景）。", "好");
                return;
            }

            var areas = Object.FindObjectsOfType<BlockMapBakedLightArea>();
            if (areas == null || areas.Length == 0)
            {
                EditorUtility.DisplayDialog("BlockMap",
                    "场景里没有发光体。\n\n先用菜单「新建发光体（矩形渐变，烘焙用）」摆一个。", "好");
                return;
            }

            world.RestoreIfNeeded();

            int lit = 0, skippedNoVariant = 0, already = 0;
            var touched = new HashSet<Vector3Int>();

            foreach (BlockMapBakedLightArea area in areas)
            {
                Matrix4x4 m = area.transform.localToWorldMatrix;
                Vector2 rect = area.RectSize;
                int halfX = Mathf.Max(0, Mathf.RoundToInt(rect.x * 0.5f));
                int halfY = Mathf.Max(0, Mathf.RoundToInt(rect.y * 0.5f));
                int beamLen = Mathf.Max(1, Mathf.RoundToInt(area.BeamLength));

                // 面光：从发光面（本地 X/Y 网格）沿本地 +Z 直线射出，逐格提亮
                for (int lx = -halfX; lx <= halfX; lx++)
                {
                    for (int ly = -halfY; ly <= halfY; ly++)
                    {
                        // 用同一套风格化算法算强度（这里只需要"亮不亮"，颜色由方块贴图决定）
                        area.CellLight(Vector3.zero, new Vector2(lx, ly), 0f, out float level);
                        if (level <= 0.001f) continue;

                        for (int lz = 0; lz <= beamLen; lz++)
                        {
                            // 抖动格坐标用"面内 + 光程"组合，保证像素格连续
                            int ditherX = lx, ditherZ = ly * 131 + lz;
                            if (!area.ShouldLight(ditherX, ditherZ, level)) continue;

                            Vector3 worldPos = m.MultiplyPoint3x4(new Vector3(lx + 0.5f, ly + 0.5f, lz + 0.5f));
                            var cell = new Vector3Int(Mathf.RoundToInt(worldPos.x),
                                                      Mathf.RoundToInt(worldPos.y),
                                                      Mathf.RoundToInt(worldPos.z));
                            if (!world.InBounds(cell)) continue;

                            int index = world.GetIndex(cell);
                            if (index < 0) continue;

                            BlockDefinition def = BlockLibrary.Get(index);
                            if (def == null) continue;
                            if (def.id.EndsWith(area.litSuffix)) { already++; continue; }

                            int litIndex = BlockLibrary.IndexOf(def.id + area.litSuffix);
                            if (litIndex < 0) { skippedNoVariant++; continue; }

                            world.Set(cell, litIndex);
                            touched.Add(cell);
                            lit++;
                        }
                    }
                }
            }

            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();

            // ★ 烘焙完成后：发光体不再与玩家/物理发生任何交互
            //   （那个触发碰撞体只是用来在 Scene 里点选它；烤完就关掉，需要重新编辑时再手动打开）
            var disabled = new List<string>();
            foreach (BlockMapBakedLightArea area in areas)
            {
                var box = area.GetComponent<Collider>();
                if (box != null && box.enabled)
                {
                    box.enabled = false;
                    EditorUtility.SetDirty(box);
                    disabled.Add(area.name);
                }
            }
            if (disabled.Count > 0) EditorSceneManager.SaveOpenScenes();

            string msg =
                $"发光体烘焙完成：\n\n" +
                $"· 被点亮的格子：{lit} 个（涉及 {touched.Count} 处）\n" +
                $"· 跳过：还没有 _Lit 提亮版的方块 {skippedNoVariant} 次\n" +
                $"· 已是提亮版（无需处理）：{already} 次\n" +
                $"· 已关闭选择框（不再与玩家/物理交互）：{disabled.Count} 个发光体\n\n" +
                (_skippedHint(skippedNoVariant) ? "提示：先跑「给所有方块生成提亮版（灯光亮斑，_Lit）」再烘焙 ✓\n\n" : "") +
                "渐变用 Bayer 抖动变成像素格疏密：越亮的地方点亮的格子越密 ✓\n" +
                "（需要再编辑发光体时，手动把它身上的 BoxCollider 勾回 Enabled 即可）";

            Debug.Log("[BlockMap] " + msg.Replace("\n", " "));
            EditorUtility.DisplayDialog("BlockMap", msg, "好");
        }

        [MenuItem("Tools/Slima/地图编辑器/把发光体烤进地形网格（像素噪波+五层阶梯+高饱和）", false, 448)]
        public static void BakeIntoMeshes()
        {
            BlockWorld world = Object.FindObjectOfType<BlockWorld>();
            if (world == null)
            {
                EditorUtility.DisplayDialog("BlockMap", "当前场景里没有 BlockWorld（请打开已经烘好的关卡场景）。", "好");
                return;
            }

            var areas = Object.FindObjectsOfType<BlockMapBakedLightArea>();
            if (areas.Length == 0)
            {
                EditorUtility.DisplayDialog("BlockMap", "场景里没有发光体。先用「新建发光体（矩形渐变，烘焙用）」摆一个。", "好");
                return;
            }

            int painted = 0, unreadable = 0, meshes = 0;
            var filters = Object.FindObjectsOfType<MeshFilter>();

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (MeshFilter mf in filters)
                {
                    if (mf == null || mf.GetComponent<MeshRenderer>() == null) continue;
                    Mesh mesh = mf.sharedMesh;
                    if (mesh == null || mesh.vertexCount == 0) continue;
                    if (!mesh.isReadable) { unreadable++; continue; }

                    Vector3[] verts = mesh.vertices;
                    Color[] cols = mesh.colors;
                    if (cols == null || cols.Length != verts.Length)
                    {
                        cols = new Color[verts.Length];
                        for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
                    }

                    bool changed = false;

                    for (int v = 0; v < verts.Length; v++)
                    {
                        Vector3 wp = mf.transform.TransformPoint(verts[v]);
                        Color add = Color.black;

                        foreach (BlockMapBakedLightArea area in areas)
                        {
                            Vector3 lp = area.transform.InverseTransformPoint(wp);
                            Vector2 face = new Vector2(lp.x, lp.y);
                            float len = Mathf.Max(0.01f, area.BeamLength);
                            if (lp.z < 0f || lp.z > len) continue;

                            float beamT = Mathf.Clamp01(lp.z / len);
                            Color c = area.CellLight(wp, face, beamT, out float level);
                            if (level <= 0.001f) continue;

                            add += new Color(c.r * level, c.g * level, c.b * level, 0f);
                        }

                        if (add.maxColorComponent <= 0.0001f) continue;

                        Color src = cols[v];
                        cols[v] = new Color(
                            Mathf.Clamp01(src.r + add.r * (1f - src.r)),
                            Mathf.Clamp01(src.g + add.g * (1f - src.g)),
                            Mathf.Clamp01(src.b + add.b * (1f - src.b)),
                            src.a);
                        changed = true;
                        painted++;
                    }

                    if (changed)
                    {
                        mesh.colors = cols;
                        EditorUtility.SetDirty(mesh);
                        meshes++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();

            string msg =
                $"已把发光体烤进地形网格：\n\n" +
                $"· 受影响网格：{meshes} 个\n" +
                $"· 被提亮的顶点：{painted} 个\n" +
                $"· 无法读取的网格：{unreadable} 个" +
                (unreadable > 0 ? "（需要在这些网格资产上勾 Read/Write，或重新 ② 烘一次场景）" : "") + "\n\n" +
                "风格化参数（像素噪波 / 五层阶梯 / 高饱和过渡 / 散射）都在发光体组件上，改完再烤一次即可 ✓\n" +
                "运行时不参与任何实时光照 ✓";

            Debug.Log("[BlockMap] " + msg.Replace("\n", " "));
            EditorUtility.DisplayDialog("BlockMap", msg, "好");
        }

        static bool _skippedHint(int n) => n > 0;
    }
}
