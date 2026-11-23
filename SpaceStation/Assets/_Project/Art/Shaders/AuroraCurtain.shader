// 오로라 커튼 (사용자 2026-10-27 "고정된 형태가 아니라 일렁거리면서", 2026-11-02 "칼로 자른 것처럼 보임 — 부드럽게, 자연의 오로라처럼, 고정된 위치가 아니라 주변 범위에서 일렁이게"): AuroraFx가 만든 띠 메시.
// 사방 끝이 모두 부드럽게 사라짐 (양 끝 · 구불구불한 아랫단 · 빛살마다 높이가 다른 위 끝), 세로 빛살은 잡음이라 줄이 아니라 결로 보임, 밝은 덩이가 띠를 따라 천천히 지나감.
// 꼭짓점: 띠 전체가 제자리 주변을 떠돌고 (_Wander) 그 위로 주름이 너울거림 (_Sway). 아래 초록 → 위 보라. 가산 · 양면.
// 조절은 재질 슬라이더로: 플레이 중에 바꿔도 바로 보이고 저장됨 (M_AuroraCurtain — 실드 돔 · 궤도 방어망 공통).
// UV: x = 띠 길이 (0 ~ 1), y = 아래 0 ~ 위 1. UV2: x = 길이 (모델 기준), y = 띠마다 다른 씨앗. 법선 = 띠 면 수직 (수평), 접선 = 띠 방향. _Level = 모듈 상태 (AuroraFx).
Shader "SpaceStation/AuroraCurtain"
{
    Properties
    {
        [HDR] _LowColor ("아래 색", Color) = (0.22, 1.2, 0.55, 1)
        [HDR] _MidColor ("가운데 색", Color) = (0.2, 0.85, 0.75, 1)
        [HDR] _HighColor ("위 색", Color) = (0.55, 0.22, 1.05, 1)
        _Intensity ("광량", Range(0, 2)) = 0.5
        _Wander ("떠도는 범위", Range(0, 1)) = 0.07
        _Sway ("주름 너울", Range(0, 1)) = 0.05
        _Speed ("움직임 속도", Range(0, 3)) = 1
        _HemSoft ("아랫단 부드러움", Range(0.02, 0.6)) = 0.2
        _EndSoft ("양 끝 부드러움", Range(0.02, 0.5)) = 0.18
        _RayScale ("빛살 잘기 (클수록 촘촘)", Range(1, 30)) = 12
        _RayStrength ("빛살 진하기", Range(0, 1)) = 0.7
        [HideInInspector] _Level ("Level", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "AuroraCurtain"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "StationNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LowColor;
                half4 _MidColor;
                half4 _HighColor;
                float _Intensity;
                float _Wander;
                float _Sway;
                float _Speed;
                float _HemSoft;
                float _EndSoft;
                float _RayScale;
                float _RayStrength;
                float _Level;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 along : TEXCOORD1;     // x = 길이, y = 씨앗
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 p = input.positionOS.xyz;
                float v = input.uv.y;
                float s = input.uv2.x;
                float seed = input.uv2.y;
                float t = _Time.y * _Speed;
                // 띠 전체가 제자리 주변을 천천히 떠돎 (면 앞뒤 · 띠 방향 · 위아래가 서로 다른 박자)
                float drift = sin(t * 0.21 + seed * 6.3) * 0.55 + sin(t * 0.13 + seed * 2.1 + s * 1.3) * 0.45;
                float slide = sin(t * 0.17 + seed * 4.1);
                float rise = sin(s * 2.1 + t * 0.37 + seed * 5.0) * 0.6 + sin(t * 0.23 + seed * 8.0) * 0.4;
                // 그 위로 길이를 따라 흐르는 주름 (위로 갈수록 크게)
                float folds = sin(s * 5.5 - t * 0.7 + seed * 3.0) * 0.5 + sin(s * 12.7 + t * 1.1 + v * 2.2 + seed) * 0.2 + sin(s * 2.3 - t * 0.31 + v * 1.2) * 0.5;
                p += input.normalOS * (drift * _Wander + folds * _Sway * (0.35 + v * 0.9));
                p += input.tangentOS.xyz * (slide * _Wander * 0.8 + sin(s * 3.1 - t * 0.5 + v * 2.0) * _Sway * 0.35 * v);
                p.y += rise * _Wander * 0.35 * (0.5 + 0.5 * v);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = input.uv;
                o.along = input.uv2;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y * _Speed;
                float u = i.uv.x, v = i.uv.y;
                float s = i.along.x, seed = i.along.y;
                float ends = smoothstep(0.0, _EndSoft, u) * smoothstep(1.0, 1.0 - _EndSoft, u);                  // 양 끝
                float lower = 0.02 + 0.12 * StationNoise3(float3(s * 2.4 - t * 0.12, seed * 7.0, t * 0.08));     // 구불구불한 아랫단
                float hem = smoothstep(lower, lower + _HemSoft, v);
                float rayWide = StationNoise3(float3(s * _RayScale + sin(s * 1.7 + t * 0.4) * 0.6, seed * 3.1, t * 0.22));
                float rayFine = StationNoise3(float3(s * _RayScale * 2.7 - t * 0.3, seed * 5.3, t * 0.37));
                float rays = lerp(1.0, saturate(0.2 + 1.2 * rayWide * rayWide + 0.35 * rayFine), _RayStrength);  // 세로 빛살 결
                float top = 0.5 + 0.5 * rayWide;                                                                 // 빛살마다 다른 위 끝
                float up = 1.0 - smoothstep(top * 0.2, top, v);
                float patch = 0.4 + 0.6 * smoothstep(0.25, 0.75, StationNoise3(float3(s * 1.1 - t * 0.1, seed * 9.0, t * 0.06)));   // 밝은 덩이가 띠를 따라 지나감
                // 주름이 시선과 나란해지는 곳은 옅게 — 얇은 판의 옆선이 칼날처럼 보이지 않게
                float3 sheet = normalize(cross(ddx(i.positionWS), ddy(i.positionWS)));
                float edgeOn = smoothstep(0.03, 0.42, abs(dot(sheet, normalize(_WorldSpaceCameraPos - i.positionWS))));
                float light = hem * up * rays * patch * ends * edgeOn * _Intensity * _Level;
                float3 color = lerp(lerp(_LowColor.rgb, _MidColor.rgb, smoothstep(0.08, 0.32, v)), _HighColor.rgb, smoothstep(0.28, 0.72, v));
                return half4(color * light, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
