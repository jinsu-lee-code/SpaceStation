# MERGE_NOTES.md — main → 각 판 합칠 때 충돌 예상 체크리스트

`main`(공통 바닥)을 바꿀 때마다, 각 판 브랜치에 합칠 때 생길 충돌과 해결 방법을 여기에 적는다 (SPLIT.md 3번).
해결하고 합치면 그 항목에 `[x]`와 날짜 · 커밋을 적는다.

**합침 기록**: `narrative` ← main (2026-10-15, 9648272) — M-1 ~ M-5 해결, 서사판 EditMode 398개 통과. 씬은 서사판 것 유지 + 레시피 설정 다시. 서사판 봇의 합성 확장은 이제 장 규칙 '합성'이 열린 때부터 (main 봇 수정). `tycoon` ← main (bd0e0cd).

**합침 기록**: `narrative` ← main (2026-10-18, 33ad7da · d1dfcf8) — M-6 ~ M-14 해결, 글자 충돌 없음. 서사판 EditMode 403개 통과 · 플레이 확인 (건설 메뉴에 1장 모듈 · [관리] 메뉴 맨 끝 코어 기록).

---

## M-1. 13-1 공통 판정 고리 (2026-10-15, `main`)

**main에 넣은 것**
- `Data/StationRule.cs` (새 파일): `StationRule` 플래그 enum + `IStationRules` 인터페이스(`Allows(rule)`) + `StationRules.ForSynthesisStage` · `DisplayName`
  - 값은 서사판 `ChapterData.cs`의 `StationRule`과 **같은 이름 · 같은 숫자**(MeteorShower 1 · Policies 2 · Synthesis 4 · Heat 8 · SocialCrisis 16 · Contact 32) + 새로 3개(SynthesisStage2 64 · SynthesisStage3 128 · PolicyExpansion 256)
- `StationSimulation`: `Rules`(IStationRules) · `RuleOpen(rule, byGrade)` · `SetRules(rules)` — 규칙을 정하는 쪽이 없으면 등급 조건 그대로 (동작 변화 없음)
- `StationProgression`: `RuleCheck` · `IsRuleLocked(data)`, `IsUnlocked`가 모듈의 `RequiredRule`도 봄
- `ModuleData`: `_requiredRule` (예: 방열판 = 열). 기본 None
- `PolicyConfig`: `_expansionSlots`(기본 1), `PolicySystem.SlotCount`가 규칙을 정하는 쪽이 있으면 정책 규칙 · 확장 규칙을 봄
- 6개 시스템 판정 줄: `HeatSystem.Active` · `MeteorShowerSystem`(Idle) · `SocialCrisisSystem`(Calm) · `ContactSystem.Unlocked` · `SynthesisSystem.IsUnlocked` · `PolicySystem.SlotCount`
- `BuildMenu`: 규칙 때문에 잠긴 모듈 안내 ("잠김 · 칸 단위 열")

### `narrative` 합칠 때 (서사판 13-1 장 틀이 같은 자리를 이미 고침)
- [x] **`StationRule` 중복 정의** (컴파일 오류, 글자 충돌 아님) — `ChapterData.cs` 66~83줄의 `enum StationRule`을 지우고 main의 `Data/StationRule.cs`를 쓴다. 이름 · 숫자가 같아서 장 데이터 에셋 값은 그대로.
- [x] **`StationSimulation.RuleOpen`** — 서사판 줄 `Chapters != null ? Chapters.Allows(rule) : byGrade` ↔ main 줄 `Rules != null ? Rules.Allows(rule) : byGrade` 충돌 → **main 줄 채택**하고, `ChapterSystem`이 `IStationRules`를 구현하게(`class ChapterSystem : StationSystem, IModuleUnlocks, IStationRules` — `Allows`는 이미 있음), 생성 직후 `SetRules(Chapters)` 한 줄 추가 (`Progression.UnlockSource = Chapters;` 옆).
  - 주의: `SetRules`는 `Progression.RuleCheck`도 연결한다. 장 틀은 `IModuleUnlocks`로 모듈 해금을 따로 정하므로, 모듈의 `RequiredRule`이 비어 있으면 영향 없음.
- [x] **`HeatSystem` · `MeteorShowerSystem` · `SocialCrisisSystem` · `ContactSystem`** — 코드는 같고 주석만 다름 ("장 틀이면 장 규칙" ↔ "규칙을 정하는 쪽이 있으면 그쪽") → 어느 쪽이든 하나. `using SpaceStation.Data;` 추가도 양쪽이 같음.
- [x] **`SynthesisSystem.IsUnlocked`** — 서사판 `StationRule.Synthesis`(단계 무관) ↔ main `StationRules.ForSynthesisStage(recipe.Stage)` → **main 줄 채택**. 서사판 장 데이터가 2 · 3단계 레시피를 열려면 그 장의 규칙에 `SynthesisStage2` · `SynthesisStage3`도 넣는다 (지금 서사판 레시피는 1단계 5종 + 4단계 1종 → 4단계 '심우주 통신기지'는 `SynthesisStage3` 필요).
- [x] **`PolicySystem.SlotCount`** — 서사판 `RuleOpen(Policies, true) ? SlotsAt(...) : 0` ↔ main 속성 블록 → **main 채택**. 결과: 정책 규칙이 열리면 등급 표 슬롯, `PolicyExpansion`도 열리면 +`ExpansionSlots`. 서사판이 확장 규칙을 안 쓰면 동작 같음.
- [x] **`StationProgression.IsUnlocked`** — 서사판 `UnlockSource` 분기 ↔ main `IsRuleLocked` 분기, 두 줄 모두 남긴다 (순서: 준비 중 → 규칙 잠김 → UnlockSource → 등급).
- [x] 합친 뒤 시험: main `StationRulesTests` 4개 + 서사판 장 틀 시험 모두 통과 확인.

### `tycoon` 합칠 때

- [x] 충돌 없음 (2026-10-15, 164d663). 이 고리 위에 타이쿤판 해금 선택 `UnlockChoiceSystem`을 얹음.

---

## M-2. 13-2 이벤트 규모 고리 · 파업 기준 보정 고리 (2026-10-15, `main`)

**main에 넣은 것**
- `Data/IEventScale.cs` — 서사판 파일을 **그대로 복사** (같은 내용 추가라 합칠 때 충돌 없음)
- `StationGrade : IEventScale` — 서사판과 같은 줄
- `StationSimulation`: `EventScale => _eventScale ?? Progression.Current` + `SetEventScale(scale)`, `EventIntensity` · `Events.IntervalMultiplier`가 `EventScale`을 봄 (서사판과 같은 줄)
- `MeteorShowerSystem.BeginWarning` · `StationMeteors.RollHits`: `var grade = Sim.EventScale;` (서사판과 코드 같음, 주석만 다름)
- `SocialCrisisSystem.ThresholdModifier` (Func<float>, null = 0) — 파업 기준에 더함

