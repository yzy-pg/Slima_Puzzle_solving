// URP 版：序列帧动画方块（岩浆/流水）。贴图用一条横向 1xN 帧序列（_simple 或 atlasOverride）
Shader "BlockMap/BlockAnimated"
{
    Properties
    {
        _MainTex ("Frames", 2D) = "white" {}
        _Frames  ("Frames", Float) = 4
        _Speed   ("FPS", Float) = 8
        _Cutoff  ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull [_Cull]
        ZWrite On
        Pass
        {
            Name "ForwardUnlitAnimated"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST; float _Frames; float _Speed; float _Cutoff; float _Cull;
            CBUFFER_END
            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float n = max(1.0, _Frames);
                float f = floor(frac(_Time.y * _Speed) * n);
                float2 uv = float2((f + i.uv.x) / n, i.uv.y);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                clip(c.a - _Cutoff);
                return half4(c.rgb * i.color.rgb, c.a * i.color.a);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}