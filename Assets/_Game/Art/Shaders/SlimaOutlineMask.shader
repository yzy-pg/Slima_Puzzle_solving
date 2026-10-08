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
            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _MaskColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings MaskVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            // 遮罩颜色由 Feature 指定：
            //   黑 = 描边分组（会被描边）；白 = 不参与像素化的物件（保持清晰）
            half4 MaskFragment(Varyings input) : SV_Target
            {
                return _MaskColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
