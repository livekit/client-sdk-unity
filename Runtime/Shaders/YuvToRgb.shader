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
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float3 yuvToRgb709Limited(float y, float u, float v)
            {
                // BT.709 limited range
                float c = y - 16.0 / 255.0;
                float d = u - 128.0 / 255.0;
                float e = v - 128.0 / 255.0;

                float3 rgb;
                rgb.r = saturate(1.16438356 * c + 1.79274107 * e);
                rgb.g = saturate(1.16438356 * c - 0.21324861 * d - 0.53290933 * e);
                rgb.b = saturate(1.16438356 * c + 2.11240179 * d);
                return rgb;
            }

            float4 frag(v2f i) : SV_Target
            {
                float y = tex2D(_TexY, i.uv).r;
                float u = tex2D(_TexU, i.uv).r;
                float v = tex2D(_TexV, i.uv).r;
                float3 rgb = yuvToRgb709Limited(y, u, v);
                return float4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}