### `narrative` 합칠 때
- [x] **`StationSimulation.EventScale`** — 서사판 `Chapters != null ? (IEventScale)Chapters : Progression.Current` ↔ main `_eventScale ?? Progression.Current` 충돌 → **main 줄 채택**하고, 장 틀 생성 직후 `SetEventScale(Chapters)` 한 줄 추가 (M-1의 `SetRules(Chapters)` 옆). `EventIntensity` 주석 줄도 충돌 가능 → 어느 쪽이든.
- [x] **`MeteorShowerSystem` · `StationMeteors`** — 주석만 다름 ("13-1 장 틀이 있으면 장" ↔ "13-2 규모를 정하는 쪽이 있으면 그쪽") → 어느 쪽이든.
- [x] `IEventScale.cs` · `StationGrade : IEventScale` — 같은 내용이라 충돌 없음 (확인만).
- [x] 합친 뒤 시험: main `StationRulesTests`(이벤트 규모 · 파업 기준 포함 6개) + 서사판 장 틀 시험 통과 확인.

### `tycoon` 합칠 때

- [x] 충돌 없음 (2026-10-15, 4b65577). 이 고리 위에 타이쿤판 성장 비례 위협 `GrowthThreatSystem`을 얹음.

---

## M-3. 13-3 요구 종류 교육 · 검역 + 요구별 감점 (2026-10-15, `main`)

**main에 넣은 것** — 어느 등급에도 넣지 않았으므로 동작 변화 없음
- `ResidentNeed`: `Education`(교육) · `Quarantine`(검역) 추가 (enum 뒤에 붙임 — 기존 값 그대로)
- `BalanceConfig._needPenalties`(요구별 만족도 상한 감소량, 없으면 공통 30) + `NeedPenaltyFor(need)`, `NeedsSystem`이 이것을 씀. 에셋: 교육 15 · 검역 15
- 학교 = 교육 반경 4 · 30명, 검역소 = 검역 반경 3 · 20명 (`NewModulesSetup` 정의 + 에셋). 두 모듈은 main에서 여전히 '준비 중'
- 시험 `NeedsTests.PerNeedPenalty_OverridesCommonValue`

### `narrative` 합칠 때
- [x] 충돌 없음 예상 (서사판은 이 파일들을 고치지 않음 — 2026-10-15 확인). 서사판 장에서 교육 · 검역을 쓰려면 장의 새 요구에 넣고 두 모듈 '준비 중'을 풂.
- [x] 주의: `NewModulesSetup`을 다시 돌리면 학교 · 검역소가 다시 '준비 중'이 됨 → 그 판의 설정 메뉴(타이쿤판 `Setup Needs`)를 뒤에 다시 돌릴 것.

### `tycoon` 합칠 때
- [x] 충돌 없음 (2026-10-15). 도시급 새 요구 배치 · 준비 중 해제 · 역병은 타이쿤판에서.

> 각 판 브랜치에서 이 문서를 고치면 main을 합칠 때 충돌한다 → 체크 표시도 main에서 하고 합친다.


---

## M-4. 13-4 레시피 6종 추가 (2026-10-15, `main`)

**main에 넣은 것** — `RecipeSetup`(메뉴 SpaceStation/Recipes/Setup) 정의 + 에셋 · 그레이박스 프리팹 · 씬 연결(레시피 목록 · 레시피 창 다시 만듦)
- 1단계 문화 센터(학교 + 휴게실, 도시급 — main은 학교가 준비 중이라 실제로는 못 만듦)
- 2단계 5종(국가급, 진화 모듈이 재료): 그리드 허브(축전 뱅크 ×2) · 아콜로지(거주 타워 ×2) · 바이오돔(생태 모듈 ×2) · 시민 회관(⇒ 복지 센터 + 문화 센터) · 연구원(연구 단지 + 데이터 센터)
- `MakeModule`이 `_researchSpeedBonus`도 비우고 채움. 시험 `SynthesisTests.Stage2_UsesEvolvedModulesAsIngredients`

### `narrative` 합칠 때
- [x] **`Main.unity` 충돌 가능** (레시피 창을 지우고 다시 만들어 fileID가 바뀜) → 서사판 쪽 씬을 유지(`git checkout --ours`)하고 메뉴 SpaceStation/Recipes/Setup을 다시 돌려 연결.
- [ ] 2단계 레시피는 `SynthesisStage2` 규칙이 열려야 함 (M-1) → 장 틀이 2단계를 열 때 그 장 규칙에 넣음. 서사판 24종 선택에 안 들어가면 장 데이터에서 빼 둠. → **남은 일 (서사판 설계)**: 2026-10-15 합침 뒤 지구급 '심우주 통신기지'(4단계)도 `SynthesisStage3`이 필요해짐 — 쓸 장이 정해지면 그 장 규칙에 넣음

### `tycoon` 합칠 때
- [x] **`Main.unity` 충돌 가능** → 타이쿤판 씬을 유지하고 SpaceStation/Recipes/Setup → SpaceStation/Tycoon/Setup Unlock Choices · Setup Growth Threat · Setup Needs를 다시 돌림 (모두 다시 돌려도 같은 결과).

---

## M-5. 13-2 보강 유성우 배율 고리 (2026-10-15, `main`)

**main에 넣은 것**: `MeteorShowerSystem.HitsMultiplier` (Func<float>, null = `BalanceConfig.ShowerHitsMultiplier`) — `BeginWarning`에서 운석 1회 개수에 곱하는 배율. 동작 변화 없음. 시험 `StationRulesTests.ShowerHitsMultiplier_Override`.

### `narrative` 합칠 때
- [x] `MeteorShowerSystem.BeginWarning`의 `HitsPerWave` 줄 근처 — 서사판은 M-2의 `var grade = …` 한 줄만 고쳤으므로 겹치지 않을 가능성이 큼. 충돌하면 main 줄 채택.

### `tycoon` 합칠 때
- [x] 충돌 없음 예상 (타이쿤판 `GrowthThreatSystem`이 노출 면으로 이 배율을 정함).

---

## M-6. 13-5 요구 종류 소비 · 행정 (2026-10-15, `main`)

**main에 넣은 것** — 어느 등급에도 넣지 않았으므로 동작 변화 없음 (M-3과 같은 방식)
- `ResidentNeed`: `Commerce`(소비) · `Administration`(행정) 추가 (enum 뒤에 붙임)
- 감점: 소비 15 · 행정 15 (`BalanceConfig._needPenalties`)
- 상업 구역 = 소비 반경 4 · 30명, 행정 관제소 = 행정 반경 5 · 40명 (`NewModulesSetup` 정의 + 에셋). main에서는 여전히 '준비 중'

