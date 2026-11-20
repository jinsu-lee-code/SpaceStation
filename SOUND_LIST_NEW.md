# 사운드 목록 (새로) — 2차 개편 (2026-11-20)

> REVIEW_1115 2번 "사운드 전면 개편" 결정 (11-15): **VARCO Sound 커넥터로 만들기 → `AudioOriginals/build_audio.py`(Blender 파이썬)로 다듬기 → `SoundLibrary.asset`에 연결 → 플레이 확인.**
> 이 문서 = 순서 ② "소리 목록 새로 만들기": (a) 소리가 없는 시스템 · (b) 기존 교체 후보 · (c) 음량 균형.
> 조사 범위 (11-20): 스크립트 전체 (`Scripts/` 바깥 · 내부 · UI) + 서사판 `narrative` 브랜치 + REVIEW_1115 · TASKS의 앞으로 할 일. 1차 목록(10/01)은 `SOUND_LIST.md` 그대로 (기록).

## 한눈에

| 묶음 | 소리 (이름 기준) | 지금 |
|---|---|---|
| 1. 기존 소리 교체 | 29 | 66파일 중 32개가 코드로 합성한 임시음 + UI 소리 한 벌 다시 만듦 |
| 2. 공통 시스템 — 바깥 | 30 | 합성 · 이민선 · 열 · 역병 · 정책 · 접촉 · 습격 포격 등 무음 또는 다른 소리 재사용 |
| 3. 바깥 진화 연출 | 15 | 전부 무음 |
| 4. 1인칭 내부 공통 | 62 | 문 · 엘리베이터 · 이동 장치 무음, 발소리는 바닥과 상관없이 금속 |
| 5. 체험 · 미니게임 | 9 공용 + 약 55 대표 | 40곳이 UI 소리를 빌려 씀 |
| 6. 서사판 | 24 | 전부 무음 (서사판 SoundLibrary = 타이쿤판과 같음) |
| 7. 음악 | 20곡 | 처음 넣은 5곡 그대로 |
| 8. 앞으로 만들 기능 | 25 | 기능 만들 때 같이 |
| 9. 음량 균형 | — | 할 일 목록 |
| **합계** | **효과음 약 245 + 음악 20곡** | 변형(`_01~03`)까지 치면 파일은 더 많음 |

우선: **A** = 먼저 (자주 들림 · 비어서 티 남 · 이미 만든 기능) / **B** = 다음 (체험 · 연출 전용) / **C** = 기능 만들 때 같이.

## 조사 결과 (지금 상태)

- 소리 파일 66개 중 **32개는 받은 소리가 아니라 `build_audio.py`가 합성한 임시음**: 배치 · 철거 · 통로 · 정비 · 운석 셋 · 레이저 · 실드 · 파손 · 스파크 셋 · 경보 · 등급 하락 · 승리 · 거절 · 일시정지 / 재개 · 발소리 6 · 방 환경음 6 · 내부 경보.
- 진화 체험 · 미니게임 · 내부 할 일 40곳이 전용 소리 없이 **UI 소리를 빌려 씀**: 창 열기 73번 · 닫기 48번 · 거절 23번 · 탭 18번 · 등급 상승 15번 · 좋은 이벤트 15번 · 보급선 9번. 예: 기차 출발 = 보급선 소리, 폭죽 = 운석 폭발, 탑승 = 창 열기.
- 소리가 **아예 없는 곳**: 바깥 진화 연출 전부 (오로라 · 번개 · 불꽃놀이 · 드론 쇼 · 후룸 · 비 · 빛기둥 · 순찰차) · 합성 · 내부 자동문 · 유리 엘리베이터 · 위아래 이동 장치 · 이민선 · 화물선 · 서사판 장 시스템 전부.
- 1차 목록 중 안 만든 것: `rebuild` · `population_up` · `music_large_loop`.

## 만들 때 규칙

