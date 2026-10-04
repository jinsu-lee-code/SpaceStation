# CLAUDE.md

## 프로젝트 개요
3D 복셀(셀 단위) 우주정거장 건설 경영 게임. Unity 기반, 1인 개발.
기획 상세는 `GDD.md`, 작업 순서는 `TASKS.md`, 수치(자원, 비용, 효율)는 `BALANCE.md`를 참고한다.
수치는 BALANCE.md와 ScriptableObject를 기준으로 하며, 코드에 임의로 값을 넣지 않는다.

## 작업 원칙
- **한 번에 하나의 기능 단위로만 작업한다.** TASKS.md의 현재 진행 중 항목만 구현한다.
- 요청받지 않은 시스템(연구 트리, 무역, 세이브/로드 등)은 미리 만들지 않는다.
- 기능 구현 후 **테스트 방법을 함께 설명**한다 (어떤 씬에서 무엇을 눌러 확인하는지).
- 에디터 작업이 필요한 부분(프리팹 생성, 씬 배치, 인스펙터 연결)은 코드로 우회하지 말고 **수동 작업 목록으로 안내**한다.
- 불확실한 설계 결정은 임의로 정하지 말고 질문한다.

## 기술 스택
- 타겟 플랫폼: PC (Windows), 마우스 + 키보드
- Unity 6000.3.25f1 (URP 17.3.0, Input System 1.20.0)
- Unity 프로젝트 폴더: `SpaceStation/` (Git 루트 아래 하위 폴더. 이 문서들은 Git 루트에 있음)
- C#, 렌더 파이프라인: URP
- 입력: Input System 패키지
- UI: uGUI (TextMeshPro)

## 폴더 구조
```
Assets/
  _Project/
    Scripts/
      Core/        # 그리드, 틱, 게임 상태
      Building/    # 배치, 고스트, 삭제
      Simulation/  # 자원, 네트워크, 이벤트
      UI/
      Data/        # ScriptableObject 정의 클래스
    Data/          # ScriptableObject 에셋 (모듈, 자원, 이벤트)
    Prefabs/
    Scenes/
    Art/
```

## 코딩 규칙
- 네임스페이스: `SpaceStation.<폴더명>` (예: `SpaceStation.Building`)
- 클래스/메서드 PascalCase, private 필드 `_camelCase`
- **게임 로직과 MonoBehaviour를 분리한다.** 그리드, 네트워크, 자원 계산은 순수 C# 클래스로 작성해 Unity 없이도 테스트 가능하게 한다.
- 수치(생산량, 비용, 소모량)는 **코드에 하드코딩하지 않고 ScriptableObject로 뺀다.**
- `Update()`에서 무거운 연산 금지. 시뮬레이션은 틱 시스템으로만 돌린다.
- `Find`, `GetComponent`를 매 프레임 호출하지 않는다.
- 이벤트는 C# `event`/`Action`으로 처리해 시스템 간 결합도를 낮춘다.

## 핵심 설계 규칙 (변경 금지)
1. 공간은 `Vector3Int` 정수 좌표 격자이다. 셀 크기는 `GridConfig`의 상수 하나로 관리한다.
2. 점유 정보는 `Dictionary<Vector3Int, ModuleInstance>` 한 곳에서만 관리한다.
3. 멀티 셀 모듈은 점유 셀 오프셋 리스트로 정의하고, 회전은 90도 단위만 허용한다.
4. **연결 규칙: 면이 인접한 모듈은 자동 연결된다** (6방향). 포트 개념은 없다.
   - 포트 방식(B안)은 폐기했다 (2026-10-03). 연결 판정은 계속 `IConnectionRule` 인터페이스 뒤에 둔다.
5. 시작 모듈(Core)에 연결된 모듈만 활성 상태이다. 끊긴 모듈은 비활성.
6. 시뮬레이션 틱은 고정 간격(기본 1초)이며, 배속 조절이 가능해야 한다.

## 아트 규칙
- 로우폴리 스타일 통일
- 모든 모듈 메시는 셀 크기에 맞춘 규격, 피벗은 모듈 원점 기준
- 그레이박스 단계에서는 Unity 기본 큐브에 색만 다르게 사용
- **Blender 모델링(특히 Phase 11 내부 키트·템플릿) 전에는 `MODELING.md`를 읽고**, 그 체크리스트(같은 평면 겹침·맞닿는 가장자리·불리언 잔여·베벨 재질/감김 등)를 지키며, 내보내기 전후 검증 절차(겹침 검사·뚫림 검사)를 돌린다. 새 문제를 겪으면 MODELING.md에 추가한다.

## 그래픽/렌더링 규칙 (URP)
- 렌더 파이프라인은 **URP로 고정**한다. HDRP, Built-in으로 변경하지 않는다.
- **그래픽 작업은 Phase 5 이전에 하지 않는다.** 프로토타입 단계에서는 기본 Lit 머티리얼만 사용한다.
- 머티리얼은 **URP Lit 또는 Shader Graph로 만든 셰이더만** 사용한다. Built-in 셰이더는 쓰지 않는다.
- 커스텀 셰이더는 코드(HLSL)보다 **Shader Graph를 우선**한다. 에디터 조작이 필요하면 만드는 방법을 단계별로 안내한다.
- 조명: Directional Light 1개(태양) + 약한 환경광. 실시간 라이트는 최소화한다.
- 포스트 프로세싱은 **글로벌 Volume 1개**로 관리한다 (Bloom, Color Adjustments, Vignette 정도만).
- 발광 표현(창문, 패널)은 Emission 머티리얼 + Bloom 조합으로 처리한다.
- 우주 배경은 스카이박스 머티리얼로 처리한다 (별/성운 텍스처 또는 Shader Graph 절차적 생성).
- 상태 표현은 머티리얼 교체가 아닌 **셰이더 프로퍼티 조절(MaterialPropertyBlock)**로 처리한다.
  - 고스트: 반투명 + 초록/빨강 틴트
  - 비활성 모듈: 어둡게 + Emission 끔
  - 선택/하이라이트: 외곽선 또는 Emission 강조
- 모듈이 많아져도 성능이 유지되도록 **GPU Instancing / SRP Batcher 호환**을 지킨다 (모듈끼리 머티리얼을 공유).
- 외부/AI 모델링 에셋을 가져오면 머티리얼을 URP Lit으로 교체하고, 분홍색 깨짐이 없는지 확인한다.
