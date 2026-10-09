using UnityEngine;

namespace SlimaPuzzle.Rendering
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Light))]
    [AddComponentMenu("Slima/Rendering/Slima Toon Light")]
    public sealed class SlimaToonLight : MonoBehaviour
    {
        public enum LightingMode { Pixel = 0, Smooth = 1 }
        public enum NoiseSpace { World = 0, Screen = 1 }

        [Tooltip("Pixel：阶梯光照和边界像素噪波；Smooth：连续光照。仅影响 Slima Toon 材质。")]
        public LightingMode mode = LightingMode.Pixel;

        [Tooltip("关闭时只覆盖光照模式，其他数值使用被照射材质的设置。")]
        public bool overrideMaterialSettings;

        [Range(2, 8)] public int bands = 5;
        [Range(0f, 1f), Tooltip("过渡宽度占一个阶梯的比例；0 为硬边。")]
        public float transitionWidth = 0.35f;
        public NoiseSpace noiseSpace = NoiseSpace.Screen;
        [Min(0.002f)] public float worldPixelSize = 0.06f;
        [Min(1f)] public float screenPixelSize = 3f;
        [Min(0f)] public float noiseSeed;
        [Min(0f), Tooltip("量化前的亮度倍率；Unity Light 的颜色、强度、范围仍然生效。")]
        public float brightness = 1f;

        internal Vector4 ModeSettings => new Vector4((float)mode,
            overrideMaterialSettings ? Mathf.Clamp(bands, 2, 8) : 0,
            Mathf.Clamp01(transitionWidth), Mathf.Max(0f, brightness));

        internal Vector4 NoiseSettings => new Vector4(Mathf.Max(0.002f, worldPixelSize),
            Mathf.Max(1f, screenPixelSize), (float)noiseSpace, noiseSeed);
    }
}
