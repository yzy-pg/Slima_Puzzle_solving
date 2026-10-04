// 序列帧动画方块（岩浆 / 流水 / 传送门）
// 贴图要求：一条横向的 1×N 序列帧图（N = BlockDefinition.animFrames），
//           因此该方块请用 _simple 或 atlasOverride（非图集），网格 UV 才是 0~1。
Shader "BlockMap/BlockAnimated"
{
    Properties
    {
        _MainTex ("Frames (横向 N 帧)", 2D) = "white" {}
        _Frames  ("Frames", Float) = 4
        _Speed   ("FPS", Float) = 8
        _Cutoff  ("Alpha Cutoff", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull [_Cull]
        Lighting Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                float2 uv    : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Frames;
            float _Speed;
            float _Cutoff;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float n = max(1.0, _Frames);
                // 当前帧号（0 ~ n-1），随时间循环
                float f = floor(frac(_Time.y * _Speed) * n);
                // 把整格 UV 映射到"第 f 帧"那一段
                float2 uv = float2((f + i.uv.x) / n, i.uv.y);

                fixed4 c = tex2D(_MainTex, uv);
                clip(c.a - _Cutoff);
                return fixed4(c.rgb * i.color.rgb, c.a * i.color.a);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Transparent Cutout"
}
