# TASKS.md

진행 규칙: 위에서부터 순서대로 하나씩 진행. 각 단계는 "완료 조건"을 만족하면 다음으로 넘어간다.
Claude Code에게는 "TASKS.md의 [현재 항목]을 진행해줘" 식으로 요청한다.

## Phase 0. 프로젝트 세팅 (수동)
- [x] Unity 프로젝트 생성 (최신 LTS, **URP 3D 템플릿**, Input System)
  - 렌더 파이프라인은 시작 시점에 URP로 확정 (중간 변경 금지)
  - 템플릿의 샘플 씬/기본 Volume은 정리하고 빈 씬에서 시작
  - 시작 씬: `Assets/_Project/Scenes/Main.unity`. RP 에셋의 Volume Profile(SampleSceneProfile) 참조 제거
- [x] 폴더 구조 생성, CLAUDE.md / GDD.md 루트에 배치
- [x] Git 초기화 + Unity용 .gitignore
- [x] (선택) Unity MCP 연동

## Phase 1. 그리드와 배치 (그레이박스)
- [x] **1-1. 그리드 코어**
  - `GridConfig`(셀 크기), `StationGrid`(점유 딕셔너리, 배치/제거/조회)
  - 멀티 셀 점유 처리, 6방향 이웃 조회
  - 완료 조건: 순수 C# 단위 테스트 통과 (배치, 중복 배치 거부, 제거)
  - 어셈블리: 런타임 `SpaceStation`(Scripts/), 테스트 `SpaceStation.Tests.EditMode`(Tests/EditMode/, 13개 통과)
  - 회전은 Y축 90도 단위 (`GridDirections.Rotate`)
- [x] **1-2. 모듈 데이터 정의**
  - `ModuleData` ScriptableObject (이름, 점유 셀 오프셋, 비용, 생산/소비)
  - 코어, 태양광, 거주 모듈 3종 에셋 생성 (컬러 큐브 프리팹)
  - 에셋: `Data/Modules/MD_*`, `Prefabs/Modules/PF_*`, `Art/Materials/M_Greybox_*` (URP Lit, 인스턴싱 켬). 수치는 임시값
- [x] **1-3. 궤도 카메라**
  - 회전, 줌, 이동
  - 완료 조건: 씬에서 정거장을 자유롭게 돌려볼 수 있음
  - `OrbitCameraRig`(순수 계산) + `OrbitCameraController`(Main Camera에 부착)
  - 조작: 휠 드래그 회전 / Shift+휠 드래그 화면 이동 / 휠 줌 / WASD 수평 이동 / Space 위·Ctrl 아래 / Q·E 회전
- [x] **1-4. 면 클릭 고스트 배치**
  - 레이캐스트로 면 판별 → 인접 셀에 고스트 표시
  - 초록/빨강 표시, 클릭 확정, ESC 취소, R 회전
  - 완료 조건: 코어에서 시작해 3방향으로 모듈을 붙여나갈 수 있음
  - `StationController`(그리드 소유·코어 배치·프리팹 생성) + `BuildController`(입력·고스트)
  - 모듈 선택은 숫자키 1~9 임시 방식 (2-6 건설 메뉴에서 교체). 고스트는 `M_Ghost`(URP Lit 투명) + MPB 틴트
- [x] **1-5. 철거**
  - 모듈 선택 후 철거, 코어 철거 불가
  - `ModuleSelectionController`: 배치 모드 아닐 때 좌클릭 선택(노랑 MPB), Delete·X 철거, ESC/빈 곳 클릭 해제
  - 철거 가능 여부는 `ModuleData.Removable`(코어 false) + `StationController.TryRemove`에서 판정

