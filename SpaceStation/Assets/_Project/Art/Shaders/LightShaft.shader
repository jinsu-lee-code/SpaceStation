// 궤도 미러 빛기둥 (사용자 2026-10-27 "선처럼 보임 → 진짜 빛줄기", 2026-11-02 "그냥 선같아보여서 반사광 느낌이 안나" · "해바라기 표면에서 명확하게 시작"): LightBeamFx가 만든 3D 빛기둥.
// 시선과 나란한 가운데가 밝고 윤곽으로 갈수록 사라져 (법선과 시선) 어느 쪽에서 봐도 부드러운 빛기둥, 길이로 뻗은 빛 결, 거울 쪽이 환한 반사 지점, 수신기 쪽만 스며듦.
// 거울 쪽 끝은 스며들지 않음: 빛기둥 아래 테두리가 거울 테두리와 같고 거울 면(uv.x < 0)도 빛나 시작점이 또렷함.
// 조절은 재질 슬라이더로 (사용자 2026-11-02 "광량이 과함 — 감쇄 효과를 조절할 수 있게"): 재질 값은 플레이 중에 바꿔도 바로 보이고 저장됨. 바깥 = M_LightShaft, 내부 = M_InteriorLightShaft.
// UV: x = 길이 (0 거울 ~ 1 수신기), y = 둘레 (0 ~ 1). 가산. _Level = 모듈 상태 (LightBeamFx).
Shader "SpaceStation/LightShaft"
{
    Properties
    {
        [HDR] _BaseColor ("빛 색", Color) = (1.5, 1.3, 0.85, 1)
        _Intensity ("광량 (전체 밝기)", Range(0, 2)) = 0.2
        _Attenuation ("감쇠 (거울에서 멀어질수록 어두워짐)", Range(0, 8)) = 1
        _Glint ("반사 지점 (거울 쪽을 더 밝게)", Range(0, 5)) = 1.2
        _MirrorGlow ("거울 면 빛", Range(0, 2)) = 0.35
        _Softness ("윤곽 부드러움", Range(0.5, 6)) = 2
        _Streak ("빛 결 잘기 (작을수록 줄이 적고 넓음)", Range(0.2, 3)) = 0.8
        _StreakStrength ("빛 결 진하기", Range(0, 1)) = 0.7
        _Speed ("흐름 속도", Float) = 0.6
        _Noise ("흐르는 밝기 잘기", Float) = 7
        [HideInInspector] _Level ("Level", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "LightShaft"
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
                half4 _BaseColor;
                float _Intensity;
                float _Attenuation;
                float _Glint;
                float _MirrorGlow;
                float _Softness;
                float _Streak;
                float _StreakStrength;
                float _Speed;
                float _Noise;
                float _Level;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 positionWS : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 toCamera = _WorldSpaceCameraPos - i.positionWS;
                float distance = length(toCamera);
                float near = saturate((distance - 0.35) / 1.4);                             // 빛기둥 속을 지날 때 눈앞을 덮지 않게 (내부)
                if (i.uv.x < 0.0)                                                           // 거울 면: 가운데가 조금 더 환한 원판 (빛이 여기서 나옴)
                    return half4(_BaseColor.rgb * (_MirrorGlow * _Level * near * (1.0 - 0.4 * i.uv.y * i.uv.y)), 1);
                float facing = abs(dot(normalize(i.normalWS), toCamera / max(distance, 1e-4)));
                float body = pow(facing, _Softness);                                        // 넓고 옅은 몸통 (윤곽에서 사라짐)
                float core = body * body * body;                                            // 가운데 심
                float fade = exp(-i.uv.x * _Attenuation) * smoothstep(1.0, 0.86, i.uv.x);   // 감쇠 + 수신기 쪽 끝이 스며듦
                float angle = i.uv.y * 6.28318;
                float grain = StationNoise3(float3(cos(angle) * _Streak, sin(angle) * _Streak, i.uv.x * 1.3 - _Time.y * _Speed * 0.12));
                float streak = lerp(1.0, 0.4 + 1.2 * grain, _StreakStrength);               // 길이로 뻗은 빛 결
                float motes = StationNoise3(float3(i.uv.x * _Noise - _Time.y * _Speed, cos(angle) * 1.5, sin(angle) * 1.5 + _Time.y * 0.3));
                float flow = 0.7 + 0.3 * motes;                                             // 천천히 흐르는 밝기
                float glint = 1.0 + _Glint * exp(-i.uv.x * 10.0);                           // 거울 쪽 = 환한 반사 지점
                float light = (body * 0.55 + core * 0.45) * streak * flow * glint * fade * near * _Level * _Intensity;
                return half4(_BaseColor.rgb * light, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
