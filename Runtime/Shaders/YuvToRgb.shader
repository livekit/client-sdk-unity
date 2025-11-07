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
            float _ColorStd; // 0 = BT.601 limited, 1 = BT.709 limited
            float _SwapUV;   // 0 = normal, 1 = swap U and V
            float _FullRange; // 0 = limited range (16-235/240), 1 = full range (0-255)
            float _InvertU;   // 0 = normal, 1 = invert U (u = 1-u)
            float _InvertV;   // 0 = normal, 1 = invert V (v = 1-v)
            float _DebugMode; // 0 = normal, 1 = show Y, 2 = show U, 3 = show V

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

            float3 yuvToRgb601Limited(float y, float u, float v)
            {
                // BT.601 limited range
                float c = y - 16.0 / 255.0;
                float d = u - 128.0 / 255.0;
                float e = v - 128.0 / 255.0;

                float3 rgb;
                rgb.r = saturate(1.16438356 * c + 1.59602678 * e);
                rgb.g = saturate(1.16438356 * c - 0.39176229 * d - 0.81296765 * e);
                rgb.b = saturate(1.16438356 * c + 2.01723214 * d);
                return rgb;
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

            float3 yuvToRgb601Full(float y, float u, float v)
            {
                // BT.601 full range
                float d = u - 0.5;
                float e = v - 0.5;
                float3 rgb;
                rgb.r = saturate(y + 1.40200000 * e);
                rgb.g = saturate(y - 0.34413629 * d - 0.71413629 * e);
                rgb.b = saturate(y + 1.77200000 * d);
                return rgb;
            }

            float3 yuvToRgb709Full(float y, float u, float v)
            {
                // BT.709 full range
                float d = u - 0.5;
                float e = v - 0.5;
                float3 rgb;
                rgb.r = saturate(y + 1.57480000 * e);
                rgb.g = saturate(y - 0.18732427 * d - 0.46812427 * e);
                rgb.b = saturate(y + 1.85560000 * d);
                return rgb;
            }

            float4 frag(v2f i) : SV_Target
            {
                float y = tex2D(_TexY, i.uv).r;
                float u = tex2D(_TexU, i.uv).r;
                float v = tex2D(_TexV, i.uv).r;
                if (_SwapUV >= 0.5)
                {
                    float t = u; u = v; v = t;
                }
                if (_InvertU >= 0.5) { u = 1.0 - u; }
                if (_InvertV >= 0.5) { v = 1.0 - v; }
                // Debug views
                if (_DebugMode >= 0.5 && _DebugMode < 1.5) { return float4(y, y, y, 1.0); }
                if (_DebugMode >= 1.5 && _DebugMode < 2.5) { return float4(u, u, u, 1.0); }
                if (_DebugMode >= 2.5 && _DebugMode < 3.5) { return float4(v, v, v, 1.0); }
                // Select matrix by _ColorStd and _FullRange
                float use709 = step(0.5, _ColorStd);
                float useFull = step(0.5, _FullRange);
                float3 rgb601Lim = yuvToRgb601Limited(y, u, v);
                float3 rgb709Lim = yuvToRgb709Limited(y, u, v);
                float3 rgb601Full = yuvToRgb601Full(y, u, v);
                float3 rgb709Full = yuvToRgb709Full(y, u, v);
                float3 rgbLim = lerp(rgb601Lim, rgb709Lim, use709);
                float3 rgbFull = lerp(rgb601Full, rgb709Full, use709);
                float3 rgb = lerp(rgbLim, rgbFull, useFull);
                return float4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}


