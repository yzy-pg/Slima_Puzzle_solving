using UnityEngine;

namespace BlockMap.Gameplay
{
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SplitterSawAnimation : MonoBehaviour
    {
        public Sprite[] frames;
        [Min(1f)] public float framesPerSecond = 16f;
        public int CurrentFrame { get; private set; }
        SpriteRenderer _renderer;
        float _elapsed;

        void OnEnable()
        {
            _renderer = GetComponent<SpriteRenderer>();
            _elapsed = 0f;
            CurrentFrame = 0;
            if (frames != null && frames.Length > 0) _renderer.sprite = frames[0];
        }

        void Update() => Advance(Time.deltaTime);

        public void Advance(float deltaTime)
        {
            if (frames == null || frames.Length == 0 || framesPerSecond <= 0f) return;
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            _elapsed = Mathf.Repeat(_elapsed + deltaTime, frames.Length / framesPerSecond);
            CurrentFrame = Mathf.FloorToInt(_elapsed * framesPerSecond) % frames.Length;
            _renderer.sprite = frames[CurrentFrame];
        }
    }
}