### `narrative` 합칠 때
- [x] (2026-10-18, 33ad7da) 충돌 없음 (enum 뒤에 붙임 · 에셋 값만). 서사판 장에서 쓰려면 장의 새 요구에 넣고 '준비 중'을 풂.

### `tycoon` 합칠 때
- [x] 충돌 없음 예상 (국가급 배치 · 준비 중 해제는 타이쿤판에서).

---

## M-7. 13-6 1인칭 가볍게 고리 (2026-10-16, `main`)

**main에 넣은 것** — 기본값이 지금 동작 그대로라 변화 없음
- `IncidentEffects.MissScale` (기본 1): 놓친 내부 사고 손실 배율. 0이면 "손실 없음"으로 끝 (`StationInteriorOutcomes.MissIncident`)
- `SupplyCrateSystem.AutoCollectRate` (기본 -1 = 끔): 켜면 다음 보급선이 올 때 줍지 않은 상자를 바깥에서 회수 (자원 × 비율, 특별 상자 그대로) — `StationSimulation.AutoCollectCrates` · `StationEventEffects.ApplySupply`
- 레시피 창 연구 포인트 안내 "내부 보급 특별 상자" → "보급 특별 상자"
- 시험 2개 (`StationSimulationTests.Incident_MissScale_*` · `SupplyShip_AutoCollect_*`)

### `narrative` 합칠 때
- [x] (2026-10-18, 33ad7da) 충돌 없음 (새 속성 · 메서드 추가, `MissIncident` 본문과 `ApplySupply` 두 줄만 바뀜 — 서사판이 그 자리를 고쳤으면 main 줄 채택 후 서사판 변경을 다시 얹음).

### `tycoon` 합칠 때
- [x] 충돌 없음 예상 (타이쿤판 `InteriorBonusSystem`이 두 값을 정함).

---

## M-8. 13-6a 좌상단 [관리] 메뉴 · 건설 패널 정리 (2026-10-16, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (사용자 결정)
- `HudLauncher` (새 파일): 왼쪽 위 [관리] 버튼 하나 → 펼치면 아이콘 + 이름 + 키캡 칸, 열린 시스템만 순서대로, 새로 열리면 '새로' · 고를 것이 있으면 '! 확인' (깜박임)
- 연구 · 주민 · 합성 · 정책 · 외부 접촉 창: 각자 만들던 왼쪽 위 버튼(`BuildLauncher` · `RefreshLauncher` · `_launcherLabel`)을 지우고 `HudLauncher.For(...).Add(...)`로 등록. 단축키는 그대로
- 아이콘: `LauncherIconBuilder` (메뉴 SpaceStation/UI/Build Launcher Icons) → `Art/UI/Launcher/LI_*.png` · `HoloArt.LauncherIcons`
- 건설 패널: `StationProgression.IsRevealed`(지금까지 도달한 최고 등급 기준 · 규칙 · 준비 중) · `BuildCategories.Filter/GetAvailable`에 보이는 것 · 정렬(열리는 등급 → 금속 비용) · `BuildController.RefreshModules/ModulesChanged/Revealed` · `BuildMenu.Relayout`(열린 것만 · 순서 · 숫자키 · 탭 수) · 패드 건설 탭도 같은 순서
- 시험 2개 (`BuildCategoriesTests.Filter_VisibleOnly_SortedByGradeThenMetal` · `ProgressionTests.Revealed_FollowsHighestGrade_StaysAfterDrop`)

### `narrative` 합칠 때
- [x] (2026-10-18, 442610c) 서사판은 다섯 창을 고치지 않아 그대로. 서사판 전용 [코어 기록]은 M-14 방식으로 등록. 원문: 서사판이 위 다섯 창의 `BuildLauncher` 근처를 고쳤으면 main 쪽(등록)으로 맞추고, 서사판 전용 창 버튼이 있으면 같은 방식으로 `HudLauncher`에 등록 (Order 값 사이에 넣음).
- [x] (2026-10-18, 442610c) `StationProgression.IsRevealed`: 서사판은 해금을 장(UnlockSource)이 정하므로, 합칠 때 `IsRevealed`도 같은 해금 출처를 보게 고침 (안 고치면 장에서 연 모듈이 건설 메뉴에 안 보일 수 있음).

### `tycoon` 합칠 때
- [x] `UnlockChoicePanel`의 [시스템] 버튼을 `HudLauncher`에 등록 (Order.Systems, 아이콘 키 "systems", 고를 것이 있으면 Attention).

---

## M-9. 13-6a 튜토리얼 카드 자리 설정 (2026-10-16, `main`)

**main에 넣은 것** — 기본값은 지금 그대로(왼쪽 위)라 동작 변화 없음
- `TutorialView._dockUnderResources` (기본 false) · `_dockGap`: 켜면 카드가 오른쪽 자원 패널 바로 아래에 붙음 (패널 높이를 따라감, 내부에서는 오른쪽 위). 왼쪽 위 [관리] 메뉴(M-8)와 겹치지 않게. 타이쿤판 씬이 켬

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음, 설정 꺼 둔 채. 서사판은 장 표시가 자원 패널 아래에 있으므로 이 설정은 꺼 둔 채로 (카드는 왼쪽 위 · [관리] 메뉴와 겹치는지는 서사판에서 따로 봄).

### `tycoon` 합칠 때
- [x] 씬의 TutorialView에서 켬 (메뉴 SpaceStation/Tycoon/Setup Hud Layout).

---

## M-10. 13-7 측정 도구: 새 일 간격 · 위기 빈도 · 여유 자원 (2026-10-16, `main`)

**main에 넣은 것** — 게임 동작 변화 없음 (측정 도구만)
- `BalanceMilestones` (새 파일): 한 판에서 처음 생긴 일(등급 · 지을 수 있게 된 모듈 · 새 요구 · 열 · 정책 · 외부 접촉 · 합성 시스템 · 처음 겪은 위기 · 첫 합성)의 시각 → 시작 ~ 도시급까지 '새 일 간격'(평균 · 가장 긴 공백과 그 등급), 위기 10분당 횟수, 여유 자원 시간(네 자원 모두 한도 70% 이상). `milestones.csv` · `milestones.txt`
- 봇 루프 연결: `BalanceRun` 두 줄(만들기 · Tick) · `BalanceJob.Start` / `BalanceRunner.Run`에서 Clear · `BalanceReportWriter.Write`에서 파일 쓰기
- 기준점 빌드에도 같은 파일을 넣어 돌릴 수 있게 그때 있던 API만 씀

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음 (봇 루프 두 줄 · 새 파일). 장 진행은 아직 새 일로 세지 않음. 서사판 장 진행도 새 일로 세고 싶으면 `CheckSystems`에 장을 더함.

