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
            if (population >= 1)
                HasEverHadPopulation = true;

            if (HasEverHadPopulation && population <= 0)
                Fail(GameOverReason.Population);
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
    }
}
