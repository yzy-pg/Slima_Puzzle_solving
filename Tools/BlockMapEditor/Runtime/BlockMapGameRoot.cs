using System.Collections.Generic;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 把「地图编辑器烘出来的场景」变成可玩关卡的游戏根。
    ///
    /// 它只做四件事：
    ///   ① 读同一物体上的 BlockWorld，找出你在地图里刷的「起点 / 终点」标记方块；
    ///   ② 在起点实例化角色（朝向取该标记方块的朝向，也就是编辑器里按 R 转出来的方向）；
    ///   ③ 架好固定角度的 3/4 正交相机（跟不跟角色可配）；
    ///   ④ 角色走到终点格就算通关，按 R 重来。
    ///
    /// 地形不加任何东西 —— 碰撞完全来自菜单 ② 烘出来的几何体，所以角色是真在模型上跑。
    /// </summary>
    [RequireComponent(typeof(BlockWorld))]
    public class BlockMapGameRoot : MonoBehaviour
    {
        [Header("地图里的标记方块 id（在 3D 编辑器里刷这两种方块）")]
        public string spawnBlockId = "Spawn";
        public string exitBlockId = "Exit";

        [Header("角色")]
        [Tooltip("留空 = 用默认角色（Resources/Player/BlockMapPlayer）；再找不到就程序生成一个灰盒")]
        public GameObject playerPrefab;

        [Tooltip("一格的世界尺寸，和烘焙时的 blockSize 一致")]
        public float cellSize = 1f;

        [Header("相机")]
        public bool cameraFollowsPlayer = true;
        public float cameraSize = 8f;

        [Tooltip("相机方位角（度，取 0 / 90 / 180 / 270）。用来对齐你在 3D 编辑器里看地图的方向：\n" +
                 "0 = 相机在 -Z 侧（编辑器默认，屏幕右 = +X、屏幕上 = +Z）\n" +
                 "90 = 相机在 -X 侧　180 = 相机在 +Z 侧　270 = 相机在 +X 侧\n" +
                 "位移方向会跟着这个角度转，所以 W 永远朝屏幕上方走。")]
        public float cameraYaw = 0f;

        public Vector3 cameraOffset = new Vector3(0f, 18f, -16f);

        BlockWorld _world;
        BlockMapGridWalker _walker;
        Camera _camera;
        Vector3Int _spawnCell;
        Vector3Int _spawnStandCell;
        Vector3Int _exitCell;
        int _spawnFacing;
        bool _hasSpawn;
        bool _hasExit;
        bool _won;
        string _hint;
        readonly List<Vector3Int> _spawnCells = new List<Vector3Int>();
        readonly List<Vector3Int> _exitCells = new List<Vector3Int>();
        int _spawnTotal;
        int _exitTotal;

        void Start()
        {
            _world = GetComponent<BlockWorld>();
            _world.RestoreIfNeeded();      // 场景里存的是可序列化镜像，先恢复成运行时世界

            _hasSpawn = TryFindMarker(spawnBlockId, out _spawnCell);
            _hasExit = TryFindMarker(exitBlockId, out _exitCell);
            _spawnFacing = _hasSpawn ? _world.GetDir(_spawnCell) : 0;
            _spawnStandCell = _hasSpawn
                ? new Vector3Int(_spawnCell.x, ResolveStandingY(_spawnCell), _spawnCell.z)
                : _spawnCell;

            // 画错地方 / 笔刷太大时会留下好几个标记 —— 逐个报坐标，方便用「擦除」清掉
            _spawnTotal = CollectMarkers(spawnBlockId, _spawnCells);
            _exitTotal = CollectMarkers(exitBlockId, _exitCells);
            if (_spawnTotal > 1)
            {
                Debug.LogWarning($"[BlockMap] 地图里有 {_spawnTotal} 个起点标记，只会用最低最靠前的 {_spawnCell}；" +
                                 $"其余在：{FormatCells(_spawnCells)}", this);
            }
            if (_exitTotal > 1)
            {
                Debug.LogWarning($"[BlockMap] 地图里有 {_exitTotal} 个终点标记，只会用最低最靠前的 {_exitCell}；" +
                                 $"其余在：{FormatCells(_exitCells)}", this);
            }

            if (!_hasSpawn) Debug.LogError($"[BlockMap] 地图里没有「{spawnBlockId}」标记方块，请在 3D 编辑器里刷一个起点", this);
            if (!_hasExit) Debug.LogError($"[BlockMap] 地图里没有「{exitBlockId}」标记方块，请在 3D 编辑器里刷一个终点", this);
            if (_hasSpawn && _hasExit && _spawnCell == _exitCell)
            {
                Debug.LogWarning("[BlockMap] 起点和终点是同一格", this);
            }

            if (_hasSpawn) SpawnPlayer();
            ConfigureCamera();
            CreateMarkerVisuals();
        }

        /// <summary>
        /// 起点 / 终点在游戏里的可见标记。
        /// 该方块若已有 previewPrefab（放了真模型，或跑了菜单 ⑥），就交给 BlockMapRuntimeSpawner，这里不重复画；
        /// 否则用基本体现场拼一个，不依赖任何美术资产 —— 所以标记方块开箱就能用。
        /// </summary>
        void CreateMarkerVisuals()
        {
            if (_world == null) return;
            if (_hasSpawn && !HasPreviewPrefab(spawnBlockId))
            {
                BuildMarker("SpawnMarker", _spawnCell, new Color(0.20f, 0.85f, 1.00f));
            }
            if (_hasExit && !HasPreviewPrefab(exitBlockId))
            {
                BuildMarker("ExitMarker", _exitCell, new Color(1.00f, 0.35f, 0.18f));
            }
        }

        bool HasPreviewPrefab(string blockId)
        {
            foreach (var kv in _world.Blocks)
            {
                BlockDefinition def = BlockLibrary.Get(kv.Value);
                if (def != null && def.id == blockId) return def.previewPrefab != null;
            }
            return false;
        }

        void BuildMarker(string name, Vector3Int cell, Color color)
        {
            var root = new GameObject(name);
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f);

            Material material = MarkerMaterial(color);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            disc.transform.SetParent(root.transform, false);
            disc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            disc.transform.localScale = new Vector3(0.45f, 0.02f, 0.45f);
            Object.Destroy(disc.GetComponent<Collider>());          // 标记不挡路
            disc.GetComponent<MeshRenderer>().sharedMaterial = material;

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            pole.transform.localScale = new Vector3(0.08f, 0.5f, 0.08f);
            Object.Destroy(pole.GetComponent<Collider>());
            pole.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Material MarkerMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.6f);   // 略微自发光，暗场景里也看得见
            }
            return material;
        }

        void LateUpdate()
        {
            if (_camera == null) return;
            Vector3 target = cameraFollowsPlayer && _walker != null ? _walker.transform.position : LevelCenter();
            _camera.transform.position = target + CameraOffsetWorld();
            _camera.transform.LookAt(target);
        }

        /// <summary>把编辑时看地图的方向搬到运行时：cameraYaw 绕世界 Y 轴旋转机位。</summary>
        Vector3 CameraOffsetWorld()
        {
            return Quaternion.Euler(0f, cameraYaw, 0f) * cameraOffset;
        }

        /// <summary>收集某种标记方块的全部格位，返回数量。</summary>
        int CollectMarkers(string blockId, List<Vector3Int> into)
        {
            into.Clear();
            if (_world == null || string.IsNullOrEmpty(blockId)) return 0;
            foreach (var kv in _world.Blocks)
            {
                BlockDefinition def = BlockLibrary.Get(kv.Value);
                if (def != null && def.id == blockId) into.Add(kv.Key);
            }
            return into.Count;
        }

        static string FormatCells(List<Vector3Int> cells, int max = 12)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < cells.Count && i < max; i++)
            {
                if (i > 0) builder.Append(' ');
                builder.Append('(').Append(cells[i].x).Append(',').Append(cells[i].y).Append(',').Append(cells[i].z).Append(')');
            }
            if (cells.Count > max) builder.Append($" …共 {cells.Count} 个");
            return builder.ToString();
        }

        /// <summary>在地图数据里找第一种 id 匹配的方块（同一 id 有多个时取最低、最靠前的一个）。</summary>
        bool TryFindMarker(string blockId, out Vector3Int cell)
        {
            cell = default;
            if (_world == null || string.IsNullOrEmpty(blockId)) return false;

            bool found = false;
            foreach (var kv in _world.Blocks)
            {
                BlockDefinition def = BlockLibrary.Get(kv.Value);
                if (def == null || def.id != blockId) continue;

                if (!found || kv.Key.y < cell.y ||
                    (kv.Key.y == cell.y && (kv.Key.z < cell.z || (kv.Key.z == cell.z && kv.Key.x < cell.x))))
                {
                    cell = kv.Key;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// 起点该站在哪一层：从起点标记往下找最高的"会被烘成实心几何的方块"，站在它顶面。
        /// 这样标记刷在地板之上那层（y=1）或别的层都能正确站住；标记自己不会被当成地板。
        /// </summary>
        int ResolveStandingY(Vector3Int markerCell)
        {
            for (int y = markerCell.y - 1; y >= 0; y--)
            {
                if (IsBakedSolid(new Vector3Int(markerCell.x, y, markerCell.z))) return y + 1;
            }
            return markerCell.y;
        }

        /// <summary>这一格会不会被烘成真实几何（标记 / 交互道具 / 特殊模型都不算）。</summary>
        bool IsBakedSolid(Vector3Int cell)
        {
            if (_world == null || !_world.IsSolid(cell)) return false;
            BlockDefinition def = BlockLibrary.Get(_world.GetIndex(cell));
            return def != null && !def.interactive && def.previewPrefab == null;
        }

        void SpawnPlayer()
        {
            GameObject prefab = playerPrefab != null ? playerPrefab : Resources.Load<GameObject>("Player/BlockMapPlayer");
            GameObject instance = prefab != null ? Instantiate(prefab) : CreateFallbackPlayer();

            instance.name = "Player";
            _walker = instance.GetComponent<BlockMapGridWalker>();
            if (_walker == null) _walker = instance.AddComponent<BlockMapGridWalker>();
            _walker.cellSize = cellSize;
            _walker.viewYaw = cameraYaw;

            var controller = instance.GetComponent<CharacterController>();
            if (controller == null)
            {
                controller = instance.AddComponent<CharacterController>();
                controller.height = 1f;
                controller.radius = 0.35f;
                controller.center = new Vector3(0f, 0.5f, 0f);
            }

            _walker.Teleport(_spawnStandCell, _spawnFacing);
            instance.transform.position += Vector3.up * 0.05f;     // 抬高一点，避免开局卡进地面

            _walker.Stepped += OnStepped;
            _won = false;
        }

        /// <summary>没给 Prefab 时兜底：一个能看出朝向的灰盒滑块</summary>
        static GameObject CreateFallbackPlayer()
        {
            var root = new GameObject("BlockMapPlayer");

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Visual";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.62f, 0.7f);
            Object.Destroy(body.GetComponent<Collider>());
            Paint(body, new Color(0.3f, 0.9f, 0.72f));

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Facing";
            nose.transform.SetParent(body.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            nose.transform.localScale = new Vector3(0.25f, 0.2f, 0.25f);
            Object.Destroy(nose.GetComponent<Collider>());
            Paint(nose, new Color(0.05f, 0.12f, 0.16f));

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.5f, 0f);
            root.AddComponent<BlockMapGridWalker>().cellSize = 1f;
            return root;
        }

        static void Paint(GameObject go, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = new Material(shader) { color = color };
        }

        void ConfigureCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.orthographic = true;
            camera.orthographicSize = cameraSize;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.08f, 0.12f);

            Vector3 target = cameraFollowsPlayer && _walker != null ? _walker.transform.position : LevelCenter();
            camera.transform.position = target + CameraOffsetWorld();
            camera.transform.LookAt(target);
            _camera = camera;
        }

        Vector3 LevelCenter()
        {
            var size = _world != null ? _world.maxSize : new Vector3Int(16, 1, 16);
            return new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
        }

        void OnStepped(Vector3Int cell)
        {
            if (_hasExit && cell == _exitCell) _won = true;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.R) && _hasSpawn && _walker != null)
            {
                _walker.Teleport(_spawnStandCell, _spawnFacing);
                _won = false;
            }

            if (_world == null || _world.Count == 0)
            {
                _hint = "这个场景里没有方块数据（多半是旧版本烘的场景）：请重新跑一次菜单 ② 烘场景，再跑 ⑦";
            }
            else if (!_hasSpawn) _hint = $"地图里没有起点标记：请在地图编辑器里刷一个「{spawnBlockId}」方块";
            else if (!_hasExit) _hint = $"地图里没有终点标记：请在地图编辑器里刷一个「{exitBlockId}」方块";
            else if (!IsBakedSolid(new Vector3Int(_spawnCell.x, _spawnStandCell.y - 1, _spawnCell.z)))
            {
                _hint = "起点下面没有地板：标记方块把地板顶掉了，请把起点刷在「地板之上那一层」";
            }
            else _hint = null;
        }

        void OnGUI()
        {
            if (!string.IsNullOrEmpty(_hint))
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 260f, 20f, 520f, 48f), GUIContent.none);
                GUI.Label(new Rect(Screen.width * 0.5f - 245f, 34f, 500f, 24f), _hint);
                return;
            }

            GUI.Box(new Rect(16f, 16f, 260f, 76f), GUIContent.none);
            GUI.Label(new Rect(30f, 26f, 240f, 24f), "WASD / 方向键：跳一格");
            GUI.Label(new Rect(30f, 50f, 240f, 24f), "R：回到起点");

            if (_spawnTotal > 1 || _exitTotal > 1)
            {
                GUI.Box(new Rect(16f, 96f, 340f, 26f), GUIContent.none);
                GUI.Label(new Rect(26f, 101f, 330f, 22f), $"起点 ×{_spawnTotal}，终点 ×{_exitTotal}：只生效第一个，多余的请擦除");
            }

            if (!_won) return;

            var style = new GUIStyle(GUI.skin.box) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.5f - 64f, 440f, 128f),
                    "到达终点，关卡完成\n按 R 重新开始", style);
        }
    }
}
