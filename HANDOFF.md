# 다른 PC에서 이어가기

마지막 정리: 2026-10-15 (두 판 분할 셋업, [SPLIT.md](SPLIT.md)). 작업 규칙은 `CLAUDE.md` → "작업 방식" 참고.

## 0. 두 판 분할 — PC별 담당 브랜치 (2026-10-15)
Another Earth를 **서사판**(스토리 캠페인)과 **타이쿤판**(경영 시뮬레이션) 두 갈래로 나눠 만들고, 나중에 한 게임 두 모드로 합친다. 자세한 규칙은 [SPLIT.md](SPLIT.md).

| 브랜치 | 내용 | 담당 PC | 설계 기준 |
|---|---|---|---|
| `main` | 공통 바닥 (SPLIT.md 3번) — 바닥 수정은 여기서 하고 두 브랜치로 합침 | 둘 다 | — |
| `narrative` | 서사판 — 방주 · 코어 AI · 7장 | **원래 PC** (`C:\Users\wlstn\desktop\unity\SpaceStation`) | [REDESIGN.md](REDESIGN.md) |
| `tycoon` | 타이쿤판 — 해금 선택 · 성장 비례 위협 | **다른 PC** | [TYCOON.md](TYCOON.md) |

- 기준점: 태그 `baseline-before-rework` (7b47144, 12-5 ② 진행 중 빌드). 두 판 모두 여기서 출발
- 각 브랜치의 `CLAUDE.md` 맨 위에 어느 판인지 적혀 있다. 세션 시작 때 지금 브랜치를 먼저 확인한다
- 커밋 메시지 형식은 그대로 (`MM.DD/작업번호/요약`). 브랜치마다 Phase 13부터 시작 — 같은 번호라도 브랜치가 다르면 다른 작업

### 다른 PC에서 `tycoon` 브랜치 받기
1. 아래 2번(새 PC 준비)대로 저장소를 클론했거나 이미 있으면 **GitHub Desktop → Fetch origin**
2. GitHub Desktop 위쪽 **Current branch → `tycoon`** 선택 (원격 브랜치를 처음 고르면 로컬 브랜치가 만들어짐)
   - 명령으로는: `git fetch origin` → `git switch tycoon`
   - 예전에 받은 `main`이 있으면: 2026-10-15에 원격 `main` 기록을 다시 썼다(서사판 13-1 커밋 3개를 `narrative`로 옮김). 로컬 `main`에 따로 만든 커밋이 없다면 `git switch main` → `git reset --hard origin/main`으로 맞춘다
3. Unity를 열면 브랜치 내용으로 다시 가져오기(import)가 돈다. Unity를 연 채로 브랜치를 바꾸지 말고 닫고 바꾸는 것이 안전
4. Claude에게 첫 요청: "SpaceStation 다른 PC에서 tycoon 브랜치 작업 시작할게. SPLIT.md · TYCOON.md 읽고 진행하자"

## 1. 현재 상태 (기준점 = `main`)
- 완료: Phase 11 전체, Phase 12의 12-0 ~ 12-4 (등급 틀 · 측정 도구 · 합성 시스템 + 보조 · 등급별 핵심 규칙(유성우 · 정책 · 사회 위기 · 열 · 방문 세력) · 연구 확장)
- 멈춤: **12-5 새 기본 모듈 14종** — 데이터 · 그레이박스 · 외형 모델 8종(학교 · 구조 프레임 · 연결 허브 · 센서 어레이 · 검역소 · 단백질 배양실 · 물 추출기 · 데이터 센터) + 기존 모듈 상징 마크까지. 이후는 판마다 다시 정한다 (서사판 V3 = 장 · 종족에 연결되는 것만 / 타이쿤판 TYCOON.md)
- 판별 진행 (2026-10-15): `narrative` = Phase 13 (1~3장 수직 단면, 13-1 장 틀 플레이 확인 대기 · 13-2 코어 성격 완료) / `tycoon` = Phase 13 (13-0 ~ 13-4 완료, 다음 13-5 기본 모듈 배치)
- 공통 바닥 수정 → `main` → 두 판으로 합침. 합칠 때 충돌 · 해결 체크리스트 = [MERGE_NOTES.md](MERGE_NOTES.md) (체크도 `main`에서), 타이쿤판만의 바닥 덧붙임 = `tycoon`의 MERGE_NOTES_TYCOON.md
- 주의: Claude가 MCP로 플레이 모드를 시험하면 종료 때 자동 저장이 사용자 자동 저장 슬롯을 덮어씀 → 시험 전 세이브 폴더 백업 · 시험 후 복원
- 긴 측정은 Balance Simulator 창의 백그라운드 버튼(또는 `BalanceSimulatorWindow.StartSegment` 등)으로 — MCP로 동기 실행하면 요청이 시간 초과되고 Unity가 멈춘다
- 테스트 (2026-10-15): `main` EditMode 391개 · `narrative` 398개 · `tycoon` 405개 통과. 봇 기준선 = `BalanceSimulatorWindow.RunWithDefaults(30, 20, 1)`
  - ~~⚠ 2026-10-15 확인: 기준점에서 봇 노멀 30분이 5판 모두 약 5분에 게임 오버(물 고갈)~~ → **해결 (2026-10-15, `main`)**: 봇이 역할마다 목록 첫 모듈을 골라 물 추출기(도시급)를 물 담당으로 잡았음 → 가장 이른 등급에 해금되는 모듈로. 60분 × 6판 게임 오버 0, 소형 3.7분 · 중형 17.4분 · 대형 45.6분(5/6)
  - 봇 합성 확장 (2026-10-15): 2단계 레시피(진화 모듈 재료)를 규격 안 진화 모듈 자리마다 1단계 재료를 깔고 그 자리에서 1단계 합성 → 2단계 합성 순으로 계획

