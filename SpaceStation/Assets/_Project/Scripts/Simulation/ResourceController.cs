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

        /// <summary>파손 모듈 수리 시작: 수리 비용(건설 비용 × 수리 비율)을 지불하고 타이머 시작.</summary>
        public RepairResult TryRepair(ModuleInstance module) => _host.Simulation.TryRepair(module);
    }
}
