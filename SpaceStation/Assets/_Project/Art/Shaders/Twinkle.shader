// 진화 복지 · 문화 센터 별빛 점: 점마다 다른 박자로 반짝인다 (사용자 요청 2026-10-22).
// 꼭짓점 색 R = 점의 위상 (Blender twinkle_phase가 이어진 점마다 무작위로 넣음) → 점마다 속도 · 박자가 다르다.
// 색은 그대로 두고 밝기만 바뀐다 (색약 배려). _BaseColor는 ModuleView가 비활성 · 파손 때 MaterialPropertyBlock으로 덮어 어둡게 한다.
Shader "SpaceStation/Twinkle"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (1.4, 1.4, 1.5, 1)
        _Min ("Dim Brightness", Range(0, 1)) = 0.2
        _Peak ("Peak Brightness", Float) = 1.6
        _Speed ("Twinkle Speed", Float) = 1.6
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            Name "Twinkle"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Min;
                half _Peak;
                float _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half glow : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // 점 하나의 꼭짓점은 위상이 같으므로 밝기를 꼭짓점에서 한 번만 계산
                float phase = input.color.r;
                float rate = _Speed * (0.6 + 0.8 * frac(phase * 13.7));
                float s = 0.5 + 0.5 * sin(_Time.y * rate + phase * 6.2832);
                o.glow = lerp(_Min, _Peak, s * s * s); // 세제곱 = 대부분 은은하고 가끔 짧게 번쩍
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                return half4(_BaseColor.rgb * i.glow, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
