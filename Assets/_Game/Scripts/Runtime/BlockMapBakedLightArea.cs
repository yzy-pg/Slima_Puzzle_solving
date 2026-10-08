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

        [Header("风格化（和 toon shader 一致）")]
        [Tooltip("像素噪波强度：把阶梯边界打散成像素颗粒")]
        [Range(0f, 1f)] public float pixelNoise = 0.35f;

        [Tooltip("噪波格子大小（世界单位，约等于几个屏幕像素）")]
        [Range(0.005f, 0.5f)] public float noiseCell = 0.06f;

        [Tooltip("阶梯层数（5 = 五层阶梯式光运算）")]
        [Range(2, 8)] public int bands = 5;

        [Tooltip("高饱和过渡：在 40%~70% 的过渡带里提高颜色饱和度（0 = 关）")]
        [Range(0f, 1f)] public float saturationBoost = 0.3f;

        [Tooltip("散射范围（格）：向外扩多少格还受光（0 = 严格按矩形）")]
        [Range(0f, 8f)] public float scatter = 0f;

        [Tooltip("边缘柔和度：0 = 硬边（到边界骤停）；1 = 从边界平滑过渡到中心")]
        [Range(0f, 1f)] public float edgeSoftness = 0.25f;

        [Tooltip("沿光程衰减：0 = 一路同亮；1 = 到射出末端衰减到 0")]
        [Range(0f, 1f)] public float beamFalloff = 0f;

        /// <summary>
        /// 一次算出某处的最终"提亮值"和"颜色"：面内渐变 → 光程衰减 → 散射 → 像素噪波 → 五层阶梯 → 高饱和过渡。
        /// </summary>
        public Color CellLight(Vector3 worldPos, Vector2 faceCells, float beamT, out float levelOut)
        {
            // ① 面内渐变（含散射扩大范围）
            Vector2 half = RectSize * 0.5f + new Vector2(scatter, scatter);
            float ex = 1f - Mathf.Clamp01(Mathf.Abs(faceCells.x) / Mathf.Max(0.5f, half.x));
            float ey = 1f - Mathf.Clamp01(Mathf.Abs(faceCells.y) / Mathf.Max(0.5f, half.y));
            float edge = Mathf.Min(ex, ey);
            if (edge <= 0f) { levelOut = 0f; return Color.black; }

            float t = edgeSoftness > 0.001f ? Mathf.Clamp01(edge / edgeSoftness) : 1f;
            float level = brightness.Evaluate(t).grayscale * intensity;

            // ② 沿光程衰减
            level *= Mathf.Lerp(1f, 1f - Mathf.Clamp01(beamT), beamFalloff);

            // ③ 像素噪波（世界空间的固定格子 → 锁在表面上，不随镜头滑动）
            float cell = Mathf.Max(0.005f, noiseCell);
            float n = Hash21(new Vector2(Mathf.Floor(worldPos.x / cell), Mathf.Floor(worldPos.z / cell)));
            level = Mathf.Clamp01(level + (n - 0.5f) * pixelNoise / Mathf.Max(2f, bands));

            // ④ 五层阶梯式光运算
            int b = Mathf.Max(2, bands);
            level = Mathf.Floor(level * b) / (b - 1f);
            level = Mathf.Clamp01(level);

            // ⑤ 高饱和过渡（40% ~ 70% 带内提饱和）
            float tt = Mathf.Clamp01((level - 0.4f) / 0.3f);
            float sat = (1f - tt) * saturationBoost;
            Color c = brightness.Evaluate(level);
            float lum = c.grayscale;
            c = Color.Lerp(new Color(lum, lum, lum, 1f), c, 1f + sat);

            levelOut = level;
            return c;
        }

        static float Hash21(Vector2 p)
        {
            p = new Vector2(p.x * 123.34f, p.y * 456.21f);
            p = new Vector2(p.x - Mathf.Floor(p.x), p.y - Mathf.Floor(p.y));
            float d = Vector2.Dot(p, new Vector2(p.x + 45.32f, p.y + 45.32f));
            p += new Vector2(d, d);
            return (p.x * p.y) - Mathf.Floor(p.x * p.y);
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

            // ★ 有方向的"面光"：Spot 放在发光面上，朝向 = 发光体的发射方向（本地 +Z）。
            //   改方向只需要旋转发光体本身；锥角按矩形尺寸/长度换算，接近"直射出去的一个柱"。
            Vector2 rect = RectSize;
            float len = Mathf.Max(1f, BeamLength);
            float halfDiag = Mathf.Max(rect.x, rect.y) * 0.5f;
            float coneDeg = 2f * Mathf.Atan2(halfDiag, len) * Mathf.Rad2Deg;

            light.type = LightType.Spot;
            light.color = brightness.Evaluate(1f);          // 用渐变最亮那一端的颜色
            light.intensity = previewIntensity;
            light.range = len;                              // 范围 = 发射长度
            light.spotAngle = Mathf.Clamp(coneDeg, 1f, 179f);
            light.innerSpotAngle = Mathf.Clamp(coneDeg * 0.5f, 0.5f, 178f);
            light.transform.localPosition = Vector3.zero;
            light.transform.localRotation = Quaternion.identity;   // ★ 指向发射方向（本地 +Z）
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
