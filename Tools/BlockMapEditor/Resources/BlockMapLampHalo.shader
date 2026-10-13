Shader "BlockMap/Lamp Halo (Pixel Additive)"
{
    Properties
    {
        [HideInInspector] _MainTex ("Legacy Shape Mask (unused)", 2D) = "white" {}
        [HDR] _Color ("Halo Color", Color) = (1,1,1,1)
        _Size ("World Diameter", Float) = 1.8
        _CameraOffset ("Offset Toward Camera", Float) = 0.9
        _Intensity ("Intensity", Float) = 0.6
        [HideInInspector] _FrontOnly ("Optical Face Visibility", Float) = 0
        [HideInInspector] _FaceNormal ("Optical Face Normal", Vector) = (0,0,1,0)
        [HideInInspector] _Bands ("Legacy Brightness Bands (unused)", Vector) = (0.15,0.4,1,0)
        [HideInInspector] _PixelCount ("Legacy Pixels (unused)", Range(4,128)) = 32
        [HideInInspector] _TransitionWidth ("Legacy Noise Width (unused)", Range(0,1)) = 0.65
        _GradientPower ("Radial Falloff Power", Range(0.1,4)) = 1
        [HideInInspector] _Seed ("Legacy Noise Seed (unused)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "LampHalo"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Bands;
                float4 _FaceNormal;
                float _Size, _CameraOffset, _Intensity, _PixelCount;
                float _TransitionWidth, _GradientPower, _Seed;
                float _FrontOnly;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float faceVisibility : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 center = TransformObjectToWorld(float3(0,0,0));
                float3 right = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 up = UNITY_MATRIX_I_V._m01_m11_m21;
                float3 towardCamera = UNITY_MATRIX_I_V._m02_m12_m22;
                float3 position = center + (right * input.positionOS.x + up * input.positionOS.y) * _Size
                    + towardCamera * _CameraOffset;
                output.positionCS = TransformWorldToHClip(position);
                output.uv = input.uv;
                output.faceVisibility = _FrontOnly > .5 ? dot(_FaceNormal.xyz, towardCamera) - .01 : 1;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                clip(input.faceVisibility);
                float radial = saturate(1 - length(input.uv * 2 - 1));
                float brightness = pow(smoothstep(0, 1, radial), max(.1, _GradientPower));
                return half4(_Color.rgb * max(0, _Intensity) * brightness, 0);
            }
            ENDHLSL
        }
    }
}
