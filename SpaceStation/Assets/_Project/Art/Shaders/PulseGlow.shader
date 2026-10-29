// 흐르는 빛 (진화 3 · 4단계 반영 ④, 2026-10-29): 형광 빛줄 · 점 (URP Lit 형광 재질과 같은 바탕 + 발광)의 발광 세기가
// 한 점(_Center, 모델 기준)에서 퍼져 나가거나(속도 +) 모여드는(속도 −) 빛 물결 + 가운데 맥박 + 칸마다 따로 깜빡임으로 바뀐다.
// FX 메시에 UV가 없어 위치로만 계산 — AI 중추 빛 신경 (양쪽 문에서 뇌로 모여듦 + 시냅스 깜빡임 + 뇌 맥박), 심우주 통신기지 신호 호 · 고리
// (가운데 돔 위에서 퍼져 나감), 항성 발전망 꿀 빛 (꿀 언덕에서 천천히 번짐), 궤도 항만 진주 빛 (진주마다 따로 숨 쉬듯).
// 색은 그대로 두고 밝기만 바뀐다 (색약 배려). _BaseColor · _EmissionColor는 ModuleView가 비활성 · 파손 때 MaterialPropertyBlock으로 덮어 어둡게 한다.
Shader "SpaceStation/PulseGlow"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.3, 0.3, 0.3, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        [HDR] _EmissionColor ("Emission", Color) = (1, 1, 1, 1)
        _Center ("Center (object)", Vector) = (0, 0, 0, 0)
        _Wave ("Wave: 파장 · 속도 (+ 퍼짐, − 모임) · 날카로움 · 세기", Vector) = (0.35, 0.3, 4, 1)
        _Floor ("Floor (가장 어두울 때 발광 배율)", Float) = 0.45
        _Beat ("Beat: 세기 · 초당 횟수 · 반지름 (0 = 어디나)", Vector) = (0, 1, 0, 0)
        _Spark ("Spark: 세기 · 칸 크기 · 빠르기", Vector) = (0, 0.03, 2, 0)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half _Smoothness;
        half4 _EmissionColor;
        float4 _Center;
        float4 _Wave;
        float _Floor;
        float4 _Beat;
        float4 _Spark;
    CBUFFER_END

    float PulseHash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }

    // 발광 배율 (물체 기준 위치)
    float PulseLevel(float3 p)
    {
        float t = _Time.y;
        float d = distance(p, _Center.xyz);
        float band = pow(saturate(0.5 + 0.5 * cos(6.2832 * (d - t * _Wave.y) / max(_Wave.x, 1e-3))), _Wave.z);
        float b = frac(t * _Beat.y);
        float beat = pow(saturate(1.0 - abs(b - 0.08) * 9.0), 2.0) + 0.6 * pow(saturate(1.0 - abs(b - 0.26) * 9.0), 2.0); // 쿵 · 쿵
        float beatMask = _Beat.z > 0.0 ? 1.0 - smoothstep(_Beat.z * 0.6, _Beat.z, d) : 1.0;
        float h = PulseHash(floor(p / max(_Spark.y, 1e-3)));
        float spark = pow(saturate(0.5 + 0.5 * sin(t * _Spark.z * (0.6 + 0.8 * h) + h * 6.2832)), 6.0);
        return _Floor + _Wave.w * band + _Beat.x * beat * beatMask + _Spark.x * spark;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.positionOS = input.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb;
                surface.alpha = 1.0h;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1.0h;
                surface.emission = _EmissionColor.rgb * PulseLevel(i.positionOS);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1.0h;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct ShadowVaryings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 ShadowFrag(ShadowVaryings i) : SV_TARGET { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DepthVaryings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half DepthFrag(DepthVaryings i) : SV_TARGET { return i.positionCS.z; }
            ENDHLSL
        }
    }
    FallBack Off
}
