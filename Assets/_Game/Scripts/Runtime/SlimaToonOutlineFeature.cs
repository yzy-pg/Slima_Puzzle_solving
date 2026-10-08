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

            [Tooltip("描边粗细（以像素块为单位）")]
            [Range(0f, 4f)] public float outlineThickness = 1f;

            [Tooltip("深度落差阈值（世界单位，1 ≈ 一格方块）：邻域落差小于它就不算出边。越小线越多")]
            [Range(0.05f, 20f)] public float depthThreshold = 0.6f;
        }

        public Settings settings = new Settings();

        Material _material;
        PixelOutlinePass _pass;
        bool _loggedOnce;

        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
        static readonly int OutlineThicknessId = Shader.PropertyToID("_OutlineThickness");
        static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
        static readonly int PixelateAmountId = Shader.PropertyToID("_PixelateAmount");

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
