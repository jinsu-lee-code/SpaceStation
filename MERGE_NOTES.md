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
  - 2026-10-20: 생선 가게 얼음판의 통생선 → 눈 없는 손질된 상품 (연어 · 흰살생선 토막 트레이 · 조개 · 생선 모양 어묵, 사용자 결정). 에셋만 바뀜. `Interior_NewBasic.blend`에 공용 헬퍼 텍스트 `int_common` · `overlap_check` 복구 (fake user)
- `TutorialView.DockUnderResources`: 내부에서도 자원 패널이 보이면(내부 일시정지 꺼짐 = 기본) 패널 아래에 붙음 — 내부에서 카드가 패널을 덮던 문제. (타이쿤판 Product Name 분리로 설정값이 기본값으로 돌아간 뒤 드러남)
- `SpaceStation/Module Check (전체 모듈 1개씩 → 수동 저장 3)` (`ModuleCheckSave`): 모든 모듈(진화 포함)을 한 개씩 코어에 이어 붙인 정거장 + 모자란 전력 · 산소 · 물 · 식량을 내는 보조 생산 모듈 → 수동 저장 3. 새 모듈 에셋은 자동 포함
- 모델 · 프리팹 · 템플릿 에셋(SM_Interior_* · PF_Interior_* · IT_* · Interior_NewBasic.blend)은 tycoon에서 만들어 binary patch로 옮김 (아래 체크)

### `narrative` 합칠 때
- [x] (2026-10-20, 6a75189) 코드 · 에셋은 자동 합침. `MODELING.md`만 충돌 — 서사판 7-8 ~ 7-10(난파선 · 이민선 · 지구)을 7-9 ~ 7-11로 밀고 main 7-8을 둠. **씬은 따로 맞춰야 했음**: 새 9종 템플릿이 씬 `InteriorMode._templates`에 없어 새 내부가 쓰이지 않음 (main 씬에도 없음) → 목록만 넣음 (`Interior/Setup`은 아래 주의 때문에 안 돌림). EditMode 444개 통과 · 플레이 모드 연결 허브 내부(표지판 "연결 없음" · 환승 안내 키오스크 = 실제 연결) · 콘솔 오류 0.

### `tycoon` 합칠 때
- [x] (2026-10-19, fda99db) 자동 합침 · 템플릿 재빌드 · 플레이 검수 (9종 뚫림 검사 0px · 키트 ↔ 템플릿 같은 평면 = 안 보이는 면뿐).
- [x] ⚠ (2026-10-20 narrative에서 발견) `Build New Basic Templates`가 부르는 `Interior/Setup`이 선택 패널 [들어가기] 줄을 다시 만들며 같은 줄의 **[옮기기 (M)] 버튼을 지움** — `origin/tycoon` 씬에 `Move` 버튼이 없음. 메뉴 `SpaceStation/Move/Setup Selection Button`을 한 번 돌리면 되살아남 (M-27 이후로는 `Interior/Setup`이 저절로 부름). → (2026-10-20, 15f8cfb) tycoon 씬에서 `_moveButton` 비어 있음 확인 → 메뉴로 되살림 · 씬 저장

---

## M-27. `Interior/Setup`이 [옮기기] 버튼을 지우지 않게 (2026-10-20, `main`)

**main에 넣은 것** — 편집 도구만 (게임 동작 변화 없음)
- `InteriorSetup.Setup` 끝에서 `MoveSetup.Setup()`을 부름: [들어가기] 줄을 다시 만들며 지운 12-3b [옮기기 (M)] 버튼을 다시 붙이고 `SelectionActionsPanel._moveButton` · `_moveLabel`에 연결 (이미 연결돼 있으면 아무것도 안 함)

### `narrative` 합칠 때
- [x] (2026-10-20, 1422264) 충돌 없음 · `Interior/Setup`을 돌려도 [옮기기 (M)]가 Row3 맨 앞에 다시 붙고 템플릿 31개 그대로 확인 (확인용으로 다시 만들어진 씬 · 재질은 되돌림).

### `tycoon` 합칠 때
- [x] (2026-10-20, c4f21da) 자동 합침 · 컴파일 오류 0 · [옮기기] 버튼 되살림 (15f8cfb, M-26 주의 항목).

---

## M-28. 동물 주민 Mesh LOD (2026-10-20, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (먼 주민일수록 덜 자세한 메시로 그림, 가까이서는 구별 안 됨)
- 모델 8종(`Art/Models/Resident/Animal/SM_Animal_*.fbx`) 가져오기 설정: `generateMeshLods` 1 · `maximumMeshLod` 4 (LOD0~4, 약 1.2만 → 0.8천 삼각형). `.meta`만 바뀜
- `ResidentFigure.MeshLodBias` = 1 → 만들 때 `SkinnedMeshRenderer.meshLodSelectionBias` (가장 가까운 주민도 LOD1)
- 측정: 화면 삼각형 코어(주민 47) 125.5만 → 104만, 상업 구역(주민 42) 110만 → 95만 (MODELING 맨 위)

### `narrative` 합칠 때
- [x] (2026-10-20, 1ce16f7) 자동 합침 · 모델 8종 가져오기 설정 그대로 (곰 · 토끼 · 펭귄 LOD 5단계 확인) · EditMode 446개 통과 · 플레이 확인 메뉴 4번(대형 정거장) 콘솔 오류 0.

### `tycoon` 합칠 때
- [x] (2026-10-20, b058c94) 자동 합침 · 컴파일 오류 0 · 플레이 확인 (편향 1 적용, 1.4m 펭귄 얼굴 정상).

---

## M-29. 직함 승진 · 동물용 발판 · 패드 비용 아이콘 (2026-10-20, `main`)

