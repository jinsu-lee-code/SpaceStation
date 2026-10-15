# MERGE_NOTES.md — main → 각 판 합칠 때 충돌 예상 체크리스트

`main`(공통 바닥)을 바꿀 때마다, 각 판 브랜치에 합칠 때 생길 충돌과 해결 방법을 여기에 적는다 (SPLIT.md 3번).
해결하고 합치면 그 항목에 `[x]`와 날짜 · 커밋을 적는다.

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
- [ ] **`StationRule` 중복 정의** (컴파일 오류, 글자 충돌 아님) — `ChapterData.cs` 66~83줄의 `enum StationRule`을 지우고 main의 `Data/StationRule.cs`를 쓴다. 이름 · 숫자가 같아서 장 데이터 에셋 값은 그대로.
- [ ] **`StationSimulation.RuleOpen`** — 서사판 줄 `Chapters != null ? Chapters.Allows(rule) : byGrade` ↔ main 줄 `Rules != null ? Rules.Allows(rule) : byGrade` 충돌 → **main 줄 채택**하고, `ChapterSystem`이 `IStationRules`를 구현하게(`class ChapterSystem : StationSystem, IModuleUnlocks, IStationRules` — `Allows`는 이미 있음), 생성 직후 `SetRules(Chapters)` 한 줄 추가 (`Progression.UnlockSource = Chapters;` 옆).
  - 주의: `SetRules`는 `Progression.RuleCheck`도 연결한다. 장 틀은 `IModuleUnlocks`로 모듈 해금을 따로 정하므로, 모듈의 `RequiredRule`이 비어 있으면 영향 없음.
- [ ] **`HeatSystem` · `MeteorShowerSystem` · `SocialCrisisSystem` · `ContactSystem`** — 코드는 같고 주석만 다름 ("장 틀이면 장 규칙" ↔ "규칙을 정하는 쪽이 있으면 그쪽") → 어느 쪽이든 하나. `using SpaceStation.Data;` 추가도 양쪽이 같음.
- [ ] **`SynthesisSystem.IsUnlocked`** — 서사판 `StationRule.Synthesis`(단계 무관) ↔ main `StationRules.ForSynthesisStage(recipe.Stage)` → **main 줄 채택**. 서사판 장 데이터가 2 · 3단계 레시피를 열려면 그 장의 규칙에 `SynthesisStage2` · `SynthesisStage3`도 넣는다 (지금 서사판 레시피는 1단계 5종 + 4단계 1종 → 4단계 '심우주 통신기지'는 `SynthesisStage3` 필요).
- [ ] **`PolicySystem.SlotCount`** — 서사판 `RuleOpen(Policies, true) ? SlotsAt(...) : 0` ↔ main 속성 블록 → **main 채택**. 결과: 정책 규칙이 열리면 등급 표 슬롯, `PolicyExpansion`도 열리면 +`ExpansionSlots`. 서사판이 확장 규칙을 안 쓰면 동작 같음.
- [ ] **`StationProgression.IsUnlocked`** — 서사판 `UnlockSource` 분기 ↔ main `IsRuleLocked` 분기, 두 줄 모두 남긴다 (순서: 준비 중 → 규칙 잠김 → UnlockSource → 등급).
- [ ] 합친 뒤 시험: main `StationRulesTests` 4개 + 서사판 장 틀 시험 모두 통과 확인.

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
- [ ] **`StationSimulation.EventScale`** — 서사판 `Chapters != null ? (IEventScale)Chapters : Progression.Current` ↔ main `_eventScale ?? Progression.Current` 충돌 → **main 줄 채택**하고, 장 틀 생성 직후 `SetEventScale(Chapters)` 한 줄 추가 (M-1의 `SetRules(Chapters)` 옆). `EventIntensity` 주석 줄도 충돌 가능 → 어느 쪽이든.
- [ ] **`MeteorShowerSystem` · `StationMeteors`** — 주석만 다름 ("13-1 장 틀이 있으면 장" ↔ "13-2 규모를 정하는 쪽이 있으면 그쪽") → 어느 쪽이든.
- [ ] `IEventScale.cs` · `StationGrade : IEventScale` — 같은 내용이라 충돌 없음 (확인만).
- [ ] 합친 뒤 시험: main `StationRulesTests`(이벤트 규모 · 파업 기준 포함 6개) + 서사판 장 틀 시험 통과 확인.

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
- [ ] 충돌 없음 예상 (서사판은 이 파일들을 고치지 않음 — 2026-10-15 확인). 서사판 장에서 교육 · 검역을 쓰려면 장의 새 요구에 넣고 두 모듈 '준비 중'을 풂.
- [ ] 주의: `NewModulesSetup`을 다시 돌리면 학교 · 검역소가 다시 '준비 중'이 됨 → 그 판의 설정 메뉴(타이쿤판 `Setup Needs`)를 뒤에 다시 돌릴 것.

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
- [ ] **`Main.unity` 충돌 가능** (레시피 창을 지우고 다시 만들어 fileID가 바뀜) → 서사판 쪽 씬을 유지(`git checkout --ours`)하고 메뉴 SpaceStation/Recipes/Setup을 다시 돌려 연결.
- [ ] 2단계 레시피는 `SynthesisStage2` 규칙이 열려야 함 (M-1) → 장 틀이 2단계를 열 때 그 장 규칙에 넣음. 서사판 24종 선택에 안 들어가면 장 데이터에서 빼 둠.

### `tycoon` 합칠 때
- [ ] **`Main.unity` 충돌 가능** → 타이쿤판 씬을 유지하고 SpaceStation/Recipes/Setup → SpaceStation/Tycoon/Setup Unlock Choices · Setup Growth Threat · Setup Needs를 다시 돌림 (모두 다시 돌려도 같은 결과).
