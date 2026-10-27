// 융합 발전소 · 융합 플랜트 플라즈마 고리 (사용자 2026-10-27 "실제 플라즈마 느낌"): 빛나는 기체가 흐르며 소용돌이치고,
// 가는 빛 실이 지나가며 맥동한다. 가산이라 겹친 곳이 더 밝다. 부품이 돌면(SpinOrbit) 잡음도 같이 돌아 고리를 따라 흐른다.
// UV가 없는 FX 메시라 물체 공간 위치로 잡음을 만든다. 마주 보는 면(관의 가운데)은 밝은 코어색, 가장자리는 테두리색.
// _BaseColor는 ModuleView가 비활성 · 파손 때 MaterialPropertyBlock으로 덮어 어둡게 한다.
Shader "SpaceStation/Plasma"
{
    Properties
    {
        [HDR] _BaseColor ("Tint (State)", Color) = (1, 1, 1, 1)
        [HDR] _CoreColor ("Core", Color) = (0.9, 1.6, 2.6, 1)
        [HDR] _EdgeColor ("Edge", Color) = (0.35, 0.45, 1.8, 1)
        _Scale ("Noise Scale", Float) = 14
        _Speed ("Flow Speed", Float) = 1.2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "Plasma"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "StationNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _CoreColor;
                half4 _EdgeColor;
                float _Scale;
                float _Speed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionOS = input.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y * _Speed;
                float3 p = i.positionOS * _Scale;
                float swirl = StationFbm3(p + float3(0.0, t, t * 0.7));                    // 흐르는 소용돌이
                float strand = StationNoise3(p * 2.7 - float3(t * 1.6, 0.0, t));          // 가는 빛 실
                float filaments = smoothstep(0.62, 0.86, strand);
                float facing = saturate(abs(dot(normalize(i.normalWS), normalize(i.viewWS))));
                float core = facing * facing;
                float pulse = 0.85 + 0.15 * sin(_Time.y * 4.0 + swirl * 6.0);
                float3 color = lerp(_EdgeColor.rgb, _CoreColor.rgb, saturate(core * 0.8 + filaments * 0.6));
                float intensity = (0.2 + 1.1 * swirl * swirl + filaments * 0.9) * pulse * (0.4 + 0.6 * core);
                return half4(color * intensity * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
