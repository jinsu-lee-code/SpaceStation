using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>UI용 창구: 이벤트 스케줄러. 발생·효과 로직은 <see cref="StationSimulation"/>.</summary>
    public sealed class EventController : MonoBehaviour
    {
        [SerializeField] private SimulationHost _host;

        public EventScheduler Scheduler => _host.Simulation.Events;
        public IReadOnlyList<GameEventData> Events => _host.Simulation.EventPool;
    }
}