## Phase 2. 연결과 시뮬레이션
- [x] **2-1. 연결 판정**
  - `IConnectionRule` + 인접 자동 연결 구현
  - 코어 기준 BFS로 활성/비활성 판정
  - 비활성 모듈은 시각적으로 어둡게 표시
  - 완료 조건: 중간 모듈 철거 시 끊어진 쪽이 비활성으로 바뀜
  - `IConnectionRule`/`FaceAdjacencyConnectionRule` + `StationConnectivity`(BFS, 바뀐 모듈만 이벤트). 배치/철거 시에만 재계산
  - `ModuleView`(프리팹에 부착): 활성/비활성·선택 강조를 합쳐 MPB 적용, 기본 상태는 블록 비움(SRP Batcher 유지)
- [x] **2-2. 틱 시스템**
  - 고정 간격 틱, 일시정지/배속(1x, 2x, 4x)
  - `TickClock`(순수, 프레임당 최대 틱 제한) + `SimulationClock`(씬 `Simulation` 오브젝트). timeScale 미사용
  - 임시 키: P 일시정지, F1/F2/F3 = 1x/2x/4x (2-6 HUD 버튼으로 교체). `_logTicks`로 콘솔 확인
- [x] **2-3. 자원 시스템**
  - 5종 자원, 활성 모듈 기준 생산/소비 집계
  - 전력 부족 시 효율 저하 처리
  - 저장 한도(창고)
  - 수치는 BALANCE.md 기준. `BalanceConfig`(Data/BalanceConfig.asset) + `ModuleData`에 수용 인구·저장 한도 증가 추가
  - `ResourceSimulation`(순수, BALANCE 5번 틱 순서) + `ResourceController`(Simulation 오브젝트). BALANCE 4번 검산 케이스는 단위 테스트로 검증
  - 입력 고갈로 정지한 모듈은 전력 수요도 0으로 처리 (BALANCE 미명시 → 임의 결정)
  - 임시 IMGUI `ResourceDebugOverlay`(인구 +1/-1 포함). 2-6 HUD에서 제거
- [x] **2-4. 건설 비용**
  - 금속 소모, 자원 부족 시 배치 불가, 철거 환급
  - `ResourceSimulation.CanAfford/TrySpend/RefundBuildCost`(전부 또는 전무, 환급은 한도 clamp)
  - `IBuildCostHandler`(Building) ← `ResourceController`가 구현·Start에서 등록. `StationController.CanPlace/TryPlace/TryRemove`에서 사용 → 부족 시 고스트 빨강
- [x] **2-5. 나머지 모듈 5종 추가** (데이터 작업 중심)
  - 채굴 도킹은 말단 배치 규칙 포함: 배치 검증(맞닿은 면 정확히 1개), 나머지 면 배치 차단
  - 등급별 채굴 도킹 최대 설치 수 제한은 3-4에서 연동
  - 5종 에셋(MD_/PF_/M_Greybox_ Oxygen, WaterRecycler, Farm, Storage, MiningDock), BALANCE 3번 수치
  - `PlacementRules`(Core): 점유 → 도킹 인접 차단(`BlockedByTerminal`) → 말단 1면 조건. `ModuleData.TerminalOnly`
  - 숫자키: 1 태양광 / 2 거주 / 3 산소 / 4 물 / 5 농장 / 6 창고 / 7 채굴 도킹
- [x] **2-6. 기본 HUD**
  - 자원 수치, 생산/소비 표시, 건설 메뉴, 시간 배속 버튼
  - 씬 `HUD`(Canvas 1920x1080 스케일) + `EventSystem`(InputSystemUIInputModule)
  - 우측 `ResourcePanel`(전력·스톡 자원 상세, 인구 +1/-1 디버그 버튼은 3-1에서 제거) / 우측 상단 `TimeControlPanel`
  - 하단 `BuildMenu`(PF_BuildButton, 이름·단축키·비용, 툴팁 `TooltipView`, 비용 부족 시 비활성) / `StatusBar`(모드 안내·배치 불가 사유·철거/분리/고갈 알림)
  - 단축키(숫자키, P, F1~F3) 유지. UI 위 클릭은 월드 배치/선택 무시(`UiPointer`). IMGUI 오버레이 제거
  - 폰트: 임시로 맑은 고딕(`Art/Fonts/malgun.ttf` → `Malgun SDF` 동적, TMP 기본 폰트). **배포 전 무료 폰트로 교체 필요**