**main에 넣은 것** — 화면 · 주민 표시가 바뀜 (사용자 결정 2026-10-20, 오래 미뤄 둔 것 정리)
- **직함 승진** (등급 상한 + 오래 산 주민부터, 표시만 · 효과 없음, BALANCE 26번): `ResidentConfig._ranks`(`TitleRank` 앞말 · 열리는 등급 · 근속) — 선임 = 소형 · 10분, 수석 = 대형 · 30분. `RankOf(근속, 가장 높은 등급)` · `TitleOf(특성, 단계)`. `Resident.JoinedAt`(저장, 이전 세이브 = 0) · `Rank`, `ResidentRoster.Now` · `HighestGrade`(시뮬레이션이 연결) · 매 틱 단계 셈 · `Promoted` 이벤트(불러온 직후 첫 셈은 조용히). `ResidentText.FullName` · 내부 이름표가 단계 붙은 직함, `StatusBar` 승진 알림 "승진 · 두부 → 선임 기관사" / "주민 n명 승진 · …". `ResidentSetup.CreateData`가 단계를 넣음 (에셋에도 들어감)
- **동물용 발판** (작업대에 손이 안 닿던 동물이 올라서서 일함): Blender `BlenderWork/InteriorProps/stool_builder.py` → `Art/Models/Interior/Stool/SM_AnimalStool{15,30,45}.fbx`(노란 상판 · 검은 고무 홈 · 금속관 다리 · 고무 발, 0.45는 뒤 디딤판, 검사 열린 · 비다양체 · 뒤집힘 0). `AnimalModelSet._stools`(모델 · 발 높이 = 상판 + 0.008) · `_workHandHeight` 0.37 · `StoolFor(작업대 높이, 크기)`, `AnimalResidentSetup.WireStools`(Wire Animal Residents도 부름, 씬 안 건드림). `InteriorResidents`: 작업 자세 주민 앞 0.2~0.8m의 작업대 높이를 광선으로 재서 발판을 발밑에 두고 그만큼 올려 세움 (상자 충돌체, 내부를 다시 만들면 지움)
- **패드 비용 아이콘**: `HudText.BigIcons(글, %)` = 글 속 `<sprite>`만 키움. 휴대 패드의 건설 칸 비용 · 건설 상세 · 알림 · 모듈 정보 · 수리 · 정비 · 재건축 · 철거 환급에 150% (작은 글자에서 점처럼 보였음 — 11-12 남은 것)
- (확인만) 파손 · 비활성 모듈 창문 · 띠 불빛: 5-4 `ModuleView`가 이미 비활성 = 끔 · 파손 = 불규칙 깜빡임, 모듈 48종 모두 발광 재질 슬롯이 있어 그대로 먹힘 — 코드 변경 없음
- 시험 2개 (`ResidentTests.TitleRanks_GradeOpens_LongestLivedFirst_AnnouncedAndSaved` · `HudTextTests.BigIcons_WrapsEverySpriteOnly`)

### `narrative` 합칠 때
- [x] (2026-10-20, 1ce16f7) 서사판 작업 트리에 먼저 넣어 둔 같은 변경(main과 내용 같음 확인)을 비우고 main을 합침 — 충돌 없음 · EditMode 446개 통과. 승진 알림 · 발판 · 패드 아이콘은 합치기 전 서사판에서 플레이 확인함.

### `tycoon` 합칠 때
- [x] (2026-10-20, 637237a) 자동 합침 · 충돌 없음 · 컴파일 오류 0 · 플레이(수동 저장 3 불러오기 · 내부) 콘솔 오류 0.

---

## M-30. 12-5 행정 관제소 내부 템플릿 · 모듈 상태 지도(층 버튼) · 실제 주민 번호 대기 (2026-10-20, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (행정 관제소가 일반 내부 대신 새 템플릿을 씀)
- 템플릿 `BuildAdministration` (2칸, `Build New Basic Templates`에 포함, 환경음 Life): 서쪽 민원 홀(창구 3 · 대기 의자 · 발권기 · 천장 돔) + 칸막이 + 동쪽 관제실(모듈 상태 지도 벽 · 콘솔 4 · 레이더 빛줄 `INT_Admin_Sweep` 회전). 문 자리 10, **해치 = 0번 칸 (−2.0, −2.0) · 1번 칸 (2.25, −2.25)** (0번 칸은 구석 벽기둥과 떨어지게)
- `InteriorStationMap : InteriorRoomDisplay` (사용자 결정 2026-10-20 "실제로 움직이게"): 이 모듈과 같은 층의 실제 모듈 칸을 위에서 본 지도 (지도 위 = 관제실에서 지도 벽을 보는 쪽, 모듈 회전을 따름). 여기 = 속 빈 흰 테두리 · 숨 쉬듯 / 정상 = 밝음 / 꺼짐 = 어두움 / 고장 = 빠른 깜빡임 (색약: 모양 · 밝기 · 움직임). 제목 · 요약 "모듈 n · 꺼짐 n · 고장 n · 다른 층 n" · 범례. `StationGrid.Version` + 모듈 상태가 바뀔 때만 메시 4개(상태별)를 다시 만듦, 깜빡임은 속성 블록. 모듈이 꺼지면 "전원 꺼짐"
- `InteriorQueueBoard : InteriorRoomDisplay` — **실제 주민이 움직여야 번호가 바뀜** (사용자 결정 2026-10-20, 같은 날 바꿈 · 처음엔 시간만으로 올라갔음):
  - 창구 직원 = 창구 의자에 앉은 주민. 직원이 없는 창구(밤 · 배정 없음) = "창구 닫힘"(어둡게) · 번호를 부르지 않음, 모든 창구가 닫히면 발권기 "발권 중지"
  - 민원인 = 대기 의자의 쉬는 주민. 번호표가 없으면 발권기로 걸어가 뽑음(발권기 번호가 그때만 올라감), 직원이 있는 빈 창구가 가장 작은 번호를 부르면 창구 앞으로 걸어가 6~10초 머문 뒤 돌아옴
  - 통로는 한 번에 한 명 (돌려보내기 → 호출 → 번호표 순). 오래 막힌 심부름(40초)은 발권기 · 창구를 놓아줌
