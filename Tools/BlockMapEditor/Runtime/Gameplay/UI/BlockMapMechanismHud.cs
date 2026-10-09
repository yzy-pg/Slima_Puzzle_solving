using UnityEngine;

namespace BlockMap.Gameplay
{
    public sealed class BlockMapMechanismHud : MonoBehaviour
    {
        readonly BlockMechanismLabels _labels = new BlockMechanismLabels();
        BlockWorld _world;

        public void Initialize(BlockWorld world) => _world = world;

        void OnGUI()
        {
            if (_world == null || !BlockGameplaySettings.Current.showMechanismNumbers) return;
            _labels.Refresh(_world);
            GameplayOverlayDrawing.DrawMechanismLabels(_labels.Labels);
        }
    }
}
