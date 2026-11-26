using System;
using System.Collections.Generic;

namespace SpaceStation.Audio
{
    /// <summary>주민 목소리 기분 (사운드 개편 4-3 voice_{종족}_{기분}).</summary>
    public enum ResidentVoiceMood
    {
        Happy,
        Upset,
        Ask,
        Surprise,
    }

    /// <summary>주민 활동 소리 갈래 (D-11 act_*). 걷기 · 앉기는 동작이 바뀌는 순간이라 따로 (발 · 방석).</summary>
    public enum ResidentActivity
    {
        None,
        Eat,
        Work,
        WorkTool,
        Sleep,
        Play,
    }

    /// <summary>
    /// 사운드 개편 4-3 · 4-4 주민 소리 고르는 규칙 (2026-11-26, SOUND_LIST_NEW voice_* · crowd_* · act_* · D-2 · D-5 ④ · D-11) — 순수 계산, EditMode 시험 대상.
    /// - 종족 8 (AnimalModelSet Id) → 목소리 칸 번호, 플레이어를 만난 반응 · 부탁 · 놀람 → 기분.
    /// - 반복 막기 (D-2 "주민 목소리 = 주민마다 20초 · 방 전체 동시 2 · 멀면 작게"): <see cref="ResidentVoiceGate"/>.
    /// - 사람 층 (D-5 ④ 웅성)은 2026-11-26 정한 것 S-29 "웅성 없이"로 뺌 (주민 수 → 웅성 단계 · 세 루프 교차 규칙 · crowd_1 ~ 3 파일 모두).
    /// - 활동 (D-11): 일하는 자리 · 앉은 자리 · 집 · 밤 · 방 종류 → 그릇 · 키보드 · 공구 · 숨 · 놀이 도구.
    /// </summary>
    public static class ResidentSound
    {
        /// <summary>AnimalModelSet 종족 Id 순서 = SoundLibrary 목소리 칸 순서.</summary>
        public static readonly string[] Species = { "rabbit", "cat", "dog", "bear", "fox", "panda", "koala", "penguin" };

        public static int SpeciesIndex(string id)
        {
            if (string.IsNullOrEmpty(id))
                return -1;
            return Array.IndexOf(Species, id);
        }

        /// <summary>잠 숨 크기 (0 = 큰 몸 곰 · 판다 · 코알라, 1 = 강아지 · 여우 · 펭귄, 2 = 고양이 · 토끼) — act_sleep_01 ~ 03 (몸이 작을수록 높고 가벼운 숨).</summary>
        public static int SleepSize(string id)
        {
            switch (id)
            {
                case "bear":
                case "panda":
                case "koala":
                    return 0;
                case "cat":
                case "rabbit":
                    return 2;
                default:
                    return 1;
            }
        }

        // ---------------- 기분 ----------------

        /// <summary>
        /// 플레이어가 다가와 주민이 반응할 때 목소리 기분 (null = 소리 없음): 부탁이 걸린 주민 = ask, 폴짝 · 손 흔들기 = happy, 팔짱 끼고 돌아섬 = upset, 바라보기만 = 없음.
        /// </summary>
        public static ResidentVoiceMood? MoodForReaction(Interior.ResidentReaction reaction, bool pendingRequest)
        {
            if (reaction == Interior.ResidentReaction.Look)
                return null;
            if (pendingRequest)
                return ResidentVoiceMood.Ask;
            switch (reaction)
            {
                case Interior.ResidentReaction.HopWave:
                case Interior.ResidentReaction.Wave:
                    return ResidentVoiceMood.Happy;
                case Interior.ResidentReaction.TurnAway:
                    return ResidentVoiceMood.Upset;
                default:
                    return null;
            }
        }

        // ---------------- 활동 (D-11) ----------------

        /// <summary>식탁 · 매대가 있는 방 (그릇 "달그락") — 상업 구역 · 집 (거주 모듈 · 중력 거주구 · 거주 타워 · 아콜로지) · 복지 센터.</summary>
        private static readonly HashSet<string> EatRooms = new HashSet<string>
        {
            "MD_Commerce", "MD_Habitat", "MD_Evo_GravityHabitat", "MD_Evo_HabitatTower", "MD_Evo_Arcology", "MD_Evo_WelfareCenter",
        };

        /// <summary>오락실 (놀이 도구 + 가끔 웃음) — 휴게실 (오락실 소파) · 문화 센터.</summary>
        private static readonly HashSet<string> PlayRooms = new HashSet<string> { "MD_Recreation", "MD_Evo_CultureCenter" };

        public static bool IsPlayRoom(string module) => module != null && PlayRooms.Contains(module);

        /// <summary>
        /// 주민 하나의 활동 소리 갈래. 밤에 집에 앉은 (누운) 주민 = 잠 숨 · 일하는 자리 = 키보드 (기계 · 고열 · 물 방 = 공구) ·
        /// 오락실 = 놀이 도구 · 식탁 있는 방에 앉음 (낮) = 그릇 · 나머지 = 없음 (걷기 · 앉기는 동작 순간에 따로).
        /// </summary>
        public static ResidentActivity ActivityOf(string module, Data.InteriorAmbience ambience, bool working, bool seated, bool atHome, bool night)
        {
            if (night && atHome)
                return seated ? ResidentActivity.Sleep : ResidentActivity.None;
            if (working)
                return ambience == Data.InteriorAmbience.Machine || ambience == Data.InteriorAmbience.Heat || ambience == Data.InteriorAmbience.Water
                    ? ResidentActivity.WorkTool : ResidentActivity.Work;
            if (IsPlayRoom(module))
                return ResidentActivity.Play;
            if (seated && !night && module != null && EatRooms.Contains(module))
                return ResidentActivity.Eat;
            return ResidentActivity.None;
        }