- 도구: 효과음 = `varco_text_to_sound` (한 번에 후보 3~5개 — 값은 한 번 요금이라 여러 개 받기) · 루프 = 만든 뒤 `varco_looping` · 변형 `_01~03` = `varco_variation` · 모노 → 스테레오 = `varco_mono2stereo` · 음악 = `varco_music_generate` (약 2분 mp3, 효과음 도구에 넣지 않음).
- 프롬프트는 영어: 만들 때 아래 표의 "느낌"을 영어로 옮겨 씀. 원본은 `AudioOriginals/` 같은 폴더 구조로 보관.
- 규격은 `SOUND_LIST.md` 1차 공통 규격 그대로 (효과음 모노 · 앞 무음 없이 · 피크 −1 dBFS · `_loop`는 이음새 없이 · 자주 나는 소리는 변형 2~3개).
- **단계별 톤** (모듈 단계 테마): 기본 = 생존 공업 (묵직한 금속 · 압력) / 1단계 = 발달한 미래 기술 (깨끗한 전자음 · 자기 부상) / 2단계 = 여가 테마파크 (밝은 종 · 오르골 · 놀이기구) / 3단계 이상 = 진주 · 금 동화 (유리 · 수정 · 물 · 자연).
- 바깥 진화 연출 소리는 **그 모듈을 고르거나 카메라가 가까울 때만** (정거장 전체 루프가 한꺼번에 울리면 시끄러움) — 거리 감쇠 + 동시 재생 수 상한.
- 체험 · 미니게임은 **공용 꾸러미(5-1) + 체험마다 대표 소리 1~3개**로 수를 줄임.
- 새 폴더: `Audio/Fx/` (바깥 진화 연출) · `Audio/Ride/` (체험 · 미니게임) · `Audio/Campaign/` (서사판) · `Audio/Music/`.

## 1. 기존 소리 교체 (b)

### 1-1. 합성 임시음 → 교체 (32파일)

| 우선 | 파일 | 쓰는 곳 | 바꿀 느낌 |
|---|---|---|---|
| A | `build_place` | 모듈 배치 | 도킹 "철컹" + 압력 "칙" (지금 가벼운 합성음) |
| A | `build_remove` | 철거 | 분리 "철컥" + 짧은 감압 |
| A | `build_connect` | 통로가 이어짐 | 에어록 잠기는 "치익-툭" |
| B | `maintain` | 정비 | 렌치 · 볼트 조이기 |
| A | `meteor_incoming` | 운석 접근 | 불타며 다가오는 "쉬이익" |
| A | `meteor_impact` | 운석 충돌 | 묵직한 금속 충격 + 파편 |
| A | `meteor_explode` | 격추된 운석 공중 폭발 | 가벼운 "퍽" |
| A | `turret_laser` | 포탑 격추 | 짧고 날카로운 레이저 |
| A | `shield_deflect` | 실드가 튕겨냄 | 에너지 장막 "우웅-팅" |
| A | `module_damaged` | 모듈 파손 | 경고 "삐빅" + 금속 균열 |
| B | `storm_spark_01~03` | 태양 폭풍 스파크 · 내부 스파크 · 사고 | 짧은 전기 "파직" |
| A | `alarm_loop` | 실패 조건 경고 | 사이렌 (지금 −10.6 dB로 작음) |
| B | `grade_down` | 등급 하락 | 하강하는 아쉬운 음 |
| B | `victory` | 대형 등급 첫 도달 | 밝은 성취 음악 짧게 |
| A | `ui_error` | 거절 (23곳에서 씀) | 낮은 "부-웅" 두 번 (지금 −12 dB로 작음) |
| B | `ui_pause` · `ui_resume` | 일시정지 · 재개 | 테이프 멈춤 · 재생 (지금 배속 소리를 잘라 씀) |
| A | `step_metal_01~06` | 1인칭 발소리 | 금속 바닥 (→ 4-1 바닥별 발소리와 함께) |
| A | `amb_room_life/water/machine/heat/electric/hall_loop` | 방 환경음 6묶음 | 12초 합성 루프 → 30~60초 실제 질감 |
| A | `interior_alarm_loop` | 파손된 방 경보 | 내부 경보 (지금 −18 dB) |

### 1-2. 받은 소리 (34파일) — UI 한 벌은 다시 만들고 나머지는 대부분 두기

