// 핵융합 플라즈마 (사용자 2026-10-27 "실제 플라즈마 느낌", 2026-11-02 "내부에 노이즈를 좀 더 넣어서 제대로된 플라즈마 느낌"): 빛나는 기체가 흐르며 소용돌이친다.
// 잡음으로 좌표를 비틀어 (도메인 왜곡) 덩어리가 고르게 뭉치지 않고 휘말리고, 기체가 옅은 곳은 어둡게 비며 (대비), 능선 잡음 두 겹이 가는 빛 실로 지나간다.
// 부품이 돌면(SpinOrbit) 잡음도 같이 돌아 고리를 따라 흐른다. UV가 없는 FX 메시라 물체 공간 위치로 잡음을 만든다. 마주 보는 면(관의 가운데)은 밝은 코어색, 가장자리는 테두리색.
// 가산 (융합 발전소 · 융합 플랜트) 또는 불투명 (재질 값 _DstBlend = Zero, _ZWrite = On — 기본 핵융합로 · 내부: 뒤에 벽이 없는 자리에 쓰임).
// _BaseColor는 ModuleView가 비활성 · 파손 때 MaterialPropertyBlock으로 덮어 어둡게 한다.
Shader "SpaceStation/Plasma"
{
    Properties
    {
        [HDR] _BaseColor ("Tint (State)", Color) = (1, 1, 1, 1)
        [HDR] _CoreColor ("Core", Color) = (0.9, 1.6, 2.6, 1)
        [HDR] _EdgeColor ("Edge", Color) = (0.35, 0.45, 1.8, 1)
        _Scale ("덩어리 크기 (클수록 잘게)", Float) = 14
        _Speed ("흐름 속도", Float) = 1.2
        _Warp ("휘말림", Range(0, 4)) = 2.2
        _Contrast ("대비 (기체가 옅은 곳이 어두움)", Range(0, 1)) = 0.85
        _Detail ("빛 실 잘기", Range(0.5, 8)) = 1.5
        _Filament ("빛 실 밝기", Range(0, 3)) = 0.9
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend (One = 가산, Zero = 불투명)", Float) = 1
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "Plasma"
            Tags { "LightMode" = "UniversalForward" }
            Blend One [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "StationNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _CoreColor;
                half4 _EdgeColor;
                float _Scale;
                float _Speed;
                float _Warp;
                float _Contrast;
                float _Detail;
                float _Filament;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
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
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }

            float Ridge(float3 p)
            {
                return 1.0 - abs(StationNoise3(p) * 2.0 - 1.0);                             // 0.5 근처가 능선 = 가는 실
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y * _Speed;
                float3 p = i.positionOS * _Scale;
                // 도메인 왜곡: 굵은 잡음 셋으로 좌표를 비틀어 소용돌이 · 덩굴
                float3 warp = float3(StationNoise3(p * 0.6 + float3(0.0, t * 0.6, 0.0)), StationNoise3(p * 0.6 + float3(5.2, -t * 0.5, 1.3)), StationNoise3(p * 0.6 + float3(1.7, 9.2, t * 0.4))) - 0.5;
                float3 q = p + warp * _Warp * 2.0;
                float swirl = StationFbm3(q + float3(0.0, t, t * 0.7));                     // 흐르는 기체
                float body = smoothstep(0.5 - 0.5 * _Contrast * 0.6, 0.5 + 0.5 * _Contrast * 0.6, swirl);   // 대비: 옅은 곳은 어둡게 빔
                float r1 = Ridge(q * _Detail - float3(t * 1.6, 0.0, t));
                float r2 = Ridge(q * _Detail * 2.3 + float3(0.0, t * 2.1, -t * 0.8) + swirl * 2.0);
                float filaments = r1 * r1 * r1 * r1 + 0.3 * r2 * r2 * r2 * r2 * r2 * r2 * r2 * r2;            // 굵은 실 + 잔 실 (잔 실이 세면 물결무늬처럼 보임)
                float facing = saturate(abs(dot(normalize(i.normalWS), normalize(i.viewWS))));
                float core = facing * facing;
                float pulse = 0.85 + 0.15 * sin(_Time.y * 4.0 + swirl * 6.0);
                float3 color = lerp(_EdgeColor.rgb, _CoreColor.rgb, saturate(core * 0.45 + body * 0.35 + filaments * _Filament * 0.5));
                float intensity = (0.1 + 1.25 * body * body + filaments * _Filament) * pulse * (0.45 + 0.55 * core);
                return half4(color * intensity * _BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
