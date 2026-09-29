using System;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 등급·해금·설치 제한(<see cref="StationProgression"/>)과 한 판 통계·게임 오버(<see cref="GameSession"/>)를 씬에 연결한다.
    /// 인구나 모듈 수가 바뀔 때마다 재판정한다. StationController에 IPlacementPolicy로 등록된다.
    /// </summary>
    public sealed class ProgressionController : MonoBehaviour, IPlacementPolicy
    {
        [SerializeField] private StationGradeConfig _grades;
        [SerializeField] private ResourceController _resources;
        [SerializeField] private StationController _station;
        [SerializeField] private EventController _events;
        [SerializeField] private SimulationClock _clock;

        private StationProgression _progression;
        private GameSession _session;

        /// <summary>등급 재판정 결과가 바뀔 수 있는 모든 시점 (UI 갱신용).</summary>
        public event Action Changed;

        public StationProgression Progression => _progression;
        public GameSession Session => _session;
        public float PlaySeconds => _clock.Clock.SimulatedSeconds;
        public int ModuleCount => _station.Grid.ModuleCount;

        private void Awake()
        {
            _progression = new StationProgression(_grades);
            _session = new GameSession();
        }

        private void Start()
        {
            _station.PlacementPolicy = this;
            _resources.Simulation.Changed += Evaluate;
            _station.Grid.ModulePlaced += HandleStationChanged;
            _station.Grid.ModuleRemoved += HandleStationChanged;
            _events.Scheduler.EventStarted += HandleEventStarted;
            _resources.Damage.Destroyed += HandleDestroyed;
            Evaluate();
        }

        private void OnDestroy()
        {
            if (_station != null && ReferenceEquals(_station.PlacementPolicy, this))
                _station.PlacementPolicy = null;
            if (_resources != null && _resources.Simulation != null)
            {
                _resources.Simulation.Changed -= Evaluate;
                _resources.Damage.Destroyed -= HandleDestroyed;
            }
            if (_station != null && _station.Grid != null)
            {
                _station.Grid.ModulePlaced -= HandleStationChanged;
                _station.Grid.ModuleRemoved -= HandleStationChanged;
            }
            if (_events != null && _events.Scheduler != null)
                _events.Scheduler.EventStarted -= HandleEventStarted;
        }

        public PlacementResult CheckBuildable(ModuleData data, StationGrid grid)
        {
            return _progression.CheckBuildable(data, grid);
        }

        private void HandleStationChanged(ModuleInstance _) => Evaluate();
        private void HandleEventStarted(GameEventData _) => _session.RecordEvent();
        private void HandleDestroyed(ModuleInstance _) => _session.RecordDestroyed();

        private void Evaluate()
        {
            int population = _resources.Simulation.Population;
            _progression.Evaluate(population, _station.Grid.ModuleCount);
            _session.ObservePopulation(population);
            Changed?.Invoke();
        }
    }
}
