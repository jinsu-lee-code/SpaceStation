// 빗줄기 (진화 3 · 4단계 반영 ④, 2026-10-29 — 바이오스피어 도는 구름 아래 비): 모델에 구운 가는 빗줄기 판 위로 밝은 물방울 토막이
// 줄마다 다른 박자로 아래로 떨어진다 (FX 메시에 UV가 없어 물체 기준 높이로 계산 — 구름과 함께 돌아도 그대로). 가산 · 양면 · 옅게.
// 비 가림 (_Shelter, 사용자 2026-10-29 "빗줄기가 기둥을 뚫고 올라감"): RainShelterFx가 이 모듈 것으로 복제한 재질에 모델 좌표(_WorldToModule)를 넘기면,
// 가운데(_ShelterCenter)에서 본 각도가 아치 방향(_ShelterAngles)에 걸친 빗줄기는 감춘다 (구름이 아치 위를 지날 때 그 자리만 비가 끊김).
// _BaseColor는 ModuleView가 비활성 · 파손 때 어둡게 한다.
Shader "SpaceStation/Rain"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (0.3, 0.36, 0.45, 1)
        _Fall ("Fall: 촘촘함 · 속도 · 칸 크기 · 물방울 길이 (0 ~ 1)", Vector) = (9, 1.6, 0.012, 0.35)
        _Rest ("물방울 사이 밝기", Float) = 0.12
        _Shelter ("비 가림 켬 (RainShelterFx가 켬)", Float) = 0
        _ShelterCenter ("가림 가운데 (모델 x, z) · 띠 반쪽 너비", Vector) = (1, 1, 0.06, 0)
        _ShelterAngles ("가림 방향 셋 (라디안)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "Rain"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _Fall;
                float _Rest;
                float _Shelter;
                float4 _ShelterCenter;
                float4 _ShelterAngles;
                float4x4 _WorldToModule;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.positionOS = input.positionOS.xyz;
                return o;
            }

            // 아치 방향 하나에 걸친 정도 (0 = 비 그대로, 1 = 감춤): 가운데에서 잰 각도 차 × 반지름 = 아치 띠에서 떨어진 가로 거리
            float Covered(float2 d, float r, float angle)
            {
                float da = abs(frac((atan2(d.y, d.x) - angle) / 6.2832 + 0.5) - 0.5) * 6.2832;
                return 1.0 - smoothstep(_ShelterCenter.z * 0.7, _ShelterCenter.z, da * r);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float h = frac(sin(dot(floor(i.positionOS.xz / max(_Fall.z, 1e-4)), float2(12.9898, 78.233))) * 43758.5453);
                float s = frac(i.positionOS.y * _Fall.x + _Time.y * _Fall.y * (0.8 + 0.4 * h) + h);
                float drop = smoothstep(1.0 - _Fall.w, 1.0, s);
                float keep = 1.0;
                if (_Shelter > 0.5)
                {
                    float2 d = mul(_WorldToModule, float4(i.positionWS, 1.0)).xz - _ShelterCenter.xy;
                    float r = length(d);
                    float cover = max(Covered(d, r, _ShelterAngles.x), max(Covered(d, r, _ShelterAngles.y), Covered(d, r, _ShelterAngles.z)));
                    keep = 1.0 - cover;
                }
                return half4(_BaseColor.rgb * (_Rest + 1.4 * drop) * keep, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
