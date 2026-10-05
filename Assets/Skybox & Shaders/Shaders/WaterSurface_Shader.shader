Shader "Custom/WaterSurfaceShader"
{
    Properties
    {
        _WaterColor ("Water Color", Color) =
            (0.02, 0.32, 0.75, 0.75)

        _WaveHeight ("Wave Height", Range(0,0.2)) = 0.045

        _WaveLength ("Wave Length", Range(0.2,5)) = 1.5

        _WaveSpeed ("Wave Speed", Range(0,3)) = 0.8

        // -1 = right to left
        // +1 = left to right
        _WaveDirection ("Wave Direction", Range(-1,1)) = -1

        _WaveSecondaryStrength
            ("Secondary Wave Strength", Range(0,1)) = 0.35

        _Smoothness ("Smoothness", Range(0,1)) = 0.95

        _Transparency ("Transparency", Range(0,1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        Pass
        {
            Name "WaterSurface"

            Tags
            {
                "LightMode"="UniversalForward"
            }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 viewDirWS   : TEXCOORD2;
                float waveValue    : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)

                float4 _WaterColor;

                float _WaveHeight;
                float _WaveLength;
                float _WaveSpeed;
                float _WaveDirection;
                float _WaveSecondaryStrength;

                float _Smoothness;
                float _Transparency;

            CBUFFER_END


            // =========================================================
            // WAVE FUNCTION
            // =========================================================

            float GetWave(
                float x,
                float z,
                float time)
            {
                float direction = _WaveDirection;

                // Main wave travelling right -> left
                float wave1 =
                    sin(
                        x / _WaveLength +
                        time *
                        direction
                    );

                // Smaller secondary wave
                float wave2 =
                    sin(
                        x /
                        (_WaveLength * 0.55) +
                        time *
                        direction *
                        1.45
                    );

                // Diagonal wave
                float wave3 =
                    sin(
                        (
                            x +
                            z * 0.45
                        )
                        /
                        (_WaveLength * 1.8)
                        +
                        time *
                        direction *
                        0.7
                    );

                // Cross movement
                float wave4 =
                    cos(
                        (
                            z * 1.5 -
                            x * 0.25
                        )
                        /
                        (_WaveLength * 0.8)
                        +
                        time *
                        direction *
                        0.9
                    );

                float result =
                    wave1 * 0.55 +
                    wave2 * 0.20 +
                    wave3 * 0.15 +
                    wave4 *
                    _WaveSecondaryStrength *
                    0.10;

                return result;
            }


            // =========================================================
            // VERTEX
            // =========================================================

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionOS =
                    input.positionOS.xyz;

                float time =
                    _Time.y *
                    _WaveSpeed;


                // -----------------------------------------------
                // ACTUAL UP / DOWN WATER MOVEMENT
                // -----------------------------------------------

                float wave =
                    GetWave(
                        positionOS.x,
                        positionOS.z,
                        time
                    );

                positionOS.y +=
                    wave *
                    _WaveHeight;


                // -----------------------------------------------
                // CALCULATE WAVE SLOPE
                // -----------------------------------------------

                float sampleDistance = 0.05;

                float waveX1 =
                    GetWave(
                        positionOS.x + sampleDistance,
                        positionOS.z,
                        time
                    );

                float waveX0 =
                    GetWave(
                        positionOS.x - sampleDistance,
                        positionOS.z,
                        time
                    );

                float waveZ1 =
                    GetWave(
                        positionOS.x,
                        positionOS.z + sampleDistance,
                        time
                    );

                float waveZ0 =
                    GetWave(
                        positionOS.x,
                        positionOS.z - sampleDistance,
                        time
                    );


                float slopeX =
                    (
                        waveX1 -
                        waveX0
                    )
                    /
                    (
                        sampleDistance * 2.0
                    );

                float slopeZ =
                    (
                        waveZ1 -
                        waveZ0
                    )
                    /
                    (
                        sampleDistance * 2.0
                    );


                // Create the actual waved normal.
                float3 waveNormalOS =
                    normalize(
                        float3(
                            -slopeX * _WaveHeight,
                            1.0,
                            -slopeZ * _WaveHeight
                        )
                    );


                // -----------------------------------------------
                // TRANSFORM
                // -----------------------------------------------

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(
                        positionOS
                    );

                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(
                        waveNormalOS
                    );


                output.positionHCS =
                    positionInputs.positionCS;

                output.positionWS =
                    positionInputs.positionWS;

                output.normalWS =
                    normalize(
                        normalInputs.normalWS
                    );

                output.viewDirWS =
                    GetWorldSpaceViewDir(
                        positionInputs.positionWS
                    );

                output.waveValue =
                    wave;

                return output;
            }


            // =========================================================
            // FRAGMENT
            // =========================================================

            half4 frag(Varyings input) : SV_Target
            {
                float3 normal =
                    normalize(
                        input.normalWS
                    );

                float3 viewDirection =
                    normalize(
                        input.viewDirWS
                    );


                // =====================================================
                // FRESNEL
                // =====================================================

                float fresnel =
                    pow(
                        1.0 -
                        saturate(
                            dot(
                                normal,
                                viewDirection
                            )
                        ),
                        4.0
                    );


                // =====================================================
                // WATER COLOR
                // =====================================================

                float3 baseColor =
                    _WaterColor.rgb;


                // Brighter on wave crests.
                float crest =
                    saturate(
                        input.waveValue *
                        0.5 +
                        0.5
                    );

                baseColor =
                    lerp(
                        baseColor,
                        float3(
                            0.15,
                            0.55,
                            1.0
                        ),
                        crest * 0.25
                    );


                // =====================================================
                // EDGE REFLECTION
                // =====================================================

                baseColor =
                    lerp(
                        baseColor,
                        float3(
                            0.5,
                            0.85,
                            1.0
                        ),
                        fresnel * 0.75
                    );


                // =====================================================
                // SPECULAR HIGHLIGHT
                // =====================================================

                float3 lightDirection =
                    normalize(
                        float3(
                            -0.3,
                            1.0,
                            -0.4
                        )
                    );

                float3 halfVector =
                    normalize(
                        lightDirection +
                        viewDirection
                    );

                float specular =
                    pow(
                        saturate(
                            dot(
                                normal,
                                halfVector
                            )
                        ),
                        lerp(
                            32.0,
                            160.0,
                            _Smoothness
                        )
                    );


                baseColor +=
                    specular *
                    0.25;


                // =====================================================
                // TRANSPARENCY
                // =====================================================

                float alpha =
                    1.0 -
                    _Transparency;

                alpha =
                    lerp(
                        alpha,
                        0.95,
                        fresnel
                    );


                return half4(
                    baseColor,
                    alpha
                );
            }

            ENDHLSL
        }
    }
}