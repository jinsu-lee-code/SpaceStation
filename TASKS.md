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
- [ ] **3-2. 이벤트 시스템** (이벤트 SO + 랜덤 발생기)
  - (코드 완료, Play 모드 확인 대기) `GameEventData`(SO, 3-3에서 상속해 효과 필드 추가) + `EventScheduler`(순수, 난수 주입) + `EventController`(Simulation 오브젝트, 틱 연동)
  - BALANCE 9번: 유예 180초, 간격 90~150초, 가중치 랜덤, 겹침 허용(같은 이벤트 중복 금지)
  - 에셋 `Data/Events/EV_Meteor·OxygenLeak·SolarStorm·SupplyShip` (효과 없음, 가중치·지속시간 임시)
  - HUD: 상단 `EventBanner`, 자원 패널 `ActiveEventList`. 디버그 F5 = 즉시 발생 (Phase 4 이후 제거)
- [ ] **3-3. 이벤트 4종 구현**
  - 운석 충돌 구현 시 모듈 파손 상태 도입: 외곽 판정(노출 면 여부), 코어 면역
  - 파손 효과(효율 50%, 산소 누출), 수리(금속 30%, 10초), 120초 방치 시 파괴
  - 파괴로 인한 분리는 2-1 연결 판정 재사용
- [ ] **3-4. 정거장 등급 및 승패 조건**
  - 등급 조건 (인구/모듈 수)을 ScriptableObject로 정의, 등급별 채굴 도킹 최대 수 연동
  - 대형 등급 도달 시 결과 화면 + 계속 플레이 옵션
  - 인구 0 시 게임 오버

## Phase 4. 재미 검증 (여기서 멈추고 플레이)
- [ ] 30분 플레이 테스트
- [ ] 체크: 배치가 직관적인가? 자원 균형을 맞추는 게 재미있는가? 위기가 긴장감을 주는가?
- [ ] 수치 밸런싱 (ScriptableObject 수정만으로 가능해야 함)
- [ ] 결과에 따라 GDD 수정

## Phase 5. 비주얼 교체 (재미 검증 후)
기본 원칙: 아래 순서대로 진행하면 적은 노력으로 룩이 크게 바뀐다.

- [ ] **5-1. 조명과 배경 (수동 + Claude Code 안내)**
  - Directional Light 1개 세팅 (각도, 색, 강도)
  - 우주 스카이박스 머티리얼 적용 (별/성운)
  - 환경광 색을 어두운 톤으로 조정
  - 완료 조건: 그레이박스 정거장이 우주 배경 위에 또렷하게 보임
- [ ] **5-2. 포스트 프로세싱**
  - 글로벌 Volume 생성: Bloom, Color Adjustments, Vignette
  - 완료 조건: 밝은 부분이 은은하게 번지고 전체 톤이 통일됨
- [ ] **5-3. 머티리얼 체계 정리**
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