### `tycoon` 합칠 때
- [x] 같은 변경이 tycoon에 먼저 들어가 있음 (같은 내용이라 그대로 합쳐짐).

---

## M-11. 13-7 봇 플레이어 행동 (2026-10-16, `main`)

**main에 넣은 것** — 측정 도구만 (게임 동작 변화 없음). 봇 측정 결과는 이전과 달라짐
- `BalanceBot.PlayerHabits` (기본 켬, 끄면 이전 봇): 유성우 예보 중 오는 방향 앞면 모듈(비싼 것부터) 앞에 장갑 격벽 (결정마다 1개 · 예보 한 번에 최대 6개 · 금속 40 남김) / 보급 상자를 처음부터 줍기 / 파손은 현장 수리 먼저 (걸어가는 시간을 흉내 내 30초에 1번)
- `ShowerBlocks` · `FieldRepairs` 횟수
- (2026-10-16 추가) 다음 등급 조건 중 인구만 모자라고 수용이 꽉 찼으면 생존 자원 다음으로 거주를 먼저 (`OnlyPopulationShort`)
- (2026-10-17 추가) 한 번이라도 파손을 겪었는데 방어 모듈(실드 · 포탑 · 손상 통제실)이 0개면, 거주 습관 다음으로 첫 방어를 금속 여유(건설비 + 거주 1개분)와 무관하게 먼저 (`_wasHit` · `CountDefense`). 모듈 15개당 1개 한도는 그대로

### `narrative` 합칠 때
- [x] (2026-10-18) 서사판 봇에는 장 진행 판단이 없어 자동 합침 그대로. 원문: 서사판 봇에 장 진행 판단이 있으면 `Decide` 맨 앞(막기 · 상자)과 `RepairDamaged`의 현장 수리 한 줄 위치만 맞춤. `PickNeed`의 거주 습관 · 첫 방어 두 덩이는 생존 자원(음식) 바로 다음에 그대로 둠.

### `tycoon` 합칠 때
- [x] 같은 변경이 tycoon에 먼저 들어가 있음.

---

## M-12. 13-7 먼저 연 모듈 고리 (2026-10-16, `main`)

**main에 넣은 것** — 기본 null이라 동작 변화 없음
- `StationProgression.ExtraUnlock` (Func<ModuleData, bool>): 등급 해금 목록과 별개로 먼저 열린 모듈 → `IsUnlocked` · `IsRevealed`가 참 (준비 중 · 규칙 조건은 그대로). 타이쿤판 '중형 중간 단계'가 씀
- 시험 1개 (`ProgressionTests.ExtraUnlock_OpensBeforeGrade`)

### `narrative` 합칠 때
- [x] (2026-10-18) 서사판 `IsUnlocked`는 장(UnlockSource)을 먼저 보므로, 합칠 때 `IsUnlocked` · `IsRevealed` 끝의 `|| IsExtraUnlocked(data)`를 서사판 판정 뒤에도 붙임 (서사판이 쓰지 않으면 null이라 영향 없음).

### `tycoon` 합칠 때
- [x] 충돌 없음 예상 (타이쿤판 `MidGradeSystem`이 값을 정함).

---

## M-13. 12-5 모델 연출: 레이더 이름 규칙 · 깃발 펄럭임 (2026-10-16, `main`)

**main에 넣은 것** — 공통 아트 (행정 관제소 외형 모델이 씀)
- `StationArtBuilder`: 레이더 회전 부품을 이름이 정확히 "SpinRadar"인 것 하나 → "SpinRadar"로 시작하는 모든 부품 (Blender 이름은 겹칠 수 없어 `SpinRadar_Administration`처럼 뒤를 붙임). 센서 어레이는 그대로
- `ModuleFlutter` (새 런타임 컴포넌트, `.meta` GUID 고정 160aec14…): 사슬 맨 앞 조각 "Flutter_{이름}"에 붙어, 같은 부모 아래 "_1", "_2" 조각까지 부모 공간에서 사슬을 계산해 위쪽 축 사인 흔들림 (비활성 = 서서히 멈춤). 조각을 계층으로 겹치지 않는 까닭 = Blender FBX 내보내기가 자식의 자식 축을 한 번 더 돌림
- (2026-10-17 추가) `StationArtBuilder`: "SpinDish"로 시작하는 부품 = 천천히 도는 접시 안테나 (`ModuleSpinner` 초당 8°, 레이더는 40° 그대로). 통신 중계소 `SpinDish_CommRelay`가 씀

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음, 두 프리팹의 `ModuleFlutter` · `ModuleSpinner` 확인. 행정 관제소 모델(`SM_Administration.fbx` · `PF_Administration`)을 합친 뒤 Unity에서 프리팹의 `ModuleFlutter` · 레이더 `ModuleSpinner`가 붙어 있는지만 확인. 통신 중계소(`PF_CommRelay`)도 접시 `ModuleSpinner`(속도 8) 확인.

### `tycoon` 합칠 때
- [x] (2026-10-17 ~ 19) 충돌 없음 (자동 합침, 타이쿤판 PC에서 만든 변경).

---

## M-14. 판 전용 칸 · 건설 메뉴 정렬 고리 (2026-10-18, `main`)

**main에 넣은 것** — 동작 변화 없음
- `StationProgression.UnlockOrder(data)`: 건설 메뉴 정렬 기준 (지금은 `GetUnlockGrade`와 같음). `BuildController` · `PadBuild`가 이것으로 정렬
- `HudLauncher.Order.Edition` (= Systems 60): 판 전용 창 칸은 [관리] 메뉴 맨 끝 같은 자리 (타이쿤판 시스템 · 서사판 코어 기록, 사용자 결정 2026-10-18)
- `LauncherIconBuilder`: 아이콘 "core" (코어 구슬 + 기록 줄) → `LI_core.png`
- 시험: `ProgressionTests.Unlocks_FollowCurrentGrade`에 `UnlockOrder` 한 줄

### `narrative` 합칠 때
- [x] (2026-10-18, 442610c) 서사판 `StationProgression`에서 `UnlockOrder` · `IsRevealed`가 장(`UnlockSource`)을 보게 고침 (M-8 두 번째 항목과 같이).
- [x] (2026-10-18, 442610c) `CoreLogPanel`의 따로 뜬 버튼을 지우고 `HudLauncher`에 Key "core" · `Order.Edition`으로 등록.

### `tycoon` 합칠 때
- [x] (2026-10-19, e74ab11) 충돌 없음. `UnlockChoicePanel`의 `Order.Systems`는 그대로.
- [x] (2026-10-19, e74ab11) `BalanceMilestones.cs.meta` GUID 충돌(add/add) → main 것으로 맞춤.

---

## M-15. 13-3 주민 종류 · 인구 고리 (2026-10-18, `main`)

