using UnityEngine;

namespace BlockMap.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class GameplayOpticGlow : MonoBehaviour
    {
        public Color color = Color.white;
        public bool receiver;
        [Min(.01f)] public float diameter = .65f;
        [Min(0)] public float intensity = .65f;
        [Min(0)] public float cameraOffset = .12f;
        public Renderer opticalSurface;
        [Min(0)] public float emissionStrength = 1.6f;
        [Min(0)] public float lightRange = 4f;
        [Min(0)] public float lightIntensity = 3.2f;
        [Range(1,179)] public float lightAngle = 90f;
        [Range(0,89)] public float lightTilt = 25f;
        public Vector3 lightLocalPosition = new Vector3(0,.5f,.55f);
        BlockMapLampHalo[] halos;
        Light[] faceLights;
        MaterialPropertyBlock surfaceProperties;
        bool activated;
        int receivingFaces;

        void Start() => Initialize();

        public void Initialize()
        {
            if (halos != null) return;
            halos = new BlockMapLampHalo[receiver ? 4 : 1];
            faceLights = new Light[halos.Length];
            for (int i=0;i<halos.Length;i++)
            {
                var face = new GameObject("OpticalGlow_"+i);
                face.transform.SetParent(transform,false);
                face.transform.localPosition = Quaternion.Euler(0,i*90,0)*new Vector3(0,.5f,.52f);
                face.transform.localRotation = Quaternion.Euler(0,i*90,0);
                halos[i] = face.AddComponent<BlockMapLampHalo>();
                var lightObject = new GameObject("OpticalLight_"+i);
                lightObject.transform.SetParent(transform,false);
                lightObject.transform.localPosition = Quaternion.Euler(0,i*90,0)*lightLocalPosition;
                lightObject.transform.localRotation = Quaternion.Euler(0,i*90,0)*Quaternion.Euler(lightTilt,0,0);
                faceLights[i] = lightObject.AddComponent<Light>();
                faceLights[i].type = LightType.Spot;
                faceLights[i].shadows = LightShadows.None;
            }
            surfaceProperties = new MaterialPropertyBlock();
            Apply();
        }

        public void SetActivated(bool active)
        {
            activated = active;
            Initialize();
            Apply();
        }

        public void SetReceivingFaces(int faces)
        {
            faces &= 15;
            if (receivingFaces == faces) return;
            receivingFaces = faces;
            Initialize();
            Apply();
        }

        void Apply()
        {
            int litFaces = receiver ? (activated ? receivingFaces : 0) : 1;
            for (int face=0;face<faceLights.Length;face++)
            {
                bool lit = (litFaces & (1 << face)) != 0;
                halos[face].Configure(color,diameter,lit ? intensity : 0f,cameraOffset);
                var light = faceLights[face];
                light.color = color;
                light.range = Mathf.Max(0,lightRange);
                light.intensity = Mathf.Max(0,lightIntensity);
                light.spotAngle = lightAngle;
                light.innerSpotAngle = lightAngle*.6f;
                light.enabled = lit;
            }
            if (opticalSurface != null)
            {
                opticalSurface.GetPropertyBlock(surfaceProperties);
                surfaceProperties.SetFloat("_EmissionStrength",litFaces != 0 ? emissionStrength : 0f);
                surfaceProperties.SetVector("_OpticFaceMask",new Vector4((litFaces & 1)!=0 ? 1:0,(litFaces & 2)!=0 ? 1:0,(litFaces & 4)!=0 ? 1:0,(litFaces & 8)!=0 ? 1:0));
                opticalSurface.SetPropertyBlock(surfaceProperties);
            }
        }
    }
}
