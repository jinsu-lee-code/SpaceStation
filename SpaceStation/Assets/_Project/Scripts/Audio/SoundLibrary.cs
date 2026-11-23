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

        [Header("체험 — 궤도 방어망 오로라 보호막 · 등대 빛 돌리기 · 별똥별 막기 (5-2, 2026-11-23)")]
        [Tooltip("방파제 끝 레버 당김 (레버 자리 3D) — 나무 \"끼익\" (궤도 항만 삐걱임 원본) + VARCO 나무 \"툭\" (레버가 끝에 닿는 0.5초)")]
        public SoundCue OdefLeverPull = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Pitch = new Vector2(0.98f, 1.02f), Cooldown = 0.5f, MaxVoices = 1 };
        [Tooltip("오로라 보호막이 펼쳐짐 (레버 0.6초 뒤, 2D 큰 순간) — 밤 오로라 울림 조각이 차오름 + 도 오음계 반짝임이 올라감 (4초)")]
        public SoundCue OdefShieldUnfurl = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.6f, Pitch = Vector2.one, Cooldown = 1f, MaxVoices = 1 };
        [Tooltip("별똥별이 보호막에 맞음 (2D) — 합성 둥근 유리 \"퐁\" + 반짝, 맞을 때마다 도 · 레 · 파 · 솔 · 라 되풀이 (기준 = 도5)")]
        public SoundCue OdefStarHitShield = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.65f, Pitch = Vector2.one, Cooldown = 0.05f, MaxVoices = 3 };
        [Tooltip("등대 금빛 손잡이를 잡은 동안 (2D 루프, 돌리는 빠르기만큼 크기) — VARCO 나무 굴대 \"웅\" + 한 바퀴마다 낮은 나무 \"끄응\" (딸깍 X)")]
        public SoundCue OdefCrankLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("빛이 닿아 새 곳이 답함 (2D, 찾은 차례대로 음계) · 이미 찾은 곳 = 그 곳의 음을 작게 — 합성 빛 유리 종 (기준 = 도5)")]
        public SoundCue OdefBeamReact = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = Vector2.one, Cooldown = 0.05f, MaxVoices = 3 };
        [Tooltip("별똥별 막기 포 쏨 (클릭마다 2D, 0.22초마다 연타) — 합성 낮은 공기 \"퐁\" + 짧은 \"쉿\" (0.25초, 가볍게)")]
        public SoundCue OdefCannonFire = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.8f, Pitch = new Vector2(0.96f, 1.04f), Cooldown = 0.05f, MaxVoices = 2 };
        [Tooltip("맞혀서 별똥별이 터짐 (2D, 공용 점수 \"띵\" 대신 — 연달아 맞히면 음계로) — 합성 \"팟\" + 반짝임 흩어짐 (기준 = 도5)")]
        public SoundCue OdefStarBurst = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.4f, Pitch = Vector2.one, Cooldown = 0.05f, MaxVoices = 3 };

        [Header("체험 — 수리 사령부 구조 바구니 · 경종 등불 쇼 · 드론 순서 기억 (5-2, 2026-11-23)")]
        [Tooltip("탄 바구니가 오르내리는 동안 (2D 루프, 줄을 당길 때 · 내려갈 때) — VARCO 줄이 나무 도르래를 지나는 결")]
        public SoundCue RcmdRopeLoop = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = Vector2.one, Cooldown = 0f, MaxVoices = 1 };
        [Tooltip("바구니가 정류장 · 나무집 마루에 닿음 (바구니 자리 3D) — 궤도 항만 삐걱임 원본 + 궤도 방어망 레버 \"툭\"을 가볍게")]
        public SoundCue RcmdBasketCreak = new SoundCue { Bus = AudioBus.Sfx, Volume = 0.7f, Pitch = new Vector2(0.97f, 1.03f), Cooldown = 0.5f, MaxVoices = 1 };
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
        [Tooltip("손바퀴를 돌린 접시가 돌아섬 (접시 자리 3D, 2.6초 — RepairStart 대신) — VARCO 부드러운 모터 \"위잉\"을 접시 빠르기대로 느리게 → 빠르게 → 느리게")]
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
