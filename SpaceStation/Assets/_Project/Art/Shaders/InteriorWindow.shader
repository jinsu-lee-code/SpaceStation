// Phase 11-8 바깥 창: 유리 대신 바깥 카메라(InteriorExteriorView)가 그린 실제 정거장·우주 화면을 화면 좌표 그대로 보여준다.
// 창 너머의 내부 공간(이웃 방의 바깥벽 등)은 이 면이 깊이를 써서 가린다. 유리 느낌은 옅은 색 + 가장자리 반사광.
// _InteriorExteriorTex(전역)가 없으면(바깥 카메라가 꺼짐) 어두운 우주색. SRP Batcher 호환.
Shader "SpaceStation/InteriorWindow"
{
    Properties
    {
        _Tint ("Tint (투과색)", Color) = (0.92, 0.97, 1, 1)
        [HDR] _RimColor ("Rim (비스듬히 볼 때 반사)", Color) = (0.35, 0.45, 0.55, 1)
        _RimPower ("Rim Power", Range(1, 8)) = 4
        _Streak ("Streak (사선 반사 줄)", Range(0, 0.2)) = 0.03
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _Tint;
        half4 _RimColor;
        half _RimPower;
        half _Streak;
    CBUFFER_END

    TEXTURE2D(_InteriorExteriorTex); SAMPLER(sampler_InteriorExteriorTex);
    float _InteriorExteriorOn;

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
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings o = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
        o.positionCS = pos.positionCS;
        o.positionWS = pos.positionWS;
        o.normalWS = TransformObjectToWorldNormal(input.normalOS);
        return o;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 outside = _InteriorExteriorOn > 0.5
                    ? SAMPLE_TEXTURE2D(_InteriorExteriorTex, sampler_InteriorExteriorTex, uv).rgb
                    : half3(0.01, 0.012, 0.02);

                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half rim = pow(1.0h - saturate(abs(dot(n, v))), _RimPower);
                // 창마다 같은 방향 사선 반사 줄 (월드 위치 기준, 걸으면 미끄러짐)
                half streak = smoothstep(0.35h, 0.5h, frac(dot(i.positionWS, float3(0.23, 0.31, 0.17)))) * smoothstep(0.65h, 0.5h, frac(dot(i.positionWS, float3(0.23, 0.31, 0.17))));

                half3 color = outside * _Tint.rgb + _RimColor.rgb * rim + streak * _Streak;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            half FragDepth(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 FragNormals(Varyings i) : SV_Target
            {
                float3 normalWS = normalize(i.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(normalWS);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
