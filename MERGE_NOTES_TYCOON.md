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

- **13-3 (2026-10-15)**
  - `SimulationHost`: `_plague` 필드 + 있으면 `PlagueSystem.AttachTo`
  - 데이터(에셋): `StationGrades` 도시급 새 요구 교육 · 검역, `MD_School` · `MD_Quarantine` 준비 중 해제 (main `NewModulesSetup`을 다시 돌리면 되돌아감 → `Setup Needs` 다시)
  - 역병 = 타이쿤판 시스템(`PlagueSystem`, 사회 위기 틀과 따로). 서사판도 역병이 필요하면 main으로 옮김

- **13-4 (2026-10-15)**
  - 측정 도구: `BalanceBotExpansion.DecideExpansion` — 규칙을 정하는 쪽이 있으면 '합성 1단계' 규칙으로 시작 (없으면 도시급, 동작 같음)
  - 데이터(에셋): `ResearchExpansionConfig._shadowLevelOffset` 4 → 1 (모드별 설정으로 나눠야 함)
  - main을 합칠 때 `Main.unity`가 자동 병합되더라도 SpaceStation/Recipes/Setup → Tycoon 메뉴 4개(Unlock Choices · Growth Threat · Needs · Synthesis)를 다시 돌려 씬 연결을 맞춤

- **13-5 (2026-10-15)**
  - 데이터(에셋): `StationGrades` 해금 등급 — 연결 허브 · 구조 프레임 → 중형, 물 추출기 · 단백질 배양실 · 손상 통제실 · 화물 터미널 → 대형, 데이터 센터 · 여객 도킹 포트 → 도시, 국가급 새 요구 소비 · 행정 + 상업 구역 · 행정 관제소 준비 중 해제 (모드별 설정으로 나눠야 함)

- **13-6 (2026-10-16)**
  - `SimulationHost`: `_interiorBonus` 필드 + 있으면 `InteriorBonusSystem.AttachTo` (main 고리 M-7 `IncidentEffects.MissScale` · `SupplyCrateSystem.AutoCollectRate`에 값을 정함)
  - 측정 도구: `BalanceRunSettings.InteriorBonus` · 창 `_interiorBonus` (Tycoon 폴더 에셋이 있으면 자동)
  - 1인칭 가볍게 = 타이쿤판 시스템. 서사판이 같은 처리가 필요하면 고리 값만 정하면 됨

- **13-6a (2026-10-16)**
  - `UnlockChoicePanel`: 왼쪽 위 [시스템] 버튼 대신 main의 `HudLauncher`에 등록 (Order.Systems · 아이콘 "systems" · 고를 후보 = Attention)

- **13-7 (2026-10-16)**
  - `GrowthThreatSystem.AttachTo(sim, config, baseGrades)` — 난이도 등급표 ÷ 원래 등급표 = 간격 배율을 성장 곡선에 곱함. `SimulationHost`는 난이도가 있을 때 `_grades`(원래)를 넘김
  - 측정 도구: `BalanceRunSettings.BaseGrades`(난이도 측정 때 `RunWithDifficulty`가 채움) · `MidGrade`, 창 `_midGrade`
  - `SimulationHost`: `_midGrade` 필드 + 있으면 `MidGradeSystem.AttachTo` (main 고리 M-12 `StationProgression.ExtraUnlock`에 값을 정함)
  - 데이터(에셋): 해금 선택 열 ↔ 유성우 교환 (중형 = 합성 1단계 · 열, 대형 = 유성우) · `MidGradeConfig` 중형 확장
