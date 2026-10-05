using System;
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

        [Header("음악 (상태별, 여러 곡이면 차례로)")]
        public SoundCue MusicDay = new SoundCue { Bus = AudioBus.Music, Volume = 0.5f, Pitch = Vector2.one };
        public SoundCue MusicNight = new SoundCue { Bus = AudioBus.Music, Volume = 0.5f, Pitch = Vector2.one };
        public SoundCue MusicCrisis = new SoundCue { Bus = AudioBus.Music, Volume = 0.5f, Pitch = Vector2.one };
        public SoundCue MusicMenu = new SoundCue { Bus = AudioBus.Music, Volume = 0.5f, Pitch = Vector2.one };
    }
}