**main에 넣은 것** — 기본값이 지금 동작 그대로라 변화 없음 (서사판 이민선 시계 · 종족이 씀)
- `Resident.Kind` (빈 문자열 기본) + `ResidentState.Kind` 세이브. `ResidentRoster.KindPicker`(새 주민 종류) · `QueueKinds(kind, n)`(이민선: 다음 도착 n명, 다 안 쓰이면 버림) · `CountKind`
- `ResidentRoster.KindAbility`(종류가 주는 능력, 특성 통로로 더함) · `AbilityCap`(종류 + 개인 합산 상한, KindAbility가 있을 때만) · `TraitAbility`(개인만, 표시용)
- `NeedsSystem.PenaltyScale` (요구별 감점 배율, null = 1)
- `StationSimulation.GrowthIntervalScale` (자연 증가 간격 배율, 기본 1) — `StationModuleStats.RefreshNeeds`의 증가 간격 줄에 곱함
- `ImmigrationSystem.Suppressed` (포트 이민선 시계 끄기)
- 내부: `InteriorResidents.Spawn`이 `Kind`와 같은 동물 모델 Id가 있으면 그 동물 (없으면 번호로, 지금 그대로)
- UI: `ResidentText.KindTag` (null = 표시 없음) — 있으면 특성 앞에 "토끼 | 기술자·낙천가"
- 시험 2개 (`ResidentTests.Kinds_QueueFirst_ThenPicker_AddAbility_WithCap_AndSurviveSave` · `GrowthIntervalScale_Hook`)

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음 (자동 합침). 서사판 13-3 `ArkShipSystem`이 고리를 모두 씀 · EditMode 408개 통과.

### `tycoon` 합칠 때
- [x] (2026-10-19, e74ab11) 충돌 없음 (자동 합침).

---

## M-16. 13-4 긴 밤 · 튜토리얼 판별 문구 고리 (2026-10-18, `main`)

**main에 넣은 것** — 고리를 쓰지 않으면 동작 그대로
- `DayNightCycle.OverrideNight(from, seconds)` · `ClearOverride` · `HasOverride`: 정해진 구간을 밤으로 덮음 (해질녘 · 새벽 전환 포함). 덮은 밤이 끝날 때 원래 주기가 아직 밤이면 원래 새벽까지 낮으로 덮음. `IsDay` · `SolarMultiplier` · `TimeUntilPhaseChange`가 모두 따름 (조명 · 소리 · 자원 패널도 같이). 저장하지 않음 (덮는 쪽이 복원 때 다시 덮음)
- `TutorialData`: `_headerLabel`(카드 머리말, 비면 "튜토리얼") · `_finishTitle` · `_finishBody`(비면 기본 완료 문구) · `_transient`(진행 중이어도 저장하지 않음) + `SetTexts(...)`
- `StationStateSerializer.Capture`: 저장하지 않는 튜토리얼은 빼고
- `TutorialView`: 처음에 튜토리얼이 없어도 꺼지지 않고, `sim.Tutorial`이 새로 시작되면 카드가 다시 붙음 (`Bind` · `Unbind` · `_watched`). 머리말 · 완료 문구는 데이터에서
- 시험 2개 (`PowerStorageTests.Cycle_OverrideNight_LongNightThenDawn` · `TutorialTests.Transient_NotSaved_HeaderLabelDefaults`)

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음 (자동 합침). 서사판 13-4 '첫 긴 밤' 절정 · 코어 기동 안내 · 내부 안내가 씀 · EditMode 413개 통과.

### `tycoon` 합칠 때
- [x] (2026-10-19, e74ab11) 충돌 없음 (자동 합침).

---

## M-17. 13-4 내부 화면: 방 이름 상단 중앙 · 안에서 시작한 튜토리얼 카드 (2026-10-18, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (사용자 피드백)
- `InteriorMode.BuildOverlay`: 방 이름(`_title`) · 사고/부탁 줄(`_tracker`)을 왼쪽 위 → **상단 중앙** (왼쪽 위 [관리] 메뉴와 겹쳤음). 사건 배너(`EventBanner`, 같은 상단 중앙)가 보이는 동안은 그 아래로 비켜 섬 (`PlaceTop`, 매 프레임). 밝은 벽 위에서도 읽히게 글자 크기에 맞춘 반투명 바탕(`TitleBack`)
- `InteriorMode.DriveTutorial`: 들어갈 때는 튜토리얼이 없어 숨긴 "Tutorial" 카드를, 안에서 튜토리얼이 새로 시작되면 다시 켬 (서사판 내부 안내가 안 보였음)

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음 (자동 합침) · 서사판 내부 안내 카드 · 상단 중앙 방 이름 플레이 확인 · 413개 통과.

### `tycoon` 합칠 때
- [x] (2026-10-19, e74ab11) 충돌 없음 (자동 합침).

---

## M-18. 13-5a 탐사 아이콘 · 판 전용 칸 하나 더 (2026-10-18, `main`)

**main에 넣은 것** — 동작 변화 없음
- `LauncherIconBuilder`: 아이콘 "probe" (레이더 원 + 신호 점 셋) → `LI_probe.png`
- `HudLauncher.Order.EditionExtra` (= Contact + 5): 판마다 더 붙는 창 칸 (서사판 탐사)

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음 · 서사판 탐사 창이 씀 · 417개 통과.

### `tycoon` 합칠 때
- [x] (2026-10-19, e74ab11) 충돌 없음 (자동 합침).

---

## M-19. 13-5b 연구 판 고리 `IResearchGate` (2026-10-18, `main`)

**main에 넣은 것** — 고리를 꽂지 않으면 동작 그대로 (타이쿤판 · 봇)
- `IResearchGate` (새 파일): 연구를 열지(`IsOpen` · `ClosedReason`) · 메뉴 이름(`Title`) · 창 위 상태(`Status`) · 단계 시작 조건(`BlockReason`) · 시작할 때 쓰는 것(`OnStarted`) · 완료한 단계의 실제 효과(`Modifiers` — 빈 목록 = 효과 대기, 다른 값 = 강화) · 상세 한 줄(`Note`)
- `ResearchSystem.Gate`(바꾸면 효과 다시 계산) · `IsOpen` · `RefreshEffects()`. `CanStart`: 인구 조건 다음에 고리가 막으면 `ResearchStartResult.Gated`(끝에 추가). `Begin`이 `OnStarted`를 부름. 효과 계산은 모두 `RefreshEffects`로
- `ResearchEffects.Recalculate(..., modifiersOf = null)`: 완료 단계마다 고리가 준 효과를 씀
- `ResearchView`: `Gated` 사유 = 고리 문구, 창 위 줄에 `Status` · 닫혔으면 `ClosedReason`, 상세에 현재 · 다음 단계 `Note`
- `ResearchPanel`: [관리] 이름 · 창 제목 · 왼쪽 위 진행 줄 = `Title`(없으면 "연구"), 닫혔으면 메뉴에서 숨기고 단축키로도 열지 않음
- 시험 2개 (`ResearchTests.Gate_BlocksStart_ConsumesOnStart_Closes` · `Gate_Modifiers_PendingThenBoosted`)

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음 (자동 합침) · 서사판 13-5b 기록 복원(`RecordArchiveSystem`)이 고리를 꽂음 · EditMode 424개 통과.

