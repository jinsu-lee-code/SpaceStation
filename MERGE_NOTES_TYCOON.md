# MERGE_NOTES_TYCOON.md — 타이쿤판이 공통 바닥 파일에 더한 줄

(`tycoon` 전용 문서. main의 [MERGE_NOTES.md](MERGE_NOTES.md)와 따로 두어 main을 합칠 때 충돌이 나지 않게 함)

## T. 타이쿤판이 공통 바닥 파일에 더한 줄 (두 모드를 한 게임으로 합칠 때 참고)

`tycoon`에서 바닥 파일을 직접 고친 곳. 모두 **"설정이 있을 때만"** 동작하는 덧붙임이라 서사판 동작은 바뀌지 않는다. 합칠 때 그대로 가져가면 된다.

- **13-1 (2026-10-15)**
  - `SimulationHost`: `_unlockChoices` 필드 + 있으면 `UnlockChoiceSystem.AttachTo` (복원 전)
  - 측정 도구: `BalanceRunSettings.UnlockChoices` · `BalanceRunResult.Choices` · `BalanceRun`(시드 무작위 선택) · `BalanceSimulatorWindow`(기본 에셋 `Data/Tycoon/UnlockChoiceConfig`) · 요약 끝에 경로 줄
  - UI 문구: `PolicyPanel` · `RecipePanel` · `ContactPanel` — `Sim.Rules`가 있으면 "'…' 시스템이 열리면" (서사판은 장 틀이 `Rules`가 되므로 같은 문구가 나옴 → 합칠 때 장 기준 문구가 필요하면 그때)
  - `EventBanner`: 해금 선택 시스템이 있으면 "새 시스템: …" 배너
  - `LoadingTips`: 유성우 · 정책 · 열 줄을 타이쿤판 문구로 (합칠 때 모드별로 나눠야 함)
  - 데이터(에셋): `PolicyConfig` 슬롯 표 `0,1,1,1,1,1,1,1` + 확장 2, `StationGrades` 센서 어레이 · 방열판 · 통신 중계소 → 중형, 이 셋의 `RequiredRule` (합칠 때 모드별 설정으로 나눠야 함)

- **13-2 (2026-10-15)**
  - `SimulationHost`: `_growthThreat` 필드 + 있으면 `GrowthThreatSystem.AttachTo` (이벤트 규모 · 파업 기준 보정 고리에 연결)
  - 측정 도구: `BalanceRunSettings.GrowthThreat` · `BalanceRun`(붙이기) · `BalanceSimulatorWindow`(기본 에셋 `Data/Tycoon/GrowthThreatConfig`)
  - UI: `UnlockChoicePanel` 위협 규모 줄(타이쿤 전용 파일) · `PolicyPanel` 파업 경고에 밀집도 (성장 비례 시스템이 있을 때만)
