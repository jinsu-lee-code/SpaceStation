using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    public enum RepairResult
    {
        Started,
        NotDamaged,
        AlreadyRepairing,
        InsufficientResources,
    }

    /// <summary>
    /// 정거장 시뮬레이션을 틱에 연결한다. 틱 순서: 파손(수리 진행·방치 파괴) → 자원(활성 모듈 + 파손 배율 + 누출) → 인구.
    /// </summary>
    public sealed class ResourceController : MonoBehaviour, IBuildCostHandler
    {
        [SerializeField] private BalanceConfig _balance;
        [SerializeField] private StationController _station;
        [SerializeField] private SimulationClock _clock;

        private readonly List<ModuleData> _activeModules = new List<ModuleData>();
        private readonly List<float> _productionMultipliers = new List<float>();
        private ResourceSimulation _simulation;
        private PopulationSimulation _population;
        private DamageSystem _damage;

        public ResourceSimulation Simulation => _simulation;
        public PopulationSimulation Population => _population;
        public DamageSystem Damage => _damage;
        public BalanceConfig Balance => _balance;

        private void Awake()
        {
            _simulation = new ResourceSimulation(_balance);
            _population = new PopulationSimulation(_balance, _simulation);
            _damage = new DamageSystem(_balance);
            _simulation.DepletionChanged += HandleDepletionChanged;
            _damage.Damaged += HandleDamaged;
            _damage.RepairStarted += HandleRepairStarted;
            _damage.Repaired += HandleRepaired;
            _damage.Destroyed += HandleDestroyed;
        }

        private void Start()
        {
            // StationController / SimulationClock의 Awake 이후
            _clock.Clock.Ticked += HandleTicked;
            _station.Grid.ModulePlaced += HandleStationChanged;
            _station.Grid.ModuleRemoved += HandleModuleRemoved;
            _station.CostHandler = this;
            RefreshCapacities();
        }

        public bool CanAfford(IReadOnlyList<ResourceAmount> cost) => _simulation.CanAfford(cost);
        public bool TrySpend(IReadOnlyList<ResourceAmount> cost) => _simulation.TrySpend(cost);
        public void Refund(IReadOnlyList<ResourceAmount> buildCost) => _simulation.RefundBuildCost(buildCost);

        /// <summary>파손 모듈 수리 시작: 수리 비용(건설 비용 × 수리 비율)을 지불하고 타이머 시작.</summary>
        public RepairResult TryRepair(ModuleInstance module)
        {
            if (!_damage.TryGetInfo(module, out var info))
                return RepairResult.NotDamaged;
            if (info.IsRepairing)
                return RepairResult.AlreadyRepairing;
            if (!_simulation.TrySpend(_damage.GetRepairCost(module)))
                return RepairResult.InsufficientResources;
            _damage.StartRepair(module);
            return RepairResult.Started;
        }

        private void OnDestroy()
        {
            if (_simulation != null)
                _simulation.DepletionChanged -= HandleDepletionChanged;
            if (_damage != null)
            {
                _damage.Damaged -= HandleDamaged;
                _damage.RepairStarted -= HandleRepairStarted;
                _damage.Repaired -= HandleRepaired;
                _damage.Destroyed -= HandleDestroyed;
            }
            if (_clock != null && _clock.Clock != null)
                _clock.Clock.Ticked -= HandleTicked;
            if (_station != null && ReferenceEquals(_station.CostHandler, this))
                _station.CostHandler = null;
            if (_station != null && _station.Grid != null)
            {
                _station.Grid.ModulePlaced -= HandleStationChanged;
                _station.Grid.ModuleRemoved -= HandleModuleRemoved;
            }
        }

        private void HandleTicked(long tick)
        {
            float dt = _clock.Clock.TickInterval;
            _damage.Tick(dt); // 방치 파괴 시 그리드에서 제거 → 연결 판정 갱신
            CollectActiveModules();
            _simulation.SetExternalDrain(ResourceType.Oxygen, _damage.OxygenLeakPerSecond);
            _simulation.Tick(_activeModules, _productionMultipliers, dt);
            _population.Tick(dt); // 이번 틱의 고갈 상태를 기준으로 인구·만족도 반영
        }

        /// <summary>건설/철거 직후 한도·수용 인구를 바로 반영 (다음 틱을 기다리지 않음).</summary>
        private void HandleStationChanged(ModuleInstance _)
        {
            RefreshCapacities();
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            _damage.Forget(module); // 파손 중 철거된 경우
            RefreshCapacities();
        }

        private void RefreshCapacities()
        {
            CollectActiveModules();
            _simulation.RefreshCapacities(_activeModules);
        }

        private void CollectActiveModules()
        {
            _activeModules.Clear();
            _productionMultipliers.Clear();
            foreach (var module in _station.Grid.Modules)
            {
                if (module.Data == null || !_station.Connectivity.IsActive(module))
                    continue;
                _activeModules.Add(module.Data);
                _productionMultipliers.Add(_damage.GetProductionMultiplier(module));
            }
        }

        private void HandleDamaged(DamageInfo info) => SetDamageVisual(info.Module, ModuleDamageVisual.Damaged);
        private void HandleRepairStarted(DamageInfo info) => SetDamageVisual(info.Module, ModuleDamageVisual.Repairing);
        private void HandleRepaired(ModuleInstance module) => SetDamageVisual(module, ModuleDamageVisual.None);

        private void HandleDestroyed(ModuleInstance module)
        {
            _station.DestroyModule(module); // 환급 없음
        }

        private void SetDamageVisual(ModuleInstance module, ModuleDamageVisual visual)
        {
            if (_station.TryGetView(module, out var view))
                view.SetDamageVisual(visual);
        }

        private static void HandleDepletionChanged(ResourceType type, bool depleted)
        {
            if (type == ResourceType.Metal)
                return; // 금속 0은 건설로 다 쓴 정상 상황
            if (depleted)
                Debug.LogWarning($"[자원] {type} 고갈");
            else
                Debug.Log($"[자원] {type} 고갈 해소");
        }
    }
}
