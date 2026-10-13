using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>底部原点的按压部件，或绕金属轴转动的拉杆。只改变美术，不改变逻辑占格。</summary>
    public sealed class GameplayControlArt : MonoBehaviour
    {
        public Transform movingPart;
        public Renderer[] colorParts;
        public bool isLever;
        public float duration = .16f;
        [Range(.05f, 1f)] public float pressedHeight = .35f;
        public float leverAngle = 32f;
        bool _active;
        float _amount;

        public void SetActivated(bool active) => _active = active;

        void Update() => AdvanceAnimation(Time.deltaTime);

        public void AdvanceAnimation(float deltaTime)
        {
            _amount = Mathf.MoveTowards(_amount, _active ? 1f : 0f,
                Mathf.Max(0f, deltaTime) / Mathf.Max(.01f, duration));
            if (movingPart == null) return;
            float eased = Mathf.SmoothStep(0f, 1f, _amount);
            if (isLever)
                movingPart.localRotation = Quaternion.Euler(Mathf.Lerp(-leverAngle, leverAngle, eased), 0f, 0f);
            else
                movingPart.localScale = new Vector3(1f, Mathf.Lerp(1f, pressedHeight, eased), 1f);
        }
    }
}
