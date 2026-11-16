// 식물 아틀라스 공용 셰이더 (2026-11-16): 식물 아틀라스 한 벌 (`PlantAtlas` — 샘플 잎 · 꽃 · 벚꽃 · 줄기 트림 띠 · 우리 색 칸)을 쓰는 모든 식물이 이 재질 하나를 씀.
// 알파 잘라내기 · 양면 (뒷면은 법선을 뒤집음) · 빛 비침 (잎 뒤에서 오는 해 · 점광원 빛이 두께 (마스크 B)만큼 비침 — 밤 내부는 등불이 거의 다라 점광원도 셈)
// · 꼭짓점 색 그늘 (샘플 메시 꼭짓점 색 R) · 시듦 (MPB `_Wilt` 0 → 1 = 누렇게 · 채도 낮춤 — 식물 자리 물 주기) · 살랑임 (원점 위 높이만큼, 자리마다 다른 박자).
// 마스크: R = 윤기, G = 차폐, B = 두께. 톤 (밝게 · 연두 · 명암 대비 줄임)은 아틀라스를 구울 때 이미 들어 있음. SRP Batcher 호환.
Shader "SpaceStation/Foliage"
{
    Properties
    {
        [MainTexture] _BaseMap ("Atlas (sRGB, 알파 = 잘라내기)", 2D) = "white" {}
        [NoScaleOffset][Normal] _NormalMap ("Atlas Normal", 2D) = "bump" {}
        [NoScaleOffset] _MaskMap ("Atlas Mask (R 윤기 · G 차폐 · B 두께)", 2D) = "white" {}
        [MainColor] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.45
        _NormalStrength ("Normal Strength", Range(0, 2)) = 1
        _Smoothness ("Smoothness (마스크 R에 곱함)", Range(0, 1)) = 0.35
        _OcclusionStrength ("Occlusion Strength", Range(0, 1)) = 0.8
        _VertexAO ("Vertex Color AO", Range(0, 1)) = 0.6
        _Translucency ("Translucency", Range(0, 2)) = 0.6
        _TranslucencyColor ("Translucency Color", Color) = (0.85, 1, 0.55, 1)
        _Wilt ("Wilt (MPB)", Range(0, 1)) = 0
        _WiltColor ("Wilt Color", Color) = (0.72, 0.6, 0.32, 1)
        _WindStrength ("Wind Strength (m)", Range(0, 0.1)) = 0.012
        _WindSpeed ("Wind Speed", Range(0, 4)) = 1.1
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _Cutoff;
        half _NormalStrength;
        half _Smoothness;
        half _OcclusionStrength;
        half _VertexAO;
        half _Translucency;
        half4 _TranslucencyColor;
        half _Wilt;
        half4 _WiltColor;
        float _WindStrength;
        float _WindSpeed;
    CBUFFER_END

    TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
    TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
    TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float4 tangentOS : TANGENT;
        float2 uv : TEXCOORD0;
        half4 color : COLOR;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        float3 positionWS : TEXCOORD1;
        float3 normalWS : TEXCOORD2;
        float4 tangentWS : TEXCOORD3;
        half4 color : TEXCOORD4;
        half fogFactor : TEXCOORD5;
        UNITY_VERTEX_INPUT_INSTANCE_ID
        UNITY_VERTEX_OUTPUT_STEREO
    };

    // 살랑임: 물체 원점 위 높이만큼 (밑동은 그대로), 자리 (월드 xz)마다 다른 박자로 옆으로 조금
    float3 Sway(float3 positionOS, float3 positionWS)
    {
        float k = saturate(positionOS.y / 1.2);
        float t = _Time.y * _WindSpeed + positionWS.x * 0.7 + positionWS.z * 0.9;
        float2 d = float2(sin(t), sin(t * 0.83 + 1.7)) * _WindStrength * k * k;
        return positionWS + float3(d.x, 0, d.y);
    }

    Varyings Vert(Attributes input)
    {
        Varyings o = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_TRANSFER_INSTANCE_ID(input, o);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        float3 ws = TransformObjectToWorld(input.positionOS.xyz);
        ws = Sway(input.positionOS.xyz, ws);
        o.positionWS = ws;
        o.positionCS = TransformWorldToHClip(ws);
        VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
        o.normalWS = n.normalWS;
        o.tangentWS = float4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
        o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
        o.color = input.color;
        o.fogFactor = ComputeFogFactor(o.positionCS.z);
        return o;
    }

    half4 SampleAlbedo(float2 uv)
    {
        half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
        // 시듦: 채도를 낮추고 누런 색으로 (식물 자리 물 주기 요청 — 처짐은 모델이 함)
        half l = dot(c.rgb, half3(0.299, 0.587, 0.114));
        c.rgb = lerp(c.rgb, l * _WiltColor.rgb * 1.6, _Wilt);
        return c;
    }

    // 양면: 뒷면이면 법선을 뒤집어 앞면처럼 빛을 받게
    float3 FoliageNormal(Varyings i, bool front)
    {
        float s = front ? 1.0 : -1.0;
        float3 n = normalize(i.normalWS) * s;
        float3 t = normalize(i.tangentWS.xyz);
        float3 b = cross(normalize(i.normalWS), t) * i.tangentWS.w * s;
        half3 nt = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, i.uv), _NormalStrength);
        return normalize(t * nt.x + b * nt.y + n * nt.z);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 빛 비침: 잎 뒤에서 오는 빛 (빛 방향이 법선 반대쪽일 때) × 두께
            half3 Transmit(Light light, float3 n, half thick)
            {
                half back = saturate(dot(-n, light.direction));
                return light.color * (light.distanceAttenuation * light.shadowAttenuation) * back * thick;
            }

            half4 Frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 albedo = SampleAlbedo(i.uv);
                clip(albedo.a - _Cutoff);
                half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv);
                float3 normalWS = FoliageNormal(i, front);

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

                half vao = lerp(1.0h, i.color.r, _VertexAO);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo.rgb;
                surface.metallic = 0;
                surface.smoothness = mask.r * _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = lerp(1.0h, mask.g, _OcclusionStrength) * vao;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);

                half thick = mask.b * _Translucency;
                half3 tr = Transmit(GetMainLight(inputData.shadowCoord), normalWS, thick);
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                UNITY_LOOP for (uint li = 0; li < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); li++)
                {
                    tr += Transmit(GetAdditionalLight(li, i.positionWS, inputData.shadowMask), normalWS, thick);
                }
                #endif
                LIGHT_LOOP_BEGIN(count)
                    tr += Transmit(GetAdditionalLight(lightIndex, i.positionWS, inputData.shadowMask), normalWS, thick);
                LIGHT_LOOP_END
                #endif
                color.rgb += tr * albedo.rgb * _TranslucencyColor.rgb * vao;
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 ws = Sway(input.positionOS.xyz, TransformObjectToWorld(input.positionOS.xyz));
                float3 nws = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - ws);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target
            {
                clip(SampleAlbedo(i.uv).a - _Cutoff);
                return 0;
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
            half FragDepth(Varyings i) : SV_Target
            {
                clip(SampleAlbedo(i.uv).a - _Cutoff);
                return i.positionCS.z;
            }
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
            half4 FragNormals(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                clip(SampleAlbedo(i.uv).a - _Cutoff);
                float3 n = FoliageNormal(i, front);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(n), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
