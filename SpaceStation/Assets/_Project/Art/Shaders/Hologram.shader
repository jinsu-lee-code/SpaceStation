// 5-4 배치 고스트 홀로그램: 반투명 + 가장자리 빛(프레넬) + 위로 흐르는 스캔라인 + 약한 깜빡임.
// 색은 _BaseColor (BuildController가 MaterialPropertyBlock으로 초록/빨강을 부드럽게 전환).
Shader "SpaceStation/Hologram"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (0.2, 1, 0.3, 0.45)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimStrength ("Rim Strength", Range(0, 4)) = 1.6
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.28
        _ScanDensity ("Scanline Density (per unit)", Float) = 14
        _ScanSpeed ("Scanline Speed", Float) = 1.2
        _ScanStrength ("Scanline Strength", Range(0, 1)) = 0.35
        _Flicker ("Flicker", Range(0, 1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Hologram"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _RimPower;
                half _RimStrength;
                half _FillAlpha;
                float _ScanDensity;
                float _ScanSpeed;
                half _ScanStrength;
                half _Flicker;
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
                // 위로 흐르는 가는 줄
                float scan = frac(i.positionWS.y * _ScanDensity - _Time.y * _ScanSpeed);
                // 'line'은 HLSL 예약어(지오메트리 셰이더 프리미티브)라 쓸 수 없음
                half scanLine = smoothstep(0.0, 0.12, scan) * (1.0 - smoothstep(0.12, 0.3, scan));
                half flicker = 1.0h - _Flicker * step(0.93, frac(sin(floor(_Time.y * 24.0) * 12.9898) * 43758.5453));
                half alpha = saturate((_FillAlpha + rim * _RimStrength * 0.6h + scanLine * _ScanStrength) * _BaseColor.a * 2.2h) * flicker;
                half3 color = _BaseColor.rgb * (1.0h + rim * _RimStrength + scanLine * _ScanStrength);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
