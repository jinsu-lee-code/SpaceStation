// 3단계 궤도 방어망 수정 결정 막 (사용자 2026-10-29 "실드 계열 단계별 이펙트 — 차단 = 수정 결정 막"):
// 운석이 닿은 자리에 육각 수정판이 맺히고, 깨져 흩어지는 조각. CrystalBarrierFx가 매 프레임 한 메시로 그림.
// 꼭짓점 색 = 조각 빛깔(rgb) · 사라짐(a), uv.x = 판 가운데 0 ~ 가장자리 1 (모서리가 밝음), uv.y = 맺힐 때 번쩍임.
// 가산 · 양면: 바라보는 각도 · 자리 · 시간에 따라 무지개빛이 돌고, 테두리(프레넬)와 모서리가 밝으며 해 쪽은 흰 반짝임.
Shader "SpaceStation/CrystalShard"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (0.9, 1.0, 1.2, 1)
        _Rainbow ("Rainbow Amount", Range(0, 1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "CrystalShard"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Rainbow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }

            float3 Hue(float h)
            {
                return saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            half4 Frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS + float3(0, 1e-5, 0)); // 법선 0에서 NaN 막기
                n = front ? n : -n;
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float ndv = saturate(abs(dot(n, v)));
                float rim = pow(max(1.0 - ndv, 1e-4), 2.5);
                float edge = smoothstep(0.8, 1.0, i.uv.x);
                float3 rainbow = Hue(ndv * 1.3 + dot(i.positionWS, float3(0.7, 0.45, 0.55)) * 1.4 + _Time.y * 0.3);
                float3 tint = lerp(float3(1, 1, 1), rainbow, _Rainbow) * i.color.rgb;
                Light sun = GetMainLight();
                float spec = pow(max(saturate(dot(reflect(-v, n), sun.direction)), 1e-4), 40.0);
                float glow = 0.05 + 0.8 * rim + 1.0 * edge + 1.2 * i.uv.y; // 속은 거의 비치고 모서리 · 윤곽이 빛남
                float3 c = _BaseColor.rgb * (tint * glow + spec * 1.2);
                c *= i.color.a;
                return half4(min(max(c, 0.0), 64.0), 1); // NaN · 무한대가 블룸으로 번지지 않게
            }
            ENDHLSL
        }
    }
    FallBack Off
}
