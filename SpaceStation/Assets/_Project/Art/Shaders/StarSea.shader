// 실드 돔 내부 별빛 유리 바다 (사용자 2026-11-05 시안 1): 바깥 모델의 별 바다 (등대를 도는 흰 물결 줄 · 드문 별)를 걸어 다니는 바닥으로.
// 등대에서 퍼져 나가는 구불구불한 물결 호 (끊어진 줄, 천천히 바깥으로) · 드문 별 (깊은 겹은 시점에 따라 어긋나 유리 밑 깊이가 느껴짐, 벌집처럼 빽빽하지 않게) · 비친 오로라 얼룩 (끝이 흐리고 떠돎) · 비스듬히 볼수록 하늘 되비침.
// 물체 공간 (템플릿 좌표) xz를 씀. 조절은 재질 슬라이더로 (M_InteriorEvo2StarSea).
Shader "SpaceStation/StarSea"
{
    Properties
    {
        [HDR] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _DeepColor ("깊은 바다색", Color) = (0.035, 0.075, 0.2, 1)
        [HDR] _RingColor ("물결 빛", Color) = (0.75, 0.9, 1.25, 1)
        [HDR] _StarColor ("별", Color) = (1.3, 1.3, 1.5, 1)
        [HDR] _AuroraLow ("비친 오로라 (초록)", Color) = (0.1, 0.62, 0.4, 1)
        [HDR] _AuroraHigh ("비친 오로라 (보라)", Color) = (0.32, 0.14, 0.62, 1)
        _Center ("등대 자리 (x, z)", Vector) = (3.2, 4.8, 0, 0)
        _RingSpacing ("물결 간격 (m)", Range(0.5, 5)) = 1.9
        _RingWidth ("물결 심 굵기 (m)", Range(0.005, 0.2)) = 0.03
        _RingGlow ("물결 번짐 (m)", Range(0.02, 1)) = 0.22
        _RingAmount ("물결 밝기", Range(0, 2)) = 0.9
        _RingSpeed ("물결 퍼지는 속도 (간격 / 초)", Range(0, 0.5)) = 0.05
        _StarAmount ("별 밝기", Range(0, 3)) = 0.8
        _AuroraAmount ("비친 오로라 밝기", Range(0, 2)) = 0.35
        _Reflect ("하늘 되비침", Range(0, 2)) = 0.6
        _Depth ("별 깊이 (m)", Range(0, 2)) = 0.6
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            Name "StarSea"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "StationNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _RingColor;
                half4 _StarColor;
                half4 _AuroraLow;
                half4 _AuroraHigh;
                float4 _Center;
                float _RingSpacing;
                float _RingWidth;
                float _RingGlow;
                float _RingAmount;
                float _RingSpeed;
                float _StarAmount;
                float _AuroraAmount;
                float _Reflect;
                float _Depth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalOS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.positionOS = input.positionOS.xyz;
                o.normalOS = input.normalOS;
                return o;
            }

            // 별: 격자 칸마다 확률로 별 하나 (MilkyRiver와 같은 방식). 칸이 화면에서 너무 작으면 사라짐 (멀리서 지글거리지 않게)
            float Stars(float2 p, float cell, float density, float size, float seed, float pixel)
            {
                float2 g = p / cell;
                float2 id = floor(g);
                float h = StationHash3(float3(id, seed));
                if (h > density)
                    return 0.0;
                float2 at = float2(StationHash3(float3(id, seed + 3.0)), StationHash3(float3(id, seed + 5.0))) * 0.6 + 0.2;
                float d = length(frac(g) - at);
                float twinkle = 0.6 + 0.4 * sin(_Time.y * (0.6 + h * 8.0) + h * 80.0);
                float radius = max(size, pixel / cell);   // 한 픽셀보다 작아지면 넓히고 그만큼 어둡게 (멀리서 지글거리지 않게)
                return smoothstep(radius, 0.0, d) * (size / radius) * (size / radius) * twinkle * smoothstep(1.5, 4.0, cell / pixel);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y;
                if (i.normalOS.y < 0.5)
                    return half4(_DeepColor.rgb * _BaseColor.rgb, 1);
                float2 p = i.positionOS.xz;
                float pixel = max(fwidth(p.x) + fwidth(p.y), 0.00001);
                float3 viewWS = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 viewOS = normalize(TransformWorldToObjectDir(viewWS));
                float2 parallax = viewOS.xz / max(viewOS.y, 0.25);
                float2 toCenter = p - _Center.xy;
                float r = length(toCenter);
                float2 dir = toCenter / max(r, 0.001);
                float3 color = _DeepColor.rgb;
                // 물결: 방향마다 다르게 일그러진 동심원 (이음매 없게 각도 대신 방향 벡터로 잡음), 바퀴마다 다른 곳이 끊겨 호가 됨
                float wobble = (StationNoise3(float3(dir * 1.6, r * 0.12)) - 0.5) * 2.2 + (StationNoise3(float3(dir * 3.7, r * 0.3 + 4.1)) - 0.5) * 0.7;
                float phase = (r + wobble) / _RingSpacing - t * _RingSpeed;
                float id = floor(phase);
                float dist = abs(frac(phase) - 0.5) * _RingSpacing;
                float arc = smoothstep(0.3, 0.5, StationNoise3(float3(dir * 1.2, id * 3.7 + 1.3)));
                float core = smoothstep(max(_RingWidth, fwidth(phase) * _RingSpacing * 1.2), 0.0, dist);   // 물결을 가로지르는 방향의 픽셀 크기 (시선과 나란한 물결이 멀리서 굵어지지 않게)
                float glow = exp(-dist * dist / (_RingGlow * _RingGlow));
                color += _RingColor.rgb * arc * (core * 0.9 + glow * 0.22) * _RingAmount * (0.55 + 0.45 * exp(-r * 0.1));
                // 별: 깊은 겹 둘 + 표면의 드문 밝은 별
                float stars = Stars(p - parallax * _Depth, 1.4, 0.22, 0.03, 7.0, pixel) * 0.5;
                stars += Stars(p - parallax * _Depth * 0.4, 2.3, 0.28, 0.028, 17.0, pixel) * 0.8;
                stars += Stars(p, 3.7, 0.35, 0.018, 29.0, pixel) * 1.2;
                color += _StarColor.rgb * stars * _StarAmount;
                // 비친 오로라: 끝이 흐린 얼룩이 천천히 떠돎, 등대 밑동에서 멀수록 · 비스듬히 볼수록 진함
                float cloud = StationFbm3(float3(p * float2(0.14, 0.3) + float2(t * 0.012, -t * 0.008), 2.1));
                float patch = smoothstep(0.42, 0.72, cloud);
                float hue = StationNoise3(float3(p * 0.1, t * 0.02 + 5.0));
                float glance = 0.3 + 0.7 * pow(1.0 - saturate(viewOS.y), 2.0);
                color += lerp(_AuroraLow.rgb, _AuroraHigh.rgb, smoothstep(0.35, 0.65, hue)) * patch * glance * smoothstep(2.5, 6.0, r) * _AuroraAmount;
                // 유리 표면: 하늘 되비침 (흐린 단계 — 별 한 점이 줄로 번지지 않게)
                float3 normalWS = TransformObjectToWorldNormal(float3(0.0, 1.0, 0.0));
                float fresnel = 0.02 + 0.5 * pow(1.0 - saturate(dot(normalWS, viewWS)), 5.0);
                half4 encoded = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflect(-viewWS, normalWS), 3.0);
                color += DecodeHDREnvironment(encoded, unity_SpecCube0_HDR) * fresnel * _Reflect;
                return half4(color * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
