// 5-5 실드 Emitter 외피: 반투명 + 가장자리 빛(프레넬) + 안쪽으로 흐르는 에너지 물결.
// _BaseColor는 상태 틴트용 배율 (ModuleView가 비활성·파손 시 MaterialPropertyBlock으로 덮어씀). 실제 색은 _ShellColor.
Shader "SpaceStation/ShieldShell"
{
    Properties
    {
        _BaseColor ("Tint (state)", Color) = (1, 1, 1, 1)
        [HDR] _ShellColor ("Shell Color", Color) = (0.25, 0.75, 2.2, 1)
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.12
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _RimStrength ("Rim Strength", Range(0, 4)) = 1.3
        _WaveScale ("Wave Scale", Float) = 18
        _WaveSpeed ("Wave Speed", Float) = 1.6
        _WaveStrength ("Wave Strength", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "ShieldShell"
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
                half4 _ShellColor;
                half _FillAlpha;
                half _RimPower;
                half _RimStrength;
                float _WaveScale;
                float _WaveSpeed;
                half _WaveStrength;
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
                float3 positionOS : TEXCOORD2;
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
                o.positionOS = input.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0h - saturate(dot(n, v)), _RimPower);
                // 서로 다른 방향의 사인 물결 두 개를 겹쳐 흐르는 플라스마 느낌
                float3 p = i.positionOS * _WaveScale;
                float t = _Time.y * _WaveSpeed;
                half wave = sin(p.x + p.y * 0.7 + t) * sin(p.z * 1.3 - p.y * 0.5 - t * 1.3);
                wave = saturate(wave * 0.5h + 0.5h);
                wave = wave * wave;
                half alpha = saturate(_FillAlpha + rim * _RimStrength * 0.7h + wave * _WaveStrength * 0.5h) * _BaseColor.a;
                half3 color = _ShellColor.rgb * _BaseColor.rgb * (0.6h + rim * _RimStrength + wave * _WaveStrength);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
