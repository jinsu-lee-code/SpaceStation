// 12-5 학교 천문대 망원경 렌즈 (사용자 요청 2026-10-14): 반투명한 어두운 유리에 우주 배경이 비친다.
// 카메라 → 렌즈 반사 방향으로 우주 배경 큐브맵(_SkyCube, 스카이박스를 구운 것)을 읽어, 보는 각도에 따라 비치는 하늘이 바뀐다.
// 가장자리(프레넬)일수록 반사가 강하고 불투명해지며, 태양(주 광원) 반짝임이 한 점 생긴다.
// _BaseColor는 상태 틴트용 배율 (ModuleView가 비활성·파손 시 MaterialPropertyBlock으로 덮어씀).
Shader "SpaceStation/SkyLens"
{
    Properties
    {
        _BaseColor ("Tint (state)", Color) = (1, 1, 1, 1)
        _GlassColor ("Glass Color", Color) = (0.04, 0.06, 0.13, 0.72)
        [NoScaleOffset] _SkyCube ("Sky Cubemap", Cube) = "black" {}
        _SkyStrength ("Sky Strength", Range(0, 4)) = 1.6
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 3
        _SpecPower ("Sun Glint Power", Range(8, 512)) = 160
        _SpecStrength ("Sun Glint Strength", Range(0, 4)) = 1.5
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "SkyLens"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GlassColor;
                half _SkyStrength;
                half _FresnelPower;
                half _SpecPower;
                half _SpecStrength;
            CBUFFER_END
            TEXTURECUBE(_SkyCube);
            SAMPLER(sampler_SkyCube);

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
                float3 r = reflect(-v, n);
                half fresnel = pow(1.0h - saturate(dot(n, v)), _FresnelPower);
                half3 sky = SAMPLE_TEXTURECUBE(_SkyCube, sampler_SkyCube, r).rgb * _SkyStrength;
                Light sun = GetMainLight();
                half glint = pow(saturate(dot(r, sun.direction)), _SpecPower) * _SpecStrength;
                half3 color = _GlassColor.rgb + sky * (0.55h + 0.45h * fresnel) + sun.color * glint;
                half alpha = saturate(lerp(_GlassColor.a, 1.0h, fresnel) + glint);
                return half4(color * _BaseColor.rgb, alpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
