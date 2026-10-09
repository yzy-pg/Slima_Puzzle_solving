using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlimaPuzzle.Rendering
{
    // Uses URP's visible-light order, so overlapping lights keep independent settings.
    public sealed class SlimaToonLightingFeature : ScriptableRendererFeature
    {
        static readonly int EnabledId = Shader.PropertyToID("_SlimaLightSettingsEnabled");
        LightingSettingsPass pass;

        public override void Create()
        {
            pass = new LightingSettingsPass();
            RenderPipelineManager.beginCameraRendering -= ResetCameraSettings;
            RenderPipelineManager.beginCameraRendering += ResetCameraSettings;
        }

        void ResetCameraSettings(ScriptableRenderContext context, Camera camera)
        {
            // Cameras using another renderer must fall back to material settings.
            Shader.SetGlobalFloat(EnabledId, 0f);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing)
        {
            RenderPipelineManager.beginCameraRendering -= ResetCameraSettings;
            Shader.SetGlobalFloat(EnabledId, 0f);
        }

        sealed class LightingSettingsPass : ScriptableRenderPass
        {
            static readonly Vector4 MaterialDefaults = new Vector4(-1f, 0f, 0f, 1f);
            static readonly int MainSettingsId = Shader.PropertyToID("_SlimaMainLightSettings");
            static readonly int MainNoiseId = Shader.PropertyToID("_SlimaMainLightNoise");
            static readonly int ExtraSettingsId = Shader.PropertyToID("_SlimaAdditionalLightSettings");
            static readonly int ExtraNoiseId = Shader.PropertyToID("_SlimaAdditionalLightNoise");
            readonly Vector4[] settings = new Vector4[UniversalRenderPipeline.maxVisibleAdditionalLights];
            readonly Vector4[] noise = new Vector4[UniversalRenderPipeline.maxVisibleAdditionalLights];

            public LightingSettingsPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            }

            static void ReadSettings(Light light, out Vector4 lightSettings, out Vector4 noiseSettings)
            {
                lightSettings = MaterialDefaults;
                noiseSettings = Vector4.zero;
                if (light != null && light.TryGetComponent(out SlimaToonLight control) && control.isActiveAndEnabled)
                {
                    lightSettings = control.ModeSettings;
                    noiseSettings = control.NoiseSettings;
                }
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var visible = renderingData.lightData.visibleLights;
                int mainIndex = renderingData.lightData.mainLightIndex;
                Vector4 mainSettings = MaterialDefaults;
                Vector4 mainNoise = Vector4.zero;
                int extraIndex = 0;
                for (int i = 0; i < visible.Length; i++)
                {
                    if (i == mainIndex)
                        ReadSettings(visible[i].light, out mainSettings, out mainNoise);
                    else if (extraIndex < settings.Length)
                    {
                        ReadSettings(visible[i].light, out settings[extraIndex], out noise[extraIndex]);
                        extraIndex++;
                    }
                }

                var cmd = CommandBufferPool.Get("Slima Toon Light Settings");
                cmd.SetGlobalFloat(EnabledId, 1f);
                cmd.SetGlobalVector(MainSettingsId, mainSettings);
                cmd.SetGlobalVector(MainNoiseId, mainNoise);
                cmd.SetGlobalVectorArray(ExtraSettingsId, settings);
                cmd.SetGlobalVectorArray(ExtraNoiseId, noise);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
