// Lit transparent blocks. Keep the legacy shader name/GUID for existing baked materials.
Shader "BlockMap/BlockUnlitTransparent"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        _AmbientColor ("Ambient Color", Color) = (.16, .18, .24, 1)
        _ShadowColor ("Shadow Color", Color) = (.42, .47, .6, 1)
        _LightTint ("Light Tint", Color) = (1, 1, 1, 1)
        _Bands ("Lighting Bands", Range(2,8)) = 5
        _LightingMode ("Lighting Mode", Float) = 0
        _Band0 ("Band 0", Float) = 0
        _Band1 ("Band 1", Float) = .25
        _Band2 ("Band 2", Float) = .5
        _Band3 ("Band 3", Float) = .75
        _Band4 ("Band 4", Float) = 1
        _Brightness ("Brightness", Float) = 1
        _LightInputMin ("Light Input Min", Float) = 0
        _LightInputMax ("Light Input Max", Float) = 1
        _LightContrast ("Light Contrast", Float) = 1
        _ShadowInfluence ("Shadow Influence", Range(0,1)) = 1
        _LightWrap ("Light Wrap", Range(0,1)) = .15
        _ExtraLightMode ("Extra Light Mode", Range(0,1)) = 1
        _NoiseStrength ("Noise Strength", Range(0,1)) = .35
        _NoiseWorldSize ("Noise World Size", Float) = .06
        _NoiseSeed ("Noise Seed", Float) = 0
        _FallbackLight ("Fallback Light", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Cull [_Cull]
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "ForwardLitTransparent"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                float4 color : COLOR;
            };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float _Cull;
                half4 _AmbientColor, _ShadowColor, _LightTint;
                float _Bands, _LightingMode, _Band0, _Band1, _Band2, _Band3, _Band4;
                float _Brightness, _LightInputMin, _LightInputMax, _LightContrast;
                float _ShadowInfluence, _LightWrap, _ExtraLightMode;
                float _NoiseStrength, _NoiseWorldSize, _NoiseSeed, _FallbackLight;
            CBUFFER_END
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            #include "Assets/_Game/Art/Shaders/SlimaToonLighting.hlsl"

            half3 ExtraLight(uint index, float3 positionWS, float3 normalWS)
            {
                #if USE_FORWARD_PLUS
                    int settingsIndex = index;
                #else
                    int settingsIndex = GetPerObjectLightIndex(index);
                #endif
                float3 samplePosition = PixelLightSamplePosition(positionWS, normalWS,
                    _SlimaAdditionalLightSettings[settingsIndex], _SlimaAdditionalLightNoise[settingsIndex]);
                Light light = GetAdditionalLight(index, samplePosition, half4(1,1,1,1));
                float brightness = LightBrightness(light.color);
                float wrapped = saturate((dot(normalWS, light.direction) + _LightWrap) / (1 + _LightWrap));
                wrapped = lerp(wrapped, 1, saturate(_ExtraLightMode));
                float raw = wrapped * light.distanceAttenuation *
                    lerp(1, light.shadowAttenuation, _ShadowInfluence) * brightness;
                float band = EvaluateToonLight(raw, positionWS, normalWS,
                    _SlimaAdditionalLightSettings[settingsIndex], _SlimaAdditionalLightNoise[settingsIndex]);
                return light.color / max(.0001, brightness) * _LightTint.rgb * band;
            }
            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float3 normalWS = normalize(i.normalWS);
                float3 mainPosition = PixelLightSamplePosition(i.positionWS, normalWS,
                    _SlimaMainLightSettings, _SlimaMainLightNoise);
                // The position-aware overload honors shadow fade and URP's transparent-shadow settings.
                Light main = GetMainLight(TransformWorldToShadowCoord(mainPosition), mainPosition, half4(1,1,1,1));
                if (_FallbackLight > .5 && dot(main.color, half3(1,1,1)) < .001)
                {
                    main.color = 1;
                    main.direction = normalize(float3(.35,.9,.25));
                    main.distanceAttenuation = main.shadowAttenuation = 1;
                }
                float brightness = LightBrightness(main.color);
                float wrapped = saturate((dot(normalWS, main.direction) + _LightWrap) / (1 + _LightWrap));
                float raw = wrapped * main.distanceAttenuation * lerp(1, main.shadowAttenuation, _ShadowInfluence) * brightness;
                float band = EvaluateToonLight(raw, i.positionWS, normalWS, _SlimaMainLightSettings, _SlimaMainLightNoise);
                half3 lighting = lerp(_ShadowColor.rgb, main.color / max(.0001, brightness) * _LightTint.rgb, band);
                lighting += max(SampleSH(normalWS), _AmbientColor.rgb * _AmbientColor.a);
                #if USE_FORWARD_PLUS
                    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                    {
                        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                        lighting += ExtraLight(lightIndex, i.positionWS, normalWS);
                    }
                #endif
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    lighting += ExtraLight(lightIndex, i.positionWS, normalWS);
                LIGHT_LOOP_END
                return half4(MixFog(c.rgb * i.color.rgb * lighting, i.fogFactor), c.a * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