## 2. 새 PC 준비
1. **Unity Hub → Unity 6000.3.25f1** 설치 (Windows Build Support (Mono) 포함)
2. **GitHub Desktop** 설치 → 로그인 → `jinsu-lee-code/SpaceStation` 클론
   - 권장 위치: `C:\Users\<사용자>\desktop\unity\SpaceStation` (원래 PC와 같은 구조: `unity` 베이스 폴더 아래 프로젝트 폴더)
3. Unity Hub에서 **`SpaceStation/SpaceStation`** 폴더(저장소 안의 Unity 프로젝트)를 연다. 첫 실행 때 Library를 새로 만들어 오래 걸림
   - 패키지(`Packages/manifest.json`)는 자동 설치: URP 17.3, Input System 1.20, Recorder 5.1.7, AI Assistant(`com.unity.ai.assistant` — Unity MCP용) 등
4. 씬 `Assets/_Project/Scenes/MainMenu` → 플레이로 동작 확인
5. **Claude 데스크톱 앱(Code 탭)** 에서 `unity` 베이스 폴더를 작업 폴더로 연다
   - 베이스 폴더 `CLAUDE.md`(여러 프로젝트 공통 지침)는 저장소 밖이라 새 PC에는 없다. 필요하면 아래 6번 내용으로 새로 만든다.
6. MCP 연결 (Claude 앱 설정 → 개발자 → `claude_desktop_config.json`):
   ```json
   {
     "mcpServers": {
       "unity-mcp": { "command": "C:\\Users\\<사용자>\\.unity\\relay\\relay_win.exe", "args": ["--mcp"] },
       "blender":   { "command": "cmd", "args": ["/c", "uvx", "blender-mcp"] }
     }
   }
   ```
   - `relay_win.exe`는 Unity에서 AI Assistant 패키지의 MCP 설정(Project Settings → AI → MCP)을 켜면 `%USERPROFILE%\.unity\relay\`에 설치된다. 경로를 확인해 넣는다.
   - Blender MCP는 Blender 모델링 작업 때만 필요 (`uv` 설치 + Blender에 blender-mcp 애드온).

### 베이스 폴더 CLAUDE.md (unity\CLAUDE.md) 요지
- `unity` 폴더 아래 최상위 폴더 하나 = 프로젝트 하나 (폴더 이름 = 프로젝트 이름). 세션 시작 때 프로젝트를 정하고 그 폴더의 `CLAUDE.md`를 먼저 읽는다.
- 5시간 이상 지나서 요청이 오면 해당 프로젝트 `CLAUDE.md`와 `TASKS.md`를 다시 읽고, 어떤 프로젝트로 판단했는지 한 줄로 알린다.

## 3. 저장소에 없는 것 (원래 PC에만 있음)
필요하면 USB · 클라우드로 직접 옮긴다. 없어도 작업 · 빌드에는 지장 없음.

| 항목 | 원래 PC 위치 | 비고 |
|---|---|---|
| 세이브 슬롯 | `%USERPROFILE%\AppData\LocalLow\BinaryCompany\Another Earth\` | 플레이 확인용 진행 상태. 새 PC에서는 새 게임으로 시작. 시험용 도시급 저장(수동 저장 3)은 메뉴 `SpaceStation/Recipes/Test Save: City Start → Slot 3`로 다시 만듦. 타이쿤판은 세이브 경로를 분리할 예정(SPLIT.md S-7) |
| 영상 | `Presentation/AnotherEarth_Teaser.mp4`, `AnotherEarth_PlayTour.mp4`, `tour_stills/` | GitHub에 올리지 않기로 함. 다시 만들려면 메뉴 SpaceStation/Teaser/Record · Record Play Tour |
| 에셋 스토어 참고 패키지 | `SpaceStation/Assets/Holograms/`, `Assets/Shield Shader FREE/` | 게임에서 쓰지 않음 (참고용). 필요하면 에셋 스토어에서 다시 받기 |
| 밸런스 봇 리포트 | `SpaceStation/BalanceReports/` | 다시 돌리면 같은 시드 = 같은 결과 |
| 빌드 | `SpaceStation/Builds/` | 다시 빌드 (메뉴 SpaceStation/Build/Build Windows) |
| Blender 백업 · 미리보기 | `BlenderWork/**/*.blend1`, `preview/` | 자동 생성물 |
| 사람 주민 보관본 | `unity\_archive\SpaceStation_HumanResident\` + 로컬 태그 `backup/before-human-cleanup` | 쓰지 않음. 태그는 원격에 push 금지 |
| Claude 메모리 | `%USERPROFILE%\.claude\projects\...\memory\` | 내용은 `CLAUDE.md` "작업 방식"에 옮겨 둠 |

## 4. 새 PC에서 첫 요청 예시
> SpaceStation 다른 PC에서 tycoon 브랜치 작업 시작할게. HANDOFF.md · SPLIT.md · TYCOON.md 읽고 Phase 13 착수하자
