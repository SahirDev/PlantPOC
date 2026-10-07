// Full-screen tint for Day / Evening / Night: multiplies everything already drawn by _Color.
// One quad in front of the camera = the cheapest possible "darken the whole scene" (no post-processing).
// Stencil: the night quad only darkens pixels outside the building light volumes (stencil 0), a second
// "inside" quad darkens the inside pixels less (see StencilVolume.shader). Default Always = everywhere.
Shader "PlantPOC/ScreenMultiply"
{
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [IntRange] _StencilRef ("Stencil Ref", Range(0, 255)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comp", Float) = 8
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+500" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend DstColor Zero
        ZWrite Off
        ZTest Always
        Cull Off
        Stencil
        {
            Ref [_StencilRef]
            Comp [_StencilComp]
            ReadMask 255
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;

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
                return _Color;
            }
            ENDCG
        }
    }
}
