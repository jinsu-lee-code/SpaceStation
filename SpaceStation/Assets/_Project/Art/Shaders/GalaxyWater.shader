// 방사선 차폐벽 · 화물 환적장 은하수 물 (사용자 2026-10-27 "진짜 은하수처럼 반짝거리고 생동감 있는 물"):
// 깊은 남청 반투명 물 위로 물결 반사광이 흐르고, 별이 저마다의 박자로 반짝이며, 비스듬히 볼수록 물빛이 밝아진다.
// UV가 없는 FX 메시라 물체 공간 위치를 면 방향별(삼면 투영)로 써서 잡음 · 별 격자를 만든다 (낙하 물길처럼 선 면도 늘어지지 않게).
// 물을 먼저 그려 속 빛 띠 · 별가루가 묻히지 않게 큐는 재질에서 Transparent − 1. _BaseColor는 ModuleView가 비활성 · 파손 때 어둡게 한다.
Shader "SpaceStation/GalaxyWater"
{
    Properties
    {
        [HDR] _BaseColor ("Tint (State)", Color) = (1, 1, 1, 1)
        _DeepColor ("Deep", Color) = (0.03, 0.08, 0.3, 0.7)
        [HDR] _WaveColor ("Wave Highlight", Color) = (0.35, 0.5, 1.5, 1)
        [HDR] _StarColor ("Stars", Color) = (1.8, 1.8, 2.3, 1)
        _Scale ("Wave Scale", Float) = 9
        _Flow ("Flow Speed", Float) = 0.35
        _StarDensity ("Star Density", Range(0, 1)) = 0.14
        _StarCell ("Star Cell", Float) = 0.02
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "GalaxyWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "StationNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _WaveColor;
                half4 _StarColor;
                float _Scale;
                float _Flow;
                float _StarDensity;
                float _StarCell;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalOS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 viewWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionOS = input.positionOS.xyz;
                o.normalOS = input.normalOS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }

            // 흐르는 물결 (평면 좌표 + 시간 = 3D 잡음)
            float Wave(float2 uv, float t)
            {
                return StationFbm3(float3(uv * _Scale + float2(t * 1.3, t * 0.6), t * 0.8));
            }

            // 반짝이는 별: 격자 칸마다 확률로 별 하나, 칸 안 무작위 자리 · 저마다의 박자
            float Stars(float2 uv, float t)
            {
                float2 g = uv / _StarCell;
                float2 cell = floor(g);
                float h = StationHash3(float3(cell, 7.0));
                if (h > _StarDensity)
                    return 0.0;
                float2 at = float2(StationHash3(float3(cell, 11.0)), StationHash3(float3(cell, 13.0))) * 0.6 + 0.2;
                float d = length(frac(g) - at);
                float twinkle = 0.5 + 0.5 * sin(t * (2.0 + h * 30.0) + h * 80.0);
                return smoothstep(0.3, 0.0, d) * twinkle * twinkle;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y * _Flow;
                float3 w = abs(normalize(i.normalOS));
                w /= max(w.x + w.y + w.z, 1e-4);
                float3 p = i.positionOS;
                float wave = Wave(p.xz, t) * w.y + Wave(p.xy, t) * w.z + Wave(p.zy, t) * w.x;
                float star = Stars(p.xz, _Time.y) * w.y + Stars(p.xy, _Time.y) * w.z + Stars(p.zy, _Time.y) * w.x;
                float caustic = smoothstep(0.52, 0.82, wave);
                float fresnel = pow(1.0 - saturate(abs(dot(normalize(i.normalWS), normalize(i.viewWS)))), 3.0);
                float3 color = _DeepColor.rgb + _WaveColor.rgb * (caustic * 0.55 + fresnel * 0.25) + _StarColor.rgb * star;
                float alpha = saturate(_DeepColor.a + caustic * 0.15 + fresnel * 0.2 + star * 0.8);
                return half4(color * _BaseColor.rgb, alpha * _BaseColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
