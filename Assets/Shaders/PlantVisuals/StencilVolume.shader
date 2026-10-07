// Invisible "light volume" for a building box (night view). Marks in the stencil buffer every pixel whose
// surface lies INSIDE the box: back faces behind the surface +1, front faces behind the surface -1.
// Works with the camera inside the box too. Draws no colour. Two materials per box:
//   back  : Cull Front, Pass IncrSat, queue Transparent+490
//   front : Cull Back,  Pass DecrSat, queue Transparent+491
// The ScreenMultiply quads then darken stencil 0 (outside) fully and stencil != 0 (inside) only a little.
Shader "PlantPOC/StencilVolume"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 1
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilOp ("Stencil Pass Op", Float) = 4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+490" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ColorMask 0
        ZWrite Off
        ZTest GEqual
        Cull [_Cull]
        Stencil
        {
            Ref 0
            Comp Always
            Pass [_StencilOp]
            ReadMask 255
            WriteMask 255
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
