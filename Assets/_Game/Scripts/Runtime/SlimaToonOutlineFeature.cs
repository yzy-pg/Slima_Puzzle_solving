using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlimaPuzzle.Rendering
{
    /// <summary>
    /// 全屏「像素化 + 描边」的 URP Renderer Feature。
    ///
    /// 三种描边依据（Edge Source）：
    ///   0 = 深度落差：需要 Depth Texture，物品与背景的前后关系断开处出线
    ///   1 = 颜色亮度：不依赖任何额外资源，但深色地面/暗物体上容易乱出线
    ///   2 = 遮罩分组（推荐）：把"一组物体"整体渲染成黑、背景白，只在这一组的外沿描边
    ///       · 分组靠 材质名片段（地面用 _Floor）/ Layer（功能物件放进同一个 Layer）
    ///       · 同组的多个物体（哪怕不同区块、不同材质）合成一个整体 → 只描这个整体的外轮廓
    ///       · 天生只画外侧：物品自己的像素不会被啃
    ///
    /// 装法：URP Renderer 资产 → Add Renderer Feature → Slima Toon Outline
    /// （或菜单 Tools/Slima/地图编辑器/给 URP Renderer 装上全屏描边 Feature 一键装）
    /// </summary>
    public sealed class SlimaToonOutlineFeature : ScriptableRendererFeature
    {
        /// <summary>一个描边分组 = 一个"整体对象"</summary>
        [System.Serializable]
        public sealed class MaskGroup
        {
            [Tooltip("分组名，只用于显示")]
            public string name = "地面";

            [Tooltip("材质名包含这个片段就归到这组（子网格级）。整片地面用 _Floor，墙面用 _Wall；多个用 | 分隔表示或")]
            public string materialContains = "_Floor";

            [Tooltip("物体名字包含这个片段就归到这组（对象级）。多个用 | 分隔表示或\n" +
                     "起点/出口/主角这类没有专属材质的，用这个最方便（如 Spawn、Exit、Player|Slime）")]
            public string nameContains = "";

            [Tooltip("按 Layer 匹配。想用渲染层分组就勾这里（和上面的条件是「同时满足」）")]
            public LayerMask layerMask = 0;

            [Tooltip("这一组的描边粗细（像素块）")]
            [Range(0f, 16f)] public float thickness = 2f;

            public bool enabled = true;
        }

        [System.Serializable]
        public sealed class Settings
        {
            [Tooltip("留空会自动找「Slima/Toon Outline Post」")]
            public Shader shader;

            [Tooltip("遮罩材质用的 shader（留空自动找「Slima/Outline Mask」）")]
            public Shader maskShader;

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

            [Tooltip("描边依据。\n" +
                     "0 = 深度落差（需要 Depth Texture 可用）。\n" +
                     "1 = 颜色/亮度落差（深色地面上容易乱出线）。\n" +
                     "2 = 遮罩分组（推荐）：同组物体合成一个整体，只在外沿出线，不依赖深度纹理。")]
            [Range(0, 2)] public int edgeSource = 2;

            [Tooltip("描边画在哪一侧（仅 0/1 依据有效；遮罩依据天生只在外面画）。\n" +
                     "0 = 两侧（会吃掉物品自己的像素）· 1 = 只向外（默认）· 2 = 只向内")]
            [Range(0, 2)] public int outlinePlacement = 1;

            [Header("遮罩分组（描边依据 = 2 时生效）")]
            [Tooltip("每一组 = 一个整体对象。同组物体（哪怕不同区块、不同材质）合成一个黑块，只描整体外沿。")]
            public List<MaskGroup> maskGroups = new List<MaskGroup>
            {
                new MaskGroup { name = "地面",  materialContains = "_Floor",                     thickness = 2f },
                new MaskGroup { name = "墙面",  materialContains = "_Wall",                      thickness = 2f },
                // 起点/出口在屏幕上只有 2~3 像素：线宽 ≈ Thickness × Pixel Size，所以这里必须给 1
                new MaskGroup { name = "起点",  nameContains = "Spawn",                          thickness = 1f },
                new MaskGroup { name = "出口",  nameContains = "Exit",                           thickness = 1f },
                new MaskGroup { name = "主角",  nameContains = "Player|Slime",                   thickness = 2f },
            };

            [Header("深度/颜色描边参数")]
            [Tooltip("全屏描边粗细（像素块）。屏幕实际宽度 ≈ 这个值 × Pixel Size。\n" +
                     "（遮罩模式用分组里的 Thickness）")]
            [Range(0f, 16f)] public float outlineThickness = 2f;

            [Tooltip("深度落差阈值（世界单位，1 ≈ 一格方块）。调小 = 出线更多、线也更粗。")]
            [Range(0.05f, 20f)] public float depthThreshold = 0.5f;

            [Tooltip("颜色描边阈值（0.01~1，仅 依据 = 颜色 时有效）。越小线越多、越粗。")]
            [Range(0.01f, 1f)] public float colorThreshold = 0.15f;

            [Header("调试")]
            [Tooltip("0 = 正常。\n" +
                     "1 = 原始深度缓冲（全黑 = 深度纹理没绑上）。\n" +
                     "2 = 边缘/遮罩可视化（遮罩模式下：白 = 该组物体）。\n" +
                     "3 = 线性眼深度灰度图（白 = 近）。")]
            [Range(0, 3)] public int debugMode = 0;
        }

        public Settings settings = new Settings();

        Material _material;
        Material _maskMaterial;
        PixelOutlinePass _pass;                       // 0/1 依据时用的合成 pass
        readonly List<MaskPass> _maskPasses = new List<MaskPass>();
        readonly List<PixelOutlinePass> _compositePasses = new List<PixelOutlinePass>();
        bool _loggedOnce;
        MeshRenderer[] _meshRenderers;
        int _rescanCountdown;

        // 用来分辨"你改的 Feature 是不是当前生效的那个"
        float _lastThickness = -1f;
        float _lastPixelSize = -1f;
        float _lastDepth = -1f;
        float _lastPixelate = -1f;

        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
        static readonly int OutlineThicknessId = Shader.PropertyToID("_OutlineThickness");
        static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
        static readonly int PixelateAmountId = Shader.PropertyToID("_PixelateAmount");
        static readonly int DebugModeId = Shader.PropertyToID("_DebugMode");
        static readonly int EdgeSourceId = Shader.PropertyToID("_EdgeSource");
        static readonly int OutlinePlacementId = Shader.PropertyToID("_OutlinePlacement");
        static readonly int ColorThresholdId = Shader.PropertyToID("_ColorThreshold");
        static readonly int MaskTextureId = Shader.PropertyToID("_SlimaMask");
        static readonly int DebugTintId = Shader.PropertyToID("_DebugTint");

        public override void Create()
        {
            _pass = new PixelOutlinePass { renderPassEvent = settings.passEvent };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection) return;

            if (!EnsureMaterials()) return;

            LogIfChanged();

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                var pipeline = GraphicsSettings.currentRenderPipeline;
                Debug.Log($"[Slima] Toon Outline 已生效：依据={EdgeSourceLabel(settings.edgeSource)}, " +
                          $"PixelSize={settings.pixelSize}, Thickness={settings.outlineThickness} | " +
                          $"管线={pipeline?.name ?? "（默认）"} | " +
                          $"相机需要深度纹理={renderingData.cameraData.requiresDepthTexture}");
            }

            // ---- 遮罩分组模式：每组 = 一次"画遮罩 + 一次合成" ----
            if (settings.edgeSource >= 2)
            {
                RefreshRenderers();

                int groupIndex = 0;
                foreach (MaskGroup group in settings.maskGroups)
                {
                    if (group == null || !group.enabled) continue;

                    EnsurePassSlot(groupIndex);

                    _maskPasses[groupIndex].renderPassEvent = settings.passEvent;
                    _maskPasses[groupIndex].Setup(_maskMaterial, group, _meshRenderers);
                    renderer.EnqueuePass(_maskPasses[groupIndex]);

                    _compositePasses[groupIndex].renderPassEvent = settings.passEvent;
                    _compositePasses[groupIndex].Setup(_material, settings, group.thickness, DebugTintFor(groupIndex));
                    renderer.EnqueuePass(_compositePasses[groupIndex]);

                    groupIndex++;
                }
                return;
            }

            // ---- 深度 / 颜色模式：单个合成 pass ----
            _pass.renderPassEvent = settings.passEvent;
            _pass.Setup(_material, settings, -1f, DebugTintFor(0));
            renderer.EnqueuePass(_pass);
        }

        void EnsurePassSlot(int index)
        {
            while (_maskPasses.Count <= index) _maskPasses.Add(new MaskPass());
            while (_compositePasses.Count <= index)
                _compositePasses.Add(new PixelOutlinePass { renderPassEvent = settings.passEvent });
        }

        /// <summary>每 60 帧重扫一次场景里的 MeshRenderer（烘出来的地形是静态的，道具是运行时生成的）</summary>
        void RefreshRenderers()
        {
            if (_meshRenderers != null && _rescanCountdown-- > 0) return;

            MeshRenderer[] all = Object.FindObjectsOfType<MeshRenderer>();
            var kept = new List<MeshRenderer>(all.Length);
            foreach (MeshRenderer meshRenderer in all)
            {
                if (meshRenderer == null) continue;

                // 挂了这个组件的物体（含子物体）不参与遮罩采样 → 不描边
                if (meshRenderer.GetComponentInParent<SlimaOutlineIgnore>() != null) continue;

                kept.Add(meshRenderer);
            }

            _meshRenderers = kept.ToArray();
            _rescanCountdown = 60;
        }

        static readonly Color[] DebugTints =
        {
            new Color(1.00f, 0.25f, 0.25f),   // 红
            new Color(0.25f, 1.00f, 0.35f),   // 绿
            new Color(0.30f, 0.55f, 1.00f),   // 蓝
            new Color(1.00f, 0.90f, 0.25f),   // 黄
            new Color(1.00f, 0.40f, 1.00f),   // 紫
            new Color(0.25f, 1.00f, 1.00f),   // 青
        };

        static Color DebugTintFor(int index) => DebugTints[index % DebugTints.Length];

        static string EdgeSourceLabel(int source) =>
            source >= 2 ? "遮罩分组" : (source == 1 ? "颜色亮度" : "深度落差");

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

            Debug.Log($"[Slima] 全屏描边参数已更新（当前相机在用）：依据={EdgeSourceLabel(settings.edgeSource)}, " +
                      $"Thickness={settings.outlineThickness}, PixelSize={settings.pixelSize}, " +
                      $"DepthThreshold={settings.depthThreshold}, Pixelate={settings.pixelateAmount}");
        }

        /// <summary>材质延迟创建：Create() 发生在资源刚加载时，那时 Shader.Find 可能还找不到。</summary>
        bool EnsureMaterials()
        {
            if (_material == null)
            {
                if (settings.shader == null) settings.shader = Shader.Find("Slima/Toon Outline Post");
                if (settings.shader == null) return false;
                _material = CoreUtils.CreateEngineMaterial(settings.shader);
            }

            if (_maskMaterial == null && settings.edgeSource >= 2)
            {
                if (settings.maskShader == null) settings.maskShader = Shader.Find("Slima/Outline Mask");
                if (settings.maskShader == null) return false;
                _maskMaterial = CoreUtils.CreateEngineMaterial(settings.maskShader);
            }

            return _material != null;
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            CoreUtils.Destroy(_maskMaterial);
            _material = null;
            _maskMaterial = null;
            _pass?.Dispose();
            foreach (MaskPass pass in _maskPasses) pass.Dispose();
            foreach (PixelOutlinePass pass in _compositePasses) pass.Dispose();
        }

        // ==================================================================
        // 遮罩 pass：把这一组物体画成黑、背景白，写进一张临时贴图
        // ==================================================================
        sealed class MaskPass : ScriptableRenderPass
        {
            Material _material;
            MaskGroup _group;
            MeshRenderer[] _renderers;
            RTHandle _mask;

            public void Setup(Material material, MaskGroup group, MeshRenderer[] renderers)
            {
                _material = material;
                _group = group;
                _renderers = renderers;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 24;      // 组内物体之间要正确遮挡
                desc.msaaSamples = 1;
                RenderingUtils.ReAllocateIfNeeded(ref _mask, desc, FilterMode.Point, TextureWrapMode.Clamp,
                                                  name: "_SlimaOutlineMask");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null || _group == null || _mask == null) return;

                CommandBuffer cmd = CommandBufferPool.Get("Slima Outline Mask");
                using (new ProfilingScope(cmd, new ProfilingSampler("Slima Outline Mask")))
                {
                    // 白 = 背景（描边会画在它上面），黑 = 这一组的物体
                    CoreUtils.SetRenderTarget(cmd, _mask, ClearFlag.Color | ClearFlag.Depth, Color.white);

                    if (_renderers != null)
                    {
                        foreach (MeshRenderer meshRenderer in _renderers)
                        {
                            if (meshRenderer == null) continue;

                            Material[] materials = meshRenderer.sharedMaterials;
                            for (int submesh = 0; submesh < materials.Length; submesh++)
                            {
                                if (!Matches(_group, meshRenderer.gameObject, materials[submesh])) continue;
                                cmd.DrawRenderer(meshRenderer, _material, submesh, 0);
                            }
                        }
                    }

                    cmd.SetGlobalTexture(MaskTextureId, _mask);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            /// <summary>过滤串匹配：多个片段用 | 分隔（任一命中即可），大小写不敏感。</summary>
            static bool MatchesAny(string filter, string value)
            {
                if (string.IsNullOrEmpty(filter)) return true;
                if (string.IsNullOrEmpty(value)) return false;

                foreach (string token in filter.Split('|'))
                {
                    string trimmed = token.Trim();
                    if (trimmed.Length == 0) continue;
                    if (value.IndexOf(trimmed, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                return false;
            }

            /// <summary>
            /// 分组匹配：材质名片段（子网格级）/ 物体名片段（对象级）/ Layer，设置了哪几个就要求同时满足。
            /// 三个都没设 → 不匹配（防止误把全场都画进遮罩）。
            /// </summary>
            static bool Matches(MaskGroup group, GameObject target, Material material)
            {
                bool hasLayer = group.layerMask.value != 0;
                bool hasName = !string.IsNullOrEmpty(group.nameContains);
                bool hasMaterial = !string.IsNullOrEmpty(group.materialContains);
                if (!hasLayer && !hasName && !hasMaterial) return false;

                if (hasLayer && (group.layerMask.value & (1 << target.layer)) == 0) return false;
                if (hasName && !MatchesAny(group.nameContains, target.name)) return false;
                if (hasMaterial)
                {
                    if (material == null) return false;
                    if (!MatchesAny(group.materialContains, material.name)) return false;
                }
                return true;
            }

            public void Dispose()
            {
                _mask?.Release();
                _mask = null;
            }
        }

        // ==================================================================
        // 合成 pass：像素化 + 描边（三种依据都在这里分支）
        // ==================================================================
        sealed class PixelOutlinePass : ScriptableRenderPass
        {
            Material _material;
            Settings _settings;
            float _thicknessOverride = -1f;
            Color _debugTint = Color.red;
            RTHandle _temp;

            public PixelOutlinePass()
            {
                ConfigureInput(ScriptableRenderPassInput.Depth);   // 依据 0 需要深度
            }

            public void Setup(Material material, Settings settings, float thicknessOverride, Color debugTint)
            {
                _material = material;
                _settings = settings;
                _thicknessOverride = thicknessOverride;
                _debugTint = debugTint;
            }

            public void RequestDepth() => ConfigureInput(ScriptableRenderPassInput.Depth);

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null || _settings == null) return;

                float thickness = _thicknessOverride >= 0f ? _thicknessOverride : _settings.outlineThickness;

                _material.SetColor(OutlineColorId, _settings.outlineColor);
                _material.SetFloat(PixelSizeId, Mathf.Max(1f, _settings.pixelSize));
                _material.SetFloat(OutlineThicknessId, Mathf.Max(0f, thickness));
                _material.SetFloat(DepthThresholdId, Mathf.Max(0.05f, _settings.depthThreshold));
                _material.SetFloat(PixelateAmountId, Mathf.Clamp01(_settings.pixelateAmount));
                _material.SetFloat(DebugModeId, Mathf.Clamp(_settings.debugMode, 0, 3));
                _material.SetFloat(EdgeSourceId, Mathf.Clamp(_settings.edgeSource, 0, 2));
                _material.SetFloat(OutlinePlacementId, Mathf.Clamp(_settings.outlinePlacement, 0, 2));
                _material.SetFloat(ColorThresholdId, Mathf.Max(0.001f, _settings.colorThreshold));
                _material.SetColor(DebugTintId, _debugTint);

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