- 주민 배치 (`ResidentPlacement`): `PlacementRoom.OpenWork` — 일터 특성이 맞는 방이 없거나 꽉 찬 주민이 특성과 상관없이 일하는 방 (사용자 결정: 행정 관제소 직원 = 일터 없는 주민). 순서 = 특성 일터 → OpenWork → 여가 → 집. 시험 `Day_OpenWork_TakesResidentsWithoutWorkplace`
- `InteriorTemplate._openWork` · `_visitStatus`(쉬러 온 사람 상태 글, 행정 관제소 = "민원 보는 중") + `EditorSetVisit`, `InteriorResidents`가 넘김. IT_* 에셋에 두 칸이 생김 (값은 기본값)
- `ResidentSpotBaker`: 행정 관제소는 고정 자리 15 (창구 직원 3 → 관제 콘솔 4 = 앉아서 일함, 대기 의자 8) · 여가 방에 행정 관제소. **휴게실(IT_Recreation)은 다시 구우면 자리가 바뀜** (예전에 템플릿이 바뀐 뒤 굽지 않았던 듯) — 이번엔 되돌려 원래 자리 유지
- `ResidentWander`: 심부름 `StartErrand(길, 방향, 머묾, 도착 · 떠남 · 돌아옴)` · `EndStay` · `ErrandMoving/Staying` · `Hold`(스스로 나들이 안 함). **버그 고침**: 돌기(Turn) 중 플레이어를 보고 손을 흔들면 걷기 동작이 남아 반응이 끝나지 않고 영영 서 있었음 → 기다리는 동안 `Walk(0)`
- 지도 층 버튼: `InteriorStationMap.ConfigureFloors` — 지도 아래 위층 · 아래층 버튼(F 한 번, `InteriorInteractable.Tap` = 새 한 번 누르기 — `InteriorMode`가 처리), 모듈이 있는 층만 · 다른 층에서는 이 모듈 자리 점선 · 가운데 층 이름 화면 · 맨 위/아래층이면 안내
- 모델 · 프리팹 · 템플릿 에셋(SM_Interior_Admin · PF_Interior_* · IT_* · M_Paint_Administration · Interior_NewBasic.blend `admin_builder`)은 tycoon에서 만들어 binary patch로 옮김

### `narrative` 합칠 때
- [ ] 코드 · 에셋은 자동 합침 예상. **씬 `InteriorMode._templates`에 `IT_Admin`을 넣어야 행정 관제소 새 내부가 쓰임** (M-26 때처럼 목록만 넣거나 `Interior/Setup` — M-27 이후 [옮기기] 버튼은 안 지워짐). 서사판이 `ResidentPlacementRules` · `ResidentWander` · `InteriorMode.UpdateHold`를 고쳤으면 그 부분만 맞춤. 플레이 (낮): 행정 관제소 창구에 직원 · 대기 의자에 민원인 · 민원인이 발권기 · 창구로 한 명씩 걸어가고 그때만 번호가 바뀌는지, 지도 층 버튼.

### `tycoon` 합칠 때
- [x] (2026-10-20, 09ba7c7 ~) 자동 합침 · 템플릿 재빌드 · 플레이 검수 (수동 저장 3: 모듈 64 지도 · 이웃 회전 링을 고장 내 깜빡임 · 요약 "고장 1" · 창구 041/042 → 053/054 · 발권기 다음 번호 045 → 057) · 뚫림 검사 390장 0px (대조 97% 자홍) · 키트 ↔ 템플릿 같은 평면 6곳 = 모두 바닥 밑 아래 보는 면.
- [x] (2026-10-20, d65da8e) 층 버튼 · 실제 주민 번호 대기: Bake Resident Spots · 템플릿 재빌드 · EditMode 427개 통과 · 플레이 (낮, 주민 150: 직원 7 · 민원인 8, 걷는 민원인 늘 1명 이하, 발권기 051 → 052는 민원인이 뽑을 때만, 창구 2 직원을 숨기면 "창구 닫힘", 위층에 임시 모듈을 놓고 1층 ↔ 2층 버튼 · 점선 확인) · 뚫림 검사 다시 390장 0px.

---

## M-31. 내부 소품 자리 = 플레이어가 닿는 곳만 · 상단 알림 클릭 → 패드가 그 모듈을 고름 (2026-10-20, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (사용자 요청 2026-10-20 "창구 뒤에는 돌발 상황 안 생기게 · 플레이어 이동이 어려운 곳에는 안 생기도록" · "패드를 꺼낸 상태에서 상단 알림을 클릭하면 그 모듈이 골라지게")
- `InteriorSpots` (돌발 상황 · 현장 수리 · 보급 상자 · 부탁 물건 자리를 고르는 곳): **방마다 플레이어가 걸어갈 수 있는 바닥 격자** (0.25m 칸, 플레이어 반지름 0.3 · 키 1.75 · 눈 1.6, 해치 자리 + 칸 가운데에서 넓이 우선 탐색, 움직이는 것 = 플레이어 · 주민 · 상호작용 물체는 장애물에서 뺌, 방 부모가 바뀌면 다시 셈). 벽 자리 = 벽 앞 0.55/0.8/1.05m에 설 수 있고 눈에서 그 자리가 보여야 함, 바닥 자리 = 둘레 어딘가에 설 수 있어야 함. 쓰는 거리 2.4m. `LastUnreachable`(뺀 개수, 확인용)
  - 화면(재질 이름에 `Screen`) 위는 벽 자리에서 뺌 — 행정 관제소 지도 벽 화면이 환풍구 자리로 잡혔음
  - `InteriorSpots.IFloorKeepClear` + `KeepClear(방 부모, 구역)`: 바닥 소품을 놓지 않을 통로. `InteriorQueueBoard`가 민원인 길(선분 8개 · 반폭 0.35, `ConfigureLanes`)을 등록 — `InteriorTemplateBuilder.BuildAdministration`이 넣음 (PF_Interior_Admin 재빌드)
  - 확인: 48종 방 모두 뺀 자리 0~2개 · 격자 만드는 데 최대 19ms (회전 링). 행정 관제소 360번 뽑기 — 창구 뒤 0 · 지도 화면 0 · 못 닿아 뺀 것 106, 바닥 소품이 통로에 놓인 것 0/40
