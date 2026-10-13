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

        [Tooltip("灯光离方块中心的高度（世界单位）。\n" +
                 "⚠ 必须让灯高于邻近地板的上表面，否则对地板顶面来说光来自下方（NdotL<0）→ 地板照不到，\n" +
                 "  只有侧面亮，看起来像「只照一个方向」。地板中心在 y=+0.5、上表面即 +1.0，所以默认给 1.5。")]
        public float lightHeightOffset = 1.5f;

        [Tooltip("true = 聚光（沿方块「正面」照射，方向由地图编辑器里的方块朝向决定，摆位麻烦）；" +
                 "false = 点光（全向，推荐：配合材质「额外光模式 = 按距离」就是以灯为中心的圆形光斑，不用瞄准）")]
        public bool lampAsSpot = false;

        [Tooltip("聚光的照射倾角：0 = 沿正面水平射；45 = 斜下；90 = 正上方（「正面朝上」）")]
        [Range(0f, 90f)] public float lightTiltDegrees = 60f;

        [Tooltip("聚光锥角（度）。越大越像柔光池，越小越像手电")]
        [Range(10f, 179f)] public float spotAngle = 90f;

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
                    // Gameplay owns the view, but emissive gameplay blocks still need lights.
                    if (!def.emitLight) continue;
                }

                Vector3 center = (Vector3)kv.Key + Vector3.one * 0.5f;

                // Independent of maxLights: lamps and emissive gameplay blocks share the halo.
                // Keep edit-time baked scenes free of transient billboard objects.
                if (Application.isPlaying && (def.IsLamp || def.emitLight) && def.lampHalo)
                {
                    var halo = new GameObject($"Halo_{def.id}_{kv.Key.x}_{kv.Key.y}_{kv.Key.z}");
                    halo.transform.SetParent(parent, false);
                    halo.transform.position = center + Quaternion.Euler(0, _world.GetDir(kv.Key) * 90f, 0) * def.lampHaloLocalOffset;
                    halo.AddComponent<BlockMapSpawned>();
                    halo.AddComponent<BlockMapLampHalo>().Configure(def);
                    _spawned.Add(halo);
                }

                // ① 特殊模型：直接实例化 Prefab（门 / 箱子 / 家具 / 树…）
                if (profile == null && def.previewPrefab != null)
                {
                    int dir = _world.GetDir(kv.Key);
                    Quaternion rot = Quaternion.Euler(0f, dir * 90f, 0f) * def.previewPrefab.transform.rotation;
                    Vector3 pos = center;
                    if (def.mountOnWall) pos -= (rot * Vector3.forward) * 0.5f;   // 贴到"朝向前方"的背后那面墙上
                    var go = Instantiate(def.previewPrefab, pos, rot, parent);
                    go.name = $"Spawned_{def.id}_{kv.Key.x}_{kv.Key.y}_{kv.Key.z}";
                    go.AddComponent<BlockMapSpawned>();
                    var gameRoot = GetComponent<BlockMapGameRoot>();
                    if (gameRoot != null)
                        foreach (Renderer item in go.GetComponentsInChildren<Renderer>(true))
                            gameRoot.ApplyAtmosphereTo(item);
                    _spawned.Add(go);
                }

                // ② 发光方块：在格子上放灯
                if (def.emitLight && lights < maxLights)
                {
                    string lightName = $"Light_{def.id}_{kv.Key.x}_{kv.Key.y}_{kv.Key.z}";

                    // ★ 场景里已经有这盏灯（烘焙时存进场景的、或你在编辑器里挪过位置/改过颜色的）→ 复用它。
                    //   不要重建：重建会丢掉你的编辑。也不加进 _spawned，避免被 Clear() 清掉。
                    GameObject existing = GameObject.Find(lightName);
                    if (existing != null && existing.GetComponent<Light>() != null)
                    {
                        lights++;
                        continue;
                    }

                    var lgo = new GameObject(lightName);
                    lgo.transform.SetParent(parent, false);
                    lgo.transform.position = def.GetLightPosition(center, _world.GetDir(kv.Key), lightHeightOffset);

                    var l = lgo.AddComponent<Light>();
                    l.color = def.lightColor;
                    l.range = def.lightRange;
                    l.intensity = def.lightIntensity;

                    // ★ 照射方向跟随方块"正面"（dir 来自地图编辑器，和门/道具共用同一套朝向）。
                    //   要改照射方向 → 在地图编辑器里旋转这个 Lamp 方块即可；
                    //   想让它朝正上方 → 把 lightTiltDegrees 设成 90。
                    if (lampAsSpot)
                    {
                        l.type = LightType.Spot;
                        l.spotAngle = spotAngle;
                        l.innerSpotAngle = spotAngle * 0.5f;

                        float yaw = _world.GetDir(kv.Key) * 90f;
                        lgo.transform.rotation = Quaternion.Euler(0f, yaw, 0f)
                                               * Quaternion.Euler(-lightTiltDegrees, 0f, 0f);
                    }
                    else
                    {
                        l.type = LightType.Point;
                        lgo.transform.rotation = Quaternion.identity;
                    }

                    lgo.AddComponent<BlockMapSpawned>();
                    _spawned.Add(lgo);
                    lights++;
                }
            }

            _lastVersion = _world.Version;
            if (!gameplayOwnsViews)
                BlockLinkVisuals.Apply(_world, parent.GetComponentsInChildren<GameplayBlockView>(true));
            _nextRebuild = Time.realtimeSinceStartup + Mathf.Max(0f, rebuildInterval);
        }

        void Clear()
        {
            for (int i = 0; i < _spawned.Count; i++) DestroySafe(_spawned[i]);
            _spawned.Clear();

            // 兼容"上一次运行 / 已保存进场景"的生成物：靠标记组件找出来清掉
            // ★ 跳过光源：场景里的灯（烘焙生成的、你在编辑器里挪过位置/改过颜色的）要保留，
            //   否则每次 Play 都会把你的灯光编辑丢掉。
            var leftovers = FindObjectsOfType<BlockMapSpawned>();
            for (int i = 0; i < leftovers.Length; i++)
            {
                GameObject go = leftovers[i].gameObject;
                if (go.GetComponent<Light>() != null) continue;   // 灯：保留
                if (go.GetComponent<BlockMapLampHalo>() != null &&
                    !go.transform.IsChildOf(container != null ? container : transform)) continue;
                DestroySafe(go);
            }
        }

        static void DestroySafe(GameObject go)
        {
            if (go == null) return;
            // Hide retiring runtime lights before name lookup can reuse a pending destruction.
            if (go.GetComponent<BlockMapLampHalo>() != null || go.GetComponent<Light>() != null) go.SetActive(false);
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }

    /// <summary>标记：这个物体是 BlockMapRuntimeSpawner 生成的（重建时会被清掉）</summary>
    public class BlockMapSpawned : MonoBehaviour { }
}
