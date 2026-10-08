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

        // 注意：[Header(中文)] 会让 ShaderLab 解析器报错（属性参数只吃 ASCII）。
        // 所以这里用「分组｜名字」写进带引号的显示名 —— 中文放在引号里是安全的，Inspector 排序效果一样。
        _Bands ("光影｜阶梯段数（5 = 五段）", Range(2, 8)) = 5
        _ShadowColor ("光影｜阴影色", Color) = (0.42, 0.47, 0.60, 1)
        _LightTint ("光影｜受光色", Color) = (1, 1, 1, 1)
        _AmbientColor ("光影｜环境色", Color) = (0.16, 0.18, 0.24, 1)
        _LightWrap ("光影｜光照包裹（0=硬，1=半兰伯特）", Range(0, 1)) = 0.15

        _NoiseStrength ("像素噪声｜过渡强度", Range(0, 1)) = 0.65
        _NoiseSurfaceLock ("像素噪声｜锁在表面（1 = 跟模型走，0 = 钉在屏幕上）", Range(0, 1)) = 1
        _NoiseWorldSize ("像素噪声｜世界噪声格大小（世界单位，0.5 ≈ 半格方块）", Range(0.05, 2)) = 0.5
        _PixelSize ("像素噪声｜屏幕噪声块大小（只在上一个参数 = 0 时用）", Range(1, 16)) = 4

        _PixelSnap ("像素化｜顶点像素吸附（0 = 关，越大越方）", Range(0, 16)) = 3

        _OutlineColor ("描边｜颜色", Color) = (0, 0, 0, 1)
        _OutlineWidth ("描边｜粗细（屏幕像素）", Range(0, 8)) = 2
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
                half   _NoiseStrength;
                half   _PixelSize;
                half   _NoiseSurfaceLock;
                half   _NoiseWorldSize;
                half   _PixelSnap;
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

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
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
                half   _NoiseStrength;
                half   _PixelSize;
                half   _NoiseSurfaceLock;
                half   _NoiseWorldSize;
                half   _PixelSnap;
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
                float2 screenPx = input.screenPos.xy / max(input.screenPos.w, 1e-5) * _ScreenParams.xy;

                // ③-a 像素化噪声：
                //   默认锁在**表面**上（世界坐标量化成固定大小的格子）——
                //   镜头移动时噪声跟着世界/模型走，不会出现"贴图钉在屏幕上、一移动就滑动"。
                //   想要老式的屏幕网点/抖动（图案固定在屏幕上），把 _NoiseSurfaceLock 设成 0。
                float noise;
                if (_NoiseSurfaceLock > 0.5)
                {
                    float3 wp = input.positionWS / max(0.01, _NoiseWorldSize);
                    // 以 XZ 为主、混一点 Y：纯竖直面（墙）上才不会退化成一条条直线
                    noise = Hash21(floor(float2(wp.x + wp.y * 0.37, wp.z - wp.y * 0.29)));
                }
                else
                {
                    noise = PixelNoise(screenPx, _PixelSize);
                }

                // ③-b 五段阶梯：先给 NdotL 加噪声抖动，再量化成 _Bands 段
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

                // 抖动幅度 ≈ 一段的宽度，这样只用噪声"糊"过渡、不破坏阶梯
                float steps = max(2.0, _Bands);
                float dither = (noise - 0.5) * _NoiseStrength / steps;
                half lit = saturate(wrapped + dither);

                half band = min(floor(lit * steps), steps - 1.0) / (steps - 1.0);       // 5 段 → 0 / .25 / .5 / .75 / 1

                // 阴影衰减也算进阶梯里：被挡住的区域整体降一档观感
                half atten = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                band *= lerp(0.65, 1.0, atten);

                half3 ramp = lerp(_ShadowColor.rgb, mainLight.color * _LightTint.rgb, band);
                half3 albedo = _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * input.color.rgb;

                half3 color = albedo * (ramp + _AmbientColor.rgb);
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
