using System.IO;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 游戏内地图编辑器（运行时脚本，打包后照样能用）。
    ///
    /// 它是一个**模式**：默认按 Tab 进入 / 退出。
    ///   编辑模式：方块面板 + 笔刷 + 鼠标放/删 + 正交相机环绕；每改一格只重建受影响的 chunk，纹样实时自适应。
    ///   退出编辑：本脚本不再吃鼠标键盘，相机也交回给你的游戏逻辑（OrthoOrbitCamera.InputEnabled = false）。
    ///
    /// 操作（编辑模式）：
    ///   左键            放置（笔刷大小 1/3/5 → 1³/3³/5³ 个方块）
    ///   Shift + 左键    删除
    ///   [ / ]           切换方块（也可点面板里的图标）
    ///   - / =           笔刷缩小 / 放大
    ///   右键拖拽        旋转视角；中键或 Alt+左键 平移；滚轮缩放；1~4 视角预设；P 像素完美
    ///   Tab             进出编辑模式
    ///   F5 / F9         保存 / 读取当前存档槽
    /// </summary>
    [RequireComponent(typeof(BlockWorld))]
    public class BlockMapEditorTool : MonoBehaviour
    {
        [Header("模式")]
        public KeyCode toggleKey = KeyCode.Tab;
        [Tooltip("是否处于地图编辑模式")]
        public bool editorMode = true;

        [Header("笔刷")]
        public int brushSize = 1;              // 1 / 3 / 5
        [Tooltip("刷子朝向（0=+Z 前 / 1=+X 右 / 2=-Z 后 / 3=-X 左），按 R 旋转")]
        public int brushDir = 0;
        static readonly string[] DirNames = { "+Z 前", "+X 右", "-Z 后", "-X 左" };
        public float maxReach = 200f;

        [Header("橡皮擦")]
        [Tooltip("开启后左键即删除（不用按 Shift）")]
        public bool eraseMode = false;

        [Header("高度锁（左侧面板可开关）")]
        [Tooltip("开启后所有放置/删除都锁定在 lockedY 这一层，做高塔和铺地板时不会误点到别的层")]
        public bool lockHeight = false;
        public int lockedY = 0;

        [Header("存档")]
        public string slotName = "map01";

        BlockWorld _world;
        BlockChunkRenderer _renderer;
        OrthoOrbitCamera _cam;
        BlockMapLinkEditorTool _linkEditor;
        int _selected;
        // 拖拽时锁定一个平面：按下左键那一刻记录命中的面，拖动只在那个平面上铺格。
        // 否则每放一块，鼠标下的命中面就变了，会连锁长成刺球（之前截图里那团就是）。
        bool _planeLocked;
        bool _hitIsProp;
        bool _showBlockList = true;
        bool _showMapList = true;
        bool _confirmDeleteMap;                  // 删除地图的二次确认
        float _confirmUntil;                // 地图下拉是否展开
        Vector2 _mapListScroll;                  // 地图列表滚动位置
        bool _expandStatic = true;               // "静态墙面"分组是否展开
        bool _expandProps = true;                // "交互道具/模型"分组是否展开              // 方块库下拉是否展开
        bool _expandLamps = true;
        Vector2 _blockListScroll;                // 方块库列表滚动位置
        Vector2 _panelScroll;                    // 整个面板的滚动位置
        bool _planeErase;
        Vector3 _planeNormal;
        int _lockedAxis;
        int _lockedCoord;
        Vector3Int _lastCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        string _toast = "";
        float _toastUntil;
        [Header("面板位置 / 大小（可在 Inspector 里调；每帧会自动夹回屏幕内，保证看得见）")]
        public Rect panelRect = new Rect(8, 8, 330, 540);

        [Header("诊断")]
        [Tooltip("画一圈红框并显示 IMGUI 坐标系信息，用来排查 UI 偏移/不可视")]
        public bool showDebugBounds = true;
        bool _showPanel = true;

        /// <summary>
        /// 面板上「新地图名字」输入框的内容，故意和 slotName（当前正在编辑的那张）分开。
        /// 以前两者是同一个变量 —— 于是"改个名字再点保存"会把当前地图另存成新名字，看起来就是
        /// 「新建 = 复制现有地图」。现在新建只能通过「新建」按钮，且一定是空地图。
        /// </summary>
        string _slotNameInput = "";

        public bool IsEditing => editorMode;

        /// <summary>鼠标当前指向的格子 —— 供 BlockMapGuides 显示放置虚影。</summary>
        public bool HasCursor { get; private set; }
        public Vector3Int CursorCell { get; private set; }

        void Awake()
        {
            Active = this;
            _world = GetComponent<BlockWorld>();
            _renderer = GetComponent<BlockChunkRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<BlockChunkRenderer>();
            _cam = FindCamera();
            _linkEditor = GetComponent<BlockMapLinkEditorTool>();
            _slotNameInput = slotName;
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        /// <summary>场景里的地图编辑器实例。正交相机用它判断「鼠标是不是停在面板上」——
        /// 否则在面板上滚滚轮会连场景一起缩放、拖拽会转动相机。</summary>
        public static BlockMapEditorTool Active { get; private set; }

        /// <summary>鼠标是否停在编辑器面板上。GUI 坐标原点在左上，鼠标坐标在左下 → Y 要翻转。</summary>
        public bool IsPointerOverPanel()
        {
            if (!editorMode) return false;
            var guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (_linkEditor != null && _linkEditor.IsPointerOverPanel(guiMouse)) return true;
            if (_showPanel) return panelRect.Contains(guiMouse);
            // 面板收起时只剩左上角一个小小的「显示面板」按钮，别让相机抢它的点击/滚轮
            return new Rect(panelRect.x, panelRect.y, 96f, 30f).Contains(guiMouse);
        }

        OrthoOrbitCamera FindCamera()
        {
            if (Camera.main == null) return null;
            return Camera.main.GetComponent<OrthoOrbitCamera>();
        }

        public void SetEditorMode(bool on)
        {
            editorMode = on;
            OrthoOrbitCamera.InputEnabled = on;
            if (on) Toast("进入地图编辑模式");
        }

        void Update()
        {
            EnsureRefs();
            if (Input.GetKeyDown(toggleKey)) SetEditorMode(!editorMode);
            if (!editorMode) return;

            if (_cam == null) _cam = FindCamera();
            OrthoOrbitCamera.InputEnabled = true;

            if (Input.GetKeyDown(KeyCode.LeftBracket)) Cycle(-1);
            if (Input.GetKeyDown(KeyCode.RightBracket)) Cycle(1);
            if (Input.GetKeyDown(KeyCode.Minus)) brushSize = Mathf.Max(1, brushSize - 2);
            if (Input.GetKeyDown(KeyCode.Equals)) brushSize = Mathf.Min(5, brushSize + 2);
            if (Input.GetKeyDown(KeyCode.R))
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (!shift || !TryRotateHoveredProp())
                {
                    brushDir = (brushDir + 1) & 3;
                    Toast($"刷子朝向：{DirNames[brushDir]}");
                }
            }
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.Z)) _world.Undo();
            if (ctrl && Input.GetKeyDown(KeyCode.Y)) _world.Redo();
            if (Input.GetKeyDown(KeyCode.F5)) Save();
            if (Input.GetKeyDown(KeyCode.F9)) Load();
            if (_linkEditor != null && _linkEditor.LinkMode) return;

            if (_cam == null) return;
            // 相机拖拽中（右键旋转 / 中键平移 / Alt+左键平移）不落方块。
            // ⚠️ 这里直接看"鼠标按键"，不再依赖 OrthoOrbitCamera.Busy：
            //    两个组件的 Update 执行顺序不确定，读上一帧的 Busy 会把本次点击整个吞掉
            //    —— 这正是"面板能点、世界里点了没反应"的原因。
            if (Input.GetMouseButton(1) || Input.GetMouseButton(2) || Input.GetKey(KeyCode.LeftAlt)) return;

            // 鼠标落在面板上时不落方块（IMGUI 坐标原点在左上，鼠标坐标在左下 → Y 要翻转）
            var guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (panelRect.Contains(guiMouse)) return;

            Camera cam = _cam.GetComponent<Camera>();
            if (cam == null) return;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);

            // ---- 按下左键：采样最近表面，并把之后这次拖拽锁在这一面所在的平面上 ----
            if (Input.GetMouseButtonDown(0))
            {
                _planeLocked = false;
                _lastCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
                _world.BeginStroke();                       // 开始记录这一笔画（用于 Ctrl+Z 撤销）
                if (RaycastVoxel(ray, out var hitCell, out var hitNormal))
                {
var hitDef0 = BlockLibrary.Get(_world.GetIndex(hitCell));
_hitIsProp = hitDef0 != null && hitDef0.previewPrefab != null;
_planeErase = eraseMode || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    _planeNormal = hitNormal;
                    _lockedAxis = Mathf.Abs(hitNormal.x) > 0.5f ? 0 : (Mathf.Abs(hitNormal.y) > 0.5f ? 1 : 2);
                    if (_planeErase)
                    {
                        _lockedCoord = _lockedAxis == 0 ? hitCell.x : (_lockedAxis == 1 ? hitCell.y : hitCell.z);
                    }
                    else
                    {
                        // 放置面 = 命中格外侧那一层（和之前一样"贴着那一面放"）
                        var target = hitCell + Vector3Int.RoundToInt(hitNormal);
                        _lockedCoord = _lockedAxis == 0 ? target.x : (_lockedAxis == 1 ? target.y : target.z);
                    }
                    _planeLocked = true;
                    Place(ray);
                }
            }
            // ---- 按住左键拖动：只在锁定平面上铺格 ----
            else if (Input.GetMouseButton(0) && _planeLocked)
            {
                Place(ray);
            }
            else if (!Input.GetMouseButton(0))
            {
                if (_planeLocked) _world.EndStroke();       // 松手 = 这一笔画结束，压入撤销栈
                _planeLocked = false;
                if (!_showPanel) HasCursor = false;         // 面板收起时也把虚影收掉
                HasCursor = false;
            }
        }

        /// <summary>把射线打到锁定平面上，取那一格（同一格不重复操作）。</summary>
        void Place(Ray ray)
        {
            var plane = new Plane(_planeNormal, _lockedAxis == 0
                ? new Vector3(_lockedCoord + 0.5f, 0f, 0f)
                : _lockedAxis == 1 ? new Vector3(0f, _lockedCoord + 0.5f, 0f) : new Vector3(0f, 0f, _lockedCoord + 0.5f));
            if (!plane.Raycast(ray, out float dist)) return;

            Vector3 p = ray.GetPoint(dist);
            var cell = new Vector3Int(
                _lockedAxis == 0 ? _lockedCoord : Mathf.FloorToInt(p.x),
                _lockedAxis == 1 ? _lockedCoord : Mathf.FloorToInt(p.y),
                _lockedAxis == 2 ? _lockedCoord : Mathf.FloorToInt(p.z));

            // 高度锁：把落点强行夹到指定层
            if (lockHeight) cell = new Vector3Int(cell.x, lockedY, cell.z);

            // ★ 判重必须用「抬升之前」的格子。
            //   逻辑物件（门 / 机关 / 起点 / 终点）会自动往上找空位再放；如果拿抬升后的格子判重，
            //   按住鼠标（拖动）时每一帧都会发现"上一格被自己占了"→ 继续往上叠一个，
            //   一直叠到 lift 上限为止 —— 表现就是"点一下，原地 1 个 + 顶上又冒出 3 个"。
            Vector3Int strokeCell = cell;

            // ★ 带模型的物件（门/箱子/家具…）与「交互 / 标记方块」当作"放在方块上方的物品"：
            //   落点已经有方块时自动往上找空位再放，避免把地板/墙面顶掉（顶掉后那格会露空洞）。
            if (!_planeErase && !lockHeight && !_hitIsProp)
            {
                var selDef = BlockLibrary.Get(_selected);
                if (IsSingleCellProp(selDef))
                {
                    int lift = 0;
                    while (lift < 4 && _world.GetIndex(cell) >= 0)
                    {
                        cell.y += 1;
                        lift++;
                    }
                    if (lift > 0 && _world.GetIndex(cell) >= 0) return;   // 上面几层也满了 → 这次不落
                }
            }

            HasCursor = _world.InBounds(cell);         // 超出世界范围只更新虚影，不落块
            CursorCell = cell;                         // 虚影画在"真正会落下的那一格"
            if (!HasCursor) return;

            if (strokeCell == _lastCell) return;       // 同一列在这一笔里只落一次（与抬升无关）
            _lastCell = strokeCell;
            // ★ 逻辑物件（门 / 机关 / 起点 / 终点…）无视笔刷大小，永远只作用一格 ——
            //   否则 3³ / 5³ 笔刷点一下就会刷出一整片起点终点。
            if (!_planeErase && brushSize > 1 && IsSingleCellProp(BlockLibrary.Get(_selected)) && _warnedBrushSize != brushSize)
            {
                _warnedBrushSize = brushSize;
                Toast("逻辑物件（门 / 机关 / 起点 / 终点）只刷一格，笔刷体积对它无效");
            }

            Brush(cell, _planeErase ? -1 : _selected, _planeErase ? brushSize : BrushSizeFor(_selected));
            if (!_planeErase) _world.SetDir(cell, brushDir);    // 朝向写在落点格（3³/5³ 笔刷的邻格用默认朝向）
        }

        int _warnedBrushSize;

        /// <summary>Shift+R：旋转鼠标指向的单格机关，不需要擦除后重新摆放。</summary>
        bool TryRotateHoveredProp()
        {
            if (_cam == null) return false;
            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (panelRect.Contains(guiMouse)) return false;

            Camera camera = _cam.GetComponent<Camera>();
            if (camera == null) return false;
            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            if (!RaycastVoxel(ray, out Vector3Int cell, out _)) return false;

            BlockDefinition definition = BlockLibrary.Get(_world.GetIndex(cell));
            if (!IsSingleCellProp(definition)) return false;

            int direction = (_world.GetDir(cell) + 1) & 3;
            _world.SetDir(cell, direction);
            brushDir = direction;
            Toast($"已旋转 {definition.displayName}：{DirNames[direction]}");
            return true;
        }

        /// <summary>供关联编辑器获取鼠标所指的已放置物件，不暴露体素步进细节。</summary>
        public bool TryGetHoveredOccupiedCell(out Vector3Int cell)
        {
            cell = Vector3Int.zero;
            EnsureRefs();
            if (!editorMode || _cam == null || IsPointerOverPanel()) return false;

            Camera camera = _cam.GetComponent<Camera>();
            if (camera == null) return false;
            return RaycastVoxel(camera.ScreenPointToRay(Input.mousePosition), out cell, out _) &&
                   _world.IsSolid(cell);
        }

        public void ShowMessage(string message) => Toast(message);

        /// <summary>逻辑物件（门 / 机关 / 起点 / 终点…）：占一格、且要放在方块上方而不是顶掉它。</summary>
        static bool IsSingleCellProp(BlockDefinition def)
        {
            return def != null && (def.interactive || def.previewPrefab != null);
        }

        /// <summary>逻辑物件强制单格笔刷；普通地形方块照笔刷大小走。</summary>
        int BrushSizeFor(int index)
        {
            return IsSingleCellProp(BlockLibrary.Get(index)) ? 1 : brushSize;
        }

        void Brush(Vector3Int center, int index, int size)
        {
            int n = Mathf.Max(1, size);
            int lo = -(n / 2);
            bool changed = false;
            for (int x = 0; x < n; x++)
                for (int y = 0; y < n; y++)
                    for (int z = 0; z < n; z++)
                    {
                        var p = center + new Vector3Int(lo + x, lo + y, lo + z);
                        // 高度锁：笔刷也压成一层。否则 3³/5³ 笔刷会往上下层溢出，
                        // 表现就是"开了高度锁还能在有方块的地方继续往上加"。
                        if (lockHeight) p = new Vector3Int(p.x, lockedY, p.z);
                        if (index < 0)
                        {
                            if (_world.IsSolid(p)) { _world.Remove(p); changed = true; }
                        }
                        else if (_world.GetIndex(p) != index) { _world.Set(p, index); changed = true; }
                    }
            if (changed && _renderer != null) { /* 脏 chunk 由 LateUpdate 自动重建 */ }
        }

        void Cycle(int delta)
        {
            int n = BlockLibrary.Count;
            if (n <= 0) return;
            _selected = ((_selected + delta) % n + n) % n;
            var def = BlockLibrary.Get(_selected);
            Toast("当前方块：" + (def != null ? def.displayName : "?"));
        }

        static Vector2 GuiMouse() => new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);

        // ---------------- 体素射线（纯数据步进，不需要碰撞体） ----------------

        /// <summary>
        /// 鼠标 → 世界射线 → 采样最近的那个实心方块表面。
        /// 注意：正交相机为了让深度精度够用放在目标 300 单位外，所以可行进距离必须
        /// 加上"相机到观察目标"的距离，否则射线走不到方块上（这是之前"无法编辑"的原因）。
        /// </summary>
        bool RaycastVoxel(Ray ray, out Vector3Int hitPos, out Vector3 hitNormal)
        {
            hitPos = Vector3Int.zero;
            hitNormal = Vector3.zero;
            const float step = 0.2f;
            Vector3 dir = ray.direction.normalized;

            float camDist = _cam != null ? Vector3.Distance(ray.origin, _cam.target) : 0f;
            float reach = camDist + maxReach;

            // 1) 体素步进：找到最近的实心格，法线取"进入这一格的来向"
            Vector3Int prev = Vector3Int.zero;
            bool hasPrev = false;
            for (float t = 0f; t < reach; t += step)
            {
                Vector3 p = ray.origin + dir * t;
                var cell = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
                if (!_world.IsSolid(cell)) { prev = cell; hasPrev = true; continue; }

                hitPos = cell;
                hitNormal = hasPrev ? (Vector3)(prev - cell) : -(Vector3)Vector3Int.RoundToInt(dir);
                return true;
            }

            // 2) 一个方块都没打到 → 退化成和 y=0 地面求交，这样空地图也能落下第一块
            if (Mathf.Abs(dir.y) > 1e-4f)
            {
                float tPlane = -ray.origin.y / dir.y;
                if (tPlane > 0f && tPlane < reach)
                {
                    Vector3 p = ray.origin + dir * tPlane;
                    hitPos = new Vector3Int(Mathf.FloorToInt(p.x), -1, Mathf.FloorToInt(p.z));
                    hitNormal = Vector3.up;
                    return true;
                }
            }
            return false;
        }

        // ---------------- 存档 ----------------

        string MapsDir
        {
            get
            {
                string d = Path.Combine(Application.persistentDataPath, "Maps");
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
        }

        public string SavePath => Path.Combine(MapsDir, slotName + ".json");

        public void Save()
        {
            File.WriteAllText(SavePath, _world.ToJson());
            Toast("已保存 → " + SavePath);
            Debug.Log("[BlockMap] 保存到 " + SavePath);
        }

        public void Load()
        {
            if (!File.Exists(SavePath)) { Toast("没有这个存档：" + SavePath); return; }
            _world.FromJson(File.ReadAllText(SavePath));
            if (_renderer != null) _renderer.RebuildAll();
            Toast("已读取 ← " + SavePath);
        }

        /// <summary>
        /// 懒解析引用。Bootstrap 带 DefaultExecutionOrder(-100)，它的 Awake 会跑在
        /// 本组件 Awake 之前 —— 那时 _world 还是 null（这正是 TryLoadSilently 报 NRE 的原因）。
        /// 所以任何可能被外部提前调用的方法，都先过一遍这里。
        /// </summary>
        void EnsureRefs()
        {
            if (_world == null) _world = GetComponent<BlockWorld>();
            if (_renderer == null) _renderer = GetComponent<BlockChunkRenderer>();
            if (_linkEditor == null) _linkEditor = GetComponent<BlockMapLinkEditorTool>();
            if (_cam == null) _cam = FindCamera();
        }

        /// <summary>启动时静默读取（读不到就当空地图，不弹提示）。</summary>
        public bool TryLoadSilently()
        {
            EnsureRefs();
            if (!File.Exists(SavePath)) return false;
            _world.FromJson(File.ReadAllText(SavePath));
            if (_renderer != null) _renderer.RebuildAll();
            return true;
        }

        public void NewMap()
        {
            _world.Clear();
            if (_renderer != null) _renderer.RebuildAll();
            Toast("已新建空地图");
        }

        /// <summary>方块库里给特殊方块加的前缀标记 —— 一眼看出它不是普通方块</summary>
        static string ModeTag(BlockDefinition def)
        {
            if (def == null) return "";
            if (def.IsLamp) return "[灯]";
            if (def.previewPrefab != null) return "[模型]";
            if (def.renderMode == BlockRenderMode.Transparent) return "[透]";
            if (def.renderMode == BlockRenderMode.Animated) return "[动]";
            if (def.renderMode == BlockRenderMode.Cutout) return "[镂空]";
            return def.doubleSided ? "[双]" : "";
        }

        /// <summary>列出存档目录里所有地图槽（多地图支持：一个编辑器管多张地图，每张一个 json）</summary>
        string[] GetMapSlots()
        {
            if (!Directory.Exists(MapsDir)) return new string[0];
            string[] files = Directory.GetFiles(MapsDir, "*.json");
            var names = new string[files.Length];
            for (int i = 0; i < files.Length; i++) names[i] = Path.GetFileNameWithoutExtension(files[i]);
            System.Array.Sort(names);
            return names;
        }

        /// <summary>切换地图：先把当前这张存下来（静默），再读目标地图</summary>
        void SwitchMap(string name)
        {
            if (string.IsNullOrEmpty(name) || name == slotName) return;
            Save();                 // 切换前自动保存当前地图
            slotName = name;
            _slotNameInput = name;
            Load();
        }

        // ---------------- 地图管理（下拉里的三个按钮）----------------

        /// <summary>
        /// 新建一张空地图：从面板上的「新地图名字」取名，先把当前这张存好，再清空画布。
        /// ⚠️ 绝不能把当前地图的内容带过去 —— 这里显式调 NewMap()（内部 _world.Clear()）。
        /// </summary>
        void MapNew()
        {
            string name = (_slotNameInput ?? "").Trim();
            if (name.Length == 0) { Toast("先在上面「新地图名字」里填个名字"); return; }
            if (name == slotName) { Toast($"「{name}」就是当前地图 —— 换个名字才是新建"); return; }
            if (File.Exists(Path.Combine(MapsDir, name + ".json")))
            {
                Toast($"「{name}」已存在 —— 换个名字，或从地图列表里点它切换过去");
                return;
            }

            Save();                 // 先把当前这张存好（存的是旧槽名）
            slotName = name;        // 切到新槽
            _slotNameInput = name;
            NewMap();               // 清空画布 = 全新空地图
            Toast($"已新建空地图「{name}」：画完记得点「保存」");
        }

        void MapRename()
        {
            string name = (_slotNameInput ?? "").Trim();
            if (name.Length == 0) { Toast("先在上面填个新名字"); return; }
            string oldPath = Path.Combine(MapsDir, slotName + ".json");
            if (!File.Exists(oldPath)) { Toast("当前地图还没保存过，先点「保存」"); return; }
            if (name == slotName) { Toast("名字没变"); return; }
            string newPath = Path.Combine(MapsDir, name + ".json");
            if (File.Exists(newPath)) { Toast($"「{name}」已存在"); return; }

            Save();                 // 把最新内容写进旧文件，再改名
            File.Move(oldPath, newPath);
            string oldName = slotName;
            slotName = name;
            _slotNameInput = name;
            Toast($"已重命名：{oldName} → {name}");
        }

        void MapDelete()
        {
            if (string.IsNullOrEmpty(slotName)) return;
            string p = Path.Combine(MapsDir, slotName + ".json");
            if (!File.Exists(p)) { Toast("当前地图还没保存过"); return; }
            if (!_confirmDeleteMap)
            {
                _confirmDeleteMap = true;
                _confirmUntil = Time.realtimeSinceStartup + 3f;
                Toast($"再点一次「确认删除?」将删除「{slotName}」");
                return;
            }
            _confirmDeleteMap = false;
            File.Delete(p);
            Toast($"已删除「{slotName}」：画布内容还在，点「保存」可另存一张");
        }

        void Toast(string msg) { _toast = msg; _toastUntil = Time.realtimeSinceStartup + 5f; }

        // ---------------- 面板 ----------------

        float TileSizeOf(BlockDefinition def)
        {
            if (def.atlasOverride != null) return Mathf.Max(1, def.atlasOverride.width / AutoTileAtlas.Sheet);
            if (def.panelTexture != null) return Mathf.Min(def.panelTexture.width, def.panelTexture.height);
            if (def.simpleTexture != null) return Mathf.Min(def.simpleTexture.width, def.simpleTexture.height);
            return 16f;
        }

        void OnGUI()
        {
            EnsureRefs();
            // —— 诊断：红框 = IMGUI 认为的屏幕边界；文字给出坐标系实测值 ——
            // 如果红框没有正好贴住 Game 视图的四条边，说明是视图缩放/DPI 导致的坐标错位。
            if (showDebugBounds)
            {
                GUI.color = new Color(1f, 0.25f, 0.25f, 0.85f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, 3), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, Screen.height - 3, Screen.width, 3), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, 0, 3, Screen.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(Screen.width - 3, 0, 3, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                string camInfo = "无相机(OrthoOrbitCamera 未挂到 MainCamera?)";
                if (_cam != null)
                {
                    var t = _cam.transform;
                    camInfo = $"{(_cam.fixedPosition ? "固定机位" : "自由机位")} size={_cam.size:F1} " +
                              $"yaw={_cam.yaw:F0} pitch={_cam.pitch:F0} offset={_cam.fixedOffset} pos={t.position}";
                }
                GUI.Label(new Rect(12, Screen.height - 84, 1700, 20),
                    $"[BlockMap] Screen={Screen.width}x{Screen.height} mode={(editorMode ? "编辑" : "游戏")} " +
                    $"hit={(HasCursor ? CursorCell.ToString() : "无")} plane={_planeLocked} erase={_planeErase} " +
                    $"count={(_world != null ? _world.Count : -1)} busy={OrthoOrbitCamera.Busy} | {camInfo}");
            }

            if (!editorMode)
            {
                GUI.Label(new Rect(8, 8, 300, 20), "Tab 进入地图编辑模式");
                return;
            }

            // 关键：每帧把面板夹回可见区域，避免出现"面板被推到屏幕外 → UI 不可视"
            panelRect.width = Mathf.Clamp(panelRect.width, 260f, Mathf.Max(180f, Screen.width - 16f));
            panelRect.height = Mathf.Clamp(panelRect.height, 160f, Mathf.Max(160f, Screen.height - 70f));
            panelRect.x = Mathf.Clamp(panelRect.x, 0f, Mathf.Max(0f, Screen.width - panelRect.width));
            panelRect.y = Mathf.Clamp(panelRect.y, 0f, Mathf.Max(0f, Screen.height - panelRect.height - 30f));

            GUILayout.BeginArea(panelRect, GUI.skin.box);
            // 面板本体可滚动：资产/地图再多也够得着
            _panelScroll = GUILayout.BeginScrollView(_panelScroll, false, true);

            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>地图编辑</b>", new GUIStyle(GUI.skin.label) { richText = true });
            if (GUILayout.Button("归位", GUILayout.Width(46)))
                panelRect = new Rect(8, 8, 330, Mathf.Min(540f, Screen.height - 70f));
            if (GUILayout.Button("收起", GUILayout.Width(46))) _showPanel = false;
            GUILayout.EndHorizontal();

            var defs = BlockLibrary.All;
            // ── 方块库：下拉式（点标题展开/收起；展开后带滚动条，不再一长条）──
            var curDef = BlockLibrary.Get(_selected);
            string curLabel = curDef != null ? $"{_selected + 1}. {ModeTag(curDef)}{curDef.displayName}" : "（空）";
            if (GUILayout.Button($"{(_showBlockList ? "▾" : "▸")} 方块库：{curLabel}    共 {defs.Count} 项  笔刷 {brushSize}³"))
                _showBlockList = !_showBlockList;
            if (_showBlockList) { _blockListScroll = GUILayout.BeginScrollView(_blockListScroll, false, true, GUILayout.Height(190f)); }
            if (_showBlockList)
            {
            bool headerDrawn = false;
            int lastGroup = -1;
            for (int i = 0; i < defs.Count; i++)
            {
                var def = defs[i];

                // 与 BlockLibrary 的地形、模型、_Lamp 三组排序一致。
                int group = def.IsLamp ? 2 : (def.interactive || def.previewPrefab != null ? 1 : 0);
                if (!headerDrawn || group != lastGroup)
                {
                    GUILayout.Space(4);
                    bool expanded = group == 2 ? _expandLamps : (group == 1 ? _expandProps : _expandStatic);
                    if (GUILayout.Button((expanded ? "▾ " : "▸ ") +
                        (group == 2 ? "灯具 _Lamp（发光方块）" :
                            (group == 1 ? "交互道具 / 模型（不烘进静态几何）" : "静态墙面（普通方块）")), GUILayout.Height(22)))
                    {
                        if (group == 2) _expandLamps = !_expandLamps;
                        else if (group == 1) _expandProps = !_expandProps;
                        else _expandStatic = !_expandStatic;
                    }
                    lastGroup = group;
                    headerDrawn = true;
                }
                // 该分组处于折叠状态 -> 跳过它的方块按钮
                if (!(group == 2 ? _expandLamps : (group == 1 ? _expandProps : _expandStatic)))
                {
                    lastGroup = group;
                    continue;
                }

                GUILayout.BeginHorizontal();
                GUI.color = (i == _selected) ? Color.yellow : Color.white;

                Rect iconRect = GUILayoutUtility.GetRect(28, 28, GUILayout.Width(28), GUILayout.Height(28));
                // 图标绘制单独 try/catch：素材有问题时只降级这一格图标，
                // 绝不能让异常冒出去 —— 之前 GetPixels32 抛异常导致整个面板全帧不绘制。
                try
                {
                    var tex = def.GetTexture();
                    if (tex != null)
                    {
                        if (def.UseAutoTile)
                        {
                            float ts = TileSizeOf(def);
                            GUI.DrawTextureWithTexCoords(iconRect, tex, AutoTileAtlas.TileRect(0, Mathf.RoundToInt(ts)));
                        }
                        else GUI.DrawTexture(iconRect, tex, ScaleMode.ScaleToFit);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[BlockMap] 方块图标绘制失败：" + def.id + " → " + e.Message, this);
                    GUI.Label(iconRect, "?");
                }

                if (GUILayout.Button($"{i + 1}. {ModeTag(def)}{def.displayName}", GUILayout.Height(28))) _selected = i;
                GUI.color = Color.white;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-")) brushSize = Mathf.Max(1, brushSize - 2);
            if (GUILayout.Button("+")) brushSize = Mathf.Min(5, brushSize + 2);
            GUILayout.EndHorizontal();

            eraseMode = GUILayout.Toggle(eraseMode, " 橡皮擦（左键删除）");

            }
            if (_showBlockList) GUILayout.EndScrollView();

            // ---- 放置朝向：不仅靠快捷键 Toast，面板中始终可见 ----
            GUILayout.BeginHorizontal();
            GUILayout.Label($"放置朝向：{DirNames[brushDir]}", GUILayout.Width(126));
            if (GUILayout.Button("左转", GUILayout.Width(54)))
            {
                brushDir = (brushDir + 3) & 3;
                Toast($"刷子朝向：{DirNames[brushDir]}");
            }
            if (GUILayout.Button("右转 (R)", GUILayout.Width(78)))
            {
                brushDir = (brushDir + 1) & 3;
                Toast($"刷子朝向：{DirNames[brushDir]}");
            }
            GUILayout.EndHorizontal();

            // ---- 高度锁 ----
            GUILayout.BeginHorizontal();
            lockHeight = GUILayout.Toggle(lockHeight, " 高度锁", GUILayout.Width(76));
            if (GUILayout.Button("-", GUILayout.Width(28))) lockedY--;
            GUILayout.Label($" y={lockedY}", GUILayout.Width(52));
            if (GUILayout.Button("+", GUILayout.Width(28))) lockedY++;
            GUILayout.EndHorizontal();
            lockedY = Mathf.Clamp(lockedY, 0, Mathf.Max(0, _world.maxSize.y - 1));

            GUILayout.BeginHorizontal();
            GUI.enabled = _world.CanUndo;
            if (GUILayout.Button("撤销")) _world.Undo();
            GUI.enabled = _world.CanRedo;
            if (GUILayout.Button("重做")) _world.Redo();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("清空画布")) NewMap();
            if (GUILayout.Button("保存")) Save();
            if (GUILayout.Button("读取")) Load();
            GUILayout.EndHorizontal();

            // ---- 地图列表（多地图：一个编辑器里可以管多张地图，每张 = 一个 json）----
            GUILayout.Label($"当前编辑：<b>{slotName}</b>（「保存」存的就是这张）", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.Label("新地图名字（填好点下面「新建」才生效）");
            _slotNameInput = GUILayout.TextField(_slotNameInput ?? "");

            string[] slots = GetMapSlots();
            if (slots.Length > 0)
            {
                            if (GUILayout.Button($"{(_showMapList ? "▾" : "▸")} 当前地图：{slotName}    共 {slots.Length} 张"))
                _showMapList = !_showMapList;
            if (_showMapList)
            {
                _mapListScroll = GUILayout.BeginScrollView(_mapListScroll, false, true, GUILayout.Height(150f));
                for (int i = 0; i < slots.Length; i++)
                {
                    string m = slots[i];
                    bool cur = m == slotName;
                    GUI.color = cur ? Color.yellow : Color.white;
                    if (GUILayout.Button((cur ? "▶ " : "   ") + m, GUILayout.Height(22))) SwitchMap(m);
                    GUI.color = Color.white;
                }
                GUILayout.EndScrollView();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("新建", GUILayout.Height(22))) MapNew();
                if (GUILayout.Button("重命名", GUILayout.Height(22))) MapRename();
                if (GUILayout.Button(_confirmDeleteMap ? "确认删除?" : "删除", GUILayout.Height(22))) MapDelete();
                GUILayout.EndHorizontal();
                if (_confirmDeleteMap && Time.realtimeSinceStartup > _confirmUntil) _confirmDeleteMap = false;
            }
            }
            GUILayout.Label("退出编辑：再按一次 Tab（或按钮）");
            if (GUILayout.Button("退出编辑模式")) SetEditorMode(false);

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (!_showPanel && GUILayout.Button("显示面板", GUILayout.Width(80))) _showPanel = true;

            GUI.Label(new Rect(8, Screen.height - 26, Screen.width - 16, 20),
                "左键放置 / Shift+左键删除 / R 转刷子 / Shift+R 转机关 / L 机关关联 / [ ] 换方块 / 右键转相机 / 滚轮缩放 / F5 保存 / F9 读取");
            if (Time.realtimeSinceStartup < _toastUntil)
                GUI.Label(new Rect(8, Screen.height - 48, Screen.width - 16, 20), _toast);
        }
    }
}
