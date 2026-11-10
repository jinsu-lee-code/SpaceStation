# KWS Water System 수정 기록 (3단계 내부 물)

`SpaceStation/Assets/KriptoFX` (KWS Water System 1.5.9a, 사용자가 멘토링으로 받은 실사 물 패키지)는 재배포 방지로 git에 올리지 않는다 (`.gitignore`).
패키지를 다시 받거나 다른 PC에 넣으면 아래 수정이 사라지므로, 이 문서대로 다시 넣는다. 수정이 없어도 물은 나오지만 반사가 원래 물리 값(위에서 보면 3 %)이 되고 우주 하늘 대신 장면 환경 반사가 비친다.

## 1. 반사 세기 — `WaterResources/Shaders/Resources/Common/KWS_WaterHelpers.cginc`

사용자 2026-11-11 "물 표면 반사를 더 높여 — 우주 하늘이 비치게". `ComputeWaterFresnel` 바로 위에 전역 값을 두고, 함수 끝을 섞기로 바꾼다.

```hlsl
float KWS_SS_ReflectionBoost;   // 0 = 원래 물리 반사, 1 = 위에서도 하늘이 비침

float ComputeWaterFresnel(half3 normal, half3 viewDir)
{
	float x = 1 - saturate(dot(normal, viewDir));
	float f = 0.03 + 0.97 * x * x * x * x * x;
	return lerp(f, 0.4 + 0.6 * x * x, saturate(KWS_SS_ReflectionBoost));
}
```

## 2. 물에만 쓰는 하늘 — `WaterResources/Shaders/Resources/PlatformSpecific/KWS_PlatformSpecificHelpers.cginc`

`KWS_GetSkyColor` (화면 반사가 비는 곳에 쓰는 하늘) 위에 전역 값을 두고, 함수 맨 앞에서 켜져 있으면 우리 큐브맵을 읽는다.
장면 환경 반사(진주 벽 · 금 테가 같이 씀)는 그대로 두고 물만 바꾸기 위함 — 반사 탐침으로 하면 벽까지 붉게 물들었음.

```hlsl
TextureCube KWS_SS_SkyCube;
float KWS_SS_SkyCubeOn;
float KWS_SS_SkyCubeIntensity;
float KWS_SS_SkyCubeFlatten;   // 0 = 그대로, 1 = 위로 향한 반사도 지평선 높이를 비춤

inline float3 KWS_GetSkyColor(float3 reflDir, float lod, float exposure)
{
	UNITY_BRANCH
	if (KWS_SS_SkyCubeOn > 0.5)
	{
		float3 d = normalize(float3(reflDir.x, reflDir.y * (1 - saturate(KWS_SS_SkyCubeFlatten)), reflDir.z));
		return max(0, KWS_SS_SkyCube.SampleLevel(sampler_trilinear_clamp, d, min(lod, 0.75)).xyz) * KWS_SS_SkyCubeIntensity;   // 별 · 성운이 뭉개지지 않게 선명한 단계로
	}
	// (원래 코드 이어짐: OverrideSkyColor · _GlossyEnvironmentCubeMap)
```

## 쓰는 쪽 (Unity 시험 값, 2026-11-11)

- 큐브맵 = 내부 하늘 면(`INT_<모듈>_Sky`, InteriorWindow 셰이더의 성운)만 보이는 카메라로 내부 가운데에서 `RenderToCubemap` (512, HDR, 밉) — Unity 스카이박스(`Purple_2K`)는 내부에서 보이는 성운과 다르고 위쪽이 어두움.
- `KWS_SS_SkyCubeOn` 1 · `KWS_SS_SkyCubeIntensity` 1.6 · `KWS_SS_SkyCubeFlatten` 0 · `KWS_SS_ReflectionBoost` 0.85, 화면 반사(SSR) 켬 · 평면 반사 끔.
- 내부를 나갈 때 `KWS_SS_SkyCubeOn` · `KWS_SS_ReflectionBoost`를 0으로 되돌린다 (다른 곳 물이 생기면 영향 없게).
