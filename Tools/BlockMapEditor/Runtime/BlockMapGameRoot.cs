using System.Collections.Generic;
using UnityEngine;
using BlockMap.Gameplay;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        [Tooltip("开启后自动把跟随视点限制在关卡范围内；可按关卡覆盖边界")]
        public bool automaticCameraBounds = true;
        public bool overrideCameraBounds;
        [Tooltip("覆盖边界：x=minX, y=maxX, z=minZ, w=maxZ")]
        public Vector4 cameraBounds;

        [Tooltip("勾选后本关覆盖全局配置中的相机阻尼时间")]
        public bool overrideCameraSmoothTime;
        [Min(0f)] public float cameraSmoothTime = 0.25f;

        [Tooltip("正交相机视野大小：越小 = 场景显示越大。默认 4.5，更贴近角色与地板细节")]
        public float cameraSize = 4.5f;

        [Tooltip("相机背景色。默认暗色（夜晚基调）；想白底截图再改回 (1,1,1)")]
        public Color backgroundColor = new Color(0.02f, 0.03f, 0.06f);

        [Tooltip("相机方位角（度，取 0 / 90 / 180 / 270）。用来对齐你在 3D 编辑器里看地图的方向：\n" +
                 "0 = 相机在 -Z 侧（编辑器默认，屏幕右 = +X、屏幕上 = +Z）\n" +
                 "90 = 相机在 -X 侧　180 = 相机在 +Z 侧　270 = 相机在 +X 侧\n" +
                 "位移方向会跟着这个角度转，所以 W 永远朝屏幕上方走。")]
        public float cameraYaw = 0f;

        [Tooltip("相机相对角色的位置。默认 (0, 12, -10.5)：和原来的 (0, 18, -16) 是同一个俯视角度，只是更近")]
        public Vector3 cameraOffset = new Vector3(0f, 12f, -10.5f);

        [Tooltip("把相机位置吸附到屏幕像素网格（默认关）。开了像素化网格会更稳，但可能出现逐帧抖动/频闪")]
        public bool snapCameraToPixelGrid = false;

        [Header("夜间模式（用来测试灯的照明）")]
        [Tooltip("勾上：平行光调暗 + 环境光变冷暗 + 背景近黑 —— 这时灯的暖色光斑才看得出来")]
        public bool nightMode = false;
        [Tooltip("夜间平行光强度")]
        [Range(0f, 1f)] public float nightSunIntensity = 0.18f;
        [Tooltip("环境光颜色（Flat 模式 = 暗色天空灯）。越暗，未受灯照处越黑、灯的暖色越明显")]
        public Color nightAmbient = new Color(0.08f, 0.10f, 0.16f);
        public Color nightBackground = new Color(0.02f, 0.03f, 0.06f);

        [Header("场景氛围阴影")]
        public bool sceneAtmosphere = true;
        [Range(0f, 1f)] public float atmosphereSunIntensity = 0.45f;
        public Color atmosphereShadow = new Color(0.065f, 0.08f, 0.14f);
        public Color atmosphereAmbient = new Color(0.13f, 0.19f, 0.29f);
        public Color atmosphereEnvironment = new Color(0.22f, 0.30f, 0.42f);

        [Header("相机亮部泛光")]
        public bool enableBloom = true;
        [Min(0f)] public float bloomIntensity = 0.35f;
        [Min(0f)] public float bloomThreshold = 0.6f;
        [Range(0f, 1f)] public float bloomScatter = 0.55f;

        BlockWorld _world;
        Camera _camera;
        Vector3Int _spawnCell;
        Vector3Int _spawnStandCell;
        Vector3Int _exitCell;
        int _spawnFacing;
        string _resolvedSpawnBlockId;
        bool _synchronousSplitMovement;
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
        Vector3 _cameraFocus;
        Vector3 _cameraVelocity;
        bool _cameraFocusInitialized;
        VolumeProfile _bloomProfile;
        readonly Dictionary<Material, Material> _nonShadowMaterials = new Dictionary<Material, Material>();
        readonly List<GameplayProgress> _undoStack = new List<GameplayProgress>(32);
        GameplayProgress _undoCurrent;
        bool _restoringUndo;

        public SlimePartyController Party => _party;
        public bool Won => _won;
        public string Hint => _hint;
        public int SpawnTotal => _spawnTotal;
        public int ExitTotal => _exitTotal;
        public event System.Action ProgressChanged;

        public GameplayProgress CaptureProgress()
        {
            if (_party == null || _party.IsBusy) return null;
            SlimeProgress[] actors = _party.CaptureProgress(out int activeActor);
            return new GameplayProgress
            {
                worldJson = _world.ToJson(),
                actors = actors,
                activeActor = activeActor,
                mechanisms = _gameplay.CaptureProgress(),
                won = _won
            };
        }

        public void RestoreProgress(GameplayProgress progress)
        {
            _world.FromJson(progress.worldJson);
            _party.RestoreProgress(progress.actors, progress.activeActor);
            _gameplay.RestoreProgress(progress.mechanisms);
            GetComponent<BlockMapRuntimeSpawner>()?.Rebuild();
            _won = progress.won;
            if (_won)
                foreach (SlimeActor actor in _party.Actors) actor.SetControlled(false);
            _cameraFocusInitialized = false;
            ProgressChanged?.Invoke();
        }

        void Start()
        {
            _world = GetComponent<BlockWorld>();
            _world.RestoreIfNeeded();
            ReadLevelMarkers();

            _gameplay = GetComponent<BlockGameplayController>();
            if (_gameplay == null) _gameplay = gameObject.AddComponent<BlockGameplayController>();
            _gameplay.Initialize(_world);
            _initialWorldJson = _world.ToJson();

            if (_hasSpawn)
            {
                _party = GetComponent<SlimePartyController>();
                if (_party == null) _party = gameObject.AddComponent<SlimePartyController>();
                _party.Initialize(_gameplay, playerPrefab, _spawnStandCell, _spawnFacing, cellSize, cameraYaw,
                    _synchronousSplitMovement);
                _party.RestartRequested += RestartLevel;
                _party.ExitReached += CompleteLevel;
                _gameplay.BindParty(_party);
                _party.Changed += RecordUndoSnapshot;
                _gameplay.Changed += RecordUndoSnapshot;
            }

            ConfigureGameplayCamera();
            CreateMarkerVisuals();

            BlockMapGameplayHud hud = GetComponent<BlockMapGameplayHud>();
            if (hud == null) hud = gameObject.AddComponent<BlockMapGameplayHud>();
            hud.Initialize(this, _party);

            BlockMapInteractionHud interactions = GetComponent<BlockMapInteractionHud>();
            if (interactions == null) interactions = gameObject.AddComponent<BlockMapInteractionHud>();
            interactions.Initialize(this, _party, _gameplay);

            BlockMapMechanismHud numbers = GetComponent<BlockMapMechanismHud>();
            if (numbers == null) numbers = gameObject.AddComponent<BlockMapMechanismHud>();
            numbers.Initialize(_world);
            _undoCurrent = CaptureProgress();
        }

        void Update()
        {
            RefreshHint();
            if (Input.GetKeyDown(KeyCode.Z)) UndoGameplayStep();
        }

        void RecordUndoSnapshot()
        {
            if (_restoringUndo || _party == null || _party.IsBusy) return;
            GameplayProgress current = CaptureProgress();
            if (current == null) return;
            if (_undoCurrent != null && JsonUtility.ToJson(current) != JsonUtility.ToJson(_undoCurrent))
            {
                _undoStack.Add(_undoCurrent);
                if (_undoStack.Count > 50) _undoStack.RemoveAt(0);
            }
            _undoCurrent = current;
        }

        void UndoGameplayStep()
        {
            if (_undoStack.Count == 0 || _party == null || _party.IsBusy || _won) return;
            GameplayProgress previous = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _restoringUndo = true;
            RestoreProgress(previous);
            _undoCurrent = previous;
            _restoringUndo = false;
        }

        void LateUpdate()
        {
            if (_camera == null) return;
            SlimeActor active = _party != null ? _party.ActiveActor : null;
            Vector3 target = cameraFollowsPlayer && active != null ? active.transform.position : LevelCenter();
            if (automaticCameraBounds) target = ClampCameraFocus(target);
            target = SmoothCameraFocus(target);
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

        Vector3 ClampCameraFocus(Vector3 target)
        {
            if (_world == null || _world.Count == 0) return target;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            if (overrideCameraBounds)
            {
                minX = cameraBounds.x; maxX = cameraBounds.y;
                minZ = cameraBounds.z; maxZ = cameraBounds.w;
            }
            else
            {
                foreach (var block in _world.Blocks)
                {
                    minX = Mathf.Min(minX, block.Key.x);
                    maxX = Mathf.Max(maxX, block.Key.x + 1f);
                    minZ = Mathf.Min(minZ, block.Key.z);
                    maxZ = Mathf.Max(maxZ, block.Key.z + 1f);
                }
            }
            if (_camera == null || !_camera.orthographic) return target;
            Quaternion cameraRotation = Quaternion.LookRotation(-cameraOffset.normalized, Vector3.up);
            Vector3 right = cameraRotation * Vector3.right;
            Vector3 up = cameraRotation * Vector3.up;
            float halfX = Mathf.Abs(right.x) * _camera.orthographicSize * _camera.aspect +
                          Mathf.Abs(up.x) * _camera.orthographicSize;
            float halfZ = Mathf.Abs(right.z) * _camera.orthographicSize * _camera.aspect +
                          Mathf.Abs(up.z) * _camera.orthographicSize;
            target.x = ClampFocusAxis(target.x, minX + halfX, maxX - halfX);
            target.z = ClampFocusAxis(target.z, minZ + halfZ, maxZ - halfZ);
            return target;
        }

        static float ClampFocusAxis(float value, float minimum, float maximum) =>
            minimum <= maximum ? Mathf.Clamp(value, minimum, maximum) : (minimum + maximum) * .5f;

        Vector3 SmoothCameraFocus(Vector3 target)
        {
            float duration = overrideCameraSmoothTime ? cameraSmoothTime : BlockGameplaySettings.Current.cameraSmoothTime;
            if (!_cameraFocusInitialized || duration <= 0f)
            {
                _cameraFocus = target;
                _cameraVelocity = Vector3.zero;
                _cameraFocusInitialized = true;
            }
            else
                _cameraFocus = Vector3.SmoothDamp(_cameraFocus, target, ref _cameraVelocity, duration);
            return _cameraFocus;
        }

        void OnDestroy()
        {
            foreach (Material material in _nonShadowMaterials.Values)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            _nonShadowMaterials.Clear();
            if (_bloomProfile != null)
            {
                foreach (VolumeComponent component in _bloomProfile.components) Destroy(component);
                Destroy(_bloomProfile);
            }
            if (_party == null) return;
            _party.RestartRequested -= RestartLevel;
            _party.ExitReached -= CompleteLevel;
            _party.Changed -= RecordUndoSnapshot;
            if (_gameplay != null) _gameplay.Changed -= RecordUndoSnapshot;
        }

        void ReadLevelMarkers()
        {
            int elevated = 0;
            foreach (var block in _world.Blocks) if (block.Key.y >= 2) elevated++;
            if (elevated > 0)
                Debug.LogWarning($"[BlockMap] 旧地图保留 {elevated} 个 y≥2 方块；该高度已废弃，请策划检查。数据未自动修改。", this);
            _resolvedSpawnBlockId = spawnBlockId;
            if (TryFindMarker("Spawn_Synchronous", out _spawnCell))
            {
                _resolvedSpawnBlockId = "Spawn_Synchronous";
                _synchronousSplitMovement = true;
            }
            else if (TryFindMarker("Spawn_Asynchronous", out _spawnCell))
            {
                _resolvedSpawnBlockId = "Spawn_Asynchronous";
                _synchronousSplitMovement = false;
            }
            else _hasSpawn = TryFindMarker(spawnBlockId, out _spawnCell);
            _hasSpawn = _hasSpawn || _resolvedSpawnBlockId == "Spawn_Synchronous" ||
                        _resolvedSpawnBlockId == "Spawn_Asynchronous";
            _hasExit = TryFindMarker(exitBlockId, out _exitCell);
            _spawnFacing = _hasSpawn ? _world.GetDir(_spawnCell) : 0;
            _spawnStandCell = _hasSpawn
                ? new Vector3Int(_spawnCell.x, ResolveStandingY(_spawnCell), _spawnCell.z)
                : _spawnCell;
            _spawnTotal = CollectMarkers(_resolvedSpawnBlockId, _spawnCells);
            _exitTotal = CollectMarkers(exitBlockId, _exitCells);

            if (_spawnTotal > 1)
                Debug.LogWarning($"[BlockMap] 起点共 {_spawnTotal} 个，仅使用 {_spawnCell}。其余：{FormatCells(_spawnCells)}", this);
            if (_exitTotal > 1)
                Debug.LogWarning($"[BlockMap] 终点共 {_exitTotal} 个，仅使用 {_exitCell}。其余：{FormatCells(_exitCells)}", this);
            if (!_hasSpawn) Debug.LogError($"[BlockMap] 地图里没有「{spawnBlockId}」或同步/异步出生点标记", this);
            if (!_hasExit) Debug.LogError($"[BlockMap] 地图里没有「{exitBlockId}」标记", this);
        }

        void RestartLevel()
        {
            if (_world == null || string.IsNullOrEmpty(_initialWorldJson)) return;
            _world.FromJson(_initialWorldJson);
            _gameplay?.ResetState();
            _party?.RestartParty();
            _cameraFocusInitialized = false;
            _won = false;
            _undoStack.Clear();
            _undoCurrent = CaptureProgress();
            ProgressChanged?.Invoke();
        }

        void CompleteLevel()
        {
            if (!_hasExit) return;
            _won = true;
            foreach (SlimeActor actor in _party.Actors)
                if (actor != null) actor.SetControlled(false);
            ProgressChanged?.Invoke();
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
            camera.backgroundColor = nightMode ? nightBackground : backgroundColor;
            camera.allowHDR = true;
            _camera = camera;

            if (enableBloom)
            {
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                var volumeObject = new GameObject("Gameplay Bloom");
                volumeObject.transform.SetParent(transform, false);
                var volume = volumeObject.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = 10f;
                _bloomProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                _bloomProfile.name = "Gameplay Bloom (Runtime)";
                var bloom = _bloomProfile.Add<Bloom>(true);
                bloom.intensity.Override(bloomIntensity);
                bloom.threshold.Override(bloomThreshold);
                bloom.scatter.Override(bloomScatter);
                bloom.tint.Override(new Color(0.85f, 0.93f, 1f));
                volume.sharedProfile = _bloomProfile;
            }

            ApplyNightMode();
        }

        /// <summary>
        /// 环境光：**默认就用"暗色天空灯"（Flat 环境光）**。
        /// 旧版什么都不设 → 用 Skybox 的环境光，空场景里会偏亮发白，看起来像 2D 平涂、也读不到"夜晚"感。
        /// nightMode 额外把平行光调暗（灯的点光才会成为主要光源）。
        /// </summary>
        void ApplyNightMode()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = sceneAtmosphere ? atmosphereEnvironment : nightAmbient;

            foreach (Light light in Object.FindObjectsOfType<Light>())
            {
                if (light.gameObject.scene != gameObject.scene || light.type != LightType.Directional) continue;
                if (nightMode) light.intensity = nightSunIntensity;
                else if (sceneAtmosphere) light.intensity = atmosphereSunIntensity;
                if (sceneAtmosphere) light.shadows = LightShadows.Soft;
            }

            foreach (GameObject sceneRoot in gameObject.scene.GetRootGameObjects())
                foreach (Renderer item in sceneRoot.GetComponentsInChildren<Renderer>(true))
                    ApplyAtmosphereTo(item);
        }

        public void ApplyAtmosphereTo(Renderer item)
        {
            // Props receive terrain shadows without casting solid shadows themselves.
            bool terrain = item.name.StartsWith("Chunk_", System.StringComparison.Ordinal);
            item.shadowCastingMode = terrain ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (terrain)
            {
                // Baked chunks can also contain lamp submeshes. Exclude those passes
                // without changing shared assets or the walls in the same mesh.
                Material[] materials = item.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null || !source.GetShaderPassEnabled("ShadowCaster")) continue;
                    string id = source.name.StartsWith("BlockMap_", System.StringComparison.Ordinal)
                        ? source.name.Substring("BlockMap_".Length) : source.name;
                    BlockDefinition definition = BlockLibrary.Get(id);
                    if (definition == null || (!definition.IsLamp && !definition.emitLight &&
                        !definition.interactive && definition.previewPrefab == null && BlockGameplayCatalog.Get(id) == null)) continue;
                    if (!_nonShadowMaterials.TryGetValue(source, out Material material))
                    {
                        material = new Material(source) { name = source.name, hideFlags = HideFlags.DontSave };
                        material.SetShaderPassEnabled("ShadowCaster", false);
                        _nonShadowMaterials.Add(source, material);
                    }
                    materials[i] = material;
                    changed = true;
                }
                if (changed) item.sharedMaterials = materials;
                item.receiveShadows = true;
            }
            if (!sceneAtmosphere) return;
            bool toon = false;
            foreach (Material material in item.sharedMaterials)
                if (material != null && (material.shader.name == "Slima/Toon (Cel + Pixel)" ||
                    material.shader.name == "BlockMap/BlockUnlitTransparent")) toon = true;
            if (!toon) return;
            var properties = new MaterialPropertyBlock();
            item.GetPropertyBlock(properties);
            properties.SetColor("_ShadowColor", atmosphereShadow);
            properties.SetColor("_AmbientColor", atmosphereAmbient);
            properties.SetFloat("_FallbackLight", 0f);
            item.SetPropertyBlock(properties);
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
            if (_hasSpawn && !HasPreviewPrefab(_resolvedSpawnBlockId))
            {
                Color spawnColor = _synchronousSplitMovement
                    ? new Color(0.25f, 0.9f, 0.55f) : new Color(0.20f, 0.85f, 1.00f);
                BuildMarker("SpawnMarker", _spawnCell, spawnColor);
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
            ApplyAtmosphereTo(disc.GetComponent<MeshRenderer>());

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(root.transform, false);
            pole.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            pole.transform.localScale = new Vector3(0.08f, 0.5f, 0.08f);
            Object.Destroy(pole.GetComponent<Collider>());
            pole.GetComponent<MeshRenderer>().sharedMaterial = material;
            ApplyAtmosphereTo(pole.GetComponent<MeshRenderer>());
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