- `InteriorMode.Builder` (읽기 전용, 확인용)
- 상단 알림 클릭 (밖에서 파손 모듈 창을 누르면 그 모듈로 가는 것처럼): 패드를 확대해 마우스가 풀린 동안 상단 줄(탑승 안내 · 사고 · 부탁)의 대상 모듈이 지금 방이 아니면 안내가 "(클릭하면 패드에서 고름)"로 바뀌고, 글자 위에 올리면 밑줄 · 소리, 왼쪽 클릭 = `InteriorPad.FocusModule(모듈)` → 관리 탭 · 그 모듈 층으로(층 따라가기 끔) · 모듈 선택 → **[이 방으로 이동]만 누르면 감**
- `InteriorInteractable.Tap` (M-30 층 버튼에 쓴 한 번 누르기, 여기 다시 적음)
- PF_Interior_* 10개는 tycoon에서 재빌드해 binary patch로 옮김 (fileID만 새로 · 행정 관제소 = 통로 추가)

### `narrative` 합칠 때
- [ ] 코드는 자동 합침 예상. 서사판이 `InteriorSpots` · `InteriorMode.UpdateTracker` · `InteriorPad`를 고쳤으면 그 부분만 맞춤 (`UpdateTrackerClick`는 `UpdateTracker` 다음에 부름). 플레이: 행정 관제소 창구 뒤 칸막이 · 지도 화면에 돌발 상황이 안 생기는지, 패드를 꺼낸 채 상단 사고 줄을 눌러 그 모듈이 골라지는지.

### `tycoon` 합칠 때
- [x] (2026-10-20, 7a23197) 템플릿 재빌드 · EditMode 427개 통과 · 플레이 (수동 저장 3: 회전 링에 "차단기 내려감"을 일으켜 상단 줄 "(클릭하면 패드에서 고름)" · 밑줄 · 클릭 → 패드가 회전 링 선택 · [이 방으로 이동] 누를 수 있음). 모양은 안 바뀜 (통로는 자리 고르기만).

---

## M-32. 내부 벽 소품 자리 늘림 (2026-10-20, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (사용자 요청 2026-10-20 "벽 자리 늘리는 것도 이어서"): 돌발 상황 · 현장 수리 · 환풍구 자리(`InteriorSpots.Walls`)가 48종 중 40종에서 한쪽 높이 0 ~ 2자리였음 → 36종이 자리 있음
- 원인 1: 방 칸 가운데에서 30°마다 쏘는 광선이 대칭이라 거의 문 옆(문 패널 · 문틀)에만 닿았음 → **갈 수 있는 바닥 가장자리 칸**(0.5m 간격, 몸이 못 서는 칸이 1.25m 안에 있는 방향만)에서 1.15m 광선을 더 쏨. 같은 자리(0.35m)는 한 번, 떨어진 점은 0.1m로만 기억
- 원인 2: 일반 방 벽 대부분인 **막힌 벽 키트 패널(KIT_Wall)**을 패널 범위째 뺐음 (충돌체가 없어 벽면에 붙이면 패널 뒤에 가려짐) → 패널 모델을 2.5cm 칸 지도(칸마다 가장 튀어나온 면, 삼각형 감김으로 방향)로 재서 소품 자리(0.72 × 0.68)에서 가장 앞면에 붙임. 그 면이 자리의 반 이상이어야 (가장자리 세로 테 · 위 테에만 닿으면 뺌), 패널과 맨 벽 · 구석 옆벽 패널에 걸쳐도 뺌. 문 패널(KIT_WallDoor)은 계속 뺌. 키트 패널은 모델 +z가 방 바깥, 보이는 면이 -z
- 받침: 소품 위아래 끝 ±0.34에도 받치는 면(6cm 안) — 낮은 가구 윗모서리에 반쯤 걸치지 않게. 받침 점이 화면이면 뺌 (행정 관제소 지도 화면 아래 끝)
- 갈 수 있는 바닥 격자: **층마다 따로** (칸 = (x, 층, z) — 아콜로지 · 거주 타워 · 코어의 위층이 아래층 격자와 겹쳐 아래층 눈높이로 판정됐음), **문 패널 안은 뺌** (열린 문 너머 연결 통로 벽이 자리로 잡혔음)
- `Clear`(앞 0.4m 빔)는 주민 · 플레이어 몸을 봐 줌 — 같은 씨앗이면 같은 자리
- 확인 (수동 저장 3): 0자리 40 → 12종, 방 밖(통로) 0, 행정 관제소 401번 뽑기 창구 뒤 0 · 지도 화면 0. 계산 = 방마다 처음(바닥 격자 포함) 최대 52ms(바이오돔 8칸) · 그다음 최대 15ms(회전 링). 통신 중계소 패널에 배관 누출을 일으켜 판 앞면에 붙는 것 확인
- 남은 0자리 12종 (상업 구역 · 거주 구역 · 학교 · 배터리실 · 연결 허브 · 연료 전지 · 산소 · 창고 · 구조 프레임 · 포탑 · 물 추출기 · 물 재활용): 템플릿 벽이 문 · 장비 · 유리 · 가구로 다 덮여 0.72m 평평한 곳이 없음 — 사고는 옆방으로, 현장 수리는 예전 연출(가운데 연기 · 아무 벽 스파크). **사용자 결정 2026-10-20: 그대로 둠** (템플릿마다 자리 지정은 안 함)
- 현장 수리 지점 순서가 바뀜 → 예전 세이브에서 일부만 고친 손상 모듈은 고친 자국 위치가 달라 보일 수 있음 (개수 · 진행은 그대로)

