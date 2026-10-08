// 描边遮罩：把"要描边的这一组物体"画成黑，背景保持白（只写进描边用的临时贴图，
// 不改世界、不改材质、不影响正常渲染）。
//
// 由 SlimaToonOutlineFeature 用 CommandBuffer.DrawRenderer(renderer, 本材质, 子网格序号, 0) 调用：
//   · 整块地面（材质名含 _Floor）→ 整片地面合成一个黑块 → 只在外沿出线
//   · 功能物件（同一 Layer 上的 Spawn/Exit/门…）→ 同类合成一个黑块 → 各自成组出线

Shader "Slima/Outline Mask"
{
    Properties
    {
        _MaskColor ("遮罩颜色（由脚本设置）", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Mask"

            // ★ 不写深度、不做深度测试：
            //   遮罩只需要回答"这个像素有没有这一组的物体"，不需要前后遮挡关系。
            //   之前用 ZTest LEqual，清屏后的深度（反转 Z 下是远平面）会让所有几何测试失败
            //   → 遮罩一个像素都画不上 → 之后所有模式都变成"纯色"。
            ZWrite Off
            ZTest Always
            Cull Back
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _MaskColor;
                float _MaskFar;      // 相机远裁剪面（把深度归一化到 0..1 写进 G 通道）
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float  eyeDepth   : TEXCOORD0;
            };

            Varyings MaskVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                // 视线方向深度（正交/透视都正确），给"被遮挡就不画线"用
                output.eyeDepth = -(mul(UNITY_MATRIX_V, float4(positionWS, 1.0)).z);
                return output;
            }

            // R = 遮罩颜色（黑=描边分组物体 / 白=不参与像素化的物件）
            // G = 该几何的视线深度归一化（合成时用来判断"有没有被更近的东西挡住"）
            half4 MaskFragment(Varyings input) : SV_Target
            {
                float depth01 = saturate(input.eyeDepth / max(1e-3, _MaskFar));
                return half4(_MaskColor.r, depth01, _MaskColor.b, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
