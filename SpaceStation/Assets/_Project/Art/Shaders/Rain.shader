// 빗줄기 (진화 3 · 4단계 반영 ④, 2026-10-29 — 바이오스피어 도는 구름 아래 비): 모델에 구운 가는 빗줄기 판 위로 밝은 물방울 토막이
// 줄마다 다른 박자로 아래로 떨어진다 (FX 메시에 UV가 없어 물체 기준 높이로 계산 — 구름과 함께 돌아도 그대로). 가산 · 양면 · 옅게.
// _BaseColor는 ModuleView가 비활성 · 파손 때 어둡게 한다.
Shader "SpaceStation/Rain"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (0.3, 0.36, 0.45, 1)
        _Fall ("Fall: 촘촘함 · 속도 · 칸 크기 · 물방울 길이 (0 ~ 1)", Vector) = (9, 1.6, 0.012, 0.35)
        _Rest ("물방울 사이 밝기", Float) = 0.12
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
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.positionOS = input.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float h = frac(sin(dot(floor(i.positionOS.xz / max(_Fall.z, 1e-4)), float2(12.9898, 78.233))) * 43758.5453);
                float s = frac(i.positionOS.y * _Fall.x + _Time.y * _Fall.y * (0.8 + 0.4 * h) + h);
                float drop = smoothstep(1.0 - _Fall.w, 1.0, s);
                return half4(_BaseColor.rgb * (_Rest + 1.4 * drop), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