| 우선 | 파일 | 판단 |
|---|---|---|
| A | 음악 5곡 (`amb_mainmenu · in_game · space · night · crisis`) | 처음 넣은 그대로 · 가장 오래 들림 → 새로 만들어 교체 (7번) |
| A | `ui_click_01~04` · `ui_hover_01~02` · `ui_tab` · `ui_open` · `ui_close` · `ui_speed_up` | **다시 만듦 (사용자 2026-11-20)**: 홀로그램 UI 톤으로 한 벌 맞춤 — 1-1의 `ui_error` · `ui_pause` · `ui_resume`과 같은 때 같은 톤으로. 체험이 빌려 쓰던 자리는 전용 소리(5번)로 옮기므로 UI 소리는 UI에만 씀 (`ui_open` · `ui_close`는 지금 한 파일을 잘라 씀 → 따로 만듦) |
| C | `event_positive · event_negative · event_end · early_warning · game_over · grade_up · module_destroyed · oxygen_leak · resource_depleted · solar_storm_loop · supply_arrive` | 두기. 새 소리와 나란히 들어 보고 어색하면 교체 |
| — | `amb_station_loop` · `build_select` · `build_rotate_01~04` · `repair_start` · `repair_done` | 두기 |

### 1-3. 1차 목록 중 안 만든 것

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| B | `rebuild` | 재건축 | 철거 + 배치가 빠르게 (지금 두 소리를 이어 붙임) |
| C | `population_up` | 주민 증가 | 작은 밝은 "핑" |
| B | `music_large_loop` | 중형 · 대형 등급 음악 레이어 | → 7번 음악 |

## 2. 소리 없는 공통 시스템 — 바깥 (a)

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| A | `synth_start` | 합성 시작 (비행 고리가 날아와 앉음) | 기계 착지 + 공구 시동 |
| A | `synth_loop` | 합성 진행 중 | 용접 · 조립 루프 |
| A | `synth_done_1` · `_2` · `_3` | 합성 완료 · 진화 모듈 등장 | 단계가 오를수록 화려하게 |
| B | `module_move_pick` · `module_move_drop` | 12-3b 모듈 옮기기 | 집어 올림 · 내려놓음 |
| A | `ship_immigrant_arrive` | 이민선 (여객 도킹 포트 · 서사판 방주 이민선 같이) | 큰 배 접근 → 도킹 집게 → 에어록 |
| B | `ship_cargo_dock` | 화물 터미널 화물선 | 무거운 화물선 도킹 |
| B | `crate_appear` | 보급 상자 생김 | 작은 낙하 · 착지 |
| B | `meteor_shower_warn` | 유성우 방향 예보 (지금 조기 경보 재사용) | 레이더 경고 + 다가오는 느낌 |
| B | `heat_warning` | 열 과열 (대륙급) | 끓는 듯한 경고 |
| B | `plague_outbreak` · `plague_cured` | 역병 | 낮은 불길음 · 회복음 |
| B | `social_crisis` | 사회 위기 선택지 | 긴장 스팅 |
| B | `policy_enact` | 정책 시행 | 결재 도장 + 전자음 |
| B | `research_done` | 연구 완료 (지금 좋은 이벤트 소리) | 데이터 완성 차임 |
| B | `unlock_reveal` | 해금 선택 카드 | 카드 펼침 |
| B | `contact_arrive_1~3` | 외부 접촉 세력 3곳 방문 | 세력마다 다른 신호음 |
| B | `contact_result` | 교역 · 협상 · 경계 결과 | 짧은 확인음 |
| B | `raid_incoming` · `raid_impact` | 습격 포격 | 포탄 접근 · 폭발 (운석과 구별되게) |
| B | `shield_deflect_hex` · `_aurora` · `_crystal` | 실드 1 · 2 · 3단계 (매트릭스 · 오로라 등대 · 궤도 방어망) | 단계마다 조금씩 세게 |
| B | `prism_beam` | 3단계 궤도 방어망 격추 광선 | 굵은 빛 광선 |
| B | `dawn` · `dusk` | 낮 · 밤 바뀜 | 아주 작은 전환음 |
| C | `grade_step` | 13-7 등급 중간 단계 | 작은 상승 |
| C | `auto_maintain` | 정비 자동화가 처리함 | 작은 기계음 |

