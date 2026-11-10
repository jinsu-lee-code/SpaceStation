// 내부 폭포 물줄기 (궤도 도시 계단 폭포, 사용자 2026-11-11 "유니티로 옮기면서 폭포도 이쁘게"): KWS 실사 물과 어울리는 반투명 물줄기.
// 결은 NevrianGlow ToonWaterfall 틀 (무늬 R = 세로 결을 두 번 아래로 흘림 · v 거듭제곱으로 아래일수록 빨라짐 · G = 둥근 거품 덩어리 · B = 찢김 잡음)을 따르고,
// 색은 만화 세 단계 대신 실제 폭포처럼:
//  - 턱 (v 0 ~ 0.25): 맑고 매끈한 물 — 뒤 (바위 · 벽)가 결 따라 휘어 비침 (불투명 화면 텍스처 굴절) + 결 마루의 가는 흰 빛
//  - 아래로 갈수록 공기가 섞여 하얘짐 (결이 굵은 곳부터) · 바닥 띠는 흰 거품 덩어리
//  - 양옆은 찢김 잡음으로 녹듯 사라짐 (자르지 않고 투명하게 — 부드러운 실사)
//  - 장면 빛 (환경광 + 주 빛)을 받고, 흰 물은 조금 스스로 밝음
// 메시 UV: u = 가로 (0 ~ 1), v = 턱에서 아래로 (0 = 턱, 1 = 바닥). 무늬 = T_WaterFallsMask (sRGB 끔).
Shader "SpaceStation/WaterFalls"
{
    Properties
    {
        [HDR] _BaseColor ("Tint (State)", Color) = (1, 1, 1, 1)
        _MaskTex ("Mask (R streaks, G foam blobs, B erosion)", 2D) = "gray" {}
        _ClearColor ("Clear Water Tint", Color) = (0.62, 0.86, 0.9, 1)
        _WhiteColor ("Aerated White Water", Color) = (0.93, 0.97, 1, 1)
        _StreakScale ("Streak Tiling (x across, y along)", Vector) = (1.0, 1.1, 0, 0)
        _Speed ("Fall Speed (v per second)", Float) = 1.2
        _VPower ("Acceleration (v power, < 1 = faster lower down)", Range(0.3, 1)) = 0.6
        _ClearUntil ("Clear Lip Until (v)", Range(0, 1)) = 0.3
        _WhiteFrom ("Fully White From (v)", Range(0, 1)) = 0.95
        _FoamBottom ("Foam At Bottom (v from the end)", Range(0, 0.5)) = 0.16
        _FoamTop ("White Lip Line (v)", Range(0, 0.2)) = 0.04
        _Refraction ("Refraction Of The Wall Behind (screen UV)", Range(0, 0.06)) = 0.025
        _ClearAlpha ("Clear Water Opacity", Range(0, 1)) = 0.32
        _WhiteAlpha ("White Water Opacity", Range(0, 1)) = 0.9
        _EdgeSoft ("Side Fade Width (u)", Range(0.01, 0.5)) = 0.16
        _EdgeBreak ("Side Tearing (erosion noise)", Range(0, 1)) = 0.5
        _Glow ("White Water Self Light", Range(0, 1)) = 0.35
        _Wobble ("Vertex Wobble (m, along the normal)", Range(0, 0.1)) = 0.012
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "RenderType" = "Transparent" }
        Pass
        {
            Name "WaterFalls"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTex_ST;
                half4 _BaseColor, _ClearColor, _WhiteColor;
                float4 _StreakScale;
                float _Speed, _VPower, _ClearUntil, _WhiteFrom, _FoamBottom, _FoamTop, _Refraction, _ClearAlpha, _WhiteAlpha, _EdgeSoft, _EdgeBreak, _Glow, _Wobble;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; half fogFactor : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float t = _Time.y * _Speed;
                float w = SAMPLE_TEXTURE2D_LOD(_MaskTex, sampler_MaskTex, float2(v.uv.x * 1.3, v.uv.y * 0.8 - t * 0.6), 0).b;
                float3 pos = v.positionOS.xyz + v.normalOS * (w - 0.5) * 2 * _Wobble * (0.2 + 0.8 * v.uv.y);
                o.positionWS = TransformObjectToWorld(pos);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y * _Speed;
                float va = pow(max(uv.y, 0), _VPower);
                float2 a = float2(uv.x * _StreakScale.x, va * _StreakScale.y - t);
                float2 b = float2(-uv.x * _StreakScale.x * 1.37 + 0.31, va * _StreakScale.y * 0.83 - t * 1.31);
                float s1 = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, a).r;
                float s2 = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, b).r;
                float n = (s1 + s2) * 0.5;
                // 결 기울기 (굴절 · 빛 마루용): u 방향 차분
                float e = 0.02;
                float nR = (SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, a + float2(e, 0)).r + SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, b - float2(e * 1.37, 0)).r) * 0.5;
                float dn = (nR - n) / e;
                float blobs = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, float2(uv.x * 2.5, va * 2.0 - t * 0.7)).g;
                float ero = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, float2(uv.x * 1.7 + 0.5, va * 1.1 - t * 0.8)).b;

                // 공기 섞임: 턱은 맑게, 아래로 갈수록 흰 물 (결이 굵은 곳 먼저), 바닥 띠 · 턱 선은 거품
                // 넓은 결 (물줄기 가닥) · 공기 덩어리 — 가닥마다 하얘지는 높이가 달라 커튼처럼 고르지 않게
                float lane = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, float2(uv.x * 0.35 + 0.13, va * 0.25 - t * 0.35)).r;
                float clump = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, float2(uv.x * 1.2 + 0.7, va * 1.6 - t * 0.9)).g;
                float aer = smoothstep(_ClearUntil, _WhiteFrom, uv.y + (n - 0.5) * 0.5 + (lane - 0.5) * 0.6 + clump * 0.25);
                aer = saturate(aer + smoothstep(1 - _FoamBottom, 1, uv.y) * (0.5 + blobs));
                aer = max(aer, (1 - smoothstep(0, _FoamTop, uv.y)) * 0.8);
                // 맑은 물: 뒤가 결 따라 휘어 비침
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 ruv = suv + float2(dn * _Refraction * 0.05, (n - 0.5) * _Refraction) * (1 - aer);
                half3 behind = SampleSceneColor(saturate(ruv));
                float3 N = normalize(i.normalWS) * (front ? 1 : -1);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light L = GetMainLight();
                half3 ambient = SampleSH(N);
                half3 lit = ambient + L.color * saturate(dot(N, L.direction) * 0.5 + 0.5) * 0.6;
                half3 clearCol = lerp(behind, behind * _ClearColor.rgb * 1.1, 0.6);
                // 결 마루의 가는 흰 빛 (맑은 물에서도 물줄기로 보이게)
                float crest = smoothstep(0.62, 0.78, n) * (1 - aer) * 0.55;
                clearCol += _WhiteColor.rgb * crest * lit;
                half3 white = _WhiteColor.rgb * (lit + _Glow) * (0.85 + 0.25 * n);
                half3 col = lerp(clearCol, white, aer);
                // 비스듬히 보는 얇은 물 가장자리 빛
                col += _WhiteColor.rgb * pow(1 - saturate(abs(dot(N, V))), 3) * 0.15 * lit;
                half alpha = lerp(_ClearAlpha, _WhiteAlpha, aer) + crest * 0.3;
                // 양옆: 찢김 잡음으로 녹듯 사라짐
                float edge = min(uv.x, 1 - uv.x) / max(_EdgeSoft, 1e-3);
                alpha *= saturate(edge * 1.6 - 0.3 + (ero - 0.5) * 2 * _EdgeBreak);
                // 웅덩이 · 바위와 만나는 곳 부드럽게 (경계선이 딱 끊기지 않게)
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float fragEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                alpha *= saturate((sceneEye - fragEye) / 0.12);
                col *= _BaseColor.rgb;
                return half4(MixFog(col, i.fogFactor), saturate(alpha));
            }
            ENDHLSL
        }
    }
}
