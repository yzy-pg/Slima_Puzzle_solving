using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 关卡角色：**3D 碰撞 + 格子跳**。
    ///
    /// 位移和旋转都是定死的：
    ///   · 只能是 4 个世界方向（+X / -X / +Z / -Z），一次正好走 1 格；
    ///   · 朝向只取 0° / 90° / 180° / 270°，按下方向键立刻转到位，没有自由旋转、没有摇杆角度；
    ///   · 走的过程是一小段"跳"（视觉子物体走一个弧线），落地正好压在格心上。
    ///
    /// 但"能不能走过去"是真的用物理碰撞判定的：CharacterController 撞到墙走不满一格，
    /// 就直接退回原格 —— 所以烘出来的地形必须带碰撞体（菜单 ② 现在默认会生成）。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class BlockMapGridWalker : MonoBehaviour
    {
        [Header("格子")]
        [Tooltip("一格的世界尺寸，和烘焙时的 blockSize 保持一致")]
        public float cellSize = 1f;

        [Tooltip("走一格用多长时间（秒）")]
        public float stepDuration = 0.16f;

        [Tooltip("跳起来的弧线高度（视觉，不影响碰撞）")]
        public float hopHeight = 0.14f;

        [Header("重力")]
        public float gravity = 20f;

        [Tooltip("掉到这个高度以下视为掉出地图，自动回到起点")]
        public float fallLimit = -8f;

        [Header("视角")]
        [Tooltip("和 BlockMapGameRoot.cameraYaw 保持一致。位移方向会随视角旋转（0/90/180/270），" +
                 "这样 W 永远朝屏幕上方走；仍然是固定的 4 个方向、90° 档位，不会变成自由朝向。")]
        public float viewYaw = 0f;

        [Header("外观")]
        [Tooltip("真正渲染的那个子物体（走弧线动画用）。留空自动找第一个子物体")]
        public Transform visual;

        public Vector3Int Cell { get; private set; }
        public bool IsStepping { get; private set; }
        public int Facing { get; private set; }                 // 0=+Z 1=+X 2=-Z 3=-X

        /// <summary>走完一格时触发（参数是落地后的格子）。</summary>
        public event System.Action<Vector3Int> Stepped;

        /// <summary>按 R 之类的重置：直接瞬移到某格。</summary>
        public void Teleport(Vector3Int cell, int facing)
        {
            Cell = cell;
            Facing = ((facing % 4) + 4) % 4;
            IsStepping = false;
            transform.position = CenterOf(cell);
            transform.rotation = Quaternion.Euler(0f, Facing * 90f, 0f);
            if (visual != null) visual.localPosition = Vector3.zero;
            _movedHorizontally = 0f;
        }

        Vector3 _stepStart;
        Vector3 _stepTarget;
        float _progress;
        float _movedHorizontally;

        void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        }

        void Update()
        {
            if (transform.position.y < fallLimit)
            {
                Teleport(Cell, Facing);
                return;
            }

            if (IsStepping) AdvanceStep();
            else TryStartStep();

            // 一直施加重力：地形不平也能贴住地面
            GetComponent<CharacterController>().Move(Vector3.down * gravity * Time.deltaTime);
        }

        void TryStartStep()
        {
            int direction = ReadDirection();
            if (direction < 0) return;

            // 屏幕方向 → 世界方向：视角转过 90° 的整数倍时，前后左右跟着转
            int yawSteps = Mathf.RoundToInt(viewYaw / 90f) & 3;
            direction = (direction + yawSteps) & 3;

            Vector3Int next = Cell + (direction == 0 ? Vector3Int.forward
                                : direction == 1 ? Vector3Int.right
                                : direction == 2 ? Vector3Int.back
                                : Vector3Int.left);

            Facing = direction;
            transform.rotation = Quaternion.Euler(0f, Facing * 90f, 0f);   // 旋转先到位，再起跳

            _stepStart = transform.position;
            _stepTarget = CenterOf(next);
            _progress = 0f;
            _movedHorizontally = 0f;
            IsStepping = true;
        }

        void AdvanceStep()
        {
            _progress += Time.deltaTime / Mathf.Max(0.01f, stepDuration);
            float t = Mathf.Clamp01(_progress);

            Vector3 want = Vector3.Lerp(_stepStart, _stepTarget, t);
            Vector3 before = transform.position;
            GetComponent<CharacterController>().Move(want - transform.position);   // 返回的是碰撞标志，位移要自己算
            Vector3 moved = transform.position - before;
            _movedHorizontally += new Vector2(moved.x, moved.z).magnitude;

            if (visual != null)
            {
                visual.localPosition = new Vector3(0f, Mathf.Sin(t * Mathf.PI) * hopHeight, 0f);
            }

            if (t < 1f) return;

            // 走不满一格 = 被墙挡住了 → 退回原格（保持格心对齐）
            if (_movedHorizontally < cellSize * 0.8f)
            {
                transform.position = _stepStart;
                if (visual != null) visual.localPosition = Vector3.zero;
                IsStepping = false;
                return;
            }

            transform.position = _stepTarget;
            if (visual != null) visual.localPosition = Vector3.zero;
            Cell = CellOf(_stepTarget);
            IsStepping = false;
            Stepped?.Invoke(Cell);
        }

        /// <summary>0=+Z 1=+X 2=-Z 3=-X，没人按返回 -1。方向与相机无关，永远是世界的四个正方向。</summary>
        static int ReadDirection()
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) return 0;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) return 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) return 2;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) return 3;
            return -1;
        }

        public Vector3 CenterOf(Vector3Int cell) =>
            new Vector3(cell.x + 0.5f, cell.y, cell.z + 0.5f);

        public static Vector3Int CellOf(Vector3 position) =>
            new Vector3Int(Mathf.FloorToInt(position.x), Mathf.RoundToInt(position.y), Mathf.FloorToInt(position.z));
    }
}
