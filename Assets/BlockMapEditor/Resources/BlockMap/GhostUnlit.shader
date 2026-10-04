// URP 版：放置虚影（纯色半透明）
Shader "BlockMap/GhostUnlit"
{
    Properties
    {
        _Color ("Color", Color) = (0.55, 0.85, 1, 0.35)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "GhostUnlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}