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
        _OutlineThickness ("描边粗细（像素块）", Range(0, 16)) = 2
        _DepthThreshold ("深度阈值（越小线越多）", Range(0.05, 20)) = 1.5
        _PixelateAmount ("像素化强度（0 = 只描边）", Range(0, 1)) = 1
        _DebugMode ("调试模式（0=正常 1=原始深度 2=边缘 3=线性深度 4=清晰遮罩 6=分组填色）", Range(0, 6)) = 0
        _EdgeSource ("描边依据（0=深度落差 1=颜色亮度 2=遮罩分组 · 推荐）", Range(0, 2)) = 2
        _OutlinePlacement ("描边位置（0=两侧 1=只向外/亮侧 2=只向内/暗侧）", Range(0, 2)) = 1
        _ColorThreshold ("颜色描边阈值（越小线越多）", Range(0.01, 1)) = 0.15
        _DebugTint ("调试用颜色（各组不同，由 Feature 设置）", Color) = (1, 0.2, 0.2, 1)
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
                float _DebugMode;
                float _EdgeSource;
                float _OutlinePlacement;
                float _ColorThreshold;
                half4 _DebugTint;
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

            // 全屏三角形：照 core 包 Runtime/Utilities/Blit.hlsl 的实现写一份。
            // 那两个官方函数不在 URP ShaderLibrary/Core.hlsl 的 include 链里，所以必须自带一份；
            // 这里换成 Slima 前缀，避免万一被间接 include 时和官方符号撞名。
            float4 SlimaFullScreenPos(uint vertexID)
            {
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);   // 0/1/2 → (0,0) (2,0) (0,2)
                return float4(uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
            }

            float2 SlimaFullScreenUV(uint vertexID)
            {
            #if UNITY_UV_STARTS_AT_TOP
                return float2((vertexID << 1) & 2, 1.0 - (vertexID & 2));
            #else
                return float2((vertexID << 1) & 2, vertexID & 2);
            #endif
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = SlimaFullScreenPos(input.vertexID);
                output.uv = SlimaFullScreenUV(input.vertexID);
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

            // 颜色亮度：给"不依赖深度纹理"的描边用
            float Lum(float2 uv)
            {
                float3 c = SampleSceneColor(uv);
                return dot(c, float3(0.299, 0.587, 0.114));
            }

            // 描边遮罩：0 = 物体，1 = 背景（由 SlimaToonOutlineFeature 每次合成前设好）
            TEXTURE2D_X(_SlimaMask);
            SAMPLER(sampler_SlimaMask);

            float SampleMask(float2 uv)
            {
                // 线性采样：遮罩边缘不再逐像素跳变（描边噪点的主要来源）
                return SAMPLE_TEXTURE2D_X(_SlimaMask, sampler_LinearClamp, uv).r;
            }

            // 不参与像素化的物件遮罩：1 = 该像素保持清晰，0 = 正常像素化
            TEXTURE2D_X(_SlimaCrispMask);
            SAMPLER(sampler_SlimaCrispMask);

            float SampleCrisp(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_SlimaCrispMask, sampler_LinearClamp, uv).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 源 UV（处理 render scale / 偏移）
                float2 sourceUV = input.uv * _BlitScaleBias.xy + _BlitScaleBias.zw;

                // ---- ① 像素化：UV 量化到虚拟分辨率，取像素中心 ----
                float2 virtualRes = max(_ScreenParams.xy / max(1.0, _PixelSize), 1.0);
                float2 pixelUV = (floor(sourceUV * virtualRes) + 0.5) / virtualRes;
                float2 sampledUV = lerp(sourceUV, pixelUV, _PixelateAmount);

                // ---- 不参与像素化的物件（特殊物件）：这些像素改用原始 UV 取色 → 保持清晰 ----
                float crisp = SampleCrisp(sourceUV);

                // 把"保持清晰"的范围沿描边方向外扩一圈：这样这些物件外面的描边线也是清晰的
                if (_OutlineThickness > 0.001)
                {
                    float2 ctexel = _OutlineThickness / virtualRes;
                    crisp = max(crisp, max(max(SampleCrisp(sourceUV + float2(-ctexel.x, 0.0)),
                                               SampleCrisp(sourceUV + float2(ctexel.x, 0.0))),
                                           max(SampleCrisp(sourceUV + float2(0.0, -ctexel.y)),
                                               SampleCrisp(sourceUV + float2(0.0, ctexel.y)))));
                }

                // 调试 4：品红 = 该物件不参与像素化（用来确认"谁被排除了"）
                if (_DebugMode > 3.5 && _DebugMode < 4.5)
                {
                    return half4(crisp, crisp * 0.2, crisp, 1);
                }

                float2 colorUV = (crisp > 0.5) ? sourceUV : sampledUV;
                float3 color = SampleSceneColor(colorUV);

                // ---- 调试 1：直接看原始深度缓冲 ----
                //   深度纹理没绑上时这里是纯黑（raw = 0 = 远平面）；
                //   正常时应该能看出地形轮廓（D3D 反转 Z：近处偏亮）。
                if (_DebugMode > 0.5 && _DebugMode < 1.5)
                {
                    float raw = SampleSceneDepth(sampledUV);
                    return half4(raw, raw, raw, 1);
                }

                // ---- 调试 3：线性眼深度（白 = 近，黑 = 远）----
                // ⚠ 必须限定在 3 这一档：以前写成 > 2.5，把 4 / 6 也一起劫持了
                if (_DebugMode > 2.5 && _DebugMode < 3.5)
                {
                    float d = SampleEyeDepth(sampledUV);
                    float t = saturate(1.0 - d / 60.0);
                    return half4(t, t, t, 1);
                }

                // ---- ②-a 颜色/亮度描边（不依赖深度纹理：深度没绑上时用这条也能出线）----
                //   注意 `_DebugMode < 5.5`：调试 6（分组填色）要留给下面的遮罩分支，
                //   否则 EdgeSource=1 时会被这一支抢先返回，填色永远看不到。
                if (_OutlineThickness > 0.001 && _EdgeSource > 0.5 && _DebugMode < 5.5)
                {
                    float2 texel = _OutlineThickness / virtualRes;
                    float lx0 = Lum(sampledUV + float2(-texel.x, 0.0));
                    float lx1 = Lum(sampledUV + float2(texel.x,  0.0));
                    float ly0 = Lum(sampledUV + float2(0.0, -texel.y));
                    float ly1 = Lum(sampledUV + float2(0.0,  texel.y));
                    float lC  = Lum(sampledUV);

                    float lMax = max(max(max(lx0, lx1), max(ly0, ly1)), lC);
                    float lMin = min(min(min(lx0, lx1), min(ly0, ly1)), lC);
                    float lJump = lMax - lMin;

                    if (_DebugMode > 1.5)
                    {
                        float r = saturate(lJump / max(_ColorThreshold, 1e-4));
                        return half4(r, r * 0.25, r * 0.25, 1);
                    }

                    float lOutline = (lJump >= _ColorThreshold) ? 1.0 : 0.0;

                    // 只描一侧：亮侧 = 背景侧（浅色背景），暗侧 = 物体侧。
                    // 这样描边画在物品外面，不会吃掉物品自己的像素。
                    if (_OutlinePlacement > 0.5 && lOutline > 0.5)
                    {
                        bool onBright = lC >= lMax - 1e-4;
                        bool onDark = lC <= lMin + 1e-4;
                        lOutline = (_OutlinePlacement < 1.5) ? (onBright ? 1.0 : 0.0)
                                                            : (onDark ? 1.0 : 0.0);
                    }

                    color = lerp(color, _OutlineColor.rgb, lOutline * _OutlineColor.a);
                }
                // ---- ②-c 遮罩分组描边（推荐）----
                //   把"这一组物体"整体渲染成黑、背景白，然后：自己是背景 + 邻域里有物体 → 就在物体外面画线。
                //   · 不依赖深度纹理，也不依赖颜色阈值 → 深色地面/暗物体都不会乱出线
                //   · 天生只画外侧：物品自己的像素永远是黑的，不会被啃
                //   · 调试 6（分组填色）也要走这一支 —— 所以不能只在 EdgeSource=2 时进入
                else if (_OutlineThickness > 0.001 && (_EdgeSource > 1.5 || _DebugMode > 5.5))
                {
                    float2 texel = _OutlineThickness / virtualRes;

                    // ★ 遮罩判定用「原始屏幕像素」(sourceUV)，不用像素化之后的格子中心。
                    //   否则比一个像素块还小的物件（起点/出口只有 2~3 像素）会被整格判成背景 → 整格涂黑 → 物件消失。
                    float mC = SampleMask(sourceUV);
                    float mMin = min(min(SampleMask(sourceUV + float2(-texel.x, 0.0)),
                                         SampleMask(sourceUV + float2(texel.x, 0.0))),
                                     min(SampleMask(sourceUV + float2(0.0, -texel.y)),
                                         SampleMask(sourceUV + float2(0.0, texel.y))));

                    // ---- 调试 6：把这一组的物体整体**填色**（不是描边）----
                    //   用途：一眼确认"这一组到底圈住了哪些物体"。每组一个颜色，多组会叠加显示。
                    //   完全没被填色的物体 = 没有被任何分组识别到 → 该组的过滤条件没匹配上。
                    if (_DebugMode > 5.5)
                    {
                        color = lerp(color, _DebugTint.rgb, saturate(1.0 - mC));
                        return half4(color, 1);
                    }

                    if (_DebugMode > 1.5)
                    {
                        // 每个组一个颜色，叠加显示：一眼看出"哪个物体被分到了哪一组"
                        float m = 1.0 - mC;   // 白 = 这一组的物体
                        return half4(max(color, _DebugTint.rgb * m), 1);
                    }

                    float mOutline = (mC > 0.5 && mMin < 0.5) ? 1.0 : 0.0;
                    color = lerp(color, _OutlineColor.rgb, mOutline * _OutlineColor.a);
                }
                // ---- ②-b 深度落差描边 ----
                else if (_OutlineThickness > 0.001 && _DepthThreshold > 0)
                {
                    float2 texel = _OutlineThickness / virtualRes;

                    float d00 = SampleEyeDepth(sampledUV + float2(-texel.x, -texel.y));
                    float d10 = SampleEyeDepth(sampledUV + float2(0.0,      -texel.y));
                    float d20 = SampleEyeDepth(sampledUV + float2(texel.x,  -texel.y));
                    float d01 = SampleEyeDepth(sampledUV + float2(-texel.x,  0.0));
                    float dc  = SampleEyeDepth(sampledUV);
                    float d21 = SampleEyeDepth(sampledUV + float2(texel.x,   0.0));
                    float d02 = SampleEyeDepth(sampledUV + float2(-texel.x,  texel.y));
                    float d12 = SampleEyeDepth(sampledUV + float2(0.0,       texel.y));
                    float d22 = SampleEyeDepth(sampledUV + float2(texel.x,   texel.y));

                    // ⚠ HLSL 的 max/min 只接受两个参数，必须两两嵌套
                    float dMax = max(max(max(d00, d10), max(d20, d01)), max(max(dc, d21), max(d02, max(d12, d22))));
                    float dMin = min(min(min(d00, d10), min(d20, d01)), min(min(dc, d21), min(d02, min(d12, d22))));
                    float jump = dMax - dMin;

                    // ⚠ 正交相机下「1 格方块」的深度落差是恒定的（≈1 世界单位，不随距离缩小），
                    //   所以这里基本不能按距离放大阈值：之前乘 (1 + dc*0.02) 会把 1 格的浅落差
                    //   刚好压到阈值线上 → 检测区只剩紧贴棱的 1 个像素 → 调 thickness 也永远不变粗。
                    //   只保留一个很轻的距离项，兜住透视相机下的远景噪声。
                    float threshold = _DepthThreshold * (1.0 + dc * 0.003);

                    // ---- 调试 2：把「落差 / 阈值」画成红色 ----
                    //   红色（>1）的那些像素就是会被描边的像素。调 Outline Thickness 时这块红区应该变宽；
                    //   如果红区怎么都不变宽，说明落差没有余量（阈值太大），加粗就没有作用范围。
                    if (_DebugMode > 1.5)
                    {
                        float ratio = saturate(jump / max(threshold, 1e-4));
                        return half4(ratio, ratio * 0.25, ratio * 0.25, 1);
                    }

                    float outline = (jump >= threshold) ? 1.0 : 0.0;

                    // 只描一侧：远侧 = 背景侧（物体挡在它前面的那侧），近侧 = 物体侧。
                    if (_OutlinePlacement > 0.5 && outline > 0.5)
                    {
                        bool onFar = dc >= dMax - 1e-4;
                        bool onNear = dc <= dMin + 1e-4;
                        outline = (_OutlinePlacement < 1.5) ? (onFar ? 1.0 : 0.0)
                                                           : (onNear ? 1.0 : 0.0);
                    }

                    color = lerp(color, _OutlineColor.rgb, outline * _OutlineColor.a);
                }

                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