## Phase 3. 거주자와 위기
- [x] **3-1. 인구/만족도 시스템**
  - 수치는 BALANCE.md 8번. `PopulationSimulation`(순수) — 자원 틱 직후 `ResourceController`가 호출
  - 만족도 증감형 / 증가는 만족도 비례(50→30초, 100→10초), 산소·물·식량 고갈 시 증가 정지 / 감소 3경로(산소 고갈 10초, 만족도<25 15초, 수용 초과 5초)
  - HUD: 인구·만족도·증가 진행도·감소 경고 표시, 감소 알림. 인구 디버그 버튼 제거
- [x] **3-2. 이벤트 시스템** (이벤트 SO + 랜덤 발생기)
  - `GameEventData`(SO, 3-3에서 상속해 효과 필드 추가) + `EventScheduler`(순수, 난수 주입) + `EventController`(Simulation 오브젝트, 틱 연동)
  - BALANCE 9번: 유예 180초, 간격 90~150초, 가중치 랜덤, 겹침 허용(같은 이벤트 중복 금지)
  - 에셋 `Data/Events/EV_Meteor·OxygenLeak·SolarStorm·SupplyShip` (효과 없음, 가중치·지속시간 임시)
  - HUD: 상단 `EventBanner`, 자원 패널 `ActiveEventList`. 디버그 F5 = 즉시 발생 (Phase 4 이후 제거)
- [x] **3-3. 이벤트 4종 구현**
  - 운석 충돌 구현 시 모듈 파손 상태 도입: 외곽 판정(노출 면 여부), 코어 면역
  - 파손 효과(효율 50%, 산소 누출), 수리(금속 30%, 10초), 120초 방치 시 파괴
  - 파괴로 인한 분리는 2-1 연결 판정 재사용
  - 수치는 BALANCE.md 10번
  - 이벤트 SO 서브클래스 `MeteorEventData`/`OxygenLeakEventData`/`SolarStormEventData`/`SupplyShipEventData` + `EventEffectController`(타입 분기로 효과 적용)
  - `DamageSystem`(순수): 파손·수리·방치 파괴, 외곽 후보 판정. `ResourceController`가 틱 순서(파손 → 자원 → 인구) 관리, `TryRepair`
  - `ResourceSimulation` 확장: 모듈별 생산 배율, `PowerSupplyMultiplier`(폭풍), `SetExternalDrain`(누출), `AddStock`/`RemoveStock`
  - UI: `ModuleView` 파손(주황)/수리(하늘) 틴트, `DamageMarkers`(모듈 위 남은 시간), 좌측 하단 `SelectionActionsPanel`([수리 R] [철거 Del]), 상태 표시줄 효과·파괴·수리 알림
- [x] **3-4. 정거장 등급 및 승패 조건**
  - 등급 조건 (인구/모듈 수)을 ScriptableObject로 정의, 등급별 채굴 도킹 최대 수 연동
  - 대형 등급 도달 시 결과 화면 + 계속 플레이 옵션
  - 인구 0 시 게임 오버
  - 수치는 BALANCE.md 11번. `StationGradeConfig`(Data/StationGrades.asset)
  - `StationProgression`(순수: 실시간 등급·해금·채굴 도킹 제한) + `GameSession`(순수: 통계·게임 오버) + `ProgressionController`(IPlacementPolicy로 StationController에 등록)
  - 배치 불가 사유 추가: `ModuleLocked`, `LimitReached`. 건설 메뉴 잠김/최대 표시, 툴팁에 해금 등급
  - UI: 자원 패널 등급 줄(다음 등급 진행도, 채굴 도킹 n/최대), 등급 상승·하락 알림, `ResultScreen`(대형 첫 도달 / 게임 오버, 일시정지 + `SimulationClock.InputLocked`)

