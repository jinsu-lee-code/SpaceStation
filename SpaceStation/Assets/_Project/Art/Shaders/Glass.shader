// 모듈 유리 (2026-10-27 사용자 "유리에 멍울처럼 생기는 부분" · "유리가 있다는 정도만 인식되면"): 양면으로 그리는 반투명 유리.
// URP Lit(양면)은 뒷면에서 법선을 뒤집지 않아 유리 껍질 안쪽 뒷면이 시선과 반대 법선으로 그려지고, 프레넬이 최대(스치는 각)로 계산돼
// 하늘 반사가 뿌연 흰 뭉치로 겹쳐 보였다 → 뒷면은 법선을 뒤집어 앞면처럼 비춘다 (조명 계산 = URP PBR, 상태 표현 MPB _BaseColor · _EmissionColor 유지, SRP Batcher 호환).
// 한쪽 면만 있는 얇은 유리(보관 단지 · 연구 기관 등)도 양쪽에서 보이도록 앞면만 그리기(Cull Back) 대신 이 방식.
// 반사 보존(미리 곱한 알파): 유리 바탕색(확산광)만 알파만큼 옅게 하고, 해 반짝임 · 하늘 반사는 그대로 더함 → 거의 투명한데 빛이 닿는 곳에서 유리가 있다는 게 보임.
Shader "SpaceStation/Glass"
{
    Properties
    {
        _BaseColor ("Base Color (알파 = 바탕색 진하기)", Color) = (0.82, 0.86, 1, 0.05)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.92
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Reflection ("하늘 반사 · 주변광 세기 (해 반짝임은 그대로)", Range(0, 1)) = 1
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness;
                half _Metallic;
                half _Reflection;
                half4 _EmissionColor;
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
                half fogFactor : TEXCOORD2;
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
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                n = front ? n : -n; // 뒷면 = 안쪽에서 본 유리

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb * _BaseColor.a; // 미리 곱한 알파: 바탕색만 옅게 (반사 · 반짝임은 금속도 0이라 바탕색과 무관)
                surface.alpha = 1.0h;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.emission = _EmissionColor.rgb;
                surface.occlusion = _Reflection; // 간접광(하늘 반사 · 주변광)만 줄임

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = _BaseColor.a; // 뒤가 비치는 정도 (Blend One OneMinusSrcAlpha)
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
