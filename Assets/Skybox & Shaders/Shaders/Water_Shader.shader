Shader "Custom/WaterFluidShader"
{
    Properties
    {
        _WaterColor ("Water Color", Color) =
            (0.02, 0.45, 1.0, 0.45)

        _DeepColor ("Deep Water Color", Color) =
            (0.0, 0.10, 0.30, 0.65)

        _Transparency ("Transparency", Range(0,1)) = 0.45

        _Smoothness ("Smoothness", Range(0,1)) = 0.95

        _WaveSpeed ("Wave Speed", Range(0,5)) = 0.5

        _WaveStrength ("Wave Strength", Range(0,1)) = 0.05

        _WaveScale ("Wave Scale", Range(0.5,10)) = 3.0

        _FillLevel ("Fill Level", Range(0,1)) = 0

        _BottomY ("Bottom Y", Float) = -1

        _TopY ("Top Y", Float) = 1

        // 0 = X
        // 1 = Y
        // 2 = Z
        _FlowAxis ("Flow Axis", Float) = 1

        // 1 = Positive
        // -1 = Negative
        _FlowDirection ("Flow Direction", Float) = 1

        // Controlled by C#
        _WaveTime ("Wave Time", Float) = 0
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
            Name "Water"

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

                float3 positionOS : TEXCOORD0;

                float3 positionWS : TEXCOORD1;

                float3 normalWS : TEXCOORD2;

                float3 viewDirectionWS : TEXCOORD3;

                float boilAmount : TEXCOORD4;
            };


            CBUFFER_START(UnityPerMaterial)

                float4 _WaterColor;

                float4 _DeepColor;

                float _Transparency;

                float _Smoothness;

                float _WaveSpeed;

                float _WaveStrength;

                float _WaveScale;

                float _FillLevel;

                float _BottomY;

                float _TopY;

                float _FlowAxis;

                float _FlowDirection;

                float _WaveTime;

            CBUFFER_END


            // =====================================================
            // GET FLOW POSITION
            // =====================================================

            float GetFlowPosition(float3 positionOS)
            {
                if (_FlowAxis < 0.5)
                {
                    return positionOS.x;
                }

                if (_FlowAxis < 1.5)
                {
                    return positionOS.y;
                }

                return positionOS.z;
            }


            // =====================================================
            // GET FLOW MINIMUM
            // =====================================================

            float GetFlowMinimum()
            {
                if (_FlowAxis < 0.5)
                {
                    return _BottomY;
                }

                if (_FlowAxis < 1.5)
                {
                    return _BottomY;
                }

                return _BottomY;
            }


            // =====================================================
            // GET FLOW MAXIMUM
            // =====================================================

            float GetFlowMaximum()
            {
                return _TopY;
            }


            // =====================================================
            // VERTEX
            // =====================================================

            Varyings vert(Attributes input)
            {
                Varyings output;


                float3 positionOS =
                    input.positionOS.xyz;


                // =================================================
                // 1. HEIGHT FOR WAVE MASK
                // =================================================

                float height01 =
                    saturate(
                        (
                            positionOS.y -
                            _BottomY
                        )
                        /
                        max(
                            _TopY -
                            _BottomY,
                            0.0001
                        )
                    );


                // =================================================
                // 2. TOP BOILING ZONE
                // =================================================

                float boilMask =
                    smoothstep(
                        0.55,
                        1.0,
                        height01
                    );


                // =================================================
                // 3. CONTROLLER TIME
                // =================================================

                float time =
                    _WaveTime *
                    _WaveSpeed;


                // =================================================
                // 4. LARGE WAVES
                // =================================================

                float largeWave1 =
                    sin(
                        positionOS.x *
                        _WaveScale +
                        time
                    );


                float largeWave2 =
                    sin(
                        positionOS.z *
                        (_WaveScale * 1.2) -
                        time * 0.85
                    );


                float largeWave3 =
                    sin(
                        (
                            positionOS.x +
                            positionOS.z
                        )
                        *
                        (_WaveScale * 0.65) +
                        time * 0.7
                    );


                // =================================================
                // 5. SMALL BOILING MOVEMENT
                // =================================================

                float boilWave1 =
                    sin(
                        positionOS.x * 7.0 +
                        positionOS.z * 4.0 +
                        time * 2.8
                    );


                float boilWave2 =
                    sin(
                        positionOS.x * 4.0 -
                        positionOS.z * 8.0 +
                        time * 3.6
                    );


                float boilWave3 =
                    cos(
                        (
                            positionOS.x +
                            positionOS.z
                        )
                        * 6.0 +
                        time * 4.2
                    );


                // =================================================
                // 6. COMBINE
                // =================================================

                float smoothWave =
                    largeWave1 * 0.30 +
                    largeWave2 * 0.25 +
                    largeWave3 * 0.20;


                float boilingWave =
                    boilWave1 * 0.10 +
                    boilWave2 * 0.08 +
                    boilWave3 * 0.07;


                float combinedWave =
                    smoothWave +
                    boilingWave;


                // =================================================
                // 7. WAVE DISPLACEMENT
                // =================================================

                positionOS.y +=
                    combinedWave *
                    _WaveStrength *
                    boilMask;


                // =================================================
                // 8. WAVE NORMAL
                // =================================================

                float sampleDistance =
                    0.05;


                float waveX1 =
                    sin(
                        (
                            positionOS.x +
                            sampleDistance
                        )
                        * _WaveScale +
                        time
                    );


                float waveX0 =
                    sin(
                        (
                            positionOS.x -
                            sampleDistance
                        )
                        * _WaveScale +
                        time
                    );


                float waveZ1 =
                    sin(
                        (
                            positionOS.z +
                            sampleDistance
                        )
                        * (_WaveScale * 1.2) -
                        time * 0.85
                    );


                float waveZ0 =
                    sin(
                        (
                            positionOS.z -
                            sampleDistance
                        )
                        * (_WaveScale * 1.2) -
                        time * 0.85
                    );


                float slopeX =
                    (
                        waveX1 -
                        waveX0
                    )
                    /
                    (
                        sampleDistance *
                        2.0
                    );


                float slopeZ =
                    (
                        waveZ1 -
                        waveZ0
                    )
                    /
                    (
                        sampleDistance *
                        2.0
                    );


                float3 animatedNormal =
                    normalize(
                        float3(
                            -slopeX *
                            _WaveStrength *
                            boilMask,

                            1.0,

                            -slopeZ *
                            _WaveStrength *
                            boilMask
                        )
                    );


                // =================================================
                // 9. TRANSFORM
                // =================================================

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(
                        positionOS
                    );


                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(
                        animatedNormal
                    );


                output.positionHCS =
                    positionInputs.positionCS;


                output.positionOS =
                    positionOS;


                output.positionWS =
                    positionInputs.positionWS;


                output.normalWS =
                    normalize(
                        normalInputs.normalWS
                    );


                output.viewDirectionWS =
                    GetWorldSpaceViewDir(
                        positionInputs.positionWS
                    );


                output.boilAmount =
                    boilMask;


                return output;
            }


            // =====================================================
            // FRAGMENT
            // =====================================================

            half4 frag(Varyings input) : SV_Target
            {
                // =================================================
                // 1. FLOW POSITION
                // =================================================

                float flowPosition =
                    GetFlowPosition(
                        input.positionOS
                    );


                float flowMin =
                    GetFlowMinimum();


                float flowMax =
                    GetFlowMaximum();


                // =================================================
                // 2. NORMALIZED FLOW
                // =================================================

                float normalizedFlow =
                    saturate(
                        (
                            flowPosition -
                            flowMin
                        )
                        /
                        max(
                            flowMax -
                            flowMin,
                            0.0001
                        )
                    );


                // =================================================
                // 3. REVERSE DIRECTION
                // =================================================

                if (_FlowDirection < 0.0)
                {
                    normalizedFlow =
                        1.0 -
                        normalizedFlow;
                }


                // =================================================
                // 4. FILL CLIP
                // =================================================

                clip(
                    _FillLevel -
                    normalizedFlow
                );


                // =================================================
                // 5. HEIGHT FOR COLOR
                // =================================================

                float normalizedY =
                    saturate(
                        (
                            input.positionOS.y -
                            _BottomY
                        )
                        /
                        max(
                            _TopY -
                            _BottomY,
                            0.0001
                        )
                    );


                // =================================================
                // 6. VIEW
                // =================================================

                float3 viewDirection =
                    normalize(
                        input.viewDirectionWS
                    );


                // =================================================
                // 7. NORMAL
                // =================================================

                float3 normal =
                    normalize(
                        input.normalWS
                    );


                // =================================================
                // 8. FRESNEL
                // =================================================

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


                // =================================================
                // 9. WATER COLOR
                // =================================================

                float3 waterColor =
                    lerp(
                        _DeepColor.rgb,
                        _WaterColor.rgb,
                        normalizedY
                    );


                waterColor =
                    lerp(
                        waterColor,
                        float3(
                            0.12,
                            0.55,
                            1.0
                        ),
                        input.boilAmount *
                        0.15
                    );


                // =================================================
                // 10. EDGE BRIGHTNESS
                // =================================================

                waterColor =
                    lerp(
                        waterColor,
                        float3(
                            0.35,
                            0.75,
                            1.0
                        ),
                        fresnel *
                        0.7
                    );


                // =================================================
                // 11. SPECULAR
                // =================================================

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
                            128.0,
                            _Smoothness
                        )
                    );


                waterColor +=
                    specular *
                    0.18;


                // =================================================
                // 12. TRANSPARENCY
                // =================================================

                float alpha =
                    _WaterColor.a *
                    (
                        1.0 -
                        _Transparency
                    );


                alpha =
                    lerp(
                        alpha,
                        0.8,
                        fresnel
                    );


                return half4(
                    waterColor,
                    alpha
                );
            }

            ENDHLSL
        }
    }
}