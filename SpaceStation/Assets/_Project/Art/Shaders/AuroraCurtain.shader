// 오로라 커튼 (사용자 2026-10-27 "고정된 형태가 아니라 일렁거리면서, 색이 너무 진해 필름 같음" · "3단계 반영 때 커튼처럼 일렁이며 흔들리게, 실드 돔 오로라도 같이"):
// 꼭짓점을 커튼 면에 수직으로 너울지게 밀고 (길이를 따라 흐르는 물결 셋, 위로 갈수록 크게) 주름이 옆으로 미끄러지며 위끝이 살짝 오르내려
// 커튼이 일렁이고, 아랫단이 밝고 위로 갈수록 옅어지며, 세로 빛살이 천천히 흐른다. 가산 · 양면이라 진한 막이 아니라 빛으로 보인다.
// 두 가지 좌표:
//  - 둘레 모드 (실드 돔 등대, 기본): UV가 없는 FX 메시라 물체 공간(모델 기준) 위치로 높이 · 둘레 각을 잰다 —
//    _Center = 등대 중심(xz), _BaseY · _Height = 커튼 아래 · 높이, 커튼 면 수직 = 중심에서 바깥 방향
//  - UV 모드 (_UVMODE, 궤도 방어망 긴 커튼): u = 커튼 길이 (0 ~ 1, _Length = 실제 길이), v = 아랫단 0 → 위 1 (커튼 전체 기준),
//    커튼 면 수직 = 접선(u 방향) × 위 — 얇은 판의 앞 · 뒷면 법선은 서로 반대라 법선으로 밀면 판이 벌어지므로 접선을 쓴다.
//    _VStart = 이 겹이 시작하는 v (위쪽 보라 겹 0.35): 밝기 · 색은 겹 기준 v로 (겹마다 밝은 아랫단), 너울은 커튼 전체 v로 (겹이 같이 움직임)
// _BaseColor는 ModuleView가 비활성 · 파손 때 어둡게 한다.
Shader "SpaceStation/AuroraCurtain"
{
    Properties
    {
        [HDR] _BaseColor ("Tint (State)", Color) = (1, 1, 1, 1)
        [HDR] _LowColor ("Low (Hem)", Color) = (0.35, 1.3, 0.75, 1)
        [HDR] _HighColor ("High", Color) = (0.6, 0.3, 1.2, 1)
        _Center ("Center (object xz)", Vector) = (0.4, 0, 0.6, 0)
        _BaseY ("Base Y", Float) = 0.33
        _Height ("Height", Float) = 0.4
        [Toggle(_UVMODE)] _UVMode ("UV Mode (long curtain)", Float) = 0
        _Length ("Curtain Length (UV mode)", Float) = 2.5
        _VStart ("Layer Start V (UV mode)", Float) = 0
        _Sway ("Sway", Float) = 0.035
        _Speed ("Speed", Float) = 1
        _Intensity ("Intensity", Float) = 0.6
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
            #pragma shader_feature_local _UVMODE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LowColor;
                half4 _HighColor;
                float4 _Center;
                float _BaseY;
                float _Height;
                float _UVMode;
                float _Length;
                float _VStart;
                float _Sway;
                float _Speed;
                float _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float v : TEXCOORD0;      // 0 아랫단 ~ 1 위 (겹 기준)
                float s : TEXCOORD1;      // 커튼을 따라간 자리 (둘레 모드 = 각 × 6, UV 모드 = 길이 m × 6 — 두 모드의 물결 · 빛살 수가 비슷하게)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 p = input.positionOS.xyz;
                float3 nrm, side;
                float v, s, layerV;
            #if defined(_UVMODE)
                v = saturate(input.uv.y);
                layerV = saturate((v - _VStart) / max(1.0 - _VStart, 1e-3));
                s = input.uv.x * _Length * 6.0;
                float2 tg = input.tangentOS.xz;
                tg = dot(tg, tg) > 1e-8 ? normalize(tg) : float2(1, 0);
                side = float3(tg.x, 0, tg.y);
                nrm = float3(tg.y, 0, -tg.x);                                                               // 접선 × 위 (앞 · 뒷면 같은 방향)
            #else
                float2 d = p.xz - _Center.xz;
                float r = max(length(d), 1e-4);
                float2 dir = d / r;
                v = saturate((p.y - _BaseY) / _Height);
                layerV = v;
                s = atan2(d.y, d.x) * 6.0;
                nrm = float3(dir.x, 0, dir.y);
                side = float3(-dir.y, 0, dir.x);
            #endif
                float t = _Time.y * _Speed;
                float grow = 0.18 + v * (0.9 + 0.6 * v);                                                     // 아랫단은 조금, 위로 갈수록 크게
                // 커튼 너울: 길이를 따라 흐르는 큰 물결 + 반대로 흐르는 잔물결 + 느린 큰 주름 (위로 갈수록 엇갈려 접히듯)
                float wave = sin(s * 0.85 - t * 0.9) * 0.55 + sin(s * 1.9 + t * 1.6 + v * 3.0) * 0.3 + sin(s * 0.33 - t * 0.35 + v * 1.4) * 0.45;
                p += nrm * wave * _Sway * grow;
                p += side * sin(s * 0.6 - t * 0.7 + v * 2.2) * _Sway * 0.45 * v;                           // 주름이 옆으로 미끄러짐
                p.y += sin(s * 0.5 + t * 0.6) * _Sway * 0.45 * v;                                          // 위끝이 살짝 오르내림
                o.positionCS = TransformObjectToHClip(p);
                o.v = layerV;
                o.s = s;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y * _Speed;
                float rays = 0.5 + 0.5 * abs(sin(i.s * 10.0 + sin(i.s * 1.15 + t * 0.7) * 3.0 + t * 0.4));   // 세로 빛살 (흐르며 굽이침)
                float hem = smoothstep(0.0, 0.06, i.v) * (1.0 - smoothstep(0.06, 0.32, i.v));                // 밝은 아랫단
                float body = 1.0 - smoothstep(0.12, 1.0, i.v);                                               // 위로 옅어짐
                float flicker = 0.72 + 0.28 * sin(t * 1.7 + i.s * 0.66);
                float a = (hem * 1.1 + body * 0.5) * rays * flicker;
                float3 color = lerp(_LowColor.rgb, _HighColor.rgb, smoothstep(0.1, 0.85, i.v));
                return half4(color * a * _Intensity * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
