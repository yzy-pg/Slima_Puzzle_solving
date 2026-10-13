Shader "BlockMap/Slime Flowing Halo"
{
    Properties
    {
        _Size ("Diameter", Float) = 1.7
        _Intensity ("Intensity", Float) = 0.36
        _CoreColor ("Orange center", Color) = (1,0.38,0.025,1)
        _EdgeColor ("Red edge", Color) = (1,0.035,0.005,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SlimeFireFlow.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Size, _Intensity;
                half4 _CoreColor, _EdgeColor;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 center = TransformObjectToWorld(float3(0,0,0));
                float3 right = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 up = UNITY_MATRIX_I_V._m01_m11_m21;
                output.positionCS = TransformWorldToHClip(center +
                    (right * input.positionOS.x + up * input.positionOS.y) * _Size);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2 - 1;
                float flow = SlimeFireFlow(p, _Time.y);
                float radius = length(p) + (flow - 0.5) * 0.16;
                float radial = saturate(1 - radius);
                half3 tint = lerp(_EdgeColor.rgb, _CoreColor.rgb, smoothstep(0.1, 0.85, radial));
                float brightness = radial * radial * (0.65 + flow * 0.7);
                return half4(tint * brightness * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
