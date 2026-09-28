using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// <see cref="ResourceSimulation"/>을 틱에 연결한다. 매 틱 코어와 연결된(활성) 모듈만 넘긴다.
    /// </summary>
    public sealed class ResourceController : MonoBehaviour, IBuildCostHandler
    {
        [SerializeField] private BalanceConfig _balance;
        [SerializeField] private StationController _station;
        [SerializeField] private SimulationClock _clock;

        private readonly List<ModuleData> _activeModules = new List<ModuleData>();
        private ResourceSimulation _simulation;
        private PopulationSimulation _population;

        public ResourceSimulation Simulation => _simulation;
        public PopulationSimulation Population => _population;
        public BalanceConfig Balance => _balance;

        private void Awake()
        {
            _simulation = new ResourceSimulation(_balance);
            _population = new PopulationSimulation(_balance, _simulation);
            _simulation.DepletionChanged += HandleDepletionChanged;
        }

        private void Start()
        {
            // StationController / SimulationClock의 Awake 이후
            _clock.Clock.Ticked += HandleTicked;
            _station.Grid.ModulePlaced += HandleStationChanged;
            _station.Grid.ModuleRemoved += HandleStationChanged;
            _station.CostHandler = this;
            RefreshCapacities();
        }

        public bool CanAfford(IReadOnlyList<ResourceAmount> cost) => _simulation.CanAfford(cost);
        public bool TrySpend(IReadOnlyList<ResourceAmount> cost) => _simulation.TrySpend(cost);
        public void Refund(IReadOnlyList<ResourceAmount> buildCost) => _simulation.RefundBuildCost(buildCost);

        private void OnDestroy()
        {
            if (_simulation != null)
                _simulation.DepletionChanged -= HandleDepletionChanged;
            if (_clock != null && _clock.Clock != null)
                _clock.Clock.Ticked -= HandleTicked;
            if (_station != null && ReferenceEquals(_station.CostHandler, this))
                _station.CostHandler = null;
            if (_station != null && _station.Grid != null)
            {
                _station.Grid.ModulePlaced -= HandleStationChanged;
                _station.Grid.ModuleRemoved -= HandleStationChanged;
            }
        }

        private void HandleTicked(long tick)
        {
            CollectActiveModules();
            float dt = _clock.Clock.TickInterval;
            _simulation.Tick(_activeModules, dt);
            _population.Tick(dt); // 이번 틱의 고갈 상태를 기준으로 인구·만족도 반영
        }

        /// <summary>건설/철거 직후 한도·수용 인구를 바로 반영 (다음 틱을 기다리지 않음).</summary>
        private void HandleStationChanged(ModuleInstance _)
        {
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
            foreach (var module in _station.Grid.Modules)
            {
                if (module.Data != null && _station.Connectivity.IsActive(module))
                    _activeModules.Add(module.Data);
            }
        }

        private static void HandleDepletionChanged(ResourceType type, bool depleted)
        {
            if (type == ResourceType.Metal)
                return; // 금속 0은 건설로 다 쓴 정상 상황
            if (depleted)
                Debug.LogWarning($"[자원] {type} 고갈"); // 경고 UI는 2-6 HUD
            else
                Debug.Log($"[자원] {type} 고갈 해소");
        }
    }
}