### `tycoon` 합칠 때
- [x] (2026-10-19, e74ab11) 충돌 없음 (자동 합침).

---

## M-20. 13-5c 유성우 판 규칙 고리 (2026-10-18, `main`)

**main에 넣은 것** — 부르지 않으면 동작 그대로 (방향 고르기 난수 순서도 같음)
- `MeteorShowerSystem.ScheduleNext(seconds, waves)`: 다음 평소 유성우를 그 시간 뒤로 맞추고 그 한 번만 파 수를 바꿈 (파당 운석 수는 보통 파 수 기준 그대로). 서사판 2장 첫 유성우 = 3분 뒤 1파
- `CancelWarning()`: 예보 중인 평소 유성우를 거둠 (서사판 장 절정이 대신 옴), 다음은 보통 간격 뒤
- `BeginRaid(..., label = "포격", avoid = 0)`: 포격 틀로 판이 부르는 특별 유성우 — 화면 이름(`Label`) · 고르지 않을 방향. 끝 알림 = "{이름}이/가 끝났습니다" (포격은 그대로 "포격이 끝났습니다")
- `Waves`: 앞당긴 한 번의 파 수를 따름. 예보 알림의 파 수도 `Waves`
- 세이브에 이름 · 이번 / 다음 파 수 추가 (예전 세이브는 빈 값 = 포격 · 보통)
- 시험 2개 (`MeteorShowerTests.ScheduleNext_FiresEarly_OnceWithCustomWaves` · `Raid_Label_AvoidsDirection_CancelWarning`)

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음 (자동 합침) · 서사판 13-5c 2장 유성우 · 절정 '큰 유성우'가 씀 · EditMode 430개 통과.

### `tycoon` 합칠 때
- [x] (2026-10-19, e74ab11) 충돌 없음 (자동 합침).

---

## M-21. 13-5c 연구 판 고리: 연계 연구 칸 숨기기 (2026-10-18, `main`)

**main에 넣은 것** — 고리를 꽂지 않으면 동작 그대로 (타이쿤판 · 봇)
- `IResearchGate.ShowLinked` (M-19 고리에 한 항목 추가): 연구 창에 연계 연구(12-4) 칸을 보일지. `ResearchSystem.ShowLinked` = 고리가 없으면 늘 true
- `ResearchView`: 숨기면 연계 연구 머리줄 · 줄을 끄고 `Height`가 그만큼 줄어듦 (연계 칸이 없는 패드 탭은 그대로)
- `ResearchPanel`: 창이 열려 있을 때 높이가 바뀌면 창 크기를 다시 맞춤(`FitHeight`)
- `HoloUi.RefitDecor(rt)`: 창 크기를 바꾼 뒤 장식 무늬(격자 · 주사선 · 스캔 띠) 간격을 새 크기에 맞춤
- 시험: `ResearchTests.Gate_BlocksStart_ConsumesOnStart_Closes`에 `ShowLinked` 확인 추가

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음 (자동 합침) · 서사판 `RecordArchiveSystem.ShowLinked` = 합성 규칙(4장)부터 보임 · EditMode 431개 통과 · 플레이 모드 2장 연구 창에서 칸 숨김 · 창 줄어듦 확인.

### `tycoon` 합칠 때
- [x] (2026-10-19, e74ab11) 충돌 없음 (자동 합침, 타이쿤판은 `IResearchGate`를 구현하지 않음).

---

## M-22. 13-6a 정책 판 고리 `IPolicyGate` · 정책 성격 (2026-10-18, `main`)

**main에 넣은 것** — 고리를 꽂지 않으면 동작 그대로 (타이쿤판 · 봇)
- `IPolicyGate` (새 파일): 슬롯 수(`SlotCount(byDefault)`) · 고를 수 있는 정책(`Offers`) · 적용 대기 · 교체 대기(`ApplyDelay` · `SwapCooldown`, 설정 값을 받아 판이 바꿈) · 시행 중 효과의 실제 값(`Value` — 예: 주민 반응) · 정책 줄 덧붙임(`Note`) · 창 위 슬롯 안내(`Summary`) · 잠긴 슬롯 문구(`LockedSlot`)
- `PolicySystem.Gate`(바꾸면 효과 다시 계산) · `Offers` · `ApplyDelay` · `SwapCooldown` · `ValueOf` · `Refresh()`(판이 효과 조건이 바뀌었을 때 부름). `AssignResult.NotOffered`(끝에 추가). 효과 합산은 정책마다 `ValueOf`로
- `PolicyData.Stance`(`PolicyStance` Neutral · Control · Autonomy, 기본 Neutral) + `SetStance`. 메뉴 Grade Rules/Setup Policies가 8종에 넣음(통제형 = 배급제 · 야간 근무 · 정비 의무 · 감시 강화 · 비상 대비 / 자율형 = 이민 개방 · 축제 문화 / 연구 우선 = 중립). 정책 에셋 8개에 `_stance` 추가. 공통 틀은 성격을 쓰지 않음
- `PolicyPanel`: 고를 수 있는 정책만 차례로 놓고 줄 수만큼 창 높이를 맞춤(8종이면 지금과 같음) · 효과 = 실제 값 · 이름 옆에 `Note` · 위 안내와 잠긴 슬롯 문구는 고리 값(없으면 지금 문구) · 시간은 `PolicySystem` 값
- 시험 1개 (`PolicyTests.Gate_SlotsOffersTimingAndValues`)

### `narrative` 합칠 때
- [x] (2026-10-18) 충돌 없음 (자동 합침) · 서사판 3장 정책(`ChapterPolicySystem`)이 고리를 꽂음 — 슬롯 1 → 대형 때 2 · 4종 · 30초 · 3분 · 종족 반응 · EditMode 435개 통과 · 플레이 모드 정책 창 4종 · 두 줄 높이 확인.

### `tycoon` 합칠 때
- [x] (2026-10-19, e74ab11) 정책 에셋 `_stance`는 자동 합침. `PolicyPanel` 위 안내 · 잠긴 슬롯 문구가 타이쿤판 해금 선택 문구(13-1)와 충돌 → 고리 문구(`Summary` · `LockedSlot`)가 있으면 그것, 없으면 타이쿤판 문구 · 시간은 `PolicySystem.ApplyDelay` · `SwapCooldown`.

---

## M-23. 13-7 봇: 핵융합로 저축 중에도 거주 (2026-10-17, `main`)

