using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlimaPuzzle.Rendering
{
    /// <summary>
    /// 全屏「像素化 + 黑描边」的 URP Renderer Feature。
    ///
    /// 装法：URP Asset → Renderer List → 你的 Renderer（URP-Balanced-Renderer / HighFidelity…）
    ///       → Add Renderer Feature → Slima Toon Outline（Shader 留空会自动找 `Slima/Toon Outline Post`）。
    ///
    /// 为什么描边放在全屏做、而不是用物体的反转壳：
    ///   本项目的方块地形是"每个 chunk 一个合并网格、每个面的顶点独立"，
    ///   反转壳沿法线外扩会在每个方块棱上裂开；屏幕空间对深度做 Sobel 只会沿真正的轮廓/直角棱出线。
    /// </summary>
    public sealed class SlimaToonOutlineFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public sealed class Settings
        {
            [Tooltip("留空会自动找「Slima/Toon Outline Post」")]
            public Shader shader;

            [Tooltip("插在哪个阶段。默认：透明物体之后、后处理之前")]
            public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            [Header("像素化")]
            [Tooltip("像素块大小（屏幕像素）。1 = 不像素化。\n" +
                     "⚠ 它会和物体材质里的「像素化｜顶点像素吸附」叠加，两个都开就会非常糊；\n" +
                     "建议只留一层：要么只用这里，要么只用 URP Asset 的 Render Scale + Point。")]
            [Range(1f, 16f)] public float pixelSize = 2f;

            [Tooltip("0 = 关闭像素化，只描边")]
            [Range(0f, 1f)] public float pixelateAmount = 1f;

            [Header("描边")]
            public Color outlineColor = Color.black;

            [Tooltip("★ 全屏描边粗细（像素块）。屏幕上的实际宽度 ≈ 这个值 × Pixel Size。\n" +
                     "⚠ 材质里那个「描边｜粗细」是物体的反转壳描边（给角色用），调它不会影响全屏这条线，地形要设 0。")]
            [Range(0f, 16f)] public float outlineThickness = 2f;

            [Tooltip("调小 = 出线更多、线也更粗；调大 = 只剩最明显的轮廓。\n" +
                     "⚠ 正交相机下「一格方块」的深度落差 ≈ 1 世界单位 —— 阈值给 3 以上就是"
                     + "「落差要超过 3 格才算出边」，普通方块棱会全部消失。建议 0.3 ~ 0.8。")]
            [Range(0.05f, 20f)] public float depthThreshold = 0.5f;

            [Header("调试")]
            [Tooltip("0 = 正常。\n" +
                     "1 = 把深度画成灰度（白=近）：如果整屏一片纯色，说明深度纹理没拿到，描边永远不会出线。\n" +
                     "2 = 把「落差/阈值」画成红色：红色区域就是会被描边的像素；调 Outline Thickness 时"
                     + "这块红色区域应该跟着变宽。")]
            [Range(0, 2)] public int debugMode = 0;
        }

        public Settings settings = new Settings();

        Material _material;
        PixelOutlinePass _pass;
        bool _loggedOnce;

        // 用来分辨"你改的 Feature 是不是当前生效的那个"（在没生效的 Renderer 上调，这里不会打日志）
        float _lastThickness = -1f;
        float _lastPixelSize = -1f;
        float _lastDepth = -1f;
        float _lastPixelate = -1f;

        /// <summary>
        /// 参数变化时打一条日志。如果你拖了滑块却没看到这条日志，说明你编辑的不是当前生效的 Renderer。
        /// </summary>
        void LogIfChanged()
        {
            if (Mathf.Approximately(_lastThickness, settings.outlineThickness) &&
                Mathf.Approximately(_lastPixelSize, settings.pixelSize) &&
                Mathf.Approximately(_lastDepth, settings.depthThreshold) &&
                Mathf.Approximately(_lastPixelate, settings.pixelateAmount))
            {
                return;
            }

            _lastThickness = settings.outlineThickness;
            _lastPixelSize = settings.pixelSize;
            _lastDepth = settings.depthThreshold;
            _lastPixelate = settings.pixelateAmount;

            Debug.Log($"[Slima] 全屏描边参数已更新（当前相机在用）：Thickness={settings.outlineThickness}, " +
                      $"PixelSize={settings.pixelSize}, DepthThreshold={settings.depthThreshold}, " +
                      $"Pixelate={settings.pixelateAmount}");
        }

        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
        static readonly int OutlineThicknessId = Shader.PropertyToID("_OutlineThickness");
        static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
        static readonly int PixelateAmountId = Shader.PropertyToID("_PixelateAmount");
        static readonly int DebugModeId = Shader.PropertyToID("_DebugMode");

        public override void Create()
        {
            _pass = new PixelOutlinePass { renderPassEvent = settings.passEvent };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 预览相机 / 反射探针不需要这套
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection) return;

            if (!EnsureMaterial()) return;

            // 第一次生效时打一条日志 —— 用来确认「Feature 到底有没有挂上、有没有在跑」
            if (!_loggedOnce)
            {
                _loggedOnce = true;
                Debug.Log($"[Slima] Toon Outline 已生效：PixelSize={settings.pixelSize}, " +
                          $"Pixelate={settings.pixelateAmount}, Outline={settings.outlineThickness}, " +
                          $"DepthThreshold={settings.depthThreshold}");
            }

            _pass.renderPassEvent = settings.passEvent;
            _pass.Setup(_material, settings);
            renderer.EnqueuePass(_pass);
            LogIfChanged();
        }

        /// <summary>
        /// 材质延迟创建：Create() 发生在资源刚加载时，那时 Shader.Find 可能还找不到。
        /// </summary>
        bool EnsureMaterial()
        {
            if (_material != null) return true;
            if (settings.shader == null) settings.shader = Shader.Find("Slima/Toon Outline Post");
            if (settings.shader == null) return false;

            _material = CoreUtils.CreateEngineMaterial(settings.shader);
            return _material != null;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
            _pass?.Dispose();
        }

        sealed class PixelOutlinePass : ScriptableRenderPass
        {
            Material _material;
            Settings _settings;
            RTHandle _temp;

            public PixelOutlinePass()
            {
                ConfigureInput(ScriptableRenderPassInput.Depth);   // 描边要读深度
            }

            public void Setup(Material material, Settings settings)
            {
                _material = material;
                _settings = settings;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null || _settings == null) return;

                _material.SetColor(OutlineColorId, _settings.outlineColor);
                _material.SetFloat(PixelSizeId, Mathf.Max(1f, _settings.pixelSize));
                _material.SetFloat(OutlineThicknessId, Mathf.Max(0f, _settings.outlineThickness));
                _material.SetFloat(DepthThresholdId, Mathf.Max(0.05f, _settings.depthThreshold));
                _material.SetFloat(PixelateAmountId, Mathf.Clamp01(_settings.pixelateAmount));
                _material.SetFloat(DebugModeId, Mathf.Clamp(_settings.debugMode, 0, 2));

                CommandBuffer cmd = CommandBufferPool.Get("Slima Toon Outline");
                using (new ProfilingScope(cmd, new ProfilingSampler("Slima Toon Outline")))
                {
                    RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                    if (source != null)
                    {
                        RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                        desc.depthBufferBits = 0;
                        desc.msaaSamples = 1;
                        RenderingUtils.ReAllocateIfNeeded(ref _temp, desc, FilterMode.Point, TextureWrapMode.Clamp,
                                                          name: "_SlimaToonTemp");

                        if (_temp != null)
                        {
                            // 源 → 临时图（像素化 + 描边）→ 贴回源
                            Blitter.BlitCameraTexture(cmd, source, _temp, _material, 0);
                            Blitter.BlitCameraTexture(cmd, _temp, source);
                        }
                    }
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                _temp?.Release();
                _temp = null;
            }
        }
    }
}
