// 부드러운 무지개 띠 (바이오스피어 폭포 물보라 무지개 · 프리즘 고리, 사용자 2026-11-11 "무지개가 과하게 선명 — 오로라처럼 희미하게"):
// 띠 하나 (UV u = 길이, v = 바깥 0 빨강 → 안 1 보라)에 색을 셰이더로 — 파스텔 · 가산 · 옅게, 띠 가장자리와 두 발끝이 흐려지고 천천히 일렁임. 양면.
Shader "SpaceStation/SoftRainbow"
{
    Properties
    {
        _Intensity ("세기", Range(0, 2)) = 0.35
        _Pastel ("파스텔 (흰빛 섞기)", Range(0, 1)) = 0.35
        _EndFade ("발끝 흐림 (u 양 끝, 0 = 고리)", Range(0, 0.5)) = 0.25
        _EdgeFade ("띠 가장자리 흐림 (v)", Range(0.01, 0.5)) = 0.3
        _Shimmer ("일렁임", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity, _Pastel, _EndFade, _EdgeFade, _Shimmer;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                return o;
            }

            float3 Hue(float h)
            {
                return saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x, v = i.uv.y;
                float3 c = lerp(Hue(v * 0.78), float3(1, 1, 1), _Pastel);
                float edge = smoothstep(0.0, _EdgeFade, v) * smoothstep(1.0, 1.0 - _EdgeFade, v);
                float ends = _EndFade > 0.001 ? smoothstep(0.0, _EndFade, u) * smoothstep(1.0, 1.0 - _EndFade, u) : 1.0;
                float shimmer = 1.0 - _Shimmer + _Shimmer * (0.5 + 0.5 * sin(u * 23.0 + _Time.y * 0.7 + v * 5.0));
                return half4(c * (_Intensity * edge * ends * shimmer), 1.0);
            }
            ENDHLSL
        }
    }
}