## 3. 바깥 진화 연출 (a) — 전부 무음

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| B | `fx_aurora_loop` | 오로라 (실드 돔 · 오로라 등대) | 유리 종 같은 잔잔한 울림 |
| B | `fx_lightning_01~03` | 그리드 허브 번개 정원 | 전기 "지직" + 짧은 천둥 |
| B | `fx_firework_launch` · `fx_firework_burst_01~03` | 요새 포대 불꽃놀이 | 쉬익 상승 · 펑 + 타닥 |
| B | `fx_droneshow_loop` | 드론 정비창 드론 라이트 쇼 | 드론 떼 웅웅 + 반짝임 |
| B | `fx_flume_loop` · `fx_flume_splash` | 화물 환적장 은하수 후룸 | 물살 · 큰 물보라 |
| B | `fx_boat_loop` | 궤도 항만 가리비 배 | 잔잔한 물살 |
| B | `fx_patrol_hover_loop` · `fx_siren` | 치안 본부 순찰차 · 경광등 | 부양 엔진 · 부드러운 경광음 |
| B | `fx_lightbeam_loop` | 궤도 미러 빛기둥 | 낮은 빛 울림 |
| B | `fx_rain_loop` | 바이오스피어 구름 고리 비 | 빗소리 |
| C | `fx_sparkle` | 진화 모듈 반짝이 입자 | 작은 반짝 |
| C | `fx_spin_loop` | 회전 링 (관람차 · 은하철도 링 등) | 낮은 회전음 |
| C | `fx_bees_loop` | 3 · 4단계 꿀벌 · 동물 | 벌 윙윙 |

## 4. 1인칭 내부 공통 (a)

### 4-1. 이동 · 문

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| A | `door_open` · `door_close` | 자동 미닫이 문 (모든 방) · 검역소 미닫이 | 공기 "슉" + 문짝 미끄러짐 — 지금 무음 |
| B | `door_vault` | 방사선 차폐실 금고문 | 무거운 잠금 해제 |
| A | `lift_button` · `lift_move_loop` · `lift_arrive` | 유리 엘리베이터 | 버튼 · 부드러운 상승 · "띵" |
| A | `transit_open` · `transit_ride_0~4` · `transit_arrive` | 위아래 이동 장치 (단계별 연출 5종) | 장치 켜짐 · 단계별 이동 (증기 · 자기장 고리 · 놀이기구 · 꽃바람 · 성운) · 도착 |
| B | `lightbridge_loop` | 문 승강판 · 빛 다리 (화물 환적장 물길) | 빛 판 웅웅 |
| A | `interior_enter` · `interior_exit` | 내부 들어가기 · 나가기 (지금 창 열기 · 닫기) | 전환 "우웅-" |
| A | `step_grass` · `step_wood` · `step_stone` · `step_water` · `step_glass` (각 4~6) | 2 · 3단계 정원 · 나무 다리 · 판석 길 · 얕은 물 · 유리 바닥 | 바닥 재질별 발소리 — 지금은 잔디 위도 금속 발소리 |
| B | `suit_helmet` · `suit_breath_loop` | 2단계 개방형 정원 우주복 | 헬멧 잠김 · 아주 작은 숨소리 |
| C | `land` | 높은 곳에서 떨어져 착지 | 작게 |

### 4-2. 휴대 패드 · 방 안 표시

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| B | `pad_raise` · `pad_lower` | 패드 꺼내기 · 넣기 (지금 창 열기 · 닫기) | 홀로그램 패드 켜짐 · 꺼짐 |
| B | `pad_holo_loop` | 패드 홀로그램 모형 | 작은 웅 |
| C | `board_chime` | 여객 도착 안내판 · 화물선 안내판 | 안내 "딩동" |
| C | `queue_ding` | 행정 관제소 번호 대기 | 번호 바뀔 때 "띵동" |
| C | `radar_ping_loop` | 센서 어레이 레이더 | 작은 핑 |
| C | `radiation_tick` | 방사선 차폐실 경보판 | 가이거 틱 |

