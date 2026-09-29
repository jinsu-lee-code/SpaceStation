using System;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 궤도 낮/밤 주기 (BALANCE 14번). 경과 시간으로 태양광 출력 배율을 계산한다.
    /// 한 주기 = [낮 (끝 전환 구간에서 서서히 감소)] + [밤 (끝 전환 구간에서 서서히 회복)]. 시작은 낮의 처음.
    /// 주기가 0이면 항상 낮(배율 1).
    /// </summary>
    public sealed class DayNightCycle
    {
        public float Period { get; }
        public float DayLength { get; }
        public float NightLength => Period - DayLength;
        public float Transition { get; }
        public float NightMultiplier { get; }
        public bool Enabled => Period > 0f && DayLength < Period;

        public DayNightCycle(float period, float dayLength, float transition, float nightMultiplier)
        {
            Period = Math.Max(0f, period);
            DayLength = Math.Max(0f, Math.Min(dayLength, Period));
            // 전환 구간은 낮·밤 어느 쪽 길이도 넘지 않게
            Transition = Math.Max(0f, Math.Min(transition, Math.Min(DayLength, Period - DayLength)));
            NightMultiplier = Math.Max(0f, Math.Min(1f, nightMultiplier));
        }

        private float Phase(float time) => Enabled ? ((time % Period) + Period) % Period : 0f;

        public bool IsDay(float time) => !Enabled || Phase(time) < DayLength;

        /// <summary>현재 낮/밤이 끝날 때까지 남은 시간(초).</summary>
        public float TimeUntilPhaseChange(float time)
        {
            if (!Enabled)
                return float.PositiveInfinity;
            float t = Phase(time);
            return t < DayLength ? DayLength - t : Period - t;
        }

        /// <summary>태양광 출력 배율 (낮 1 ~ 밤 NightMultiplier).</summary>
        public float SolarMultiplier(float time)
        {
            if (!Enabled)
                return 1f;
            float t = Phase(time);
            if (t < DayLength - Transition)
                return 1f;
            if (t < DayLength) // 해질녘
                return Lerp(1f, NightMultiplier, (t - (DayLength - Transition)) / Transition);
            if (t < Period - Transition)
                return NightMultiplier;
            return Lerp(NightMultiplier, 1f, (t - (Period - Transition)) / Transition); // 새벽
        }

        private static float Lerp(float a, float b, float k) => a + (b - a) * Math.Max(0f, Math.Min(1f, k));
    }
}