### `narrative` 합칠 때
- [ ] 코드는 자동 합침 예상 (`InteriorSpots`만). 플레이: 일반 방(진화 모듈 · 통신 중계소)에서 사고가 패널 앞면에 붙는지, 문 너머 통로에 안 생기는지.

### `tycoon` 합칠 때
- [x] (2026-10-20, c244093) 자동 합침 · EditMode 427개 통과 · 플레이 측정 (위 확인 내용).
- **고침 (2026-10-21, M-33)**: 패널 모양을 재는 `ShapeOf`가 게임 루프(Update)에서 `SM_InteriorKit.fbx`(Read/Write 꺼짐) 메시를 못 읽어 예외 — 막힌 벽 패널이 있는 방에서 환풍구 부탁을 고를 때마다 났음. 위 측정은 에디터 명령(읽기 허용)으로 돌려 못 봤음.

---

## M-33. 12-5 방사선 차폐실 내부 템플릿 · 방사선 경보판 · 벽 소품 자리 예외 고침 (2026-10-21, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (방사선 차폐실이 일반 내부 대신 새 템플릿을 씀)
- 템플릿 `BuildRadiationShelter` (2칸, `Build New Basic Templates`에 포함, 환경음 Machine): 사용자 1안 **제염실 + 대피실**. 원통 콘크리트 천장(`M_InteriorConcrete` · `T_InteriorConcrete_*` 굽기) + 칸막이 둥근 금고문 통로. 서쪽 제염실 = 선량계 게이트(발바닥 발판) · 제염 샤워 부스 2 · 방호복 · 방독면 · 배지판 · 게이트 콘솔 · 폐기물 드럼 · 열린 금고문 문짝 / 동쪽 대피실 = 2층 침상 2 · 벤치 2 · 비상 물자 선반 · 공기 정화기 + 덕트 · 정수기. 문 자리 6, **해치 = 0번 칸 (−0.8, 0) · 1번 칸 (1.5, 0)** (원통 천장 꼭대기 평평한 띠)
  - 금고문 문짝 = 따로 조각 `INT_Shelter_Door` (충돌체 있음 · `InteriorSpots`가 이름의 `Door`로 벽 소품 자리에서 뺌). 경첩 축을 0° ~ 90° 돌려 보는 검사로 **실제로 닫힘** (닫히면 통로 테 앞 3mm, 가운데 = 통로 가운데). 닫히며 쓸고 가는 자리는 바닥 점선 호로 표시하고 비움 — 사용자 지적 (2026-10-21): 처음엔 선량계 게이트가 그 안에 있어 문을 닫을 수 없었음 → 게이트를 바닥 해치 자리와 문 사분면 사이로 옮김
  - 정수기 물통 = 물 추출기 정수기와 같은 투명 병 + 속의 물 (사용자 지적: 처음엔 불투명 통)
- `InteriorRadiationBoard : InteriorRoomDisplay` (사용자 결정 "방사선 경보판", 통로 위 칸막이 양면): 외부 방사선 상태 + 대피 수용 (지금 이 방의 주민 / 정원 = 침상 4 + 벤치 8). 방사선 폭풍이 아직 없어 늘 "정상" — 12-8에서 경보 글 · 경광등으로 이음. 모듈이 꺼지면 "전원 꺼짐"(밝기로 구분)
- 주민: **일터 없는 주민이 비축 물자 관리** (사용자 결정, `OpenWork` — 행정 관제소와 같음). 고정 자리 3 = 게이트 콘솔(앉음) · 비상 물자 선반 · 공기 정화기 제어판. 샤워 부스 앞 자리는 뺌 (사용자 지적: 샤워 부스는 일하는 자리가 아님). 침상 · 벤치는 대피 때 자리라 평소엔 비움
  - `InteriorTemplate._workStatus` + `WorkStatus` · `EditorSetVisit(openWork, visitStatus, workStatus)`: 일하는 사람 상태 글 (차폐실 = "물자 관리 중"), `InteriorResidents`가 넘김. **IT_* 에셋 모두에 칸이 생김** (값은 비어 있음 = 기본 글)
- **벽 소품 자리 예외 고침 (M-32 버그)**: `SM_InteriorKit.fbx` Read/Write 켬 (메시 메모리가 조금 늘어남 — 키트 조각은 작음) + `InteriorSpots.CollectPanels`가 읽을 수 없는 메시는 예외 대신 문 패널처럼 뺌
- 모델 · 프리팹 · 템플릿 에셋(SM_Interior_Shelter · PF_Interior_* · IT_* · M_InteriorConcrete · M_Paint_RadiationShelter · T_InteriorConcrete_* · Interior_NewBasic.blend `shelter_builder`)은 tycoon에서 만들어 binary patch로 옮김. PF_Interior_* 다른 방은 재빌드로 fileID만 새로

### `narrative` 합칠 때
- [ ] 코드 · 에셋은 자동 합침 예상. **씬 `InteriorMode._templates`에 `IT_Shelter`를 넣어야 방사선 차폐실 새 내부가 쓰임** (M-30 `IT_Admin`처럼). 서사판에 방사선 차폐실 모듈 데이터(`MD_RadiationShelter`)가 없으면 템플릿만 들어오고 쓰이지 않음. `InteriorTemplate`을 고쳤으면 `_workStatus` 칸만 맞춤. 플레이: 차폐실 안 경보판 글 · 직원 3 · 금고문 문짝 앞에 물건이 없는지, 일반 방에서 환풍구 부탁이 생길 때 콘솔 예외가 없는지.

