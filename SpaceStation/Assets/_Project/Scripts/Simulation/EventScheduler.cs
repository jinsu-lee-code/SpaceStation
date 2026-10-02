using System;
using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>진행 중인 지속형 이벤트 하나.</summary>
    public sealed class ActiveEvent
    {
        public GameEventData Data { get; }
        /// <summary>실제 적용된 지속시간 (등급 강도 배율 반영).</summary>
        public float Duration { get; }
        public float Remaining { get; internal set; }
        public float Elapsed => Duration - Remaining;

        internal ActiveEvent(GameEventData data, float duration)
        {
            Data = data;
            Duration = duration;
            Remaining = duration;
        }
    }

    /// <summary>
    /// 랜덤 이벤트 발생기 (BALANCE.md 9번).
    /// 유예 시간 이후 [최소, 최대] 무작위 간격마다 가중치로 이벤트를 뽑는다.
    /// 서로 다른 이벤트는 겹칠 수 있고, 이미 진행 중인 같은 이벤트는 뽑지 않는다.
    /// 즉발 이벤트(지속 0)는 Started만, 지속형은 Started → (지속시간 후) Ended.
    /// </summary>
    public sealed class EventScheduler
    {
        private readonly float _intervalMin;
        private readonly float _intervalMax;
        private readonly Func<float> _random01;
        private readonly List<ActiveEvent> _active = new List<ActiveEvent>();
        private readonly List<GameEventData> _candidates = new List<GameEventData>();
        private readonly List<ActiveEvent> _ended = new List<ActiveEvent>();

        public event Action<GameEventData> EventStarted;
        public event Action<ActiveEvent> EventEnded;
        /// <summary>진행 중 목록·남은 시간이 바뀐 뒤 (UI 갱신용).</summary>
        public event Action Changed;

        public IReadOnlyList<ActiveEvent> ActiveEvents => _active;
        /// <summary>다음 랜덤 이벤트까지 남은 시간(초).</summary>
        public float TimeUntilNext { get; private set; }

        /// <summary>새 간격을 정할 때 곱하는 배율 (등급별 빈도). null이면 1.</summary>
        public Func<float> IntervalMultiplier { get; set; }
        /// <summary>지속형 이벤트의 실제 지속시간 (등급별 강도). null이면 데이터 값.</summary>
        public Func<GameEventData, float> DurationProvider { get; set; }

        /// <summary>
        /// 조기 경보 (Phase 6 방어 연구): 다음 이벤트까지 이 시간(초) 이하가 되면 다음 이벤트를 미리 뽑는다. 0이면 기존과 같이 발생 순간에 뽑음
        /// (난수 순서가 같아 연구가 없으면 결과가 바뀌지 않는다).
        /// </summary>
        public float WarningLead { get; set; }
        /// <summary>미리 뽑은 이벤트 중 경보할 종류 (null이면 전부).</summary>
        public Func<GameEventData, bool> WarningFilter { get; set; }
        /// <summary>경보 중인 다음 이벤트 (없으면 null). 남은 시간은 <see cref="TimeUntilNext"/>.</summary>
        public GameEventData Upcoming { get; private set; }
        /// <summary>경보 시작(이벤트) / 해제(null).</summary>
        public event Action<GameEventData> UpcomingChanged;

        private bool _preRolled;
        private GameEventData _preRolledEvent;

        /// <param name="random01">[0, 1) 난수. 테스트에서 결과를 고정할 수 있도록 주입한다.</param>
        public EventScheduler(float gracePeriod, float intervalMin, float intervalMax, Func<float> random01)
        {
            if (intervalMin <= 0f || intervalMax < intervalMin)
                throw new ArgumentOutOfRangeException(nameof(intervalMin));
            _intervalMin = intervalMin;
            _intervalMax = intervalMax;
            _random01 = random01 ?? throw new ArgumentNullException(nameof(random01));
            TimeUntilNext = gracePeriod + NextInterval();
        }

        /// <summary>세이브 복원: 다음 이벤트까지 남은 시간 (이벤트 없음).</summary>
        internal void RestoreTimer(float timeUntilNext)
        {
            TimeUntilNext = Math.Max(0.01f, timeUntilNext);
        }

        private void ClearPreRoll()
        {
            _preRolled = false;
            _preRolledEvent = null;
            if (Upcoming == null)
                return;
            Upcoming = null;
            UpcomingChanged?.Invoke(null);
        }

        /// <summary>세이브 복원: 경보 중이던 다음 이벤트 (이벤트 없음).</summary>
        internal void RestoreUpcoming(GameEventData data)
        {
            if (data == null)
                return;
            _preRolled = true;
            _preRolledEvent = data;
            Upcoming = data;
        }

        /// <summary>세이브 복원: 진행 중이던 지속형 이벤트 (시작 효과는 다시 적용하지 않음).</summary>
        internal void RestoreActive(GameEventData data, float duration, float remaining)
        {
            if (data == null || !data.IsTimed || IsActive(data))
                return;
            float d = Math.Max(0.01f, duration);
            _active.Add(new ActiveEvent(data, d) { Remaining = Math.Max(0.01f, Math.Min(d, remaining)) });
        }

        public bool IsActive(GameEventData data)
        {
            foreach (var a in _active)
            {
                if (a.Data == data)
                    return true;
            }
            return false;
        }

        public void Tick(float deltaSeconds, IReadOnlyList<GameEventData> pool)
        {
            bool changed = _active.Count > 0;
            AdvanceActive(deltaSeconds);

            TimeUntilNext -= deltaSeconds;
            if (TimeUntilNext <= 0f)
            {
                TimeUntilNext += NextInterval();
                // 미리 뽑은 이벤트가 그사이 진행 중이 되었으면(F5 등) 다시 뽑는다
                var picked = _preRolled && _preRolledEvent != null && !IsActive(_preRolledEvent) ? _preRolledEvent : PickRandom(pool);
                if (picked != null)
                {
                    Start(picked); // 경보 해제는 시작 뒤 (운석은 미리 정한 대상을 시작 때 쓴다)
                    changed = true;
                }
                ClearPreRoll();
            }
            else if (!_preRolled && WarningLead > 0f && TimeUntilNext <= WarningLead)
            {
                _preRolled = true;
                _preRolledEvent = PickRandom(pool);
                if (_preRolledEvent != null && (WarningFilter == null || WarningFilter(_preRolledEvent)))
                {
                    Upcoming = _preRolledEvent;
                    UpcomingChanged?.Invoke(Upcoming);
                }
            }

            if (changed)
                Changed?.Invoke();
        }

        /// <summary>가중치로 하나를 골라 즉시 발생 (F5 디버그). 다음 랜덤 타이머는 건드리지 않는다.</summary>
        public GameEventData TriggerRandom(IReadOnlyList<GameEventData> pool)
        {
            var picked = PickRandom(pool);
            if (picked != null)
            {
                Start(picked);
                Changed?.Invoke();
            }
            return picked;
        }

        /// <summary>지정한 이벤트를 즉시 발생. 같은 이벤트가 진행 중이면 false.</summary>
        public bool Trigger(GameEventData data)
        {
            if (data == null || IsActive(data))
                return false;
            Start(data);
            Changed?.Invoke();
            return true;
        }

        private void Start(GameEventData data)
        {
            if (data.IsTimed)
            {
                float duration = DurationProvider != null ? DurationProvider(data) : data.Duration;
                _active.Add(new ActiveEvent(data, Math.Max(0.01f, duration)));
            }
            EventStarted?.Invoke(data);
        }

        private void AdvanceActive(float dt)
        {
            _ended.Clear();
            foreach (var a in _active)
            {
                a.Remaining -= dt;
                if (a.Remaining <= 1e-4f)
                {
                    a.Remaining = 0f;
                    _ended.Add(a);
                }
            }
            foreach (var a in _ended)
            {
                _active.Remove(a);
                EventEnded?.Invoke(a);
            }
        }

        private GameEventData PickRandom(IReadOnlyList<GameEventData> pool)
        {
            _candidates.Clear();
            float total = 0f;
            if (pool != null)
            {
                foreach (var e in pool)
                {
                    if (e == null || e.Weight <= 0f || IsActive(e))
                        continue;
                    _candidates.Add(e);
                    total += e.Weight;
                }
            }
            if (_candidates.Count == 0)
                return null;

            float roll = _random01() * total;
            foreach (var e in _candidates)
            {
                if (roll < e.Weight)
                    return e;
                roll -= e.Weight;
            }
            return _candidates[_candidates.Count - 1]; // 부동소수 오차 대비
        }

        private float NextInterval()
        {
            float multiplier = IntervalMultiplier != null ? Math.Max(0.01f, IntervalMultiplier()) : 1f;
            return (_intervalMin + _random01() * (_intervalMax - _intervalMin)) * multiplier;
        }
    }
}
