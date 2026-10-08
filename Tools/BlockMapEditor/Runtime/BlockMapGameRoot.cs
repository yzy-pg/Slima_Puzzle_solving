using System.Collections.Generic;
using BlockMap.Gameplay;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 可玩关卡的组合入口：读取起终点、组装玩法模块、管理相机与关卡重置。
    /// 移动、玩家状态、机关、光学和 HUD 分别由 Gameplay 子目录中的组件负责。
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

        [Tooltip("正交相机视野大小：越小 = 越靠近场景（放大）。默认 5.5（原来是 8，更贴近场景）")]
        public float cameraSize = 5.5f;

        [Tooltip("相机背景色。临时想白底看轮廓/截图就设白色（原来的深蓝是 0.055, 0.08, 0.12）")]
        public Color backgroundColor = Color.white;

        [Tooltip("相机方位角（度，取 0 / 90 / 180 / 270）。用来对齐你在 3D 编辑器里看地图的方向：\n" +
                 "0 = 相机在 -Z 侧（编辑器默认，屏幕右 = +X、屏幕上 = +Z）\n" +
                 "90 = 相机在 -X 侧　180 = 相机在 +Z 侧　270 = 相机在 +X 侧\n" +
                 "位移方向会跟着这个角度转，所以 W 永远朝屏幕上方走。")]
        public float cameraYaw = 0f;

        [Tooltip("相机相对角色的位置。默认 (0, 12, -10.5)：和原来的 (0, 18, -16) 是同一个俯视角度，只是更近")]
        public Vector3 cameraOffset = new Vector3(0f, 12f, -10.5f);

        [Tooltip("把相机位置吸附到屏幕像素网格（默认关）。开了像素化网格会更稳，但可能出现逐帧抖动/频闪")]
        public bool snapCameraToPixelGrid = false;

        BlockWorld _world;
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

        BlockGameplayController _gameplay;
        SlimePartyController _party;
        string _initialWorldJson;

        public SlimePartyController Party => _party;
        public bool Won => _won;
        public string Hint => _hint;
        public int SpawnTotal => _spawnTotal;
        public int ExitTotal => _exitTotal;

        void Start()
        {
            _world = GetComponent<BlockWorld>();
            _world.RestoreIfNeeded();
            _initialWorldJson = _world.ToJson();
            ReadLevelMarkers();

            _gameplay = GetComponent<BlockGameplayController>();
            if (_gameplay == null) _gameplay = gameObject.AddComponent<BlockGameplayController>();
            _gameplay.Initialize(_world);

            if (_hasSpawn)
            {
                _party = GetComponent<SlimePartyController>();
                if (_party == null) _party = gameObject.AddComponent<SlimePartyController>();
                _party.Initialize(_gameplay, playerPrefab, _spawnStandCell, _spawnFacing, cellSize, cameraYaw);
                _party.RestartRequested += RestartLevel;
                _party.ExitReached += CompleteLevel;
                _gameplay.BindParty(_party);
            }

            ConfigureGameplayCamera();
            CreateMarkerVisuals();

            BlockMapGameplayHud hud = GetComponent<BlockMapGameplayHud>();
            if (hud == null) hud = gameObject.AddComponent<BlockMapGameplayHud>();
            hud.Initialize(this, _party);
        }

        void Update()
        {
            RefreshHint();
        }

        void LateUpdate()
        {
            if (_camera == null) return;
            SlimeActor active = _party != null ? _party.ActiveActor : null;
            Vector3 target = cameraFollowsPlayer && active != null ? active.transform.position : LevelCenter();
            Vector3 desired = target + CameraOffsetWorld();

            // ★ 像素吸附（默认关闭）：把相机位置量化到"一个屏幕像素"的整数倍。
            //   好处：像素化网格相对世界稳定，环境物品不会随移动重新采样。
            //   风险：吸附基准取自相机的实时朝向，若和 LookAt 互相影响会出现逐帧抖动/频闪 —— 所以默认关。
            if (snapCameraToPixelGrid && _camera.orthographic)
            {
                float unitsPerPixel = (_camera.orthographicSize * 2f) / Mathf.Max(1f, Screen.height);
                Vector3 right = _camera.transform.right;
                Vector3 up = _camera.transform.up;

                float offsetX = Vector3.Dot(desired, right);
                float offsetY = Vector3.Dot(desired, up);
                float snapX = Mathf.Round(offsetX / unitsPerPixel) * unitsPerPixel - offsetX;
                float snapY = Mathf.Round(offsetY / unitsPerPixel) * unitsPerPixel - offsetY;

                desired += right * snapX + up * snapY;
            }

            _camera.transform.position = desired;
            _camera.transform.LookAt(target);
        }

        void OnDestroy()
        {
            if (_party == null) return;
            _party.RestartRequested -= RestartLevel;
            _party.ExitReached -= CompleteLevel;
        }

        void ReadLevelMarkers()
        {
            _hasSpawn = TryFindMarker(spawnBlockId, out _spawnCell);
            _hasExit = TryFindMarker(exitBlockId, out _exitCell);
            _spawnFacing = _hasSpawn ? _world.GetDir(_spawnCell) : 0;
            _spawnStandCell = _hasSpawn
                ? new Vector3Int(_spawnCell.x, ResolveStandingY(_spawnCell), _spawnCell.z)
                : _spawnCell;
            _spawnTotal = CollectMarkers(spawnBlockId, _spawnCells);
            _exitTotal = CollectMarkers(exitBlockId, _exitCells);

            if (_spawnTotal > 1)
                Debug.LogWarning($"[BlockMap] 起点共 {_spawnTotal} 个，仅使用 {_spawnCell}。其余：{FormatCells(_spawnCells)}", this);
            if (_exitTotal > 1)
                Debug.LogWarning($"[BlockMap] 终点共 {_exitTotal} 个，仅使用 {_exitCell}。其余：{FormatCells(_exitCells)}", this);
            if (!_hasSpawn) Debug.LogError($"[BlockMap] 地图里没有「{spawnBlockId}」标记", this);
            if (!_hasExit) Debug.LogError($"[BlockMap] 地图里没有「{exitBlockId}」标记", this);
        }

        void RestartLevel()
        {
            if (_world == null || string.IsNullOrEmpty(_initialWorldJson)) return;
            _world.FromJson(_initialWorldJson);
            _gameplay?.ResetState();
            _party?.RestartParty();
            _won = false;
        }

        void CompleteLevel()
        {
            if (!_hasExit) return;
            _won = true;
            foreach (SlimeActor actor in _party.Actors)
                if (actor != null) actor.SetControlled(false);
        }

        void ConfigureGameplayCamera()
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
            camera.backgroundColor = backgroundColor;
            _camera = camera;
        }

        void RefreshHint()
        {
            if (_world == null || _world.Count == 0)
                _hint = "场景没有方块数据，请重新烘焙关卡";
            else if (!_hasSpawn) _hint = $"缺少起点：{spawnBlockId}";
            else if (!_hasExit) _hint = $"缺少终点：{exitBlockId}";
            else if (!IsBakedSolid(new Vector3Int(_spawnCell.x, _spawnStandCell.y - 1, _spawnCell.z)))
                _hint = "起点下方没有地板；请把起点刷在地板上一层";
            else _hint = null;
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

        Vector3 LevelCenter()
        {
            var size = _world != null ? _world.maxSize : new Vector3Int(16, 1, 16);
            return new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
        }

    }
}
