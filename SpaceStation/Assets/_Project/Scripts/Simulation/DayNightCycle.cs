using System;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 궤도 낮/밤 주기 (BALANCE 14번). 경과 시간으로 태양광 출력 배율을 계산한다.
    /// 한 주기 = [낮 (끝 전환 구간에서 서서히 감소)] + [밤 (끝 전환 구간에서 서서히 회복)]. 시작은 낮의 처음.
    /// 주기가 0이면 항상 낮(배율 1).
    /// 13-4 공통 고리: <see cref="OverrideNight"/>로 정해진 구간을 밤으로 덮을 수 있다 (서사판 1장 절정 '첫 긴 밤'). 덮은 밤이 끝난 뒤
    /// 원래 주기가 아직 밤이면 원래 새벽까지 낮으로 덮어, 밤이 정해진 길이보다 길어지지 않게 한다. 저장하지 않음 (덮는 쪽이 복원 때 다시 덮음).
    /// </summary>
    public sealed class DayNightCycle
    {
        public float Period { get; }
        public float DayLength { get; }
        public float NightLength => Period - DayLength;
        public float Transition { get; }
        public float NightMultiplier { get; }
        public bool Enabled => Period > 0f && DayLength < Period;

        private float _nightFrom = -1f, _nightUntil = -1f, _dayUntil = -1f, _startSolar = 1f;

        public DayNightCycle(float period, float dayLength, float transition, float nightMultiplier)
        {
            Period = Math.Max(0f, period);
            DayLength = Math.Max(0f, Math.Min(dayLength, Period));
            // 전환 구간은 낮·밤 어느 쪽 길이도 넘지 않게
            Transition = Math.Max(0f, Math.Min(transition, Math.Min(DayLength, Period - DayLength)));
            NightMultiplier = Math.Max(0f, Math.Min(1f, nightMultiplier));
        }

        private float Phase(float time) => Enabled ? ((time % Period) + Period) % Period : 0f;

        /// <summary>덮은 밤이 있는지 (끝났어도 지우기 전까지).</summary>
        public bool HasOverride => _nightUntil > _nightFrom;

        /// <summary>13-4: from부터 seconds 동안 밤으로 덮음 (해질녘 · 새벽 전환 포함).</summary>
        public void OverrideNight(float from, float seconds)
        {
            _startSolar = NaturalSolar(from);
            _nightFrom = from;
            _nightUntil = from + Math.Max(0f, seconds);
            _dayUntil = NaturalIsDay(_nightUntil) ? _nightUntil : _nightUntil + NaturalTimeUntilPhaseChange(_nightUntil);
        }

        public void ClearOverride()
        {
            _nightFrom = _nightUntil = _dayUntil = -1f;
        }

        /// <summary>0 = 덮지 않음, 1 = 덮은 밤, 2 = 덮은 밤 뒤 원래 새벽까지 덮은 낮.</summary>
        private int Window(float time)
        {
            if (!HasOverride)
                return 0;
            if (time >= _nightFrom && time < _nightUntil)
                return 1;
            if (time >= _nightUntil && time < _dayUntil)
                return 2;
            return 0;
        }

        public bool IsDay(float time)
        {
            switch (Window(time))
            {
                case 1: return false;
                case 2: return true;
                default: return NaturalIsDay(time);
            }
        }

        /// <summary>현재 낮/밤이 끝날 때까지 남은 시간(초).</summary>
        public float TimeUntilPhaseChange(float time)
        {
            switch (Window(time))
            {
                case 1: return _nightUntil - time;
                case 2: return _dayUntil - time + NaturalTimeUntilPhaseChange(_dayUntil);
                default: return NaturalTimeUntilPhaseChange(time);
            }
        }

        /// <summary>태양광 출력 배율 (낮 1 ~ 밤 NightMultiplier).</summary>
        public float SolarMultiplier(float time)
        {
            switch (Window(time))
            {
                case 1:
                    if (Transition > 0f && time - _nightFrom < Transition) // 해질녘 (덮기 시작 때 밝기에서)
                        return Lerp(_startSolar, NightMultiplier, (time - _nightFrom) / Transition);
                    if (Transition > 0f && _nightUntil - time < Transition) // 새벽
                        return Lerp(NightMultiplier, 1f, 1f - (_nightUntil - time) / Transition);
                    return NightMultiplier;
                case 2:
                    return 1f;
                default:
                    return NaturalSolar(time);
            }
        }

        private bool NaturalIsDay(float time) => !Enabled || Phase(time) < DayLength;

        private float NaturalTimeUntilPhaseChange(float time)
        {
            if (!Enabled)
                return float.PositiveInfinity;
            float t = Phase(time);
            return t < DayLength ? DayLength - t : Period - t;
        }

        private float NaturalSolar(float time)
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
