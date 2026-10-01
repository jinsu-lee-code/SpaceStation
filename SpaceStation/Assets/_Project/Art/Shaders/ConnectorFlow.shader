// 5-6 연결 통로 불빛 띠: 길이 방향(오브젝트 Z, -0.5~0.5)으로 빛 덩어리가 흐른다.
// MaterialPropertyBlock: _Length(실제 길이, 간격 유지용) / _Flow(+1 = +Z 쪽으로, -1 = 반대) / _Energy(0 = 꺼짐, 1 = 켜짐)
Shader "SpaceStation/ConnectorFlow"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (0.3, 1.2, 2.6, 1)
        _Length ("Length (m)", Float) = 1
        _Flow ("Flow Direction", Float) = 1
        _Energy ("Energy", Range(0, 1)) = 1
        _Spacing ("Pulse Spacing (m)", Float) = 0.35
        _Speed ("Pulse Speed (m/s)", Float) = 0.6
        _Idle ("Idle Glow", Range(0, 1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            Name "ConnectorFlow"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Length;
                float _Flow;
                half _Energy;
                float _Spacing;
                float _Speed;
                half _Idle;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float along : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.along = (input.positionOS.z + 0.5) * _Length; // 0 ~ 길이(m)
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float phase = frac(i.along / max(_Spacing, 0.01) - _Time.y * _Speed / max(_Spacing, 0.01) * _Flow);
                // 짧고 밝은 머리 + 흐름 반대쪽으로 꼬리
                half pulse = _Flow >= 0 ? smoothstep(0.55, 1.0, phase) : smoothstep(0.55, 1.0, 1.0 - phase);
                half glow = lerp(0.04h, _Idle + pulse * 1.6h, _Energy);
                return half4(_BaseColor.rgb * glow, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
