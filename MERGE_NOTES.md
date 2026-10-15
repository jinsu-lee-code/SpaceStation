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

## T. 타이쿤판이 공통 바닥 파일에 더한 줄 (두 모드를 한 게임으로 합칠 때 참고)

`tycoon`에서 바닥 파일을 직접 고친 곳. 모두 **"설정이 있을 때만"** 동작하는 덧붙임이라 서사판 동작은 바뀌지 않는다. 합칠 때 그대로 가져가면 된다.

- **13-1 (2026-10-15)**
  - `SimulationHost`: `_unlockChoices` 필드 + 있으면 `UnlockChoiceSystem.AttachTo` (복원 전)
  - 측정 도구: `BalanceRunSettings.UnlockChoices` · `BalanceRunResult.Choices` · `BalanceRun`(시드 무작위 선택) · `BalanceSimulatorWindow`(기본 에셋 `Data/Tycoon/UnlockChoiceConfig`) · 요약 끝에 경로 줄
  - UI 문구: `PolicyPanel` · `RecipePanel` · `ContactPanel` — `Sim.Rules`가 있으면 "'…' 시스템이 열리면" (서사판은 장 틀이 `Rules`가 되므로 같은 문구가 나옴 → 합칠 때 장 기준 문구가 필요하면 그때)
  - `EventBanner`: 해금 선택 시스템이 있으면 "새 시스템: …" 배너
  - `LoadingTips`: 유성우 · 정책 · 열 줄을 타이쿤판 문구로 (합칠 때 모드별로 나눠야 함)
  - 데이터(에셋): `PolicyConfig` 슬롯 표 `0,1,1,1,1,1,1,1` + 확장 2, `StationGrades` 센서 어레이 · 방열판 · 통신 중계소 → 중형, 이 셋의 `RequiredRule` (합칠 때 모드별 설정으로 나눠야 함)
