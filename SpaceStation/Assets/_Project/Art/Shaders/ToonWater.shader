// 내부 물 (궤도 도시 강 · 연못 · 폭포 웅덩이, 사용자 2026-11-11 "물 텍스쳐 다른 유니티 프로젝트 예시 참고해서 만들어볼래"):
// NevrianGlow 프로젝트 DSEWA/ToonLakeWater (밤 호수)의 틀을 낮 정원 물로 옮김.
//  - 물색 = 깊이: 화면 깊이로 이 픽셀 아래 바닥 위치를 되살려 (물 면 높이 − 바닥 높이) 얕은 청록 → 깊은 푸른 남색, 얕을수록 바닥이 비침
//  - 바닥 물빛 무늬 (코스틱): 되살린 바닥 위치에 시간 축 3D 보로노이 F2 − F1 선, 얕은 곳만, 두 단계로 끊어 만화처럼
//  - 흐름: 정점 색 (rg = 물체 공간 흐름 xz × 0.5 + 0.5, b = 0.5 표지) — 강은 섬을 돌아, 폭포 물길은 아래로. 두 박자 섞기 (흐름 지도)
//  - 잔물결: 흐르는 잡음의 기울기를 빛 쪽 · 반대쪽 밝고 어두운 면으로 끊고, 같은 높이 선을 물결 마루 선으로
//  - 윤슬: 시간 축 3D 보로노이의 작은 점 (흰빛), 화면에서 몇 픽셀보다 작아지면 사라짐 (멀리서 깜빡이는 점선 방지)
//  - 거품: 물가 띠 (숨쉬듯 굵기가 변함) · 물가로 밀려오는 물결 선 둘 · 물에 꽂힌 돌 · 연꽃 · 백조 · 폭포 둘레 (시선 방향 깊이 차)
//  - 비스듬히 볼수록 하늘빛, 바닥은 잔물결 따라 살짝 굴절 (불투명 화면 텍스처를 직접 섞고 불투명으로 출력)
// 밤 · 달 · 물결 시뮬레이션 · 반사 탐침 · 평면 반사는 뺐음 (안은 낮 정원, 물이 작음). PC_RPAsset의 깊이 · 불투명 텍스처가 켜져 있어야 함.
Shader "SpaceStation/ToonWater"
{
    Properties
    {
        [HDR] _BaseColor ("Tint (State)", Color) = (1, 1, 1, 1)
        [Header(Body)]
        _ShallowColor ("Shallow", Color) = (0.38, 0.84, 0.8, 1)
        _DeepColor ("Deep", Color) = (0.04, 0.3, 0.5, 1)
        _DepthMax ("Depth For Full Deep Color (m)", Range(0.05, 5)) = 0.45
        _AlphaShallow ("Opacity At Shore", Range(0, 1)) = 0.3
        _AlphaDeep ("Opacity Deep", Range(0, 1)) = 0.88
        _ReceiveLight ("Receive Scene Light", Range(0, 1)) = 0.35
        [Header(Flow and ripples)]
        _FlowSpeed ("Flow Speed (m per second at full flow)", Float) = 0.6
        _FlowCycle ("Flow Cycle (s)", Float) = 2.5
        _RippleScale ("Ripple Scale (per metre)", Float) = 1.6
        _RippleChurn ("Still Water Churn Speed", Float) = 0.12
        _RippleLightDir ("Ripple Light Direction (world xz)", Vector) = (0.6, 0, 0.8, 0)
        _RippleBand ("Ripple Light / Dark Faces", Range(0, 0.5)) = 0.12
        _RippleCut ("Ripple Face Threshold (slope)", Range(0.01, 2)) = 0.35
        _CrestColor ("Crest Line", Color) = (0.86, 0.98, 1, 1)
        _CrestAmount ("Crest Lines", Range(0, 1)) = 0.45
        _CrestLevel ("Crest Line Height", Range(0, 1)) = 0.68
        _CrestWidth ("Crest Line Width", Range(0.005, 0.2)) = 0.02
        [Header(Bed caustics)]
        [HDR] _CausticColor ("Caustic", Color) = (0.9, 1, 1, 1)
        _CausticScale ("Caustic Cells Per Metre", Float) = 0.9
        _CausticSpeed ("Caustic Morph Speed", Float) = 0.25
        _CausticWidth ("Caustic Line Width", Range(0.01, 0.4)) = 0.045
        _CausticStrength ("Caustic Strength", Range(0, 2)) = 0.3
        _CausticDepth ("Caustics Gone At Depth (m)", Range(0.05, 3)) = 0.65
        _CausticPatch ("Caustic Patchiness (slow noise)", Range(0, 1)) = 0.75
        [Header(Glints)]
        [HDR] _GlintColor ("Glint", Color) = (1.6, 1.6, 1.5, 1)
        _GlintScale ("Glint Cells Per Metre", Float) = 4.5
        _GlintSize ("Glint Size", Range(0, 0.5)) = 0.07
        _GlintSpeed ("Glint Twinkle Speed", Float) = 0.35
        [Header(Foam)]
        _FoamColor ("Foam", Color) = (0.97, 1, 1, 1)
        _FoamWidth ("Shore Foam Width (m of depth)", Range(0.001, 0.3)) = 0.035
        _FoamBreath ("Shore Foam Breathing", Range(0, 1)) = 0.6
        _FoamWaveReach ("Wave Line Reach (m of depth)", Range(0.01, 1)) = 0.16
        _FoamWaveSpeed ("Wave Lines Per Second", Range(0, 1)) = 0.22
        _FoamWaveWidth ("Wave Line Width (m of depth)", Range(0.001, 0.1)) = 0.012
        _FoamWaveBreak ("Wave Line Gaps", Range(0, 1)) = 0.42
        _ContactFoam ("Contact Foam (m along the view)", Range(0, 0.6)) = 0.12
        [Header(Sky and refraction)]
        _SkyColor ("Sky Seen At Grazing Angles", Color) = (0.78, 0.92, 1, 1)
        _FresnelStrength ("Fresnel Strength", Range(0, 1)) = 0.55
        _FresnelPower ("Fresnel Power", Range(1, 10)) = 4
        _Refraction ("Bed Refraction (screen UV)", Range(0, 0.05)) = 0.01
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-40" "RenderType" = "Transparent" }
        Pass
        {
            Name "ToonWater"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _ShallowColor, _DeepColor, _CrestColor, _CausticColor, _GlintColor, _FoamColor, _SkyColor;
                float4 _RippleLightDir;
                float _DepthMax, _AlphaShallow, _AlphaDeep, _ReceiveLight;
                float _FlowSpeed, _FlowCycle, _RippleScale, _RippleChurn, _RippleBand, _RippleCut, _CrestAmount, _CrestLevel, _CrestWidth;
                float _CausticScale, _CausticSpeed, _CausticWidth, _CausticStrength, _CausticDepth, _CausticPatch;
                float _GlintScale, _GlintSize, _GlintSpeed;
                float _FoamWidth, _FoamBreath, _FoamWaveReach, _FoamWaveSpeed, _FoamWaveWidth, _FoamWaveBreak, _ContactFoam;
                float _FresnelStrength, _FresnelPower, _Refraction;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 flowWS : TEXCOORD1; half fogFactor : TEXCOORD2; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                // 흐름: 표지 (b = 0.5)가 있는 정점 색만 (색 없는 메시는 흰색 · 검정으로 들어와 흐름 0)
                float2 f = abs(v.color.b - 0.5) < 0.1 ? v.color.rg * 2 - 1 : float2(0, 0);
                o.flowWS = TransformObjectToWorldDir(float3(f.x, 0, f.y), false).xz;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float3 Hash33(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453123);
            }
            float Noise3(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash33(i).x, Hash33(i + float3(1, 0, 0)).x, f.x), lerp(Hash33(i + float3(0, 1, 0)).x, Hash33(i + float3(1, 1, 0)).x, f.x), f.y),
                            lerp(lerp(Hash33(i + float3(0, 0, 1)).x, Hash33(i + float3(1, 0, 1)).x, f.x), lerp(Hash33(i + float3(0, 1, 1)).x, Hash33(i + float3(1, 1, 1)).x, f.x), f.y), f.z);
            }
            // 3D 보로노이: x = 부드러운 F1, y = F1, z = F2
            float3 Voronoi3(float3 p, float smoothness)
            {
                float3 i = floor(p), f = frac(p);
                float f1 = 8, f2 = 8, sm = 0, k = 1.0 / max(smoothness * 0.25, 1e-3);
                [unroll] for (int z = -1; z <= 1; z++) [unroll] for (int y = -1; y <= 1; y++) [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 g = float3(x, y, z);
                    float d = length(g + Hash33(i + g) - f);
                    sm += exp(-k * d);
                    if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) { f2 = d; }
                }
                return float3(-log(max(sm, 1e-6)) / k, f1, f2);
            }
            // 흐르는 잔물결 높이 (한 박자): 흐름만큼 밀린 자리의 잡음, 고인 물은 시간 축으로만 일렁임
            float RippleH(float2 xz, float2 shift, float t, float seed)
            {
                return Noise3(float3((xz - shift) * _RippleScale + seed, t * _RippleChurn));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float rawD = SampleSceneDepth(suv);
                float3 behind = ComputeWorldSpacePosition(suv, rawD, UNITY_MATRIX_I_VP);
                float depthV = max(0, i.positionWS.y - behind.y);   // 이 물 면에서 바닥까지 (단마다 높이가 달라 면 자신의 높이로)
                half3 sceneLight = lerp(half3(1, 1, 1), saturate(SampleSH(half3(0, 1, 0)) * 1.4 + GetMainLight().color * 0.25), _ReceiveLight);
                half dd = saturate(depthV / _DepthMax);
                half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, smoothstep(0, 1, dd)) * sceneLight;
                half alpha = lerp(_AlphaShallow, _AlphaDeep, dd);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);

                // 흐름 지도 두 박자: 반 박자 어긋난 두 무늬를 삼각 무게로 섞어 이음매 없이 계속 흐름
                float2 flow = i.flowWS * _FlowSpeed;
                float cyc = max(_FlowCycle, 0.1);
                float f0 = frac(t / cyc), f1 = frac(t / cyc + 0.5);
                float w0 = 1 - abs(1 - 2 * f0);
                float2 xz = i.positionWS.xz;
                float e = 0.06;
                float h0 = RippleH(xz, flow * f0 * cyc, t, 0), h1 = RippleH(xz, flow * f1 * cyc, t, 17.3);
                float2 g0 = float2(RippleH(xz + float2(e, 0), flow * f0 * cyc, t, 0) - h0, RippleH(xz + float2(0, e), flow * f0 * cyc, t, 0) - h0) / e;
                float2 g1 = float2(RippleH(xz + float2(e, 0), flow * f1 * cyc, t, 17.3) - h1, RippleH(xz + float2(0, e), flow * f1 * cyc, t, 17.3) - h1) / e;
                float h = lerp(h1, h0, w0);
                float2 slope = lerp(g1, g0, w0) / max(_RippleScale, 1e-3);

                // 바닥 물빛 무늬: 굴절로 바닥이 시선 쪽으로 얕아 보이게 조금 밀고, 잔물결 기울기로 흔듦
                float2 bedXZ = behind.xz + V.xz / max(V.y, 0.25) * depthV * 0.35 + slope * 0.08;
                float3 cv = Voronoi3(float3(bedXZ * _CausticScale, t * _CausticSpeed), 0.5);
                float cl = cv.z - cv.y;
                float caw = fwidth(cl) + 1e-3;
                half caustic = 1 - smoothstep(_CausticWidth - caw, _CausticWidth + caw, cl);
                half shallow = pow(saturate(1 - depthV / _CausticDepth), 1.6) * smoothstep(0, 0.06, depthV);
                // 군데군데만 (느린 잡음 조각): 고르게 깔리면 칸 무늬 · 타일 바닥처럼 보임 (벌집 무늬 피하기)
                shallow *= lerp(1, smoothstep(0.4, 0.75, Noise3(float3(bedXZ * 0.22, t * 0.04))), _CausticPatch);
                col += _CausticColor.rgb * caustic * shallow * _CausticStrength * sceneLight;

                // 잔물결 면: 빛 쪽으로 기운 면은 밝게, 반대는 어둡게 (두 단계) + 같은 높이 선 = 물결 마루 선 (끊어서)
                float2 Lxz = normalize(_RippleLightDir.xz + 1e-4);
                float facing = -dot(slope, Lxz);
                float fwid = fwidth(facing) + 1e-4;
                float face = smoothstep(_RippleCut - fwid, _RippleCut + fwid, facing) - smoothstep(_RippleCut - fwid, _RippleCut + fwid, -facing);
                col *= 1 + face * _RippleBand;
                float cw = fwidth(h) + 1e-4;
                half crest = 1 - smoothstep(_CrestWidth - cw, _CrestWidth + cw, abs(h - _CrestLevel));
                crest *= step(0.45, Noise3(float3(xz * 0.9, t * 0.2 + 3.7)));
                col = lerp(col, _CrestColor.rgb * sceneLight, crest * _CrestAmount);
                alpha = max(alpha, crest * _CrestAmount * 0.8);

                // 비스듬히 = 하늘빛
                half fres = pow(1 - saturate(V.y), _FresnelPower) * _FresnelStrength;
                col = lerp(col, _SkyColor.rgb * sceneLight, fres);
                alpha = lerp(alpha, 1, fres);

                // 윤슬
                float3 gp = float3((xz + slope * 0.15) * _GlintScale, t * _GlintSpeed);
                float3 gv = Voronoi3(gp, 0.6);
                float gaa = max(fwidth(gv.x), 1e-4);
                float cellPx = 1.0 / max(length(fwidth(gp.xy)), 1e-4);
                half glint = (1 - smoothstep(_GlintSize - gaa, _GlintSize + gaa, gv.x)) * smoothstep(3, 10, cellPx) * smoothstep(0.02, 0.1, depthV);
                col = lerp(col, _GlintColor.rgb, glint);
                alpha = max(alpha, glint);

                // 거품: 물가 띠 (숨쉼) + 밀려오는 물결 선 둘 + 물에 꽂힌 것 둘레
                float nb = Noise3(float3(xz * 0.6, t * 0.25));
                float rimW = max(_FoamWidth * lerp(1, lerp(0.4, 1.6, nb), _FoamBreath), fwidth(depthV) * 1.2);
                half foam = 1 - step(rimW, depthV);
                float phase0 = Noise3(float3(xz * 0.12, 3.1)) * 0.8;
                half gaps = step(_FoamWaveBreak, Noise3(float3(xz * 0.9, t * 0.15 + 7.7)));
                [unroll] for (int w = 0; w < 2; w++)
                {
                    float ph = frac(t * _FoamWaveSpeed + phase0 + w * 0.5);
                    float waveD = _FoamWidth + (1 - ph) * _FoamWaveReach;
                    half wave = 1 - step(_FoamWaveWidth * lerp(0.4, 1, ph), abs(depthV - waveD));
                    foam = max(foam, wave * ph * gaps * step(0.15, ph));
                }
                float eyeScene = LinearEyeDepth(rawD, _ZBufferParams);
                float eyeWater = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float gap = eyeScene - eyeWater;
                float cfw = _ContactFoam * lerp(0.55, 1.25, Noise3(float3(xz * 2.2, t * 0.6)));
                half contact = (1 - step(cfw, gap)) * step(0.3, Noise3(float3(xz * 3.1, t * 0.35 + 4.2)) + 0.25) * step(0.0005, gap);
                foam = max(foam, contact);
                col = lerp(col, _FoamColor.rgb * sceneLight, foam);
                alpha = lerp(alpha, 0.95, foam);

                // 비치는 바닥: 잔물결 따라 조금 굴절해 직접 섞고 불투명으로 냄 (굴절한 자리가 물보다 앞 물체면 굴절 안 함)
                float2 rUV = suv + slope * _Refraction * saturate(depthV * 4);
                if (LinearEyeDepth(SampleSceneDepth(rUV), _ZBufferParams) < eyeWater) rUV = suv;
                half3 behindCol = SampleSceneColor(rUV) * lerp(half3(1, 1, 1), _ShallowColor.rgb * 1.3, 0.35);
                col = lerp(behindCol, col, saturate(alpha)) * _BaseColor.rgb;
                return half4(MixFog(col, i.fogFactor), 1);
            }
            ENDHLSL
        }
    }
}
