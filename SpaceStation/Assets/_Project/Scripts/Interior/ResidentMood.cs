using System;

namespace SpaceStation.Interior
{
    /// <summary>11-16 플레이어가 다가왔을 때 동물 주민의 반응.</summary>
    public enum ResidentReaction
    {
        /// <summary>바라보기만.</summary>
        Look,
        /// <summary>손 흔들기.</summary>
        Wave,
        /// <summary>폴짝 뛰며 손 흔들기 (기분 좋음).</summary>
        HopWave,
        /// <summary>팔짱 끼고 고개 돌림 (기분 나쁨).</summary>
        TurnAway,
    }

    /// <summary>11-16 기분에 따른 평소 동작 세기 (기분 나쁨 → 좋음으로 섞음).</summary>
    public readonly struct MoodStyle
    {
        /// <summary>동작 빠르기 배율 (나쁨일수록 느림).</summary>
        public readonly float Speed;
        /// <summary>고개 숙임 (도, + = 아래).</summary>
        public readonly float HeadDown;
        /// <summary>귀 처짐 (도, + = 바깥 · 아래로 처짐, − = 쫑긋).</summary>
        public readonly float EarDroop;
        /// <summary>꼬리 흔들기 폭 (도).</summary>
        public readonly float TailWag;
        /// <summary>꼬리 흔들기 빠르기 (Hz).</summary>
        public readonly float TailHz;
        /// <summary>꼬리 처짐 (도, + = 아래).</summary>
        public readonly float TailDroop;
        /// <summary>제자리 들썩임 높이 (모델 원래 크기 기준 m).</summary>
        public readonly float Bounce;
        /// <summary>기지개 · 두리번 같은 작은 동작 사이 간격 (초).</summary>
        public readonly float IdleGap;

        public MoodStyle(float speed, float headDown, float earDroop, float tailWag, float tailHz, float tailDroop, float bounce, float idleGap)
        {
            Speed = speed;
            HeadDown = headDown;
            EarDroop = earDroop;
            TailWag = tailWag;
            TailHz = tailHz;
            TailDroop = tailDroop;
            Bounce = bounce;
            IdleGap = idleGap;
        }

        public static MoodStyle Lerp(MoodStyle a, MoodStyle b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            float L(float x, float y) => x + (y - x) * t;
            return new MoodStyle(L(a.Speed, b.Speed), L(a.HeadDown, b.HeadDown), L(a.EarDroop, b.EarDroop), L(a.TailWag, b.TailWag),
                L(a.TailHz, b.TailHz), L(a.TailDroop, b.TailDroop), L(a.Bounce, b.Bounce), L(a.IdleGap, b.IdleGap));
        }
    }

    /// <summary>
    /// 11-16 동물 주민 기분 (순수 계산, 테스트 대상): 본인 만족도 보정(특성) + 정거장 만족도 + 집 없음 → −1(나쁨) ~ +1(좋음).
    /// 숫자를 보지 않아도 내부를 걸으며 정거장 상태가 느껴지게 평소 동작과 반응에 쓴다 (게임 규칙에는 영향 없음).
    /// </summary>
    public static class ResidentMood
    {
        /// <summary>본인 보정 이 값이면 그 몫이 가득 (특성 하나 ±2 안팎).</summary>
        public const float PersonalFull = 3f;
        /// <summary>정거장 만족도 기준 (이 위면 좋음 쪽).</summary>
        public const float SatisfactionNeutral = 55f;
        public const float SatisfactionSpan = 35f;
        public const float HomelessPenalty = 0.5f;
        /// <summary>이 위 = 기분 좋음 반응, 이 아래(음수) = 기분 나쁨 반응.</summary>
        public const float ReactionThreshold = 0.35f;

        public static readonly MoodStyle Sad = new MoodStyle(0.7f, 14f, 32f, 3f, 0.6f, 28f, 0f, 16f);
        public static readonly MoodStyle Neutral = new MoodStyle(1f, 0f, 4f, 10f, 1.3f, 8f, 0.003f, 11f);
        public static readonly MoodStyle Happy = new MoodStyle(1.25f, -4f, -8f, 26f, 3f, -12f, 0.012f, 7f);

        public static float Of(float personalMood, float satisfaction, bool homeless)
        {
            float personal = Clamp(personalMood / PersonalFull);
            float station = Clamp((satisfaction - SatisfactionNeutral) / SatisfactionSpan);
            return Clamp(0.5f * personal + 0.5f * station - (homeless ? HomelessPenalty : 0f));
        }

        public static MoodStyle Style(float mood)
        {
            mood = Clamp(mood);
            return mood < 0f ? MoodStyle.Lerp(Neutral, Sad, -mood) : MoodStyle.Lerp(Neutral, Happy, mood);
        }

        /// <summary>플레이어를 만났을 때 반응. 보통 기분은 주민 번호 · 만난 횟수로 손 흔들기 또는 바라보기를 번갈아.</summary>
        public static ResidentReaction Reaction(float mood, int id, int meetCount)
        {
            if (mood >= ReactionThreshold)
                return ResidentReaction.HopWave;
            if (mood <= -ReactionThreshold)
                return ResidentReaction.TurnAway;
            return ((uint)(id * 31 + meetCount) & 1u) == 0 ? ResidentReaction.Wave : ResidentReaction.Look;
        }

        private static float Clamp(float v) => Math.Max(-1f, Math.Min(1f, v));
    }
}
