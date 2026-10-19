// 13-7a 꺼져 가는 지구 (사용자 결정 2026-10-19): 정거장 바깥 배경에 떠 있는 둥근 지구 (BlenderWork/Earth/earth_builder.py → SM_Earth · T_Earth*).
// 해(주 광원) 쪽은 낮 — 대륙 · 바다 색, 반대쪽은 밤 — 도시 불빛. 불빛 그림 R = 세기, G = 꺼지는 차례 → G < _Lights 인 불빛만 켬
// (EarthView가 장 진행에 맞춰 _Lights를 1 → 0.48로 내림, 도시마다 같은 차례라 도시 단위로 하나씩 꺼짐).
// 구름은 경도 방향으로 흐르고(_CloudShift) 아래 불빛을 조금 가림, 가장자리는 대기 빛(프레넬). 모델 · 그림 하나로 그리는 배경이라 Shader Graph 대신 코드 (SkyLens와 같은 틀).
Shader "SpaceStation/Earth"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Surface (sRGB)", 2D) = "gray" {}
        [NoScaleOffset] _LightsMap ("City Lights (R = intensity, G = off order, linear)", 2D) = "black" {}
        [NoScaleOffset] _CloudMap ("Clouds (R, linear)", 2D) = "black" {}
        _Lights ("Lights Left (0-1)", Range(0, 1)) = 1
        [HDR] _LightsColor ("Lights Color", Color) = (2.4, 1.5, 0.7, 1)
        _CloudShift ("Cloud Shift (u)", Float) = 0
        _CloudOpacity ("Cloud Opacity", Range(0, 1)) = 0.72
        _SunBoost ("Day Brightness", Range(0, 4)) = 1.6
        _NightAmbient ("Night Ambient", Range(0, 0.2)) = 0.025
        [HDR] _AtmoColor ("Atmosphere Color", Color) = (0.35, 0.62, 1.4, 1)
        _AtmoPower ("Atmosphere Power", Range(0.5, 8)) = 3.2
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Earth"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LightsColor;
                half4 _AtmoColor;
                half _Lights;
                float _CloudShift;
                half _CloudOpacity;
                half _SunBoost;
                half _NightAmbient;
                half _AtmoPower;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_LightsMap); SAMPLER(sampler_LightsMap);
            TEXTURE2D(_CloudMap); SAMPLER(sampler_CloudMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
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
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light sun = GetMainLight();
                half ndl = dot(n, sun.direction);
                half lit = saturate(ndl);
                half night = 1.0h - smoothstep(-0.22h, 0.06h, ndl); // 해가 진 쪽 (해 질 녘 띠는 부드럽게)

                half3 surface = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb;
                half cloud = SAMPLE_TEXTURE2D(_CloudMap, sampler_CloudMap, float2(i.uv.x + _CloudShift, i.uv.y)).r * _CloudOpacity;
                half3 dayLight = sun.color * lit * _SunBoost;
                half3 color = surface * (dayLight + _NightAmbient);
                color = lerp(color, dayLight * 0.95h + _NightAmbient * 0.6h, cloud); // 구름 = 햇빛을 받는 흰 덮개

                half2 city = SAMPLE_TEXTURE2D(_LightsMap, sampler_LightsMap, i.uv).rg;
                half on = saturate((_Lights - city.g) * 30.0h + 0.5h); // 차례가 지난 불빛은 꺼짐
                color += _LightsColor.rgb * city.r * on * night * (1.0h - cloud * 0.85h);

                half rim = pow(1.0h - saturate(dot(n, v)), _AtmoPower);
                color += _AtmoColor.rgb * rim * saturate(ndl + 0.35h); // 해 쪽 가장자리의 푸른 대기
                return half4(color, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