**main에 넣은 것** — 측정 도구만 (게임 동작 변화 없음). 봇 측정 결과는 이전과 달라짐
- `BalanceBot.PickNeed` 맨 앞(낮 전력 여유 다음)에 거주 습관 한 덩이 더: 다음 등급 조건 중 인구만 모자라고 수용이 꽉 찼을 때 생존 자원이 안정적(`LifeSupportStable`)이고 금속 수입이 있으면 거주. 생존 자원이 줄 때는 원래 자리(생존 자원 다음, M-11)에서 판단
- 까닭: 타이쿤판 측정에서 밤 전력 부족 → 핵융합로 저축 규칙이 거주 습관보다 앞이라, 저축하는 15 ~ 20분 동안 거주가 꽉 찬 채 인구가 멈췄음 (tycoon BALANCE 47번 시드 8 · 9)
- 2026-10-19 보완: 밤을 배터리로 못 버티면(`NightUncovered` — 배터리 규칙과 같은 조건을 함수로 뺌) 이 덩이를 건너뜀. 처음 넣은 대로는 배터리 규칙까지 건너뛰어 측정 12판 중 3판이 밤 정전 → 산소 고갈로 게임 오버

### `narrative` 합칠 때
- [x] (2026-10-19, c9d37b1) `BalanceBot` 자동 합침 · EditMode 444개 통과. 서사판 봇 측정은 아직 다시 돌리지 않음 (13-8 준비 ①에서).

### `tycoon` 합칠 때
- [x] 같은 변경이 tycoon에 먼저 들어가 있음.

---

## M-24. 13-7 봇 측정 결정성 · 시드에 묶인 봇 성향 (2026-10-19, `main`)

**main에 넣은 것** — 게임 동작 변화 없음 (봇 측정만). 봇 측정 결과는 이전과 달라짐
- 결정성: `StationSimulationSettings.CrateSeed` (int?, null = 매 판 다른 시스템 난수 = 게임 그대로). 보급 상자 내용 · 내부 결과 난수(`_crateRandom`)가 시드를 받지 않아, 봇이 상자를 처음부터 줍게 된 뒤(M-11) 같은 시드도 약 10분부터 결과가 갈라졌음. `BalanceRun`이 `BalanceBotPersona.CrateSeedFor(seed)`를 넣음
- 봇 성향 (사용자 결정 2026-10-19): `BalanceBotPersona` (BalanceBot.cs 안) — 시드에서 성장 ↔ 안전 · 저축 · 연구 순서를 정함 (세기 ±40%). `BalanceBot.Persona` (기본 켬, 끄면 `Neutral` = 이전 봇 값 그대로). 바뀌는 값: 방어 빈도(모듈 15개마다 → 9 ~ 21) · 방어 · 장갑 · 연료전지 전 금속 여유(거주 1개분 × 0.6 ~ 1.4) · 거주 미리 짓기(1 → 성장형 최대 4) · 핵융합로 시작 밤 부족량(15 → 9 ~ 21) · 핵융합로 여유(60 → 36 ~ 84) · 회전 링 여유(100 → 60 ~ 140) · 연구 순서(이웃끼리 2번 자리 바꿈)
- `BalanceBot` 생성자에 `persona` (생략 = 중립), `BalanceRunResult.Persona` · `PersonaGrowth` · `PersonaSaving`, summary.txt "봇 성향 시드 n: …" 줄 · summary.csv `persona_growth` · `persona_saving` 열

### `narrative` 합칠 때
- [x] (2026-10-19, c9d37b1) `BalanceRun` · `BalanceRunner` 자동 합침. `StationSimulationSettings`만 충돌 — 서사판 `Chapters` 다음에 `CrateSeed`를 둘 다 둠 · EditMode 444개 통과. 서사판 봇 측정은 아직 다시 돌리지 않음 (13-8 준비 ①에서).

### `tycoon` 합칠 때
- [x] (2026-10-19, e74ab11) `BalanceRun` · `BalanceRunner` 자동 합침 (`CrateSeed` · 해금 선택 블록 둘 다 살아 있음) · M-14 ~ M-24 합친 뒤 EditMode 424개 통과.

---

## M-25. 내부 보급 상자 자리 · 걷는 주민이 상자를 뚫지 않게 (2026-10-19, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (사용자 피드백 "주민이 돌아다니다 코어 보급품과 겹치면 보급품이 사라졌다 나타남")
- `InteriorSpots.Floor(..., ignoreResidents = false)`: true면 빈 자리 판단에서 주민 몸(`ResidentFigure`)을 뺌 (기본값이면 그대로 — 부탁 물건 등)
- `InteriorSupply.Spawn`: 보급 상자 자리 = 다른 상자 · **그 방 주민 자리(템플릿 서기 · 앉기 · 일터)에서 0.9m 이상** + 주민 몸은 보지 않음 → 내부를 다시 만들 때(들어갈 때 · 패드로 지을 때) 주민이 선 곳에 따라 상자가 다른 자리로 튀던 것이 없어짐 (같은 상자 = 늘 같은 자리). 상자 자리가 바뀌므로 예전과 놓이는 곳이 다를 수 있음 (저장 안 함)
- `ResidentWander`: 걷는 중 0.25초마다 앞 0.8m에 새 상호작용 물건(보급 상자 · 부탁 물건 — `InteriorInteractable`, 주민 제외)이 있으면 그 자리에 섬 (`PropAhead`, `LastDecision` "가는 길에 물건이 놓여 멈춤"). 길은 출발 때 한 번만 봐서 걷는 사이 놓인 상자를 뚫고 지나갈 수 있었음

### `narrative` 합칠 때
- [x] (2026-10-19, c9d37b1) 충돌 없음 (같은 내용) · EditMode 444개 통과 · 플레이 모드: 코어 상자 6개가 나갔다 다시 들어가도 같은 자리 · 주민 자리에서 1.75m 이상, 걷는 토끼 0.75m 앞에 물건을 놓으면 바로 멈춤(겹침 0).

### `tycoon` 합칠 때
- [ ] 충돌 없음 예상. 타이쿤판이 `InteriorSupply` 자리 고르기나 `ResidentWander.UpdateWalk`를 고쳤으면 그 부분만 맞춤.

---

