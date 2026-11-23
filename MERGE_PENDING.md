# MERGE_PENDING.md — `main`으로 옮길 공통 바닥 커밋 (대기 목록)

> 임시 규칙 (사용자 결정 2026-11-24, SPLIT.md 3번): 엔드리스 PC가 `main`을 쓰는 동안 서사판 PC의 바닥 수정은 `narrative`에 **공통 파일만 담은 커밋**으로 쌓고, 여기 적은 뒤 모아서 한 번에 `main`으로 옮긴다.

## 옮기는 순서

1. `git fetch origin` → `origin/main`에서 새 브랜치 (예: `pending-to-main`)
2. 아래 표의 커밋을 위에서부터 차례로 `git cherry-pick` → 충돌 정리 (엔드리스 쪽 새 변경을 살림)
3. `main` 쪽에서 EditMode 시험 통과 · 봇 기준선 확인
4. 이때 **M 번호를 붙임** (그 시점 `main` 마지막 번호 다음) → MERGE_NOTES.md 체크리스트 · SPLIT.md 6번 고리 줄 · BALANCE
5. `main` push → `narrative`에 `main` 합침 (같은 내용이라 충돌이 거의 없음) → 이 표에서 옮긴 줄을 지우고 'M-번호로 옮김'으로 적음

## 커밋 규칙

- 한 커밋 = 공통 파일만 (스크립트 · 공통 데이터 · 공통 시험). 서사판 값(장 데이터 · 문구 · 6 · 7장 설정)은 따로 커밋
- 메시지 `MM.DD/공통 대기/요약`
- 새 고리는 서사판 · 엔드리스가 값만 채우게 (SPLIT 6번 방식)

## 대기 중

