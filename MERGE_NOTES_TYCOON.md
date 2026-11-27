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

- **main M-336 주민 추방 공통 고리 받음 (2026-11-27, CONNECT_1121 1번의 엔드리스 몫)** — main을 통째로 합치지 않고 그 커밋만 3-way로 적용
  - `StationRule`: `Exile = 1024`만 (main의 `Radiation = 512`는 타이쿤에 시스템이 없어 값만 비워 둠)
  - `PopulationChangeReason`: `Environment`(main M-122) · `Exiled`를 main과 같은 순서로 (타이쿤에서 `Environment`는 쓰이지 않음 — 번호를 맞추려고)
  - `ResidentRoster.ProblemOf`: main의 `StressAt`(집 스트레스 M-122) 항을 뺌 — 타이쿤엔 그 고리가 없음 (main에서도 정하는 쪽이 없으면 0)
  - 데이터: `PolicyConfig`에 정책 4종 · 추방 값 (슬롯 표 · 확장 2는 타이쿤 그대로), 해금 선택 소형 자동 규칙 = 정책 + 추방 (`UnlockChoiceConfig` · `TycoonSetup`)
  - 두 모드를 합칠 때: main 쪽 파일 그대로 가져가면 됨 (위 셋은 main이 이미 가진 것의 부분집합)

- **CONNECT_1121 1번 광맥 (2026-11-27)** — main M-337 탐사 틀을 커밋째 받음 (새 파일만, 충돌 없음) + 공통 고리 셋을 더해 main으로도 옮김 (M-362): `StationSimulation.ModuleProductionScale` · `StationModuleStats` 두 곳에 곱함 · `ExplorationSystem.AutoReveal` · `EffectText` · `EffectOf` (`ExplorationPanel`도 이것을 씀)
  - 타이쿤 전용: `OreVeinSystem` · `OreVeinConfig` · `Data/Tycoon/OreVeinConfig.asset` · `ExplorationConfig_Endless.asset` · `SimulationHost._oreVeins` · 메뉴 Setup Ore Veins · 측정 도구 (`BalanceRunSettings.OreVeins` · `BalanceBotVeins` · 결과 광맥 열)
  - 두 모드를 합칠 때: 서사판은 `AutoReveal` 꺼짐 · `EffectText` 없음이라 동작 그대로
  - 화면 (2026-11-27, main M-363): `StationSimulation.ModuleInfoText` · `ExplorationSystem.NameText`/`NameOf` · 탐사 표시 판 라벨 — 서사판은 둘 다 null이라 그대로. 타이쿤 장면에 `ExplorationView` (Setup Ore Veins가 놓음)

- **CONNECT_1121 2번 묶음 선택 조작 M7 (2026-11-27)** — 공통 바닥이라 main(M-364)과 같은 코드: `StationGrid.TryMoveGroup` · `PeekGroupMove` · `GroupMove` · `StationConnectivity.CollectReachable`/`CollectBranch` · `PlacementRules.SupportsOthers(group)` · `PlacementResult.BreaksSupport` · `StationSimulation` 묶음 함수 다섯 · `StationConnectors.MoveFor` · `ModuleSelectionController` 묶음 · `BuildController.BeginGroupMove` · 정보 창 · 상태줄 · `GameAction.SelectBranch`(G, 끝에 더함) · 메뉴 Setup Branch Button
  - main판만 다른 곳: `StationConstruction` 묶음 판정에 `InZone` (main의 칸 제한 `IBuildZone`) · `StatusBar`의 `PlacementReason(result, zone)` — 타이쿤엔 칸 제한이 없음
  - 장면: 타이쿤 `Main.unity`엔 [가지 선택] 버튼을 놓음. main · 서사판 장면은 메뉴 SpaceStation/Move/Setup Branch Button을 그 쪽 Unity에서 한 번 (없으면 G 키만)

- **CONNECT_1121 3번 ① 인접 태그 틀 (2026-11-27)** — 공통 바닥이라 main(M-365)과 같은 코드 · 데이터: `AdjacencyTag` 39개 · `ModuleData.AdjacencyTags` · 규칙의 대상 태그 · 이웃 태그 · `AppliedAdjacency.Target` · 메뉴 Setup Tags
  - 기존 12개 규칙은 태그를 달고 모듈 칸을 그대로 → 봇 결과 바이트까지 같음 (`BalanceReports/20261127_190308` ↔ `190821`)
  - main판만 다른 곳: 서사판 거주 타워 규칙 2개 (타이쿤엔 없음)

- **CONNECT_1121 3번 ② 단계별 내부 상황 U1 (2026-11-27)** — 공통 바닥이라 main(M-366)과 같은 코드: `IncidentCatalog` · `IncidentLoss` · 방 단계 상황만 · 2단계 이상 벽 자리 없음

- **CONNECT_1121 3번 ② U2 ② 장식 조명 꺼짐 (2026-11-27)** — 공통 바닥이라 main(M-367)과 같은 코드 · 모델: 2단계 방 장식 발광 끔 + 바닥 조명 분전함 2 ~ 3개 · `InteriorAtmosphere.SetIncidentDim` · `InteriorSpots.NearDoor` · `IgnoredForSpots` · 바닥을 높여 지은 방(요새) 바닥 자리 고침 · 씬 연결 메뉴 Setup Stage Incidents

- **CONNECT_1121 3번 ② U2 ① 놀이기구 · 쇼 멈춤 (2026-11-27)** — 공통 바닥이라 main(M-368)과 같은 코드 · 모델: `InteriorIncidents.Ride.cs` (기구 16종 꺼 둠 · 사람 손 장식 천천히 멈춤 · 좌석 막음 · 운행 조작반 레버 = 차단기 규칙) · `SM_IncRideConsole` · `TrySpawn`이 못 놓으면 같은 방 다른 상황부터 · 말풍선 자리 `BubbleAt`

- **CONNECT_1121 3번 ② U2 ③ 놀이 소품 흩어짐 (2026-11-27)** — 공통 바닥이라 main(M-369)과 같은 코드 · 모델: `InteriorIncidents.Toys.cs` (소품 2 ~ 3개 줍기 → 바구니에 넣기 · 한 번에 하나 · 소품 빛) · `SM_IncToys` — U2 2단계 셋 끝
