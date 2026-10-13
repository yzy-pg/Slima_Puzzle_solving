using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 推拉门：关闭时保持模型原位，打开时向本地右侧滑动并隐藏门板。
    ///
    /// ── 使用格式（三步）──────────────────────────────────────────
    /// 1. 把门做成 Prefab，丢进  Assets/任意/Resources/BlockModels/Door.prefab
    ///    （文件名 = id；编辑器会自动把它识别成特殊物件，**不用建 .asset**）
    /// 2. 在 Prefab 根节点挂本组件。
    ///    指定 hinge 作为移动门板；字段名保留以兼容已有场景。
    /// 3. 地图编辑器里：刷上去（占位方块）→ **左键单击 = 原地转 90°**（调朝向）→ F5 存档
    ///
    /// ── 运行时数据流 ─────────────────────────────────────────────
    ///   BlockMapRuntimeSpawner 读 BlockWorld 里每格的「坐标 + 朝向」：
    ///     Instantiate(prefab, 格心, Quaternion.Euler(0, 朝向*90, 0) * prefab原朝向)
    ///   所以编辑器里摆的位置和朝向，运行时会一模一样地还原。
    ///
    /// ── 朝向约定 ─────────────────────────────────────────────────
    ///   0 = +Z(前) / 1 = +X(右) / 2 = −Z(后) / 3 = −X(左)，顺时针。
    ///   门"朝哪边"= prefab 的 +Z 面朝哪边。
    /// </summary>
    public class BlockMapDoor : MonoBehaviour
    {
        [Header("开门")]
        [HideInInspector] public float openAngle = 90f; // Legacy serialized field; sliding doors do not rotate.
        [Min(0.01f), Tooltip("门板向自身右侧滑动的距离")]
        public float slideDistance = 1.05f;
        [Tooltip("开/关一次用多少秒")]
        public float duration = 0.35f;
        [Tooltip("移动门板（保留旧 hinge 字段名以兼容场景）")] public Transform hinge;

        [Header("触发方式")]
        [Tooltip("按这个键开关（玩家在附近才响应；None = 不响应按键）")]
        public KeyCode toggleKey = KeyCode.E;
        [Tooltip("玩家进入这个距离内按键才有效；0 = 不限制距离")]
        public float interactDistance = 2.5f;
        [Tooltip("是否也允许鼠标点击开关（Play 模式下、地图编辑器关闭时可用）")]
        public bool clickToToggle = true;

        bool _open;
        float _t;                  // 0 = 全关，1 = 全开
        Vector3 _closedPosition;
        Quaternion _closedRotation;
        Renderer[] _renderers;
        bool[] _rendererVisibility;
        bool _initialized;

        void Awake()
        {
            InitializePose();
        }

        void Start() => AdvanceAnimation(0f);

        public void SetHinge(Transform target)
        {
            hinge = target;
            _initialized = false;
            AdvanceAnimation(0f);
        }

        void InitializePose()
        {
            if (_initialized) return;
            Transform panel = hinge != null ? hinge : transform;
            _closedPosition = panel.localPosition;
            _closedRotation = panel.localRotation;
            _renderers = panel.GetComponentsInChildren<Renderer>(true);
            _rendererVisibility = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++) _rendererVisibility[i] = _renderers[i].enabled;
            _initialized = true;
        }

        void Update()
        {
            AdvanceAnimation(Time.deltaTime);

            if (toggleKey == KeyCode.None) return;
            if (!Input.GetKeyDown(toggleKey)) return;

            if (interactDistance > 0f)
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                if (Vector3.Distance(cam.transform.position, transform.position) > interactDistance) return;
            }
            Toggle();
        }

        public void AdvanceAnimation(float deltaTime)
        {
            InitializePose();
            float target = _open ? 1f : 0f;
            if (!Mathf.Approximately(_t, target))
            {
                _t = Mathf.MoveTowards(_t, target, deltaTime / Mathf.Max(0.01f, duration));
            }
            Transform panel = hinge != null ? hinge : transform;
            panel.localPosition = _closedPosition + _closedRotation * Vector3.right *
                (slideDistance * Mathf.SmoothStep(0f, 1f, _t));
            panel.localRotation = _closedRotation;
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = _rendererVisibility[i] && (!_open || _t < 1f);
        }

        void OnMouseDown()
        {
            if (clickToToggle) Toggle();     // 地图编辑器会吃掉鼠标单击，所以平时用上面的按键
        }

        // ── 对外接口：其它脚本 / 触发器 / 按钮都可以直接调 ──

        /// <summary>开关一次</summary>
        public void Toggle() => SetOpen(!_open);
        public void Open() => SetOpen(true);
        public void Close() => SetOpen(false);

        /// <summary>整体原地转 90°（改的是"朝向"，不是开门）—— 想单击转向就挂 BlockMapRotateOnClick</summary>
        public void Rotate90()
        {
            InitializePose();
            _closedRotation = Quaternion.Euler(0f, 90f, 0f) * _closedRotation;
            AdvanceAnimation(0f);
        }

        /// <summary>当前是否开着</summary>
        public bool IsOpen => _open;

        public void SetOpen(bool open)
        {
            _open = open;
            AdvanceAnimation(0f);
        }
    }
}
