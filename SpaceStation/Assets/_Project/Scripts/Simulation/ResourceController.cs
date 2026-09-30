using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// UI용 창구: 자원·인구·파손 시뮬레이션과 수리 명령. 실제 로직과 틱 순서는 <see cref="StationSimulation"/>.
    /// </summary>
    public sealed class ResourceController : MonoBehaviour
    {
        [SerializeField] private SimulationHost _host;

        public ResourceSimulation Simulation => _host.Simulation.Resources;
        public PopulationSimulation Population => _host.Simulation.Population;
        public DamageSystem Damage => _host.Simulation.Damage;
        public BalanceConfig Balance => _host.Simulation.Balance;
        public DayNightCycle DayNight => _host.Simulation.DayNight;
        public float ElapsedSeconds => _host.Simulation.ElapsedSeconds;

        public DurabilitySystem Durability => _host.Simulation.Durability;
        public AdjacencySystem Adjacency => _host.Simulation.Adjacency;

        /// <summary>파손 모듈 수리 시작: 수리 비용(건설 비용 × 수리 비율)을 지불하고 타이머 시작.</summary>
        public RepairResult TryRepair(ModuleInstance module) => _host.Simulation.TryRepair(module);
        public bool TryPrioritizeRepair(ModuleInstance module) => _host.Simulation.TryPrioritizeRepair(module);
        public bool TryCancelRepair(ModuleInstance module) => _host.Simulation.TryCancelRepair(module);
        public System.Collections.Generic.List<ResourceAmount> GetCancelRefund(ModuleInstance module) => _host.Simulation.GetCancelRefund(module);
        public MaintainResult TryMaintain(ModuleInstance module) => _host.Simulation.TryMaintain(module);
        public RebuildResult TryRebuild(ModuleInstance module, out ModuleInstance rebuilt) => _host.Simulation.TryRebuild(module, out rebuilt);
        public System.Collections.Generic.List<ResourceAmount> GetRefund(ModuleInstance module) => _host.Simulation.GetRefund(module);
        public System.Collections.Generic.List<ResourceAmount> GetRebuildCost(ModuleInstance module) => _host.Simulation.GetRebuildCost(module);
    }
}
