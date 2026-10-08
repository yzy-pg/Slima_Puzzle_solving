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
            go.AddComponent<BlockMapBakedLightArea>();

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
                Vector3 c = world.transform.InverseTransformPoint(area.transform.position);
                Vector2 centerXZ = new Vector2(c.x, c.z);
                int halfW = Mathf.CeilToInt(area.size.x * 0.5f);
                int halfD = Mathf.CeilToInt(area.size.y * 0.5f);

                int cx = Mathf.RoundToInt(c.x);
                int cz = Mathf.RoundToInt(c.z);

                for (int dx = -halfW; dx <= halfW; dx++)
                {
                    for (int dz = -halfD; dz <= halfD; dz++)
                    {
                        int x = cx + dx, z = cz + dz;
                        var offset = new Vector2(x - centerXZ.x, z - centerXZ.y);
                        float level = area.Evaluate(offset, x, z);
                        if (!area.ShouldLight(x, z, level)) continue;

                        for (int y = area.heightRange.x; y <= area.heightRange.y; y++)
                        {
                            var cell = new Vector3Int(x, y, z);
                            if (!world.InBounds(cell)) continue;

                            int index = world.GetIndex(cell);
                            if (index < 0) continue;

                            BlockDefinition def = BlockLibrary.Get(index);
                            if (def == null) continue;
                            if (def.id.EndsWith(area.litSuffix)) { already++; continue; }   // 已是提亮版

                            int litIndex = BlockLibrary.IndexOf(def.id + area.litSuffix);
                            if (litIndex < 0) { skippedNoVariant++; continue; }              // 还没生成 _Lit

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

        static bool _skippedHint(int n) => n > 0;
    }
}
