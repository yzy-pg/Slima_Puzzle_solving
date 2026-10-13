Shader "BlockMap/Beam (Flowing Noise)"
{
    Properties
    {
        _NoiseTex ("Flowing Noise Texture", 2D) = "gray" {}
        _FalloffTex ("Glow Gradient Falloff", 2D) = "white" {}
        _NoiseScale ("Noise Scale", Float) = 0.3
        _NoiseSpeed ("Noise Speed", Float) = 0.7
        _NoiseStrength ("Noise Brightness Variation", Range(0,1)) = 0.8
        _NoiseTransparency ("Noise Transparency", Range(0,1)) = 0.85
        _Opacity ("Overall Opacity", Range(0,1)) = 1
        _CoreWhiteness ("Core Saturation Blend", Range(0,1)) = 1
        _CoreWidth ("Bright Core Width", Range(0.1,0.7)) = 0.42
        _CoreBrightness ("Core Minimum Brightness", Float) = 1.15
        _CoreMinOpacity ("Core Minimum Opacity", Range(0,1)) = 1
        _ScreenPixelSize ("Screen Pixel Block Size", Range(1,16)) = 2
        _NoiseThreshold ("Noise Hard Cutoff", Range(0,1)) = 0.46
        _GlowEdge ("Glow Hard Edge", Range(0.1,1)) = 0.78
        _EdgeNoiseStrength ("Edge Noise Distortion", Range(0,1)) = 0.25
        _FlowDirection ("World Flow Direction", Vector) = (1,0,0,0)
        [HideInInspector] _BandColor ("Saturated Dark Band", Color) = (0.214041,0.214041,0.214041,1)
        [HideInInspector] _CoreColor ("Saturated Bright Core", Color) = (1,1,1,1)
        _GlowWidth ("Glow Width Multiplier", Float) = 5
        _GlowIntensity ("Glow Intensity", Float) = 1.2
        _ParticleMode ("Particle Shape", Float) = 0
        _GlowMode ("Glow Shape", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "Beam"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_FalloffTex);
            SAMPLER(sampler_FalloffTex);
            CBUFFER_START(UnityPerMaterial)
                float _NoiseScale, _NoiseSpeed, _NoiseStrength;
                float _CoreWhiteness, _ParticleMode, _ScreenPixelSize, _NoiseThreshold, _GlowEdge;
                float _CoreWidth, _CoreBrightness, _CoreMinOpacity;
                float4 _FlowDirection, _BandColor, _CoreColor;
                float _NoiseTransparency, _Opacity, _EdgeNoiseStrength, _GlowWidth, _GlowIntensity, _GlowMode;
                float _SrcBlend, _DstBlend;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float clipW : TEXCOORD2;
                half4 color : COLOR;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.clipW = output.positionCS.w;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_ParticleMode > 0.5)
                {
                    // Unfiltered, unrotated billboard squares read as individual pixel points.
                    return half4(lerp(input.color.rgb, half3(1,1,1), 0.45) * 1.8, input.color.a);
                }
                // Evaluate the moving texture at one shared sample per screen block.
                // Weighted derivatives reconstruct that sample correctly in perspective too.
                float pixelSize = max(1, floor(_ScreenPixelSize + 0.5));
                float2 pixelCenter = (floor(input.positionCS.xy / pixelSize) + 0.5) * pixelSize;
                float2 offset = pixelCenter - input.positionCS.xy;
                float inverseW = rcp(max(0.0001, input.clipW));
                float sampleW = inverseW + ddx(inverseW) * offset.x + ddy(inverseW) * offset.y;
                float3 weightedPosition = input.positionWS * inverseW;
                float3 pixelPosition = (weightedPosition + ddx(weightedPosition) * offset.x
                    + ddy(weightedPosition) * offset.y) / max(0.0001, sampleW);
                float2 weightedUV = input.uv * inverseW;
                float2 pixelUV = (weightedUV + ddx(weightedUV) * offset.x
                    + ddy(weightedUV) * offset.y) / max(0.0001, sampleW);
                float2 flow = pixelPosition.xz - _FlowDirection.xz * _Time.y * _NoiseSpeed;
                float noise = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, flow * _NoiseScale, 0).r;
                float noiseAlpha = lerp(1, step(_NoiseThreshold, noise), _NoiseTransparency) * _Opacity;
                float noiseBrightness = lerp(1, 0.3 + noise * 1.4, _NoiseStrength);
                float across = abs(pixelUV.y * 2 - 1);
                if (_GlowMode > 0.5)
                {
                    float falloff = SAMPLE_TEXTURE2D_LOD(_FalloffTex, sampler_FalloffTex,
                        float2(saturate(across / max(0.001, _GlowEdge)), 0.5), 0).r;
                    float halo = (1 - step(_GlowEdge, across)) * falloff;
                    return half4(input.color.rgb * _GlowIntensity * noiseBrightness,
                        halo * noiseAlpha * input.color.a);
                }
                float warpedAcross = across + (noise - 0.5) * _EdgeNoiseStrength * step(0.35, across);
                float gradient = saturate((0.75 - warpedAcross) / max(0.01, 0.75 - _CoreWidth));
                half3 core = lerp(input.color.rgb, _CoreColor.rgb, _CoreWhiteness * gradient);
                float filaments = step(0.58, noise) * step(0.4, across)
                    * (1 - step(0.8, warpedAcross)) * _EdgeNoiseStrength;
                float alpha = (1 - step(1, warpedAcross)) * noiseAlpha * input.color.a;
                // Keep a readable saturated spine even when a sample falls beside the thin core.
                float spine = 1 - step(_CoreWidth, abs(input.uv.y * 2 - 1));
                half3 rgb = lerp(_BandColor.rgb, core * noiseBrightness, gradient) + input.color.rgb * filaments;
                rgb = lerp(rgb, _CoreColor.rgb * max(_CoreBrightness, noiseBrightness), spine);
                alpha = lerp(alpha, max(noiseAlpha, _CoreMinOpacity * _Opacity) * input.color.a, spine);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
