using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>소리 묶음. 볼륨 설정(5-10)이 이 단위로 곱해진다.</summary>
    public enum AudioBus
    {
        Ui,
        Sfx,
        Ambient,
        Music,
    }

    /// <summary>
    /// 소리 하나의 재생 규칙. 변형 클립 중 직전과 다른 것을 무작위로 고르고,
    /// 음높이를 살짝 흔들어 반복감을 줄인다. 쿨다운·최대 동시 재생 수로 겹침을 막는다.
    /// </summary>
    [Serializable]
    public sealed class SoundCue
    {
        public AudioClip[] Clips = Array.Empty<AudioClip>();
        public AudioBus Bus = AudioBus.Sfx;
        [Range(0f, 1.5f)] public float Volume = 1f;
        [Tooltip("무작위 음높이 범위 (1 = 원래 높이)")]
        public Vector2 Pitch = new Vector2(0.97f, 1.03f);
        [Tooltip("같은 소리를 다시 낼 수 있는 최소 간격 (실시간 초)")]
        [Min(0f)] public float Cooldown = 0.05f;
        [Tooltip("동시에 울릴 수 있는 수 (0 = 제한 없음). 넘치면 가장 오래된 것을 끊는다")]
        [Min(0)] public int MaxVoices = 3;

        [NonSerialized] internal int LastIndex = -1;
        [NonSerialized] internal float LastPlayed = -999f;

        public bool HasClip => Clips != null && Clips.Length > 0;

        internal AudioClip Pick()
        {
            if (!HasClip)
                return null;
            int i = 0;
            if (Clips.Length > 1)
            {
                i = UnityEngine.Random.Range(0, Clips.Length - 1);
                if (i >= LastIndex && LastIndex >= 0)
                    i++; // 직전 변형은 건너뜀
            }
            LastIndex = i;
            return Clips[i];
        }
    }

    /// <summary>
    /// 5-8 게임 전체 소리 목록 (에셋 하나). 클립 연결은 메뉴 SpaceStation/Audio/Setup이 파일 이름으로 채운다.
    /// 볼륨·음높이·쿨다운은 인스펙터에서 조정.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Sound Library", fileName = "SoundLibrary")]
    public sealed class SoundLibrary : ScriptableObject
    {
        [Header("UI")]
        [Tooltip("확인 (버튼 클릭) — 2026-11-25 홀로그램 UI 한 벌: 유리 결 파5 → 도6, 변형 4 (같은 음 · 배음 위상 · 간격만 다름). UI 소리는 모두 F 장조 음이라 음높이 흔들기를 끔 (Pitch 1 · 1)")]
        public SoundCue UiClick = new SoundCue { Bus = AudioBus.Ui, Volume = 0.25f, Pitch = Vector2.one };
        [Tooltip("마우스 올림 — 유리 도7 아주 짧게 (0.05초), 변형 4, 목록을 훑을 때 몰리므로 쉬는 시간 0.04초 · 동시 2 (D-2)")]
        public SoundCue UiHover = new SoundCue { Bus = AudioBus.Ui, Volume = 0.11f, Cooldown = 0.04f, MaxVoices = 2, Pitch = Vector2.one };
        public SoundCue UiTab = new SoundCue { Bus = AudioBus.Ui, Volume = 0.29f, Pitch = Vector2.one };
        public SoundCue UiOpen = new SoundCue { Bus = AudioBus.Ui, Volume = 0.26f, Pitch = Vector2.one };
        public SoundCue UiClose = new SoundCue { Bus = AudioBus.Ui, Volume = 0.25f, Pitch = Vector2.one };
        public SoundCue UiError = new SoundCue { Bus = AudioBus.Ui, Volume = 0.63f, Cooldown = 0.15f, MaxVoices = 1, Pitch = Vector2.one };
        public SoundCue UiSpeedUp = new SoundCue { Bus = AudioBus.Ui, Volume = 0.24f, Pitch = Vector2.one };
        public SoundCue UiPause = new SoundCue { Bus = AudioBus.Ui, Volume = 0.31f, Pitch = Vector2.one };
        public SoundCue UiResume = new SoundCue { Bus = AudioBus.Ui, Volume = 0.27f, Pitch = Vector2.one };
        [Tooltip("취소 · 닫기 · 뒤로 버튼 (UiSound가 버튼 이름 · 글로 저절로 고름) — 확인의 반대 도6 → 파5, 같은 클릭으로 창이 닫히면 창 닫힘 소리 대신 이것 하나")]
        public SoundCue UiCancel = new SoundCue { Bus = AudioBus.Ui, Volume = 0.26f, Pitch = Vector2.one };
        [Tooltip("켜기 (Toggle이 켜짐 · 설정 창 켬 / 끔 줄) — 레6 → 라6")]
        public SoundCue UiToggleOn = new SoundCue { Bus = AudioBus.Ui, Volume = 0.23f, Pitch = Vector2.one };
        [Tooltip("끄기 — 라6 → 레6 (4.5 kHz 로우패스)")]
        public SoundCue UiToggleOff = new SoundCue { Bus = AudioBus.Ui, Volume = 0.25f, Pitch = Vector2.one };
        [Tooltip("슬라이더 — 값이 8칸 중 한 칸 바뀔 때 나무 톡 (TryPlayNote로 오음계 오름, 파일 높이 = 도5), 쉬는 시간 0.05초")]
        public SoundCue UiSlider = new SoundCue { Bus = AudioBus.Ui, Volume = 0.22f, Cooldown = 0.05f, MaxVoices = 2, Pitch = Vector2.one };

        [Header("건설·수리")]
        public SoundCue BuildSelect = new SoundCue { Bus = AudioBus.Ui, Volume = 0.45f };
        public SoundCue BuildRotate = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = new Vector2(0.95f, 1.08f) };
        [Tooltip("모듈 배치 1칸 (2026-11-25 사운드 개편 1-1, D-6 크기 3갈래 = BuildSound.SizeOf): 실제 녹음: 문 자물쇠 걸림 \"철컥\" + 트럭 문 닫힘 \"쿵\" (3반음 위) + 공압 못총 \"칙\". " +
                 "도킹 \"철컹\" + 압력 \"칙\"을 크기마다 무게로 (1칸 가볍게). 0.15초 안 여러 번 = 한 번 (D-2, BuildSound.Admit). " +
                 "음악 + 바탕 대비 귀 감도 +5 (예전 합성음 약 +10: UI +3 · 위험 경보 +8 사이로)")]
        public SoundCue BuildPlace = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f };
        [Tooltip("모듈 배치 여러 칸: 트럭 문 닫힘 \"쿵\" 그대로 + 건조기 문 \"툭\" 몸통 + 걸림 \"철컥\" + \"칙\" (1칸보다 묵직하게), +6")]
        public SoundCue BuildPlaceM = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.59f };
        [Tooltip("진화 모듈 배치 (합성 결과 모듈: 재건축 · 내부 패드 건설): 트럭 문 3반음 아래 + 건조기 문 \"툭\" + 낮춘 \"철컥\" + 김 \"쉬익\" 옅게 (더 크고 둥글게, 1.6초), +7")]
        public SoundCue BuildPlaceEvo = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.68f };
        [Tooltip("모듈 철거 1칸: 실제 녹음: 문 자물쇠 풀림 \"철컥\" + 짧은 감압 \"쉬익\" 0.3초 (클수록 길게), +4.5 (예전 약 +12)")]
        public SoundCue BuildRemove = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.35f };
        [Tooltip("모듈 철거 여러 칸: 풀림 \"철컥\" + 3반음 낮은 \"철컥\" 한 번 더 + 감압 0.6초, +5.5")]
        public SoundCue BuildRemoveM = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.36f };
        [Tooltip("진화 모듈 철거: 4반음 낮춘 \"철컥\" + 건조기 문 \"툭\" 옅게 + 감압 1.1초, +6.5")]
        public SoundCue BuildRemoveEvo = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.48f };
        [Tooltip("통로가 이어짐 (배치 0.28초 뒤, 크기와 상관없이 하나): 실제 녹음: 공압 자동문 \"치익-툭\" 한 번 그대로 (에어록이 잠김), +2.5 (예전 약 -2: 거의 안 들렸음)")]
        public SoundCue BuildConnect = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.3f };
        public SoundCue RepairStart = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Cooldown = 0.2f };
        public SoundCue RepairDone = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Cooldown = 0.2f };
        [Tooltip("정비 (2026-11-26 사운드 개편 1-1, 1차 합성 임시음을 바꿈): 실제 녹음 래칫 렌치로 볼트 조이기 \"드륵 드륵 드륵\" (0.4초 간격 세 번, 1초). " +
                 "바깥 정비 · 일괄 정비 한 번 · 1인칭 현장 수리 지점 마무리. 자동 정비는 무음 (D-2 · M-49, GameAudio). 음악 + 바탕 대비 귀 감도 약 +4.5 (0.37에서 +3.0을 잼)")]
        public SoundCue Maintain = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.44f };
        [Header("모듈 옮기기 (사운드 개편 2026-11-26, SOUND_LIST_NEW 2번 · 12-3b, 바깥 2D · 화면 좌우)")]
        [Tooltip("옮기기 시작 = 집어 올림 (BuildController.BeginMove): 걸쇠 풀림 \"딸깍\" 옅게 + 반중력 바람결이 솟음 (실제 녹음 큰 휙, 0.85초). " +
                 "크기 3갈래는 재생 높이 · 크기로 (BuildSound.MoveTone: 1칸 2반음 위 · 진화 3반음 아래). 약 +2.6 · +3.5 · +4.8 (0.3에서 +1.5 · +2.4 · +3.7을 잼)")]
        public SoundCue ModuleMovePick = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.34f, Cooldown = 0.15f, MaxVoices = 1 };
        [Tooltip("옮기기 확정 = 내려놓음 (BuildController.Moved): 내려오는 바람결 + 0.38초 \"툭\" (건조기 문) + 잠금 \"철컥\" 옅게 (1초). 배치보다 가볍게 (쿵 · 칙 없음). 크기 3갈래는 MoveTone. 약 +3.5 · +4.5 · +5.6 (0.65에서 +2.0 · +3.0 · +4.1을 잼)")]
        public SoundCue ModuleMoveDrop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.77f, Cooldown = 0.15f, MaxVoices = 1 };

        [Header("이벤트·경보")]
        public SoundCue EventPositive = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("운석 접근 (2026-11-25 사운드 개편 1-1, 2D, 도착 순간에 가장 센 곳 = GameAudio _incomingPeak 1.28초): 실제 녹음 큰 바람결 \"휙\" (거꾸로 · 앞으로 두 변형) + 가스 버너 불 웅 (다가올수록 높고 밝게), " +
                 "최대 뒤 0.35초 잦아듦. 1인칭 내부에서는 안 울림. 음악 + 바탕 대비 귀 감도 +1.5 (게임 안 녹음, 예전 합성 +3.4). AI Whoosh 0.59 ~ 0.67")]
        public SoundCue MeteorIncoming = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.33f, Cooldown = 0.25f, MaxVoices = 2, Pitch = new Vector2(0.94f, 1.06f) };
        [Tooltip("옛 운석 충돌 한 칸 (1차 합성 meteor_impact). 2026-11-25부터 GameAudio는 아래 갈래 칸 (맞은 곳 3 x 크기 2)을 씀: 다른 곳 연결이 안 깨지게 두고, 갈래 칸에 클립이 없을 때만 대신 울림")]
        public SoundCue MeteorImpact = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Cooldown = 0.08f, MaxVoices = 3, Pitch = new Vector2(0.9f, 1.08f) };

        [Header("운석 충돌 갈래 (사운드 개편 2026-11-25, SOUND_LIST_NEW 1-1 · D-6 맞은 곳 3 x 크기 2 · D-2)")]
        [Tooltip("선체 큰 충돌 (보통 모듈이 파손됨, 큰 = 1초에 한 번까지: MeteorSound): 실제 녹음 속 빈 금속 \"퉁\" (에어컨 실외기) + 불 \"훅\" + 캔 찌그러짐 셋을 낮춰 \"쿵 찌그럭\" (금속 찢김), 변형 2. " +
                 "+6.4 (게임 안, 예전 한 칸 +5.9). 찢김이 들어 있어 균열 (ModuleDamaged)은 덧대지 않음 (위험 경보만)")]
        public SoundCue MeteorImpactHullL = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Cooldown = 0.3f, MaxVoices = 2, Pitch = new Vector2(0.95f, 1.05f) };
        [Tooltip("선체 작은 충돌 (큰 충돌 뒤 1초 안: 유성우 · 한 사건의 여러 발): 퉁 + 찌그럭 한 덩이 0.5초, 변형 2. +3.5 (음량 0.24에서 +2.2를 잼. 짧은 부딪힘은 파일 눈금보다 게임 안에서 4 ~ 5 dB 크게 잡혀 음량은 녹음으로 맞춤)")]
        public SoundCue MeteorImpactHullS = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.28f, Cooldown = 0.06f, MaxVoices = 2, Pitch = new Vector2(0.93f, 1.07f) };
        [Tooltip("장갑 큰 충돌 (장갑 격벽 계열, 또는 방어 연구로 파손을 면한 충돌): 실제 녹음 둔탁한 금속 \"쿵\" 4 ~ 5반음 아래 + 불 \"훅\" + 상자 \"툭\", 1.2 ~ 1.5 kHz 아래 (먹먹하게, 찌그럭 없음), 변형 2. +5.1. " +
                 "장갑이 파손되면 균열 (ModuleDamaged)을 0.12초 뒤에 덧댐")]
        public SoundCue MeteorImpactArmorL = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.47f, Cooldown = 0.3f, MaxVoices = 2, Pitch = new Vector2(0.95f, 1.05f) };
        [Tooltip("장갑 작은 충돌: 상자 \"툭\" + 쿵 옅게, 1.8 kHz 아래 0.45초, 변형 2. +2.6")]
        public SoundCue MeteorImpactArmorS = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.28f, Cooldown = 0.06f, MaxVoices = 2, Pitch = new Vector2(0.93f, 1.07f) };
        [Tooltip("실드 큰 충돌 (운석이 실드 구면에 닿음: 운석 쪽 몸통, 실드 쪽 \"우웅-팅\" ShieldDeflect와 함께): 실제 녹음 둥근 \"퍽\" + 낮은 \"붕\" (150에서 70 Hz로 내려앉는 사인) + 테슬라 코일 \"지직\", 변형 2. " +
                 "+3.8 (튕김과 합쳐 약 +5.5). 튕긴 운석이 다른 실드에 또 막혀 부딪힌 모듈이 없을 때도 이 칸")]
        public SoundCue MeteorImpactShieldL = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.34f, Cooldown = 0.3f, MaxVoices = 2, Pitch = new Vector2(0.95f, 1.05f) };
        [Tooltip("실드 작은 충돌: 퍽 + 높은 붕 (240에서 130 Hz) + 지직 0.45초, 변형 2. +2.6")]
        public SoundCue MeteorImpactShieldS = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.26f, Cooldown = 0.06f, MaxVoices = 2, Pitch = new Vector2(0.93f, 1.07f) };

        [Tooltip("격추된 운석 공중 폭발 (2026-11-25): 실제 녹음 상용 불꽃 터짐 셋을 2.2 ~ 2.6 kHz 아래로 = 가벼운 \"퍽\" + 0.6초 잦아듦 (탁탁 · 쉬 없이). +3.6 (예전 합성 +4.5, AI Bang). AI Fireworks · Firecracker")]
        public SoundCue MeteorExplode = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Cooldown = 0.06f, MaxVoices = 3, Pitch = new Vector2(0.92f, 1.08f) };
        [Tooltip("포탑 레이저 (2026-11-25): 레이저 흉내 녹음 (자석 장난감 \"징\") 5반음 아래 + 유리 결 라5에서 레5로 내려앉는 \"퓨\" 옅게, 0.32초, 변형 3. " +
                 "음 있는 밑층이 있어 음높이 흔들기 끔 (조 안). 1인칭 내부에서는 안 울림. +2.1 (예전 합성 +4.2)")]
        public SoundCue TurretLaser = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.23f, Cooldown = 0.05f, MaxVoices = 3, Pitch = Vector2.one };
        [Tooltip("실드가 튕겨냄 = 실드 쪽 소리 (2026-11-25, 합성: 실제 녹음 없음): 에너지 장막 \"우웅\" (레3 · 라3 · 레4 일렁임 + 바람결) 뒤 0.09초 \"팅\" (미6 · 레6 · 솔6 변형 3, 유리 결). " +
                 "같은 순간 운석 쪽 몸통은 MeteorImpactShieldL/S. 음 = 바깥 · 위기 · 내부 음악 모두의 조 안 (음높이 흔들기 끔), 안내 경보 (라5)와 다른 음. " +
                 "+3.0 (예전 합성 Ding · Clang +9.4). 실드 단계별 3갈래는 B 묶음")]
        public SoundCue ShieldDeflect = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.39f, Cooldown = 0.12f, MaxVoices = 2, Pitch = Vector2.one };

        [Header("습격 포격 · 방어 단계 · 바깥 진화 연출 · 오가는 것 (사운드 개편 2026-11-26, SOUND_LIST_NEW 2번 · 3번 · D-12, 작업자 P4)")]
        [Tooltip("습격 포격탄 접근 (2D, 운석 \"휙\" 대신: 유성우 틀이 포격 중이고 이름이 \"포격\"일 때, DefenseSound): 실제 녹음 만화 폭탄의 떨어지는 휘파람 끝 1.6초, 끝 음 라6 (조 안), " +
                 "도착 순간에 끝남 (RaidPeak 1.55초). 변형 2, 음높이 흔들기 끔. 1인칭 내부에서는 안 울림")]
        public SoundCue RaidIncoming = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.32f, Cooldown = 0.25f, MaxVoices = 2, Pitch = Vector2.one };
        [Tooltip("습격 포격탄 터짐 (모듈에 맞음 = 그대로 · 몰리면 작게, 포탑이 공중에서 맞힘 = 0.6배): 실제 녹음 박격포 \"쿠르릉\" + 불 \"훅\", 1.3초, 변형 3. 운석 금속 \"퉁\"과 다른 결")]
        public SoundCue RaidImpact = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Cooldown = 0.08f, MaxVoices = 3, Pitch = new Vector2(0.96f, 1.04f) };
        [Tooltip("실드 1단계 (실드 매트릭스, 육각 무늬): 기본 \"우웅-팅\" + 팅 뒤 작은 유리 \"치리리\" 여섯 (라6 · 레7 · 미7), 0.8초, 변형 3. 단계마다 조금씩 세게 (기본 +3.0, 이것 +3.5)")]
        public SoundCue ShieldDeflectHex = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.41f, Cooldown = 0.12f, MaxVoices = 2, Pitch = Vector2.one };
        [Tooltip("실드 2단계 (실드 돔, 오로라 파문): 기본 \"우웅-팅\" + 오로라 막이 일렁이는 \"쉬르르\" + 옅은 레5 · 라5 · 미6 울림, 1.1초, 변형 3 (약 +4)")]
        public SoundCue ShieldDeflectAurora = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.44f, Cooldown = 0.12f, MaxVoices = 2, Pitch = Vector2.one };
        [Tooltip("실드 3단계 (궤도 방어망, 수정 결정 막): 낮은 레2를 더한 우웅 + 수정 \"칭칭\" (라6 · 레7 · 미7) + 오로라 일렁임, 1.4초, 변형 3 (약 +4.5)")]
        public SoundCue ShieldDeflectCrystal = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.46f, Cooldown = 0.12f, MaxVoices = 2, Pitch = Vector2.one };
        [Tooltip("포탑 1 ~ 2단계 레이저 (보안청 · 방어 포대 · 요새 포대 · 치안 본부): 기본과 같은 자석 \"징\"을 7반음 낮춰 굵게 + 유리 \"퓨\" 키움 + 낮은 \"둥\", 0.42초, 변형 3, 음높이 흔들기 끔")]
        public SoundCue TurretLaser1 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.25f, Cooldown = 0.05f, MaxVoices = 3, Pitch = Vector2.one };
        [Tooltip("포탑 3단계 프리즘 광선 (궤도 방어망 수정 부표): 1단계 레이저 + 수정 \"치리링\" (레6 · 라6 · 레7 오름), 0.65초, 변형 3, 음높이 흔들기 끔")]
        public SoundCue PrismBeam = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.33f, Cooldown = 0.05f, MaxVoices = 3, Pitch = Vector2.one };
        [Tooltip("바깥 오로라 (실드 돔 · 궤도 방어망 AuroraFx, ExteriorFxAudio: 가까이 · 화면 가운데만 · 먹먹하게, 밤 1 · 낮 0.7): 궤도 방어망 밤 오로라 울림과 같은 합성을 바깥 음 레4 · 라4 · 레5 · 미5로, 16초 = 24박")]
        public SoundCue FxAuroraLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.95f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("바깥 빛기둥 (궤도 미러 LightBeamFx): 낮은 빛 울림 (레3 · 라3 · 레4 + 데워진 빛 결 잡음), 12초 = 18박")]
        public SoundCue FxLightBeamLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 1.05f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("바깥 번개 (그리드 허브 LightningFx 탑에서 치는 번개, 몇 초에 한 번): 1인칭 번개와 같은 찌직 원본 + 옅은 퐁 (레5 · 라5 · 미6) + 먼 천둥 녹음 한 토막, 변형 3")]
        public SoundCue FxLightning = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.28f, Pitch = Vector2.one, Cooldown = 3.5f, MaxVoices = 2 };
        [Tooltip("바깥 요새 포대 박격포대 불꽃 쏨 (FireworkFx.Launched): 1인칭 폭죽놀이와 같은 파일 (Ride/firework_launch_)")]
        public SoundCue FxFireworkLaunch = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.94f, 1.06f), Cooldown = 0.3f, MaxVoices = 2 };
        [Tooltip("바깥 불꽃 터짐 (쏨 0.8초 뒤 · 요새 포대 · 궤도 방어망이 운석을 맞혀 별자리 불꽃): 1인칭 폭죽놀이와 같은 실제 녹음 (Ride/firework_burst_)")]
        public SoundCue FxFireworkBurst = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.32f, Pitch = new Vector2(0.9f, 1.1f), Cooldown = 0.3f, MaxVoices = 2 };
        [Tooltip("바깥 드론 쇼 (드론 정비창 DroneShowFx, 큰 쇼 1 · 평소 0.75): 1인칭 쇼와 같은 드론 떼 (Ride/droneshow_swarm_loop)")]
        public SoundCue FxDroneShowLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.71f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("바깥 은하수 후룸 물길 (화물 환적장 FlumeRideFx): 1인칭 후룸 물살과 같은 파일 (scape_bio_sluice_loop)")]
        public SoundCue FxFlumeLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.47f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("바깥 후룸 큰 낙하 물보라 (FlumeRideFx.Splashed): 1인칭과 같은 파일 (Ride/flume_big_splash)")]
        public SoundCue FxFlumeSplash = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.28f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 1f, MaxVoices = 2 };
        [Tooltip("바깥 가리비 배 (궤도 항만 BoatShuttleFx): 소리 풍경 배 물살과 같은 파일 (scape_port_wake_loop)")]
        public SoundCue FxBoatLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 1.2f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("바깥 순찰차 부양 (치안 본부 PatrolCarFx, 날면 1 · 서 있으면 0.6): 1인칭 순찰차와 같은 파일 (Ride/patrol_hover_loop)")]
        public SoundCue FxPatrolHoverLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.32f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("바깥 순찰차 출동 · 돌아옴 경광음: 1인칭과 같은 \"삐용\" 레6에서 라5 (Ride/patrol_siren)")]
        public SoundCue FxSiren = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.15f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 2 };
        [Tooltip("바깥 바이오스피어 구름 비 (RainFx): 소리 풍경 구름 비와 같은 파일 (scape_bio_rain_loop)")]
        public SoundCue FxRainLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.9f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("바깥 오가는 배 엔진 (화물선 · 채굴선 · 보급선 · 지나가는 배, 같은 배 모형 AmbientSpace): 실제 녹음 먼 제트기의 고른 구간 고리, 배를 따라 크기 · 좌우 · 높이 (빠를수록 크게, 작은 배 높게)")]
        public SoundCue ExtShipEngine = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.34f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("실드에 튕겨 우주로 멀어지는 운석 \"휘우우\" (MeteorFx.PassedAway, 0.2초 늦게): 운석 접근과 같은 녹음을 거꾸로 된 모양으로 (낮고 어두워지며 잦아듦), 변형 2")]
        public SoundCue ExtMeteorPass = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.25f, Pitch = new Vector2(0.95f, 1.05f), Cooldown = 0.3f, MaxVoices = 2 };
        [Tooltip("모듈 파손 금속 균열 (2026-11-25, 알림은 위험 경보 alert_danger): 실제 녹음 얼음 갈라짐 9반음 아래 + 캔 찌그러짐 10반음 아래 = \"쩌억 찌그럭\" 0.7초, 3 kHz 아래. " +
                 "번진 파손 등 운석 밖의 파손, 운석에는 파손된 장갑에만 (선체 충돌에는 찢김이 들어 있음). +4.3 (예전 합성 +4.5)")]
        public SoundCue ModuleDamaged = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.39f, Cooldown = 0.2f, MaxVoices = 2 };
        public SoundCue ModuleDestroyed = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.75f, Cooldown = 0.3f, MaxVoices = 2 };
        [Tooltip("짧은 전기 \"파직\" (2026-11-26 사운드 개편 1-1, 1차 합성 셋을 바꿈): 실제 녹음 전기 아크의 다른 순간 여섯 (0.12 ~ 0.28초, 변형끼리 같은 크기). " +
                 "바깥 태양 폭풍 태양광 판 (내부에 있으면 안 울림) · 내부 파손 방 · 현장 수리 지점 · 사고 차단기 · 드론 끄기가 함께 씀 (D-2 초 단위로 몰림)")]
        public SoundCue StormSpark = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.16f, Cooldown = 0.12f, MaxVoices = 2, Pitch = new Vector2(0.85f, 1.2f) };
        [Tooltip("산소 누출 사건 (나쁜 사건 알림 0.6초 뒤 2D) — 실제 녹음 스팀 김 \"쉬익\" 큰 크기 (2026-11-25, 정전기 · 흰 잡음으로 들리던 것을 바꿈). 작은 크기 oxygen_leak_small은 파일만 — 예정 (이웃에 따라 규모)")]
        public SoundCue OxygenLeak = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.32f, Cooldown = 1f, MaxVoices = 1 };
        public SoundCue SupplyArrive = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Cooldown = 1f, MaxVoices = 2, Pitch = Vector2.one };
        public SoundCue GradeUp = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("대형 등급 첫 도달 (2026-11-26 바깥 알림: 1차 합성 4.4초를 바꿈, 그 순간 등급 상승 소리는 안 울림): 밝은 성취 음악 3.4초, 유리 결 오름 셋 + Dsus2 Asus4 패드 + 둥 + 반짝 (레 미 솔 라만). 게임 안 +8.5 = 위험 경보와 같게 (치명 아래, 예전 1차 합성은 약 +17)")]
        public SoundCue Victory = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.36f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        public SoundCue GameOver = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };

        [Header("경보 위계 4단계 (사운드 개편 2026-11-25, SOUND_LIST_NEW D-3 · AlertSound)")]
        [Tooltip("안내: 밝은 한 음 (라5, UI와 같은 유리 결) · 사건 끝남 · 함선 접근. 음악 대비 귀 감도 +5")]
        public SoundCue AlertInfo = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.33f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("주의: 두 음 (라5 미5) · 조기 경보 · 유성우 예보 · 역병 · 파업 조짐 · 함선 도착 · 등급 하락 · 사고 놓침. +6.5")]
        public SoundCue AlertCaution = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.44f, Cooldown = 1.5f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("위험: 세 음 내림 (라5 솔5 레5) + 낮은 둥 · 나쁜 사건 · 모듈 파손 · 자원 바닥 · 내부 사고 · 습격 · 침입 드론 · 역병 · 파업. +8")]
        public SoundCue AlertDanger = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.54f, Cooldown = 3f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("치명: 세 음 내림을 빠르게 두 번 + 낮은 울림 (레2) · 모듈 파괴 (실패 조건 경보 고리 AlarmLoop도 같은 가족). +9.5")]
        public SoundCue AlertCritical = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Cooldown = 3f, MaxVoices = 1, Pitch = Vector2.one };

        [Header("바깥 알림 (사운드 개편 2026-11-26, SOUND_LIST_NEW 2번 · NoticeSound · AlertSound.TryPlayAs)")]
        [Tooltip("연구 완료 · 레시피 (그림자) 연구 완료 · 연계 연구 완료 (예전 좋은 소식 EventPositive 0.8을 빌림): 데이터 착착착착 (레6 미6 솔6 라6) + 차임 레5 라5, 레6. 위계 밖 좋은 소식, 여러 개가 한꺼번에 끝나도 1초에 한 번. 게임 안 +5.4 (같은 판 주의 경보 +7.1)")]
        public SoundCue ResearchDone = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.29f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("해금 선택 창을 고를 후보가 있을 때 열면 (창 열기 대신): 실제 녹음 카드 세 장 슥 슥 슥 + 내려앉는 탁 + 반짝 미6 라6 옅게. 게임 안 약 +4.6 (창 열기 수준)")]
        public SoundCue UnlockReveal = new SoundCue { Bus = AudioBus.Ui, Volume = 0.31f, Cooldown = 0.3f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("정책을 슬롯에 예약 (결재, 정책 줄 버튼은 클릭음 없이 이것 하나): 실제 녹음 카드 뭉치 툭 (도장) + 둥근 몸통 + 전자음 삐빅 (레6 라6) 옅게. 게임 안 +4.7")]
        public SoundCue PolicyEnact = new SoundCue { Bus = AudioBus.Ui, Volume = 0.6f, Cooldown = 0.15f, MaxVoices = 2, Pitch = Vector2.one };
        [Tooltip("사회 위기 선택지 (파업 조짐): 주의 자리를 대신 맡는 뜻 소리 = 주의 음형 (라5 미5) + 긴장 스팅 (낮은 레 라 빈 5도가 떨며 부풂 + 둥). 파업 발생은 위험 경보 그대로. 게임 안 +6.9 (주의 경보와 같게)")]
        public SoundCue SocialCrisis = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.48f, Cooldown = 1.5f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("역병 발생: 위험 자리를 대신 맡는 뜻 소리 = 위험 음형 (라5 솔5 레5 + 둥) + 낮은 불길음 (레2 맥놀이 + 어두운 숨). 역병 징후는 주의 경보 그대로. 게임 안 +8.6 (위험 경보 +8.5)")]
        public SoundCue PlagueOutbreak = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.56f, Cooldown = 3f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("역병을 막음 (방역 물자 · 격리 · 캠페인) 또는 역병이 끝남: 따뜻한 회복음 (레 라 패드 + 미5, 라5). 위계 밖 좋은 소식, 위험 이상 경보가 막 울렸으면 생략. 게임 안 약 +4.4")]
        public SoundCue PlagueCured = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.28f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("외부 접촉 상인 연합 도착 (해독된 함선만, 정체 불명은 주의 경보 그대로라 소리로 정체가 새지 않음): 주의 자리 = 무전 치직 + 레5, 라5 (오름) + 칙. 게임 안 +7.0")]
        public SoundCue ContactArrive1 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.39f, Cooldown = 1.5f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("외부 접촉 학술 함대 도착: 무전 치직 + 라5, 미6 + 라6 레7 짧게 (데이터). 게임 안 +7.0")]
        public SoundCue ContactArrive2 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.34f, Cooldown = 1.5f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("외부 접촉 무법 선단 도착: 무전 치직 크게 + 라4, 레4 (내림 5도) + 낮은 레3 둥. 게임 안 +7.2")]
        public SoundCue ContactArrive3 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Cooldown = 1.5f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("외부 접촉 결과 (교역 · 협상 · 경계 · 응답 없이 떠남, 대응 버튼은 클릭음 없이 이것 하나): 무전 닫힘 칙 + 솔5, 레6. 게임 안 약 +3.6 (확인음 수준)")]
        public SoundCue ContactResult = new SoundCue { Bus = AudioBus.Ui, Volume = 0.24f, Cooldown = 0.3f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("유성우 방향 예보: 주의 자리 = 주의 음형 + 레이더 핑 (라6) + 다가오는 낮은 바람, 화면 좌우 = 오는 방향. 습격 포격 예보는 위험 경보 그대로. 게임 안 +7.0")]
        public SoundCue MeteorShowerWarn = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.43f, Cooldown = 1.5f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("열 과열 (모듈이 위험 열에 들어 60초 뒤 파손, 8번 overheat 같은 파일): 주의 자리 = 주의 음형 + 실제 녹음 가마솥 거품 + 김 지글. 30초에 한 번까지. 게임 안 +6.8")]
        public SoundCue HeatWarning = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.43f, Cooldown = 1.5f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("낮이 됨 (바깥에서만): 아주 작은 전환음, 느린 패드 레4 라4 미5 + 반짝 옅게. 게임 안 약 0 (음악 + 바탕과 비슷하게)")]
        public SoundCue Dawn = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.21f, Cooldown = 5f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("밤이 됨 (바깥에서만): 아주 작은 전환음, 낮은 패드 라3 레4 미4 (1.8 kHz 아래). 게임 안 약 0")]
        public SoundCue Dusk = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.34f, Cooldown = 5f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("연결이 끊겨 끝쪽 모듈이 꺼짐 (CONNECT M9, 한 프레임에 여러 개 = 한 번, 철거 소리 0.35초 뒤): 실제 녹음 청소기 스위치 딸깍 + 서는 모터 우웅 (5반음 아래). 위험 이상 경보가 막 울렸으면 생략. 게임 안 약 +4.6")]
        public SoundCue ModuleDisconnect = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.33f, Cooldown = 0.5f, MaxVoices = 1, Pitch = Vector2.one };
        [Tooltip("끊겼던 모듈이 다시 이어짐 (배치 소리 0.55초 뒤): 도는 모터 우웅 + 레5, 라5 옅게. 게임 안 약 +3.5")]
        public SoundCue ModuleReconnect = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.27f, Cooldown = 0.5f, MaxVoices = 1, Pitch = Vector2.one };

        [Header("반복 (루프)")]
        [Tooltip("실패 조건 경보 (치명 가족 고리, 2026-11-25): 치명 소리를 2초마다, 남은 10초부터 GameAudio가 5도 위로. 귀 감도 평균 +3 · 최대 +7 (옛 사인 사이렌 +1.2)")]
        public SoundCue AlarmLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.51f, Pitch = Vector2.one };
        public SoundCue StormLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.35f, Pitch = Vector2.one };
        public SoundCue StationHum = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.22f, Pitch = Vector2.one };
        [Tooltip("바깥 정거장 험 3단계 (2026-11-25, SOUND_LIST_NEW 4-4): StationHum 위에 겹치는 층, 정거장 크기 (모듈 수 · 등급)로 이웃 단계끼리 교차 (GameAudio · ExteriorHum). 실제 녹음 팬 · 공기 · 펌프 · 깊은 웅, 음은 E · A만. 음량 = 바탕 혼자보다 귀 감도 +2 · +4 · +6 dB")]
        public SoundCue ExtHum1 = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.07f, Pitch = Vector2.one };
        public SoundCue ExtHum2 = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.12f, Pitch = Vector2.one };
        public SoundCue ExtHum3 = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.24f, Pitch = Vector2.one };

        [Header("내부 방문 (11-9)")]
        public SoundCue Footstep = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.93f, 1.07f), Cooldown = 0.12f, MaxVoices = 2 };
        [Tooltip("바닥별 발소리 (2026-11-25, SOUND_LIST_NEW 4-1): 금속 판 달리기 = 걷기와 같은 VARCO 부츠 원본을 짧고 세게. 바닥은 FootstepSurface가 발밑 재질 이름으로 고름, 칸이 비면 금속 판")]
        public SoundCue FootstepRun = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        [Tooltip("금속 격자 (HullDark 바닥) = 큰 금속 들통 쿵 + 철망 우리 문 떨림, 실제 녹음 (CC0)")]
        public SoundCue StepGrate = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        public SoundCue StepGrateRun = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        [Tooltip("유리 (유리 · 창 · 얼음 바닥) = 부츠 쿵 + 유리 톡 (솔6로 옮겨 짧게), 실제 녹음 (CC0)")]
        public SoundCue StepGlass = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.98f, 1.02f), Cooldown = 0.12f, MaxVoices = 2 };
        public SoundCue StepGlassRun = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.98f, 1.02f), Cooldown = 0.12f, MaxVoices = 2 };
        [Tooltip("잔디 (잔디 · 흙 · 화단 · 모래) = 부츠 쿵 + 덤불 잎 사각, 실제 녹음 (CC0)")]
        public SoundCue StepGrass = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        public SoundCue StepGrassRun = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        [Tooltip("나무 (나무 · 그루터기 · 밀랍 · 깔개) = 판자 바닥 탁 + 나무 문 쿵, 실제 녹음 (CC0)")]
        public SoundCue StepWood = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        public SoundCue StepWoodRun = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        [Tooltip("돌 (진주 몸체 · 길 · 바위 · 콘크리트) = 부츠 쿵 + 벽돌 턱 + 잔돌 알갱이, 실제 녹음 (CC0)")]
        public SoundCue StepStone = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        public SoundCue StepStoneRun = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        [Tooltip("얕은 물 (평평한 물 면 아래 2 ~ 45 cm, 흙 · 돌 위) = 부츠 쿵 + 물 첨벙 첫 순간, 실제 녹음 (CC0)")]
        public SoundCue StepWater = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        public SoundCue StepWaterRun = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        [Tooltip("방 환경음 6묶음 (루프): 생활 / 물·식물 / 기계 / 고열 / 전기·방어 / 넓은 공간 — 방 템플릿의 Ambience로 고름. " +
                 "2026-11-24 분홍 잡음 · 사이렌 같던 합성음을 실제 녹음 (CC0)으로: 방 공기 + 환풍기 / 펌프 물 + 졸졸 / 작은 펌프 / 열풍기 / 안정기 웅 (Bb2) / 밤 바깥 공기, " +
                 "음량은 바탕만 울린 귀 감도 크기를 고치기 전과 같게 (SOUND_LIST_NEW 10번 A)")]
        public SoundCue RoomLife = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.54f, Pitch = Vector2.one };
        public SoundCue RoomWater = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.76f, Pitch = Vector2.one };
        public SoundCue RoomMachine = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.29f, Pitch = Vector2.one };
        public SoundCue RoomHeat = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.28f, Pitch = Vector2.one };
        public SoundCue RoomElectric = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.19f, Pitch = Vector2.one };
        public SoundCue RoomHall = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.62f, Pitch = Vector2.one };
        [Tooltip("11-10 파손된 방 안에 있을 때 울리는 경보 (루프). 2026-11-25 치명 가족 세 음을 2.4초마다 (옛 사이렌 -18 dB), 귀 감도 최대 +3")]
        public SoundCue InteriorAlarm = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.3f, Pitch = Vector2.one };

        [Header("내부 문 (사운드 개편 2026-11-20, SOUND_LIST_NEW 4-1 · D-6 — 문 자리에서 3D)")]
        [Tooltip("일반 미닫이 문 — 기본 단계 방 (2026-11-20 사용자 \"하나로 통일 · 산업 공단 느낌으로 조금 묵직하게\" → 열림 · 닫힘 하나씩, 반복감은 음높이 ±3 %)")]
        public SoundCue DoorOpen = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.03f, MaxVoices = 4 };
        public SoundCue DoorClose = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.03f, MaxVoices = 4 };
        [Tooltip("일반 미닫이 문 — 1단계 방 (같은 문짝, 미래 기술 톤)")]
        public SoundCue Door1Open = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.03f, MaxVoices = 4 };
        public SoundCue Door1Close = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.03f, MaxVoices = 4 };
        [Tooltip("에어록 — 우주복 정원 현관 · 3단계와 붙은 낮은 단계 쪽")]
        public SoundCue AirlockOpen = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.03f, MaxVoices = 3 };
        public SoundCue AirlockClose = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.03f, MaxVoices = 3 };

        [Header("합성 · 이민선 (사운드 개편 2026-11-25, SOUND_LIST_NEW 2번 — 바깥 2D · 화면 좌우, 내부에선 안 울림)")]
        [Tooltip("합성 시작 — 비행 고리가 규격 밑에서 솟아 앉음 (1.4초). 실제 녹음: 헤어드라이어가 켜지는 추진 \"슈우\" + 1.25초 자동 잠금 \"철컥\" + 로봇 서보 공구 \"위잉\"")]
        public SoundCue SynthStart = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.63f, Cooldown = 0.2f, MaxVoices = 2 };
        [Tooltip("합성 진행 중 (2D 루프, 카메라에 가장 가까운 합성 하나 · 거리 · 좌우 · 일시정지로 크기) — 실제 녹음: 호버 공 팬 웅 (C3) + 로봇 서보 위잉 + 용접 지글, 11.5초 고리")]
        public SoundCue SynthLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("합성 완료 1단계 레시피 — 큰 휙 (실제 녹음) + 매끈한 유리 아르페지오 파5 · 라5 · 도6 · 파6 + 사인 패드 (F 장조 합성)")]
        public SoundCue SynthDone1 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Pitch = Vector2.one, Cooldown = 0.3f, MaxVoices = 2 };
        [Tooltip("합성 완료 2단계 레시피 — 큰 휙 + 밝은 꽃 종 굴림 파5 ~ 도7 + 끝 화음 · 반짝임")]
        public SoundCue SynthDone2 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = Vector2.one, Cooldown = 0.3f, MaxVoices = 2 };
        [Tooltip("합성 완료 3단계 이상 레시피 — 큰 휙 + 낮은 유리 종 화음 + 풍경 같은 유리 퐁 + 잎 사각임 (실제 녹음)")]
        public SoundCue SynthDone3 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.48f, Pitch = Vector2.one, Cooldown = 0.3f, MaxVoices = 2 };
        [Tooltip("이민선 도착 (여객 도킹 포트 쪽, 내부에선 그 포트 방에 있을 때만) — 실제 녹음: 화물기 접근 (낮춰 먹먹하게) → 자동 잠금 \"철컥\" + 냉동고 문 \"쿵\" → 공중전화 부스 유압 \"치익\" (6.6초)")]
        public SoundCue ShipImmigrantArrive = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.85f, Cooldown = 2f, MaxVoices = 1 };
        [Tooltip("화물 터미널 화물선 도킹 (2026-11-26, CargoSystem.Delivered 1.5초 뒤: 배가 4.5초에 입구 앞에 섬): 실제 녹음 디젤 트럭 접근 (낮춰 먹먹하게) → 3.0초 트레일러 걸쇠 \"쿵 · 철컹\" → 공기 \"퍽\" (4.2초). " +
                 "이민선과 같은 꼴 (다가옴 · 집게 · 공기)이되 무겁고 짧게, 터미널이 여럿이면 몰려서 8초에 한 번까지. 내부에선 그 터미널 방에 있을 때만. 약 +3.7 (이민선 +5.0 아래, 0.7에서 +6.3을 잼)")]
        public SoundCue ShipCargoDock = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.52f, Cooldown = 8f, MaxVoices = 1 };
        [Tooltip("보급 상자 생김 (2026-11-26, SupplyCrateSystem.Spawn: 보급선 도착): 작은 상자가 내려앉는 \"툭\" + 속 물건 \"달그락\" (실제 녹음, 변형 3). " +
                 "바깥 = 배가 머무는 5.6초 뒤 2D 한두 번 (개수만큼 조금 두껍게), 안 = 상자가 놓인 방에 있으면 상자 자리 3D (InteriorSupply). 바깥 약 +2.5 · 안 방 바탕 위 +5.2")]
        public SoundCue CrateAppear = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.35f, Cooldown = 0.1f, MaxVoices = 3 };

        [Header("위아래 이동 장치 · 유리 엘리베이터 · 방 드나들기 (사운드 개편 2026-11-25, SOUND_LIST_NEW 4-1 · D-6)")]
        [Tooltip("이동 장치 목적지 판이 열림 = 장치 켜짐 (2D, 창 열기 0.5 대신) — 낮은 웅 F2 · F3 · C4 + 서보 \"윙\" + 유리 도6 → 파6")]
        public SoundCue TransitOpen = new SoundCue { Bus = AudioBus.Ui, Volume = 0.3f, Pitch = Vector2.one, Cooldown = 0.2f, MaxVoices = 1 };
        [Tooltip("이동 장치 타기 (2D, 떠나는 장치 단계) 0 = 기본 화물 승강기 칸: 차고 문 체인 모터 (Bb2로 옮김) + 출발 · 도착 유압 \"치익\" (실제 녹음)")]
        public SoundCue TransitRide0 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.65f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("1 = 흰 세라믹 반중력 원판: 낮은 휙 바람결 (실제 녹음) + 아주 옅은 웅 파3 · 도4 · 파4")]
        public SoundCue TransitRide1 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.26f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("2 = 혼천의: 도는 휙 (큰 휙을 초당 3번 일렁이게, 실제 녹음) + 오르골 도6 · 파6 한 음씩")]
        public SoundCue TransitRide2 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.27f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("3 = 수정 꽃 연꽃: 덤불 바람결 + 큰 휙 돌풍 (실제 녹음) + 유리 반짝임 아주 옅게")]
        public SoundCue TransitRide3 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.33f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("4 = 성운 은 원판: 큰 휙을 낮춰 먹먹하게 (실제 녹음) + 반짝이는 패드 · 별 아주 옅게")]
        public SoundCue TransitRide4 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.36f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("이동 장치 도착 (2D, 연출을 끈 설정에서도) — 냉동고 문 \"쿵\"을 아주 먹먹하게 + 짧은 유압 숨 (실제 녹음)")]
        public SoundCue TransitArrive = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.77f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("유리 엘리베이터 버튼 (버튼 자리 3D) — 1단계 방: 사각 버튼 딸깍 (실제 녹음) + 유리 \"삑\" 도6")]
        public SoundCue LiftButton1 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.75f, Pitch = Vector2.one, Cooldown = 0.1f, MaxVoices = 2 };
        [Tooltip("2단계 이상 방: 같은 딸깍 + 밝은 두 음 라5 → 도6")]
        public SoundCue LiftButton2 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.41f, Pitch = Vector2.one, Cooldown = 0.1f, MaxVoices = 2 };
        [Tooltip("유리 엘리베이터가 움직이는 동안 (캡슐에 붙은 3D 루프, 빠르기만큼 크기) — 1단계: 러닝머신 모터 (실제 녹음) + 자기 부상 웅 파3 · 도4 아주 옅게")]
        public SoundCue LiftMove1Loop = new SoundCue { Bus = AudioBus.Sfx, Volume = 1f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("2단계 이상: 같은 모터 + 웅 + 유리 반짝임 파6 · 도6 · 라6 · 레6 옅게")]
        public SoundCue LiftMove2Loop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.95f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("유리 엘리베이터 도착 (캡슐 자리 3D) — 1단계: 꽃 종 \"띵\" 도6 + 문 \"스르륵\" (자판기 미닫이 실제 녹음)")]
        public SoundCue LiftArrive1 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.31f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("2단계 이상: \"띵 동\" 라6 → 파6 + 반짝 + 문 \"스르륵\"")]
        public SoundCue LiftArrive2 = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.28f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("내부 들어가기 (2D, 창 열기 대신) — 큰 휙 \"우웅\" (실제 녹음, 3반음 낮춤) + 0.7초 해치 유압 \"치익\"")]
        public SoundCue InteriorEnter = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.43f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("내부 나가기 (2D, 창 닫기 대신) — 큰 휙이 잦아드는 \"우웅\" (5반음 낮춤) + 해치 닫힘 \"쿵\" 옅게")]
        public SoundCue InteriorExit = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("패드 빠른 이동 · 떨어짐 구조 (2D, 창 열기 0.5 대신, 2026-11-25) — 큰 휙을 짧고 가볍게 (실제 녹음, 2반음 올림) + 유리 두 음 라5 (어두워짐) → 도6 (밝아짐)")]
        public SoundCue PadTravel = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.35f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };

        [Header("내부 장치 (사운드 개편 2026-11-26, SOUND_LIST_NEW 4-1 · 4-2 · 4-3: 금고문 · 빛 다리 · 우주복 · 패드 · 레이더 · 신호판 · 부탁 · 보급품 · 침입 드론)")]
        [Tooltip("방사선 차폐실 금고문 열림 (차폐실 방 쪽 문짝 자리 3D, InteriorDeviceSound.DoorFor): 실제 녹음 축음기 손잡이 돌림 \"드르륵\" (바퀴) + 업소용 냉동고 손잡이 풀림 + 끝 \"툭\"")]
        public SoundCue DoorVaultOpen = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.57f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.03f, MaxVoices = 3 };
        [Tooltip("금고문 닫힘: 냉동고 문 먹먹한 \"쿵\" (0.25초) → 바퀴로 잠금 \"드르륵\" → 공구함 걸쇠 \"철컥\"")]
        public SoundCue DoorVaultClose = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.49f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.03f, MaxVoices = 3 };
        [Tooltip("빛 다리 · 문 승강판을 타는 동안 (2D 루프, InteriorMode): 호버 공 팬 웅 (실제 녹음) + 빛 판 \"웅웅\" 1.5 Hz 맥동 + 유리 결 파5 · 도6 · 파6 아주 옅게")]
        public SoundCue LightBridgeLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.27f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("2단계 개방형 정원 우주복 신발 (헬멧 밖에서 걸을 때 바닥 발소리 위에 겹침 2D): 천 \"슥\" + 두툼한 \"푹\" (실제 녹음 옷 휘두름), 변형 6")]
        public SoundCue StepSuit = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.65f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.12f, MaxVoices = 2 };
        [Tooltip("우주복 헬멧 잠김 (실내 구역 밖으로 나설 때 2D, InteriorDeviceSound.SuitOn): 과자통 뚜껑 \"딸깍\" + 짧은 김 \"칙\" + 유리 파6 아주 옅게")]
        public SoundCue SuitHelmet = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("헬멧 안 숨 (헬멧 밖에 있는 동안 2D 루프, 아주 작게): 방독면 너머 숨쉬기 (실제 녹음) 1.4 kHz 아래, 숨 넷 10.25초")]
        public SoundCue SuitBreathLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.14f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("휴대 패드 꺼내기 (2D, 창 열기 0.6을 빌려 쓰던 자리): 오르는 바람결 + 유리 도5 · 파5 · 도6 + 낮은 웅 파3 (UI 한 벌과 같은 결)")]
        public SoundCue PadRaise = new SoundCue { Bus = AudioBus.Ui, Volume = 0.16f, Pitch = Vector2.one, Cooldown = 0.1f, MaxVoices = 1 };
        [Tooltip("패드 넣기 (창 닫기 0.6 대신): 도6 · 파5 · 도5 + 잦아드는 웅")]
        public SoundCue PadLower = new SoundCue { Bus = AudioBus.Ui, Volume = 0.31f, Pitch = Vector2.one, Cooldown = 0.1f, MaxVoices = 1 };
        [Tooltip("패드 홀로그램 모형 (확대해 모형이 보이는 동안 2D 루프): 형광등 안정기 웅 (실제 녹음, 시b2) + 유리 파5 · 도6 깜빡임 아주 옅게")]
        public SoundCue PadHoloLoop = new SoundCue { Bus = AudioBus.Ui, Volume = 0.37f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("센서 어레이 레이더 화면 (표시대 3D 루프, 빛줄 한 바퀴 6초에 핑 하나): 유리 꽃병 \"딩\" 도6 (실제 녹음) + 메아리 + 옅은 PC 팬 웅. 예보 · 낙하 중이면 크게 (InteriorDeviceSound.RadarLevel)")]
        public SoundCue RadarPingLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.17f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("해독실 통신 신호판 먼 무전 잡음 (신호판 3D 루프): 김 \"쉬\" + 전기 아크 \"지직\" (실제 녹음) 400 Hz ~ 2.8 kHz. 크기 = 신호판 파형과 같은 갈래 (InteriorDeviceSound.SignalNoise)")]
        public SoundCue SignalNoiseLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.17f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("신호가 잡힘 (해독 끝, 신호판 3D): 잡음 \"치\"가 잦아들며 유리 파6 · 도7")]
        public SoundCue SignalLock = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.47f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("주민 부탁 말풍선 (2D 알림, 창 열기 0.8을 빌려 쓰던 자리): 입으로 내는 \"뽁\" (실제 녹음, 레6로 맞춤) + 같은 높이 유리 결 옅게")]
        public SoundCue RequestPop = new SoundCue { Bus = AudioBus.Sfx, Volume = 1f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("주민 부탁 완료 (2D, 좋은 소식 0.7 대신. 같은 프레임의 F 길게 누르기 완료 화음은 멈춤): 꽃 종 \"띵 동\" 도6 → 파6")]
        public SoundCue RequestDone = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.62f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("시든 화분 물 주기 (F 누르는 1.2초, 화분 3D, 누르기 시작부터 · 떼면 꺼짐): 컵에 물 붓는 졸졸 + 끝 물방울 (실제 녹음)")]
        public SoundCue WaterPour = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.49f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("덜컹거리는 환풍구 조이기 (F 누르는 1.5초, 환풍구 3D): 덮개 \"덜컥\" → 나사 \"끼릭\" 셋 → 1.45초 \"톡\" → 조용 (실제 녹음)")]
        public SoundCue VentFix = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.49f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("환풍구 덮개가 덜컹거림 (환풍구 3D, 1.5 ~ 4초마다, BuildRotate를 빌려 쓰던 자리): 문고리 흔들림 \"덜컥\" (실제 녹음) 변형 3")]
        public SoundCue VentRattle = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.35f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.3f, MaxVoices = 1 };
        [Tooltip("보급 상자 줍기 (상자 자리 3D, 보급선 도착 소리를 빌려 쓰던 자리. 같은 프레임의 F 길게 누르기 완료 화음은 멈춤): 통 손잡이 \"덜컥\" + 상자 \"스윽\" (실제 녹음)")]
        public SoundCue CratePickup = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.49f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.2f, MaxVoices = 1 };
        [Tooltip("침입 드론이 선체를 뚫고 들어옴 (새 습격, 대상 방 자리 3D 25 m까지): 아크 \"지직\" → 판이 휘는 \"찌그럭 툭\" + 김 \"슉\" → 날개가 켜짐 (실제 녹음)")]
        public SoundCue DroneBreach = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("침입 드론 떠 있음 (드론마다 3D 루프, 지금 있는 방일 때만): RC 헬기 날개 웅 (실제 녹음, 날개 줄을 파 음으로) 5 kHz 아래")]
        public SoundCue DroneHoverLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("침입 드론을 끔 (드론 자리 3D, 스파크를 빌려 쓰던 자리): 날개가 잦아듦 + 작은 \"지직\" + 바닥에 \"툭\" (실제 녹음)")]
        public SoundCue DroneDown = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.49f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.1f, MaxVoices = 2 };

        [Header("1인칭 할 일 (사운드 개편 2026-11-25, SOUND_LIST_NEW 4-3 — 현장 수리 · 내부 사고 4종 · F 길게 누르기)")]
        [Tooltip("F 길게 누르기 (0.5초 넘는 것만, 2D 루프) — 유리 결 파5 \"차오르는 음\", 코드가 누른 만큼 재생 높이를 파5 → 도6으로 올림 (UI 한 벌과 같은 F 장조 합성)")]
        public SoundCue HoldLoop = new SoundCue { Bus = AudioBus.Ui, Volume = 0.3f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("F 길게 누르기를 다 누름 (2D) — 파5 · 라5 · 도6 + 위 파6 짧은 화음 (차오르던 도6에서 풀림)")]
        public SoundCue HoldDone = new SoundCue { Bus = AudioBus.Ui, Volume = 0.45f, Pitch = Vector2.one, Cooldown = 0.1f, MaxVoices = 1 };
        [Tooltip("현장 긴급 수리 — 손상 지점을 누르는 동안 그 지점 3D 루프 (예전 0.12초마다 스파크 \"지직\") — 실제 녹음: 래칫 렌치 조임 · 전동 드라이버 · 부드러운 용접을 이은 5.6초 고리")]
        public SoundCue FieldRepairLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("배관 누출 — 새는 이음에 붙은 3D 루프 (지금 방에 있을 때만, 밸브를 돌리는 만큼 줄어듦) — 실제 녹음: 스팀 청소기 김 + 스프레이 캔 쉬익, 작은 크기 (큰 = pipe_burst_big_loop 파일만, 예정: 이웃에 따라 규모)")]
        public SoundCue PipeBurstLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.35f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("누출 밸브를 F로 돌리는 2.0초 (바퀴 3D, 누르기 시작부터 · 떼면 꺼짐) — 실제 녹음: 다이얼 \"드르륵\" + 2.0초에 금속 \"툭\"")]
        public SoundCue ValveTurn = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 2 };
        [Tooltip("차단기 함 문 열기 (함 문 3D, UiOpen 대신) — 실제 녹음: 금속 우편함 열림 \"철컥\"")]
        public SoundCue BreakerDoor = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.2f, MaxVoices = 1 };
        [Tooltip("차단기 경고등 켜진 스위치를 올림 (스위치 3D, BuildRotate 대신) — 실제 녹음: 토글 스위치 + 벽 스위치 + 금속 함 \"툭\"을 겹친 큰 \"철컥\"")]
        public SoundCue BreakerSwitch = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.05f, MaxVoices = 2 };
        [Tooltip("마지막 스위치 = 전기 돌아옴 (스위치 3D) — 같은 \"딸깍\" + 전기 방 바탕과 같은 안정기 웅 (Bb2)이 차올랐다 잦아듦")]
        public SoundCue BreakerFlip = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Pitch = Vector2.one, Cooldown = 0.2f, MaxVoices = 1 };
        [Tooltip("작은 화재 — 배전함 위 불꽃에 붙은 3D 루프 (지금 방에 있을 때만, 분사하는 만큼 줄어듦) — 실제 녹음: 벽난로 가까이 타닥, 작은 크기 (큰 = fire_big_loop 파일만, 예정: 이웃에 따라 규모)")]
        public SoundCue FireLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 1f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("벽의 소화기를 듦 (거치대 3D, BuildSelect 대신) — 실제 녹음: 금속 통을 집어 드는 짧은 \"철컥\"")]
        public SoundCue ExtinguisherPickup = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.65f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.2f, MaxVoices = 1 };
        [Tooltip("불에 대고 F를 누르는 2.0초 소화기 분사 (들고 있는 소화기 = 눈 앞, 누르기 시작부터 · 떼면 꺼짐) — 실제 녹음: 길게 뿌리는 \"쉬익\"")]
        public SoundCue Extinguish = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.3f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("환기 경보 필터 교체 1.8초 (필터함 3D, 누르기 시작부터 · 떼면 꺼짐) — 실제 녹음: 걸쇠 풀림 · 카트리지가 미끄러져 나왔다 들어감 · 걸쇠 \"딸깍\"")]
        public SoundCue FilterSwap = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.65f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };

        [Header("체험 · 미니게임 공용 (사운드 개편 2026-11-23, SOUND_LIST_NEW 5-1 — 창 열기 · 닫기 같은 UI 소리를 빌려 쓰던 자리)")]
        [Tooltip("미니게임 시작 — 맑은 음 셋 (3 · 2 · 1) + 출발 화음, 음악 조 (F 장조)에 맞춰 합성")]
        public SoundCue GameStart = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.34f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("남은 시간이 얼마 안 남았을 때 1초마다 — 작은 나무 \"톡\"")]
        public SoundCue GameTick = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.66f, Pitch = Vector2.one, Cooldown = 0.2f, MaxVoices = 1 };
        [Tooltip("하나 맞힘 · 점수 — 밝은 \"띵\" (도6). 연달아 맞히면 AudioService.TryPlayScore가 F 오음계로 한 칸씩 높임 (음높이 흔들림 없음 — 조가 안 깨지게)")]
        public SoundCue GameScore = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.28f, Pitch = Vector2.one, Cooldown = 0.05f, MaxVoices = 2 };
        [Tooltip("틀림 · 놓침 — 부드럽게 내려가는 \"뿌\" (거절음보다 가볍게)")]
        public SoundCue GameMiss = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = Vector2.one, Cooldown = 0.2f, MaxVoices = 1 };
        [Tooltip("결과: 성공 = 짧은 오름 팡파르 · 아쉬움 = 내려가는 두 음 · 새 기록 = 더 큰 팡파르 + 반짝임 (모두 F 장조 합성)")]
        public SoundCue GameClear = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.41f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        public SoundCue GameFail = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.35f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        public SoundCue GameRecord = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("체험 탑승 (자리에 앉고 안전바가 \"딸깍\") · 내림 (안전바가 풀리고 일어남)")]
        public SoundCue RideBoard = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.65f, Pitch = new Vector2(0.98f, 1.02f), Cooldown = 0.3f, MaxVoices = 1 };
        public SoundCue RideLeave = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.38f, Pitch = new Vector2(0.98f, 1.02f), Cooldown = 0.3f, MaxVoices = 1 };

        [Header("체험 — 궤도 도시 금빛 궤도열차 (5-2, 2026-11-23)")]
        [Tooltip("탄 동안 달리는 소리 (2D 루프, 빠르기에 따라 크기 · 재생 높이) — VARCO 낮은 전기 노면 열차 \"웅\" (5개 중 가장 고름 · 울림 0) + 합성 레일 이음 \"다-닥\" 옅게")]
        public SoundCue OrbitTrainLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 1f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("S 브레이크를 거는 순간 (빠를 때만) — VARCO 부드러운 공기 브레이크 \"푸쉬\" (끽 소리 X, 5개 중 가장 둥근 시작)")]
        public SoundCue OrbitTrainBrake = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.8f, MaxVoices = 1 };
        [Tooltip("판정 뒤 정지선에 딱 맞춰 역에 섬 — 합성 유리 종 파 · 라 · 도 · 파 굴린 화음 (F 장조, 결과음은 3.5초 전에 끝남)")]
        public SoundCue OrbitTrainStopPerfect = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };

        [Header("체험 — 바이오스피어 물꼬 맞추기 · 반딧불이 잡기 (5-2, 2026-11-23)")]
        [Tooltip("수문 바퀴를 한 칸 돌림 (바퀴 자리 3D) — VARCO 나무 톱니 \"드르륵\" + 바퀴가 서는 0.5초에 \"툭\"")]
        public SoundCue BioSluiceWheel = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.66f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.1f, MaxVoices = 3 };
        [Tooltip("물꼬가 열려 물이 흐르기 시작 (그 물길 수문 3D, 목마른 밭으로 가면 조금 크게) — 소리 풍경 수문 폭포 + 물길 졸졸 원본을 1.6초 부풀었다 잦아들게")]
        public SoundCue BioWaterFlow = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Pitch = new Vector2(0.95f, 1.05f), Cooldown = 0.15f, MaxVoices = 3 };
        [Tooltip("반딧불이를 병에 담음 (2D) — 합성 병 \"퐁\" (솔5 → 도6) + 반짝임, 연달아 잡으면 TryPlayNote로 음계를 오름 (기준 = 도6)")]
        public SoundCue BioFireflyCatch = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = Vector2.one, Cooldown = 0.05f, MaxVoices = 2 };
        [Tooltip("물이 닿아 목마른 밭이 핌 (2D, 공용 점수 대신 — 연달아 피면 TryPlayNote로 음계, 기준 = 도6) — 꽃잎 \"사르륵\" (고목 잎 원본) + 유리 종 도6 + 반짝 솔6 · 도7 (5-2 넷째 묶음)")]
        public SoundCue BioFieldBloom = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.25f, Pitch = Vector2.one, Cooldown = 0.03f, MaxVoices = 3 };

        [Header("체험 — 궤도 방어망 오로라 보호막 · 등대 빛 돌리기 · 별똥별 막기 (5-2, 2026-11-23)")]
        [Tooltip("방파제 끝 레버 당김 (레버 자리 3D) — 나무 \"끼익\" (궤도 항만 삐걱임 원본) + VARCO 나무 \"툭\" (레버가 끝에 닿는 0.5초)")]
        public SoundCue OdefLeverPull = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Pitch = new Vector2(0.98f, 1.02f), Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("오로라 보호막이 펼쳐짐 (레버 0.6초 뒤, 2D 큰 순간) — 밤 오로라 울림 조각이 차오름 + 도 오음계 반짝임이 올라감 (4초)")]
        public SoundCue OdefShieldUnfurl = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("별똥별이 보호막에 맞음 (2D) — 합성 둥근 유리 \"퐁\" + 반짝, 맞을 때마다 도 · 레 · 파 · 솔 · 라 되풀이 (기준 = 도5)")]
        public SoundCue OdefStarHitShield = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.65f, Pitch = Vector2.one, Cooldown = 0.05f, MaxVoices = 3 };
        [Tooltip("등대 금빛 손잡이를 잡은 동안 (2D 루프, 돌리는 빠르기만큼 크기) — VARCO 나무 굴대 \"웅\" + 한 바퀴마다 낮은 나무 \"끄응\" (딸깍 X)")]
        public SoundCue OdefCrankLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("빛이 닿아 새 곳이 답함 (2D, 찾은 차례대로 음계) · 이미 찾은 곳 = 그 곳의 음을 작게 — 실제 녹음: 유리 꽃병 \"딩\" (기준 = 도6, 2026-11-25 사인파로 들리던 합성 유리 종 (도5)을 바꿈)")]
        public SoundCue OdefBeamReact = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = Vector2.one, Cooldown = 0.05f, MaxVoices = 3 };
        [Tooltip("별똥별 막기 포 쏨 (클릭마다 2D, 0.22초마다 연타) — 합성 낮은 공기 \"퐁\" + 짧은 \"쉿\" (0.25초, 가볍게)")]
        public SoundCue OdefCannonFire = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.05f, MaxVoices = 2 };
        [Tooltip("맞혀서 별똥별이 터짐 (2D, 공용 점수 \"띵\" 대신 — 연달아 맞히면 음계로) — 합성 \"팟\" + 반짝임 흩어짐 (기준 = 도5)")]
        public SoundCue OdefStarBurst = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = Vector2.one, Cooldown = 0.05f, MaxVoices = 3 };
        [Tooltip("별똥별이 다가옴 (보호막 쇼 · 별똥별 막기, 2D — 바깥 운석 경보 MeteorIncoming 대신) — 소리 풍경 별똥별 원본의 \"휘이잉\"이 커지며 지나감 (5-2 넷째 묶음)")]
        public SoundCue OdefStarIncoming = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.32f, Pitch = new Vector2(0.95f, 1.05f), Cooldown = 0.2f, MaxVoices = 2 };

        [Header("체험 — 수리 사령부 구조 바구니 · 경종 등불 쇼 · 드론 순서 기억 (5-2, 2026-11-23)")]
        [Tooltip("탄 바구니가 오르내리는 동안 (2D 루프, 줄을 당길 때 · 내려갈 때) — VARCO 줄이 나무 도르래를 지나는 결")]
        public SoundCue RcmdRopeLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("바구니가 정류장 · 나무집 마루에 닿음 (바구니 자리 3D) — 궤도 항만 삐걱임 원본 + 궤도 방어망 레버 \"툭\"을 가볍게")]
        public SoundCue RcmdBasketCreak = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("빈 바구니를 부름 (바구니 자리 3D, UiOpen 대신 — 그 뒤 혼자 오르내리는 동안은 줄 루프를 바구니에 붙여 3D) — 멈춤쇠 \"딸깍\" (5-1 걸쇠 조각) + 줄 \"스윽\" (5-2 넷째 묶음)")]
        public SoundCue RcmdBasketCall = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("금빛 경종 \"딩 — 딩\" (종 자리 3D) — 소리 풍경 경종 (scape_repair_bell_01, F4) 그대로")]
        public SoundCue RcmdBellToll = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 2 };
        [Tooltip("드론이 가지 등불을 켤 때마다 (그 등불 3D, 켠 차례대로 음계) — 합성 반딧불 반짝 (소리 풍경 반딧불과 같은 결, 기준 = 도5)")]
        public SoundCue RcmdLanternShimmer = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = Vector2.one, Cooldown = 0.03f, MaxVoices = 4 };
        [Tooltip("드론 떼가 공방에서 날아오름 (쇼 2단계, 2D) — 합성 작은 반짝임이 F 오음계로 흩어지며 올라감 + 옅은 바람")]
        public SoundCue RcmdDronesRise = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.3f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("쇼 피날레 — 등불이 다 켜지고 떼가 환히 빛남 (2D 큰 순간) — 합성 꽃 종 F 장조 화음 + 반짝임이 흩어져 내림 + 낮은 하프")]
        public SoundCue RcmdShowFinale = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("드론 순서 기억 등불 다섯 (2D, 보여 줄 때 · 고를 때) — 합성 유리 종 한 음, 등불 k = 도 · 레 · 파 · 솔 · 라 (기준 = 도5)")]
        public SoundCue RcmdLanternNote = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.37f, Pitch = Vector2.one, Cooldown = 0.03f, MaxVoices = 3 };

        [Header("체험 — 궤도 항만 가리비 배 뱃놀이 · 진주 조개 잡기 · 인어 분수 진주 던지기 (5-2, 2026-11-23)")]
        [Tooltip("배가 나아가는 동안 (탄 사람 귀에 2D 루프, 빠르기만큼 크기) — VARCO 부드러운 노 첨벙을 2박 (1.33초)마다 + 작은 노걸이 \"끽\" (선착장 삐걱임 원본)")]
        public SoundCue OportPaddleLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("배가 선착장에 닿음 (배 자리 3D) — 선착장 삐걱임 원본 + 나무 \"툭\" (구조 바구니 닿음과 같은 조각, 더 낮고 큼)")]
        public SoundCue OportBoatDock = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.5f, MaxVoices = 2 };
        [Tooltip("진주 조개가 열림 (그 조개 자리 3D) — VARCO 껍데기 \"스르륵\" + 다 열린 0.2초에 작은 \"딸깍\"")]
        public SoundCue OportClamOpen = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.48f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.03f, MaxVoices = 3 };
        [Tooltip("진주 조개가 닫힘 (다 닫히는 순간 그 조개 자리 3D) — VARCO 둥근 \"탁\" (밝은 딸깍 · 날 선 후보는 뺌)")]
        public SoundCue OportClamSnap = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.03f, MaxVoices = 3 };
        [Tooltip("진주를 잡음 (2D, 공용 점수 대신 — 연달아 잡으면 TryPlayNote로 음계, 금빛 진주는 1.3배) — 합성 둥근 유리 \"또롱\" + 반짝임 (기준 = 도6)")]
        public SoundCue OportPearlGet = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.3f, Pitch = Vector2.one, Cooldown = 0.03f, MaxVoices = 3 };
        [Tooltip("진주를 던짐 (F마다 2D) — 합성 가벼운 \"휙\" (UiClick 대신)")]
        public SoundCue OportThrow = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Pitch = new Vector2(0.94f, 1.06f), Cooldown = 0.1f, MaxVoices = 2 };
        [Tooltip("진주가 분수 물에 떨어짐 (떨어진 자리 3D — 고리에 들면 공용 점수 \"띵\"과 같이) — 소리 풍경 물고기 퐁당 (scape_port_fish_02) 그대로")]
        public SoundCue OportPlop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = new Vector2(1.05f, 1.2f), Cooldown = 0.05f, MaxVoices = 3 };

        [Header("체험 — AI 중추 수정 뇌 깨우기 · 정원 드론 날기 · 뇌 미로 빛 구슬 · 시냅스 따라 켜기 (5-2, 2026-11-23)")]
        [Tooltip("책을 펼침 (그 책 받침 자리 3D) — 소리 풍경 독서대 책장 원본 + 펼쳐진 책장이 닿는 작은 \"툭\" (UiTab 대신)")]
        public SoundCue AicoreBookOpen = new SoundCue { Bus = AudioBus.Sfx, Volume = 1f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.2f, MaxVoices = 2 };
        [Tooltip("수정 뇌가 깨어나 차오름 (쇼 시작 2D 큰 순간, EventPositive 대신) — 합성 유리 화음 라 · 도 · 미가 부풂 + 소리 풍경 수정 뇌 공명 + 반짝임 오름")]
        public SoundCue AicoreCrystalSwell = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("쇼 끝 — 빛이 잦아드는 3.5초 (2D, UiClose 대신) — 차오름과 같은 유리 화음이 먹먹해지며 잦아듦 + 반짝임이 내려감 (5-2 넷째 묶음)")]
        public SoundCue AicoreCrystalFade = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.38f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("드론을 타고 나는 동안 (2D 루프, 빠르기만큼) — 궤도 도시 옥상 바람 원본 + 작은 날개 \"그르릉\" 합성 (소리 풍경 드론과 같은 결)")]
        public SoundCue AicoreDroneRideLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.75f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("드론이 착륙장에 내려앉음 (드론 자리 3D, SupplyArrive 대신) — 부드러운 \"툭\" + 날개 그르릉이 멎음")]
        public SoundCue AicoreDroneLand = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("빛 구슬 주움 (2D, 공용 점수 대신 — 모은 차례대로 음계) — 합성 \"띠리링\" 도 · 솔 · 위 도 (기준 = 도6)")]
        public SoundCue AicoreOrbCollect = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.25f, Pitch = Vector2.one, Cooldown = 0.03f, MaxVoices = 2 };
        [Tooltip("시냅스 여섯 (2D, 보여 줄 때 · 고를 때) — 소리 풍경 수정 띵을 한 옥타브 내려 (도6), 시냅스 k = 도 · 레 · 파 · 솔 · 라 · 위 도")]
        public SoundCue AicoreSynapseNote = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = Vector2.one, Cooldown = 0.03f, MaxVoices = 3 };

        [Header("체험 — 항성 발전망 정자 꽃 그네 · 벌 몰래 꿀 먹기 (5-2, 2026-11-23)")]
        [Tooltip("그네가 가장 낮은 점을 지날 때마다 \"휘익\" (빠를수록 크게, 탄 사람 2D · 보는 사람 3D) — 합성 걸러진 바람")]
        public SoundCue SgridSwingWhoosh = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.95f, 1.05f), Cooldown = 0.3f, MaxVoices = 2 };
        [Tooltip("그네가 수정 꽃송이 종에 닿음 (그 종 자리 3D, UiTab 대신) — 소리 풍경 수정 꽃 종 (도6) 그대로, 높은 종일수록 높은 음 (도 · 레 · 파 · 솔 · 라 · 위 도)")]
        public SoundCue SgridFlowerBell = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.33f, Pitch = Vector2.one, Cooldown = 0.03f, MaxVoices = 4 };
        [Tooltip("경비 꿀벌 윙윙 (미니게임 동안 경비 벌에 붙은 3D 루프, 경고 · 쏘러 갈 때 높고 크게) — 소리 풍경 벌 원본의 배음으로 다시 합성 (F3)")]
        public SoundCue SgridBeeGuardLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("꿀 한 숟갈 (2D, 공용 점수 \"띵\"과 겹쳐) — VARCO 곰 \"후룹 · 냠\" 0.55초")]
        public SoundCue SgridHoneySlurp = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.64f, Pitch = new Vector2(0.95f, 1.05f), Cooldown = 0.2f, MaxVoices = 1 };
        [Tooltip("들킴 (2D, 공용 틀림 대신) — 경비 벌이 \"윙!\" 치솟음 (같은 벌 합성) + 곰 \"이잇\" (VARCO, 라5)")]
        public SoundCue SgridCaught = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };

        [Header("체험 — 심우주 통신기지 전파 접시 · 별똥별 아치 소원 · 신호 맞추기 · 별똥별 잡기 (5-2, 2026-11-23)")]
        [Tooltip("손바퀴를 돌린 접시가 돌아섬 (접시 자리 3D, 2.6초 — RepairStart 대신) — 실제 녹음: 차 선루프 모터가 \"툭\" 걸려 도는 \"위잉\" (D5, 2026-11-25 빠르기 곡선으로 높이가 오르내려 사이렌으로 들리던 VARCO를 바꿈)")]
        public SoundCue DscDishMotor = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.37f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.1f, MaxVoices = 2 };
        [Tooltip("접시가 신호를 잡음 (그 접시 3D) · 빛 알이 돔에 닿음 (모은 신호 화음 2D) — 소리 풍경 접시 신호 \"삐 · 빕\" (도6) 그대로, 파 · 라 · 도 · 위 파 (모일수록 화음)")]
        public SoundCue DscSignalLock = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.48f, Pitch = Vector2.one, Cooldown = 0.03f, MaxVoices = 5 };
        [Tooltip("신호 넷이 가운데 관측실에 모임 (2D, GameClear 대신) — 소리 풍경 대표 선율 (신호 \"삐 · 빕\" → 유리 별) 그대로")]
        public SoundCue DscSignalMotif = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.68f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("아치 소원 시작 (2D, UiOpen 대신) — 항성 발전망 수정 꽃 종을 한 옥타브 위로 (이 방 \"강 위 유리 별\"과 같은 소리, TryPlayNote 칸 5)")]
        public SoundCue DscWishBegin = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.3f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("별똥별이 하늘을 가로지름 (2D, UiTab 대신) — 궤도 방어망 별똥별 (scape_defense_star_01) 그대로")]
        public SoundCue DscWishStar = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.36f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("별똥별이 강에 닿아 별빛이 번짐 (2D 큰 순간, EventPositive 대신) — 합성 반짝임이 F 오음계로 쏟아져 내림 + 유리 종 화음")]
        public SoundCue DscWishSparkle = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.38f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("신호 맞추기 무전 잡음 (조작대에 선 동안 2D 루프, 크기 = 1 − 맞은 정도) — 합성 걸러진 잡음 + 아주 옅은 지직")]
        public SoundCue DscStaticLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.47f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("신호를 맞춤 (2D, 공용 점수 · UiClick 대신 — 85점 이상 = 크게 · 연달아 위 음, 아래 = 작게 도) — 합성 신호 \"삐 · 빕\" + 유리 종 (기준 = 도6)")]
        public SoundCue DscTuneLock = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.35f, Pitch = Vector2.one, Cooldown = 0.1f, MaxVoices = 2 };
        [Tooltip("별똥별 잡음 (2D, 공용 점수 대신 — 연달아 잡으면 음계, 큰 별은 1.3배) — 합성 \"슉\" + 반짝임 도 · 솔 · 위 도 (기준 = 도6)")]
        public SoundCue DscStarCatch = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.31f, Pitch = Vector2.one, Cooldown = 0.03f, MaxVoices = 3 };

        [Header("체험 — 대형 토러스 은하철도 링 기차 (5-2, 2026-11-23)")]
        [Tooltip("문이 닫히기 시작할 때 출발 기적 (역 근처 · 탄 사람 2D, RideBoard 대신) — VARCO 장난감 증기 기적 \"뽀 뽀오—\" (F5 그대로 — 조 안)")]
        public SoundCue TorusWhistle = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("탄 동안 달리는 소리 (2D 루프, 빠르기만큼 크기 · 재생 높이) — VARCO 증기 기차 칙칙폭폭 (5개 중 가장 규칙적 · 고른 크기)")]
        public SoundCue TorusRunLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("역에 서는 순간 증기 \"쉬익\" (역 근처 · 탄 사람 2D, SupplyArrive 대신) — VARCO 증기 쉿을 부풀었다 잦아들게 · 높은 쉿을 눌러 부드럽게")]
        public SoundCue TorusBrake = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.32f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.8f, MaxVoices = 1 };

        [Header("체험 — 식량 공장 유리 바퀴 관람차 (5-2, 2026-11-23)")]
        [Tooltip("맨 아래 곤돌라 유리문이 열림 · 닫힘 (바퀴 가까이 · 탄 사람 2D, RideLeave · RideBoard 대신) — 조개 \"스르륵\" 원본을 낮춰 + 끝에 걸쇠 \"딸깍\"")]
        public SoundCue FerrisDoor = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.41f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.2f, MaxVoices = 2 };
        [Tooltip("탄 곤돌라가 도는 동안 (2D 루프, 바퀴 빠르기만큼) — 아주 느린 나무 삐걱 (선착장 삐걱임 원본) + 높은 곳 바람 (궤도 도시 옥상 바람 원본)")]
        public SoundCue FerrisCreakLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };

        [Header("체험 — 저장 단지 별빛 회전목마 (5-2, 2026-11-23)")]
        [Tooltip("출발 종 \"딩 딩 딩—\" (회전목마 가까이 · 탄 사람 2D, RideBoard 대신 — 멈출 때는 작게 한 번) — 합성 유리 종 파5 · 파5 · 도6")]
        public SoundCue CarouselBell = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.33f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("오르골 (회전목마에 붙은 3D 루프, 도는 동안 크기 1 · 서면 0) — 합성 F 오음계 선율 12마디 · 90 BPM 48박 = 내부 음악 박에 맞춰 시작 (ObjectLoop.SyncBeat)")]
        public SoundCue CarouselMusicLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };

        [Header("체험 — 화물 환적장 은하수 후룸 (5-2, 2026-11-23)")]
        [Tooltip("체인 리프트 오르막 (탄 사람 2D 루프) — VARCO 둥근 래칫 \"딸깍\" 넷을 0.5초마다 + 물길 졸졸 옅게")]
        public SoundCue FlumeChainLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.76f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("내리막 · 물길을 타고 내려감 (탄 사람 2D 루프, 빠르기만큼 크기 · 높이) — 바이오스피어 수문 폭포 루프 (scape_bio_sluice_loop) 그대로")]
        public SoundCue FlumeRushLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("큰 낙하 끝 물보라 \"촤악\" (탄 보트 = 2D · 다른 보트 = 물보라 자리 3D) — VARCO 후룸 물보라 (바로 촤악 → 물방울이 쏟아져 잦아듦)")]
        public SoundCue FlumeBigSplash = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.65f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.5f, MaxVoices = 2 };
        [Tooltip("보트가 정차장에 섬 (정차장 근처 · 탄 사람 2D, SupplyArrive 대신) — 노 첨벙 원본 \"철썩\" + 나무 \"툭\"")]
        public SoundCue FlumeDock = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.53f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.5f, MaxVoices = 1 };

        [Header("체험 — 연구원 별빛 데이터 미로 (5-2, 2026-11-23)")]
        [Tooltip("미로 안에서 시간을 재는 동안 (2D 루프) — 궤도 방어망 수정 포대 루프 (낮은 웅웅 + 도 오음계 반짝임, scape_defense_battery_loop) 그대로")]
        public SoundCue MazeHumLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 1f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("가운데 연구실 도착 (2D — 결과음 0.5초 앞) — 합성 유리 아르페지오 도6 · 레6 · 파6 · 라6 + 반짝임 \"데이터 문이 열림\"")]
        public SoundCue MazeExit = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.3f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };

        [Header("체험 — 드론 정비창 드론 라이트 쇼 (5-2, 2026-11-23)")]
        [Tooltip("쇼 동안 드론 떼 웅 (2D 루프) — 실제 녹음: RC 헬기 날개 여섯 겹 (날개 음 F3 · D5, 2026-11-25 음악 · 피아노로 들리던 합성 떼를 바꿈)")]
        public SoundCue DroneShowSwarmLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.85f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("쇼 시작 — 드론 떼가 떠오름 (2D, UiOpen 대신) — 수리 사령부 \"드론 떼 날아오름\" 반짝임 (rcmd_drones_rise) 그대로")]
        public SoundCue DroneShowRise = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.34f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };

        [Header("체험 — 요새 포대 폭죽놀이 직관 (5-2, 2026-11-23)")]
        [Tooltip("포구에서 쏨 \"퉁\" (포구 자리 3D, 0.8초 뒤 하늘에서 터짐) — 실제 발사통 녹음 (CC0) + 짧고 옅은 쉬익 (2026-11-24 합성 쉬익만으론 잡음 · 파도처럼 들려 바꿈)")]
        public SoundCue FireworkLaunch = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = new Vector2(0.94f, 1.06f), Cooldown = 0.1f, MaxVoices = 3 };
        [Tooltip("하늘 화면에서 터짐 (2D, 넷 중 하나 · 재생 높이 ±10 %) — 실제 공중 폭죽 녹음 (CC0) + 하늘 울림 꼬리 (2026-11-24 합성 \"팡 + 지지직\"은 몸통이 비고 고음 · 지직만 많아 딱총처럼 들려 바꿈)")]
        public SoundCue FireworkBurst = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = new Vector2(0.9f, 1.1f), Cooldown = 0.1f, MaxVoices = 3 };
        [Tooltip("쇼 레버 당김 (레버 자리 3D, UiOpen 대신) — 궤도 방어망 레버와 같은 나무 \"끼익 · 툭\" (레버가 0.2초에 끝까지)")]
        public SoundCue FireworkLever = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Pitch = new Vector2(0.98f, 1.02f), Cooldown = 0.5f, MaxVoices = 1 };

        [Header("체험 — 그리드 허브 번개 정원 (5-2, 2026-11-23)")]
        [Tooltip("번개가 나무에 닿음 \"찌직 · 퐁\" (그 나무 3D, 음계 칸을 골라 도 · 레 · 파 · 솔 · 라 — 기준 = 도6) — VARCO 정전기를 먹먹하게 + 합성 \"퐁\"")]
        public SoundCue LightningZap = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = Vector2.one, Cooldown = 0.45f, MaxVoices = 3 };
        [Tooltip("테슬라 탑 에너지 구 웅 (탑 꼭대기에 붙은 3D 루프, 가동 중) — 실제 녹음: 테슬라 코일 \"부르르\" (F 배음) + 전기 아크 지직 아주 옅게 (2026-11-25 사인파 · 음악으로 들리던 합성 웅을 바꿈)")]
        public SoundCue LightningHumLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };

        [Header("체험 — 치안 본부 떠 있는 순찰차 (5-2, 2026-11-23)")]
        [Tooltip("떠 있는 웅 (차에 붙은 3D 루프, 추진 세기 · 멀어지는 만큼 크기 · 높이) — VARCO 부드러운 호버 웅 (F3으로 맞춤)")]
        public SoundCue PatrolHoverLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("경광등이 켜질 때 아주 작은 \"삐용\" (차 자리 3D — 출동 · 돌아올 때) — 합성 레6 → 라5")]
        public SoundCue PatrolSiren = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.3f, Pitch = Vector2.one, Cooldown = 0.5f, MaxVoices = 2 };

        [Header("체험 — 시민 회관 스텔라돔 상영 (5-2, 2026-11-23)")]
        [Tooltip("불이 꺼지며 상영 시작 (돔 안 2D, UiOpen 대신) — 합성 화음 · 바람이 먹먹해지며 내려가는 \"슈우웅\"")]
        public SoundCue DomeDim = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.86f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("새벽에 객석 불이 다시 오름 (돔 안 2D) — 불 꺼짐을 거꾸로 (밝아지며 차오름) + 옅은 반짝")]
        public SoundCue DomeBrighten = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("상영 동안 별 하늘 울림 (돔 안 2D 루프, 별 · 은하수 밝기만큼) — 심우주 통신기지 은하수 깊은 울림 (F 장조 으뜸화음)을 12 dB 크게 저장 (그대로는 음악보다 −19.5 dB로 안 들림)")]
        public SoundCue DomeShowLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };

        [Header("체험 — 정련 콤비나트 작은 해 용광로 · 집게 크레인 (5-2, 2026-11-23)")]
        [Tooltip("작은 해 용광로 (해 가운데에 붙은 3D 루프, 가동 중) — VARCO 따뜻한 불 \"웅\" (5개 중 가장 고르고 잔 탁탁 없음)")]
        public SoundCue RefineryFurnaceLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.75f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("국자가 주형에 쇳물을 부음 (그 주형 자리 3D) — 실제 녹음: 용암 꿀렁 + 불 탁탁 + 식으며 지글 \"치익\" (2026-11-25, 물 · 변기 물 내림으로 들리던 VARCO를 바꿈)")]
        public SoundCue RefineryLadlePour = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.71f, Pitch = new Vector2(0.95f, 1.05f), Cooldown = 0.2f, MaxVoices = 3 };
        [Tooltip("크레인 들보 · 집게가 움직이는 동안 (들보에 붙은 3D 루프) — 실제 녹음: 책상 리프트 기어 모터 (웅 Bb1 · 위잉 F5, 2026-11-25 신시사이저로 들리던 VARCO 모터를 바꿈)")]
        public SoundCue RefineryCraneMoveLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("집게가 광석을 쥠 \"턱\" (집게 자리 3D) — 5-1 걸쇠 원본을 낮춰 묵직하게")]
        public SoundCue RefineryCraneGrab = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Pitch = new Vector2(0.95f, 1.05f), Cooldown = 0.2f, MaxVoices = 2 };

        [Header("체험 — 중력 거주구 전망실 망원경 (5-2, 2026-11-23)")]
        [Tooltip("망원경에 눈을 댐 (2D, UiOpen 대신) — VARCO 매끈한 렌즈 \"위잉\"이 깨어나며 높아짐 + 옅은 \"스르륵\"")]
        public SoundCue T1TelescopeIn = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.28f, Pitch = Vector2.one, Cooldown = 0.3f, MaxVoices = 1 };
        [Tooltip("망원경에서 눈을 뗌 (2D, UiClose 대신) — 같은 렌즈 \"위잉\"이 낮아지며 잦아듦")]
        public SoundCue T1TelescopeOut = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.35f, Pitch = Vector2.one, Cooldown = 0.3f, MaxVoices = 1 };
        [Tooltip("휠로 배율을 한 칸 바꿈 (2D) — 같은 렌즈의 짧은 \"윙\", 확대 = 그대로 (미 · 라 · 도) · 축소 = 0.891 높이 (레 · 솔 · 시♭ — 둘 다 조 안)")]
        public SoundCue T1TelescopeZoom = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.28f, Pitch = Vector2.one, Cooldown = 0.06f, MaxVoices = 2 };

        [Header("체험 — 보안청 천장 포탑 받침 (5-2, 2026-11-23)")]
        [Tooltip("포탑이 도는 동안 서보 \"위잉\" (포탑에 붙은 3D 루프, 도는 빠르기만큼 크기 · 높이 — 경보 때 빠르게 = F 배음 · 평소 경계 = D 배음) — 실제 녹음: 차 전동 창문 모터 (기음 F2, 2026-11-25 사인파로 들리던 합성 서보를 바꿈)")]
        public SoundCue T1TurretServoLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };

        [Header("체험 — 정비 공창 수리 패드 용접 · 조선소 용접 갠트리 (5-2, 2026-11-23)")]
        [Tooltip("용접하는 동안 \"지글 · 탁탁\" (그 토치 끝에 붙은 3D 루프 — 불꽃이 튀는 토치만) — 실제 녹음: 스팀이 바닥에 닿는 지글 + 전기 아크 탁탁, 4 kHz 위 눌러 부드럽게 (2026-11-25, 폭죽 · 천둥으로 들리던 합성을 바꿈)")]
        public SoundCue T1WeldLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.76f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("조선소 갠트리가 레일을 따라 옮겨 감 · 토치를 대고 뗌 (갠트리에 붙은 3D 루프) — 정련 콤비나트 크레인 모터 (refinery_crane_move_loop) 그대로")]
        public SoundCue ShipyardGantryLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };

        [Header("음악 (상태별, 여러 곡이면 차례로)")]
        public SoundCue MusicDay = new SoundCue { Bus = AudioBus.Music, Volume = 0.5f, Pitch = Vector2.one };
        public SoundCue MusicNight = new SoundCue { Bus = AudioBus.Music, Volume = 0.5f, Pitch = Vector2.one };
        public SoundCue MusicCrisis = new SoundCue { Bus = AudioBus.Music, Volume = 0.5f, Pitch = Vector2.one };
        public SoundCue MusicMenu = new SoundCue { Bus = AudioBus.Music, Volume = 0.5f, Pitch = Vector2.one };

        [Header("내부 음악 (사운드 개편 2026-11-23, SOUND_LIST_NEW 7번 · \"내부 음악 이어 붙이기\")")]
        [Tooltip("모듈 단계별 4곡 = 기본 · 1 · 2 · 3단계 이상. 차분한 패드 곡(바깥 음악처럼), 겹쳐도 안 부딪히는 음 · 같은 길이(96초)라 함께 돌리고 음량만 바꿈 — 방을 옮겨도 이어짐. 곡이 없는 단계는 아래 단계 곡으로")]
        public SoundCue MusicInterior0 = new SoundCue { Bus = AudioBus.Music, Volume = 0.6f, Pitch = Vector2.one };
        public SoundCue MusicInterior1 = new SoundCue { Bus = AudioBus.Music, Volume = 0.6f, Pitch = Vector2.one };
        public SoundCue MusicInterior2 = new SoundCue { Bus = AudioBus.Music, Volume = 0.6f, Pitch = Vector2.one };
        public SoundCue MusicInterior3 = new SoundCue { Bus = AudioBus.Music, Volume = 0.6f, Pitch = Vector2.one };

        [Header("모듈 소리 풍경 (사운드 개편 D-7 · D-7b, 2026-11-23)")]
        [Tooltip("모듈마다 하나 (SS_*.asset) — 있는 방은 성격별 환경음 묶음 대신 이것이 울림 (지금 있는 방만)")]
        public List<RoomSoundscape> Soundscapes = new List<RoomSoundscape>();

        public RoomSoundscape SoundscapeFor(Data.ModuleData module)
        {
            if (module == null)
                return null;
            foreach (var s in Soundscapes)
                if (s != null && s.Module == module)
                    return s;
            return null;
        }

        [Header("방 환경음 1단계 · 가끔 소리 · 상태 층 (사운드 개편 2026-11-25, SOUND_LIST_NEW 4-4 · D-5)")]
        [Tooltip("1단계 판 방 환경음 6 (루프): 1단계 이상 방 (소리 풍경이 없는 2단계 방도) — 같은 성격을 더 매끈 · 조용하게, 실제 녹음 (CC0): 욕실 환풍기 / 실내 분수 / PC 팬 / 열풍 팬 / 냉장고 압축기 + 안정기 웅 / 밤 공기, 게임 안 크기는 기본 판보다 1.5 dB 작게")]
        public SoundCue Room1Life = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.49f, Pitch = Vector2.one };
        public SoundCue Room1Water = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.65f, Pitch = Vector2.one };
        public SoundCue Room1Machine = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.37f, Pitch = Vector2.one };
        public SoundCue Room1Heat = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.58f, Pitch = Vector2.one };
        public SoundCue Room1Electric = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.24f, Pitch = Vector2.one };
        public SoundCue Room1Hall = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.7f, Pitch = Vector2.one };
        [Tooltip("가끔 소리 꾸러미 12 (SS_Spot_*.asset, 번호 = 성격 순서 x 2 + 단계 (0 기본 · 1 = 1단계 이상)): 지금 있는 방에서만, 무리마다 25 ~ 70초에 하나씩 방 아무 칸에서 3D (꾸러미 전체로 8 ~ 30초에 하나 꼴) — 소리 풍경이 있는 방은 그쪽")]
        public List<RoomSoundscape> RoomSpots = new List<RoomSoundscape>();
        [Tooltip("상태 층 · 파손: 전선 지직 (2D 루프)")]
        public SoundCue StateSparkLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.65f, Pitch = Vector2.one };
        [Tooltip("상태 층 · 파손: 선체 삐걱 넷 (방 아무 칸 3D, 6 ~ 14초마다)")]
        public SoundCue StateCreak = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.18f, Pitch = new Vector2(0.94f, 1.06f), Cooldown = 2f, MaxVoices = 1 };
        [Tooltip("상태 층 · 전력 부족 / 비활성: 웅이 낮아지며 꺼짐 (2D 한 번 — 그 상태인 방에 들어설 때 · 30 ~ 50초마다), 방 환경음은 효율만큼 작아짐")]
        public SoundCue StatePowerDown = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.3f, Pitch = Vector2.one, Cooldown = 5f, MaxVoices = 1 };
        [Tooltip("상태 층 · 산소 바닥: 환기 팬 멈춤 (2D 한 번 — 그 상태인 방에 들어설 때), 방 환경음 0.6배")]
        public SoundCue StateFanStop = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.3f, Pitch = Vector2.one, Cooldown = 5f, MaxVoices = 1 };
        [Tooltip("상태 층 · 과열 (대륙급 열): 끓는 거품 (2D 루프)")]
        public SoundCue StateBoilLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.8f, Pitch = Vector2.one };

        [Header("방 장치 소리 · 바깥 일이 안에서 (사운드 개편 2026-11-26, SOUND_LIST_NEW 4-4 obj_* · D-8 · D-10 — 지금 있는 방의 부품 자리 3D)")]
        [Tooltip("컨베이어 벨트 \"스르르 달각\" (단백질 배양실 · 식량 공장 · 자재 재생 벨트 · 조선소 롤러, 그 자리 3D 루프 · 라인이 돌고 이 방에 있을 때): 실제 녹음 러닝머신 벨트 + 에스컬레이터 발판")]
        public SoundCue ObjConveyorLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.68f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("라인 끝 \"덜컹\" (상자 뚜껑이 닫힘 · 식량 공장 라인 끝 가끔 · 물류 스태커가 상자를 칸에 넣음, 그 자리 3D): 실제 녹음 수납장 문 \"툭\" 하나 + 음높이 흔들기")]
        public SoundCue ObjConveyorClunk = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.13f, Pitch = new Vector2(0.95f, 1.05f), Cooldown = 0.4f, MaxVoices = 2 };
        [Tooltip("서버 랙 팬 (데이터 센터 표시등 묶음 자리 3D 루프, 모듈이 켜져 있을 때 · 연구 중이면 크게): 실제 녹음 컴퓨터 본체 팬 (조 밖 줄 깎음)")]
        public SoundCue ObjServerFanLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.26f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("화면 · 홀로그램 작은 전자 웅 (상태 화면 · 홀로그램 · 플라즈마 · 학습 코어 고리 자리 3D 루프, 4 m 안에서만): 형광등 안정기 웅을 Bb2로 (F 장조 안)")]
        public SoundCue ObjScreenHumLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.24f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("물 추출기 얼음 \"딸그락\" (낙하관 아래 드럼 자리 3D, 얼음 조각이 드럼에 닿을 때 몇 초에 한 번): 실제 녹음 금속 국자로 얼음 퍼기 (CC0), 변형 3")]
        public SoundCue ObjIceClatter = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.15f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.8f, MaxVoices = 1 };
        [Tooltip("식물 곁을 지나감 \"스윽\" (화분 식물 · 나무 자리 3D, 걷다가 0.85 m 안을 스칠 때): 실제 녹음 덤불 흔들림, 변형 3")]
        public SoundCue ObjPlantRustle = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.09f, Pitch = new Vector2(0.95f, 1.05f), Cooldown = 0.25f, MaxVoices = 2 };
        [Tooltip("번호 부름 · 도착 안내 \"띵동\" (행정 · 보안청 민원 창구 번호가 바뀜 · 여객 포트 이민선 도착 안내판, 그 판 자리 3D): 1단계 생활 방 가끔 소리 \"띵동\" (파5 → 도6) 그대로")]
        public SoundCue ObjQueueChime = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.13f, Pitch = Vector2.one, Cooldown = 0.6f, MaxVoices = 1 };
        [Tooltip("D-10 운석이 모듈을 맞힘 (1인칭 내부, 맞은 모듈 칸 자리 3D 8 m까지 그대로 → 60 m에서 0): 선체 너머 먹먹한 \"쿵\" (실외기 \"퉁\" · 둔탁한 금속을 낮춰 700 Hz 아래) + 안의 물건 \"달그락\", 변형 4")]
        public SoundCue HullThud = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.12f, MaxVoices = 3 };
        [Tooltip("D-10 가까운 큰 충돌 · 유성우 몰아침 (1인칭 내부, 4초에 한 번까지): 먼 천둥 1.1 kHz 아래 = 선체 낮은 울림")]
        public SoundCue HullRumble = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.25f, Pitch = Vector2.one, Cooldown = 3f, MaxVoices = 1 };
        [Tooltip("D-10 태양 폭풍, 방 불빛이 번쩍이는 순간 조명 \"지직\" (2D, 몇 초에 한 번까지): 아크 지직이 대여섯 번 끊김 + 안정기 웅 옅게, 변형 2")]
        public SoundCue LightFlicker = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 1.5f, MaxVoices = 1 };
        [Tooltip("D-10 태양 폭풍 동안 가끔 방 스피커 무전 잡음 (2D, 14 ~ 30초마다): 실제 녹음 AM 라디오 튜닝 잡음 (말소리 없는 구간, CC0) 300 Hz ~ 3.5 kHz")]
        public SoundCue RadioStatic = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.28f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 5f, MaxVoices = 1 };
        [Tooltip("D-10 화물선 · 보급선 · 이민선이 다른 방 쪽에 붙음 · 합성 완료 (1인칭 내부, 그 모듈 칸 자리 3D): 트럭 문 \"쿵\" + 자물쇠 \"철컥\"을 낮춰 1.2 kHz 아래 = 선체로 전해지는 \"철컹\"")]
        public SoundCue DockClank = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.35f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 1f, MaxVoices = 2 };

        /// <summary>2026-11-25 ⑥ 성격별 1단계 판 바탕.</summary>
        public SoundCue Room1For(Data.InteriorAmbience ambience)
        {
            switch (ambience)
            {
                case Data.InteriorAmbience.Life: return Room1Life;
                case Data.InteriorAmbience.Water: return Room1Water;
                case Data.InteriorAmbience.Machine: return Room1Machine;
                case Data.InteriorAmbience.Heat: return Room1Heat;
                case Data.InteriorAmbience.Electric: return Room1Electric;
                default: return Room1Hall;
            }
        }

        /// <summary>2026-11-25 ⑥ 성격 · 단계 (0 기본, 1 이상 = 1단계 판) 가끔 소리 꾸러미 — 없으면 null.</summary>
        public RoomSoundscape SpotsFor(Data.InteriorAmbience ambience, int stage)
        {
            int i = (int)ambience * 2 + (stage >= 1 ? 1 : 0);
            return RoomSpots != null && i < RoomSpots.Count ? RoomSpots[i] : null;
        }
    }
}