### 4-3. 1인칭 할 일 (수리 · 사고 · 요청 · 보급)

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| A | `hold_loop` · `hold_done` | F 길게 누르기 (현장 수리 · 사고 · 보급 공통) | 차오르는 음 · 완료 |
| A | `fieldrepair_loop` | 현장 긴급 수리 (뜯긴 패널 · 터진 배관) | 공구 · 용접 |
| A | `pipe_burst_loop` | 터진 배관 · 누출 사고 | 가스 "쉬이익" |
| A | `valve_turn` | 누출 사고 밸브 잠그기 | 밸브 돌림 + 멈춤 |
| A | `breaker_flip` | 차단기 사고 스위치 올리기 | 큰 "딸깍" + 전기 돌아옴 |
| A | `fire_loop` · `extinguish` | 작은 화재 · 소화기 | 불 타닥 · 분사 "쉬익" |
| A | `filter_swap` | 환기 경보 필터 교체 | 뚜껑 열고 끼움 |
| B | `incident_alert` | 사고 생김 (지금 조기 경보 재사용) | 짧은 경고 |
| B | `request_pop` · `request_done` | 주민 부탁 말풍선 · 완료 | 뿅 · 고마운 차임 |
| B | `water_pour` · `vent_fix` | 화분 물 주기 · 환풍구 손보기 | 물 붓기 · 덜컹 → 조용 |
| B | `voice_{종족}_01~03` | 동물 주민 8종 한마디 (요청 · 말동무 · 서사판 한마디) | 짧고 귀여운 울음 (말소리 아님) |
| B | `crate_pickup` | 보급품 줍기 (지금 보급선 소리) | 상자 들어 올림 |
| B | `drone_breach` · `drone_hover_loop` · `drone_down` | 습격 침입 드론 내부 (지금 스파크) | 선체 뚫음 · 비행 · 격추 |
| C | `sit` · `stand` | 벤치 앉기 | 천 · 의자 |
| C | `telescope_zoom` | 전망실 망원경 | 렌즈 돌림 |

### 4-4. 방 환경음 · 기계 소리 추가 (진화 내부)

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| A | `amb_room_garden_loop` | 2 · 3단계 개방형 정원 | 바람 · 잎 · 새 |
| A | `amb_room_shore_loop` | 궤도 항만 · 궤도 방어망 해안 | 파도 · 물새 |
| B | `amb_room_nightwood_loop` | 수리 사령부 · 바이오스피어 반딧불 숲 | 풀벌레 |
| B | `amb_room_park_loop` | 2단계 놀이 시설 | 먼 놀이공원 소리 · 웃음 |
| B | `amb_room_crystal_loop` | AI 중추 · 연구원 데이터 미로 | 유리 공명 |
| B | `waterfall_loop` · `fountain_loop` | 궤도 도시 폭포 · 인어 분수 · 물 추출기 | 물 떨어짐 (가까이에서만) |
| B | `conveyor_loop` · `crane_move` · `crane_grab` · `weld_loop` · `furnace_loop` · `servo_arm` | 생산 라인 · 집게 크레인 · 용접 갠트리 · 작은 해 용광로 · 실습 팔 | 그 자리 가까이에서만 |

## 5. 체험 · 미니게임 (a) — 지금 UI 소리를 빌려 씀

### 5-1. 공용 꾸러미 (미니게임 15종 · 탑승 체험 공통)

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| A | `game_start` | 시작 (3 · 2 · 1 · 시작) | 카운트 + 출발 |
| A | `game_tick` | 남은 시간 10초 | 째깍 |
| A | `game_score` | 하나 맞힘 · 점수 | 밝은 "띵" (연달아 높아지게) |
| A | `game_miss` | 틀림 · 놓침 (지금 거절음) | 부드러운 "뿌" (거절음보다 가볍게) |
| A | `game_clear` · `game_fail` · `game_record` | 결과 · 새 기록 | 짧은 팡파르 · 아쉬움 · 더 큰 팡파르 |
| A | `ride_board` · `ride_leave` | 체험 탑승 · 내림 (지금 창 열기 · 닫기) | 안전바 "철컥" · 내림 |

### 5-2. 체험 · 미니게임별 대표 소리

