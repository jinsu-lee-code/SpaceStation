using System;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>UI용 창구: 등급·해금(<see cref="StationProgression"/>)과 통계·게임 오버(<see cref="GameSession"/>).</summary>
    public sealed class ProgressionController : MonoBehaviour
    {
        [SerializeField] private SimulationHost _host;

        /// <summary>등급·승패 재판정 직후 (UI 갱신용).</summary>
        public event Action Changed
        {
            add => _host.Simulation.ProgressionEvaluated += value;
            remove
            {
                if (_host != null && _host.Simulation != null)
                    _host.Simulation.ProgressionEvaluated -= value;
            }
        }

        public StationProgression Progression => _host.Simulation.Progression;
        /// <summary>13-1 장 틀 (없으면 null = 등급 방식).</summary>
        public ChapterSystem Chapters => _host.Simulation.Chapters;
        public GameSession Session => _host.Simulation.Session;
        public float PlaySeconds => _host.Simulation.ElapsedSeconds;
        public int ModuleCount => _host.Simulation.Grid.ModuleCount;
    }
}
