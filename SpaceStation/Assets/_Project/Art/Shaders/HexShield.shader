// 1단계 실드 매트릭스 육각 무늬 (사용자 2026-10-29 "실드 계열 단계별 이펙트 — 1단계도 조금 강하게, Shield Shader FREE 참고"):
// 운석이 닿으면 기존 파란 구면 파문 위로, 닿은 자리에서 육각 격자 무늬가 구면을 따라 퍼지며 잠깐 비친다 (퍼지는 앞 가장자리 밝음 → 뒤로 옅어짐).
// 구 메시(물체 기준 단위 구)에 HexBarrierFx가 _HitDir(물체 기준 닿은 방향) · _Spread(0 닿은 자리 ~ 1 반대편) · _Fade를 넘긴다.
// 격자는 닿은 자리 기준 등거리 방위 투영으로 펼쳐 그림 (닿은 쪽 반구 안에서 옅어져 사라짐 — 기존 파문이 구 전체를 맡음).
// 가산 · 양면 (구 뒤쪽에 닿아도 비쳐 보이게, 뒷면은 옅게).
Shader "SpaceStation/HexShield"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (0.35, 1.0, 2.6, 1)
        _HitDir ("Hit Direction (object)", Vector) = (0, 1, 0, 0)
        _Spread ("Spread (0 ~ 1)", Float) = 0.3
        _Fade ("Fade", Float) = 1
        _HexScale ("Hex Cells per Radian", Float) = 7
        _LineWidth ("Line Width", Float) = 0.07
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "HexShield"
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
                float4 _HitDir;
                float _Spread;
                float _Fade;
                float _HexScale;
                float _LineWidth;
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

            // 육각 칸 안에서 가운데 0 ~ 가장자리 0.5 (뾰족한 꼭짓점이 위아래인 격자)
            float HexEdge(float2 p)
            {
                const float2 s = float2(1.0, 1.7320508);
                float4 hc = floor(float4(p, p - float2(0.5, 1.0)) / s.xyxy) + 0.5;
                float4 h = float4(p - hc.xy * s, p - (hc.zw + 0.5) * s);
                float2 g = dot(h.xy, h.xy) < dot(h.zw, h.zw) ? h.xy : h.zw;
                float2 q = abs(g);
                return max(dot(q, s * 0.5), q.x);
            }

            half4 Frag(Varyings i, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.positionOS + float3(0, 1e-5, 0));
                float3 hit = normalize(_HitDir.xyz + float3(0, 1e-5, 0));
                float c = dot(n, hit);
                float rad = acos(clamp(c, -1.0, 1.0));
                float ang = rad / 3.14159265;                                        // 0 닿은 자리 ~ 1 반대편
                // 닿은 자리 기준 등거리 방위 투영 (구면 거리 그대로 펼침 — 평면 투영은 옆쪽 칸이 늘어져 물결무늬가 생김)
                float3 t1 = normalize(cross(hit, abs(hit.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0)));
                float3 t2 = cross(hit, t1);
                float2 tang = float2(dot(n, t1), dot(n, t2));
                float2 p = tang / max(length(tang), 1e-4) * rad * _HexScale;
                float edge = HexEdge(p);
                float aa = fwidth(edge) * 1.2;
                float grid = smoothstep(0.5 - _LineWidth - aa, 0.5 - _LineWidth * 0.3, edge);
                // 스치는 각(윤곽)에서는 칸이 촘촘해져 깜빡이므로 격자를 감춤 (윤곽 빛은 기존 파란 파문이 맡음), 구 뒤쪽 면은 옅게
                float3 nW = normalize(mul((float3x3)UNITY_MATRIX_M, n));
                float facing = abs(dot(nW, GetWorldSpaceNormalizeViewDir(i.positionWS)));
                grid *= smoothstep(0.15, 0.45, facing) * (frontFace ? 1.0 : 0.35);
                float wd = (ang - _Spread) / 0.03;
                float front = exp(-wd * wd);                                         // 퍼지는 앞 가장자리
                float inside = saturate(1.0 - (_Spread - ang) / 0.12) * step(ang, _Spread + 0.03); // 앞 가장자리 뒤로 옅어짐
                float reach = 1.0 - smoothstep(0.22, 0.5, ang);                      // 멀리 갈수록 옅어져 닿은 쪽 반구 안에서 사라짐
                float glow = (grid * (0.22 * inside + 1.0 * front) + 0.12 * front * (frontFace ? 1.0 : 0.35)) * reach * _Fade;
                return half4(min(_BaseColor.rgb * glow, 64.0), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