| 우선 | 모듈 | 체험 · 미니게임 | 소리 |
|---|---|---|---|
| B | 대형 토러스 (2단계) | 은하철도 링 기차 탑승 | `train_whistle` · `train_run_loop` (덜컹) · `train_brake` |
| B | 식량 공장 (2단계) | 유리 바퀴 관람차 | `gondola_door` · `wheel_creak_loop` |
| B | 저장 단지 (2단계) | 별빛 회전목마 | `carousel_bell` · `carousel_music_loop` (오르골) |
| B | 화물 환적장 (2단계) | 은하수 후룸 | `flume_chain_loop` (오르막) · `flume_rush_loop` · `flume_big_splash` |
| B | 연구원 (2단계) | 별빛 데이터 미로 | `maze_hum_loop` · `maze_exit` |
| B | 드론 정비창 (2단계) | 드론 라이트 쇼 관람 | `fx_droneshow_loop` 같이 + 쇼 음악 (7번) |
| B | 요새 포대 (2단계) | 폭죽놀이 직관 (지금 운석 폭발) | `fx_firework_*` 같이 (가까이 크게) |
| B | 그리드 허브 (2단계) | 번개 정원 | `fx_lightning_*` 같이 |
| B | 치안 본부 (2단계) | 떠 있는 순찰차 | `fx_patrol_*` 같이 |
| B | 시민 회관 (2단계) | 스텔라돔 상영 | `dome_dim` (불 꺼짐) + 상영 음악 (7번) |
| B | 정련 콤비나트 (2단계) | 작은 해 용광로 · 집게 크레인 | `furnace_loop` · `ladle_pour` · `crane_*` |
| B | 궤도 도시 (3단계) | 금빛 궤도열차 · 정확히 멈추기 | `orbittrain_loop` · `brake_squeal` · `stop_perfect` |
| B | 바이오스피어 (3단계) | 물꼬 맞추기 · 반딧불이 잡기 | `sluice_wheel` (나무 바퀴) · `water_flow_start` · `firefly_catch` (병 "퐁") |
| B | 궤도 방어망 (3단계) | 오로라 보호막 · 등대 빛 돌리기 · 별똥별 막기 | `lever_pull` · `shield_unfurl` · `star_hit_shield` / `lighthouse_crank_loop` · `beam_react` / `gunseat_sit` · `cannon_fire` · `star_burst` |
| B | 수리 사령부 (3단계) | 구조 바구니 · 경종 등불 쇼 · 드론 순서 기억 | `rope_pull` · `basket_creak` / `bell_toll` (큰 금종) · `lantern_shimmer` / `lantern_note_1~5` (5음) |
| B | 궤도 항만 (3단계) | 가리비 배 뱃놀이 · 진주 조개 잡기 · 인어 분수 진주 던지기 | `paddle_loop` / `clam_open` · `clam_snap` · `pearl_get` / `throw` · `plop` |
| B | AI 중추 (3단계) | 수정 뇌 깨우기 · 정원 드론 날기 · 뇌 미로 빛 구슬 · 시냅스 따라 켜기 | `book_open` · `crystal_swell` / `droneride_loop` (바람) / `orb_collect` / `synapse_note_1~7` |
| B | 항성 발전망 (3단계) | 정자 꽃 그네 · 벌 몰래 꿀 먹기 | `swing_whoosh` · `flower_bell` / `bee_guard_buzz` · `honey_slurp` · `caught` |
| B | 심우주 통신기지 (4단계) | 전파 접시 · 별똥별 아치 소원 · 신호 맞추기 · 별똥별 잡기 | `dish_motor` · `signal_lock` / `wish_sparkle` / `radio_static_loop` · `tune_lock` / `star_catch` |
| C | 1단계 (중력 거주구 · 보안청 · 조선소 · 정비 공창) | 망원경 · 천장 포탑 · 용접 | `telescope_zoom` (4-3) · `turret_servo` · `weld_loop` (4-4) |

