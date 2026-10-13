Shader "BlockMap/Package Burn Particle"
{
    Properties
    {
        _NoiseTex ("Flow noise", 2D) = "gray" {}
        _Smoke ("Smoke plume", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float _Smoke;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv; output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = (floor(input.uv * float2(12, 20)) + .5) / float2(12, 20);
                float2 p = uv * 2 - 1;
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                    uv * 1.7 + float2(0, -_Time.y * .8)).r;
                if (_Smoke > .5)
                {
                    float mask = 1 - smoothstep(.55, .9, length(p) + (noise - .5) * .3);
                    return half4(input.color.rgb, input.color.a * mask);
                }
                float width = pow(saturate(1 - uv.y), .7) * .9;
                float shape = min(width - abs(p.x + sin(uv.y * 5 - _Time.y * 3) * uv.y * .12)
                    + (noise - .5) * .35, uv.y - .025);
                clip(shape - .01);
                float heat = floor(saturate(shape * 3 + (1 - uv.y) * .25) * 4) / 4;
                half3 fire = lerp(half3(1, .12, .008), half3(1, .82, .14), heat);
                return half4(fire * input.color.rgb * 1.3, input.color.a);
            }
            ENDHLSL
        }
    }
}
