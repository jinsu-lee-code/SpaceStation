using System;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    public enum PopulationChangeReason
    {
        Growth,
        OxygenDepleted,
        LowSatisfaction,
        Overcrowded,
    }

    /// <summary>
    /// 인구·만족도 계산 (BALANCE.md 8번). 자원 틱 직후에 호출한다.
    /// 만족도: 고갈된 자원마다 초당 감소, 고갈이 없으면 초당 회복 (0~100).
    /// 증가: 거주자 소비 자원 고갈 없음 + 만족도 최소값 이상 + 수용 여유 → 만족도에 비례한 간격마다 +1.
    /// 감소: 산소 고갈 / 만족도 미달 / 수용 초과 — 각각 독립 타이머로 -1.
    /// </summary>
    public sealed class PopulationSimulation
    {
        public const float MaxSatisfaction = 100f;

        private readonly BalanceConfig _config;
        private readonly ResourceSimulation _resources;
        private float _oxygenLossTimer;
        private float _lowSatisfactionLossTimer;
        private float _overcrowdedLossTimer;

        /// <summary>(변화량, 사유).</summary>
        public event Action<int, PopulationChangeReason> PopulationChanged;
        /// <summary>만족도·진행도 등 표시값이 바뀐 뒤 (UI 갱신용).</summary>
        public event Action Changed;

        public float Satisfaction { get; private set; }
        /// <summary>마지막 틱의 초당 만족도 변화량.</summary>
        public float SatisfactionRate { get; private set; }
        /// <summary>다음 +1까지 진행도 0~1. 증가 조건이 아니면 0.</summary>
        public float GrowthProgress { get; private set; }
        public bool IsGrowing { get; private set; }
        /// <summary>거주자 소비 자원(산소·물·식량 등) 중 하나라도 고갈되어 증가가 막힘.</summary>
        public bool IsGrowthBlockedByShortage { get; private set; }
        public bool IsLosingFromOxygen { get; private set; }
        public bool IsLosingFromLowSatisfaction { get; private set; }
        public bool IsOvercrowded { get; private set; }

        public PopulationSimulation(BalanceConfig config, ResourceSimulation resources)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _resources = resources ?? throw new ArgumentNullException(nameof(resources));
            Satisfaction = Mathf.Clamp(config.StartingSatisfaction, 0f, MaxSatisfaction);
        }

        /// <summary>테스트/디버그용.</summary>
        public void SetSatisfaction(float value)
        {
            Satisfaction = Mathf.Clamp(value, 0f, MaxSatisfaction);
            Changed?.Invoke();
        }

        /// <summary>만족도에 따른 +1 간격(초). 최소 만족도 미만이면 무한대.</summary>
        public float GetGrowthInterval(float satisfaction)
        {
            float min = _config.GrowthMinSatisfaction;
            if (satisfaction < min)
                return float.PositiveInfinity;
            float t = Mathf.InverseLerp(min, MaxSatisfaction, satisfaction);
            return Mathf.Lerp(_config.GrowthIntervalAtMinSatisfaction, _config.GrowthIntervalAtMaxSatisfaction, t);
        }

        public void Tick(float deltaSeconds)
        {
            UpdateSatisfaction(deltaSeconds);
            ApplyLosses(deltaSeconds);
            ApplyGrowth(deltaSeconds);
            Changed?.Invoke();
        }

        private void UpdateSatisfaction(float dt)
        {
            float rate = 0f;
            bool anyDepleted = false;
            foreach (var penalty in _config.SatisfactionPenaltyPerSecond)
            {
                if (!_resources.IsDepleted(penalty.Type))
                    continue;
                rate -= penalty.Amount;
                anyDepleted = true;
            }
            if (!anyDepleted)
                rate += _config.SatisfactionRecoveryPerSecond;

            SatisfactionRate = rate;
            Satisfaction = Mathf.Clamp(Satisfaction + rate * dt, 0f, MaxSatisfaction);
        }

        private void ApplyLosses(float dt)
        {
            IsLosingFromOxygen = _resources.IsDepleted(ResourceType.Oxygen);
            IsLosingFromLowSatisfaction = Satisfaction < _config.LowSatisfactionThreshold;
            IsOvercrowded = _resources.Population > _resources.HousingCapacity;

            if (Advance(IsLosingFromOxygen, ref _oxygenLossTimer, _config.OxygenDepletedLossInterval, dt))
                Change(-1, PopulationChangeReason.OxygenDepleted);
            if (Advance(IsLosingFromLowSatisfaction, ref _lowSatisfactionLossTimer, _config.LowSatisfactionLossInterval, dt))
                Change(-1, PopulationChangeReason.LowSatisfaction);
            if (Advance(IsOvercrowded, ref _overcrowdedLossTimer, _config.OvercrowdedLossInterval, dt))
                Change(-1, PopulationChangeReason.Overcrowded);
        }

        private void ApplyGrowth(float dt)
        {
            IsGrowthBlockedByShortage = IsAnyResidentResourceDepleted();
            IsGrowing = !IsGrowthBlockedByShortage
                        && Satisfaction >= _config.GrowthMinSatisfaction
                        && _resources.Population < _resources.HousingCapacity;
            if (!IsGrowing)
            {
                GrowthProgress = 0f;
                return;
            }

            GrowthProgress += dt / GetGrowthInterval(Satisfaction);
            if (GrowthProgress >= 1f - 1e-4f) // 부동소수 누적 오차 허용 (0.1 × 10 등)
            {
                GrowthProgress = 0f;
                Change(+1, PopulationChangeReason.Growth);
            }
        }

        private bool IsAnyResidentResourceDepleted()
        {
            foreach (var a in _config.ConsumptionPerResident)
            {
                if (a.Amount > 0f && _resources.IsDepleted(a.Type))
                    return true;
            }
            return false;
        }

        /// <summary>조건이 유지되는 동안 타이머를 누적하고, 간격을 채우면 true. 조건이 풀리면 초기화.</summary>
        private static bool Advance(bool condition, ref float timer, float interval, float dt)
        {
            if (!condition)
            {
                timer = 0f;
                return false;
            }
            timer += dt;
            if (timer < interval)
                return false;
            timer -= interval;
            return true;
        }

        private void Change(int delta, PopulationChangeReason reason)
        {
            int before = _resources.Population;
            _resources.SetPopulation(before + delta);
            int actual = _resources.Population - before;
            if (actual != 0)
                PopulationChanged?.Invoke(actual, reason);
        }
    }
}
