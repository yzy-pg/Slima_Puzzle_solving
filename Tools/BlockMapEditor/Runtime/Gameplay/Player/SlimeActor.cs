using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>单个史莱姆的状态和表现，不读取输入、不判断地图。</summary>
    public sealed class SlimeActor : MonoBehaviour
    {
        SlimeState _state;
        Renderer[] _renderers;

        public SlimeGridMover Mover { get; private set; }
        public SlimeState State => _state;
        public bool IsControlled { get; private set; }
        public int Weight => _state?.Weight ?? 0;

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
            _renderers = GetComponentsInChildren<Renderer>(true);
            Mover = GetComponent<SlimeGridMover>();
            if (Mover == null) Mover = gameObject.AddComponent<SlimeGridMover>();
            Mover.Planner = planner;
            Mover.cellSize = cellSize;
            Mover.viewYaw = viewYaw;
            Mover.stepDuration = BlockGameplaySettings.Current.stepDuration;
            Mover.hopHeight = BlockGameplaySettings.Current.hopHeight;
            SetState(state);
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
            var properties = new MaterialPropertyBlock();
            foreach (Renderer item in _renderers)
            {
                if (item == null) continue;
                item.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", color);
                properties.SetColor("_Color", color);
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
