using UnityEngine;
using UnityEngine.Rendering;

namespace BlockMap
{
    // Orientation is computed in the vertex shader for each rendering camera.
    // No Camera.main dependency, collider, or additional Unity Light.
    [DisallowMultipleComponent]
    public sealed class BlockMapLampHalo : MonoBehaviour
    {
        [SerializeField] BlockDefinition definition;
        Mesh mesh;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock properties;
        bool directSettings;
        Color directColor;
        float directSize, directIntensity, directOffset;

        public void Configure(BlockDefinition source)
        {
            definition = source;
            directSettings = false;
            if (EnsureRenderer()) ApplySettings();
        }

        // Optical props share the lamp halo renderer without creating temporary block definitions.
        public void Configure(Color color, float size, float intensity, float cameraOffset)
        {
            definition = null;
            directSettings = true;
            directColor = color; directSize = size;
            directIntensity = intensity; directOffset = cameraOffset;
            if (EnsureRenderer()) ApplySettings();
        }

        bool EnsureRenderer()
        {
            var material = Resources.Load<Material>("BlockMapLampHalo");
            if (material == null)
            {
                Debug.LogError("Missing Resources/BlockMapLampHalo material.", this);
                enabled = false;
                return false;
            }
            if (mesh == null)
            {
                mesh = new Mesh { name = "Lamp halo quad", hideFlags = HideFlags.DontSave };
                mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0),
                    new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
                mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                properties = new MaterialPropertyBlock();
            }
            meshRenderer.sharedMaterial = material;
            return true;
        }

        void LateUpdate() => ApplySettings();

        void ApplySettings()
        {
            if ((!directSettings && definition == null) || meshRenderer == null) return;
            meshRenderer.enabled = directSettings ? directIntensity > 0 : definition.lampHalo;
            float size = Mathf.Max(.01f, directSettings ? directSize : definition.lampHaloSize);
            float offset = Mathf.Max(0, directSettings ? directOffset : definition.lampHaloCameraOffset);
            properties.SetColor("_Color", directSettings ? directColor : definition.lightColor * definition.lampHaloTint);
            properties.SetFloat("_Size", size);
            properties.SetFloat("_CameraOffset", offset);
            properties.SetFloat("_Intensity", Mathf.Max(0, directSettings ? directIntensity : definition.lampHaloIntensity));
            properties.SetFloat("_GradientPower", directSettings ? 1f : Mathf.Clamp(definition.lampHaloGradientPower, .1f, 4));
            properties.SetFloat("_FrontOnly", directSettings ? 1f : 0f);
            properties.SetVector("_FaceNormal", transform.forward);
            meshRenderer.SetPropertyBlock(properties);
            // Billboard rotation/offset happen on the GPU; culling must cover every orientation.
            float extent = size * Mathf.Sqrt(2) + offset * 2;
            Vector3 scale = transform.lossyScale;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(extent / Mathf.Max(.001f, Mathf.Abs(scale.x)),
                extent / Mathf.Max(.001f, Mathf.Abs(scale.y)), extent / Mathf.Max(.001f, Mathf.Abs(scale.z))));
        }

        void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}
