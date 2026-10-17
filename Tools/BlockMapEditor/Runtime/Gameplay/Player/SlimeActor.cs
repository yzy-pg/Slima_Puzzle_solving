using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>单个史莱姆的状态和表现，不读取输入、不判断地图。</summary>
    public sealed class SlimeActor : MonoBehaviour
    {
        SlimeState _state;
        Renderer[] _renderers;
        Light _glow;
        SlimeElementEffects _effects;

        [Header("角色发光")]
        [Min(0f)] public float glowIntensity = 2.4f;
        [Min(0f)] public float glowRange = 4.5f;
        [Min(0f)] public float emissionStrength = 0.2f;
        [Range(0.002f, 0.5f), Tooltip("角色和点光源的世界像素格大小；越小，像素越细密。")]
        public float glowPixelSize = 0.04f;
        [Range(0.1f, 0.75f), Tooltip("相对脚下地面的光源高度（格）；跳跃和悬浮时最高保持在 0.75 格。")]
        public float glowHeight = 0.38f;
        [Range(0f, 0.5f)] public float glowBreathAmount = 0.12f;
        [Min(0.1f)] public float glowBreathPeriod = 2.6f;
        [Header("史莱姆透光材质")]
        [Range(0f, 1f)] public float subsurfaceStrength = 0.7f;
        [Range(0f, 1f)] public float subsurfaceThickness = 0.45f;

        public SlimeGridMover Mover { get; private set; }
        public SlimeState State => _state;
        public bool IsControlled { get; private set; }
        public int Weight => _state?.Weight ?? 0;
        public Color BodyColor => ColorForState(_state);

        public Vector3 OverlayAnchor
        {
            get
            {
                float top = transform.position.y + 0.8f;
                if (_renderers != null)
                    foreach (Renderer item in _renderers)
                        if (item != null && item.enabled) top = Mathf.Max(top, item.bounds.max.y);
                return new Vector3(transform.position.x, top, transform.position.z);
            }
        }

        public void Initialize(SlimeState state, ISlimeMovementPlanner planner, float cellSize, float viewYaw)
        {
            if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
            Mover = GetComponent<SlimeGridMover>();
            if (Mover == null) Mover = gameObject.AddComponent<SlimeGridMover>();
            if (Mover.visual == null && transform.childCount > 0) Mover.visual = transform.GetChild(0);
            Mover.Planner = planner;
            Mover.cellSize = cellSize;
            Mover.viewYaw = viewYaw;
            Mover.stepDuration = BlockGameplaySettings.Current.stepDuration;
            Mover.hopHeight = BlockGameplaySettings.Current.hopHeight;
            _effects = GetComponent<SlimeElementEffects>();
            if (_effects == null) _effects = gameObject.AddComponent<SlimeElementEffects>();
            _effects.Initialize(Mover.visual != null ? Mover.visual : transform);
            if (_glow == null)
            {
                var source = new GameObject("Slime Glow");
                source.transform.SetParent(transform, false);
                _glow = source.AddComponent<Light>();
                _glow.type = LightType.Point;
                _glow.renderMode = LightRenderMode.ForcePixel;
                _glow.shadows = LightShadows.Soft;
                _glow.shadowBias = 0.04f;
                _glow.shadowNormalBias = 0.15f;
                _glow.shadowNearPlane = 0.1f;
            }
            SetState(state);
            var gameRoot = GetComponentInParent<BlockMapGameRoot>();
            if (gameRoot != null)
                foreach (Renderer item in _renderers) gameRoot.ApplyAtmosphereTo(item);
        }

        void LateUpdate()
        {
            UpdateGlow();
        }

        void UpdateGlow()
        {
            if (_glow == null) return;
            float wave = Mathf.Sin(Time.time * Mathf.PI * 2f / Mathf.Max(0.1f, glowBreathPeriod));
            bool fire = _state.form == SlimeForm.Solid && _state.element == SlimeElement.Red;
            if (fire) wave = Mathf.Sin(Time.time * 5.3f) * 0.65f + Mathf.Sin(Time.time * 9.7f) * 0.35f;
            float scale = _state.size == SlimeSize.Small ? 0.72f : 1f;
            _glow.intensity = glowIntensity * (1f + glowBreathAmount * wave);
            _glow.range = glowRange * Mover.cellSize * scale * (1f + glowBreathAmount * 0.35f * wave);
            _glow.transform.position = GlowPosition();
            if (fire) _glow.transform.position += new Vector3(Mathf.Sin(Time.time * 2.1f), 0f,
                Mathf.Cos(Time.time * 2.7f)) * (0.045f * Mover.cellSize * scale);
        }

        Vector3 GlowPosition()
        {
            float scale = _state.size == SlimeSize.Small ? 0.72f : 1f;
            float lift = Mover.visual != null ? Mover.visual.position.y - transform.position.y : 0f;
            // Keep the source inside the body and below a one-cell wall, even at hop apex.
            float height = Mathf.Clamp(glowHeight * scale + lift / Mover.cellSize, 0.1f, 0.75f);
            return transform.position + Vector3.up * (height * Mover.cellSize);
        }

        public void SetState(SlimeState state)
        {
            _state = state ?? new SlimeState();
            Mover?.SetVisualHeight(_state.form == SlimeForm.Gas
                ? BlockGameplaySettings.Current.gasVisualHeight
                : 0f);
            ApplyAppearance();
        }

        public void SetControlled(bool controlled)
        {
            IsControlled = controlled;
            if (Mover != null) Mover.InputEnabled = controlled;
        }

        void ApplyAppearance()
        {
            float scale = _state.size == SlimeSize.Small ? 0.72f : 1f;
            transform.localScale = Vector3.one * scale;

            Color color = ColorForState(_state);
            bool fire = _state.form == SlimeForm.Solid && _state.element == SlimeElement.Red;
            _effects?.SetState(_state);
            if (_glow != null)
            {
                _glow.color = fire ? new Color(1f, 0.32f, 0.045f)
                    : _state.form == SlimeForm.Solid && _state.element == SlimeElement.Blue
                        ? new Color(0.12f, 0.42f, 1f) : Color.Lerp(color, Color.white, 0.35f);
                UpdateGlow();
            }
            var properties = new MaterialPropertyBlock();
            foreach (Renderer item in _renderers)
            {
                if (item == null) continue;
                if (item is ParticleSystemRenderer || item.name == "Flowing Fire Glow" || item.name == "Pixel Flames") continue;
                if (item.name == "Facing") { item.enabled = false; continue; }
                if (item.sharedMaterial == null || !item.sharedMaterial.HasProperty("_SlimeEyes"))
                {
                    var material = Resources.Load<Material>("Player/BlockMapPlayer");
                    if (material != null) item.sharedMaterial = material;
                }
                // The luminous body must not block its own light onto the floor.
                item.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                item.GetPropertyBlock(properties);
                properties.SetFloat("_SlimeEyes", 1f);
                properties.SetTexture("_SlimeEyeMap", Resources.Load<Texture2D>("SlimeEye"));
                properties.SetFloat("_SlimeFireFlow", fire ? 1f : 0f);
                properties.SetFloat("_SlimeSnowCap", _state.form == SlimeForm.Solid && _state.element == SlimeElement.Blue ? 1f : 0f);
                properties.SetColor("_BaseColor", color);
                properties.SetColor("_Color", color);
                properties.SetColor("_EmissionColor", color);
                properties.SetFloat("_EmissionStrength", emissionStrength);
                properties.SetFloat("_NoiseWorldSize", Mathf.Max(0.002f, glowPixelSize));
                // A source inside a luminous body should not wash out its own color.
                properties.SetFloat("_ExtraLightMode", 0f);
                properties.SetFloat("_LightWrap", 0f);
                properties.SetFloat("_SubsurfaceStrength", item.name == "Facing" ? 0f : subsurfaceStrength);
                properties.SetFloat("_SubsurfaceThickness", subsurfaceThickness);
                properties.SetColor("_SubsurfaceColor", Color.white);
                item.SetPropertyBlock(properties);
            }
        }

        static Color ColorForState(SlimeState state)
        {
            if (state.form == SlimeForm.Gas) return new Color(0.88f, 0.94f, 1f, 0.75f);
            if (state.element == SlimeElement.Red) return new Color(0.95f, 0.22f, 0.15f);
            if (state.element == SlimeElement.Blue) return new Color(0.15f, 0.48f, 0.95f);
            return new Color(0.90f, 0.94f, 0.88f);
        }
    }
}
