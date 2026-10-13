using UnityEngine;
using UnityEngine.Rendering;

namespace BlockMap.Gameplay
{
    /// <summary>Detached visual only: the grid cell is removed immediately, while the cardboard burns away.</summary>
    public sealed class DestructibleBurnEffect : MonoBehaviour
    {
        public const float Duration = 1.25f;
        public const float ParticleTail = .8f;
        Renderer[] _renderers;
        MaterialPropertyBlock _properties;
        Bounds _bounds;
        ParticleSystem _embers;
        ParticleSystem _flames;
        ParticleSystem _smoke;
        Light _glow;
        Texture2D _noise;
        float _elapsed;
        bool _emissionStopped;

        public float Progress => Mathf.Clamp01(_elapsed / Duration);

        public static DestructibleBurnEffect Create(GameplayBlockView source, Transform parent)
        {
            var root = new GameObject("Burning_" + source.name);
            root.transform.SetParent(parent, true);
            var effect = root.AddComponent<DestructibleBurnEffect>();
            effect._properties = new MaterialPropertyBlock();
            effect._noise = Resources.Load<Texture2D>("SlimeFlameNoise");
            bool first = true;
            foreach (MeshRenderer original in source.GetComponentsInChildren<MeshRenderer>())
            {
                var part = new GameObject(original.name);
                part.transform.SetParent(root.transform, false);
                part.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                part.transform.localScale = original.transform.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = original.GetComponent<MeshFilter>().sharedMesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = original.sharedMaterials;
                original.GetPropertyBlock(effect._properties);
                renderer.SetPropertyBlock(effect._properties);
                renderer.shadowCastingMode = original.shadowCastingMode;
                renderer.receiveShadows = original.receiveShadows;
                if (first) { effect._bounds = original.bounds; first = false; }
                else effect._bounds.Encapsulate(original.bounds);
            }
            effect._renderers = root.GetComponentsInChildren<Renderer>();
            effect.BuildEmbers();
            effect.AdvanceAnimation(0);
            return effect;
        }

        void Update() => AdvanceAnimation(Time.deltaTime);

        public void AdvanceAnimation(float deltaTime)
        {
            _elapsed += Mathf.Max(0, deltaTime);
            float progress = Progress;
            foreach (Renderer renderer in _renderers)
            {
                renderer.GetPropertyBlock(_properties);
                _properties.SetFloat("_BurnEnabled", 1);
                _properties.SetFloat("_BurnProgress", progress);
                // The inverse-hull outline would expose black backfaces inside the clipped cardboard.
                _properties.SetFloat("_OutlineWidth", 0);
                _properties.SetVector("_BurnHeight", new Vector4(_bounds.min.y, Mathf.Max(.001f, _bounds.size.y), 0, 0));
                _properties.SetTexture("_BurnNoiseTex", _noise);
                renderer.SetPropertyBlock(_properties);
            }
            Vector3 edge = _bounds.center;
            edge.y = Mathf.Lerp(_bounds.max.y, _bounds.min.y, progress);
            _embers.transform.position = edge;
            _flames.transform.position = edge;
            _smoke.transform.position = edge + Vector3.up * .06f;
            _glow.transform.position = edge + Vector3.up * .12f;
            _glow.intensity = .18f * (1 - progress);
            if (progress >= 1 && !_emissionStopped)
            {
                _emissionStopped = true;
                foreach (Renderer renderer in _renderers) renderer.enabled = false;
                foreach (var particles in new[] { _embers, _flames, _smoke })
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            if (_elapsed >= Duration + ParticleTail)
            {
                gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
            }
        }

        void BuildEmbers()
        {
            var sparks = new GameObject("Burning embers");
            sparks.transform.SetParent(transform, false);
            sparks.transform.rotation = Quaternion.Euler(-90, 0, 0);
            _embers = sparks.AddComponent<ParticleSystem>();
            _embers.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _embers.main;
            main.playOnAwake = false; main.loop = true; main.maxParticles = 32;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.18f, .4f);
            main.startSize = new ParticleSystem.MinMaxCurve(.025f, .05f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.25f, .5f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1,.18f,.01f), new Color(1,.7f,.08f));
            var emission = _embers.emission; emission.rateOverTime = 24;
            var shape = _embers.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_bounds.size.x, _bounds.size.z, .025f);
            var noise = _embers.noise; noise.enabled = true; noise.strength = .1f; noise.frequency = 3;
            var fade = _embers.colorOverLifetime; fade.enabled = true; fade.color = Fade(Color.white, Color.white, .9f);
            var shrink = _embers.sizeOverLifetime; shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var renderer = sparks.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("SlimeFireParticle");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            _flames = BuildPlume("Burning flames", "PackageBurnFlame", 54, 48, .28f, .55f, .18f, .4f);
            var flameMain = _flames.main; flameMain.startSize3D = true;
            flameMain.startSizeX = new ParticleSystem.MinMaxCurve(.09f, .16f);
            flameMain.startSizeY = new ParticleSystem.MinMaxCurve(.19f, .32f);
            flameMain.startSizeZ = .1f;
            var flameColor = _flames.colorOverLifetime; flameColor.enabled = true;
            flameColor.color = Fade(new Color(1,.9f,.4f), new Color(1,.22f,.025f), .9f);
            var flameSize = _flames.sizeOverLifetime; flameSize.enabled = true;
            flameSize.size = new ParticleSystem.MinMaxCurve(1,
                new AnimationCurve(new Keyframe(0, .5f), new Keyframe(.2f, 1), new Keyframe(1, .15f)));

            _smoke = BuildPlume("Burning smoke", "PackageBurnSmoke", 14, 24, .45f, .75f, .12f, .24f);
            var smokeMain = _smoke.main; smokeMain.startSize = new ParticleSystem.MinMaxCurve(.09f, .16f);
            smokeMain.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var smokeColor = _smoke.colorOverLifetime; smokeColor.enabled = true;
            smokeColor.color = Fade(new Color(.24f,.22f,.2f), new Color(.38f,.37f,.36f), .38f);
            var smokeSize = _smoke.sizeOverLifetime; smokeSize.enabled = true;
            smokeSize.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .5f, 1, 2));

            if (Application.isPlaying)
            {
                _embers.Play(); _flames.Play(); _smoke.Play();
            }
            var light = new GameObject("Burn glow"); light.transform.SetParent(transform, false);
            _glow = light.AddComponent<Light>(); _glow.type = LightType.Point;
            _glow.color = new Color(1, .25f, .015f); _glow.range = 1.25f; _glow.shadows = LightShadows.None;
        }

        ParticleSystem BuildPlume(string name, string material, float rate, int limit,
            float minLife, float maxLife, float minSpeed, float maxSpeed)
        {
            var source = new GameObject(name); source.transform.SetParent(transform, false);
            source.transform.rotation = Quaternion.Euler(-90, 0, 0);
            var particles = source.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = true; main.maxParticles = limit;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLife, maxLife);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startColor = Color.white;
            var emission = particles.emission; emission.rateOverTime = rate;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(_bounds.size.x, _bounds.size.z, .025f);
            var noise = particles.noise;
            noise.enabled = true; noise.strength = .065f; noise.frequency = 2; noise.scrollSpeed = .6f;
            var renderer = source.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = Resources.Load<Material>(material);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            return particles;
        }

        static Gradient Fade(Color start, Color end, float opacity)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(start, 0), new GradientColorKey(end, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(opacity, .15f),
                    new GradientAlphaKey(opacity * .65f, .55f), new GradientAlphaKey(0, 1) });
            return gradient;
        }
    }
}
