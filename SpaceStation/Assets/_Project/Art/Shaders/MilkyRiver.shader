// 방사선 차폐벽 은하수 강 (사용자 2026-11-02 "보라색 선이 가운데 있어 방사선에 오염된 물 같음 — 실제 은하수처럼, 강처럼 흐르고, 물 표면에 반사된 우주 느낌"):
// 진짜 은하수 = 따뜻한 별빛 핵 · 푸른 바깥 · 가운데를 가르는 어두운 먼지 띠 · 옅은 성운 · 빽빽한 작은 별과 드문 밝은 별, 이 전체가 물결에 일그러진 채 강을 따라 흐른다.
// 물 표면: 물결의 기울기로 그림을 일그러뜨리고, 실제 하늘(환경 반사)을 되비추며, 비스듬히 볼수록 더 비침. 작은 별은 시점에 따라 어긋나 물속 깊이가 느껴짐.
// UV가 없는 FX 메시라 물체 공간(모델 기준) 위치를 씀. 불투명 — 물속에 구워 둔 옛 빛 띠 · 별가루는 물 표면에 가려짐.
// 조절은 재질 슬라이더로 (M_MilkyRiver). _BaseColor는 ModuleView가 비활성 · 파손 때 어둡게 한다.
Shader "SpaceStation/MilkyRiver"
{
    Properties
    {
        [HDR] _BaseColor ("Tint (State)", Color) = (1, 1, 1, 1)
        _DeepColor ("깊은 물색", Color) = (0.008, 0.014, 0.04, 1)
        [HDR] _CoreColor ("은하수 핵", Color) = (1.05, 0.86, 0.62, 1)
        [HDR] _ArmColor ("은하수 바깥", Color) = (0.3, 0.42, 0.95, 1)
        [HDR] _NebulaColor ("성운", Color) = (0.55, 0.2, 0.42, 1)
        [HDR] _StarColor ("별", Color) = (1.25, 1.25, 1.45, 1)
        _Brightness ("은하수 밝기", Range(0, 3)) = 1
        _StarAmount ("별 밝기", Range(0, 3)) = 1
        _RiverZ ("강 가운데 (모델 z)", Float) = 0.14
        _RiverWidth ("강 폭", Range(0.01, 0.2)) = 0.036
        _Meander ("굽이침", Range(0, 0.1)) = 0.028
        _Flow ("흐름 속도", Range(0, 0.3)) = 0.05
        _Current ("흐름 결 · 물결 굴곡", Range(0, 0.3)) = 0.04
        _Ripple ("물결 일그러짐", Range(0, 0.03)) = 0.007
        _Reflect ("하늘 되비침", Range(0, 2)) = 0.8
        _Glint ("햇빛 윤슬", Range(0, 2)) = 0.35
        _Depth ("별 깊이", Range(0, 0.1)) = 0.03
        _CoordScale ("좌표 배율 (바깥 = 1, 내부 = 1/8)", Float) = 1
        _StarSize ("별 크기 (바깥 = 1, 가까이서 보면 줄임)", Range(0.1, 1)) = 1
        _StarDensity ("별 밀도 (바깥 = 1)", Range(0.1, 1)) = 1
        _BankPeriod ("물가 지그재그 간격", Float) = 0.6
        _BankOffset ("물가 지그재그 어긋남", Float) = 0.1
        _BankFront ("물가 앞 끝", Float) = -0.058
        _BankAmp ("물가 지그재그 폭", Float) = 0.12
        _BankBack ("물가 뒤 끝", Float) = 0.2314
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            Name "MilkyRiver"
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
                half4 _CoreColor;
                half4 _ArmColor;
                half4 _NebulaColor;
                half4 _StarColor;
                float _Brightness;
                float _StarAmount;
                float _RiverZ;
                float _RiverWidth;
                float _Meander;
                float _Flow;
                float _Current;
                float _Ripple;
                float _Reflect;
                float _Glint;
                float _Depth;
                float _CoordScale;
                float _StarSize;
                float _StarDensity;
                float _BankPeriod;
                float _BankOffset;
                float _BankFront;
                float _BankAmp;
                float _BankBack;
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
                float3 p = input.positionOS.xyz;
                if (input.normalOS.y > 0.5)   // 윗면을 테 바로 아래까지 올림 — 물속에 구운 별 상자 (꼭대기가 원래 수면보다 살짝 위)가 수면을 뚫지 않게
                    p.y += 0.0055;
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.positionOS = p;
                o.normalOS = input.normalOS;
                return o;
            }

            // 별: 격자 칸마다 확률로 별 하나, 칸 안 무작위 자리 · 저마다의 박자. density = 칸에 별이 있을 확률. 칸이 화면에서 너무 작으면 사라짐 (멀리서 지글거리지 않게)
            float Stars(float2 p, float cell, float density, float size, float seed, float pixel)
            {
                float2 g = p / cell;
                float2 id = floor(g);
                float h = StationHash3(float3(id, seed));
                if (h > density * _StarDensity)
                    return 0.0;
                float2 at = float2(StationHash3(float3(id, seed + 3.0)), StationHash3(float3(id, seed + 5.0))) * 0.6 + 0.2;
                float d = length(frac(g) - at);
                float twinkle = 0.65 + 0.35 * sin(_Time.y * (1.5 + h * 20.0) + h * 80.0);
                return smoothstep(size * _StarSize, 0.0, d) * twinkle * smoothstep(1.5, 4.0, cell / pixel);
            }

            // 물 높이: 흐름 쪽으로 늘어난 부드러운 너울 (윤슬 줄이 흐름을 가로지름)
            float Swell(float2 r, float w)
            {
                return StationNoise3(float3(r, w)) + 0.5 * StationNoise3(float3(r * 2.1 + 3.7, w * 1.4));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y;
                if (abs(i.normalOS.y) < 0.5)   // 옆면 (테두리에 가려 거의 안 보임). 아랫면 = 내부 전시 회랑 천장 (강 아래에서 올려다봄)
                    return half4(_DeepColor.rgb * _BaseColor.rgb, 1);
                float2 p = i.positionOS.xz * _CoordScale;
                float pixel = max(fwidth(p.x) + fwidth(p.y), 0.00001);
                // 물결: 흐름과 함께 밀려가는 너울의 기울기 — 이것이 비친 은하수를 일그러뜨리고 하늘 · 햇빛 반사를 흔듦
                float2 rp = p * float2(13.0, 30.0) - float2(t * _Flow * 13.0, t * 0.04);
                float tw = t * 0.25;
                float h0 = Swell(rp, tw);
                float2 grad = float2(Swell(rp + float2(0.25, 0.0), tw) - h0, Swell(rp + float2(0.0, 0.25), tw) - h0) * 4.0;
                float2 q = p + grad * _Ripple;
                // 시점
                float3 viewWS = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 viewOS = normalize(TransformWorldToObjectDir(viewWS));
                float2 parallax = viewOS.xz / max(abs(viewOS.y), 0.25);
                // 강: 흐르는 좌표 x, 강 가운데에서 떨어진 거리 d
                float x = q.x - t * _Flow;
                float zc = _RiverZ + _Meander * sin(q.x * 2.6 + 0.8) + 0.008 * sin(q.x * 6.1);
                float d = q.y - zc;
                float rim = StationFbm3(float3(x * 2.2, q.y * 5.0, 3.1));
                float w = _RiverWidth * (0.8 + 0.7 * rim);
                float band = exp(-d * d / (w * w));
                float wide = exp(-d * d / (w * w * 6.0));
                // 은하수 구름 · 어두운 먼지 띠 (길게 늘어난 잡음, 가운데를 가름)
                float cloud = StationFbm3(float3(x * 9.0, q.y * 26.0, 4.2));
                float patches = 0.55 + 0.9 * StationFbm3(float3(x * 1.4, 7.7, t * 0.02));
                float warp = StationNoise3(float3(x * 1.8, q.y * 6.0, 2.3));
                float lane = smoothstep(0.4, 0.62, StationFbm3(float3(x * 3.0 + warp * 1.4, d * 22.0, 1.9))) * exp(-d * d / (w * w * 0.45));
                float3 color = _DeepColor.rgb;
                color += _ArmColor.rgb * wide * (0.03 + 0.14 * cloud) * _Brightness;
                color += lerp(_ArmColor.rgb, _CoreColor.rgb, band * band) * band * patches * (0.06 + 0.5 * cloud * cloud * 2.0) * (1.0 - 0.9 * lane) * _Brightness;
                color += _NebulaColor.rgb * smoothstep(0.5, 0.75, StationFbm3(float3(x * 2.1, q.y * 7.0, 9.1))) * wide * 0.12 * _Brightness;
                // 별: 은하수 쪽에 몰린 작은 별 (깊은 겹은 시점에 따라 어긋남) + 드문 밝은 별. 물결에는 그림보다 덜 일그러짐
                float2 flowing = float2(p.x + grad.x * _Ripple * 0.4 - t * _Flow, p.y + grad.y * _Ripple * 0.4);
                float stars = Stars(flowing - parallax * _Depth, 0.007, 0.08 + 0.6 * wide, 0.32, 7.0, pixel) * 0.4;
                stars += Stars(flowing - parallax * _Depth * 0.4, 0.016, 0.05 + 0.25 * wide, 0.22, 17.0, pixel) * 0.7;
                stars += Stars(flowing, 0.05, 0.09, 0.09, 29.0, pixel) * 1.4;
                color += _StarColor.rgb * stars * (1.0 - 0.7 * lane) * _StarAmount;
                // 흐름 결: 흐르는 쪽으로 길게 늘어난 옅은 물줄기 (그림보다 조금 빨리 흐름)
                float current = StationNoise3(float3((q.x - t * _Flow * 1.7) * 7.0, q.y * 60.0, t * 0.08));
                color += lerp(_ArmColor.rgb, 1.0, 0.4) * smoothstep(0.6, 0.85, current) * _Current;
                // 물결의 굴곡: 너울 한쪽 비탈이 하늘빛을 받아 위에서 봐도 수면으로 읽힘
                color += _ArmColor.rgb * saturate(grad.x * 0.5 + grad.y * 0.3) * _Current;
                // 물가: 벽 앞(지그재그) · 뒤 쪽으로 갈수록 어두워져 물이 깊어 보임
                float front = _BankFront + _BankAmp * abs(frac((p.x - _BankOffset) / _BankPeriod + 0.5) * 2.0 - 1.0);
                color *= 0.45 + 0.55 * smoothstep(0.0, 0.03, min(p.y - front, _BankBack - p.y));
                // 물 표면: 실제 하늘 되비침 (비스듬할수록) · 물결에 부서지는 햇빛
                float3 normalWS = normalize(TransformObjectToWorldNormal(normalize(float3(-grad.x * 0.16, i.normalOS.y > 0.0 ? 1.0 : -1.0, -grad.y * 0.16))));
                float facing = saturate(dot(normalWS, viewWS));
                float fresnel = 0.02 + 0.5 * pow(1.0 - facing, 5.0);
                float3 reflected = reflect(-viewWS, normalWS);
                // 흐린 단계로 읽음 — 하늘의 별 한 점이 굽은 수면에 비치면 고리 줄이 되므로 성운 빛깔만 되비침
                half4 encoded = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflected, 3.0);
                color += DecodeHDREnvironment(encoded, unity_SpecCube0_HDR) * fresnel * _Reflect;
                Light sun = GetMainLight();
                // 윤슬: 해가 비치는 길 안에서 잘게 부서진 반짝임 (좁은 반사만 쓰면 너울 둘레에 고리 줄이 그려짐), 멀면 고르게 옅어짐
                float sunPath = pow(saturate(dot(normalWS, normalize(sun.direction + viewWS))), 90.0);
                float fleck = StationNoise3(float3((p.x - t * _Flow * 1.7) * 190.0, p.y * 380.0, t * 0.9));
                fleck = lerp(0.08, smoothstep(0.66, 0.88, fleck), smoothstep(1.5, 4.0, 0.006 / pixel));
                color += sun.color * sunPath * fleck * _Glint;
                return half4(color * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
