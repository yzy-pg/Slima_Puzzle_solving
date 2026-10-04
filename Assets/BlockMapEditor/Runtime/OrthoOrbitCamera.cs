using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 正交相机控制：右键拖拽旋转 / 中键拖拽（或 Alt+左键）平移 / 滚轮缩放 / 1~4 切视角预设 / P 切像素完美。
    /// 视角完全可调，且始终是正交投影。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class OrthoOrbitCamera : MonoBehaviour
    {
        public Vector3 target = Vector3.zero;
        public float yaw = 45f;
        public float pitch = 35f;

        [Header("视野")]
        [Tooltip("正交尺寸：数值越小越拉近。20 大约能看到画布中心 35 格高的一块")]
        public float size = 20f;
        [Tooltip("启动时把视野对到 worldCenter（画布中心），而不是塞下整张画布")]
        public bool centerOnStart = true;
        public float minSize = 1.5f, maxSize = 120f;
        public float rotateSpeed = 0.25f;
        public float zoomStep = 0.12f;

        [Header("像素完美（正交像素风必须）")]
        public bool pixelPerfect = true;
        [Tooltip("一张贴图代表多少像素（方块贴图 16 或 64）")]
        public int pixelsPerUnit = 16;

        [Header("固定机位（编辑场景用）")]
        [Tooltip("勾上后相机位置固定为 worldCenter + fixedOffset，不允许旋转（仍可滚轮缩放/平移）")]
        public bool fixedPosition = false;
        [Tooltip("机位相对世界中心的偏移。默认 (0, -75, 0) = 位于世界中心正下方 75")]
        public Vector3 fixedOffset = new Vector3(0f, -75f, 0f);
        public Vector3 worldCenter = new Vector3(128f, 18f, 128f);

        [Header("启动强制对齐（会覆盖 Inspector 里的旧值 —— 老场景改了不生效就是被序列化值顶掉了）")]
        [Tooltip("启动直接切正俯视：相机移到世界中心正上方、垂直往下看")]
        public bool forceTopDownOnStart = true;
        public float topDownHeight = 75f;
        [Tooltip("启动强制这个正交尺寸（≤0 表示不强制）。20 约等于屏幕可见 40m x 40m")]
        public float forceSizeOnStart = 20f;
        [Tooltip("俯视倾角（度）：从垂直方向往下偏移多少。\n0 = 正俯视（只看到顶面）；34 ≈ 同时看到顶面和一个侧面（推荐）；45 = 更立体")]
        public float topDownTiltDegrees = 34f;

        /// <summary>相机正在旋转/平移时，地图编辑器应当忽略鼠标操作。</summary>
        public static bool Busy { get; private set; }

        /// <summary>是否接受输入。退出编辑模式后置 false，相机交给你自己的游戏逻辑控制。</summary>
        public static bool InputEnabled = true;

        Camera _cam;
        Vector3 _lastMouse;

        void Awake()
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;

            // ★ 强制对齐：直接覆盖序列化值，避免"改了没生效"（Unity 里 Inspector 的旧值优先于代码默认值）
            if (forceTopDownOnStart)
            {
                fixedPosition = true;                                  // 锁死俯视机位
                // 只沿 Z 方向偏移 → 网格在屏幕上是横平竖直的（不会左右歪），
                // 但上下方向带角度，于是能同时看到顶面和朝向镜头的那一个侧面。
                float tilt = topDownHeight * Mathf.Tan(topDownTiltDegrees * Mathf.Deg2Rad);
                fixedOffset = new Vector3(0f, topDownHeight, -tilt);
            }
            if (forceSizeOnStart > 0f) size = forceSizeOnStart;
            if (centerOnStart) target = worldCenter;                    // 视野中心 = 画布中心

            Apply();
        }

        void Update()
        {
            Busy = false;
            if (!InputEnabled) return;

            // 旋转（固定机位模式下禁止旋转）
            if (!fixedPosition && Input.GetMouseButtonDown(1)) _lastMouse = Input.mousePosition;
            if (!fixedPosition && Input.GetMouseButton(1))
            {
                Busy = true;
                Vector3 d = Input.mousePosition - _lastMouse;
                _lastMouse = Input.mousePosition;
                yaw += d.x * rotateSpeed;
                pitch = Mathf.Clamp(pitch - d.y * rotateSpeed, -89f, 89f);
                Apply();
            }

            // 平移
            bool pan = Input.GetMouseButton(2) || (Input.GetKey(KeyCode.LeftAlt) && Input.GetMouseButton(0));
            if (pan)
            {
                Busy = true;
                if (Input.GetMouseButtonDown(2) || Input.GetMouseButtonDown(0)) _lastMouse = Input.mousePosition;
                Vector3 d = Input.mousePosition - _lastMouse;
                _lastMouse = Input.mousePosition;
                float worldPerPixel = size * 2f / Mathf.Max(1, Screen.height);
                // 固定机位（俯视）下平移的是"视野中心"，这样俯视也能拖拽着地图走
                if (fixedPosition)
                {
                    worldCenter -= transform.right * (d.x * worldPerPixel);
                    worldCenter -= transform.up * (d.y * worldPerPixel);
                }
                else
                {
                    target -= transform.right * (d.x * worldPerPixel);
                    target -= transform.up * (d.y * worldPerPixel);
                }
                Apply();
            }

            // 缩放
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                size = Mathf.Clamp(size * Mathf.Exp(-scroll * 3f), minSize, maxSize);
                Apply();
            }

            // 视角预设
            if (Input.GetKeyDown(KeyCode.Alpha1)) { yaw = 45f; pitch = 35f; Apply(); }   // 等轴（Minecraft 默认）
            if (Input.GetKeyDown(KeyCode.Alpha2)) { yaw = 0f; pitch = 89.9f; Apply(); }  // 俯视
            if (Input.GetKeyDown(KeyCode.Alpha3)) { yaw = 0f; pitch = 0f; Apply(); }     // 正前
            if (Input.GetKeyDown(KeyCode.Alpha4)) { yaw = 90f; pitch = 0f; Apply(); }    // 正侧
            if (Input.GetKeyDown(KeyCode.P)) { pixelPerfect = !pixelPerfect; Apply(); }

            // WASD 平移
            float speed = size * 0.8f * Time.deltaTime;
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) move -= Vector3.forward;
            if (Input.GetKey(KeyCode.A)) move -= Vector3.right;
            if (Input.GetKey(KeyCode.D)) move += Vector3.right;
            if (move != Vector3.zero)
            {
                Vector3 flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, flat).normalized;
                target += (flat * move.z + right * move.x) * speed;
                Apply();
            }
        }

        public void Apply()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            _cam.orthographic = true;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 1000f;
            _cam.orthographicSize = pixelPerfect ? SnapSize(size) : size;

            if (fixedPosition)
            {
                // 固定机位：位置 = 世界中心 + 偏移，朝向始终看向世界中心
                transform.position = worldCenter + fixedOffset;
                Vector3 dir = worldCenter - transform.position;
                // 显式指定 up = 世界 Y：保证相机不产生"左右滚转"，
                // 倾斜只会体现在屏幕的上下方向 —— 这正是斜俯视想要的效果。
                if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                return;
            }

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rot;
            transform.position = target - rot * Vector3.forward * 300f;
        }

        /// <summary>把正交尺寸吸附成"1 贴图像素 = 整数屏幕像素"。</summary>
        float SnapSize(float raw)
        {
            float ppuPerWorld = Screen.height / (2f * raw);        // 每世界单位多少屏幕像素
            float zoom = Mathf.Max(1f, Mathf.Round(ppuPerWorld / Mathf.Max(1, pixelsPerUnit)));
            return Screen.height / (2f * zoom * Mathf.Max(1, pixelsPerUnit));
        }

        public void Focus(Vector3 worldPos, float newSize = -1f)
        {
            target = worldPos;
            if (newSize > 0f) size = newSize;
            Apply();
        }
    }
}
