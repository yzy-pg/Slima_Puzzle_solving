using System;
using UnityEngine;

namespace BlockMap.Gameplay
{
    public static class BlockGameplaySettings
    {
        [Serializable]
        public sealed class Data
        {
            public string _comment;
            public float stepDuration = 0.16f;
            public float hopHeight = 0.14f;
            public float gasVisualHeight = 1f;
            public int blueSlideLimit = 64;
            public int beamRange = 64;
            public float beamWidth = 0.10f;
            public float doorAnimationDuration = 0.35f;
            public bool showEmitterDirectionIndicator = true;
            public float emitterIndicatorHeight = 1.05f;
            public float emitterIndicatorLength = 0.70f;
            public float emitterIndicatorWidth = 0.10f;
            public float cameraSmoothTime = 0.25f;
            public float interactionBubbleSize = 38f;
            public float interactionBubbleHeight = 0.65f;
            public float activeMarkerHeight = 0.24f;
            public bool showMechanismNumbers = true;
            public float mechanismLabelHeight = 1.35f;
            public float mechanismLabelGap = 6f;
        }

        const string ResourcePath = "BlockMap/GameplaySettings";
        static Data _current;

        public static Data Current
        {
            get
            {
                if (_current == null) Load();
                return _current;
            }
        }

        public static void Reload()
        {
            _current = null;
            Load();
        }

        static void Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            _current = asset != null ? JsonUtility.FromJson<Data>(asset.text) : new Data();
            if (_current == null) _current = new Data();
        }
    }
}
