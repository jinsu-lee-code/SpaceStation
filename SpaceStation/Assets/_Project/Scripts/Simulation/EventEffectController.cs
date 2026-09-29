using System;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>UI용 창구: 이벤트 효과 알림. 효과 적용은 <see cref="StationSimulation"/>.</summary>
    public sealed class EventEffectController : MonoBehaviour
    {
        [SerializeField] private SimulationHost _host;

        /// <summary>(요약 메시지, 긍정 여부). 상태 표시줄 알림용.</summary>
        public event Action<string, bool> Reported
        {
            add => _host.Simulation.EffectReported += value;
            remove
            {
                if (_host != null && _host.Simulation != null)
                    _host.Simulation.EffectReported -= value;
            }
        }
    }
}
