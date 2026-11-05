// 작은 해 홍염 (사용자 2026-11-05 정련 콤비나트 "태양의 고리가 뚫고 지나가는 점 · 고정되어 있는 점"): LineRenderer 띠에 입히는 가산 빛.
// 굳은 관 대신 가로 · 양 끝이 흐려 표면에서 솟아 녹아드는 불꽃 고리 + 길이 방향으로 흐르는 빛 결. 투명도 = 정점 색 알파 (InteriorSunForge가 솟고 사라지는 동안 조절).
// UV: u = 띠 길이 (Stretch, 0 ~ 1), v = 띠 너비 (0 ~ 1). 깊이 쓰기 없음 · 깊이 검사 있음: 해 뒤로 넘어간 부분은 해에 가려짐.
Shader "SpaceStation/SolarFlare"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (2.6, 1.15, 0.28, 1)
        [HDR] _CoreColor ("Core", Color) = (4.2, 2.7, 1.1, 1)
        _Speed ("Speed", Float) = 1
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "SolarFlare"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _CoreColor;
                float _Speed;
                float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            float Fbm(float2 p)
            {
                return Noise(p) * 0.55 + Noise(p * 2.03 + 7.1) * 0.3 + Noise(p * 4.07 + 3.7) * 0.15;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y * _Speed;
                float across = abs(i.uv.y * 2.0 - 1.0);
                float body = pow(saturate(1.0 - across), 2.2);                                     // 가장자리는 흐리게
                float core = pow(saturate(1.0 - across), 6.0);
                float ends = smoothstep(0.0, 0.16, i.uv.x) * smoothstep(0.0, 0.16, 1.0 - i.uv.x);  // 양 끝은 표면으로 녹아듦
                float grain = Fbm(float2(i.uv.x * 7.0 - t * 0.9 + _Seed, across * 1.8 + _Seed * 3.1));
                float strands = smoothstep(0.2, 0.72, grain);                                      // 흐르는 빛 결
                float a = body * ends * (0.3 + strands * 0.9) * i.color.a;
                float3 color = lerp(_Color.rgb, _CoreColor.rgb, saturate(core * strands)) * a;
                return half4(color, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