### `tycoon` 합칠 때
- [x] (2026-10-21) 자동 합침 · 템플릿 재빌드 · Bake Resident Spots · EditMode 427개 통과 · 플레이 (수동 저장 3: 차폐실 직원 3 · 경보판 "외부 방사선 정상 / 대피 수용 3 / 12명" · 뚫림 검사 2132장 0px (대조 97% 자홍, 자동문은 닫고) · 키트 ↔ 템플릿 같은 평면 0 · 벽 자리 7(문짝 위 0) · 64개 방 벽 자리 계산 · 1분 게임 루프에서 부탁이 생겨도 예외 0). Blender 검사: 겹침(숨은 면 6 = 이전과 같음) · 문 구역 · 벽 뚫림 · 해치 자리 비움 · 문짝 회전 0 ~ 90° 부딪힘 0.

---

## M-34. 12-5 여객 도킹 포트 내부 템플릿 · 도착 안내판 (2026-10-21, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (여객 도킹 포트가 일반 내부 대신 새 템플릿을 씀)
- 템플릿 `BuildPassengerPort` (2칸, `Build New Basic Templates`에 포함, 환경음 Life): 사용자 결정 2026-10-21 = **1안 대합실 + 2안 입국장을 칸별로**. 앞면(+z)은 이민선 접근로라 문 자리 없음 → 앞벽 전체 유리(바깥 화면) + 둥근 탑승구 2 (외형 도킹 해치, 닫힌 두 쪽 문 · 초록 고리 · 번호판 "도착" / "탑승")
  - 서쪽 입국장: 탑승구 1 → 입국 심사 부스 쌍(등을 맞댐, 양옆 줄 · 줄 기둥 · 바닥 화살표, **부스 안 조명** — 사용자 요청 "너무 어두워 보임" → 처음 0.9는 "너무 밝아" → 점광원 0.45) → 수하물 컨베이어(가방 4 · 표지) → 환영 아치("어서 오세요" / 뒷면 "입국장") · 발자국 깔개 → 뒤 문
  - 동쪽 대합실: 창을 보는 공항 의자 14 · 탑승권 확인기 2 · 안내 데스크(i 표지) · 자판기 · 바닥 우주선 표식 · 화분
  - 가운데 유리 칸막이(뒤쪽 통로) 위 양면 도착 안내판. 천장 = 외형 물결 지붕(칸 가운데가 높음), 뒤쪽 띠는 평평 F + 3.6 → **해치 = 0번 칸 (−2.15, −2.15) · 1번 칸 (2.15, −2.15)**. 문 자리 = 양 끝 + 칸마다 뒤
- `InteriorArrivalBoard : InteriorRoomDisplay` (사용자 결정 "도착 안내판"): 실제 이민선 시계 — "다음 이민선 m분 ss초 후 · 지금까지 도착 n명", 이민선이 오면 10초 깜빡임 "이민선 도착 n명 입국" / "이민선 출발 내린 주민 없음", 모듈이 꺼지면 "운항 중지". 방 안 표지 글자(탑승구 · 심사 · 수하물 · 아치 · 안내)도 같은 컴포넌트가 그림
  - `ImmigrationSystem.Visits` (이번 실행에서 이민선이 온 횟수, 저장 안 함): 안내판이 도착을 알아채는 데 씀 (시계가 다시 차오르는 것으로 보면 게임 시간이 멈춘 동안 놓쳤음)
- 주민: 안내 직원 = 일터 없는 주민(`OpenWork`, 행정 관제소와 같음) — 심사 부스 2 · 안내 데스크 1, 상태 글 "여객 안내 중". 쉬러 온 주민 = 대합실 의자 14 (여가 방, "우주 구경 중")
- 화분 2 = 템플릿에는 화분 · 흙만, 식물은 `InteriorPlantSpots` (MODELING 7-5 — 처음에 잎 덩어리로 만들었다가 사용자 지적 "나무가 또 뭉뚱그려져 있음")
- 모델 · 프리팹 · 템플릿 에셋(SM_Interior_Passenger · PF_Interior_* · IT_* · M_Paint_PassengerPort · Interior_NewBasic.blend `passenger_builder`)은 tycoon에서 만들어 binary patch로 옮김. PF_Interior_* 다른 방은 재빌드로 fileID만 새로

### `narrative` 합칠 때
- [ ] 코드 · 에셋은 자동 합침 예상. **씬 `InteriorMode._templates`에 `IT_Passenger`를 넣어야 새 내부가 쓰임**. 서사판이 이민선을 따로 정하면(`ImmigrationSystem.Suppressed`) 안내판은 "운항 일정 없음" — 서사판 이민선 시계를 보여 주려면 그쪽 값으로 잇기. 서사판이 `ImmigrationSystem`을 고쳤으면 `Visits` 한 줄만 맞춤. 플레이: 여객 도킹 포트 안 안내판 시계 · 직원 3 · 대합실 주민, 이민선이 올 때 깜빡임.

### `tycoon` 합칠 때
- [x] (2026-10-21) 자동 합침 · 템플릿 재빌드 · Bake Resident Spots · EditMode 테스트 · 플레이 (수동 저장 3: 주민 17 = 직원 3 · 대합실 14 · 안내판 "다음 이민선 2분 41초 후 / 지금까지 도착 0명" → 이민선을 앞당기면 "이민선 출발 내린 주민 없음" 깜빡임(집 150/150) · 창 너머 실제 바깥 정거장 · 화분 식물 2 · 뚫림 검사 2704장 0px (물결 천장 높이 포함, 자동문은 닫고) · 키트 ↔ 템플릿 같은 평면 0 · 벽 자리 37 · 콘솔 오류 0). Blender 검사: 겹침 0 · 해치 자리 비움 · 벽 뚫림 0 (탑승구 고리 아래 0.1은 바닥 속) · 문 구역 6 = 끝 벽 위 천장 단(띠 끝 면, 소품 아님).

