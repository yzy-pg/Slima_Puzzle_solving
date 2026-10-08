// 三渲二（cel）卡通着色 —— 给角色 / 道具 / 方块物体用
//
// 三个需求对应到这里的实现：
//   ① 像素化       —— _PixelSnap：顶点在裁剪空间吸附到虚拟像素网格（PS1 味），方块/角色轮廓会变"方"
//                      （整屏像素化请用 URP Asset 的 Render Scale + Upscaling Filter = Point，见 Docs/三渲二风格化shader.md）
//   ② 黑色描边     —— Outline Pass：反转壳（Cull Front + 沿法线外扩），粗细按屏幕像素算，物体轮廓上自动一样粗
//                      （方块地形不要用这个描边，棱会裂 —— 地形用全屏后处理描边）
//   ③ 五段阶梯光影 —— _Bands = 5：把 NdotL 切成 5 段硬边；段与段之间用「像素化噪声」抖动过渡，
//                      噪声在 _PixelSize 大小的屏幕方块内是常数，所以过渡是像素颗粒而不是渐变
//
// 兼容 URP / Forward。多乘了一道顶点色，方便和 BlockMap 的方块网格（顶点色里带面朝向明暗）一起用。

Shader "Slima/Toon (Cel + Pixel)"
{
    Properties
    {
        [MainTexture] _BaseMap ("贴图", 2D) = "white" {}
        [MainColor]   _BaseColor ("颜色", Color) = (1, 1, 1, 1)

        // 这两个是"继承自旧 BlockUnlit 的接口"：缺了会让双面墙 / 镂空方块显示错误
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        // 注意：[Header(中文)] 会让 ShaderLab 解析器报错（属性参数只吃 ASCII）。
        // 所以这里用「分组｜名字」写进带引号的显示名 —— 中文放在引号里是安全的，Inspector 排序效果一样。
        _Bands ("光影｜阶梯段数（5 = 五段）", Range(2, 8)) = 5
        _ShadowColor ("光影｜阴影色", Color) = (0.42, 0.47, 0.60, 1)
        _LightTint ("光影｜受光色", Color) = (1, 1, 1, 1)
        _AmbientColor ("光影｜环境色", Color) = (0.16, 0.18, 0.24, 1)
        _LightWrap ("光影｜光照包裹（0=硬，1=半兰伯特）", Range(0, 1)) = 0.15
        _ExtraLightMode ("光影｜额外光（Lamp）模式（0 = 按法线，1 = 按距离·以灯为中心）", Range(0, 1)) = 0
        _NoiseStrength ("光影｜阶梯过渡噪声强度（0 = 纯阶梯，0.35 = 像素颗粒过渡）", Range(0, 1)) = 0.35
        _NoiseWorldSize ("光影｜噪声格大小（世界单位，0.02 ≈ 2 屏幕像素）", Range(0.002, 0.5)) = 0.02
        _NoiseMaskInvert ("光影｜噪声遮罩反转（0 = 只有受光处有颗粒，1 = 反过来）", Range(0, 1)) = 0

        _PixelSnap ("像素化｜顶点像素吸附（0 = 关；会和全屏像素化叠加，建议只开一层）", Range(0, 16)) = 0

        _OutlineColor ("描边｜颜色", Color) = (0, 0, 0, 1)
        _OutlineWidth ("描边｜粗细（屏幕像素，0 = 关掉这个物体的反转壳描边）", Range(0, 8)) = 2
        _OutlineNoise ("描边｜噪声（手绘感）", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // ------------------------------------------------------------------
        // ① 描边：反转壳（只适合角色 / 单体物件；方块地形请用全屏后处理描边）
        // ------------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front          // 只画背面 → 沿法线外扩后从轮廓外沿露出来
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Bands;
                half4  _ShadowColor;
                half4  _LightTint;
                half4  _AmbientColor;
                half   _LightWrap;
                half   _PixelSnap;
                half   _NoiseStrength;
                half   _NoiseWorldSize;
                half   _NoiseMaskInvert;
                half   _ExtraLightMode;
                half   _Cull;
                half   _Cutoff;
                half4  _OutlineColor;
                half   _OutlineWidth;
                half   _OutlineNoise;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  fogFactor  : TEXCOORD0;
                float2 screenPx   : TEXCOORD1;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                // ★ 粗细 = 0 → 整个 pass 退化到裁剪空间外，什么都不画。
                //   方块地形（chunk 是合并网格、每面顶点独立）必须用这个来关掉反转壳：
                //   反转壳沿各面法线外扩会让每个方块棱裂开，表现就是"只在部分边上出现厚黑块、非常不均衡"。
                //   地形轮廓交给全屏描边（SlimaToonOutlineFeature）去做。
                if (_OutlineWidth <= 0.001)
                {
                    output.positionCS = float4(0.0, 0.0, -10.0, 1.0);
                    return output;
                }

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                float4 positionCS = positionInputs.positionCS;

                // 沿法线在"屏幕空间"外扩：这样无论物体多远，描边在屏幕上都是同样粗细
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 normalCS = TransformWorldToHClipDir(normalWS);

                float2 dir = normalCS.xy;
                float  len = max(length(dir), 1e-5);
                dir /= len;

                // 手绘感：让外扩长度沿轮廓抖动一点
                if (_OutlineNoise > 0)
                {
                    float wobble = Hash21(floor(positionInputs.positionWS.xz * 3.0));
                    dir *= 1.0 + (wobble - 0.5) * 2.0 * _OutlineNoise;
                }

                // _OutlineWidth 单位 = 屏幕像素
                float pixelOffset = _OutlineWidth * 2.0 / _ScreenParams.y;
                positionCS.xy += dir * pixelOffset * positionCS.w;

                output.positionCS = positionCS;
                output.screenPx = positionCS.xy;
                output.fogFactor = ComputeFogFactor(positionCS.z);
                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                half3 color = _OutlineColor.rgb;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // ② 主体：五段阶梯光影 + 像素化噪声过渡 + 顶点像素吸附
        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _Bands;
                half4  _ShadowColor;
                half4  _LightTint;
                half4  _AmbientColor;
                half   _LightWrap;
                half   _PixelSnap;
                half   _NoiseStrength;
                half   _NoiseWorldSize;
                half   _NoiseMaskInvert;
                half   _ExtraLightMode;
                half   _Cull;
                half   _Cutoff;
                half4  _OutlineColor;
                half   _OutlineWidth;
                half   _OutlineNoise;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 shadowCoord: TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                float4 color      : COLOR;
                float4 screenPos  : TEXCOORD5;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // 像素化噪声：同一个屏幕像素块里取到同一个值 → 看着是"颗粒"，不是渐变
            float PixelNoise(float2 screenPixelPos, float pixelSize)
            {
                return Hash21(floor(screenPixelPos / max(1.0, pixelSize)));
            }

            Varyings ToonVertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                float4 positionCS = positionInputs.positionCS;

                // ① 顶点像素吸附：把裁剪空间坐标吸到虚拟像素网格上，几何轮廓就会"方"
                if (_PixelSnap > 0.01 && positionCS.w > 0)
                {
                    float2 grid = _ScreenParams.xy / max(1.0, _PixelSnap);
                    float2 snapped = floor(positionCS.xy / positionCS.w * grid + 0.5) / grid;
                    positionCS.xy = snapped * positionCS.w;
                }

                output.positionCS = positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.shadowCoord = TransformWorldToShadowCoord(positionInputs.positionWS);
                output.fogFactor = ComputeFogFactor(positionCS.z);
                output.screenPos = ComputeScreenPos(positionCS);
                return output;
            }

            half4 ToonFragment(Varyings input) : SV_Target
            {
                // ★ 阶梯过渡噪声：**锁在世界（表面）上** —— 相机移动时图案跟着物体走，不会"跟着镜头滑动"。
                //   格大小默认 0.02 世界单位（约 2 个屏幕像素），远小于一个方块且非整数倍 →
                //   不会和方块网格对齐，因此不会出现"每个方块图案都一样"的重复花纹（那是上一版的坑）。
                float3 wp = input.positionWS / max(0.0001, _NoiseWorldSize);
                float noise = Hash21(floor(wp.xz) + floor(wp.y) * 0.37);
                float dither = (noise - 0.5) * _NoiseStrength;

                // ③ 五段阶梯
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(input.shadowCoord);

                // ★ 兜底：场景里没有平行光时（地图编辑器烘出来的场景是"空场景 + 几何"，默认没有灯），
                //   GetMainLight 会返回黑光，NdotL≈0 → 整幅画面都落在最暗那一段，看起来就是"一片灰"。
                //   这里给一个默认斜上方的光，保证五段阶梯在没有灯的场景里也能看出来。
                if (dot(mainLight.color, half3(1.0, 1.0, 1.0)) < 0.001)
                {
                    mainLight.color = _LightTint.rgb;
                    mainLight.direction = normalize(float3(0.35, 0.9, 0.25));
                    mainLight.shadowAttenuation = 1.0;
                    mainLight.distanceAttenuation = 1.0;
                }

                half NdotL = dot(normalWS, mainLight.direction);
                half wrapped = saturate((NdotL + _LightWrap) / (1.0 + _LightWrap));   // 0=硬边，>0 偏半兰伯特

                // ★ 主光 = 天空 / 环境平行光：**不加任何噪声**。
                //   环境对物体不产生颗粒影响，只走干净的硬边阶梯；
                //   阶梯过渡的颗粒**只来自 Lamp 类额外光**（见下面 additional light 循环里各自的 extraMask）。
                float steps = max(2.0, _Bands);
                dither = 0.0;
                half lit = saturate(wrapped + dither);

                half band = min(floor(lit * steps), steps - 1.0) / (steps - 1.0);       // 5 段 → 0 / .25 / .5 / .75 / 1

                // 阴影衰减也算进阶梯里：被挡住的区域整体降一档观感
                half atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                band *= lerp(0.65, 1.0, atten);

                half3 ramp = lerp(_ShadowColor.rgb, mainLight.color * _LightTint.rgb, band);
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(baseSample.a - _Cutoff);   // 延续 BlockUnlit 的 alpha 裁切（镂空方块需要）
                half3 albedo = _BaseColor.rgb * baseSample.rgb * input.color.rgb;

                // ★ 环境光取「场景环境光」（URP 的 SH，即 RenderSettings.ambientLight）；
                //   材质里的 _AmbientColor 只当"最低环境光 / 色调"兜底 —— 取两者较大值。
                //   之前只用材质的固定颜色 → 在场景里调环境光时主角和地形毫无反应（只有点光源有效）。
                half3 ambientScene = SampleSH(normalWS);
                half3 ambientFloor = _AmbientColor.rgb * _AmbientColor.a;

                half3 color = albedo * (ramp + max(ambientScene, ambientFloor));

                // ★ 额外光（点光 / 聚光，例如 Lamp 的 emitLight）——之前完全没处理，所以灯照不亮画面
                //   每盏按同样规则量化成五段，再乘距离/阴影衰减累加
                uint additionalCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < additionalCount; li++)
                {
                    Light extra = GetAdditionalLight(li, input.positionWS);

                    half attenExtra = saturate(extra.distanceAttenuation * extra.shadowAttenuation);
                    half nlExtra = dot(normalWS, extra.direction);
                    half wrappedExtra = saturate((nlExtra + _LightWrap) / (1.0 + _LightWrap));

                    // ★ 模式 1：不看面法线，只用"到灯的距离" → 以灯为中心的圆形光斑（背面、侧面一样亮）
                    wrappedExtra = lerp(wrappedExtra, 1.0, saturate(_ExtraLightMode));

                    // ★ Lamp 的噪声遮罩：按你的要求**不做渐变** —— 只要这盏灯照到（atten > 0）就统一为 1，
                    //   照不到就是 0。这样"灯范围内"全是满强度颗粒，不再随距离衰减、也不会弱到看不见。
                    half extraMask = step(0.001, attenExtra);
                    extraMask = lerp(extraMask, 1.0 - extraMask, _NoiseMaskInvert);
                    half ditherExtra = (noise - 0.5) * _NoiseStrength * extraMask / steps;
                    half litExtra = saturate(wrappedExtra + ditherExtra);
                    half bandExtra = min(floor(litExtra * steps), steps - 1.0) / (steps - 1.0);

                    bandExtra *= lerp(0.65, 1.0, attenExtra);

                    half3 rampExtra = lerp(_ShadowColor.rgb, extra.color * _LightTint.rgb, bandExtra);

                    // ★ 抗"线性减淡发灰"：在 40%~70% 的受光过渡带里提升这一盏光的饱和度。
                    //   公式：饱和度增强 = 30% − (n% − 40%)，n 为受光水平（0..1）
                    //   → n=40% 时 +30%（峰值），n=70% 时 +0%，中间线性过渡
                    half lumExtra = dot(rampExtra, half3(0.299, 0.587, 0.114));
                    half transT = saturate((attenExtra - 0.4) / 0.3);     // 0 = 40%，1 = 70%
                    half satBoost = (1.0 - transT) * 0.3;                 // 30% → 0%
                    rampExtra = lerp(lumExtra.xxx, rampExtra, 1.0 + satBoost);

                    color += albedo * rampExtra * attenExtra;
                }

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // ③ 阴影投射（不写这个的话物体不会投影）
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // ★ URP 的 Shadows.hlsl 内部用了 core 的 LerpWhiteTo（声明在 CommonMaterial.hlsl）。
            //   官方 Lit 是靠 LitInput → SurfaceInput → CommonMaterial 这条链把它带进来的；
            //   手写 ShadowCaster 必须自己补，否则报 undeclared identifier 'LerpWhiteTo'。
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));

            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // ④ 深度（SSAO / 深度相关后处理需要）
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings DepthVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFragment(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
