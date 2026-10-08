using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlimaPuzzle.Rendering
{
    /// <summary>
    /// 三渲二描边（重写版）—— 严格按五步流程：
    ///
    ///   ① 摄影机采样           : 每个 pass 都从相机颜色 Blit 一遍
    ///   ② 多对象分别采样        : 每个"遮罩分组"单独一次 pass
    ///   ③ 每个对象一张遮罩      : 把该组几何画成黑、背景白，写进临时贴图
    ///   ④ 对遮罩做外轮廓描边    : 只有"自己是背景 + 邻域有物体"的像素才画 → 天生只在外面
    ///   ⑤ 剔除特殊对象 + 重采样像素化 : 特殊物件进"清晰遮罩"，最后一步像素化时它们保持清晰
    ///
    /// 上一版的坑都在结构上避免了：
    ///   · 遮罩与合成在**同一个 Execute** 里 → 执行顺序不可能乱
    ///   · 遮罩贴图**绑在材质上**（不是全局贴图）→ 各组不会互相覆盖
    ///   · **每个 pass 独立材质实例** → 参数不会互相覆盖
    ///   · 遮罩 pass 与合成 pass 用**不同 renderPassEvent** → 排序稳定
    /// </summary>
    public sealed class SlimaToonOutlineFeature : ScriptableRendererFeature
    {
        /// <summary>一个描边分组 = 一个"整体对象"：同组物体合成一张遮罩，只描这个整体的外轮廓</summary>
        [System.Serializable]
        public sealed class MaskGroup
        {
            [Tooltip("分组名，只用于显示（内置组按名字识别，想覆盖内置组就取同名）")]
            public string name = "地面";

            [Tooltip("材质名包含这个片段就归到这组（子网格级）。地面 _Floor，墙面 _Wall；多个用 | 表示或")]
            public string materialContains = "_Floor";

            [Tooltip("物体名包含这个片段就归到这组（对象级，会沿父链匹配）。起点 Spawn、出口 Exit、主角 Player|Slime")]
            public string nameContains = "";

            [Tooltip("按 Layer 匹配（和上面条件是「同时满足」）")]
            public LayerMask layerMask = 0;

            [Tooltip("这一组的描边粗细（屏幕像素）")]
            [Range(0f, 32f)] public float thickness = 3f;

            public bool enabled = true;
        }

        [System.Serializable]
        public sealed class Settings
        {
            [Tooltip("留空会自动找 Slima/Outline Composite")]
            public Shader compositeShader;

            [Tooltip("留空会自动找 Slima/Outline Mask")]
            public Shader maskShader;

            [Tooltip("描边/填色插在哪个阶段（要在透明物体之后、后处理之前）")]
            public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            public Color outlineColor = Color.black;

            [Header("像素化（最后一步）")]
            [Range(1f, 16f)] public float pixelSize = 2f;
            [Range(0f, 1f)] public float pixelateAmount = 1f;

            [Header("不参与像素化的物件（特殊物件保持清晰）")]
            public bool crispSpecials = true;
            [Tooltip("名字过滤器（多个用 | 分隔）。这些物件 + 它们的外轮廓会保持清晰")]
            public string crispNameFilter = "Spawn|Exit|Player|Slime";
            [Tooltip("清晰范围外扩像素数：让特殊物件外面的描边也清晰")]
            [Range(0f, 32f)] public float crispDilate = 3f;

            [Header("遮罩分组（每组 = 一个整体对象）")]
            public List<MaskGroup> maskGroups = new List<MaskGroup>
            {
                new MaskGroup { name = "地面", materialContains = "_Floor",                       thickness = 3f },
                new MaskGroup { name = "墙面", materialContains = "_Wall",                        thickness = 3f },
                new MaskGroup { name = "起点", nameContains = "Spawn",                            thickness = 2f },
                new MaskGroup { name = "出口", nameContains = "Exit",                             thickness = 2f },
                new MaskGroup { name = "主角", nameContains = "Player|Slime",                     thickness = 3f },
            };

            [Header("调试")]
            [Tooltip("0 = 正常。1 = 分组填色测试（一眼看出每组识别到哪些物体）。2 = 遮罩读数（红=物体 绿=背景）")]
            [Range(0, 2)] public int debugMode = 0;
        }

        public Settings settings = new Settings();

        readonly List<GroupPass> _groupPasses = new List<GroupPass>();
        MaskPass _crispPass;
        PixelatePass _pixelatePass;
        bool _loggedOnce;
        MeshRenderer[] _renderers;
        int _rescan;
        readonly List<MaskGroup> _groups = new List<MaskGroup>();

        public override void Create()
        {
            _crispPass = new MaskPass { renderPassEvent = RenderPassEvent.BeforeRenderingOpaques };
            _pixelatePass = new PixelatePass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection) return;
            if (settings.compositeShader == null) settings.compositeShader = Shader.Find("Slima/Outline Composite");
            if (settings.maskShader == null) settings.maskShader = Shader.Find("Slima/Outline Mask");
            if (settings.compositeShader == null || settings.maskShader == null) return;

            RefreshRenderers();
            CollectGroups();

            if (!_loggedOnce)
            {
                _loggedOnce = true;
                float aspect = settings.pixelSize;
                Debug.Log($"[Slima] 描边流程已就绪：分组 {_groups.Count} 个，像素化 PixelSize={aspect}，" +
                          $"管线={GraphicsSettings.currentRenderPipeline?.name ?? "（默认）"}");
            }

            if (settings.debugMode >= 1) LogReport();

            // ⑤ 清晰遮罩（要被最终像素化那一步读取）
            bool wantCrisp = settings.crispSpecials && !string.IsNullOrEmpty(settings.crispNameFilter);
            if (wantCrisp)
            {
                if (_crispPass.Material == null) _crispPass.CreateMaterial(settings.maskShader, Color.white);
                _crispPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
                _crispPass.Setup(BuildFilterGroup("清晰（不像素化）", "", settings.crispNameFilter), _renderers, Color.black);
                renderer.EnqueuePass(_crispPass);
            }

            // ③④ 每个分组：一张遮罩 + 一次外轮廓描边（同一个 pass 内，顺序确定）
            int index = 0;
            foreach (MaskGroup group in _groups)
            {
                while (_groupPasses.Count <= index)
                    _groupPasses.Add(new GroupPass { renderPassEvent = settings.passEvent });

                GroupPass pass = _groupPasses[index];
                pass.renderPassEvent = settings.passEvent;
                pass.Setup(settings, group, _renderers,
                           index < DebugTints.Length ? DebugTints[index] : DebugTints[index % DebugTints.Length]);
                renderer.EnqueuePass(pass);
                index++;
            }

            // ⑤ 最后一步：像素化（清晰遮罩里的像素除外）
            if (settings.debugMode == 0)
            {
                _pixelatePass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                _pixelatePass.Setup(settings, wantCrisp ? _crispPass.Mask : null);
                renderer.EnqueuePass(_pixelatePass);
            }
        }

        static readonly Color[] DebugTints =
        {
            new Color(1.00f, 0.25f, 0.25f), new Color(0.25f, 1.00f, 0.35f),
            new Color(0.30f, 0.55f, 1.00f), new Color(1.00f, 0.90f, 0.25f),
            new Color(1.00f, 0.40f, 1.00f), new Color(0.25f, 1.00f, 1.00f),
        };

        static MaskGroup BuildFilterGroup(string name, string materialFilter, string nameFilter) =>
            new MaskGroup { name = name, materialContains = materialFilter, nameContains = nameFilter };

        /// <summary>内置 5 组：老资产里缺的自动补上（按名字识别，同名可覆盖）</summary>
        static readonly MaskGroup[] Builtins =
        {
            BuildFilterGroup("地面", "_Floor", ""),
            BuildFilterGroup("墙面", "_Wall", ""),
            BuildFilterGroup("起点", "", "Spawn"),
            BuildFilterGroup("出口", "", "Exit"),
            BuildFilterGroup("主角", "", "Player|Slime"),
        };

        void CollectGroups()
        {
            _groups.Clear();
            foreach (MaskGroup group in settings.maskGroups)
                if (group != null && group.enabled) _groups.Add(group);

            foreach (MaskGroup builtin in Builtins)
            {
                bool exists = false;
                foreach (MaskGroup group in _groups)
                    if (string.Equals(group.name, builtin.name, System.StringComparison.Ordinal)) { exists = true; break; }
                if (!exists) _groups.Add(builtin);
            }
        }

        /// <summary>每 60 帧重扫一次（地形是静态的，道具是运行时生成的）；跳过挂了 SlimaOutlineIgnore 的物体</summary>
        void RefreshRenderers()
        {
            if (_renderers != null && _rescan-- > 0) return;

            MeshRenderer[] all = Object.FindObjectsOfType<MeshRenderer>();
            var kept = new List<MeshRenderer>(all.Length);
            foreach (MeshRenderer renderer in all)
            {
                if (renderer == null) continue;
                if (renderer.GetComponentInParent<SlimaOutlineIgnore>() != null) continue;
                kept.Add(renderer);
            }
            _renderers = kept.ToArray();
            _rescan = 60;
        }

        void LogReport()
        {
            var report = new System.Text.StringBuilder("[Slima] 遮罩分组报告：\n");
            var materials = new HashSet<string>();

            foreach (MaskGroup group in _groups)
            {
                int hit = 0;
                foreach (MeshRenderer renderer in _renderers)
                {
                    if (renderer == null) continue;
                    Material[] mats = renderer.sharedMaterials;
                    if (mats == null) continue;
                    for (int submesh = 0; submesh < mats.Length; submesh++)
                    {
                        if (mats[submesh] != null) materials.Add(mats[submesh].name);
                        if (Matches(group, renderer.gameObject, mats[submesh])) hit++;
                    }
                }
                report.Append($"  · {group.name}｜材质'{group.materialContains}'｜名字'{group.nameContains}' → 命中 {hit} 个子网格\n");            }

            var list = new List<string>(materials);
            if (list.Count > 20) list = list.GetRange(0, 20);
            report.Append($"  场景材质名（{materials.Count}）：" + string.Join(", ", list));
            Debug.Log(report.ToString());
        }

        /// <summary>过滤串匹配：多个片段用 | 分隔（任一命中），大小写不敏感</summary>
        internal static bool MatchesAny(string filter, string value)
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

        /// <summary>分组匹配：材质名 / 物体名（含父链）/ Layer，设置了哪几个就要求同时满足</summary>
        internal static bool Matches(MaskGroup group, GameObject target, Material material)
        {
            bool hasLayer = group.layerMask.value != 0;
            bool hasName = !string.IsNullOrEmpty(group.nameContains);
            bool hasMaterial = !string.IsNullOrEmpty(group.materialContains);
            if (!hasLayer && !hasName && !hasMaterial) return false;

            if (hasLayer && (group.layerMask.value & (1 << target.layer)) == 0) return false;
            if (hasName && !MatchesAny(group.nameContains, HierarchyName(target))) return false;
            if (hasMaterial)
            {
                if (material == null) return false;
                if (!MatchesAny(group.materialContains, material.name)) return false;
            }
            return true;
        }

        static string HierarchyName(GameObject target)
        {
            string result = target.name;
            Transform parent = target.transform.parent;
            int depth = 0;
            while (parent != null && depth < 6) { result += "|" + parent.name; parent = parent.parent; depth++; }
            return result;
        }

        protected override void Dispose(bool disposing)
        {
            _crispPass?.Dispose();
            _pixelatePass?.Dispose();
            foreach (GroupPass pass in _groupPasses) pass.Dispose();
            _groupPasses.Clear();
        }

        // ==================================================================
        // ③ 遮罩 pass：把一组几何画成单色，背景为反色（写进自己的 RT）
        // ==================================================================
        sealed class MaskPass : ScriptableRenderPass
        {
            Material _material;
            MaskGroup _group;
            MeshRenderer[] _renderers;
            Color _clearColor = Color.white;
            RTHandle _mask;

            public Material Material => _material;
            public RTHandle Mask => _mask;

            public void CreateMaterial(Shader shader, Color maskColor)
            {
                if (_material != null) return;
                _material = CoreUtils.CreateEngineMaterial(shader);
                _material.SetColor(MaskColorId, maskColor);
            }

            public void Setup(MaskGroup group, MeshRenderer[] renderers, Color clearColor)
            {
                _group = group;
                _renderers = renderers;
                _clearColor = clearColor;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;          // 遮罩不做深度测试
                desc.msaaSamples = 1;
                desc.colorFormat = RenderTextureFormat.ARGBHalf;   // ★ 半浮点：G 通道存深度，精度要够分辨"叠在一起的物品"
                RenderingUtils.ReAllocateIfNeeded(ref _mask, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                                                  name: "_SlimaMask");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null || _group == null || _mask == null) return;

                CommandBuffer cmd = CommandBufferPool.Get("Slima Mask");
                using (new ProfilingScope(cmd, new ProfilingSampler("Slima Mask")))
                {
                    cmd.SetRenderTarget(_mask);
                    cmd.ClearRenderTarget(true, true, _clearColor);

                    if (_renderers != null)
                    {
                        foreach (MeshRenderer renderer in _renderers)
                        {
                            if (renderer == null) continue;
                            Material[] mats = renderer.sharedMaterials;
                            if (mats == null) continue;
                            for (int submesh = 0; submesh < mats.Length; submesh++)
                            {
                                if (!Matches(_group, renderer.gameObject, mats[submesh])) continue;
                                cmd.DrawRenderer(renderer, _material, submesh, 0);
                            }
                        }
                    }
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                CoreUtils.Destroy(_material);
                _material = null;
                _mask?.Release();
                _mask = null;
            }
        }

        // ==================================================================
        // ④ 分组 pass：画遮罩 + 立刻用它做外轮廓描边（同一个 Execute，顺序确定）
        // ==================================================================
        sealed class GroupPass : ScriptableRenderPass
        {
            Settings _settings;
            MaskGroup _group;
            MeshRenderer[] _renderers;
            Color _tint;

            Material _maskMaterial;
            Material _compositeMaterial;
            RTHandle _mask;
            RTHandle _temp;

            public GroupPass()
            {
                // 描边要判断"邻域物体有没有被更近的东西挡住" → 需要相机深度图
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }
            bool _loggedMaskInfo;

            public void Setup(Settings settings, MaskGroup group, MeshRenderer[] renderers, Color tint)
            {
                _settings = settings;
                _group = group;
                _renderers = renderers;
                _tint = tint;

                if (_maskMaterial == null) _maskMaterial = CoreUtils.CreateEngineMaterial(settings.maskShader);
                if (_maskMaterial != null) _maskMaterial.SetColor(MaskColorId, Color.black);
                if (_compositeMaterial == null) _compositeMaterial = CoreUtils.CreateEngineMaterial(settings.compositeShader);
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;          // 遮罩不做深度测试，不需要深度缓冲
                desc.msaaSamples = 1;
                desc.colorFormat = RenderTextureFormat.ARGBHalf;   // ★ 半浮点：G 通道存深度
                RenderingUtils.ReAllocateIfNeeded(ref _mask, desc, FilterMode.Bilinear, TextureWrapMode.Clamp,
                                                  name: "_SlimaGroupMask");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_maskMaterial == null || _compositeMaterial == null || _mask == null || _group == null) return;

                RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                if (source == null) return;

                CommandBuffer cmd = CommandBufferPool.Get("Slima Outline Group");
                using (new ProfilingScope(cmd, new ProfilingSampler("Slima Outline Group")))
                {
                    // ---- ③ 画这一组的遮罩（白底，物体涂黑）----
                    cmd.SetRenderTarget(_mask);
                    cmd.ClearRenderTarget(false, true, Color.white);   // 只清颜色（遮罩不做深度测试）

                    if (_renderers != null)
                    {
                        foreach (MeshRenderer renderer in _renderers)
                        {
                            if (renderer == null) continue;
                            Material[] mats = renderer.sharedMaterials;
                            if (mats == null) continue;
                            for (int submesh = 0; submesh < mats.Length; submesh++)
                            {
                                if (!Matches(_group, renderer.gameObject, mats[submesh])) continue;
                                cmd.DrawRenderer(renderer, _maskMaterial, submesh, 0);
                            }
                        }
                    }

                    // ---- ④ 用这张遮罩做外轮廓描边 ----
                    // 两条路都接上：材质绑定 + 全局绑定（哪个生效都行）
                    _compositeMaterial.SetTexture(MaskId, _mask.rt);
                    _compositeMaterial.SetFloat(MaskFarId, renderingData.cameraData.camera.farClipPlane);
                    _maskMaterial.SetFloat(MaskFarId, renderingData.cameraData.camera.farClipPlane);
                    cmd.SetGlobalTexture(MaskId, _mask);

                    if (_mask.rt == null)
                    {
                        Debug.LogError("[Slima] 遮罩贴图句柄无效（_mask.rt == null）→ 合成只能读到黑色");
                    }
                    else if (!_loggedMaskInfo)
                    {
                        _loggedMaskInfo = true;
                        Debug.Log($"[Slima] 遮罩贴图：{_mask.rt.width}x{_mask.rt.height} 格式={_mask.rt.format} " +
                                  $"（组={_group.name}）");
                    }

                    _compositeMaterial.SetFloat(PixelatePassId, 0f);
                    _compositeMaterial.SetColor(OutlineColorId, _settings.outlineColor);
                    _compositeMaterial.SetFloat(OutlineThicknessId, Mathf.Max(0f, _group.thickness));
                    _compositeMaterial.SetFloat(DebugModeId, _settings.debugMode);
                    _compositeMaterial.SetColor(DebugTintId, _tint);

                    RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                    desc.depthBufferBits = 0;
                    desc.msaaSamples = 1;
                    RenderingUtils.ReAllocateIfNeeded(ref _temp, desc, FilterMode.Point, TextureWrapMode.Clamp,
                                                      name: "_SlimaOutlineTemp");

                    if (_temp != null)
                    {
                        Blitter.BlitCameraTexture(cmd, source, _temp, _compositeMaterial, 0);
                        Blitter.BlitCameraTexture(cmd, _temp, source);
                    }
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                CoreUtils.Destroy(_maskMaterial);
                CoreUtils.Destroy(_compositeMaterial);
                _maskMaterial = null;
                _compositeMaterial = null;
                _mask?.Release();
                _mask = null;
                _temp?.Release();
                _temp = null;
            }
        }

        // ==================================================================
        // ⑤ 最后一步：重采样 + 像素化（清晰遮罩内的像素除外）
        // ==================================================================
        sealed class PixelatePass : ScriptableRenderPass
        {
            Settings _settings;
            RTHandle _crispMask;
            Material _material;
            RTHandle _temp;

            public void Setup(Settings settings, RTHandle crispMask)
            {
                _settings = settings;
                _crispMask = crispMask;
                if (_material == null) _material = CoreUtils.CreateEngineMaterial(settings.compositeShader);
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null) return;

                RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                if (source == null) return;

                _material.SetFloat(PixelatePassId, 1f);
                _material.SetFloat(PixelSizeId, Mathf.Max(1f, _settings.pixelSize));
                _material.SetFloat(PixelateAmountId, Mathf.Clamp01(_settings.pixelateAmount));
                _material.SetFloat(CrispDilateId, Mathf.Max(0f, _settings.crispDilate));
                _material.SetTexture(CrispMaskId, _crispMask != null ? _crispMask.rt : Texture2D.blackTexture);

                CommandBuffer cmd = CommandBufferPool.Get("Slima Pixelate");
                using (new ProfilingScope(cmd, new ProfilingSampler("Slima Pixelate")))
                {
                    RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
                    desc.depthBufferBits = 0;
                    desc.msaaSamples = 1;
                    RenderingUtils.ReAllocateIfNeeded(ref _temp, desc, FilterMode.Point, TextureWrapMode.Clamp,
                                                      name: "_SlimaPixelateTemp");

                    if (_temp != null)
                    {
                        Blitter.BlitCameraTexture(cmd, source, _temp, _material, 0);
                        Blitter.BlitCameraTexture(cmd, _temp, source);
                    }
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                CoreUtils.Destroy(_material);
                _material = null;
                _temp?.Release();
                _temp = null;
            }
        }

        static readonly int MaskColorId = Shader.PropertyToID("_MaskColor");
        static readonly int MaskId = Shader.PropertyToID("_SlimaMask");
        static readonly int MaskFarId = Shader.PropertyToID("_MaskFar");
        static readonly int CrispMaskId = Shader.PropertyToID("_SlimaCrispMask");
        static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int OutlineThicknessId = Shader.PropertyToID("_OutlineThickness");
        static readonly int PixelatePassId = Shader.PropertyToID("_PixelatePass");
        static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
        static readonly int PixelateAmountId = Shader.PropertyToID("_PixelateAmount");
        static readonly int CrispDilateId = Shader.PropertyToID("_CrispDilate");
        static readonly int DebugModeId = Shader.PropertyToID("_DebugMode");
        static readonly int DebugTintId = Shader.PropertyToID("_DebugTint");
    }
}
