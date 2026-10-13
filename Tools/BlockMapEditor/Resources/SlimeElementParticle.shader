Shader "BlockMap/Slime Element Particle"
{
    Properties { _Snow ("Snowflake", Float) = 0 }
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
            CBUFFER_START(UnityPerMaterial)
                float _Snow;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2 - 1;
                float radius = length(p);
                float alpha;
                if (_Snow > 0.5)
                {
                    // Fold six arms into one wedge, then draw its stem and two pairs of branches.
                    float angle = atan2(p.y, p.x);
                    angle = abs(frac(angle / 1.04719755 + 0.5) - 0.5) * 1.04719755;
                    float2 q = float2(cos(angle), sin(angle)) * radius;
                    float stem = 1 - smoothstep(0.035, 0.065, q.y);
                    float branch = min(abs(q.y - (q.x - 0.28) * 0.7), abs(q.y - (q.x - 0.55) * 0.7));
                    float twigs = (1 - smoothstep(0.028, 0.055, branch)) * step(0.28, q.x);
                    alpha = max(stem, twigs) * (1 - smoothstep(0.8, 0.94, radius));
                }
                else
                {
                    float ember = length(float2(p.x * 1.4, p.y));
                    alpha = 1 - smoothstep(0.15, 0.95, ember);
                }
                return half4(input.color.rgb, input.color.a * alpha);
            }
            ENDHLSL
        }
    }
}