## 6. 서사판 (캠페인, `narrative`) — 전부 무음

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| A | `type_tick` | 장 직전 컷신 · 장면 자막 타자 | 단말기 글자 "틱" (아주 작게) |
| A | `chapter_start` | 장 시작 (화면이 켜짐) | 장 제목 스팅 |
| A | `goal_check` | 장 목표 체크 | 체크 차임 |
| A | `climax_warn` · `climax_start` | 절정 예고 · 시작 | 긴장 상승 · 큰 스팅 |
| A | `result_clean` · `result_hurt` · `result_lost` | 절정 결과 3등급 | 밝음 · 씁쓸 · 무거움 |
| A | `chapter_transition` | 장 전환 연출 (레터박스) | 넓은 전환음 |
| A | `ark_approach` · `ark_dock` | 이민선 · 처음 만나는 종족 도킹 연출 | 큰 배 접근 · 도킹 (2번 이민선과 같이) |
| B | `species_sting_{종족}` | 새 종족 등장 | 종족마다 짧은 테마 |
| B | `core_shift` | 코어 성격 축 이동 · 코어 기록 창 | 데이터 울림 |
| B | `old_record` | 기록 복원 옛 녹음 | 라디오 잡음 (재생 앞뒤) |
| B | `earth_fade` | 꺼져 가는 지구 불빛이 줄 때 | 아주 낮은 슬픈 음 |
| B | `probe_launch` · `probe_return` · `discovery` | 탐사 | 발사 · 귀환 · 발견 |
| B | `tension_rise` | 긴장도 오름 | 낮은 맥박 |
| B | `debris_band_loop` · `debris_hit` | 4장 위기 파편 띠 | 바위 · 선체 조각이 도는 소리 · 부딪힘 |
| B | `stellar_storm_loop` | 5장 위기 항성 폭풍 (태양 폭풍보다 셈) | 방사선 지직 + 웅웅 + 가이거 |
| C | `unrest_loop` | 6장 위기 물음의 시간 | 웅성거림 · 긴장 |
| C | `fleet_horn` | 7장 대함대 | 함대 도착 경적 (→ 8-5 습격과 같이) |

## 7. 음악 (`varco_music_generate`, 약 2분 mp3)

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| A | `music_menu` | 메인 메뉴 (교체) | 넓은 우주 · 희망 |
| A | `music_day_01` · `_02` | 낮 (in_game · space 교체) | 잔잔한 건설 · 경영 |
| A | `music_night` | 밤 (교체) | 차갑고 조용함 |
| A | `music_crisis` | 위기 (교체) | 긴장 저음 맥박 |
| B | `music_large` | 중형 · 대형 등급 레이어 (1차 미제작) | 정거장이 커지며 더해지는 은은한 음악 |
| B | `music_interior_1~3` | 1인칭 내부 단계별 (**넣기로 함, 사용자 2026-11-20** — 지금 내부는 환경음만) | 1단계 미래 기술 · 2단계 테마파크 · 3단계 이상 동화. 방 환경음 위에 작게, 쇼 · 탑승 · 미니게임 중엔 그 음악으로 바뀜 |
| B | `music_minigame` | 미니게임 공용 | 경쾌 |
| B | `music_stelladome` · `music_droneshow` | 스텔라돔 상영 · 드론 쇼 | 별빛 상영 · 반짝이는 쇼 |
| B | `music_chapter_calm` · `_tense` · `_climax` | 서사판 장 (준비 · 긴장 · 절정) | — |
| C | `music_ending_1~3` | 엔딩 3갈래 (번영한 새 지구 · 상처 입은 새 지구 · 홀로 남은 AI) | — |
| C | `music_battle` · `music_flagship` | 습격 전투 · 기함 (REVIEW 6번) | — |

## 8. 앞으로 만들 기능 (REVIEW_1115 · TASKS) — 기능 만들 때 같이 (C)

