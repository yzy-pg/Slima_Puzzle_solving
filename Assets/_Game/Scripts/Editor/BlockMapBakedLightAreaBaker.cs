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

            // 触发碰撞体：让它在 Scene 视图里可以**点击选中**（运行时会被自动关掉，不参与物理）
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.5f, 0f);
            box.size = new Vector3(area.size.x, 1f, area.size.y);

            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Create BakedLightArea");
            EditorSceneManager.MarkSceneDirty(go.scene);

            Debug.Log("[BlockMap] 已新建发光体：黄色线框 = 矩形范围，彩色格子 = 会被点亮的格子。\n" +
                      "调 Size / Brightness / Dither Block，然后跑「把发光体范围烘焙到方块」。");
            EditorUtility.DisplayDialog("BlockMap",
                "已新建发光体 ✓\n\n" +
                "· 黄色线框 = 矩形范围（Size 控制）\n" +
                "· 彩色格子 = 按渐变会被点亮的格子（Dither Block 控制像素格大小）\n" +
                "· 两个黄色立柱 = 高度区间（Height Range）\n\n" +
                "调好后跑「把发光体范围烘焙到方块（像素抖动渐变）」。\n" +
                "（它带了触发碰撞体以便在 Scene 里点击选中；运行时会自动关闭，不影响物理。）", "好");
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

            string msg =
                $"发光体烘焙完成：\n\n" +
                $"· 被点亮的格子：{lit} 个（涉及 {touched.Count} 处）\n" +
                $"· 跳过：还没有 _Lit 提亮版的方块 {skippedNoVariant} 次\n" +
                $"· 已是提亮版（无需处理）：{already} 次\n\n" +
                (_skippedHint(skippedNoVariant) ? "提示：先跑「给所有方块生成提亮版（灯光亮斑，_Lit）」再烘焙 ✓\n\n" : "") +
                "渐变是用「Bayer 抖动」变成像素格疏密的：越亮的地方点亮的格子越密 ✓";

            Debug.Log("[BlockMap] " + msg.Replace("\n", " "));
            EditorUtility.DisplayDialog("BlockMap", msg, "好");
        }

        static bool _skippedHint(int n) => n > 0;
    }
}
