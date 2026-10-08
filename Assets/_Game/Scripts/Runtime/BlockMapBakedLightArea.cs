using UnityEngine;

namespace BlockMap.Tools
{
    /// <summary>
    /// 烘焙用「特殊发光体」——**不参与任何实时光照**（本身不带 Light 组件）。
    ///
    /// 它是一块**矩形范围 + 颜色渐变**：用 Tools 里的烘焙菜单把范围烤进方块贴图
    /// （把范围内的方块换成它的 `_Lit` 提亮版），并用 Bayer 抖动把它做成**像素格**密度，
    /// 于是渐变 = 像素颗粒的疏密，零实时开销、和全屏像素化也不打架。
    ///
    /// 注意：这是"工具"，不是 BlockMap 的方块/特殊物品 —— 它不进入方块库，只在场景里摆一个空物体挂它。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("BlockMap Tools/烘焙发光体（矩形渐变，像素格）")]
    public class BlockMapBakedLightArea : MonoBehaviour
    {
        [Tooltip("矩形范围：X = 宽（格），Y = 深（格）。以本物体的 XZ 位置为中心")]
        public Vector2 size = new Vector2(12f, 12f);

        [Tooltip("高度范围（格）：只烤这个高度区间内的方块。默认只烤一层")]
        public Vector2Int heightRange = new Vector2Int(0, 0);

        [Tooltip("颜色渐变：左端 = 范围边缘，右端 = 范围中心。\n" +
                 "它的**明度**决定该处的点亮强度（像素格密度）；颜色请配合下面生成的 _Lit 色系使用")]
        public Gradient brightness = DefaultGradient();

        [Tooltip("整体强度 0~2：乘在渐变明度上。1 = 完全按渐变")]
        [Range(0f, 2f)] public float intensity = 1f;

        [Tooltip("像素格大小（格）：1 = 逐格抖动（最细）；2 = 2×2 格一组（更粗的像素块）")]
        [Range(1, 8)] public int ditherBlock = 1;

        [Tooltip("点亮后使用的方块后缀（对应提亮版方块，例如 Lab_Floor_07 → Lab_Floor_07_Lit）")]
        public string litSuffix = "_Lit";

        [Tooltip("按面积衰减：勾上 = 方形范围内的面积光（推荐）；不勾 = 只看到中心的距离（圆形）")]
        public bool rectangularFalloff = true;

