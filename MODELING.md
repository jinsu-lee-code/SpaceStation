# MODELING.md — 내부 모델링 체크리스트

> **2026-11-11 나눔**: 한 파일(445줄 · 164KB)이 너무 길어 내보내기 전마다 모든 규칙을 확인하지 못하고 놓치는 일이 생겨, 사용자 요청으로 `docs/modeling/`의 주제 파일로 나눴다 (절 단위로 한 글자도 바꾸지 않고 그대로 옮김, 원래 절 번호 · 제목 유지). **모델링 전 · 내보내기 전마다 `docs/modeling/00-checklist.md`를 처음부터 끝까지 확인하고, 해당 주제 파일을 읽는다.** 새 문제를 겪으면 주제 파일에 자세히 적고 체크리스트에 한 줄을 더한다.

- [`00-checklist.md`](docs/modeling/00-checklist.md) — 꼭 볼 규칙 62줄 (면 · 메시 / 배치 · 겹침 / 걷기 · 동선 / 물 · 연출 · 빛 · 재질 / 형태 / 내보내기 · Blender 자동 검사 / Unity 플레이 확인), 줄마다 확인 방법 · 상세 위치
- [`01-geometry.md`](docs/modeling/01-geometry.md) — 머리말 (Mesh Baker 실효성 · 주민 Mesh LOD · 작업 파일) · 1 좌표 · 면 방향 · 2 베벨 · 3 같은 평면 겹침 · 4 맞닿는 가장자리 · 5 불리언 · 6 얇은 수직면 · 6-1 소품 재질 · 형태
- [`02-space.md`](docs/modeling/02-space.md) — 7 공간 구성 (문 자리 예약 구역 · 벽 뚫림 · 콘솔 방향 · 열리는 면 · 화면 · 톱니 · 장비 특징 · 문 패널 주변)
- [`03-animals-props-plants.md`](docs/modeling/03-animals-props-plants.md) — 7-1 동물 주민 · 7-2 손에 드는 소품 · 7-3 홀로그램 모형 (글꼴 글자 포함) · 7-4 벽 상태 소품 · 7-5 식물 · 7-6 움직이는 계층 모델
- [`04-exterior.md`](docs/modeling/04-exterior.md) — 7-7 바깥 모듈 외형 · 7-9 진화 모듈 외형 · 7-10 진화 모듈 단계별 테마 · 2단계 이상 내부 아이디어 (체험 8가지 · 탈 수 있는 놀이기구)
- [`05-interior-templates.md`](docs/modeling/05-interior-templates.md) — 7-8 새 기본 모듈 내부 템플릿 (배관 · 진열 · 과일 · 문 · 화면 빛 · 주민 길 · 벽 소품 자리 · 경첩 문 · 기울인 판)
- [`06a-evo-interiors-common.md`](docs/modeling/06a-evo-interiors-common.md) — 7-11 진화 모듈 내부 템플릿 ① 공통 · 1단계 (단계 키트 · 창 · 여러 층 엘리베이터 · 식물 · 걷는 길 · 통 속 물 · 상태 화면 · 나르는 장치 · 유리 돔)
- [`06b-evo-interiors-open.md`](docs/modeling/06b-evo-interiors-open.md) — 7-11 ② 2단계 개방형 방 공통 + 그리드 허브 · 융합 플랜트 · 아콜로지 · 대형 토러스 · 시민 회관 · 치안 본부 · 바이오돔 · 식량 공장
- [`06c-evo-interiors-rides-stage3.md`](docs/modeling/06c-evo-interiors-rides-stage3.md) — 7-11 ③ 정련 콤비나트 · 실드 돔 · 요새 포대 · 차폐벽 · 드론 정비창 · 칸 경계 · 후룸 · 회전목마 · 미로 · 3단계 궤도 도시 · 정원 동물 · 바이오스피어
- [`08-verification.md`](docs/modeling/08-verification.md) — 8 검증 절차 (겹침 · 경계 · 문 자리 · 벽 뚫림 · Unity 뚫림 · 밀폐 · 떠 있는 조각 · 앉기 · 엘리베이터 · 칸 경계 · 걷기 지도)
