# 다른 PC에서 이어가기

마지막 정리: 2026-10-12 (12-2 · 12-2b 완료 직후). 작업 규칙은 `CLAUDE.md` → "작업 방식" 참고.

## 1. 현재 상태
- 완료: Phase 11 전체, Phase 12 착수 전 결정(C-1 ~ C-7), **12-0b**(시뮬레이션 구조 정리), **12-1**(등급 틀 · 누적 결과 화면 · ★ 배너), **12-2**(측정 도구 확장 — 구간 시작 상태 36개 저장소에 포함), **12-2b**(큰 정거장 틱 성능)
- 다음: **12-3 합성 시스템** (첫 레시피 = 연구소 계통 1단계, 관문 C-4: 그레이박스 레시피로 재미 확인)
- 긴 측정은 Balance Simulator 창의 백그라운드 버튼(또는 `BalanceSimulatorWindow.StartSegment` 등)으로 — MCP로 동기 실행하면 요청이 시간 초과되고 Unity가 멈춘다
- 버전 계획: 12-7 도시급 = v0.12, 12-8 = v0.13, 12-9 = v0.14, 지구급 + 샌드박스 · 도전 과제 = v1.0.0
- 테스트: EditMode 335개 통과 (slot3 세이브 읽기 경고 2개는 원래 PC의 시험용 손상 세이브 때문이며, 다른 PC에서는 나오지 않을 수 있음)

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
| 세이브 슬롯 | `%USERPROFILE%\AppData\LocalLow\BinaryCompany\Another Earth\` | 플레이 확인용 진행 상태. 새 PC에서는 새 게임으로 시작 |
| 영상 | `Presentation/AnotherEarth_Teaser.mp4`, `AnotherEarth_PlayTour.mp4`, `tour_stills/` | GitHub에 올리지 않기로 함. 다시 만들려면 메뉴 SpaceStation/Teaser/Record · Record Play Tour |
| 에셋 스토어 참고 패키지 | `SpaceStation/Assets/Holograms/`, `Assets/Shield Shader FREE/` | 게임에서 쓰지 않음 (참고용). 필요하면 에셋 스토어에서 다시 받기 |
| 밸런스 봇 리포트 | `SpaceStation/BalanceReports/` | 다시 돌리면 같은 시드 = 같은 결과 |
| 빌드 | `SpaceStation/Builds/` | 다시 빌드 (메뉴 SpaceStation/Build/Build Windows) |
| Blender 백업 · 미리보기 | `BlenderWork/**/*.blend1`, `preview/` | 자동 생성물 |
| 사람 주민 보관본 | `unity\_archive\SpaceStation_HumanResident\` + 로컬 태그 `backup/before-human-cleanup` | 쓰지 않음. 태그는 원격에 push 금지 |
| Claude 메모리 | `%USERPROFILE%\.claude\projects\...\memory\` | 내용은 `CLAUDE.md` "작업 방식"에 옮겨 둠 |

## 4. 새 PC에서 첫 요청 예시
> SpaceStation 다른 PC에서 이어서 작업할게. HANDOFF.md 확인하고 12-3 시작하자
