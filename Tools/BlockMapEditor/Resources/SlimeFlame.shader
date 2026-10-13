Shader "BlockMap/Slime Pixel Flames"
{
    Properties
    {
        _NoiseTex ("Flow noise (point sampled)", 2D) = "gray" {}
        _Width ("Width", Float) = 1.12
        _Height ("Height", Float) = 1.25
        [HideInInspector] _FlowTime ("Preview time (-1 = animate)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
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
                float4 _NoiseTex_ST;
                float _Width, _Height, _FlowTime;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 center = TransformObjectToWorld(float3(0,0,0));
                float3 right = normalize(float3(UNITY_MATRIX_I_V._m00, 0, UNITY_MATRIX_I_V._m20));
                float3 towardCamera = UNITY_MATRIX_I_V._m02_m12_m22;
                // Place the sheet behind the body so the face remains readable from every direction.
                float3 position = center + right * input.positionOS.x * _Width
                    + float3(0, (input.positionOS.y + 0.24) * _Height, 0) - towardCamera * _Width * 0.25;
                output.positionCS = TransformWorldToHClip(position);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Quantize both silhouette and palette into visible pixel blocks.
                float2 uv = (floor(input.uv * float2(32, 48)) + 0.5) / float2(32, 48);
                float time = _FlowTime >= 0 ? _FlowTime : _Time.y;
                float2 scroll = float2(0.07 * sin(time * 0.8), -time * 0.48);
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, uv * float2(1.5, 1.3) + scroll).r;
                float detail = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                    uv * float2(3, 2.2) + scroll * 1.4 + 0.37).r;
                float y = uv.y, x = uv.x * 2 - 1;
                float bend = sin(y * 6 - time * 2.3) * y * y * 0.2;
                float width = pow(saturate(1 - y), 0.68) * 0.94;
                float shape = width - abs(x + bend) + (noise - 0.5) * (0.3 + y * 0.5);
                // Round the base; noise splits the tapered top into rising tongues.
                shape = min(shape, y - 0.025 - abs(x) * 0.09);
                clip(shape - 0.012);
                float heat = saturate(shape * 2.8 + (1 - y) * 0.18 + detail * 0.22);
                heat = floor(heat * 4) / 4;
                half3 color = heat < 0.25 ? half3(1,0.12,0.008)
                    : heat < 0.5 ? half3(1,0.32,0.015)
                    : heat < 0.75 ? half3(1,0.55,0.025) : half3(1,0.76,0.08);
                return half4(color, 0.88);
            }
            ENDHLSL
        }
    }
}
