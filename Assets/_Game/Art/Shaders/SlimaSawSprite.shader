Shader "Slima/Saw Sprite"
{
    Properties
    {
        [PerRendererData] [MainTexture] _MainTex ("Sprite", 2D) = "white" {}
        [MainColor] _Color ("Tint", Color) = (1,1,1,1)
        _BaseColor ("Gameplay Tint", Color) = (1,1,1,1)
        _EmissionStrength ("Emission", Float) = 0
        _VisibleMinY ("Lowest Visible Sprite Y", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite On
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BaseColor;
                half _EmissionStrength;
                float _VisibleMinY;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; half fog : TEXCOORD1; float bladeY : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color * _Color;
                output.fog = ComputeFogFactor(output.positionCS.z);
                // Each frame uses the full 64px canvas; UV clipping also works when sprites are batched.
                output.bladeY = input.uv.y - .5;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                clip(color.a - .5h);
                clip(input.bladeY - _VisibleMinY);
                color.rgb = MixFog(color.rgb, input.fog);
                return color;
            }
            ENDHLSL
        }
    }
}
