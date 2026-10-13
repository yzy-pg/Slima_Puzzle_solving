using UnityEngine;
using UnityEngine.Rendering;

namespace BlockMap.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class SlimeElementEffects : MonoBehaviour
    {
        ParticleSystem _particles;
        MeshRenderer _halo;
        MeshRenderer _flames;
        Mesh _haloMesh;
        SlimeElement _element;
        bool _active;
        MaterialPropertyBlock _properties;

        public void Initialize(Transform visual)
        {
            if (_particles != null) return;
            _properties = new MaterialPropertyBlock();
            var source = new GameObject("Element Particles");
            source.transform.SetParent(visual, false);
            source.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            _particles = source.AddComponent<ParticleSystem>();
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 40;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.085f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = _particles.emission;
            emission.rateOverTime = 10f;
            emission.enabled = false;
            var shape = _particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.3f;
            var noise = _particles.noise;
            noise.enabled = true;
            noise.strength = 0.16f;
            noise.frequency = 1.4f;
            noise.scrollSpeed = 0.6f;
            noise.damping = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            var color = _particles.colorOverLifetime;
            color.enabled = true;
            color.color = fade;
            var renderer = source.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;

            var halo = new GameObject("Flowing Fire Glow");
            halo.transform.SetParent(visual, false);
            halo.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            _haloMesh = new Mesh { name = "Slime halo quad", hideFlags = HideFlags.DontSave };
            _haloMesh.vertices = new[] { new Vector3(-0.5f,-0.5f,0), new Vector3(-0.5f,0.5f,0),
                new Vector3(0.5f,0.5f,0), new Vector3(0.5f,-0.5f,0) };
            _haloMesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            _haloMesh.triangles = new[] { 0,1,2,0,2,3 };
            _haloMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            halo.AddComponent<MeshFilter>().sharedMesh = _haloMesh;
            _halo = halo.AddComponent<MeshRenderer>();
            _halo.sharedMaterial = Resources.Load<Material>("SlimeElementHalo");
            _halo.shadowCastingMode = ShadowCastingMode.Off;
            _halo.receiveShadows = false;
            _halo.lightProbeUsage = LightProbeUsage.Off;
            _halo.enabled = false;

            var flames = new GameObject("Pixel Flames");
            flames.transform.SetParent(visual, false);
            flames.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            flames.AddComponent<MeshFilter>().sharedMesh = _haloMesh;
            _flames = flames.AddComponent<MeshRenderer>();
            _flames.sharedMaterial = Resources.Load<Material>("SlimeFlame");
            _flames.shadowCastingMode = ShadowCastingMode.Off;
            _flames.receiveShadows = false;
            _flames.lightProbeUsage = LightProbeUsage.Off;
            _flames.enabled = false;
        }

        public void SetState(SlimeState state)
        {
            bool active = state.form == SlimeForm.Solid && state.element != SlimeElement.Neutral;
            bool changed = active != _active || state.element != _element;
            _active = active;
            _element = state.element;
            bool fire = active && _element == SlimeElement.Red;
            var emission = _particles.emission;
            emission.enabled = active;
            _halo.enabled = fire;
            _flames.enabled = fire;
            float scale = state.size == SlimeSize.Small ? 0.72f : 1f;
            _properties.Clear();
            _properties.SetFloat("_Size", 1.7f * scale * GetComponent<SlimeGridMover>().cellSize);
            _halo.SetPropertyBlock(_properties);
            _properties.Clear();
            float cellScale = scale * GetComponent<SlimeGridMover>().cellSize;
            _properties.SetFloat("_Width", 1.12f * cellScale);
            _properties.SetFloat("_Height", 1.25f * cellScale);
            _flames.SetPropertyBlock(_properties);
            if (!changed) return;
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (!active) return;
            var main = _particles.main;
            main.startColor = fire ? new ParticleSystem.MinMaxGradient(new Color(1f,0.18f,0.015f), new Color(1f,0.6f,0.08f))
                : new ParticleSystem.MinMaxGradient(new Color(0.45f,0.7f,1f), new Color(0.85f,0.95f,1f));
            main.startSize = fire ? new ParticleSystem.MinMaxCurve(0.045f,0.085f)
                : new ParticleSystem.MinMaxCurve(0.09f,0.14f);
            main.gravityModifier = fire ? -0.018f : 0.012f;
            _particles.GetComponent<ParticleSystemRenderer>().sharedMaterial =
                Resources.Load<Material>(fire ? "SlimeFireParticle" : "SlimeSnowParticle");
            if (Application.isPlaying) _particles.Play();
        }

        void OnDestroy()
        {
            if (_haloMesh == null) return;
            if (Application.isPlaying) Destroy(_haloMesh);
            else DestroyImmediate(_haloMesh);
        }
    }
}
