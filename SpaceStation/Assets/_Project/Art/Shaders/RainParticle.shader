// 빗방울 · 비 안개 입자 (사용자 2026-10-29 "비가 너무 대강 — 진짜 구름에서 비가 내리는 느낌", RainFx가 만드는 입자 재질):
// 텍스처 없이 입자 사각형 안에서 부드러운 덩이 (_Shape 0 = 가운데가 진한 빗방울 — 속도 방향으로 늘이면 가는 빗줄, 1 = 둥근 안개).
// 비 가림 (_Shelter): RainFx가 모듈마다 복제한 재질에 모델 좌표(_WorldToModule)를 넘기면, 가운데(_ShelterCenter)에서 본 각이
// 아치 방향(_ShelterAngles)에 걸친 입자는 감춘다 (구름이 아치 위를 지날 때 아치를 뚫고 내리지 않게). 반투명 · 양면 · 깊이 안 씀.
Shader "SpaceStation/RainParticle"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Shape ("Shape (0 빗방울 · 1 둥근 안개)", Float) = 0
        _Shelter ("비 가림 켬 (RainFx가 켬)", Float) = 0
        _ShelterCenter ("가림 가운데 (모델 x, z) · 띠 반쪽 너비", Vector) = (1, 1, 0.06, 0)
        _ShelterAngles ("가림 방향 셋 (라디안)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "RainParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Shape;
                float _Shelter;
                float4 _ShelterCenter;
                float4 _ShelterAngles;
                float4x4 _WorldToModule;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = input.color;
                o.uv = input.uv;
                return o;
            }

            float Covered(float2 d, float r, float angle)
            {
                float da = abs(frac((atan2(d.y, d.x) - angle) / 6.2832 + 0.5) - 0.5) * 6.2832;
                return 1.0 - smoothstep(_ShelterCenter.z * 0.7, _ShelterCenter.z, da * r);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 d = i.uv * 2.0 - 1.0;
                float shape = _Shape < 0.5 ? (1.0 - d.x * d.x) * (1.0 - d.y * d.y) : saturate(1.0 - dot(d, d));
                shape *= shape;
                float keep = 1.0;
                if (_Shelter > 0.5)
                {
                    float2 m = mul(_WorldToModule, float4(i.positionWS, 1.0)).xz - _ShelterCenter.xy;
                    float r = length(m);
                    keep = 1.0 - max(Covered(m, r, _ShelterAngles.x), max(Covered(m, r, _ShelterAngles.y), Covered(m, r, _ShelterAngles.z)));
                }
                half4 c = i.color * _BaseColor;
                c.a *= shape * keep;
                return c;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