---

## M-35. 12-5 방열판 내부 템플릿 · 열 감시판 · 표지 글자 분리 (2026-10-22, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (방열판이 일반 내부 대신 새 템플릿을 씀)
- 템플릿 `BuildRadiator` (1칸, `Build New Basic Templates`에 포함, 환경음 Machine): 사용자 결정 2026-10-22 **냉각 펌프실**. 가운데 받침 위 열교환기(서쪽 고온 머리관 · 동쪽 저온 머리관 + 구불 관 2 · 관 받침 · "고온" / "저온" 표 · "열교환기" 이름판) · 열교환기 북쪽 통제 콘솔(계기 · 밸브 손잡이, 윗면이 작업자 쪽으로 기운 경사 몸통 — 사용자 지적 2026-10-22: 처음엔 윗판이 반대로 기울어 계기판과 엇갈렸음) · 바닥 접근 금지 선 · 북쪽 양 모서리 세로 펌프 2 · 머리 위 · 모서리 배관(죔쇠 · 걸이) · 천장 방열 지느러미 뿌리 + 조명 두 줄. 문 자리 4면, **해치 = (−2.25, −2.25)**
- `InteriorHeatBoard : InteriorRoomDisplay` (사용자 결정 "열 감시판", 동쪽 벽 남쪽): 맨 위 = 이 방열판 상태 ("방열 중 · 이웃 n곳 -4" / "막힘 · 바깥에 드러난 면 없음" / "멈춤" / 꺼지면 "전원 꺼짐" / 칸 단위 열 규칙 전 "열 관리 전 · 대륙급부터 방열" + 맞닿은 모듈 이름 · "대기"), 아래 = 면이 맞닿은 모듈마다(최대 6, 뜨거운 순) 이름 · 열 막대(어두운 홈 위) · "열 / 한도 정상 · 과열 · 위험" — 위험은 깜빡임 (색약: 글자 · 막대 길이 · 깜빡임). 값은 `HeatSystem` 그대로 (`HeatOf` · `Limit` · `IsOverheated` · `IsDanger` · `RadiatorWorking`, 노출 판정은 `DamageSystem.IsExterior`)
- `InteriorSignLabels : InteriorRoomDisplay` (새): 템플릿 안 고정 표지 글자(모델엔 어두운 판만). 모듈이 꺼지면 어둡게. **여객 도킹 포트의 표지 글자(탑승구 · 심사 · 수하물 · 아치 · 안내)를 `InteriorArrivalBoard`에서 이리로 옮김** — `InteriorArrivalBoard.Configure`는 안내판 판만 받음
- 주민: 기술자 일터 (사용자 결정, 배터리 · 태양광 · 연료전지와 같은 `Technician`) — 고정 자리 3 = 통제 콘솔 · 열 감시판 앞(자판) · 북동 펌프. `ResidentSpotBaker` 고정 자리 방도 `WorkTraits`가 있으면 일터 특성을 넣음 (지금까지 고정 자리 방 = 행정 · 차폐실 · 여객은 특성 없음 → 변화 없음)
- 모델 · 프리팹 · 템플릿 에셋(SM_Interior_Radiator · PF_Interior_* · IT_Radiator · M_Paint_Radiator · Interior_NewBasic.blend `radiator_builder`)은 tycoon에서 만들어 binary patch로 옮김. PF_Interior_* 다른 방은 재빌드로 fileID만 새로 (여객은 표지 글자 컴포넌트가 바뀜)

### `narrative` 합칠 때
- [ ] 코드 · 에셋은 자동 합침 예상. **씬 `InteriorMode._templates`에 `IT_Radiator`를 넣어야 새 내부가 쓰임**. 서사판이 칸 단위 열 규칙(`StationRule.Heat`)을 열지 않으면 감시판은 늘 "열 관리 전" + 이웃 이름 "대기". 서사판이 `InteriorArrivalBoard`를 고쳤으면 표지 글자가 `InteriorSignLabels`로 옮겨 간 것만 맞춤. 플레이: 방열판 안 감시판 글 · 기술자 3, 여객 도킹 포트 표지 글자가 그대로 보이는지.

### `tycoon` 합칠 때
- [x] (2026-10-22) 자동 합침 · 템플릿 재빌드 · Bake Resident Spots · EditMode 427개 통과 · 플레이 (수동 저장 3: 기술자 3 · 감시판 "열 관리 전" + 이웃 4곳 "대기" → 열 규칙을 켜면 "방열 중 이웃 4곳 -4" · 핵융합로 0.3 / 8 정상 · 심우주 통신기지 · 연료전지 · 물 추출기 0.0 / 8 · 뚫림 검사 1222장 0px (자동문은 닫고) · 키트 ↔ 템플릿 같은 평면 0 · 콘솔 오류 0). Blender 검사: 겹침 0 · 문 구역 0 · 벽 뚫림 0 · 해치 자리 비움.

---

## M-36. 12-5 통신 중계소 내부 템플릿 · 신호판 + 세력 평판 (2026-10-22, `main`)