        /// <summary>활동 소리 사이 간격 (실시간 초, 최소 · 최대) — 잠 숨은 이어서 (숨 한 번 길이 + 0.8 ~ 2초).</summary>
        public static (float Min, float Max) GapOf(ResidentActivity a)
        {
            switch (a)
            {
                case ResidentActivity.Eat: return (6f, 14f);
                case ResidentActivity.Work: return (3.5f, 8f);
                case ResidentActivity.WorkTool: return (5f, 11f);
                case ResidentActivity.Sleep: return (0.8f, 2f);
                case ResidentActivity.Play: return (12f, 30f);   // 주민 여섯인 휴게실에서 8 ~ 20초는 3초마다 났음 (2026-11-26 게임 안 시험)
                default: return (10f, 20f);
            }
        }

        /// <summary>오락실 · 2단계 이상 여가 방 (테마파크)의 쉬는 주민이 가끔 웃음 (happy 목소리) 사이 간격 — 목소리 상한 (<see cref="ResidentVoiceGate"/>)은 그대로.</summary>
        public const float CheerGapMin = 30f, CheerGapMax = 70f;

        /// <summary>웃음은 방 전체로도 8 ~ 15초에 하나 (주민 여섯인 휴게실에서 15초에 다섯 번 났음 — 2026-11-26 게임 안 시험).</summary>
        public const float CheerRoomGapMin = 8f, CheerRoomGapMax = 15f;

        /// <summary>이어지는 활동 소리 (그릇 · 키보드 · 공구 · 숨 · 놀이)는 방 전체 시작끼리 이 초 넘게 (걷는 발 · 앉고 일어섬은 그 순간이라 따로).</summary>
        public const float ActivityRoomGap = 2f;

        /// <summary>활동 소리가 나는 거리 · 한 방 동시 수 (D-2 "멀면 작게" · 몰리지 않게): 걷는 발은 가까운 둘만, 나머지 활동은 둘까지.</summary>
        public const float ActivityRange = 7f;
        /// <summary>걷는 발은 더 가까이만 (5 m) — 첫 시험에서 4분에 287번 (가까운 둘이 내내 톡톡).</summary>
        public const float WalkRange = 5f;
        public const int MaxWalkers = 2;
        public const int MaxActivity = 2;
    }

    /// <summary>
    /// D-2 주민 목소리 반복 막기 (순수 상태): 같은 주민은 20초에 한 번 · 방 전체 동시 2 · 시작끼리 0.45초 넘게 (같은 순간 둘이 겹쳐 흐려지지 않게).
    /// 부탁 · 완료 (플레이어가 한 일의 대답)는 주민 간격을 건너뛰되 동시 수 · 시작 간격은 지킴.
    /// </summary>
    public sealed class ResidentVoiceGate
    {
        public const float PerResident = 20f;
        public const int MaxRoom = 2;
        public const float Spacing = 0.45f;

        private readonly Dictionary<int, float> _last = new Dictionary<int, float>();
        private readonly List<float> _ends = new List<float>();
        private float _lastStart = -999f;

        /// <param name="at">울릴 시각 (실시간 초, 지연 포함)</param>
        /// <param name="length">소리 길이 (초) — 이 동안 방 동시 수에 셈</param>
        /// <param name="reply">부탁 · 완료 대답 (주민 간격 건너뜀)</param>
        public bool TryAdmit(int resident, float at, float length, bool reply = false)
        {
            if (!reply && _last.TryGetValue(resident, out float last) && at - last < PerResident && at >= last)
                return false;
            if (Math.Abs(at - _lastStart) < Spacing)
                return false;
            _ends.RemoveAll(e => e <= at);
            if (_ends.Count >= MaxRoom)
                return false;
            _last[resident] = at;
            _lastStart = at;
            _ends.Add(at + Math.Max(0.05f, length));
            return true;
        }

        /// <summary>방이 바뀜 · 내부를 다시 만듦 — 울리는 중인 수만 비움 (주민 간격은 사람 기준이라 남김).</summary>
        public void ClearRoom()
        {
            _ends.Clear();
            _lastStart = -999f;
        }
    }
}

namespace SpaceStation.Audio
{
    /// <summary>한 종족의 목소리 칸 넷 (기분마다, 변형 2 — voice_{종족}_{기분}_01 · 02). 음 있는 울음이라 음높이 흔들기 끔 (F 장조 안).</summary>
    [System.Serializable]
    public sealed class ResidentVoiceSet
    {
        public SoundCue Happy = Voice();
        public SoundCue Upset = Voice();
        public SoundCue Ask = Voice();
        public SoundCue Surprise = Voice();

        private static SoundCue Voice() => new SoundCue { Bus = AudioBus.Sfx, Volume = 0.2f, Pitch = UnityEngine.Vector2.one, Cooldown = 0.05f, MaxVoices = 2 };

        public SoundCue For(ResidentVoiceMood mood)
        {
            switch (mood)
            {
                case ResidentVoiceMood.Happy: return Happy;
                case ResidentVoiceMood.Upset: return Upset;
                case ResidentVoiceMood.Ask: return Ask;
                default: return Surprise;
            }
        }
    }
}
