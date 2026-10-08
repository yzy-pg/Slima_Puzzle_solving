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
        [Tooltip("矩形范围 = 本物体 Transform 的 X/Y 缩放（用 Scale 工具拖即可）；射出长度 = Z 缩放")]
        [Header("范围（看 Transform 的 Scale，不用改这里）")]
        public Vector2 baseSizeHint = new Vector2(12f, 12f);

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

        /// <summary>发光面尺寸（用 Transform 缩放表达）。</summary>
        public Vector2 RectSize => new Vector2(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));

        /// <summary>射出长度（沿本地 +Z）。</summary>
        public float BeamLength => Mathf.Abs(transform.lossyScale.z);

        /// <summary>某点在"发光面"上的归一化亮度 0~1（面内坐标，单位 = 格）。</summary>
        public float EvaluateFace(Vector2 faceCells)
        {
            Vector2 half = RectSize * 0.5f;
            float ex = 1f - Mathf.Clamp01(Mathf.Abs(faceCells.x) / Mathf.Max(0.5f, half.x));
            float ey = 1f - Mathf.Clamp01(Mathf.Abs(faceCells.y) / Mathf.Max(0.5f, half.y));
            float t = Mathf.Min(ex, ey);                       // 面边缘 0，面中心 1

            float lum = brightness.Evaluate(t).grayscale;
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
        // 场景里可见：**一个面 + 一束直线光**（面光），用物体自身坐标画 → 跟随旋转和缩放
        // ------------------------------------------------------------------
        void OnDrawGizmos()
        {
            float len = Mathf.Max(0.1f, BeamLength);
            float w = Mathf.Max(0.1f, RectSize.x);
            float h = Mathf.Max(0.1f, RectSize.y);

            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;      // ★ 用本地坐标 → 自动跟随旋转 / 缩放

            // 光束体积（矩形面沿 +Z 直射出去的"盒子"）
            Gizmos.color = new Color(1f, 0.82f, 0.35f, 0.85f);
            Gizmos.DrawWireCube(new Vector3(0f, 0f, len * 0.5f), new Vector3(w, h, len));

            // 发光面（亮一点，一眼看出"从哪面射出去"）
            Gizmos.color = new Color(1f, 0.92f, 0.6f, 0.95f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(w, h, 0.02f));

            // 中心射线（方向指示）
            Gizmos.color = new Color(1f, 0.65f, 0.2f, 0.95f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, 0f, len));

            Gizmos.matrix = old;
        }

        [Header("编辑器预览灯（只在编辑时亮，Play 时自动关闭）")]
        [Tooltip("勾上 = 用一盏真实点光在编辑器里实时预览效果（你能直接看到范围）\n" +
                 "★ Play 时会被自动关闭：不参与任何运行时灯光计算")]
        public bool editorPreviewLight = true;

        [Tooltip("预览灯强度（编辑器里看的，与烘焙无关）")]
        [Range(0.1f, 8f)] public float previewIntensity = 2f;

        /// <summary>按当前设置同步那盏"编辑器预览灯"（菜单新建后也会调它）。</summary>
        public void SyncPreviewLight()
        {
            Light light = GetComponentInChildren<Light>(true);

            if (!editorPreviewLight)
            {
                if (light != null) light.enabled = false;
                return;
            }

            if (light == null)
            {
                var go = new GameObject("PreviewLight");
                go.transform.SetParent(transform, false);
                light = go.AddComponent<Light>();
            }

            light.type = LightType.Point;
            light.color = brightness.Evaluate(1f);          // 用渐变最亮那一端的颜色
            light.intensity = previewIntensity;
            light.range = Mathf.Max(1f, BeamLength);        // 范围 = 发射长度
            light.transform.localPosition = Vector3.zero;   // 灯就在发光体中心
            light.shadows = LightShadows.None;
            light.enabled = true;
        }

        void OnValidate()
        {
            ditherBlock = Mathf.Max(1, ditherBlock);
            SyncPreviewLight();
        }

        // ★ 运行时：关掉预览灯 —— 发光体在 Play 里不参与任何光照计算
        void Awake() => DisablePreviewLights();
        void OnEnable() => DisablePreviewLights();

        void DisablePreviewLights()
        {
            foreach (Light l in GetComponentsInChildren<Light>(true)) l.enabled = false;
        }
    }
}