## Phase 4. 재미 검증 (여기서 멈추고 플레이)
- [x] 30분 플레이 테스트 (1차) — 로직 정상, **난이도 체감 없음** (GDD 13-1)
- [x] 체크: 배치가 직관적인가? 자원 균형을 맞추는 게 재미있는가? 위기가 긴장감을 주는가?
- [ ] 수치 밸런싱 (ScriptableObject 수정만으로 가능해야 함) → 4-10에서
- [x] 결과에 따라 GDD 수정 → GDD 13번 "난이도 확장"

### 난이도 확장 (GDD 13번, 한 항목씩 진행)
- [x] **4-0. 측정 도구**
  - 틱 순서(파손 → 자원 → 인구 → 이벤트/등급)를 순수 C# `StationSimulation`으로 분리, MonoBehaviour는 얇은 어댑터로
  - 에디터 메뉴에서 실행: 빌드 봇으로 30분을 빠르게 시뮬레이션, 여러 시드 반복
  - 리포트: 등급 도달 시간, 자원 최저치·고갈 시간, 인구 곡선, 이벤트·파괴 수, 게임 오버 여부
  - 구조: `StationSimulation`(순수, 배치·비용·수리·이벤트 효과 포함) ← `SimulationHost`(씬 소유, 틱 연결, F5).
    `StationController`는 뷰 어댑터, `ResourceController`/`EventController`/`EventEffectController`/`ProgressionController`는 UI 창구. `IBuildCostHandler`·`IPlacementPolicy` 삭제
  - 도구: `Scripts/Editor/`(asmdef `SpaceStation.Editor`) `BalanceBot`·`BalanceRunner`·`BalanceSimulatorWindow`. 출력 `SpaceStation/BalanceReports/`(.gitignore)
  - 기준선: BALANCE.md 12번 (대형 평균 13.3분, 위기·파괴 0회)
- [x] **4-1. 등급별 이벤트 규모 비례 + 다중 운석** (분산 타격 + 노출 가중)
  - BALANCE.md 13번. `StationGrade`에 운석 수·간격·강도 배율 필드
  - `DamageSystem.PickMeteorTargets`(가중치 = 3×(노출 면−1)+1, 기울기는 BalanceConfig, 중복 없음), `EventScheduler.IntervalMultiplier/DurationProvider`
  - 측정: 이벤트 2배·파손 12배지만 무제한 병렬 수리로 파괴 0 → 4-3/4-6/4-7에서 압박 완성
- [x] **4-2. 태양광 낮/밤 주기 + 배터리 모듈**
  - BALANCE.md 14번. `DayNightCycle`(순수), `ResourceSimulation` 배터리 충·방전·`SolarMultiplier`
  - `ModuleData.SolarPowered/BatteryCapacity/BatteryRate`, `MD_Battery`(키 8, 초소형 해금). HUD 전력 줄에 배터리·낮/밤·태양광 %
  - 봇: 밤 부족량 기준으로 태양광(낮 충전분 포함)·배터리(용량·속도) 판단
  - 측정: 진행 약 2배 느려짐, 첫 산소 고갈·전력 부족 발생, 배터리 과다(70/125) → 4-10에서 검토
- [x] **4-3. 모듈 노후화 / 정비** (누적 노후도, 내구도 비례 철거 환급, 재건축 이득 구간)
  - BALANCE.md 15번. 조정: 코어 금속 +0.5/s, 정비 비율 20%. `DurabilitySystem`(순수), `StationSimulation.TryMaintain/TryRebuild/GetRefund`
  - UI: 선택 패널 [수리 R][정비 M][재건축 B][철거 Del] + 내구도 표시, 노후 모듈 갈색 틴트, 자원 패널 노후 경고, 노후 파괴 알림
  - 측정: 원안은 성장 정체(소형 4/20) → 조정 후 소형 20/20(6.8분), 중형 정체(인구 23)
