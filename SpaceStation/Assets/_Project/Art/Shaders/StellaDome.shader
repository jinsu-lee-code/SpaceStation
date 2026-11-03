// 시민 회관 스텔라돔 상영 하늘 (사용자 2026-11-03 시안 1 "스텔라돔 체험"): 유리 돔 안쪽 껍질. 평소엔 투명 (유리 너머 진짜 우주), 상영 때 가장자리부터 어두워지며 밤하늘이 됨.
// 별 = 위에서 본 입체 투영 격자 (늘어나지 않음, 칸마다 나타나는 순서가 달라 하나씩 떠오름) · 은하수 = 어두운 바탕 + 따뜻한 별빛 핵 + 푸른 바깥 + 가운데를 가르는 먼지 띠 (MilkyRiver와 같은 꼴, natural-fx 규칙: 사방 끝 흐리게) ·
// 오로라 = 가장자리 위 부드러운 커튼 · 별똥별 = 짧게 스치는 꼬리 · 새벽 = 지평선의 따뜻한 빛. 값은 InteriorStellaDome이 시간에 따라 넣음, 밝기 · 색은 재질 슬라이더 (M_InteriorEvo2StarDome).
// UV 없이 물체 공간 위치를 씀 (FBX 원점 = 템플릿 원점). 양면 — 갑판에서 돔을 봐도 상영 하늘이 보임.
Shader "SpaceStation/StellaDome"
{
    Properties
    {
        _Show ("상영 (0 = 유리 · 1 = 밤하늘)", Range(0, 1)) = 0
        _StarReveal ("별 떠오름", Range(0, 1)) = 0
        _Milky ("은하수", Range(0, 1)) = 0
        _Aurora ("오로라", Range(0, 1)) = 0
        _Meteor ("별똥별", Range(0, 1)) = 0
        _Dawn ("새벽", Range(0, 1)) = 0
        _SkyCenter ("하늘 가운데 (모델)", Vector) = (4, -1, 4, 0)
        _RimY ("돔 시작 높이", Float) = 1.2
        _TopY ("돔 꼭대기 높이", Float) = 3.7
        _ZenithColor ("하늘 꼭대기", Color) = (0.004, 0.006, 0.02, 1)
        _HorizonColor ("지평선", Color) = (0.016, 0.028, 0.08, 1)
        [HDR] _CoreColor ("은하수 핵", Color) = (1.05, 0.8, 0.52, 1)
        [HDR] _ArmColor ("은하수 바깥", Color) = (0.3, 0.42, 0.95, 1)
        [HDR] _NebulaColor ("성운", Color) = (0.5, 0.2, 0.42, 1)
        [HDR] _StarColor ("별", Color) = (1.3, 1.3, 1.45, 1)
        _StarBright ("별 밝기", Range(0, 3)) = 1
        _MilkyBright ("은하수 밝기", Range(0, 3)) = 1
        _AuroraBright ("오로라 밝기", Range(0, 3)) = 1
        _DawnColor ("새벽빛", Color) = (1, 0.55, 0.34, 1)
    }
    SubShader
    {
        // 불투명 · 하늘 껍질 다음, 유리보다 먼저 + 깊이를 씀 (앞쪽 난간 유리는 그대로, 뒤쪽 돔 유리는 안 덮임 — 안 쓰면 유리가 별 위에 덧칠되며 살 · 등롱 자리만 비어 윤곽이 비침)
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "StellaDome"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "StationNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Show;
                float _StarReveal;
                float _Milky;
                float _Aurora;
                float _Meteor;
                float _Dawn;
                float4 _SkyCenter;
                float _RimY;
                float _TopY;
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _CoreColor;
                half4 _ArmColor;
                half4 _NebulaColor;
                half4 _StarColor;
                float _StarBright;
                float _MilkyBright;
                float _AuroraBright;
                half4 _DawnColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionOS = input.positionOS.xyz;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            // 별 한 겹: 위에서 본 평면 p (입체 투영 — 원이 원으로, 격자 줄무늬 없음)의 칸마다 별 하나. cell · radius는 p 단위. reveal보다 순서가 빠른 별부터 나타남, 박자가 저마다 다름
            float Stars(float2 p, float cell, float density, float radius, float seed, float pixel, float reveal)
            {
                float2 g = p / cell;
                float2 id = floor(g);
                float h = StationHash3(float3(id, seed));
                if (h > density)
                    return 0.0;
                float2 at = float2(StationHash3(float3(id, seed + 3.1)), StationHash3(float3(id, seed + 5.7))) * 0.7 + 0.15;
                float dist = length(frac(g) - at) * cell;
                float order = StationHash3(float3(id, seed + 12.3));
                float appear = smoothstep(order - 0.1, order + 0.02, reveal * 1.1);
                float twinkle = 0.7 + 0.3 * sin(_Time.y * (0.8 + h * 9.0) + order * 80.0);
                float w = pixel * 0.8;
                float dim = saturate(radius / max(w, 0.000001));   // 화면에서 한 점보다 작으면 어둡게 (지글거리지 않게)
                return smoothstep(radius + w, max(radius - w, 0.0), dist) * twinkle * appear * dim;
            }

            float3 Milky(float3 d, out float bandAmount, out float laneAmount)
            {
                // 은하수 띠: 꼭대기 근처를 지나는 큰 원 (이음매는 지평선 아래)
                const float3 N = float3(0.4264, 0.3331, -0.8413);
                float3 C0 = normalize(float3(0.0, 1.0, 0.0) - N * N.y);
                float3 V0 = cross(N, C0);
                float lat = dot(d, N);
                float lon = atan2(dot(d, V0), dot(d, C0));
                float rim = StationFbm3(float3(lon * 1.6, lat * 4.0, 3.1));
                lat += (StationFbm3(float3(lon * 1.1, 2.3, 7.4)) - 0.5) * 0.09;
                float w = 0.14 * (0.8 + 0.7 * rim);
                float band = exp(-lat * lat / (w * w));
                float wide = exp(-lat * lat / (w * w * 5.0));
                float cloud = StationFbm3(float3(lon * 7.0, lat * 26.0, 4.2));
                float patches = 0.55 + 0.9 * StationFbm3(float3(lon * 1.6, 7.7, 1.3));
                float warp = StationNoise3(float3(lon * 1.8, lat * 6.0, 2.3));
                float lane = smoothstep(0.42, 0.62, StationFbm3(float3(lon * 3.0 + warp * 1.4, lat * 22.0, 1.9))) * exp(-lat * lat / (w * w * 0.42));
                float core = exp(-(lon + 0.42) * (lon + 0.42) / 0.5);
                float3 c = _ArmColor.rgb * wide * (0.02 + 0.13 * cloud);
                c += lerp(_ArmColor.rgb, _CoreColor.rgb, saturate(band * band * (0.4 + core))) * band * patches * (0.05 + 1.0 * cloud * cloud) * (1.0 - 0.9 * lane) * (0.55 + 1.15 * core);
                c += _NebulaColor.rgb * smoothstep(0.5, 0.75, StationFbm3(float3(lon * 2.1, lat * 7.0, 9.1))) * wide * 0.1;
                bandAmount = wide;
                laneAmount = lane;
                return c;
            }

            // 오로라: 가장자리 위 부드러운 커튼 조각 몇 개 (이어진 고리가 아님). 방위를 원 위 좌표로 재서 이음매가 없고, 아랫단 · 위 끝 · 양옆이 모두 흐리게 사라지며 천천히 떠돎 (natural-fx: 칼로 자른 선 금지)
            float3 AuroraSky(float3 d, float h)
            {
                float t = _Time.y;
                float2 ring = normalize(d.xz + 0.0001);
                float n = StationFbm3(float3(ring * 1.5, t * 0.04));
                float hc = 0.1 + 0.34 * n;   // 아랫단 높이가 방위 따라 너울거림
                float below = (hc - h) / 0.11;
                float above = (h - hc) / 0.3;
                float profile = h < hc ? exp(-below * below) : exp(-above * above);
                float rays = StationNoise3(float3(ring * 15.0 + t * 0.03, t * 0.1));
                rays = 0.25 + 0.75 * rays * rays * 2.2;
                float env = smoothstep(0.47, 0.66, StationFbm3(float3(ring * 1.25 + 7.0, t * 0.03)));   // 커튼 조각: 끊어진 사이는 빈 하늘
                float3 c = lerp(float3(0.14, 0.72, 0.46), float3(0.42, 0.26, 0.78), saturate((h - hc) / 0.3));
                return c * profile * rays * env * 0.8;
            }

            // 별똥별 다섯: 박자마다 다른 자리에서 짧게 스침 (머리 밝고 꼬리 흐림)
            float MeteorSky(float3 d)
            {
                float sum = 0.0;
                for (int k = 0; k < 5; k++)
                {
                    float beat = _Time.y / 2.7 + k * 0.37;
                    float cycle = floor(beat);
                    float p = saturate(frac(beat) / 0.3);
                    if (p <= 0.0 || p >= 1.0)
                        continue;
                    float a0 = StationHash3(float3(cycle, k, 1.3)) * 6.2832;
                    float e0 = 0.5 + StationHash3(float3(cycle, k, 2.9)) * 0.6;
                    float a1 = a0 + (StationHash3(float3(cycle, k, 4.7)) - 0.5) * 1.1;
                    float e1 = e0 - 0.32;
                    float3 A = float3(cos(a0) * cos(e0), sin(e0), sin(a0) * cos(e0));
                    float3 B = float3(cos(a1) * cos(e1), sin(e1), sin(a1) * cos(e1));
                    float3 head = normalize(lerp(A, B, p));
                    float3 tail = normalize(lerp(A, B, saturate(p - 0.42)));
                    float3 ab = head - tail;
                    float s = saturate(dot(d - tail, ab) / max(dot(ab, ab), 0.00001));
                    float3 q = tail + ab * s;
                    float dist2 = dot(d - q, d - q);
                    // 꼬리가 충분히 길어진 뒤에만 보임 (시작점에 둥근 빛덩이가 생기지 않게)
                    sum += exp(-dist2 / 0.00001) * s * s * sin(p * 3.14159) * smoothstep(0.02, 0.1, length(ab));
                }
                return sum;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float h = saturate((i.positionOS.y - _RimY) / max(_TopY - _RimY, 0.01));
                float edge = _Show * 1.25;
                float alpha = 1.0 - smoothstep(edge - 0.25, edge, h);
                clip(alpha - 0.002);   // 아직 안 덮인 곳은 깊이도 안 씀 (유리 · 바깥이 그대로)
                float3 d = normalize(i.positionOS - _SkyCenter.xyz);
                float3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(h, 0.6));
                float bandAmount = 0.0, lane = 0.0;
                if (_Milky > 0.002)
                    color += Milky(d, bandAmount, lane) * _Milky * _MilkyBright;
                else
                    bandAmount = 0.0;
                if (_StarReveal > 0.002)
                {
                    float2 p = d.xz / (1.0 + max(d.y, -0.9));
                    float pp = max(length(fwidth(p)), 0.00001);
                    float s = Stars(p, 0.0055, 0.05 + 0.5 * bandAmount, 0.0007, 7.0, pp, _StarReveal) * 0.42;
                    s += Stars(p, 0.014, 0.15 + 0.25 * bandAmount, 0.0009, 17.0, pp, _StarReveal) * 0.7;
                    s += Stars(p, 0.036, 0.17, 0.0012, 29.0, pp, _StarReveal) * 1.05;
                    s += Stars(p, 0.09, 0.22, 0.0019, 41.0, pp, _StarReveal) * 2.2;
                    color += _StarColor.rgb * s * (1.0 - 0.7 * lane) * _StarBright;
                }
                if (_Aurora > 0.002)
                    color += AuroraSky(d, h) * _Aurora * _AuroraBright;
                if (_Meteor > 0.002)
                    color += float3(1.0, 0.97, 0.9) * MeteorSky(d) * _Meteor * 2.2;
                if (_Dawn > 0.002)
                {
                    float2 ring = normalize(d.xz + 0.0001);
                    float side = 0.5 + 0.5 * dot(ring, float2(0.7071, -0.7071));   // 동남쪽 (의료동 쪽에서) 날이 밝아 옴
                    float glow = exp(-h * h / (0.05 + 0.34 * _Dawn)) * (0.3 + 0.7 * side);
                    color = lerp(color, color * 0.55 + float3(0.05, 0.1, 0.22) * _Dawn, _Dawn * 0.8);
                    color += _DawnColor.rgb * glow * _Dawn * 0.75;
                }
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
