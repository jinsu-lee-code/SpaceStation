// 무지개 띠 빛 흐름 (진화 3 · 4단계 반영 ④, 2026-10-29): 가산 빛 띠 위로 밝은 물결이 비스듬히 천천히 지나가 무지개가 살아 있는 빛처럼 일렁인다.
// FX 메시에 UV가 없어 월드 위치로 계산 (띠마다 색은 재질 _BaseColor 그대로 — 밝기만 바뀜, 색약 배려). 가산 · 양면.
// _BaseColor는 ModuleView가 비활성 · 파손 때 어둡게 한다.
Shader "SpaceStation/Shimmer"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _Sweep ("Sweep: 파장 · 속도 · 세기 · 바탕 밝기", Vector) = (0.6, 0.25, 0.7, 0.8)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "Shimmer"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _Sweep;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float d = dot(i.positionWS, normalize(float3(0.7, 0.45, 0.55)));
                float band = pow(saturate(0.5 + 0.5 * cos(6.2832 * (d - _Time.y * _Sweep.y) / max(_Sweep.x, 1e-3))), 3.0);
                return half4(_BaseColor.rgb * (_Sweep.w + _Sweep.z * band), _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