- [x] **4-4. 공간 인접 효과**
  - BALANCE.md 16번. 규칙 5개(보너스+페널티), 이웃 수만큼 최대 3회. 추가 규칙은 Phase 6 이후
  - 피드백 반영: 태양광끼리는 그늘 없음(`excludeSameType`), 전력 부족 시 "가동률 N%" 표시(자원·선택 패널)
  - `AdjacencyRuleSet`(SO) + `AdjacencySystem`(순수, 그리드 변경 시 재계산·캐시, 배치 미리보기)
  - `ResourceSimulation`: 모듈별 소비 배율, `ExtraHousing`. 생산 배율은 파손×내구도×인접
  - UI: 배치 중 상태 표시줄 미리보기, 선택 패널 적용 효과, 건설 메뉴 툴팁 규칙. 봇: 배치 점수에 인접 효과, 전력 계산에 그늘 반영
- [x] **4-5. 건설 메뉴 카테고리 탭** (신규 모듈 증가 대비)
  - `ModuleCategory`(전력/생활/산업/방어) + `ModuleData.Category`. 모듈이 없는 탭은 숨김(방어는 4-8에서 등장)
  - 분류: 전력=태양광·배터리 / 생활=거주·산소·물·농장(+의료·여가) / 산업=창고·채굴 도킹(+정비 베이) / 방어=실드·포탑
  - `BuildCategories`(순수: 탭 목록·필터·순환), `BuildController` Tab/Shift+Tab 전환, 숫자키 1~9 = 탭 안 순서, 탭을 바꿔도 배치 중 모듈 유지
  - UI: 건설 메뉴 위 탭 줄(`BuildTabs`, `PF_BuildTab`, 탭 이름 + 모듈 수, "Tab / Shift+Tab" 안내), 상태 표시줄은 y 140으로 올림
- [x] **4-6. 수리 인력 제한** (정비 베이 모듈)
  - BALANCE.md 17번. 슬롯 = 코어 1(`baseRepairSlots`) + 활성·정상 정비 베이(`ModuleData.RepairSlots`), 수리 30초
  - `DamageSystem`: `RepairCapacity`, 대기열(`Queue`, `Prioritize`, `CancelQueued`), `StationSimulation.CountRepairSlots/TryPrioritizeRepair/TryCancelRepair`, `RepairResult.Queued/AlreadyQueued`
  - `MD_MaintenanceBay`(금속 100, 전력 5, 2칸, 소형 해금, 산업 탭) + `PF_MaintenanceBay`(주황)
  - UI: 선택 패널 대기 순번·우선 수리(R)·[대기 취소 (C)], 자원 패널 수리 슬롯 줄, 파손 마커 "대기 n", 툴팁 "동시 수리 슬롯 +1". 봇: 대기 판단 + 필요 슬롯만큼 베이 건설
- [x] **4-7. 연쇄 파손 확산**
  - BALANCE.md 18번. 60초 방치 시 이웃 1곳으로 1회 확산(연쇄 허용, 코어 제외, 내구도 -20), 수리 시작·대기열 등록 시 멈춤(`queuePausesSpread`)
  - `DamageInfo.TimeUntilSpread/HasSpread/SpreadPending`, `DamageSystem.SpreadDue` → `StationSimulation.HandleSpreadDue`(대상 선택), `DamageSpread` 이벤트, `GameSession.DamageSpreads`
  - UI: 마커 "확산 N초"(15초 이하 강조), 선택 패널 확산 시간, 번짐 알림. 측정 도구에 확산 횟수 열 추가
  - B안: 대기 = 확산 멈춤 유지, 대기 취소 환불 50%(`repairCancelRefundRate`)로 허점 차단. 우선 수리 점검 + 같은 틱 파괴/베이 복구 겹침 버그 수정(회귀 테스트)
