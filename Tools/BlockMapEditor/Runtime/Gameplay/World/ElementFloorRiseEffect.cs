using UnityEngine;
using UnityEngine.Rendering;

namespace BlockMap.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class ElementFloorRiseEffect : MonoBehaviour
    {
        public Color color = new Color(1, .32f, .055f);
        public float surfaceHeight = .12f;
        public const float RiseHeight = .85f;
        public const float Lifetime = 2.4f;
        Mesh mesh;
        MeshRenderer[] rings;
        MaterialPropertyBlock properties;

        // Let GameplayBlockView collect only the model materials before adding effect renderers.
        void Start() => Initialize();

        public void Initialize()
        {
            if (rings != null) return;
            var material = Resources.Load<Material>("ElementFloorRise");
            if (material == null) { Debug.LogError("Missing ElementFloorRise material.", this); return; }
            mesh = new Mesh { name = "Element floor rectangular pulse", hideFlags = HideFlags.DontSave };
            mesh.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f),
                new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            properties = new MaterialPropertyBlock();
            rings = new MeshRenderer[3];
            for (int i = 0; i < rings.Length; i++)
            {
                var ring = new GameObject("RisingRectangle_" + i);
                ring.transform.SetParent(transform, false);
                ring.AddComponent<MeshFilter>().sharedMesh = mesh;
                rings[i] = ring.AddComponent<MeshRenderer>();
                rings[i].sharedMaterial = material;
                rings[i].shadowCastingMode = ShadowCastingMode.Off;
                rings[i].receiveShadows = false;
            }
            SetPhase(0);
        }

        void LateUpdate() { if (rings != null) SetPhase(Time.time / Lifetime); }

        public void SetPhase(float phase)
        {
            if (rings == null) return;
            for (int i = 0; i < rings.Length; i++)
            {
                float age = Mathf.Repeat(phase + i / (float)rings.Length, 1);
                rings[i].transform.localPosition = new Vector3(0, surfaceHeight + .006f + age * RiseHeight, 0);
                rings[i].transform.localScale = new Vector3(.72f, 1, .72f);
                float alpha = (1 - age) * .9f;
                properties.SetColor("_Color", new Color(color.r, color.g, color.b, alpha));
                rings[i].SetPropertyBlock(properties);
            }
        }

        void OnDestroy()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        }
    }
}
