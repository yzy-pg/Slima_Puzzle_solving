// 全屏：像素化 + 黑描边（给整幅画面用，尤其是方块地形）
//
//   · 像素化：把屏幕 UV 量化到"虚拟分辨率"（= 屏幕 / _PixelSize），再按量化后的格子取色
//     → 画面变成大颗粒像素，且颗粒是整齐的格子
//   · 描边：在量化后的格子上对**深度**做 Sobel，边缘处涂黑
//     → 轮廓、以及方块与方块之间的直角棱都会出现黑线（这正是方块场景想要的），
//       而且这是屏幕空间描边，不会像反转壳那样在方块棱上裂开
//
// 由 SlimaToonOutlineFeature.cs（URP Renderer Feature）调用，需要 Depth 输入。

Shader "Slima/Toon Outline Post"
{
    Properties
    {
        _OutlineColor ("描边颜色", Color) = (0, 0, 0, 1)
        _PixelSize ("像素块大小（越大越糊）", Range(1, 16)) = 4
        _OutlineThickness ("描边粗细（像素块）", Range(0, 4)) = 1
        _DepthThreshold ("深度阈值（越小线越多）", Range(0.05, 20)) = 1.5
        _PixelateAmount ("像素化强度（0 = 只描边）", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PixelOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // URP 的 Blitter 会设置这两个：源贴图 + 采样缩放偏移
            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_LinearClamp);
            float4 _BlitScaleBias;

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _PixelSize;
                float _OutlineThickness;
                float _DepthThreshold;
                half  _PixelateAmount;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float3 SampleSceneColor(float2 uv)
            {
                float2 blitUV = uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, blitUV).rgb;
            }

            float SampleEyeDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 源 UV（处理 render scale / 偏移）
                float2 sourceUV = input.uv * _BlitScaleBias.xy + _BlitScaleBias.zw;

                // ---- ① 像素化：UV 量化到虚拟分辨率，取像素中心 ----
                float2 virtualRes = max(_ScreenParams.xy / max(1.0, _PixelSize), 1.0);
                float2 pixelUV = (floor(sourceUV * virtualRes) + 0.5) / virtualRes;
                float2 sampledUV = lerp(sourceUV, pixelUV, _PixelateAmount);

                float3 color = SampleSceneColor(sampledUV);

                // ---- ② 描边：在像素格子上对深度做 Sobel ----
                if (_OutlineThickness > 0.001 && _DepthThreshold > 0)
                {
                    float2 texel = _OutlineThickness / virtualRes;

                    // 3x3 深度（用像素格中心采样，避免采到格边界）
                    float d00 = SampleEyeDepth(sampledUV + float2(-texel.x, -texel.y));
                    float d10 = SampleEyeDepth(sampledUV + float2(0.0,      -texel.y));
                    float d20 = SampleEyeDepth(sampledUV + float2(texel.x,  -texel.y));
                    float d01 = SampleEyeDepth(sampledUV + float2(-texel.x,  0.0));
                    float dc  = SampleEyeDepth(sampledUV);
                    float d21 = SampleEyeDepth(sampledUV + float2(texel.x,   0.0));
                    float d02 = SampleEyeDepth(sampledUV + float2(-texel.x,  texel.y));
                    float d12 = SampleEyeDepth(sampledUV + float2(0.0,       texel.y));
                    float d22 = SampleEyeDepth(sampledUV + float2(texel.x,   texel.y));

                    float gx = (d20 + 2.0 * d21 + d22) - (d00 + 2.0 * d01 + d02);
                    float gy = (d02 + 2.0 * d12 + d22) - (d00 + 2.0 * d10 + d20);

                    // 用中心深度归一化：远处的物体不用更大的阈值就会满屏是线
                    float edge = sqrt(gx * gx + gy * gy) / max(dc, 1e-3);
                    float outline = step(_DepthThreshold, edge);

                    color = lerp(color, _OutlineColor.rgb, outline * _OutlineColor.a);
                }

                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