- [x] **4-8. 방어 모듈** (실드/포탑)
  - BALANCE.md 19번. 둘 다 범위형(반경 2칸, 체비셰프): 실드 빗겨냄 70%(중첩 없음, 그중 70%는 범위 밖 외곽 모듈로 튕김·1회), 포탑 완전 격추 20%(합산 최대 60%). 금속 120, 전력 6, 1칸, 소형 해금, 방어 탭
  - `ModuleData` 방어 필드, `DefenseSystem`(순수: 차단·격추 확률, 범위 계산), `StationSimulation.ApplyMeteor`에서 격추→차단 판정, 가동률 = 활성·정상 × 전력 × 내구도
  - `MD_Shield`/`MD_Turret` + 프리팹(받침 + 구/원기둥), `DefenseRangeView`(반투명 범위 상자), UI(상태 표시줄 범위, 선택 패널 방어·보호 줄, 툴팁, 운석 알림)
  - 봇: 금속 여유 시 모듈 15개당 1개. 측정: 우선 건설 시 파손 -27%지만 인구 -36% → 4-10에서 재조정
- [x] **4-9. 거주자 요구 단계** (의료·여가 등 신규 모듈)
  - BALANCE.md 20번. 소형 의료 / 중형 여가, 범위형(반경 3칸) + 담당 인원, 미충족 비율만큼 만족도 상한 하락(요구당 최대 -30)
  - `ResidentNeed`, `StationGrade.NewNeeds`, `ModuleData` 서비스 필드, `NeedsSystem`(순수: 주민 배분·담당·상한), `PopulationSimulation.SatisfactionCap`
  - `MD_Medical`(1칸, 금속 80, 전력 4, 물 0.2) / `MD_Recreation`(2칸, 금속 100, 전력 3), 생활 탭. 범위 상자 초록(`DefenseRangeView` 확장)
  - UI: 자원 패널 요구 줄·상한·증가 정지 사유, 선택 패널, 배치 미리보기, 툴팁. 봇: 충족률 95% 미만이면 서비스 모듈
- [x] **4-10. A: 전체 수치 조정** (측정 도구 기준 목표 난이도 곡선)
  - BALANCE.md 21번. 플레이 확인: 느긋하게 해도 약 15분에 중형 도달(목표 일치), 산소 위기는 산소 누출이 겹칠 때만 문제. 목표: 소형 5 / 중형 15 / 대형 35~45분(절반), 게임 오버 10~25%, 파괴 2~5·고갈 2~4
  - 측정 60분 × 20, 고갈 횟수 지표, `BalanceExperiment`(임시 값 측정 후 복원). 실험 13안 기록
  - 노멀 적용: 노후 0.12, 배터리 600, 이벤트 간격 중형 0.45 / 대형 0.3 → 대형 10/20 (45.2분), 게임 오버 15%
  - 실패 조건 추가 `FailureMonitor`: 산소 고갈 90초, 만족도 0 90초, 코어 주변 붕괴 30초(이웃 2개 이상) + 경고·결과 화면 사유
- [ ] 난이도 프리셋 (추후, 메인 메뉴와 함께): 이지 = G안, 하드 = 노멀 + 노후 0.13~0.14 + 실패 조건 강화 (측정 후 확정). BALANCE 21번
- [ ] 배포 전: 임시 폰트(맑은 고딕) 무료 폰트로 교체
- [x] 30분 플레이 테스트 (2차) — 중형 약 15분(목표 일치), 산소 누출 겹침 위기는 그대로 유지하기로 결정

## Phase 5. 비주얼 교체 (재미 검증 후)
기본 원칙: 아래 순서대로 진행하면 적은 노력으로 룩이 크게 바뀐다.

- [ ] **5-1. 조명과 배경 (수동 + Claude Code 안내)**
  - 결정: 따뜻한 항성광 톤, 무료 에셋 스토어 스카이박스(사용자 임포트 대기), 낮/밤 주기를 조명에 반영
  - 완료: `DayNightLighting`(태양 색·밝기·기울기, 평면 환경광, 스카이박스 `_Exposure`를 태양광 배율로 보간, 밤 최소 밝기 유지)
  - 스카이박스: SpaceSkies Free(6 Sided, 3종) 중 **Purple 2K** 적용, 태양은 은하 중심(+Z) 쪽 yaw 180 / 기울기 낮 32°·밤 10°
  - 정리: 사용하는 Purple 2K(재질 + 텍스처 6장)만 남기고 Demo·1K·4K·다른 2종 삭제 (482 MB → 24 MB)
  - Directional Light 1개 세팅 (각도, 색, 강도)
  - 우주 스카이박스 머티리얼 적용 (별/성운)
  - 환경광 색을 어두운 톤으로 조정
  - 완료 조건: 그레이박스 정거장이 우주 배경 위에 또렷하게 보임