## M-26. 12-5 새 기본 모듈 내부 템플릿 9종 · 방 안 실행 중 표시 · 모듈 확인 세이브 (2026-10-19, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (새 내부 9종이 일반 내부 대신 쓰임)
- 데이터: `ModuleData._closedFaces` + `IsClosedFace(셀, 방향)` — 문 자리를 만들지 않는 면 (단백질 배양실 +z 생산 라인 벽). `ConnectorDepthBaker`가 닫힌 면 깊이를 −1로 굽는다
- `InteriorTemplate.ShowOwnExterior` (+ `EditorSetShowOwnExterior`): 창 너머로 자기 바깥 모델을 숨기지 않음 (구조 프레임 트러스 · 센서 접시). `InteriorExteriorView.Create(..., showOwnExterior)`가 받음
- 재질 `M_InteriorCulture.mat` (배양액 발광, `InteriorSetup.CulturePath`)
- 방 안 실행 중 표시 틀 `InteriorRoomDisplay` (정적 `Font` · `Poll(정거장, 모듈, 활성)` 0.25초마다 · TMP 글자 칸): `InteriorWayfinding`(허브 표지판 · 키오스크 = 실제 연결 · 이어 주는 인접 효과) · `InteriorRadar`(센서 레이더 = 실제 유성우 예보 · 경광등 깜빡임) · `InteriorConveyor`(단백질 바 컨베이어 · 상자 뚜껑) · `InteriorServerLights`(데이터 센터 표시등 · 상황판 = 연구 처리 상태, 색이 아니라 깜빡임 · 밝기). `InteriorAtmosphere`가 방마다 `Displays`를 모아 부름, `InteriorMode.Awake`가 글꼴을 넣음, `InteriorOrbit.Angle` 공개
- 편집 도구: `SpaceStation/Interior/Build New Basic Templates` (Build All도 부름), `InteriorPlantSpots`(허브 화분 2 · 상업 구역 나무), `ResidentSpotBaker` 일터 특성 · 여가 방(상업 구역)
- 검수 반영 (같은 날): 허브 키오스크 · 센서 레이더 화면 빛을 화면 반사점이 안 생기는 자리로, 키오스크 인접 효과 글자 크기 = 목록 줄, 허브 표지판 글자 칸 (0.36, 폭 1.0)
- 검수 2차 (같은 날, 16항목):
  - 발광 없는 소품 색 재질 9종 `M_Interior{Paper, PropBlue, PropGreen, PropOrange, PropPurple, PropYellow, Fish, Ice, Aqua}` (`InteriorSetup.Plain`). 소품 색을 발광 재질(Screen · Light · Accent)로 칠해 "텍스처가 안 들어간 것처럼" 보이던 문제
  - Blender 재질 `Paint` → `M_Paint_{모듈}.mat` (`InteriorTemplateBuilder.PaintFor`: 강조색 바탕색 · 발광 없음). 탱크 · 배관 · 의자처럼 강조색으로 칠할 큰 면
  - 화면 재질(Screen · ScreenDark · ScreenGrid) 광택 0.85 → 0.55 (`InteriorSetup.ScreenSmoothness`): 천장 조명이 화면에 작고 강한 흰 반사점으로 비침
  - 물 추출기 드럼 · 단백질 배양 점광원 제거 (붉은 반사광), 허브 키오스크 빛은 화면 면 뒤
  - `InteriorSlidingDoor` (검역소 격리실 미닫이 문, `_Door` 조각에 MeshCollider) · `InteriorFalling` (물 추출기 낙하관 속 얼음 조각)
  - 상업 구역 생선 · 포도 디테일 (같은 날, 사용자 요청): 에셋만 바뀜 (SM_Interior_Commerce · PF_Interior_* 재빌드). 생선 · 포도는 INT_Commerce_Soft(충돌체 없음)로 옮김. 상업 구역 9.3만 → 12만 삼각형 — 상업 구역 13개 최악 배치 측정(RTX 3060 Ti, 1080p)에서 폴리곤 2배까지도 렌더 시간 차이가 오차 범위
- `TutorialView.DockUnderResources`: 내부에서도 자원 패널이 보이면(내부 일시정지 꺼짐 = 기본) 패널 아래에 붙음 — 내부에서 카드가 패널을 덮던 문제. (타이쿤판 Product Name 분리로 설정값이 기본값으로 돌아간 뒤 드러남)
- `SpaceStation/Module Check (전체 모듈 1개씩 → 수동 저장 3)` (`ModuleCheckSave`): 모든 모듈(진화 포함)을 한 개씩 코어에 이어 붙인 정거장 + 모자란 전력 · 산소 · 물 · 식량을 내는 보조 생산 모듈 → 수동 저장 3. 새 모듈 에셋은 자동 포함
- 모델 · 프리팹 · 템플릿 에셋(SM_Interior_* · PF_Interior_* · IT_* · Interior_NewBasic.blend)은 tycoon에서 만들어 binary patch로 옮김 (아래 체크)

### `narrative` 합칠 때
- [x] (2026-10-20, 6a75189) 코드 · 에셋은 자동 합침. `MODELING.md`만 충돌 — 서사판 7-8 ~ 7-10(난파선 · 이민선 · 지구)을 7-9 ~ 7-11로 밀고 main 7-8을 둠. **씬은 따로 맞춰야 했음**: 새 9종 템플릿이 씬 `InteriorMode._templates`에 없어 새 내부가 쓰이지 않음 (main 씬에도 없음) → 목록만 넣음 (`Interior/Setup`은 아래 주의 때문에 안 돌림). EditMode 444개 통과 · 플레이 모드 연결 허브 내부(표지판 "연결 없음" · 환승 안내 키오스크 = 실제 연결) · 콘솔 오류 0.

### `tycoon` 합칠 때
- [x] (2026-10-19, fda99db) 자동 합침 · 템플릿 재빌드 · 플레이 검수 (9종 뚫림 검사 0px · 키트 ↔ 템플릿 같은 평면 = 안 보이는 면뿐).
- [ ] ⚠ (2026-10-20 narrative에서 발견) `Build New Basic Templates`가 부르는 `Interior/Setup`이 선택 패널 [들어가기] 줄을 다시 만들며 같은 줄의 **[옮기기 (M)] 버튼을 지움** — `origin/tycoon` 씬에 `Move` 버튼이 없음. 메뉴 `SpaceStation/Move/Setup Selection Button`을 한 번 돌리면 되살아남 (M-27 이후로는 `Interior/Setup`이 저절로 부름).

---

## M-27. `Interior/Setup`이 [옮기기] 버튼을 지우지 않게 (2026-10-20, `main`)

**main에 넣은 것** — 편집 도구만 (게임 동작 변화 없음)
- `InteriorSetup.Setup` 끝에서 `MoveSetup.Setup()`을 부름: [들어가기] 줄을 다시 만들며 지운 12-3b [옮기기 (M)] 버튼을 다시 붙이고 `SelectionActionsPanel._moveButton` · `_moveLabel`에 연결 (이미 연결돼 있으면 아무것도 안 함)

### `narrative` 합칠 때
- [ ] 충돌 없음 예상.

### `tycoon` 합칠 때
- [ ] 충돌 없음 예상. 합친 뒤 위 M-26 주의대로 [옮기기] 버튼을 되살림 (`Move/Setup Selection Button` 또는 `Interior/Setup`).