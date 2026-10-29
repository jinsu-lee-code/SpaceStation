// 모듈 유리 (2026-10-27 사용자 "유리에 멍울처럼 생기는 부분" · "유리가 있다는 정도만 인식되면"): 양면으로 그리는 반투명 유리.
// URP Lit(양면)은 뒷면에서 법선을 뒤집지 않아 유리 껍질 안쪽 뒷면이 시선과 반대 법선으로 그려지고, 프레넬이 최대(스치는 각)로 계산돼
// 하늘 반사가 뿌연 흰 뭉치로 겹쳐 보였다 → 뒷면은 법선을 뒤집어 앞면처럼 비춘다 (조명 계산 = URP PBR, 상태 표현 MPB _BaseColor · _EmissionColor 유지, SRP Batcher 호환).
// 한쪽 면만 있는 얇은 유리(보관 단지 · 연구 기관 등)도 양쪽에서 보이도록 앞면만 그리기(Cull Back) 대신 이 방식.
// 반사 보존(미리 곱한 알파): 유리 바탕색(확산광)만 알파만큼 옅게 하고, 해 반짝임 · 하늘 반사는 그대로 더함 → 거의 투명한데 빛이 닿는 곳에서 유리가 있다는 게 보임.
// 진화 3 · 4단계 반영 ④ (2026-10-29) — 물 · 수정 움직임 (FX 메시에 UV가 없어 위치로 계산, 키워드로 켬):
//  - _FLOW_FALL (폭포): 가파른 면에 흰 물줄기가 아래로 흘러내림 (칸마다 다른 박자, 평평한 면은 그대로)
//  - _FLOW_POOL (호수 · 강 · 연못): 엇갈려 흐르는 잔물결이 위 면 법선을 흔들어 해 반짝임이 일렁이고, 물결이 겹치는 곳에 윤슬
//  - _GLINT (수정 · 프리즘): 자리마다 다른 박자로 도는 가상 빛이 면을 스칠 때 무지개빛으로 반짝
Shader "SpaceStation/Glass"
{
    Properties
    {
        _BaseColor ("Base Color (알파 = 바탕색 진하기)", Color) = (0.82, 0.86, 1, 0.05)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.92
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Reflection ("하늘 반사 · 주변광 세기 (해 반짝임은 그대로)", Range(0, 1)) = 1
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)
        [Toggle(_FLOW_FALL)] _FlowFall ("Falling Water", Float) = 0
        [Toggle(_FLOW_POOL)] _FlowPool ("Rippling Water", Float) = 0
        [Toggle(_GLINT)] _Glint ("Prism Glint", Float) = 0
        _Flow ("Fall: 줄 촘촘함 · 속도 · 칸 크기 · 물거품 세기", Vector) = (14, 0.9, 0.02, 0.5)
        _Ripple ("Ripple: 물결 촘촘함 · 속도 · 흔들림 · 윤슬 세기", Vector) = (70, 1.0, 0.1, 0.18)
        _Ripple2 ("Ripple: 가장 밝은 빛 (해 되비침 상한) · - · - · -", Vector) = (1.4, 0, 0, 0)
        _GlintParams ("Glint: 세기 · 도는 빠르기 · 날카로움 · 무지개", Vector) = (1.2, 0.35, 24, 0.7)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _FLOW_FALL
            #pragma shader_feature_local _FLOW_POOL
            #pragma shader_feature_local _GLINT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Smoothness;
                half _Metallic;
                half _Reflection;
                half4 _EmissionColor;
                float _FlowFall;
                float _FlowPool;
                float _Glint;
                float4 _Flow;
                float4 _Ripple;
                float4 _Ripple2;
                float4 _GlintParams;
            CBUFFER_END

            float GlassHash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            float3 GlassHue(float h)
            {
                return saturate(abs(frac(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.normalWS + float3(0, 1e-5, 0)); // 법선 0 (찌그러진 면)에서 NaN 막기
                n = front ? n : -n; // 뒷면 = 안쪽에서 본 유리
                float t = _Time.y;
                float3 extra = 0;
                float alphaAdd = 0;
            #if defined(_FLOW_POOL)
                {
                    // 엇갈려 흐르는 잔물결 둘 → 위 면 법선 흔들기 (해 반짝임이 일렁임) + 물결이 겹치는 곳에 윤슬
                    float2 p = i.positionWS.xz;
                    float f = _Ripple.x, s = _Ripple.y;
                    float2 d1 = float2(0.8, 0.6), d2 = float2(-0.45, 0.89);
                    float a1 = dot(p, d1) * f + t * s, a2 = dot(p, d2) * f * 1.37 - t * s * 0.8;
                    float2 slope = d1 * cos(a1) + d2 * cos(a2) * 1.37;
                    float up = saturate(n.y * 2.0 - 1.0);
                    n = normalize(n + float3(slope.x, 0, slope.y) * _Ripple.z * up);
                    float glitter = pow(saturate(0.5 + 0.5 * sin(dot(p, float2(0.31, 0.95)) * f * 2.1 + t * s * 1.3) * sin(dot(p, float2(0.97, -0.24)) * f * 1.7 - t * s)), 36.0);
                    extra += glitter * _Ripple.w * up;
                }
            #endif
            #if defined(_FLOW_FALL)
                {
                    // 가파른 면에만 흰 물줄기가 아래로 (칸마다 다른 박자 — 같은 높이가 한꺼번에 번쩍이지 않게)
                    float steep = saturate(1.0 - abs(n.y) * 1.3);
                    float h = GlassHash(floor(i.positionWS.xz / max(_Flow.z, 1e-3)));
                    float s = frac(i.positionWS.y * _Flow.x + t * _Flow.y + h);
                    float foam = smoothstep(0.72, 1.0, s) * steep;
                    extra += foam * _Flow.w;
                    alphaAdd += foam * 0.3;
                }
            #endif
            #if defined(_GLINT)
                {
                    // 자리마다 다른 박자로 도는 가상 빛 — 면이 그쪽을 볼 때 반짝 (색 = 면 방향에 따른 무지개빛, 흰빛과 섞음)
                    float ang = t * _GlintParams.y * 6.2832 + dot(i.positionWS.xz, float2(1.7, 2.3)) + i.positionWS.y * 3.1;
                    float3 L = normalize(float3(cos(ang), 0.7, sin(ang)));
                    float g = pow(max(saturate(dot(n, L)), 1e-6), max(_GlintParams.z, 1.0)); // 지수 0 · 밑 0에서 NaN 막기
                    float3 tint = lerp(float3(1, 1, 1), GlassHue(dot(n, float3(0.3, 0.9, 0.2)) * 2.0 + t * 0.15), _GlintParams.w);
                    extra += g * _GlintParams.x * tint;
                }
            #endif

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.positionCS = i.positionCS;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fogFactor;
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb * _BaseColor.a; // 미리 곱한 알파: 바탕색만 옅게 (반사 · 반짝임은 금속도 0이라 바탕색과 무관)
                surface.alpha = 1.0h;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                half lit = saturate(max(_EmissionColor.r, max(_EmissionColor.g, _EmissionColor.b)) * 8.0 + 0.2); // 비활성 · 파손 때 발광을 어둡게 하면 움직임도 같이 어두워짐
                surface.emission = _EmissionColor.rgb + extra * lit;
                surface.occlusion = _Reflection; // 간접광(하늘 반사 · 주변광)만 줄임

                half4 color = UniversalFragmentPBR(inputData, surface);
            #if defined(_FLOW_POOL)
                // 흔들린 물 법선이 해를 거울처럼 되비추면 밝기가 수백까지 튀어 블룸으로 화면이 번쩍임 (사용자 2026-10-29 플레이 — 궤도 항만) → 윤슬은 은은한 빛까지만
                color.rgb = min(color.rgb, _Ripple2.x);
            #endif
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.rgb = min(max(color.rgb, 0.0), 64.0); // 혹시 모를 NaN · 무한대가 블룸으로 번지지 않게 (max는 NaN을 버림)
                color.a = saturate(_BaseColor.a + alphaAdd); // 뒤가 비치는 정도 (Blend One OneMinusSrcAlpha)
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
