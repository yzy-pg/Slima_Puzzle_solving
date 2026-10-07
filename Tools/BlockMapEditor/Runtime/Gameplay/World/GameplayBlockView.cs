using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>机关的纯表现层。开关状态由 BlockGameplayController 决定。</summary>
    public sealed class GameplayBlockView : MonoBehaviour
    {
        Renderer[] _renderers;
        Color _baseColor;
        BlockMapDoor _door;

        public Vector3Int Cell { get; private set; }
        public GameplayBlockProfile Profile { get; private set; }

        public void Initialize(Vector3Int cell, GameplayBlockProfile profile)
        {
            Cell = cell;
            Profile = profile;
            _baseColor = profile.Tint;
            _renderers = GetComponentsInChildren<Renderer>(true);
            _door = GetComponentInChildren<BlockMapDoor>(true);
            ApplyColor(_baseColor);
        }

        public void SetActivated(bool active)
        {
            if (_door != null) _door.SetOpen(active);
            ApplyColor(active ? Color.Lerp(_baseColor, Color.white, 0.45f) : _baseColor);
        }

        void ApplyColor(Color color)
        {
            if (_renderers == null) return;
            var block = new MaterialPropertyBlock();
            foreach (Renderer item in _renderers)
            {
                if (item == null) continue;
                item.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                item.SetPropertyBlock(block);
            }
        }
    }
}