| 우선 | 파일 | 쓰는 곳 | 느낌 |
|---|---|---|---|
| C | `room_enter` | REVIEW 1-9 방 입장 표시 (가운데 2~3초) | 아주 작은 차임 |
| C | `slider_tick` | REVIEW 1-8 조준점 · 이동 속도 · 감도 설정 | 슬라이더 틱 |
| C | `core_zoom_intro` | REVIEW 1-7 시작할 때 코어 확대 | 코어 웅 + 카메라 쓸림 |
| C | `incident_ping` | REVIEW 3 위치 표시 테두리 · 가장자리 화살표 | 방향감 있는 핑 (좌우 위치) |
| C | `noise_complaint_loop` | REVIEW 3 새 상황: 채굴 도킹 소음 민원 | 드릴 · 쿵쿵 (벽 너머 먹먹하게) |
| C | `neighbor_help` | REVIEW 3 이웃 직원이 와서 반쯤 막아 둠 | 달려오는 발 + 주민 한마디 |
| C | `xray_open` · `xray_close` · `xray_floor` | REVIEW 4 X-레이 지도 열기 · 닫기 · 층 바꾸기 | 스캔 쓸림 · 층 틱 |
| C | `exile_send` | REVIEW 5 추방 · 내보내는 배 출발 | 에어록 + 작은 배 출발 (무겁게) |
| C | `pardon` · `memorial` | REVIEW 5 사면 정책 · 추모 공간 | 따뜻한 차임 · 조용한 종 |
| C | `squad_order` · `squad_ack` | REVIEW 6 편대 목표 · 대형 명령 | 무전 "삑" + 응답음 |
| C | `gunseat_fire_1~3` | REVIEW 6 포수 의자 포대 (요격 · 보호막 · 반격 갈래) | 갈래마다 다른 사격음 |
| C | `enemy_scout` · `enemy_fleet` · `enemy_flagship` | REVIEW 6 습격 단계 정찰 → 함대 → 기함 | 엔진음 점점 크게 · 기함 경적 |
| C | `flagship_down` | REVIEW 6 기함 격파 | 큰 폭발 + 승리 스팅 |
| C | `level_up` · `class_pick` | REVIEW 6 편대 · 포대 레벨업 · 갈래 고르기 | 레벨업 차임 · 굳히는 큰 스팅 |
| C | `trump_call` · `trump_fire` · `trump_guard` | REVIEW 6 비장의 카드 (방주 방패 · 궤도 포격) | 충전 → 발동 · 지킬 때 경보 |
| C | `achievement` | TASKS 12-13 도전 과제 | 짧은 성취 |

## 9. 음량 균형 (c)

- 모든 소리의 체감 음량(LUFS)을 잼: `build_audio.py`에 측정을 더하고 묶음(UI · 효과 · 환경 · 음악)마다 목표를 정함.
- 지금 작은 것: `ui_error` −12 dB · `alarm_loop` −10.6 dB · `interior_alarm_loop` −18 dB · 방 환경음 −12 ~ −17 dB → 교체하며 맞춤.
- 묶음별 기본 볼륨 다시 정하기 (마스터 · 음악 · 효과 · 환경 · UI).
- 큰 스팅(절정 · 장 전환 · 기함) 동안 음악 · 환경음 낮추기 (덕킹). 내부 쇼 · 탑승 중엔 방 환경음을 낮춤.
- 바깥 ↔ 내부 전환 때 교차 페이드. 바깥 연출 · 기계 루프는 동시 재생 수 상한.

## 연결할 때 필요한 코드 (참고)

- `SoundLibrary`에 새 칸 (묶음별 Header), 메뉴 `SpaceStation/Audio/Setup`이 새 폴더 이름도 읽게.
- 체험 · 미니게임 40곳의 `PlayNear(l => l.UiOpen)` 같은 빌린 소리를 전용 칸으로 바꿈.
- 방 환경음 종류(`InteriorAmbience`)에 정원 · 물가 · 밤 숲 · 놀이 · 수정 추가, 발소리는 밟은 바닥 재질로 고름.
- 바깥 연출 소리 = 모듈 선택 · 카메라 거리로 켜고 끄는 공용 부품 하나.
- 내부 음악 = 지금 방의 모듈 단계로 고름 (기본 모듈 방 = 음악 없이 환경음만). 들어갈 때 바깥 음악에서 교차 페이드, 방을 옮겨 단계가 바뀌면 다시 교차 페이드.

## 순서 제안

1. **A 먼저** — 합성 임시음 교체 · UI 소리 한 벌 · 문 · 엘리베이터 · 이동 장치 · 합성 · 바닥별 발소리 · 미니게임 공용 꾸러미 · 서사판 장 기본 · 음악 5곡.
2. **B** — 체험 · 미니게임 대표 소리 · 바깥 진화 연출 · 방 환경음 추가 · 주민 목소리 · 내부 단계별 음악 3곡.
3. **C** — 기능 만들 때 같이.

매 묶음 = 만들기 (후보 여러 개) → 사용자가 골라 듣기 → 다듬기 → 연결 → 플레이 확인.

