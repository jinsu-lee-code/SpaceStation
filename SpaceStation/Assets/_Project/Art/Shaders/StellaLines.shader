// 시민 회관 스텔라돔 별자리 (InteriorStellaDome이 메시를 만듦): 잇는 선이 별에서 별로 차례로 그려지고 별이 네 갈래로 빛남.
// uv0 = (선 따라 0 ~ 1 또는 별 사각 -1 ~ 1, 가로 -1 ~ 1), uv1 = (별자리 번호, 시작, 길이, 0 = 선 · 1 = 별). _Reveal · _Glow = 별자리마다 그려진 정도 · 바라보는 밝기. 더해 그림.
Shader "SpaceStation/StellaLines"
{
    Properties
    {
        [HDR] _LineColor ("선", Color) = (0.62, 0.74, 1.05, 1)
        [HDR] _StarColor ("별", Color) = (1.6, 1.6, 1.7, 1)
        _Fade ("전체", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "StellaLines"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LineColor;
                half4 _StarColor;
                float _Fade;
            CBUFFER_END
            float _Reveal[8];
            float _Glow[8];

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 data : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 data : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.data = input.data;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                int c = (int)(i.data.x + 0.5);
                float reveal = _Reveal[c];
                float glow = _Glow[c];
                if (reveal <= 0.001)
                    return half4(0, 0, 0, 0);
                float local = saturate((reveal - i.data.y) / max(i.data.z, 0.001));
                if (i.data.w < 0.5)
                {
                    // 선: 시작 별에서 앞으로 그려짐, 가로로 끝이 흐림
                    float across = 1.0 - abs(i.uv.y);
                    float ahead = 1.0 - smoothstep(local - 0.02, local + 0.02, i.uv.x);
                    float a = across * across * ahead * _Fade * (0.55 + 0.75 * glow);
                    return half4(_LineColor.rgb, saturate(a));
                }
                // 별: 네 갈래 + 가운데 빛
                float2 q = abs(i.uv);
                float spike = saturate(1.0 - sqrt(q.x) - sqrt(q.y));
                float core = exp(-dot(i.uv, i.uv) * 24.0);
                float appear = smoothstep(0.0, 0.25, local);
                float b = (spike * spike * 1.4 + core * 1.6) * appear * _Fade * (0.8 + 0.7 * glow);
                return half4(_StarColor.rgb, saturate(b));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
