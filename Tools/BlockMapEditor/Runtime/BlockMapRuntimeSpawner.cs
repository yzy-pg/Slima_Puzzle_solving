using System.Collections.Generic;
using BlockMap.Gameplay;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 把"用方块标记出来的位置"变成真实的 Prefab 与点光源。
    ///
    /// · 带 <c>previewPrefab</c> 的方块 → 在格位实例化 Prefab：
    ///   编辑器里就能看到门 / 箱子 / 家具 / 树的真实外观，正式场景同样生效。
    ///   ⚠️ 这类方块在菜单 ② 烘焙静态几何时会被自动跳过，不会和立方体重复。
    /// · 勾了 <c>emitLight</c> 的方块 → 在格子上生成点光源（受 maxLights 限制，防止灯光炸性能）。
    ///
    /// 跟 BlockWorld 放在同一个物体上即可。编辑时方块数量变化会自动重建（带节流）。
    /// </summary>
    [RequireComponent(typeof(BlockWorld))]
    public class BlockMapRuntimeSpawner : MonoBehaviour
    {
        [Tooltip("生成物挂到这个父节点下（留空 = 自己）")]
        public Transform container;

        [Tooltip("点光源数量上限，超出的发光方块不再生成灯")]
        public int maxLights = 32;

        [Tooltip("点光源相对格子中心的高度偏移")]
        public float lightHeightOffset = 0.5f;

        [Tooltip("重建节流：两次重建之间至少间隔的秒数（避免连续刷方块时反复重建）")]
        public float rebuildInterval = 0.2f;

        readonly List<GameObject> _spawned = new List<GameObject>();
        BlockWorld _world;
        int _lastVersion = -1;
        float _nextRebuild;

        void Awake()
        {
            _world = GetComponent<BlockWorld>();
        }

        void OnEnable()
        {
            if (_world == null) _world = GetComponent<BlockWorld>();
            Rebuild();
        }

        void LateUpdate()
        {
            if (_world == null) return;
            if (_world.Version == _lastVersion) return;             // 数量、类型、朝向都没变就不用管
            if (Time.realtimeSinceStartup < _nextRebuild) return;    // 节流
            Rebuild();
        }

        /// <summary>清掉旧的生成物，按当前世界数据重新生成一遍。</summary>
        public void Rebuild()
        {
            if (_world == null) _world = GetComponent<BlockWorld>();
            _world.RestoreIfNeeded();      // 场景里存的是可序列化镜像，先恢复成运行时世界
            Clear();

            Transform parent = container != null ? container : transform;
            int lights = 0;
            bool gameplayOwnsViews = GetComponent<BlockMapGameRoot>() != null ||
                                     GetComponent<BlockGameplayController>() != null;

            foreach (var kv in _world.Blocks)
            {
                var def = BlockLibrary.Get(kv.Value);
                if (def == null) continue;

                GameplayBlockProfile profile = BlockGameplayCatalog.Get(def.id);
                if (profile != null)
                {
                    // 可玩关卡由玩法控制器生成；纯地编场景也要生成同一套预览，
                    // 否则光源的朝向箭头只在正式关卡中出现，策划编辑时仍然看不见。
                    if (!gameplayOwnsViews)
                    {
                        GameplayBlockView view = GameplayPrototypeFactory.Create(
                            def,
                            profile,
                            kv.Key,
                            _world.GetDir(kv.Key),
                            parent);
                        GameObject preview = view.gameObject;
                        if (preview.GetComponent<BlockMapSpawned>() == null)
                            preview.AddComponent<BlockMapSpawned>();
                        _spawned.Add(preview);
                    }
                    continue;
                }

                Vector3 center = (Vector3)kv.Key + Vector3.one * 0.5f;

                // ① 特殊模型：直接实例化 Prefab（门 / 箱子 / 家具 / 树…）
                if (def.previewPrefab != null)
                {
                    int dir = _world.GetDir(kv.Key);
                    Quaternion rot = Quaternion.Euler(0f, dir * 90f, 0f) * def.previewPrefab.transform.rotation;
                    Vector3 pos = center;
                    if (def.mountOnWall) pos -= (rot * Vector3.forward) * 0.5f;   // 贴到"朝向前方"的背后那面墙上
                    var go = Instantiate(def.previewPrefab, pos, rot, parent);
                    go.name = $"Spawned_{def.id}_{kv.Key.x}_{kv.Key.y}_{kv.Key.z}";
                    go.AddComponent<BlockMapSpawned>();
                    _spawned.Add(go);
                }

                // ② 发光方块：在格子上放点光源
                if (def.emitLight && lights < maxLights)
                {
                    var lgo = new GameObject($"Light_{def.id}_{kv.Key.x}_{kv.Key.y}_{kv.Key.z}");
                    lgo.transform.SetParent(parent, false);
                    lgo.transform.position = center + Vector3.up * lightHeightOffset;

                    var l = lgo.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = def.lightColor;
                    l.range = def.lightRange;
                    l.intensity = def.lightIntensity;

                    lgo.AddComponent<BlockMapSpawned>();
                    _spawned.Add(lgo);
                    lights++;
                }
            }

            _lastVersion = _world.Version;
            _nextRebuild = Time.realtimeSinceStartup + Mathf.Max(0f, rebuildInterval);
        }

        void Clear()
        {
            for (int i = 0; i < _spawned.Count; i++) DestroySafe(_spawned[i]);
            _spawned.Clear();

            // 兼容"上一次运行 / 已保存进场景"的生成物：靠标记组件找出来清掉
            var leftovers = FindObjectsOfType<BlockMapSpawned>();
            for (int i = 0; i < leftovers.Length; i++) DestroySafe(leftovers[i].gameObject);
        }

        static void DestroySafe(GameObject go)
        {
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }

    /// <summary>标记：这个物体是 BlockMapRuntimeSpawner 生成的（重建时会被清掉）</summary>
    public class BlockMapSpawned : MonoBehaviour { }
}
