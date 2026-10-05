#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 把当前场景（菜单 ② 烘出来的那个）变成可玩关卡：在 BlockWorld 所在物体上挂 BlockMapGameRoot。
    ///
    /// 这一步只加"玩法"（角色 / 相机 / 终点判定），不动地形 —— 地形的碰撞来自烘焙，
    /// 所以这里会顺便检查一遍场景里到底有没有碰撞体、有没有起点/终点标记。
    /// </summary>
    public static class BlockMapPlayableScene
    {
        [MenuItem("Tools/Slima/地图编辑器/③ 把当前场景变成可玩关卡", false, 300)]
        public static void MakePlayable()
        {
            BlockWorld world = Object.FindObjectOfType<BlockWorld>();
            if (world == null)
            {
                EditorUtility.DisplayDialog("BlockMap",
                    "当前场景里没有 BlockWorld。\n\n请先用菜单 ② 把地图烘成场景，再来加游戏根。", "好");
                return;
            }

            BlockMapGameRoot root = world.GetComponent<BlockMapGameRoot>();
            if (root == null) root = world.gameObject.AddComponent<BlockMapGameRoot>();

            world.RestoreIfNeeded();        // 编辑器里也要能读到场里存的方块数据

            int spawnCount = CountMarkers(world, root.spawnBlockId);
            int exitCount = CountMarkers(world, root.exitBlockId);
            int colliders = Object.FindObjectsOfType<MeshCollider>().Length + Object.FindObjectsOfType<BoxCollider>().Length;

            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            EditorSceneManager.SaveScene(world.gameObject.scene);

            string report = $"游戏根已挂到「{world.gameObject.name}」上，按 Play 就能玩。\n\n" +
                            $"场景里的方块数据：{world.Count} 个\n" +
                            $"起点标记（{root.spawnBlockId}）：{spawnCount} 个\n" +
                            $"终点标记（{root.exitBlockId}）：{exitCount} 个\n" +
                            $"场景里的碰撞体：{colliders} 个\n";

            if (world.Count == 0)
            {
                report += "\n⚠ 这个场景里没有方块数据 —— 多半是用旧版本烘的场景（旧版本没把数据存进场景）。\n" +
                          "请重新跑一次菜单 ② 烘场景，再跑一次本菜单。";
            }

            if (spawnCount == 0 || exitCount == 0)
            {
                report += "\n⚠ 还缺标记：回地图编辑器里刷 Spawn / Exit（先跑菜单 ⑥ 生成这两种标记方块），" +
                          "然后重新烘场景。\n" +
                          "标记请刷在「地面之上那一层」，别把地板方块顶掉。";
            }

            if (colliders == 0)
            {
                report += "\n⚠ 场景里没有任何碰撞体：地形是用旧版本烘的，重新跑一次菜单 ② " +
                          "（现在会自动生成碰撞体），否则角色会直接掉下去。";
            }

            Debug.Log("[BlockMap] 可玩关卡已就绪：\n" + report);
            EditorUtility.DisplayDialog("BlockMap", report, "好");
        }

        static int CountMarkers(BlockWorld world, string id)
        {
            if (world == null || string.IsNullOrEmpty(id)) return 0;
            int count = 0;
            foreach (var kv in world.Blocks)
            {
                BlockDefinition def = BlockLibrary.Get(kv.Value);
                if (def != null && def.id == id) count++;
            }
            return count;
        }
    }
}
#endif
