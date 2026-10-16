# MERGE_NOTES.md — main → 각 판 합칠 때 충돌 예상 체크리스트

`main`(공통 바닥)을 바꿀 때마다, 각 판 브랜치에 합칠 때 생길 충돌과 해결 방법을 여기에 적는다 (SPLIT.md 3번).
해결하고 합치면 그 항목에 `[x]`와 날짜 · 커밋을 적는다.

**합침 기록**: `narrative` ← main (2026-10-15, 9648272) — M-1 ~ M-5 해결, 서사판 EditMode 398개 통과. 씬은 서사판 것 유지 + 레시피 설정 다시. 서사판 봇의 합성 확장은 이제 장 규칙 '합성'이 열린 때부터 (main 봇 수정). `tycoon` ← main (bd0e0cd).

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
- [ ] 충돌 없음 예상 (enum 뒤에 붙임 · 에셋 값만). 서사판 장에서 쓰려면 장의 새 요구에 넣고 '준비 중'을 풂.

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
- [ ] 충돌 없음 예상 (새 속성 · 메서드 추가, `MissIncident` 본문과 `ApplySupply` 두 줄만 바뀜 — 서사판이 그 자리를 고쳤으면 main 줄 채택 후 서사판 변경을 다시 얹음).

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
- [ ] 서사판이 위 다섯 창의 `BuildLauncher` 근처를 고쳤으면 main 쪽(등록)으로 맞추고, 서사판 전용 창 버튼이 있으면 같은 방식으로 `HudLauncher`에 등록 (Order 값 사이에 넣음).
- [ ] `StationProgression.IsRevealed`: 서사판은 해금을 장(UnlockSource)이 정하므로, 합칠 때 `IsRevealed`도 같은 해금 출처를 보게 고침 (안 고치면 장에서 연 모듈이 건설 메뉴에 안 보일 수 있음).

### `tycoon` 합칠 때
- [x] `UnlockChoicePanel`의 [시스템] 버튼을 `HudLauncher`에 등록 (Order.Systems, 아이콘 키 "systems", 고를 것이 있으면 Attention).

---

## M-9. 13-6a 튜토리얼 카드 자리 설정 (2026-10-16, `main`)

**main에 넣은 것** — 기본값은 지금 그대로(왼쪽 위)라 동작 변화 없음
- `TutorialView._dockUnderResources` (기본 false) · `_dockGap`: 켜면 카드가 오른쪽 자원 패널 바로 아래에 붙음 (패널 높이를 따라감, 내부에서는 오른쪽 위). 왼쪽 위 [관리] 메뉴(M-8)와 겹치지 않게. 타이쿤판 씬이 켬

### `narrative` 합칠 때
- [ ] 충돌 없음 예상. 서사판은 장 표시가 자원 패널 아래에 있으므로 이 설정은 꺼 둔 채로 (카드는 왼쪽 위 · [관리] 메뉴와 겹치는지는 서사판에서 따로 봄).

### `tycoon` 합칠 때
- [x] 씬의 TutorialView에서 켬 (메뉴 SpaceStation/Tycoon/Setup Hud Layout).

---

## M-10. 13-7 측정 도구: 새 일 간격 · 위기 빈도 · 여유 자원 (2026-10-16, `main`)

**main에 넣은 것** — 게임 동작 변화 없음 (측정 도구만)
- `BalanceMilestones` (새 파일): 한 판에서 처음 생긴 일(등급 · 지을 수 있게 된 모듈 · 새 요구 · 열 · 정책 · 외부 접촉 · 합성 시스템 · 처음 겪은 위기 · 첫 합성)의 시각 → 시작 ~ 도시급까지 '새 일 간격'(평균 · 가장 긴 공백과 그 등급), 위기 10분당 횟수, 여유 자원 시간(네 자원 모두 한도 70% 이상). `milestones.csv` · `milestones.txt`
- 봇 루프 연결: `BalanceRun` 두 줄(만들기 · Tick) · `BalanceJob.Start` / `BalanceRunner.Run`에서 Clear · `BalanceReportWriter.Write`에서 파일 쓰기
- 기준점 빌드에도 같은 파일을 넣어 돌릴 수 있게 그때 있던 API만 씀

### `narrative` 합칠 때
- [ ] 충돌 없음 예상 (봇 루프 두 줄 · 새 파일). 서사판 장 진행도 새 일로 세고 싶으면 `CheckSystems`에 장을 더함.

### `tycoon` 합칠 때
- [x] 같은 변경이 tycoon에 먼저 들어가 있음 (같은 내용이라 그대로 합쳐짐).

---

## M-11. 13-7 봇 플레이어 행동 (2026-10-16, `main`)

**main에 넣은 것** — 측정 도구만 (게임 동작 변화 없음). 봇 측정 결과는 이전과 달라짐
- `BalanceBot.PlayerHabits` (기본 켬, 끄면 이전 봇): 유성우 예보 중 오는 방향 앞면 모듈(비싼 것부터) 앞에 장갑 격벽 (결정마다 1개 · 예보 한 번에 최대 6개 · 금속 40 남김) / 보급 상자를 처음부터 줍기 / 파손은 현장 수리 먼저 (걸어가는 시간을 흉내 내 30초에 1번)
- `ShowerBlocks` · `FieldRepairs` 횟수

### `narrative` 합칠 때
- [ ] 서사판 봇에 장 진행 판단이 있으면 `Decide` 맨 앞(막기 · 상자)과 `RepairDamaged`의 현장 수리 한 줄 위치만 맞춤.

### `tycoon` 합칠 때
- [x] 같은 변경이 tycoon에 먼저 들어가 있음.

---

## M-12. 13-7 먼저 연 모듈 고리 (2026-10-16, `main`)

**main에 넣은 것** — 기본 null이라 동작 변화 없음
- `StationProgression.ExtraUnlock` (Func<ModuleData, bool>): 등급 해금 목록과 별개로 먼저 열린 모듈 → `IsUnlocked` · `IsRevealed`가 참 (준비 중 · 규칙 조건은 그대로). 타이쿤판 '중형 중간 단계'가 씀
- 시험 1개 (`ProgressionTests.ExtraUnlock_OpensBeforeGrade`)

### `narrative` 합칠 때
- [ ] 서사판 `IsUnlocked`는 장(UnlockSource)을 먼저 보므로, 합칠 때 `IsUnlocked` · `IsRevealed` 끝의 `|| IsExtraUnlocked(data)`를 서사판 판정 뒤에도 붙임 (서사판이 쓰지 않으면 null이라 영향 없음).

### `tycoon` 합칠 때
- [x] 충돌 없음 예상 (타이쿤판 `MidGradeSystem`이 값을 정함).
