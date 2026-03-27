Shader "Hidden/LiveKit/YUV2RGB"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _TexY;
            sampler2D _TexU;
            sampler2D _TexV;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                half2  uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = half2(v.uv);
                return o;
            }

            inline half3 yuvToRgb709Full(half y, half u, half v)
            {
                // BT.709 full range (0-255)
                half d = u - half(128.0 / 255.0);
                half e = v - half(128.0 / 255.0);

                half3 rgb;
                rgb.r = y + half(1.5748) * e;
                rgb.g = y - half(0.18733) * d - half(0.46813) * e;
                rgb.b = y + half(1.8556) * d;
                return saturate(rgb);
            }

            half4 frag(v2f i) : SV_Target
            {
                // Flip horizontally to match Unity's texture orientation with incoming YUV data
                half2 uv = half2(1.0h - i.uv.x, i.uv.y);

                half y = tex2D(_TexY, uv).r;
                half u = tex2D(_TexU, uv).r;
                half v = tex2D(_TexV, uv).r;
                half3 rgb = yuvToRgb709Full(y, u, v);
                // YUV→RGB produces sRGB/gamma values; convert to linear so the
                // hardware's linear→sRGB write to the sRGB RT gives correct output.
                rgb = GammaToLinearSpace(rgb);
                return half4(rgb, 1.0h);
            }
            ENDHLSL
        }
    }
}


