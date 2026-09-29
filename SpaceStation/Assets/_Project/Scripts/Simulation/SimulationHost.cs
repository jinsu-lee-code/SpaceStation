using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 씬에서 <see cref="StationSimulation"/>을 소유하고 SimulationClock 틱에 연결한다.
    /// 다른 컨트롤러(StationController, ResourceController 등)는 이 호스트를 통해 시뮬레이션에 접근한다.
    /// 디버그: F5 = 가중치 랜덤 이벤트 즉시 발생 (Phase 4 이후 제거 예정).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationHost : MonoBehaviour
    {
        [SerializeField] private BalanceConfig _balance;
        [SerializeField] private StationGradeConfig _grades;
        [SerializeField] private ModuleData _coreModule;
        [SerializeField] private AdjacencyRuleSet _adjacencyRules;
        [Tooltip("랜덤 이벤트 후보")]
        [SerializeField] private List<GameEventData> _events = new List<GameEventData>();
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private bool _enableDebugEventTrigger = true;

        public StationSimulation Simulation { get; private set; }
        public SimulationClock Clock => _clock;

        private void Awake()
        {
            Simulation = new StationSimulation(new StationSimulationSettings
            {
                Balance = _balance,
                Grades = _grades,
                CoreModule = _coreModule,
                Events = _events,
                AdjacencyRules = _adjacencyRules,
                Random01 = () => Random.value * 0.99999f, // [0, 1) 보장
            });
            Simulation.Resources.DepletionChanged += HandleDepletionChanged;
            Simulation.Events.EventStarted += HandleEventStarted;
            Simulation.Events.EventEnded += HandleEventEnded;
        }

        private void Start()
        {
            // SimulationClock.Awake 이후
            _clock.Clock.Ticked += HandleTicked;
        }

        private void OnDestroy()
        {
            if (_clock != null && _clock.Clock != null)
                _clock.Clock.Ticked -= HandleTicked;
        }

        private void Update()
        {
            if (!_enableDebugEventTrigger || _clock.InputLocked)
                return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f5Key.wasPressedThisFrame && Simulation.TriggerRandomEvent() == null)
                Debug.Log("[이벤트] 발생 가능한 이벤트 없음 (모두 진행 중)");
        }

        private void HandleTicked(long tick)
        {
            Simulation.Tick(_clock.Clock.TickInterval);
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

        private static void HandleEventStarted(GameEventData data)
        {
            Debug.Log($"[이벤트] 발생: {data.DisplayName}" + (data.IsTimed ? $" ({data.Duration:0}초)" : ""));
        }

        private static void HandleEventEnded(ActiveEvent active)
        {
            Debug.Log($"[이벤트] 종료: {active.Data.DisplayName}");
        }
    }
}
