Shader "Hidden/LiveKit/Resize"
{
    Properties
    {
        _MainTex ("Render Texture", 2D) = "white" {}
        // Note: matrices cannot be declared in the Properties block.
        // _ResizeMatrix is set from C# via material.SetMatrix()
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4x4  _ResizeMatrix;  // Set via material.SetMatrix() from C#

            struct Attributes
            {
                float4 pos : POSITION;
                float2 uv  : TEXCOORD0;
            };

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.pos = UnityObjectToClipPos(v.pos);

                // Apply the resize/crop matrix in UV space.
                // The matrix transforms [0,1]² UVs:
                //   • scale  → zooms into the texture
                //   • offset → shifts the crop window
                float4 uvH = float4(v.uv, 0.0, 1.0);   // homogeneous UV
                float4 uvT = mul(_ResizeMatrix, uvH);
                o.uv = uvT.xy;
                return o;
            }

            fixed4 frag(Varyings i) : SV_Target
            {
                // Pixels outside [0,1] are cropped — return black / transparent.
                if (i.uv.x < 0.0 || i.uv.x > 1.0 ||
                    i.uv.y < 0.0 || i.uv.y > 1.0)
                    return fixed4(0, 0, 0, 1);

                return tex2D(_MainTex, i.uv);
            }
            ENDHLSL
        }
    }
}