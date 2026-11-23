// ⑤ 트림 (2026-11-16, 사용자 시안 2 고름): UV 없는 내부 단색 면에 실제 크기 (m)의 모서리 빛 · 그늘 선 · 이음선 · 홈을 그림 — 텍스처 없음.
// 트림 UV = Blender 내보내기 (BlenderWork/Tools/trim_uv_export.py)가 넣음: 그 점에서 면 묶음 테두리 선 넷까지 거리 (m) + 1 · 면 폭
//   가져올 때 InteriorTrimImporter가 16비트로 묶음: UV1 = (선0, 선1) · UV2 = (선2, 선3, 면 폭, 0), 선이 없으면 11.
//   묶음 = 같은 재질 · 46° 안 굽은 면이라 원기둥 · 굽은 띠는 한 덩이, 테두리 = 재질이 바뀌거나 각진 곳.
//   UV1 · UV2가 0이면 (트림 UV 없는 메시 — 아직 다시 내보내지 않은 방 · 키트 · 다른 곳에서 같은 재질을 쓰는 메시) 장식 없이 예전 모습 그대로.
//   면 폭 (UV2.z)이 0이면 UV1이 있어도 트림 아님 (Unity 기본 메시의 라이트맵 UV1).
// _TrimKind: 0 금 띠 (모서리 빛 + 가로 결) · 1 금 상감 (테두리 안쪽 두 번째 선) · 2 진주 (보는 각에 따라 옅은 무지개 빛) · 3 흰 몸체 판 (1.2 m 이음선)
//            4 금속 테 (테두리 따라 홈 둘) · 5 벽 판 (안쪽 테 어둡게) · 6 짙은 광택 (모서리 빛만, 세게)
//            7 성운 몸체 (4단계, 2026-11-17 "옅은 별가루 무늬"): 벽 판 테 + 드문 작은 별빛 점 (물체 좌표 — 트림 UV 없는 메시도, 구멍 · 어두운 점 아님)
//   면 폭 (마주 보는 테두리 사이, m) — 가는 면 (손잡이 · 가는 테)은 모서리 빛 폭 ≤ 면 폭의 30 % (면 전체가 빛나지 않게).
// 멀리서는 가는 선을 흐리게 (화면 픽셀보다 가늘면 사라짐 — 깜빡임 방지). 조명 = URP PBR (InteriorTriplanar와 같음). 상태 표현 MPB (_BaseColor · _EmissionColor) 그대로. SRP Batcher 호환.
Shader "SpaceStation/InteriorTrim"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.9, 0.9, 0.9, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.5
        _Metallic ("Metallic", Range(0, 1)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        _TrimKind ("Trim Kind", Float) = 0
        _Bevel ("Bevel (m)", Float) = 0.016
        _Highlight ("Bevel Highlight", Range(0, 1)) = 0.35
        _Shadow ("Shadow Line", Range(0, 1)) = 0.4
        _Seam ("Panel Seam (m)", Float) = 1.2
        _Iridescence ("Iridescence", Range(0, 1)) = 0.25
        [HDR] _StarDust ("Star Dust (kind 7)", Color) = (0.75, 0.72, 1.0, 1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half _Smoothness;
        half _Metallic;
        half4 _EmissionColor;
        float _TrimKind;
        float _Bevel;
        half _Highlight;
        half _Shadow;
        float _Seam;
        half _Iridescence;
        half4 _StarDust;
    CBUFFER_END

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS : NORMAL;
        float2 trimA : TEXCOORD1;
        float4 trimB : TEXCOORD2;          // 선 2 · 3 · 면 폭 · 0 (InteriorTrimImporter가 16비트로 묶음)
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS : TEXCOORD1;
        float4 trim : TEXCOORD2;
        half fogFactor : TEXCOORD3;
        float width : TEXCOORD4;
        float3 positionOS : TEXCOORD5;                 // 별가루 (kind 7) — 도는 접시에서도 점이 면에 붙어 있게
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
        o.trim = float4(input.trimA, input.trimB.xy);
        o.width = input.trimB.z;                       // 0 = 트림 값 없음 (UV1만 있는 메시 — Unity 기본 원기둥의 라이트맵 UV를 트림으로 읽어 막대 전체가 빛났음)
        o.positionOS = input.positionOS.xyz;
        o.fogFactor = ComputeFogFactor(pos.positionCS.z);
        return o;
    }

    // x에서 폭 w인 선 (aa = 화면 한 픽셀의 m) — 선이 픽셀보다 가늘면 흐리게
    half Line(float x, float at, float w, float aa)
    {
        half l = 1.0h - smoothstep(w * 0.5, w * 0.5 + aa, abs(x - at));
        return l * saturate(w / max(aa * 1.5, 1e-5));
    }

    float Hash11(float p)
    {
        p = frac(p * 0.1031);
        p *= p + 33.33;
        p *= p + p;
        return frac(p);
    }

    float Hash31(float3 p)
    {
        p = frac(p * 0.1031);
        p += dot(p, p.zyx + 31.32);
        return frac((p.x + p.y) * p.z);
    }

    // 성운 별가루: 물체 좌표 5 cm 칸마다 6 %에 작은 별빛 점 하나 (지름 7 mm, 저마다 천천히 반짝임) — 점이 화면 2 픽셀보다 작아지면 사라짐 (멀리서 지글거리지 않게)
    half StarDust(float3 positionOS, float pixel)
    {
        float3 sp = positionOS / 0.05;
        float3 cell = floor(sp);
        float h = Hash31(cell);
        if (h > 0.06)
            return 0.0h;
        float3 at = cell + 0.15 + 0.7 * float3(Hash31(cell + 3.1), Hash31(cell + 7.7), Hash31(cell + 13.3));
        float d = length(sp - at) * 0.05;
        half speck = 1.0h - smoothstep(0.0015, 0.0035, d);
        half twinkle = 0.55h + 0.45h * sin(_Time.y * (0.8 + h * 25.0) + h * 300.0);
        return speck * twinkle * saturate(0.0035 / max(pixel, 1e-5) - 0.5);
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half3 albedo = _BaseColor.rgb;
                half smooth = _Smoothness;

                bool hasTrim = i.width > 0.0 && max(max(i.trim.x, i.trim.y), max(i.trim.z, i.trim.w)) > 0.5;
                if (hasTrim)
                {
                    float4 d = i.trim - 1.0;                                   // 선 넷까지 거리 (m)
                    float e = min(min(d.x, d.y), min(d.z, d.w));               // 테두리까지
                    float width = i.width;                                     // 면 폭 (마주 보는 테두리 사이 — Blender가 잼)
                    float bevelW = min(_Bevel, 0.3 * width);
                    float aa = max(fwidth(e), 1e-5);
                    int kind = (int)round(_TrimKind);

                    // 모서리 빛 (테두리 bevelW 안) + 바로 안쪽 그늘 선
                    half bevel = (1.0h - smoothstep(bevelW - aa, bevelW + aa, e)) * saturate(bevelW / max(aa * 1.5, 1e-5));
                    half shadow = Line(e, bevelW + 0.003, 0.004, aa) * saturate(width / 0.05);
                    half hi = _Highlight * (kind == 6 ? 1.6h : 1.0h);
                    albedo *= (1.0h + hi * bevel) * (1.0h - _Shadow * shadow);
                    smooth = saturate(smooth + 0.12h * bevel);

                    if (kind == 0 || kind == 1)
                    {
                        // 가로 결 (월드 높이 1.5 mm마다 아주 옅게)
                        float g = Hash11(floor(i.positionWS.y * 650.0) + floor(dot(i.positionWS.xz, float2(0.7, 0.3)) * 3.0) * 17.0);
                        albedo *= 1.0h + 0.035h * (g - 0.5h) * saturate(0.0015 / max(fwidth(i.positionWS.y), 1e-5));
                        if (kind == 1 && width > 0.09)
                            albedo *= (1.0h - 0.35h * Line(e, 0.032, 0.005, aa)) * (1.0h + 0.25h * Line(e, 0.038, 0.004, aa));
                    }
                    else if (kind == 3)
                    {
                        // 흰 몸체 판 이음선: 세운 면만, 높이 _Seam마다 가로 · 면 따라 2 × _Seam마다 세로 (테두리 5 cm 안은 비움)
                        if (abs(n.y) < 0.7)
                        {
                            float3 t = normalize(cross(float3(0, 1, 0), n));
                            float sy = frac(i.positionWS.y / _Seam), sx = frac(dot(i.positionWS, t) / (_Seam * 2.0));
                            float aay = max(fwidth(i.positionWS.y), 1e-5), aax = max(fwidth(dot(i.positionWS, t)), 1e-5);
                            half seam = max(Line(sy * _Seam, 0.0, 0.006, aay) + Line(sy * _Seam, _Seam, 0.006, aay),
                                            Line(sx * _Seam * 2.0, 0.0, 0.006, aax) + Line(sx * _Seam * 2.0, _Seam * 2.0, 0.006, aax));
                            seam *= smoothstep(0.04, 0.06, e);
                            albedo *= 1.0h - 0.3h * seam;
                        }
                    }
                    else if (kind == 4)
                    {
                        if (width > 0.11)
                            albedo *= (1.0h - 0.4h * Line(e, 0.03, 0.005, aa)) * (1.0h - 0.4h * Line(e, 0.05, 0.005, aa));
                    }
                    else if (kind == 5 || kind == 7)
                    {
                        if (width > 0.2)
                        {
                            half inset = smoothstep(0.03 - aa, 0.03 + aa, e) * (1.0h - smoothstep(0.07 - aa, 0.07 + aa, e));
                            albedo *= (1.0h - 0.25h * inset) * (1.0h + 0.35h * Line(e, 0.072, 0.004, aa));
                        }
                    }
                }
                if ((int)round(_TrimKind) == 2)
                {
                    // 진주 빛: 비스듬히 볼수록 옅은 무지개 (분홍 · 하늘 · 연두가 돌아감) — 트림 UV 없는 메시도 같은 진주
                    half f = pow(1.0h - saturate(dot(n, v)), 2.0h);
                    half3 tint = 0.5h + 0.5h * cos(6.2831h * (f * 0.9h + half3(0.0h, 0.33h, 0.67h)));
                    albedo = lerp(albedo, albedo * (0.75h + 0.5h * tint), _Iridescence * (0.35h + 0.65h * f));
                }
                half3 dust = 0.0h;
                if ((int)round(_TrimKind) == 7)
                    dust = _StarDust.rgb * StarDust(i.positionOS, length(fwidth(i.positionWS)));

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = v;
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = saturate(albedo);
                surface.metallic = _Metallic;
                surface.smoothness = smooth;
                surface.normalTS = half3(0, 0, 1);
                surface.emission = _EmissionColor.rgb + dust;
                surface.occlusion = 1.0h;
                surface.alpha = 1.0h;
                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // 그림자 (내부 조각은 그림자를 끔 — 켜 둔 메시를 위해 URP 기본 그림자 바이어스)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment FragShadow
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct ShadowAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 ShadowVert(ShadowAttributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }
            half FragShadow() : SV_Target { return 0; }
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
