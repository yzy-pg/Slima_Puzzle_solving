using UnityEngine;
using UnityEngine.Rendering;

namespace BlockMap.Gameplay
{
    // Pixel points appear near the emitter outlet and wander gently around it.
    public sealed class BlockBeamParticles : MonoBehaviour
    {
        Vector3 _origin;
        Color _color;
        bool _hasSource;
        ParticleSystem _particles;
        float _nextEmission;

        public void Initialize()
        {
            _particles = gameObject.AddComponent<ParticleSystem>();
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.055f, 0.085f);
            main.startRotation = 0;
            main.startSpeed = 0;
            main.maxParticles = 24;
            var emission = _particles.emission;
            emission.enabled = false;
            var shape = _particles.shape;
            shape.enabled = false;
            var color = _particles.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.75f), new GradientAlphaKey(0, 1) });
            color.color = fade;
            var size = _particles.sizeOverLifetime;
            size.enabled = false;
            var noise = _particles.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 1;
            noise.scrollSpeed = 0.4f;
            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("BlockMapBeamParticle");
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            _nextEmission = Random.Range(0.10f, 0.18f);
            if (Application.isPlaying) _particles.Play();
        }

        public void AddSegment(Vector3 from, Vector3 to, Color color)
        {
            if (_hasSource) return;
            _origin = from + (to - from).normalized * 0.58f;
            _color = color;
            _hasSource = true;
        }

        void Update()
        {
            if (!Application.isPlaying || !_hasSource) return;
            _nextEmission -= Time.deltaTime;
            if (_nextEmission > 0) return;
            _nextEmission = Random.Range(0.10f, 0.18f);
            _particles.Emit(new ParticleSystem.EmitParams
            {
                position = _origin + Random.insideUnitSphere * 0.045f,
                velocity = Random.onUnitSphere * Random.Range(0.08f, 0.28f),
                startColor = _color
            }, 1);
        }
    }
}
