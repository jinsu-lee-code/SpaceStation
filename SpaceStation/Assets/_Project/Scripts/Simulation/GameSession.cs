using System;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 한 판의 통계와 패배 판정 (GDD 12-3).
    /// 패배: 인구가 한 번이라도 1 이상이 된 뒤 0이 되면 게임 오버 (한 번만 발생).
    /// </summary>
    public sealed class GameSession
    {
        /// <summary>게임 오버가 처음 확정될 때.</summary>
        public event Action GameOver;

        public bool HasEverHadPopulation { get; private set; }
        public bool IsGameOver { get; private set; }
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

            if (!IsGameOver && HasEverHadPopulation && population <= 0)
            {
                IsGameOver = true;
                GameOver?.Invoke();
            }
        }

        public void RecordEvent() => EventsExperienced++;
        public void RecordDestroyed() => ModulesDestroyed++;
        public void RecordSpread() => DamageSpreads++;
    }
}
