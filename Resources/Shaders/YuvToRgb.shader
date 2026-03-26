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

            inline half3 yuvToRgb709Limited(half y, half u, half v)
            {
                // BT.709 limited range
                half c = y - half(16.0 / 255.0);
                half d = u - half(128.0 / 255.0);
                half e = v - half(128.0 / 255.0);

                half Y = half(1.16438356) * c;

                half3 rgb;
                rgb.r = Y + half(1.79274107) * e;
                rgb.g = Y - half(0.21324861) * d - half(0.53290933) * e;
                rgb.b = Y + half(2.11240179) * d;
                return saturate(rgb);
            }

            half4 frag(v2f i) : SV_Target
            {
                // Flip horizontally to match Unity's texture orientation with incoming YUV data
                half2 uv = half2(1.0h - i.uv.x, i.uv.y);

                half y = tex2D(_TexY, uv).r;
                half u = tex2D(_TexU, uv).r;
                half v = tex2D(_TexV, uv).r;
                return half4(yuvToRgb709Limited(y, u, v), 1.0h);
            }
            ENDHLSL
        }
    }
}