**main에 넣은 것** — 화면 동작이 바뀜 (통신 중계소가 일반 내부 대신 새 템플릿을 씀)
- 템플릿 `BuildCommRelay` (1칸, `Build New Basic Templates`에 포함, 환경음 Electric): 사용자 결정 2026-10-22 **신호 해독실**. 가운데 수신 기둥 (증폭기 받침 · 유리 관 · 도파관 심 · 빛 고리 4 = 조각 `INT_CommRelay_Pulse_0~3`) + 천장 접시 회전 구동부 (`INT_CommRelay_Drive` → `InteriorOrbit` 초당 8°, 바깥 접시 `SpinDish`와 같은 속도) · 구동 모터 · 케이블 트레이 3. 북동 ㄱ자 해독 책상 (경사 조작면 2 · 헤드셋 걸이 · 거위목 마이크) + 구석 벽 화면 2, 북서 기록 책상 (기록계 · 릴 테이프 데크 · 기록철), 남동 송수신기 함 (주파수 표시창 · 큰 다이얼 · 레벨 계기 · 패치 베이 + 패치 코드 4 · 증폭기 방열 핀). 문 자리 4면, **해치 = (−2.25, −2.25)**, 스폰 = 남쪽 문 앞 조금 동쪽 (0.7, −2.4)
- `InteriorSignalBoard : InteriorRoomDisplay` (사용자 결정 "신호판 + 평판"): 값은 `ContactSystem` · 외부 접촉 창(U)과 같은 문구
  - 신호판 (북쪽 벽): 맨 위 = 수신 상태 ("수신 대기 · 다음 함선 약 m분 ss초" / "함선 접근 · 도착 …" / "함선 도착 · 응답 마감 …"(깜빡임) / 규칙 전 "외부 접촉 전 · 신호 대기" / 꺼지면 "전원 꺼짐"), 그 아래 = "해독 중 n% · 세기 a / 필요 b" + 진행 막대 / "해독 불가 · 정체 불명" / "해독 완료 · 세력 계급", 가운데 = 파형 막대 36 (평소 잔잔 · 해독 못 함 = 거친 잡음 · 해독 중 = 진행만큼 고른 물결로 · 해독 끝 = 고른 물결), 맨 아래 = "통신 세기 n · 해독 말단 ~ …" (침입 드론이 있으면 그 경보, 깜빡임)
  - 평판판 (동쪽 벽): 세력 3개마다 이름 (방문 중이고 해독됐으면 "· 방문 중") · 단계 +값 (적대는 굵게) · −100 ~ +100 막대 위 표시 · 직전 대응. 규칙 전 "-" · "아직 만난 적 없음"
  - 수신 기둥 빛 고리: 평소 하나가 느리게, 함선이 오면 넷이 빠르게 위 → 아래 (꺼지면 없음)
- `InteriorRoomDisplay`에 공통 도우미 `CreateQuad`(화면 위 사각 판) · `Clock`("2분 05초") — `InteriorHeatBoard`의 사각 판 · `InteriorArrivalBoard`의 시계 글이 이것을 씀 (동작 같음)
- 주민: 연구원 일터 (사용자 결정, `Scientist` — 연구소 · 데이터 센터와 같음). 고정 자리 3 = 해독 책상 (구석 화면을 봄, 앉음) · 기록 책상 (앉음) · 송수신기 앞 (서서). **연구원은 일터가 많아(수동 저장 3: 연구원 14명 · 일터 13방 43자리) 주민 번호 배치에서 이 방이 비는 일이 흔함** — 규칙대로임
- 모델 · 프리팹 · 템플릿 에셋(SM_Interior_CommRelay · PF_Interior_* · IT_CommRelay · M_Paint_CommRelay · Interior_NewBasic.blend `comm_builder`)은 tycoon에서 만들어 binary patch로 옮김. PF_Interior_* 다른 방은 재빌드로 fileID만 새로 (열 감시판 · 도착 안내판은 코드 도우미만 바뀜)

### `narrative` 합칠 때
- [ ] 코드 · 에셋은 자동 합침 예상. **씬 `InteriorMode._templates`에 `IT_CommRelay`를 넣어야 새 내부가 쓰임**. 서사판이 방문 세력 규칙(`StationRule.Contact`)을 열지 않으면 신호판은 늘 "외부 접촉 전" · 평판판 "아직 만난 적 없음". 서사판이 `ContactSystem`을 고쳤으면 `InteriorSignalBoard`가 쓰는 공개 값(`Phase` · `TimeLeft` · `DecodeProgress` · `CanDecode` · `Decoded` · `VisitorName` · `Reputation` · `Tier` · `LastChoice` · `RelayPower` · `Boarding`)만 맞춤. 플레이: 통신 중계소 안 두 화면 · 기둥 빛 고리 · 천장 구동부 회전.

### `tycoon` 합칠 때
- [x] (2026-10-22) 자동 합침 · 템플릿 재빌드 · Bake Resident Spots · EditMode 427개 통과 · 플레이 (수동 저장 3, 통신 세기 5: "외부 접촉 전 신호 대기" · "통신 세기 5 · 해독 말단 ~ 간부" → 방문 세력 규칙을 켜고 방문 시작 → "함선 접근 도착 1분 00초" · "해독 중 0%" 거친 잡음 → 해독 55% 막대 반 · 파형 반쯤 고름 → "해독 완료 상인 연합 말단" 고른 물결 · 평판 "상인 연합 · 방문 중" (내부에 있는 동안 게임 시간이 멈춰 `ContactSystem.Tick`을 직접 불러 진행) · 연구원 자리 3 (모든 주민에 연구원 특성을 잠시 붙여 확인 — 수동 저장 3 그대로면 0명) · 구동부 회전 · 빛 고리 흐름 · 뚫림 검사 1196장 0px (대조 99% 자홍, 자동문은 닫고) · 키트 ↔ 템플릿 같은 평면 0 · 벽 소품 자리 13 (흩어진 6) · 콘솔 오류 0). Blender 검사: 경계 0 · 겹침 0 · 문 구역 0 · 벽 뚫림 0 · 해치 자리 비움.
