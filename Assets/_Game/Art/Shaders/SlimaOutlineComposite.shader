// 描边合成（重写版）：一张"这一组物体的遮罩" → 外轮廓描边 / 分组填色测试 / 最终像素化
//
// 设计原则（吸取上一版教训）：
//   · 一个 pass 只做一件事，遮罩与合成在同一个 Execute 内 → 顺序不可能乱
//   · 遮罩贴图**直接绑在材质上**（不走全局），每个 pass 独立材质实例 → 组之间不会互相覆盖
//   · 遮罩语义统一：**1 = 背景，0 = 该组物体**（轮廓只画在"背景且邻域有物体"的像素上 → 天生只向外）
//   · 像素化单独一步（_PixelatePass = 1），所以描边线是清晰的；"清晰遮罩"内的像素不参与像素化

Shader "Slima/Outline Composite"
{
    Properties
    {
        _OutlineColor ("描边颜色", Color) = (0, 0, 0, 1)
        _OutlineThickness ("描边粗细（屏幕像素）", Range(0, 32)) = 3

        _PixelatePass ("是否像素化这一步（0=只描边 1=像素化）", Range(0, 1)) = 0
        _PixelSize ("像素块大小（屏幕像素）", Range(1, 16)) = 2
        _PixelateAmount ("像素化强度", Range(0, 1)) = 1

        _CrispDilate ("清晰物件外扩（像素，让外轮廓也清晰）", Range(0, 32)) = 3

        _DebugMode ("调试（0正常 1分组填色 2遮罩读数）", Range(0, 2)) = 0
        _DebugTint ("调试颜色", Color) = (1, 0.25, 0.25, 1)

        _SlimaMask ("组遮罩（1=背景 0=该组物体）", 2D) = "white" {}
        _SlimaCrispMask ("清晰遮罩（1=不参与像素化）", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "OutlineComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_LinearClamp);
            float4 _BlitScaleBias;

            TEXTURE2D(_SlimaMask);
            SAMPLER(sampler_SlimaMask);
            TEXTURE2D(_SlimaCrispMask);
            SAMPLER(sampler_SlimaCrispMask);
            // 保护遮罩：1 = 这里永远不画线（主角所在处 → 保证角色永远清晰）
            TEXTURE2D(_SlimaProtectMask);
            SAMPLER(sampler_SlimaProtectMask);

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineThickness;
                float _PixelatePass;
                float _PixelSize;
                float _PixelateAmount;
                float _CrispDilate;
                float _MaskFar;
                float _DebugMode;
                half4 _DebugTint;
            CBUFFER_END

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            // 全屏三角形（照 core 的 Blit.hlsl 写一份，避免多 include 引起符号冲突）
            Varyings Vert(Attributes input)
            {
                float2 p = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                Varyings output;
                output.positionCS = float4(p * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
            #if UNITY_UV_STARTS_AT_TOP
                output.uv = float2(p.x, 1.0 - p.y);
            #else
                output.uv = p;
            #endif
                return output;
            }

            float3 SceneColor(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv * _BlitScaleBias.xy + _BlitScaleBias.zw).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv * _BlitScaleBias.xy + _BlitScaleBias.zw;
                float2 texel = _OutlineThickness * (_PixelatePass > 0.5 ? _PixelSize : 1.0) / _ScreenParams.xy;

                // ---------------- 最终像素化这一步 ----------------
                if (_PixelatePass > 0.5)
                {
                    float2 virtualRes = max(_ScreenParams.xy / max(1.0, _PixelSize), 1.0);
                    float2 pixelUV = (floor(uv * virtualRes) + 0.5) / virtualRes;

                    // 清晰遮罩：这些像素不参与像素化（外扩一圈，让它们的外轮廓也保持清晰）
                    float crisp = SAMPLE_TEXTURE2D(_SlimaCrispMask, sampler_SlimaCrispMask, uv).r;
                    float2 ctexel = _CrispDilate / _ScreenParams.xy;
                    crisp = max(crisp, max(max(
                        SAMPLE_TEXTURE2D(_SlimaCrispMask, sampler_SlimaCrispMask, uv + float2(-ctexel.x, 0)).r,
                        SAMPLE_TEXTURE2D(_SlimaCrispMask, sampler_SlimaCrispMask, uv + float2(ctexel.x, 0)).r),
                        max(SAMPLE_TEXTURE2D(_SlimaCrispMask, sampler_SlimaCrispMask, uv + float2(0, -ctexel.y)).r,
                            SAMPLE_TEXTURE2D(_SlimaCrispMask, sampler_SlimaCrispMask, uv + float2(0, ctexel.y)).r)));

                    float2 sampleUV = lerp(lerp(uv, pixelUV, _PixelateAmount), uv, step(0.5, crisp));
                    return half4(SceneColor(sampleUV), 1);
                }

                // ---------------- 描边（每组一次） ----------------
                float mC = SAMPLE_TEXTURE2D(_SlimaMask, sampler_SlimaMask, uv).r;      // 1 = 背景，0 = 该组物体

                // 调试 2：遮罩读数（红 = 物体，绿 = 背景）
                if (_DebugMode > 1.5)
                {
                    return half4(1.0 - mC, mC, 0.0, 1);
                }

                // 调试 1：把这一组的物体整体填色（一眼看出识别范围）
                if (_DebugMode > 0.5)
                {
                    return half4(lerp(SceneColor(uv), _DebugTint.rgb, saturate(1.0 - mC)), 1);
                }

                // 外轮廓：自己是背景 + 邻域里有该组物体 + **那个物体没有被更近的东西挡住**
                //   （遮罩 G 通道存了该物体的深度；与相机深度图比较 → 主角站上去、墙挡在前面都不会画线）
                float4 mx0 = SAMPLE_TEXTURE2D(_SlimaMask, sampler_SlimaMask, uv + float2(-texel.x, 0));
                float4 mx1 = SAMPLE_TEXTURE2D(_SlimaMask, sampler_SlimaMask, uv + float2(texel.x, 0));
                float4 my0 = SAMPLE_TEXTURE2D(_SlimaMask, sampler_SlimaMask, uv + float2(0, -texel.y));
                float4 my1 = SAMPLE_TEXTURE2D(_SlimaMask, sampler_SlimaMask, uv + float2(0, texel.y));

                float mMin = min(min(mx0.r, mx1.r), min(my0.r, my1.r));      // 0 = 邻域里有该组物体

                // 取"最近的那个邻域物体"的深度
                float groupDepth = 1.0;
                groupDepth = min(groupDepth, mx0.r < 0.5 ? mx0.g : 1.0);
                groupDepth = min(groupDepth, mx1.r < 0.5 ? mx1.g : 1.0);
                groupDepth = min(groupDepth, my0.r < 0.5 ? my0.g : 1.0);
                groupDepth = min(groupDepth, my1.r < 0.5 ? my1.g : 1.0);

                // 相机深度图（归一化到同一尺度）。深度图不可用时线性化结果会偏大 → 退化为"总是画线"（不会更糟）
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams) / max(1e-3, _MaskFar);
                // 容差：遮罩是半浮点格式（精度足够），用很小的容差即可分辨"叠在一起"的物品
                //   （主角站在出口上差 ~1 单位 → 归一化后 ~0.0025，所以容差必须小于它）
                float visible = (sceneDepth >= groupDepth - 0.002) ? 1.0 : 0.0;

                float outline = (mC > 0.5 && mMin < 0.5 && visible > 0.5) ? 1.0 : 0.0;

                // 保护遮罩（主角）：任何组的线都不许画在这里 → 主角永远压住线条
                float protect = SAMPLE_TEXTURE2D(_SlimaProtectMask, sampler_SlimaProtectMask, uv).r;
                outline *= (protect < 0.5) ? 1.0 : 0.0;
                float3 color = lerp(SceneColor(uv), _OutlineColor.rgb, outline * _OutlineColor.a);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