        static Gradient DefaultGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.25f, 0.25f, 0.25f), 0f),
                    new GradientColorKey(new Color(1f, 1f, 1f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        /// <summary>某点(相对中心，格)的归一化"亮度" 0~1。</summary>
        public float Evaluate(Vector2 offsetCells, int cellX, int cellZ)
        {
            float w = Mathf.Max(1f, size.x) * 0.5f;
            float d = Mathf.Max(1f, size.y) * 0.5f;

            float t;
            if (rectangularFalloff)
            {
                // 到矩形边的归一化距离：边缘 0，中心 1
                float ex = 1f - Mathf.Clamp01(Mathf.Abs(offsetCells.x) / w);
                float ez = 1f - Mathf.Clamp01(Mathf.Abs(offsetCells.y) / d);
                t = Mathf.Min(ex, ez);
            }
            else
            {
                float r = Mathf.Sqrt(offsetCells.x * offsetCells.x + offsetCells.y * offsetCells.y);
                t = 1f - Mathf.Clamp01(r / Mathf.Max(w, d));
            }

            float lum = brightness.Evaluate(t).grayscale;   // 用渐变的明度当强度
            return Mathf.Clamp01(lum * intensity);
        }

        /// <summary>该格子是否被点亮（Bayer 4×4 抖动 → 像素颗粒疏密）。</summary>
        public bool ShouldLight(int cellX, int cellZ, float level01)
        {
            int bx = Mathf.FloorToInt((float)cellX / ditherBlock);
            int bz = Mathf.FloorToInt((float)cellZ / ditherBlock);
            float threshold = (Bayer4[bz & 3, bx & 3] + 0.5f) / 16f;
            return level01 > threshold;
        }

        static readonly int[,] Bayer4 = new int[4, 4]
        {
            {  0,  8,  2, 10 },
            { 12,  4, 14,  6 },
            {  3, 11,  1,  9 },
            { 15,  7, 13,  5 },
        };

        // ------------------------------------------------------------------
        // 场景里可见（否则空物体在 Scene 视图里完全看不到、也点不中）
        // ------------------------------------------------------------------
        void OnDrawGizmos()
        {
            float w = Mathf.Max(1f, size.x);
            float d = Mathf.Max(1f, size.y);
            float y = (heightRange.x + heightRange.y) * 0.5f + 0.5f;
            Vector3 basePos = transform.position;
            Vector3 center = new Vector3(basePos.x, basePos.y + y, basePos.z);

            // ① 矩形线框
            Gizmos.color = new Color(1f, 0.82f, 0.35f, 0.95f);
            Gizmos.DrawWireCube(center, new Vector3(w, 0.05f, d));

            int halfW = Mathf.CeilToInt(w * 0.5f);
            int halfD = Mathf.CeilToInt(d * 0.5f);

            // ② 四角立柱（高度区间看得见）
            float y0 = basePos.y + heightRange.x;
            float y1 = basePos.y + heightRange.y + 1f;
            Gizmos.color = new Color(1f, 0.82f, 0.35f, 0.45f);
            foreach (Vector2 corner in new[]
            {
                new Vector2(-halfW, -halfD), new Vector2(halfW, -halfD),
                new Vector2(-halfW,  halfD), new Vector2(halfW,  halfD),
            })
            {
                var a = new Vector3(basePos.x + corner.x, y0, basePos.z + corner.y);
                var b = new Vector3(basePos.x + corner.x, y1, basePos.z + corner.y);
                Gizmos.DrawLine(a, b);
            }

            // ③ 会被点亮的格子（按渐变上色）—— 直接看到"像素格提亮"的样子
            int dx0 = Mathf.RoundToInt(basePos.x) - halfW;
            int dz0 = Mathf.RoundToInt(basePos.z) - halfD;
            for (int dx = -halfW; dx <= halfW; dx++)
            {
                for (int dz = -halfD; dz <= halfD; dz++)
                {
                    var offset = new Vector2(dx, dz);
                    int cellX = Mathf.RoundToInt(basePos.x) + dx;
                    int cellZ = Mathf.RoundToInt(basePos.z) + dz;
                    float level = Evaluate(offset, cellX, cellZ);
                    if (!ShouldLight(cellX, cellZ, level)) continue;

                    Color c = brightness.Evaluate(level);
                    c.a = 0.35f;
                    Gizmos.color = c;
                    Gizmos.DrawCube(new Vector3(cellX, basePos.y + y, cellZ), new Vector3(0.92f, 0.02f, 0.92f));
                }
            }
        }

        void OnValidate()
        {
            size.x = Mathf.Max(1f, size.x);
            size.y = Mathf.Max(1f, size.y);
            ditherBlock = Mathf.Max(1, ditherBlock);
            if (heightRange.y < heightRange.x) heightRange.y = heightRange.x;

            // 同步触发碰撞体（Scene 里可点击选中）；只影响编辑器拾取
            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                box.isTrigger = true;
                box.center = new Vector3(0f, (heightRange.x + heightRange.y) * 0.5f + 0.5f, 0f);
                box.size = new Vector3(size.x, Mathf.Max(0.1f, heightRange.y - heightRange.x + 1f), size.y);
            }
        }

        /// <summary>运行时不参与物理：关掉碰撞体（编辑器里仍可点选）。</summary>
        void OnEnable()
        {
            var box = GetComponent<Collider>();
            if (box != null) box.enabled = false;
        }
    }
}
