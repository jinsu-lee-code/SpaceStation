using System;

namespace SpaceStation.Simulation
{
    public enum GameOverReason
    {
        None,
        /// <summary>인구 0.</summary>
        Population,
        /// <summary>산소 고갈이 일정 시간 지속 (4-10).</summary>
        Oxygen,
        /// <summary>만족도 0이 일정 시간 지속 (4-10).</summary>
        Satisfaction,
        /// <summary>코어에 맞닿은 모듈이 모두 파손된 채 일정 시간 지속 (4-10).</summary>
        CoreCollapse,
    }

    /// <summary>
    /// 한 판의 통계와 패배 판정 (GDD 12-3).
    /// 패배: 인구가 한 번이라도 1 이상이 된 뒤 0이 되면, 또는 <see cref="FailureMonitor"/>의 실패 조건 (한 번만 발생).
    /// </summary>
    public sealed class GameSession
    {
        /// <summary>게임 오버가 처음 확정될 때.</summary>
        public event Action GameOver;

        public bool HasEverHadPopulation { get; private set; }
        public bool IsGameOver { get; private set; }
        public GameOverReason Reason { get; private set; }

        public void Fail(GameOverReason reason)
        {
            if (IsGameOver || reason == GameOverReason.None)
                return;
            IsGameOver = true;
            Reason = reason;
            GameOver?.Invoke();
        }
        public int MaxPopulation { get; private set; }
        public int EventsExperienced { get; private set; }
        public int ModulesDestroyed { get; private set; }
        /// <summary>파손이 번진 횟수 (4-7).</summary>
        public int DamageSpreads { get; private set; }

        public void ObservePopulation(int population)
        {
            if (population > MaxPopulation)
                MaxPopulation = population;
            if (population > _segmentMaxPopulation)
                _segmentMaxPopulation = population;
            if (population >= 1)
                HasEverHadPopulation = true;

            if (HasEverHadPopulation && population <= 0)
                Fail(GameOverReason.Population);
        }

        /// <summary>세이브 복원: 통계 (게임 오버 상태는 저장하지 않으므로 복원하지 않음).</summary>
        internal void Restore(bool hasEverHadPopulation, int maxPopulation, int events, int destroyed, int spreads,
            int intercepted, int blocked, int ricochets)
        {
            HasEverHadPopulation = hasEverHadPopulation;
            MaxPopulation = maxPopulation;
            EventsExperienced = events;
            ModulesDestroyed = destroyed;
            DamageSpreads = spreads;
            MeteorsIntercepted = intercepted;
            MeteorsBlocked = blocked;
            Ricochets = ricochets;
        }

        public void RecordEvent() => EventsExperienced++;
        public void RecordDestroyed() => ModulesDestroyed++;
        public void RecordSpread() => DamageSpreads++;
        /// <summary>포탑이 격추한 운석 수 (4-8).</summary>
        public int MeteorsIntercepted { get; private set; }
        public void RecordIntercepted(int count) => MeteorsIntercepted += count;
        /// <summary>실드가 막은 운석 수 (4-8).</summary>
        public int MeteorsBlocked { get; private set; }
        public void RecordShieldBlocked(int count) => MeteorsBlocked += count;
        /// <summary>실드가 빗겨낸 운석이 다른 모듈에 맞은 수 (4-8).</summary>
        public int Ricochets { get; private set; }
        public void RecordRicochet(int count) => Ricochets += count;
        /// <summary>12-3: 끝난 합성 수.</summary>
        public int Syntheses { get; private set; }
        public void RecordSynthesis() => Syntheses++;

        /// <summary>세이브 복원: 합성 수 (12-3 전 세이브는 0).</summary>
        internal void RestoreSyntheses(int count) => Syntheses = Math.Max(0, count);

        // ---------------- 12-1 구간 기록 (직전 결과 화면 이후) ----------------

        private float _segmentStartSeconds;
        private int _segmentStartModules;
        private int _segmentStartEvents;
        private int _segmentStartDestroyed;
        private int _segmentStartSyntheses;
        private int _segmentMaxPopulation;

        /// <summary>마지막으로 끝난 구간 (결과 화면이 읽음). 아직 없으면 null.</summary>
        public SegmentRecord LastSegment { get; private set; }

        /// <summary>결과 화면 등급 도달: 지금까지의 구간을 마감하고 새 구간을 시작한다.</summary>
        public SegmentRecord CloseSegment(float elapsed, int modules, int population)
        {
            ObservePopulation(population);
            LastSegment = new SegmentRecord
            {
                Seconds = Math.Max(0f, elapsed - _segmentStartSeconds),
                MaxPopulation = _segmentMaxPopulation,
                ModulesAdded = modules - _segmentStartModules,
                Events = EventsExperienced - _segmentStartEvents,
                Destroyed = ModulesDestroyed - _segmentStartDestroyed,
                Syntheses = Syntheses - _segmentStartSyntheses,
            };
            _segmentStartSeconds = elapsed;
            _segmentStartModules = modules;
            _segmentStartEvents = EventsExperienced;
            _segmentStartDestroyed = ModulesDestroyed;
            _segmentStartSyntheses = Syntheses;
            _segmentMaxPopulation = population;
            return LastSegment;
        }

        /// <summary>세이브용 구간 시작 값.</summary>
        public SegmentStart CaptureSegment() => new SegmentStart
        {
            Seconds = _segmentStartSeconds,
            Modules = _segmentStartModules,
            Events = _segmentStartEvents,
            Destroyed = _segmentStartDestroyed,
            Syntheses = _segmentStartSyntheses,
            MaxPopulation = _segmentMaxPopulation,
        };

        /// <summary>세이브 복원: 구간 시작 값 (이전 세이브는 0 = 처음부터 한 구간).</summary>
        internal void RestoreSegment(SegmentStart s)
        {
            _segmentStartSeconds = s.Seconds;
            _segmentStartModules = s.Modules;
            _segmentStartEvents = s.Events;
            _segmentStartDestroyed = s.Destroyed;
            _segmentStartSyntheses = Math.Max(0, s.Syntheses);
            _segmentMaxPopulation = Math.Max(s.MaxPopulation, 0);
        }
    }

    /// <summary>12-1: 한 구간 기록 (직전 결과 화면 ~ 이번 결과 화면).</summary>
    public sealed class SegmentRecord
    {
        public float Seconds;
        public int MaxPopulation;
        /// <summary>구간 동안 늘어난 모듈 수 (줄었으면 음수).</summary>
        public int ModulesAdded;
        public int Events;
        public int Destroyed;
        /// <summary>12-3: 구간 동안 끝난 합성 수.</summary>
        public int Syntheses;
    }

    /// <summary>12-1: 진행 중인 구간의 시작 값 (세이브).</summary>
    [Serializable]
    public sealed class SegmentStart
    {
        public float Seconds;
        public int Modules;
        public int Events;
        public int Destroyed;
        public int MaxPopulation;
        /// <summary>12-3 (이전 세이브는 0).</summary>
        public int Syntheses;
    }
}