| # | 커밋 | 내용 | 바뀐 공통 파일 | 새 고리 (SPLIT 6번 줄 초안) | 예상 충돌 · 해결 |
|---|---|---|---|---|---|
| 1 | `26f2af26` | **주민 추방 공통 고리** (REVIEW_1115 5번, 사용자 결정 11-15 · 11-24) — `ExileSystem`(대가 = 만족도 이유 없음 5 · 문제 주민 1 × 같은 종류 최대 ×2 × 정책, 고른 주민이 떠남, 떠난 주민 기록 · 세이브 키 `exile`) · 정책 효과 7종 · 정책 4종(추방령 · 사면 · 이주 지원 · 귀환 허가) · 명단 [내보내기] · 시험 `ExileTests` 12 | `Data/StationRule.cs` · `Data/PolicyData.cs` (enum 끝에 7개 · Count 20) · `Data/PolicyConfig.cs` · `Data/Policies/PolicyConfig.asset` · `PO_ExileDecree` · `PO_Amnesty` · `PO_ResettlementAid` · `PO_ReturnPermit` (+meta) · `Simulation/ExileSystem.cs` (새) · `ResidentRoster.cs` · `PopulationSimulation.cs` · `ImmigrationSystem.cs` · `StationSimulation.cs` · `UI/RosterView.cs` · `RosterPanel.cs` · `ResidentText.cs` · `Interior/InteriorPad.cs` · `Editor/GradeRulesSetup.cs` · `Tests/EditMode/ExileTests.cs` (새) | 규칙 `StationRule.Exile`(판이 연다, 규칙을 정하는 쪽이 없으면 늘 열림) · `ExileSystem.Exiled`(판이 긴장도 · 코어 축 등 대가를 더함) · `ExileSystem.PreviewSuffix`(미리보기 문구) · `ExileSystem.Returned` · `Departed`(추모 · 연대기) · `ResidentRoster.ArrivalTemplate` · `TraitWeightScale` | ① `StationSimulation.cs`: `narrative`는 위쪽에 13-1 장 틀(`Chapters`) 줄이 더 있음 — 이 커밋은 정책 블록(`if (settings.Policies != null)`) 안 두 줄 + 속성 한 줄만이라 위치만 맞추면 됨 ② **엔드리스 해금 선택 규칙**(`IStationRules`)이 `Exile`을 모르면 닫힘 → 엔드리스는 늘 열리게 `Allows(StationRule.Exile)` = true로 (REVIEW: 개별 추방은 늘 가능) ③ 엔드리스 정책 창에 정책이 12종으로 늘어남 (`PolicyConfig` 공용) ④ `PolicyConfig.asset`에 `_expansionSlots: 1`이 새로 적힘 (기본값이라 동작 같음) ⑤ 명단 칸 너비가 줄어듦 (특성 176 · 집 180 · 보정 48) |
| 2 | `da1793a3` | **탐사 틀 공통 고리** (CONNECT_1121 M8 — 엔드리스 광맥이 씀) — 장 틀에서 떼어 냄: 신호를 정하는 쪽 `IExploreSignals`(묶음 번호 · 종류 · 거리 · 방향 · 이름) · 닿음 판정 `ReachRule` · 매장량 칸 `ExploreSite.Reserve` · 난파선 로그 `NextWreckLog()` · 시험 `ExplorationFrameTests` 1 | **`main`에 탐사 파일이 아예 없음** → cherry-pick이 아니라 이 커밋 기준으로 통째로 가져감 (`git checkout da1793a3 -- 파일` + 각 `.meta` — GUID 유지): `Data/ExplorationConfig.cs` · `Simulation/ExplorationSystem.cs` · `UI/ExplorationPanel.cs` · `UI/ExplorationView.cs` · `Tests/EditMode/ExplorationFrameTests.cs` | `ExplorationSystem.Signals`(판이 신호를 정함 — 서사판 `ChapterExploration` = 장, 엔드리스 = 광맥) · `ReachRule`(엔드리스 = 채굴 태그 모듈이 맞닿음) · `Revealed` · `Reached` 이벤트(판의 보상) · `ExploreSite.Reserve` · `IExploreSignals.Directions`(위아래 포함 가능) | ① `main`에서 붙이는 곳이 없음 — 엔드리스가 `AddSystem(new ExplorationSystem(...))` + 신호를 정하는 쪽을 붙여야 동작 ② `ExplorationPanel` · `ExplorationView`는 씬에 놓아야 보임 (서사판은 Main.unity에 있음) ③ 묶음마다 탐사선 하나 · 센서 = `ExplorationConfig.Sensor` 모듈 하나 (CONNECT R11 '감지 태그 모듈 전체'는 엔드리스가 고리를 넓힐 것) ④ 옮긴 뒤 `main`에서 컴파일 확인 (UI가 `main`에 없는 것을 쓰는지) |
| 3 | `0952ee05` | **외부 위협 1차 X1a — 단계형 습격** (REVIEW_1115 6번 · CONNECT X1 · X4) — `RaidFleetSystem` (정찰 → 함대 → 기함, 함선 체력, 포탑 격추 반격, 기함 꺾음 = 세력 습격 멈춤, 시간 안에 못 꺾으면 큰 포격) · `RaidFleetConfig` 가안 값 · 시험 `RaidFleetTests` 7 | `Data/RaidFleetConfig.cs` (새) · `Simulation/RaidFleetSystem.cs` (새) · `Data/ContactConfig.cs` (`_fleet`) · `Simulation/ContactSystem.cs` (`RaidOverride` · `BeginBoardingRaid`) · `Simulation/MeteorShowerSystem.cs` (`EndRaid` · `BeginRaid(toward)`) · `Data/StationRule.cs` (`FleetRaid = 2048`) · `Simulation/StationSimulation.cs` (`RaidFleet`) · `Tests/EditMode/RaidFleetTests.cs` (새) | 규칙 `StationRule.FleetRaid`(판이 열어야 넘겨받음 — 없으면 기존 포격 · 침입 그대로) · `ContactSystem.RaidOverride` · `RaidFleetSystem.Start`(판이 바로 부름 — 서사판 7장 위기) · `DamageShip` · `DamageFlagship`(포수 의자 · 비장의 카드) · `SizeScale`(엔드리스 성장 비례 위협이 바꿀 수 있음) · `Ended` · `ShipDestroyed` | ① `ContactConfig.asset`에 `_fleet`을 넣어야 시스템이 붙음 (아직 아무 판도 안 넣음) ② 엔드리스 해금 선택 규칙이 `FleetRaid`를 모르면 닫힘 = 지금 습격 그대로 — 엔드리스가 지구급에서 열지 정함 ③ `StationSimulation.cs`는 `narrative`에 장 틀 줄이 더 있음 — 방문 세력 줄 바로 아래 두 줄이라 위치만 맞춤 ④ `BeginRaid`에 인자 하나가 끝에 더해짐 (기본값이면 예전과 같은 난수) |
| 4 | `1eb15a99` | **외부 위협 1차 X1b — 비장의 카드** (방주 방패 · 궤도 포격, 함대 습격이 열릴 때 하나를 골라 한 판에 한 번) · 시험 5 | `Data/RaidFleetConfig.cs` · `Simulation/RaidFleetSystem.cs` · `Simulation/ContactSystem.cs` (특별한 침입 `BeginBoardingAt` · `BoardingTag` · `BoardingFinished`, 세이브에 `BoardingTag`, 코어 -2 복원) · `Simulation/MeteorShowerSystem.cs` (`RaidBlocker` · `RaidFocus` · `RaidFocusShare`) · `Tests/EditMode/RaidFleetTests.cs` | `RaidFleetSystem.TrumpChoicePending` · `ChooseTrump` · `UseArkShield` · `UseOrbitalStrike(발사대)` · `CanUseTrump` · `ContactSystem.BeginBoardingAt` · `MeteorShowerSystem.RaidBlocker` · `RaidFocus` | ① 3번 바로 뒤에 옮김 (같은 파일) ② 고르기 창 · [비장의 카드] 버튼 · 발사대 고르기는 X1c 화면 작업 |

## 옮김

(아직 없음)
