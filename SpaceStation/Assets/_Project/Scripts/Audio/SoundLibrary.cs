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
        public SoundCue UiClick = new SoundCue { Bus = AudioBus.Ui, Volume = 0.45f };
        public SoundCue UiHover = new SoundCue { Bus = AudioBus.Ui, Volume = 0.18f, Cooldown = 0.04f, MaxVoices = 2 };
        public SoundCue UiTab = new SoundCue { Bus = AudioBus.Ui, Volume = 0.45f };
        public SoundCue UiOpen = new SoundCue { Bus = AudioBus.Ui, Volume = 0.3f };
        public SoundCue UiClose = new SoundCue { Bus = AudioBus.Ui, Volume = 0.3f };
        public SoundCue UiError = new SoundCue { Bus = AudioBus.Ui, Volume = 0.5f, Cooldown = 0.15f, MaxVoices = 1 };
        public SoundCue UiSpeedUp = new SoundCue { Bus = AudioBus.Ui, Volume = 0.4f };
        public SoundCue UiPause = new SoundCue { Bus = AudioBus.Ui, Volume = 0.4f };
        public SoundCue UiResume = new SoundCue { Bus = AudioBus.Ui, Volume = 0.4f };

        [Header("건설·수리")]
        public SoundCue BuildSelect = new SoundCue { Bus = AudioBus.Ui, Volume = 0.45f };
        public SoundCue BuildRotate = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = new Vector2(0.95f, 1.08f) };
        public SoundCue BuildPlace = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.75f };
        public SoundCue BuildRemove = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f };
        public SoundCue BuildConnect = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.35f };
        public SoundCue RepairStart = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Cooldown = 0.2f };
        public SoundCue RepairDone = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Cooldown = 0.2f };
        public SoundCue Maintain = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f };

        [Header("이벤트·경보")]
        public SoundCue EventNegative = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        public SoundCue EventPositive = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        public SoundCue EventEnd = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Cooldown = 0.5f, MaxVoices = 1, Pitch = Vector2.one };
        public SoundCue EarlyWarning = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        public SoundCue MeteorIncoming = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Cooldown = 0.25f, MaxVoices = 2, Pitch = new Vector2(0.92f, 1.08f) };
        public SoundCue MeteorImpact = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Cooldown = 0.08f, MaxVoices = 3, Pitch = new Vector2(0.9f, 1.08f) };
        public SoundCue MeteorExplode = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Cooldown = 0.06f, MaxVoices = 3, Pitch = new Vector2(0.9f, 1.12f) };
        public SoundCue TurretLaser = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Cooldown = 0.05f, MaxVoices = 3, Pitch = new Vector2(0.94f, 1.08f) };
        public SoundCue ShieldDeflect = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Cooldown = 0.08f, MaxVoices = 2, Pitch = new Vector2(0.95f, 1.06f) };
        public SoundCue ModuleDamaged = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Cooldown = 0.2f, MaxVoices = 2 };
        public SoundCue ModuleDestroyed = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.75f, Cooldown = 0.3f, MaxVoices = 2 };
        public SoundCue StormSpark = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.22f, Cooldown = 0.12f, MaxVoices = 2, Pitch = new Vector2(0.85f, 1.2f) };
        public SoundCue OxygenLeak = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Cooldown = 1f, MaxVoices = 1 };
        public SoundCue SupplyArrive = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.45f, Cooldown = 1f, MaxVoices = 2, Pitch = Vector2.one };
        public SoundCue ResourceDepleted = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.5f, Cooldown = 3f, MaxVoices = 1, Pitch = Vector2.one };
        public SoundCue GradeUp = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        public SoundCue GradeDown = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.55f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        public SoundCue Victory = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };
        public SoundCue GameOver = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Cooldown = 1f, MaxVoices = 1, Pitch = Vector2.one };

        [Header("반복 (루프)")]
        public SoundCue AlarmLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.22f, Pitch = Vector2.one };
        public SoundCue StormLoop = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.35f, Pitch = Vector2.one };
        public SoundCue StationHum = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.22f, Pitch = Vector2.one };

        [Header("내부 방문 (11-9)")]
        public SoundCue Footstep = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.42f, Pitch = new Vector2(0.93f, 1.07f), Cooldown = 0.12f, MaxVoices = 2 };
        [Tooltip("방 환경음 6묶음 (루프): 생활 / 물·식물 / 기계 / 고열 / 전기·방어 / 넓은 공간 — 방 템플릿의 Ambience로 고름")]
        public SoundCue RoomLife = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.45f, Pitch = Vector2.one };
        public SoundCue RoomWater = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.45f, Pitch = Vector2.one };
        public SoundCue RoomMachine = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.45f, Pitch = Vector2.one };
        public SoundCue RoomHeat = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.5f, Pitch = Vector2.one };
        public SoundCue RoomElectric = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.45f, Pitch = Vector2.one };
        public SoundCue RoomHall = new SoundCue { Bus = AudioBus.Ambient, Volume = 0.45f, Pitch = Vector2.one };
        [Tooltip("11-10 파손된 방 안에 있을 때 울리는 경보 (루프)")]
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
    }
}
