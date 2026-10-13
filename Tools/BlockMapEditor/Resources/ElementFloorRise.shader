Shader "BlockMap/ElementFloorRise"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, .32, .055, 1)
        _Intensity ("Emission Intensity", Float) = 6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Cull Off
        ZWrite Off
        // Linear dodge: add the premultiplied emissive RGB without dimming the background.
        Blend One One
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float edge = min(min(i.uv.x, 1-i.uv.x), min(i.uv.y, 1-i.uv.y));
                clip(.026 - edge);
                return half4(_Color.rgb * (_Intensity * _Color.a), 0);
            }
            ENDHLSL
        }
    }
}