- [x] **5-2. 포스트 프로세싱**
  - `Settings/PP_Main.asset` + 씬 `GlobalVolume`: Bloom(임계 1.0, 강도 0.6, 은은하게), Tonemapping ACES, Color Adjustments(노출 +0.35, 대비 +8, 채도 +6), Vignette(검정 0.22)
  - 카메라 후처리 켜기 + SMAA, URP 색보정 HDR 모드. asmdef에 URP 런타임 참조 추가
  - `CrisisVignette`: 실패 조건 경고 중 비네팅이 붉게 맥박(남은 30초 이하부터 빨라짐), 런타임 프로필 복사본 사용
  - 글로벌 Volume 생성: Bloom, Color Adjustments, Vignette
  - 완료 조건: 밝은 부분이 은은하게 번지고 전체 톤이 통일됨
- [ ] **5-3. 머티리얼 체계 정리**
  - (적용 완료, Play 모드 확인 대기) 결정: 공용 세트 + 그레이박스 개선, 선체는 공통 금속·모듈 구분은 색 띠와 불빛
  - 공용 재질 `Art/Materials/Station/`: M_Hull(밝은 금속), M_HullDark, M_Window(따뜻한 발광 ×2.2), M_SolarPanel. 모듈색 `M_Greybox_X` → `M_Accent_X`(GUID 유지, 은은한 발광)
  - `Editor/StationArtBuilder`(메뉴 SpaceStation/Art/Rebuild Greybox Modules): 선체(셀 92%) + 띠 + 창문, 태양광은 패널, 배터리·창고·코어·실드·포탑 개별 형태. 셀 판정 콜라이더는 Visual 1개만 유지
  - SRP Batcher 켬, 전 재질 GPU Instancing. ModuleView는 정상 상태에서 MPB를 비워 배처 호환 유지
  - 남은 것(5-4): 비활성·파손 시 창문·띠 발광이 그대로 → Emission on/off
  - 공용 URP Lit 머티리얼 세트 제작 (금속 패널, 창문, 태양광 패널 등)
  - SRP Batcher/GPU Instancing 호환 확인
- [ ] **5-4. 상태 표현 셰이더 (Shader Graph)**
  - 고스트 셰이더 (반투명 + 틴트, 초록/빨강 전환)
  - 비활성/활성 모듈 표현 (Emission on/off)
  - 선택 하이라이트
  - MaterialPropertyBlock으로 연동
- [ ] **5-5. 모듈 모델 교체**
  - AI 모델링 결과물을 셀 규격에 맞게 정리 (피벗, 스케일)
  - URP Lit 머티리얼로 교체, 분홍색 깨짐 확인
  - 창문/불빛 부분에 Emission 적용
  - 모듈 1종씩 교체하며 톤이 맞는지 확인
- [ ] **5-6. 연결 통로/조인트 자동 생성**
- [ ] **5-7. 배경 연출 (선택)**
  - 우주 먼지 파티클, 멀리 떠다니는 소행성
  - 이벤트 화면 효과 (운석 충돌, 태양 폭풍)
- [ ] **5-8. 사운드, UI 다듬기**

## Phase 6. 확장 후보
- [ ] 포트 방식 연결 (B안) 도입
- [ ] 연구 트리, 세이브/로드, 회전 링 모듈
  - 연구소 + 연구: 난이도 확장(4-x) 완료 후. 첫 효과 후보는 이벤트 조기 경보 (GDD 10번)
