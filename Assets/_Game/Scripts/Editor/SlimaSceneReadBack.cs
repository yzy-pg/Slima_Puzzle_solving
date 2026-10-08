using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockMap.EditorTools
{
    /// <summary>
    /// 以「当前场景」为准，重新读取场景里的物品并更新地图数据。
    ///
    /// 用途：关卡场景**已经烘好**之后，你又手动做了二次编辑（挪动/新增/删除道具、起点终点），
    ///       用这个菜单把场景里的现状**反向写回** BlockWorld，场景数据就与你的编辑一致了。
    ///
    /// 识别方式：运行时/烘焙生成的道具都带 <see cref="BlockMapSpawned"/> 标记，
    ///           名字形如 Spawned_&lt;id&gt;_x_y_z；物体移动过就用它当前的**世界坐标**反推格位。
    /// </summary>
    public static class SlimaSceneReadBack
    {
        [MenuItem("Tools/Slima/地图编辑器/以当前场景为准：重新读取场景里的物品并更新地图数据", false, 422)]
        public static void ReadBackFromScene()
        {
            BlockWorld world = Object.FindObjectOfType<BlockWorld>();
            if (world == null)
            {
                EditorUtility.DisplayDialog("BlockMap",
                    "当前场景里没有 BlockWorld。\n\n这个工具用于**已经烘好的关卡场景**（里面有 BlockMapStatic / BlockWorld）。", "好");
                return;
            }

            world.RestoreIfNeeded();

            float cellSize = 1f;
            var root = world.GetComponent<BlockMapGameRoot>();
            if (root != null && root.cellSize > 0.001f) cellSize = root.cellSize;

            // 1) 扫描场景里的道具：id → 它现在所在的格位
            var seen = new Dictionary<string, HashSet<Vector3Int>>();
            var knownPropIds = new HashSet<string>();
            int moved = 0, added = 0;

            foreach (BlockMapSpawned spawned in Object.FindObjectsOfType<BlockMapSpawned>())
            {
                if (spawned == null) continue;
                string name = spawned.gameObject.name;
                if (!name.StartsWith("Spawned_")) continue;                 // 跳过 Light_*

                Match m = Regex.Match(name.Substring("Spawned_".Length),
                                      @"^(?<id>.+)_(?<x>-?\d+)_(?<y>-?\d+)_(?<z>-?\d+)$");
                if (!m.Success) continue;

                string id = m.Groups["id"].Value;
                int index = BlockLibrary.IndexOf(id);
                if (index < 0) continue;

                var original = new Vector3Int(int.Parse(m.Groups["x"].Value),
                                              int.Parse(m.Groups["y"].Value),
                                              int.Parse(m.Groups["z"].Value));

                Vector3 local = world.transform.InverseTransformPoint(spawned.transform.position) / cellSize;
                var now = new Vector3Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.y), Mathf.RoundToInt(local.z));

                if (!seen.TryGetValue(id, out HashSet<Vector3Int> set))
                {
                    set = new HashSet<Vector3Int>();
                    seen[id] = set;
                    knownPropIds.Add(id);
                }
                set.Add(now);

                if (now != original)
                {
                    world.Remove(original);          // 挪走了：清掉旧格位
                    moved++;
                }
                if (world.GetIndex(now) != index)
                {
                    world.Set(now, index);           // 当前位置没有它就补上
                    added++;
                }
            }

            // 2) 场景里已不存在的道具 → 从数据里删掉
            var toRemove = new List<Vector3Int>();
            foreach (KeyValuePair<Vector3Int, int> kv in world.Blocks)
            {
                BlockDefinition def = BlockLibrary.Get(kv.Value);
                if (def == null) continue;

                bool isProp = def.previewPrefab != null || def.interactive;
                if (!isProp) continue;                                        // 只处理道具类（不动地形）
                if (def.id == root?.spawnBlockId || def.id == root?.exitBlockId) { /* 起点终点也按道具处理 */ }

                bool stillThere = seen.TryGetValue(def.id, out HashSet<Vector3Int> set) && set.Contains(kv.Key);
                if (!stillThere) toRemove.Add(kv.Key);
            }
            foreach (Vector3Int cell in toRemove) world.Remove(cell);

            EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();

            string report =
                $"已按当前场景更新地图数据：\n\n" +
                $"· 扫描到的道具种类：{seen.Count} 种\n" +
                $"· 被移动（旧格位已清除）：{moved} 个\n" +
                $"· 新位置补写数据：{added} 个\n" +
                $"· 场景里已不存在、从数据删除：{toRemove.Count} 个\n" +
                $"· 更新后方块总数：{world.Count}\n\n" +
                "提示：这只同步**道具/标记**（不影响地形几何）。地形改动仍需回地图编辑器重刷后重新 ② 烘场景。";

            Debug.Log("[BlockMap] " + report.Replace("\n", " "));
            EditorUtility.DisplayDialog("BlockMap", report, "好");
        }
    }
}
