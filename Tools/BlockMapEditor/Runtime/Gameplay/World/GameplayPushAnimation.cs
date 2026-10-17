using UnityEngine;
using UnityEngine.Rendering;

namespace BlockMap.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplayPushAnimation : MonoBehaviour
    {
        Vector3 _start;
        Vector3 _target;
        float _duration;
        float _elapsed;
        ParticleSystem _particles;

        public bool IsMoving { get; private set; }

        public void MoveTo(Vector3 target, float duration)
        {
            _start = transform.position;
            _target = target;
            _duration = Mathf.Max(.01f, duration);
            _elapsed = 0f;
            IsMoving = true;
            if (_particles == null) CreateParticles();
            Vector3 drift = (_start - target).normalized * 1.4f;
            _particles.transform.position = _start + Vector3.up * .045f + drift.normalized * .48f;
            _particles.transform.rotation = Quaternion.LookRotation(-drift);
            var velocity = _particles.velocityOverLifetime;
            velocity.x = new ParticleSystem.MinMaxCurve(drift.x - .3f, drift.x + .3f);
            velocity.y = new ParticleSystem.MinMaxCurve(.15f, .45f);
            velocity.z = new ParticleSystem.MinMaxCurve(drift.z - .3f, drift.z + .3f);
            _particles.Play();
            _particles.Emit(8);
        }

        void Update() => AdvanceAnimation(Time.deltaTime);

        public void AdvanceAnimation(float deltaTime)
        {
            if (!IsMoving) return;
            _elapsed += deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            transform.position = Vector3.Lerp(_start, _target, t);
            if (t < 1f) return;
            IsMoving = false;
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void CreateParticles()
        {
            var root = new GameObject("Push Base Particles");
            root.transform.SetParent(transform, false);
            _particles = root.AddComponent<ParticleSystem>();
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.18f, .35f);
            main.startSize = new ParticleSystem.MinMaxCurve(.035f, .075f);
            main.startSpeed = 0;
            main.startColor = new Color(.65f, .6f, .5f, .75f);
            main.gravityModifier = .35f;
            main.maxParticles = 48;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var emission = _particles.emission;
            emission.rateOverTime = 70;
            var shape = _particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(.8f, .02f, .1f);
            var velocity = _particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            var color = _particles.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            color.color = fade;
            var size = _particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, .25f));
            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("BlockMapBeamParticle");
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }

        void OnDisable()
        {
            if (IsMoving) transform.position = _target;
            IsMoving = false;
            if (_particles != null) _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
