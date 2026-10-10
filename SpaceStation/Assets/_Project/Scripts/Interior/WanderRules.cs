using System;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-16 ③ 동물 주민 방 안 걷기 규칙 (순수 계산, 테스트 대상). 같은 방 안에서만, 서 있는 쉬는 주민만 걷는다
    /// (GDD 10번 "걸어 다니는 AI 없음"을 "방 안에서만"으로 완화 — 사용자 결정 2026-10-10).
    /// - 한 자리에 머무는 시간: 기분 좋을수록 짧음 (활발)
    /// - 나들이는 연달아 최대 <see cref="MaxExcursions"/>번, 그다음은 원래 자리로 돌아감
    /// - 걷는 빠르기: 기분 좋을수록 빠름
    /// </summary>
    public static class WanderRules
    {
        public const int MaxExcursions = 2;
        public const float IdleSad = 16f;
        public const float IdleHappy = 7f;
        public const float SpeedSad = 0.32f;
        public const float SpeedHappy = 0.6f;
        /// <summary>기분 보통일 때 나들이 확률 (좋음 +0.25 · 나쁨 −0.25).</summary>
        public const float WanderChance = 0.45f;

        /// <param name="r">0~1 난수</param>
        public static float IdleSeconds(float mood, double r)
        {
            float t = (Clamp(mood) + 1f) * 0.5f;
            return (IdleSad + (IdleHappy - IdleSad) * t) * (0.7f + 0.6f * (float)r);
        }

        /// <summary>지금 자리를 떠나 다른 곳으로 걸어갈지. 나들이를 다 했으면 false (원래 자리로).</summary>
        public static bool ShouldWander(float mood, int excursions, double r)
        {
            if (excursions >= MaxExcursions)
                return false;
            return r < WanderChance + 0.25f * Clamp(mood);
        }

        public static float WalkSpeed(float mood)
        {
            float t = (Clamp(mood) + 1f) * 0.5f;
            return SpeedSad + (SpeedHappy - SpeedSad) * t;
        }

        private static float Clamp(float v) => Math.Max(-1f, Math.Min(1f, v));
    }
}
