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
    /// 13-8 측정 확장(<see cref="IBalanceExtension"/>)이 있으면 봇 결정 뒤 확장이 결정하고, 게임 오버 때 확장이 준 상태로 새 시뮬레이션을 만들어 이어 간다
    /// (되돌아간 시간도 <see cref="Elapsed"/>에 넣음). 확장이 없으면 예전과 같음 (같은 시드 = 같은 결과).
    /// </summary>
    public sealed class BalanceRun
    {
        private static readonly ResourceType[] Stocks = { ResourceType.Oxygen, ResourceType.Water, ResourceType.Food, ResourceType.Metal };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly BalanceRunSettings _s;
        private StationSimulation _sim;
        private BalanceBot _bot;
        private BalanceMilestones _milestones;
        private readonly BalanceBotPersona _persona;
        private readonly IBalanceExtension _ext;
        private readonly int _seed;
        private float _lost; // 13-8 다시 시작으로 되돌아간 게임 시간
        private BotTotals _carry; // 13-8 다시 시작 전 봇들이 쌓은 수
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
        private UnlockChoiceSystem _choices;
        private readonly Func<int, int> _pickChoice;

        public BalanceRunResult Result { get; }
        public bool Done { get; private set; }
        /// <summary>12-2 구간 시작 만들기: 이번 판에서 캡처한 상태 (등급 오름차순).</summary>
        public List<BalanceStartState> Captured { get; } = new List<BalanceStartState>();
        /// <summary>시작 상태에서 이어 돌린 판이면 그 등급, 새 판이면 -1.</summary>
        public int StartGrade { get; }
        /// <summary>시작 상태를 복원할 때 찾지 못해 건너뛴 항목 수 (데이터가 바뀌었다는 신호).</summary>
        public int RestoreMissing { get; }
        public float Elapsed => _sim.ElapsedSeconds - _t0 + _lost;

        public BalanceRun(BalanceRunSettings s, int seed, StringBuilder series, BalanceStartState start = null)
        {
            _s = s;
            _series = series;
            _seed = seed;
            _ext = s.Extension?.Invoke();
            _sim = CreateSim(seed, 0);
            if (s.UnlockChoices != null)
            {
                // 해금 선택 난수 (시드별 고정). 가까운 시드의 System.Random은 처음 값들이 서로 닮아 모든 판이 같은 쪽을 골랐음 → 시드 · 순번을 섞는 해시
                int picks = 0;
                _pickChoice = n => (int)(Mix((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)(++picks)) % (ulong)n);
            }
            StartGrade = -1;
            if (start != null)
            {
                RestoreMissing = StationStateSerializer.Restore(_sim, start.Station);
                StartGrade = start.Grade;
            }
            var persona = BalanceBot.Persona ? BalanceBotPersona.FromSeed(seed) : BalanceBotPersona.Neutral; // 13-7 시드에 묶인 봇 성향
            _persona = persona;
            _bot = new BalanceBot(_sim, s.Buildable, persona);
            _milestones = new BalanceMilestones(_sim, s.Buildable, seed); // 13-7 새 일 간격 · 위기 빈도 · 여유 자원
            _t0 = _sim.ElapsedSeconds;
            _nextSample = _t0;
            // 구간 측정: 다음 등급에서 멈춤. 그 밖에는 설정값 (-1 = 시간 끝까지)
            _stopAtGrade = start != null ? start.Grade + 1 : s.StopAtGrade;

            Result = new BalanceRunResult
            {
                Seed = seed,
                GradeReachSeconds = new float[_sim.Progression.GradeCount],
                Persona = BalanceBot.Persona ? persona.Describe() : "",
                PersonaGrowth = persona.Growth,
                PersonaSaving = persona.Saving,
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

            Wire();
        }

        /// <summary>
        /// 시뮬레이션 만들기 (attempt = 13-8 다시 시작 횟수 — 0이면 예전과 같은 난수 씨앗). 확장이 있으면 만들기 전 설정 · 만든 뒤 시스템 붙이기.
        /// </summary>
        private StationSimulation CreateSim(int seed, int attempt)
        {
            var rng = new Random(seed + attempt * 1000003);
            var residentRng = new Random(seed * 7919 + 13 + attempt * 31); // 이름·특성 난수 분리 (시드별 고정)
            var hazardRng = new Random(seed * 104729 + 7 + attempt * 17); // 12-3c 등급 위기 난수 분리 (시드별 고정)
            var s = _s;
            var settings = new StationSimulationSettings
            {
                Residents = s.Residents,
                ResidentRandom01 = () => (float)residentRng.NextDouble(),
                HazardRandom01 = () => (float)hazardRng.NextDouble(),
                Balance = s.Balance,
                Grades = s.Grades,
                CoreModule = s.CoreModule,
                Events = s.Events,
                AdjacencyRules = s.AdjacencyRules,
                Random01 = () => (float)rng.NextDouble(),
                ResearchCategories = s.ResearchCategories,
                ResearchCaps = s.ResearchCaps,
                Recipes = s.Recipes != null && s.Recipes.Count > 0 ? s.Recipes : null,
                ResearchExpansion = s.ResearchExpansion,
                CrateSeed = BalanceBotPersona.CrateSeedFor(seed) + attempt, // 13-7 보급 상자 · 내부 결과도 시드 고정 (같은 시드 = 같은 결과)
            };
            _ext?.Configure(settings, seed);
            var sim = new StationSimulation(settings);
            // 엔드리스(타이쿤판) 시스템 — 다시 시작한 시뮬레이션에도 붙음 (복원 전에)
            if (s.UnlockChoices != null)
                _choices = UnlockChoiceSystem.AttachTo(sim, s.UnlockChoices); // 13-1
            if (s.GrowthThreat != null)
                GrowthThreatSystem.AttachTo(sim, s.GrowthThreat, s.BaseGrades); // 13-2 (13-7 난이도 간격 배율)
            if (s.InteriorBonus != null)
                InteriorBonusSystem.AttachTo(sim, s.InteriorBonus); // 13-6
            if (s.MidGrade != null)
                MidGradeSystem.AttachTo(sim, s.MidGrade); // 13-7
            _ext?.Attach(sim, seed);
            return sim;
        }

        private void Wire()
        {
            _sim.Damage.Damaged += _ => _damaged++;
            _sim.Progression.GradeChanged += (prev, next) =>
            {
                if (float.IsNaN(Result.GradeReachSeconds[next]))
                    Result.GradeReachSeconds[next] = Elapsed;
            };
        }

        /// <summary>SplitMix64 섞기 (13-1 해금 선택 난수).</summary>
        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>13-8 게임 오버 → 확장이 준 상태로 새 시뮬레이션 · 새 봇 (같은 성향), 되돌아간 시간은 <see cref="Elapsed"/>에 더함. 13-7 새 일 지표는 첫 시도만.</summary>
        private void Retry(StationState state)
        {
            Result.Retries++;
            _lost += _sim.ElapsedSeconds - state.Elapsed;
            _carry.Add(_bot);
            _sim = CreateSim(_seed, Result.Retries);
            StationStateSerializer.Restore(_sim, state);
            _bot = new BalanceBot(_sim, _s.Buildable, _persona);
            _milestones = null;
            Array.Clear(_wasDepleted, 0, _wasDepleted.Length);
            Wire();
        }

        /// <summary>다시 시작 전 봇들이 쌓은 수 (결과는 모든 시도의 합).</summary>
        private struct BotTotals
        {
            public int ModulesBuilt, RepairsStarted, Maintenances, Rebuilds, ResearchStarted;
            public float MetalSpentOnRepairs, MetalSpentOnUpkeep;

            public void Add(BalanceBot bot)
            {
                ModulesBuilt += bot.ModulesBuilt;
                RepairsStarted += bot.RepairsStarted;
                Maintenances += bot.Maintenances;
                Rebuilds += bot.Rebuilds;
                ResearchStarted += bot.ResearchStarted;
                MetalSpentOnRepairs += bot.MetalSpentOnRepairs;
                MetalSpentOnUpkeep += bot.MetalSpentOnUpkeep;
            }
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
            while (_choices != null && _choices.Pending)
            {
                int grade = _choices.OfferGrade;
                int pick = _pickChoice(_choices.Offer.Count);
                result.Choices.Add($"{grade}:{_choices.Offer[pick].Option.Key}");
                _choices.Choose(pick);
            }
            _bot.Decide();
            _ext?.Decide(_bot, Elapsed);
            sim.Tick(s.TickInterval);
            _milestones?.Tick(Elapsed, s.TickInterval);
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
                AppendSample();
                var retry = _ext?.RetryState(); // 13-8 확장이 다시 시작할 상태를 주면 이어 감
                if (retry != null)
                {
                    Retry(retry);
                    return;
                }
                result.GameOver = true;
                result.Reason = sim.Session.Reason;
                Finish();
                return;
            }
            if (_ext != null && _ext.Finished)
            {
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
            result.ModulesBuilt = bot.ModulesBuilt + _carry.ModulesBuilt;
            result.Events = sim.Session.EventsExperienced;
            result.ModulesDamaged = _damaged;
            result.ModulesDestroyed = sim.Session.ModulesDestroyed;
            result.DamageSpreads = sim.Session.DamageSpreads;
            result.MeteorsIntercepted = sim.Session.MeteorsIntercepted;
            result.MeteorsBlocked = sim.Session.MeteorsBlocked;
            result.Ricochets = sim.Session.Ricochets;
            result.RepairsStarted = bot.RepairsStarted + _carry.RepairsStarted;
            result.MetalSpentOnRepairs = bot.MetalSpentOnRepairs + _carry.MetalSpentOnRepairs;
            result.Maintenances = bot.Maintenances + _carry.Maintenances;
            result.Rebuilds = bot.Rebuilds + _carry.Rebuilds;
            result.ResearchStarted = bot.ResearchStarted + _carry.ResearchStarted;
            foreach (var c in sim.Research.Categories)
                result.ResearchLevels[(int)c.Category] = sim.Research.GetLevel(c);
            result.MetalSpentOnUpkeep = bot.MetalSpentOnUpkeep + _carry.MetalSpentOnUpkeep;
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
            _ext?.Finish(result);
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
