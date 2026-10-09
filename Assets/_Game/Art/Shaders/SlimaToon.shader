// 三渲二（cel）卡通着色 —— 给角色 / 道具 / 方块物体用
//
// 三个需求对应到这里的实现：
//   ① 像素化       —— _PixelSnap：顶点在裁剪空间吸附到虚拟像素网格（PS1 味），方块/角色轮廓会变"方"
//                      （整屏像素化请用 URP Asset 的 Render Scale + Upscaling Filter = Point，见 Docs/三渲二风格化shader.md）
//   ② 黑色描边     —— Outline Pass：反转壳（Cull Front + 沿法线外扩），粗细按屏幕像素算，物体轮廓上自动一样粗
//                      （方块地形不要用这个描边，棱会裂 —— 地形用全屏后处理描边）
//   ③ 五段阶梯光影 —— _Bands = 5：把 NdotL 切成 5 段硬边；段与段之间用「像素化噪声」抖动过渡，
//                      噪声在可调的世界 / 屏幕方格内是常数，边界只混合相邻档位的像素点。
//
// 兼容 URP / Forward。多乘了一道顶点色，方便和 BlockMap 的方块网格（顶点色里带面朝向明暗）一起用。

Shader "Slima/Toon (Cel + Pixel)"
{
    Properties
    {
        [MainTexture] _BaseMap ("贴图", 2D) = "white" {}
        [MainColor]   _BaseColor ("颜色", Color) = (1, 1, 1, 1)
        _EmissionMap ("发光｜遮罩贴图", 2D) = "white" {}
        [HDR] _EmissionColor ("发光｜颜色", Color) = (1, 1, 1, 1)
        _EmissionStrength ("发光｜强度（0 = 关闭）", Float) = 0

        // 这两个是"继承自旧 BlockUnlit 的接口"：缺了会让双面墙 / 镂空方块显示错误
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        // 注意：[Header(中文)] 会让 ShaderLab 解析器报错（属性参数只吃 ASCII）。
        // 所以这里用「分组｜名字」写进带引号的显示名 —— 中文放在引号里是安全的，Inspector 排序效果一样。
        _Bands ("光影｜阶梯段数（5 = 五段）", Range(2, 8)) = 5
        [Enum(Pixel, 0, Smooth, 1)] _LightingMode ("光影｜默认光源模式（可被 Light 组件覆盖）", Float) = 0
        _Band0 ("光影｜亮度 1（黑）", Range(0, 1)) = 0
        _Band1 ("光影｜亮度 2", Range(0, 1)) = 0.25
        _Band2 ("光影｜亮度 3", Range(0, 1)) = 0.5
        _Band3 ("光影｜亮度 4", Range(0, 1)) = 0.75
        _Band4 ("光影｜亮度 5（白）", Range(0, 1)) = 1
        _Brightness ("光影｜量化前亮度倍率", Float) = 1
        _LightInputMin ("光影｜输入黑点", Float) = 0
        _LightInputMax ("光影｜输入白点", Float) = 1
        _LightContrast ("光影｜光照曲线指数", Range(0.1, 4)) = 1
        _ShadowInfluence ("光影｜阴影影响", Range(0, 1)) = 1
        _ShadowColor ("光影｜阴影色", Color) = (0.42, 0.47, 0.60, 1)
        _LightTint ("光影｜受光色", Color) = (1, 1, 1, 1)
        _AmbientColor ("光影｜环境色", Color) = (0.16, 0.18, 0.24, 1)
        _LightWrap ("光影｜光照包裹（0=硬，1=半兰伯特）", Range(0, 1)) = 0.15
        _ExtraLightMode ("光影｜额外光（Lamp）模式（0 = 按法线，1 = 按距离·以灯为中心）", Range(0, 1)) = 1
        _NoiseStrength ("光影｜阶梯过渡噪声强度（0 = 纯阶梯，0.35 = 像素颗粒过渡）", Range(0, 1)) = 0.35
        _NoiseWorldSize ("光影｜噪声格大小（世界单位，0.06 ≈ 6 屏幕像素 ≈ 3 个像素块）", Range(0.002, 0.5)) = 0.06
        [Enum(World, 0, Screen, 1)] _NoiseSpace ("光影｜像素噪波坐标（世界 / 屏幕）", Float) = 1
        _NoisePixelSize ("光影｜噪波格大小（渲染目标像素）", Range(1, 32)) = 3
        _NoiseSeed ("光影｜噪波种子", Float) = 0
        [HideInInspector] _NoiseMaskInvert ("Legacy Noise Mask Invert", Float) = 0
        [Toggle] _FallbackLight ("光影｜无主光时使用备用光", Float) = 1

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
                float4 _EmissionColor;
                float  _EmissionStrength;
                half   _Bands;
                half   _LightingMode;
                half   _Band0, _Band1, _Band2, _Band3, _Band4;
                float  _Brightness, _LightInputMin, _LightInputMax;
                half   _LightContrast, _ShadowInfluence;
                float  _NoiseSpace, _NoisePixelSize, _NoiseSeed;
                half   _FallbackLight;
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
            #pragma target 3.5

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
                float4 _EmissionColor;
                float  _EmissionStrength;
                half   _Bands;
                half   _LightingMode;
                half   _Band0, _Band1, _Band2, _Band3, _Band4;
                float  _Brightness, _LightInputMin, _LightInputMax;
                half   _LightContrast, _ShadowInfluence;
                float  _NoiseSpace, _NoisePixelSize, _NoiseSeed;
                half   _FallbackLight;
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
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

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

            #include "SlimaToonLighting.hlsl"

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
                float3 normalWS = normalize(input.normalWS);
                float3 mainPosition = PixelLightSamplePosition(input.positionWS, normalWS, input.positionCS.xy,
                    _SlimaMainLightSettings, _SlimaMainLightNoise);
                float4 mainShadowCoord = input.shadowCoord;
                #if !defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    mainShadowCoord = TransformWorldToShadowCoord(mainPosition);
                #endif
                Light mainLight = GetMainLight(mainShadowCoord);
                if (_FallbackLight > 0.5 && dot(mainLight.color, half3(1, 1, 1)) < 0.001)
                {
                    mainLight.color = half3(1, 1, 1);
                    mainLight.direction = normalize(float3(0.35, 0.9, 0.25));
                    mainLight.shadowAttenuation = 1;
                    mainLight.distanceAttenuation = 1;
                }

                float mainBrightness = LightBrightness(mainLight.color);
                float wrapped = saturate((dot(normalWS, mainLight.direction) + _LightWrap) / (1 + _LightWrap));
                float mainRaw = wrapped * mainLight.distanceAttenuation *
                    lerp(1.0, mainLight.shadowAttenuation, _ShadowInfluence) * mainBrightness;
                float band = EvaluateToonLight(mainRaw, input.positionWS, normalWS, input.positionCS.xy,
                    _SlimaMainLightSettings, _SlimaMainLightNoise);
                half3 mainTint = mainLight.color / max(0.0001, mainBrightness);
                half3 ramp = lerp(_ShadowColor.rgb, mainTint * _LightTint.rgb, band);
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(baseSample.a - _Cutoff);
                half3 albedo = _BaseColor.rgb * baseSample.rgb * input.color.rgb;
                half3 ambientScene = SampleSH(normalWS);
                half3 ambientFloor = _AmbientColor.rgb * _AmbientColor.a;
                half3 color = albedo * (ramp + max(ambientScene, ambientFloor));

                // Top material color layer: add light after albedo/ambient, then apply fog.
                // Distance, shadows and intensity are evaluated BEFORE quantization.
                uint additionalCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < additionalCount; li++)
                {
                    int lightIndex = GetPerObjectLightIndex(li);
                    float3 samplePosition = PixelLightSamplePosition(input.positionWS, normalWS, input.positionCS.xy,
                        _SlimaAdditionalLightSettings[lightIndex], _SlimaAdditionalLightNoise[lightIndex]);
                    Light extra = GetAdditionalLight(li, samplePosition, half4(1, 1, 1, 1));
                    float brightness = LightBrightness(extra.color);
                    float wrappedExtra = saturate((dot(normalWS, extra.direction) + _LightWrap) / (1 + _LightWrap));
                    wrappedExtra = lerp(wrappedExtra, 1.0, saturate(_ExtraLightMode));
                    float raw = wrappedExtra * extra.distanceAttenuation *
                        lerp(1.0, extra.shadowAttenuation, _ShadowInfluence) * brightness;
                    float bandExtra = EvaluateToonLight(raw, input.positionWS, normalWS, input.positionCS.xy,
                        _SlimaAdditionalLightSettings[lightIndex], _SlimaAdditionalLightNoise[lightIndex]);
                    half3 tint = extra.color / max(0.0001, brightness);
                    color += albedo * tint * _LightTint.rgb * bandExtra;
                }

                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb *
                    _EmissionColor.rgb * max(0.0, _EmissionStrength);
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
