using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SpaceStation.Data;
using SpaceStation.Simulation;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 측정 한 판(시드 1개). 틱을 나눠 진행할 수 있어(<see cref="Step"/>) 일괄 실행과 백그라운드 실행(12-2)이 같은 코드를 쓴다.
    /// 시작 상태(<see cref="BalanceStartState"/>)가 있으면 그 정거장을 복원해 이어서 돌린다(구간 측정) — 시간 지표는 시작 시점부터 잰다.
    /// </summary>
    public sealed class BalanceRun
    {
        private static readonly ResourceType[] Stocks = { ResourceType.Oxygen, ResourceType.Water, ResourceType.Food, ResourceType.Metal };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly BalanceRunSettings _s;
        private readonly StationSimulation _sim;
        private readonly BalanceBot _bot;
        private readonly StringBuilder _series;
        private readonly float _t0;
        private readonly int _stopAtGrade;
        private readonly bool[] _wasDepleted = new bool[3];
        private readonly bool[] _captured;
        private float _effSum;
        private int _ticks;
        private float _moodSum;
        private int _moodTicks;
        private float _nextSample;
        private int _damaged;

        public BalanceRunResult Result { get; }
        public bool Done { get; private set; }
        /// <summary>12-2 구간 시작 만들기: 이번 판에서 캡처한 상태 (등급 오름차순).</summary>
        public List<BalanceStartState> Captured { get; } = new List<BalanceStartState>();
        /// <summary>시작 상태에서 이어 돌린 판이면 그 등급, 새 판이면 -1.</summary>
        public int StartGrade { get; }
        /// <summary>시작 상태를 복원할 때 찾지 못해 건너뛴 항목 수 (데이터가 바뀌었다는 신호).</summary>
        public int RestoreMissing { get; }
        public float Elapsed => _sim.ElapsedSeconds - _t0;

        public BalanceRun(BalanceRunSettings s, int seed, StringBuilder series, BalanceStartState start = null)
        {
            _s = s;
            _series = series;
            var rng = new Random(seed);
            var residentRng = new Random(seed * 7919 + 13); // 이름·특성 난수 분리 (시드별 고정)
            _sim = new StationSimulation(new StationSimulationSettings
            {
                Residents = s.Residents,
                ResidentRandom01 = () => (float)residentRng.NextDouble(),
                Balance = s.Balance,
                Grades = s.Grades,
                CoreModule = s.CoreModule,
                Events = s.Events,
                AdjacencyRules = s.AdjacencyRules,
                Random01 = () => (float)rng.NextDouble(),
                ResearchCategories = s.ResearchCategories,
                ResearchCaps = s.ResearchCaps,
            });
            StartGrade = -1;
            if (start != null)
            {
                RestoreMissing = StationStateSerializer.Restore(_sim, start.Station);
                StartGrade = start.Grade;
            }
            _bot = new BalanceBot(_sim, s.Buildable);
            _t0 = _sim.ElapsedSeconds;
            _nextSample = _t0;
            // 구간 측정: 다음 등급에서 멈춤. 그 밖에는 설정값 (-1 = 시간 끝까지)
            _stopAtGrade = start != null ? start.Grade + 1 : s.StopAtGrade;

            Result = new BalanceRunResult
            {
                Seed = seed,
                GradeReachSeconds = new float[_sim.Progression.GradeCount],
            };
            int reached = _sim.Progression.GradeIndex;
            for (int i = 1; i < Result.GradeReachSeconds.Length; i++)
                Result.GradeReachSeconds[i] = i <= reached ? 0f : float.NaN;
            for (int i = 0; i < 4; i++)
                Result.MinStock[i] = float.MaxValue;
            Result.MinSatisfaction = float.MaxValue;
            Result.MaxGrade = reached;

            if (s.CaptureGrades != null)
                _captured = new bool[s.CaptureGrades.Length];

            _sim.Damage.Damaged += _ => _damaged++;
            _sim.Progression.GradeChanged += (prev, next) =>
            {
                if (float.IsNaN(Result.GradeReachSeconds[next]))
                    Result.GradeReachSeconds[next] = Elapsed;
            };
        }

        /// <summary>최대 <paramref name="maxTicks"/>틱 진행. 끝나면 <see cref="Done"/>.</summary>
        public void Step(int maxTicks)
        {
            for (int n = 0; n < maxTicks && !Done; n++)
            {
                if (Elapsed >= _s.DurationSeconds - 1e-4f)
                {
                    Finish();
                    return;
                }
                TickOnce();
            }
        }

        private void TickOnce()
        {
            var s = _s;
            var sim = _sim;
            var result = Result;
            _bot.Decide();
            sim.Tick(s.TickInterval);
            _ticks++;

            var r = sim.Resources;
            _effSum += r.PowerEfficiency;
            if (r.PowerEfficiency < 1f - 1e-4f)
                result.LowPowerSeconds += s.TickInterval;
            for (int i = 0; i < 4; i++)
                result.MinStock[i] = Math.Min(result.MinStock[i], r.GetStock(Stocks[i]));
            for (int i = 0; i < 3; i++)
            {
                bool depleted = r.IsDepleted(Stocks[i]);
                if (depleted)
                    result.DepletedSeconds[i] += s.TickInterval;
                // 4-10: 고갈 "횟수" (산소·물·식량 합). 0 근처에서 깜빡이는 것을 한 번으로 세도록
                // 재고가 다시 RearmStock 이상으로 회복되어야 다음 고갈을 새로 센다.
                if (depleted && !_wasDepleted[i])
                {
                    result.DepletionEvents++;
                    _wasDepleted[i] = true;
                }
                else if (_wasDepleted[i] && r.GetStock(Stocks[i]) >= BalanceRunResult.RearmStock)
                {
                    _wasDepleted[i] = false;
                }
            }
            result.MaxGrade = Math.Max(result.MaxGrade, sim.Progression.GradeIndex);
            result.MinSatisfaction = Math.Min(result.MinSatisfaction, sim.Population.Satisfaction);
            if (sim.Residents != null)
            {
                _moodSum += sim.Residents.MoodTotal;
                _moodTicks++;
                result.MinResidentMood = Math.Min(result.MinResidentMood, sim.Residents.MoodTotal);
            }

            if (sim.ElapsedSeconds >= _nextSample - 1e-4f)
            {
                AppendSample();
                _nextSample += s.SampleInterval;
            }
            if (sim.Session.IsGameOver)
            {
                result.GameOver = true;
                result.Reason = sim.Session.Reason;
                AppendSample();
                Finish();
                return;
            }
            CaptureReachedGrades();
            if (_stopAtGrade >= 0 && sim.Progression.HighestGradeIndex >= _stopAtGrade)
                Finish();
        }

        /// <summary>12-2: 캡처할 등급에 처음 도달한 틱 직후의 상태를 저장 (한 번에 여러 등급을 넘으면 같은 상태를 각 등급으로).</summary>
        private void CaptureReachedGrades()
        {
            if (_captured == null)
                return;
            int highest = _sim.Progression.HighestGradeIndex;
            StationState state = null;
            for (int i = 0; i < _captured.Length; i++)
            {
                int grade = _s.CaptureGrades[i];
                if (_captured[i] || highest < grade)
                    continue;
                _captured[i] = true;
                state ??= StationStateSerializer.Capture(_sim);
                Captured.Add(new BalanceStartState
                {
                    Seed = Result.Seed,
                    Grade = grade,
                    GradeName = _s.Grades.Grades[grade].DisplayName,
                    ReachedAt = _sim.ElapsedSeconds,
                    Population = _sim.Resources.Population,
                    Modules = _sim.Grid.ModuleCount,
                    Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv),
                    Station = state,
                });
            }
        }

        private void Finish()
        {
            if (Done)
                return;
            Done = true;
            var sim = _sim;
            var result = Result;
            var bot = _bot;
            result.EndSeconds = Elapsed;
            result.FinalGrade = sim.Progression.GradeIndex;
            result.FinalPopulation = sim.Resources.Population;
            result.MaxPopulation = sim.Session.MaxPopulation;
            result.FinalModules = sim.Grid.ModuleCount;
            result.ModulesBuilt = bot.ModulesBuilt;
            result.Events = sim.Session.EventsExperienced;
            result.ModulesDamaged = _damaged;
            result.ModulesDestroyed = sim.Session.ModulesDestroyed;
            result.DamageSpreads = sim.Session.DamageSpreads;
            result.MeteorsIntercepted = sim.Session.MeteorsIntercepted;
            result.MeteorsBlocked = sim.Session.MeteorsBlocked;
            result.Ricochets = sim.Session.Ricochets;
            result.RepairsStarted = bot.RepairsStarted;
            result.MetalSpentOnRepairs = bot.MetalSpentOnRepairs;
            result.Maintenances = bot.Maintenances;
            result.Rebuilds = bot.Rebuilds;
            result.ResearchStarted = bot.ResearchStarted;
            foreach (var c in sim.Research.Categories)
                result.ResearchLevels[(int)c.Category] = sim.Research.GetLevel(c);
            result.MetalSpentOnUpkeep = bot.MetalSpentOnUpkeep;
            result.AverageEfficiency = _ticks > 0 ? _effSum / _ticks : 1f;
            result.FinalSatisfaction = sim.Population.Satisfaction;
            result.AverageResidentMood = _moodTicks > 0 ? _moodSum / _moodTicks : 0f;
            result.FinalResidentMood = sim.Residents != null ? sim.Residents.MoodTotal : 0f;
            if (result.MinResidentMood == float.MaxValue)
                result.MinResidentMood = 0f;
            if (result.MinSatisfaction == float.MaxValue)
                result.MinSatisfaction = sim.Population.Satisfaction;
            for (int i = 0; i < 4; i++)
                if (result.MinStock[i] == float.MaxValue)
                    result.MinStock[i] = sim.Resources.GetStock(Stocks[i]);
            foreach (var m in sim.Grid.Modules)
            {
                if (m.Data == null || m == sim.Core)
                    continue;
                result.FinalModuleCounts.TryGetValue(m.Data.DisplayName, out int c);
                result.FinalModuleCounts[m.Data.DisplayName] = c + 1;
            }
        }

        private void AppendSample()
        {
            var sim = _sim;
            var r = sim.Resources;
            _series.Append(Result.Seed).Append(',')
              .Append(F(sim.ElapsedSeconds)).Append(',')
              .Append(r.Population).Append(',')
              .Append(r.HousingCapacity).Append(',')
              .Append(F(sim.Population.Satisfaction)).Append(',')
              .Append(sim.Progression.GradeIndex).Append(',')
              .Append(sim.Grid.ModuleCount).Append(',')
              .Append(F(r.GetStock(ResourceType.Oxygen))).Append(',')
              .Append(F(r.GetStock(ResourceType.Water))).Append(',')
              .Append(F(r.GetStock(ResourceType.Food))).Append(',')
              .Append(F(r.GetStock(ResourceType.Metal))).Append(',')
              .Append(F(r.PowerSupply)).Append(',')
              .Append(F(r.PowerDemand)).Append(',')
              .Append(F(r.PowerEfficiency)).Append(',')
              .Append(sim.Damage.DamagedCount).Append(',')
              .Append(sim.Events.ActiveEvents.Count).AppendLine();
        }

        private static string F(float v) => v.ToString("0.###", Inv);
    }
}
