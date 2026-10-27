// 실드 돔(오로라 등대) 오로라 커튼 (사용자 2026-10-27 "고정된 형태가 아니라 일렁거리면서, 색이 너무 진해 필름 같음"):
// 꼭짓점을 등대 둘레 방향으로 너울지게 밀어 커튼이 일렁이고, 아랫단이 밝고 위로 갈수록 옅어지며, 세로 빛살이 천천히 흐른다.
// 가산 · 양면이라 진한 막이 아니라 빛으로 보인다. UV가 없는 FX 메시라 물체 공간(모델 기준) 위치로 높이 · 둘레 각을 잰다:
// _Center = 등대 중심(xz), _BaseY · _Height = 커튼 아래 · 높이. _BaseColor는 ModuleView가 비활성 · 파손 때 어둡게 한다.
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _LowColor;
                half4 _HighColor;
                float4 _Center;
                float _BaseY;
                float _Height;
                float _Sway;
                float _Speed;
                float _Intensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float v : TEXCOORD0;      // 0 아랫단 ~ 1 위
                float theta : TEXCOORD1;  // 등대 둘레 각
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 p = input.positionOS.xyz;
                float2 d = p.xz - _Center.xz;
                float r = max(length(d), 1e-4);
                float2 dir = d / r;
                float theta = atan2(d.y, d.x);
                float v = saturate((p.y - _BaseY) / _Height);
                float t = _Time.y * _Speed;
                // 커튼 너울: 둘레를 따라 흐르는 두 물결 (위로 갈수록 크게)
                float sway = sin(theta * 5.0 + t * 0.9) * 0.6 + sin(theta * 11.0 - t * 1.7 + v * 3.0) * 0.4;
                p.xz += dir * sway * _Sway * (0.25 + v);
                p.y += sin(theta * 3.0 + t * 0.6) * _Sway * 0.4 * v;
                o.positionCS = TransformObjectToHClip(p);
                o.v = v;
                o.theta = theta;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y * _Speed;
                float rays = 0.5 + 0.5 * abs(sin(i.theta * 60.0 + sin(i.theta * 7.0 + t * 0.7) * 3.0 + t * 0.4)); // 세로 빛살
                float hem = smoothstep(0.0, 0.06, i.v) * (1.0 - smoothstep(0.06, 0.32, i.v));                       // 밝은 아랫단
                float body = 1.0 - smoothstep(0.12, 1.0, i.v);                                                      // 위로 옅어짐
                float flicker = 0.72 + 0.28 * sin(t * 1.7 + i.theta * 4.0);
                float a = (hem * 1.1 + body * 0.5) * rays * flicker;
                float3 color = lerp(_LowColor.rgb, _HighColor.rgb, smoothstep(0.1, 0.85, i.v));
                return half4(color * a * _Intensity * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
