// 압력 막 (사용자 2026-11-02 "문을 좀 더 신비감 느껴지게"): 아치 안에서 가운데로 천천히 소용돌이치는 안개 + 테두리 쪽 금빛 + 드문드문 떠오르는 빛 알갱이.
// 맞은편이 비쳐 보여야 하므로 안개가 짙은 곳만 조금 가림. 지나갈 때 눈앞을 덮지 않게 가까이 가면 옅어짐.
// UV = 아치 기준 미터 (u = 아치 가운데에서 가로, v = 바닥에서 위 — Blender evo2_kit.arch_veil), _Arch = (반폭, 둥근 부분 시작 높이).
Shader "SpaceStation/PressureVeil"
{
    Properties
    {
        [HDR] _DeepColor ("Deep", Color) = (0.3, 0.1, 0.85, 1)
        [HDR] _MistColor ("Mist", Color) = (0.1, 0.5, 0.95, 1)
        [HDR] _EdgeColor ("Edge", Color) = (1.5, 1.05, 0.45, 1)
        [HDR] _MoteColor ("Mote", Color) = (2.4, 2.2, 1.6, 1)
        _Arch ("Arch (half width, spring height)", Vector) = (1.02, 1.6, 0, 0)
        _Speed ("Speed", Float) = 1
        _Swirl ("Swirl", Float) = 1.6
        _Veil ("Veil (cover)", Float) = 0.5
        _Glow ("Glow", Float) = 0.62
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "PressureVeil"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _MistColor;
                half4 _EdgeColor;
                half4 _MoteColor;
                float4 _Arch;
                float _Speed;
                float _Swirl;
                float _Veil;
                float _Glow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = input.uv;
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            float Fbm(float2 p)
            {
                return Noise(p) * 0.55 + Noise(p * 2.03 + 7.1) * 0.3 + Noise(p * 4.07 + 3.7) * 0.15;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y * _Speed;
                float2 q = i.uv - float2(0.0, _Arch.y * 0.82);                                   // 소용돌이 가운데 = 아치 한가운데쯤
                float r = length(q);
                float angle = _Swirl * exp(-r * 1.2) + t * 0.13;                                 // 가운데일수록 많이 감김, 천천히 돎
                float cs = cos(angle), sn = sin(angle);
                q = float2(q.x * cs - q.y * sn, q.x * sn + q.y * cs);
                float wide = Fbm(q * 1.35 + float2(0.0, -t * 0.09));
                float mist = smoothstep(0.32, 0.8, Fbm(q * 2.5 + wide * 1.7 + float2(t * 0.07, -t * 0.05)));

                // 테두리까지 거리 (아치 안쪽 +)
                float edgeDist = i.uv.y > _Arch.y ? _Arch.x - length(i.uv - float2(0.0, _Arch.y)) : _Arch.x - abs(i.uv.x);
                edgeDist = max(edgeDist, 0.0);
                float edge = exp(-edgeDist / 0.16);
                float hem = smoothstep(0.0, 0.5, i.uv.y);                                        // 바닥 쪽은 옅게

                // 드문드문 떠오르는 빛 알갱이 (칸 약 0.42 m, 넷 중 하나만)
                float2 cell = float2(i.uv.x * 2.4, i.uv.y * 2.4 - t * 0.16);
                float2 id = floor(cell);
                float2 at = frac(cell) - 0.5 - (float2(Hash(id + 3.1), Hash(id + 5.7)) - 0.5) * 0.6;
                float lit = step(0.75, Hash(id));
                float twinkle = 0.5 + 0.5 * sin(t * 1.9 + Hash(id + 9.3) * 40.0);
                float mote = lit * twinkle * twinkle * exp(-dot(at, at) * 900.0);

                float breathe = 0.88 + 0.12 * sin(t * 0.6);
                float near = saturate((length(_WorldSpaceCameraPos - i.positionWS) - 0.35) / 1.4);   // 지나갈 때 눈앞을 덮지 않게

                float3 color = lerp(_DeepColor.rgb, _MistColor.rgb, saturate(wide * 1.5 - 0.25)) * (0.28 + mist * 0.95) * hem;
                color = color * breathe * _Glow + _EdgeColor.rgb * edge * 0.5 + _MoteColor.rgb * mote;
                float alpha = saturate((0.1 + mist * 0.75) * _Veil * hem + edge * 0.25);
                return half4(color * near, alpha * near);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
