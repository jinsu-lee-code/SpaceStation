// 궤도 미러 빛줄기 (사용자 2026-10-27 "선처럼 보임 → 진짜 빛줄기"): LineRenderer가 카메라를 향한 띠로 그리고,
// 이 셰이더가 띠 가운데만 밝고 가장자리로 부드럽게 사라지게(빛기둥), 길이 방향으로 빛 알갱이가 흐르게, 양끝은 스며들게 한다.
// LineRenderer UV: x = 길이(0 거울 ~ 1 수신기), y = 폭. 꼭짓점 색(LineRenderer 색 · 투명도)을 곱한다. 가산.
Shader "SpaceStation/LightShaft"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (1.6, 1.4, 0.9, 1)
        _Speed ("Flow Speed", Float) = 0.6
        _Noise ("Noise Scale", Float) = 7
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
                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                float soft = pow(saturate(across), 1.8);                                   // 가운데만 밝은 빛기둥
                float ends = smoothstep(0.0, 0.08, i.uv.x) * smoothstep(1.0, 0.9, i.uv.x); // 양끝 스며듦
                float motes = StationNoise3(float3(i.uv.x * _Noise - _Time.y * _Speed, i.uv.y * 3.0, _Time.y * 0.3));
                float flow = 0.55 + 0.45 * motes * motes;                                   // 흐르는 빛 알갱이
                return half4(_BaseColor.rgb * i.color.rgb * (i.color.a * soft * ends * flow), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
