using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// <see cref="EventScheduler"/>를 시뮬레이션 틱에 연결한다 (배속·일시정지를 따름).
    /// 이벤트별 효과는 3-3에서 EventStarted/EventEnded를 구독해 적용한다.
    /// 디버그: F5 = 가중치 랜덤 이벤트 즉시 발생 (Phase 4 이후 제거 예정).
    /// </summary>
    public sealed class EventController : MonoBehaviour
    {
        [SerializeField] private BalanceConfig _balance;
        [SerializeField] private SimulationClock _clock;
        [Tooltip("랜덤 발생 후보")]
        [SerializeField] private List<GameEventData> _events = new List<GameEventData>();
        [SerializeField] private bool _enableDebugTrigger = true;

        private EventScheduler _scheduler;

        public EventScheduler Scheduler => _scheduler;
        public IReadOnlyList<GameEventData> Events => _events;

        private void Awake()
        {
            _scheduler = new EventScheduler(
                _balance.EventGracePeriod, _balance.EventIntervalMin, _balance.EventIntervalMax,
                () => Random.value * 0.99999f); // [0, 1) 보장
            _scheduler.EventStarted += HandleEventStarted;
            _scheduler.EventEnded += HandleEventEnded;
        }

        private void Start()
        {
            _clock.Clock.Ticked += HandleTicked;
        }

        private void OnDestroy()
        {
            if (_clock != null && _clock.Clock != null)
                _clock.Clock.Ticked -= HandleTicked;
            if (_scheduler != null)
            {
                _scheduler.EventStarted -= HandleEventStarted;
                _scheduler.EventEnded -= HandleEventEnded;
            }
        }

        private void Update()
        {
            if (!_enableDebugTrigger)
                return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f5Key.wasPressedThisFrame)
            {
                if (_scheduler.TriggerRandom(_events) == null)
                    Debug.Log("[이벤트] 발생 가능한 이벤트 없음 (모두 진행 중)");
            }
        }

        private void HandleTicked(long tick)
        {
            _scheduler.Tick(_clock.Clock.TickInterval, _events);
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
