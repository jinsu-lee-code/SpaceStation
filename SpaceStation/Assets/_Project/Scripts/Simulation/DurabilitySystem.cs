using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>모듈 하나의 내구도 상태.</summary>
    public sealed class DurabilityInfo
    {
        public const float FullDurability = 100f;

        public ModuleInstance Module { get; }
        public float Current { get; internal set; } = FullDurability;
        /// <summary>정비로 회복 가능한 상한. 정비할 때마다 줄어든다 (누적 노후도).</summary>
        public float Max { get; internal set; } = FullDurability;
        public int MaintenanceCount { get; internal set; }

        internal DurabilityInfo(ModuleInstance module)
        {
            Module = module;
        }
    }

    public enum MaintainResult
    {
        Done,
        NotApplicable,
        AlreadyAtMax,
        InsufficientResources,
    }

    /// <summary>
    /// 모듈 노후화·정비 (GDD 13번, BALANCE.md 15번).
    /// 내구도가 시간에 따라 줄고, 기준치 아래에서는 생산 효율이 선형으로 떨어진다. 0이 되면 파괴(<see cref="WornOut"/>).
    /// 정비: 즉시 최대치로 회복하되 최대치 자체가 매번 줄어든다 → 오래된 모듈은 재건축이 유리해지는 구간이 생김.
    /// 코어처럼 등록하지 않은 모듈은 노후화되지 않는다.
    /// </summary>
    public sealed class DurabilitySystem
    {
        /// <summary>이 값 이하는 0으로 본다 (0.2 × 500 같은 부동소수 누적 오차 허용).</summary>
        private const float ZeroEpsilon = 0.01f;

        private readonly BalanceConfig _config;
        private readonly Dictionary<ModuleInstance, DurabilityInfo> _states = new Dictionary<ModuleInstance, DurabilityInfo>();
        private readonly List<DurabilityInfo> _wornOut = new List<DurabilityInfo>();

        /// <summary>내구도 0 도달. 구독자가 그리드에서 제거해야 한다 (환급 없음).</summary>
        public event Action<ModuleInstance> WornOut;
        public event Action<DurabilityInfo> Maintained;

        public IReadOnlyCollection<DurabilityInfo> Modules => _states.Values;

        public DurabilitySystem(BalanceConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Effects = new ResearchEffects(config);
        }

        /// <summary>Phase 6 연구 효과 (노후 속도 배율, 효율 최저값). StationSimulation이 공유 인스턴스로 바꿔 끼운다.</summary>
        public ResearchEffects Effects { get; set; }

        public void Track(ModuleInstance module)
        {
            if (module != null && !_states.ContainsKey(module))
                _states.Add(module, new DurabilityInfo(module));
        }

        public void Forget(ModuleInstance module)
        {
            if (module != null)
                _states.Remove(module);
        }

        public bool TryGetInfo(ModuleInstance module, out DurabilityInfo info)
        {
            info = null;
            return module != null && _states.TryGetValue(module, out info);
        }

        /// <summary>내구도에 따른 생산 배율 (추적하지 않는 모듈은 1).</summary>
        public float GetEfficiency(ModuleInstance module)
        {
            return TryGetInfo(module, out var info) ? EfficiencyFor(info.Current) : 1f;
        }

        public float EfficiencyFor(float durability)
        {
            float threshold = _config.DurabilityEfficiencyThreshold;
            if (durability >= threshold || threshold <= 0f)
                return 1f;
            return Math.Max(Effects.DurabilityEfficiencyFloor, Math.Max(0f, durability / threshold)); // 연구: 효율 최저값
        }

        /// <summary>철거 환급 배율 (내구도/100). 추적하지 않는 모듈은 1.</summary>
        public float GetRefundMultiplier(ModuleInstance module)
        {
            return TryGetInfo(module, out var info) ? info.Current / DurabilityInfo.FullDurability : 1f;
        }

        /// <summary>정비 비용 = 건설비 × 비율 × (최대 − 현재)/100.</summary>
        public List<ResourceAmount> GetMaintenanceCost(ModuleInstance module)
        {
            var cost = new List<ResourceAmount>();
            if (!TryGetInfo(module, out var info) || module.Data == null)
                return cost;
            float worn = Math.Max(0f, info.Max - info.Current) / DurabilityInfo.FullDurability;
            foreach (var a in module.Data.BuildCost)
                cost.Add(new ResourceAmount(a.Type, a.Amount * _config.MaintenanceCostRate * worn));
            return cost;
        }

        /// <summary>정비 후 최대 내구도 (= 회복될 값).</summary>
        public float MaxAfterMaintenance(DurabilityInfo info)
        {
            return Math.Max(_config.MaxDurabilityFloor, info.Max - _config.MaintenanceMaxLoss);
        }

        /// <summary>정비 적용 (비용은 호출자가 먼저 지불). 최대치를 깎고 그 값으로 회복.</summary>
        public bool Maintain(ModuleInstance module)
        {
            if (!TryGetInfo(module, out var info) || info.Current >= info.Max - 1e-4f)
                return false;
            info.Max = MaxAfterMaintenance(info);
            info.Current = info.Max;
            info.MaintenanceCount++;
            Maintained?.Invoke(info);
            return true;
        }

        /// <summary>충격(운석)으로 내구도 감소. 0이 되면 즉시 WornOut.</summary>
        public void ApplyImpact(ModuleInstance module, float amount)
        {
            if (!TryGetInfo(module, out var info) || amount <= 0f)
                return;
            info.Current = Math.Max(0f, info.Current - amount);
            if (info.Current <= ZeroEpsilon)
                RaiseWornOut(info);
        }

        public void Tick(float dt)
        {
            float decay = _config.DurabilityDecayPerSecond * Effects.DecayMultiplier * dt; // 연구: 노후 완화
            if (decay <= 0f || _states.Count == 0)
                return;

            _wornOut.Clear();
            foreach (var info in _states.Values)
            {
                info.Current = Math.Max(0f, info.Current - decay);
                if (info.Current <= ZeroEpsilon)
                    _wornOut.Add(info);
            }
            foreach (var info in _wornOut)
                RaiseWornOut(info);
        }

        private void RaiseWornOut(DurabilityInfo info)
        {
            _states.Remove(info.Module);
            WornOut?.Invoke(info.Module);
        }
    }
}
