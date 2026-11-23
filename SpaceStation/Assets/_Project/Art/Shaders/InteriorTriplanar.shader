// Phase 11 내부 표면 디테일: 월드 위치로 투영(트라이플래너)하는 금속 패널. UV 없이 늘린 키트·템플릿 어디에나 같은 크기로 붙는다.
// _DetailNormal = 패널 이음선·볼트·굴곡 (InteriorTextureBaker가 만든 타일), _DetailMask R = 틈 어둡게(AO), G = 얼룩. _WetColor.a > 0이면 _WetLevel 아래를 젖은 색 · 윤기로 (모래 해변).
// _DETAIL_ALBEDO (재질 토글) = _DetailAlbedo 색 텍스처를 같은 트라이플래너로 _BaseColor에 곱함 (AI 중추 생울타리 잎 색, 2026-11-16) — 끄면 예전과 같음.
// _VERTEX_AO (재질 토글) = 꼭짓점 색 RGB를 색 · 차폐에 곱함 (AI 중추 생울타리 잎 덩이 골 그늘 — Blender HedgeAO, 굴곡과 같은 자리) — 끄면 예전과 같음.
// 조명은 URP PBR (Forward+ 추가 조명·SSAO 포함). 상태 표현은 기존처럼 MPB로 _BaseColor·_EmissionColor를 바꾼다. SRP Batcher 호환.
Shader "SpaceStation/InteriorTriplanar"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.72, 0.72, 0.75, 1)
        [NoScaleOffset][Normal] _DetailNormal ("Detail Normal (타일)", 2D) = "bump" {}
        [NoScaleOffset] _DetailMask ("Detail Mask (R 틈 AO, G 얼룩)", 2D) = "white" {}
        [Toggle(_DETAIL_ALBEDO)] _UseDetailAlbedo ("Use Detail Albedo", Float) = 0
        [NoScaleOffset] _DetailAlbedo ("Detail Albedo (sRGB 색, _DETAIL_ALBEDO)", 2D) = "white" {}
        [Toggle(_VERTEX_AO)] _UseVertexAO ("Use Vertex AO (꼭짓점 색 그늘)", Float) = 0
        _Tiling ("Tiling (1m당 타일 수)", Float) = 0.5
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 0.8
        _GrimeStrength ("Grime Strength", Range(0, 1)) = 0.25
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0.5
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _WetColor ("Wet Color (a = 켬)", Color) = (0, 0, 0, 0)
        _WetLevel ("Wet Level (물체 공간 높이)", Float) = 0
        _WetBand ("Wet Band (m)", Float) = 0.1
        _WetSmoothness ("Wet Smoothness", Range(0, 1)) = 0.5
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        float _Tiling;
        half _NormalStrength;
        half _OcclusionStrength;
        half _GrimeStrength;
        half _Smoothness;
        half _Metallic;
        half4 _EmissionColor;
        half4 _WetColor;
        float _WetLevel;
        float _WetBand;
        half _WetSmoothness;
    CBUFFER_END

    TEXTURE2D(_DetailNormal); SAMPLER(sampler_DetailNormal);
    TEXTURE2D(_DetailMask); SAMPLER(sampler_DetailMask);
    TEXTURE2D(_DetailAlbedo); SAMPLER(sampler_DetailAlbedo);

    // 축별 투영 가중치 (경계가 부드럽게 섞이도록 4제곱)
    float3 TriWeights(float3 n)
    {
        float3 w = pow(abs(n), 4.0);
        return w / max(dot(w, 1.0), 1e-4);
    }

    // 화이트아웃 블렌드 트라이플래너 노멀 (축 부호 반영)
    float3 TriNormal(float3 p, float3 n, float3 w)
    {
        float3 s = n < 0 ? -1.0 : 1.0;
        float2 uvX = p.zy * _Tiling; uvX.x *= s.x;
        float2 uvY = p.xz * _Tiling; uvY.x *= s.y;
        float2 uvZ = p.xy * _Tiling; uvZ.x *= -s.z;
        half3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uvX), _NormalStrength);
        half3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uvY), _NormalStrength);
        half3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, uvZ), _NormalStrength);
        tx.x *= s.x; ty.x *= s.y; tz.x *= -s.z;
        tx = half3(tx.xy + n.zy, abs(tx.z) * n.x);
        ty = half3(ty.xy + n.xz, abs(ty.z) * n.y);
        tz = half3(tz.xy + n.xy, abs(tz.z) * n.z);
        return normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
    }

    half4 TriMask(float3 p, float3 n, float3 w)
    {
        float3 s = n < 0 ? -1.0 : 1.0;
        float2 uvX = p.zy * _Tiling; uvX.x *= s.x;
        float2 uvY = p.xz * _Tiling; uvY.x *= s.y;
        float2 uvZ = p.xy * _Tiling; uvZ.x *= -s.z;
        return SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uvX) * w.x
             + SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uvY) * w.y
             + SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uvZ) * w.z;
    }

    // 색 텍스처 (sRGB → 샘플러가 선형으로 풀어 줌) — 노멀 · 마스크와 같은 투영이라 잎 색 · 잎 모양이 어긋나지 않음
    half3 TriAlbedo(float3 p, float3 n, float3 w)
    {
        float3 s = n < 0 ? -1.0 : 1.0;
        float2 uvX = p.zy * _Tiling; uvX.x *= s.x;
        float2 uvY = p.xz * _Tiling; uvY.x *= s.y;
        float2 uvZ = p.xy * _Tiling; uvZ.x *= -s.z;
        return SAMPLE_TEXTURE2D(_DetailAlbedo, sampler_DetailAlbedo, uvX).rgb * w.x
             + SAMPLE_TEXTURE2D(_DetailAlbedo, sampler_DetailAlbedo, uvY).rgb * w.y
             + SAMPLE_TEXTURE2D(_DetailAlbedo, sampler_DetailAlbedo, uvZ).rgb * w.z;
    }

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        half4 color : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        half fogFactor : TEXCOORD2;
        half4 color : TEXCOORD3;
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
        o.color = input.color;
        return o;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _DETAIL_ALBEDO
            #pragma shader_feature_local_fragment _VERTEX_AO
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float3 w = TriWeights(n);
                float3 normalWS = TriNormal(i.positionWS, n, w);
                half4 mask = TriMask(i.positionWS, n, w);

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                half3 baseRgb = _BaseColor.rgb;
                #if defined(_DETAIL_ALBEDO)
                    baseRgb *= TriAlbedo(i.positionWS, n, w);
                #endif
                #if defined(_VERTEX_AO)
                    baseRgb *= i.color.rgb;
                #endif
                surface.albedo = baseRgb * lerp(1.0h, mask.g, _GrimeStrength);
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness * lerp(1.0h, mask.g, _GrimeStrength * 0.6h);
                // 젖은 띠 (궤도 항만 모래 해변, 2026-11-16): 물체 공간 높이가 _WetLevel 아래로 갈수록 젖은 색 · 윤기 — 땅 격자 면마다 재질을 나누면 물가선이 톱니였음
                half wet = _WetColor.a * (1.0h - smoothstep(_WetLevel - _WetBand, _WetLevel + _WetBand, TransformWorldToObject(i.positionWS).y));
                surface.albedo = lerp(surface.albedo, _WetColor.rgb * lerp(1.0h, mask.g, _GrimeStrength), wet);
                surface.smoothness = lerp(surface.smoothness, _WetSmoothness, wet);
                surface.normalTS = half3(0, 0, 1);
                surface.emission = _EmissionColor.rgb;
                surface.occlusion = lerp(1.0h, mask.r, _OcclusionStrength);
                #if defined(_VERTEX_AO)
                    surface.occlusion *= i.color.r;
                #endif
                surface.alpha = 1.0h;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing
            half FragDepth(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        // SSAO(Source = Normals)가 읽는 노멀 (디테일 노멀까지 반영해 틈이 더 살아나게)
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 FragNormals(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 normalWS = TriNormal(i.positionWS, n, TriWeights(n));
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
