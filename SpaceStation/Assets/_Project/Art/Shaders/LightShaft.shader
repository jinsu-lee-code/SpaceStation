// 궤도 미러 빛줄기 (사용자 2026-10-27 "선처럼 보임 → 진짜 빛줄기", 2026-11-02 "그냥 선같아보여서 반사광 느낌이 안나"): LineRenderer가 카메라를 향한 띠로 그리고,
// 이 셰이더가 넓고 옅은 몸통 + 가운데 심 (가운데 한 줄만 밝으면 띠가 넓어도 선으로 보임), 길이로 뻗은 빛 결 (거울면 곳곳에서 나온 빛), 거울 쪽이 환한 반사 지점,
// 길이 방향으로 천천히 흐르는 밝기를 만든다. 수신기 쪽 끝은 스며들게.
// LineRenderer UV: x = 길이(0 거울 ~ 1 수신기), y = 폭. 꼭짓점 색(LineRenderer 색 · 투명도)을 곱한다. 가산.
Shader "SpaceStation/LightShaft"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (1.6, 1.4, 0.9, 1)
        _Speed ("Flow Speed", Float) = 0.6
        _Noise ("Noise Scale", Float) = 7
        _Streak ("Streak Count", Float) = 7
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "LightShaft"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "StationNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Speed;
                float _Noise;
                float _Streak;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float across = saturate(1.0 - abs(i.uv.y * 2.0 - 1.0));
                float body = smoothstep(0.0, 0.8, across);                                 // 넓고 옅은 몸통 (거울 지름 쯤에서 사라짐)
                float core = across * across * across;                                     // 가운데 심
                float ends = smoothstep(0.0, 0.015, i.uv.x) * smoothstep(1.0, 0.86, i.uv.x); // 거울에서 바로 시작, 수신기 쪽만 스며듦
                float grain = StationNoise3(float3(i.uv.y * _Streak, i.uv.x * 1.3 - _Time.y * _Speed * 0.12, _Time.y * 0.1));
                float streak = 0.55 + 0.9 * grain;                                          // 길이로 뻗은 빛 결
                float motes = StationNoise3(float3(i.uv.x * _Noise - _Time.y * _Speed, i.uv.y * 3.0, _Time.y * 0.3));
                float flow = 0.7 + 0.3 * motes;                                             // 천천히 흐르는 밝기
                float glint = 1.0 + 2.2 * exp(-i.uv.x * 11.0);                              // 거울 쪽 = 환한 반사 지점
                float light = (body * 0.5 + core * 0.5) * streak * flow * glint * ends;
                return half4(_BaseColor.rgb * i.color.rgb * (i.color.a * light), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
