// 5-4 선택 강조: 모듈과 같은 메시를 한 번 더 그려 가장자리(프레넬)만 더해지는 빛. 원래 색·상태 틴트는 가리지 않는다.
// ModuleView가 선택될 때 각 렌더러에 이 재질의 복제 렌더러를 켠다.
Shader "SpaceStation/SelectionRim"
{
    Properties
    {
        [HDR] _RimColor ("Rim Color", Color) = (2.2, 1.8, 0.5, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _PulseSpeed ("Pulse Speed", Float) = 2.5
        _PulseAmount ("Pulse Amount", Range(0, 1)) = 0.3
        _Fill ("Fill (약한 전체 틴트)", Range(0, 0.5)) = 0.08
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "SelectionRim"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RimColor;
                half _RimPower;
                float _PulseSpeed;
                half _PulseAmount;
                half _Fill;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower);
                half pulse = 1.0h - _PulseAmount * (0.5h + 0.5h * sin(_Time.y * _PulseSpeed));
                return half4(_RimColor.rgb * (rim + _Fill) * pulse, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
