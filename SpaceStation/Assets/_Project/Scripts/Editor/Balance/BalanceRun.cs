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
        private readonly BalanceMilestones _milestones;
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
        private readonly UnlockChoiceSystem _choices;
        private readonly Func<int, int> _pickChoice;
        // 제자리 봇 (CONNECT_1121 0번, 2026-11-26)
        private readonly int _holdGrade;
        private float _holdStart = float.NaN;
        private float _holdReach = float.NaN;
        private float _stableSince = float.NaN;
        private const float HoldStableSeconds = 60f;
        private const float HoldSettleMaxSeconds = 600f;
        private int _holdTicks;
        private float _holdIncomeSum, _holdNetSum;
        private int _holdDamaged0, _holdDestroyed0;
        private float _holdRepair0, _holdUpkeep0;
        public bool Holding => !float.IsNaN(_holdStart);

        public BalanceRunResult Result { get; }
        public bool Done { get; private set; }
        /// <summary>12-2 구간 시작 만들기: 이번 판에서 캡처한 상태 (등급 오름차순).</summary>
        public List<BalanceStartState> Captured { get; } = new List<BalanceStartState>();
        /// <summary>시작 상태에서 이어 돌린 판이면 그 등급, 새 판이면 -1.</summary>
        public int StartGrade { get; }
        /// <summary>시작 상태를 복원할 때 찾지 못해 건너뛴 항목 수 (데이터가 바뀌었다는 신호).</summary>
        public int RestoreMissing { get; }
        public float Elapsed => _sim.ElapsedSeconds - _t0;
        /// <summary>진단용 (봇 선택 추적 — 한 판을 직접 돌리며 <see cref="BalanceBot.LastNeed"/> · 지은 모듈을 볼 때).</summary>
        public BalanceBot Bot => _bot;
        public StationSimulation Simulation => _sim;

        public BalanceRun(BalanceRunSettings s, int seed, StringBuilder series, BalanceStartState start = null, int holdGrade = -1)
        {
            _s = s;
            _series = series;
            var rng = new Random(seed);
            var residentRng = new Random(seed * 7919 + 13); // 이름·특성 난수 분리 (시드별 고정)
            var hazardRng = new Random(seed * 104729 + 7); // 12-3c 등급 위기 난수 분리 (시드별 고정)
            _sim = new StationSimulation(new StationSimulationSettings
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
                CrateSeed = BalanceBotPersona.CrateSeedFor(seed), // 13-7 보급 상자 · 내부 결과도 시드 고정 (같은 시드 = 같은 결과)
            });
            if (s.UnlockChoices != null)
            {
                _choices = UnlockChoiceSystem.AttachTo(_sim, s.UnlockChoices); // 13-1 (복원 전에)
                // 해금 선택 난수 (시드별 고정). 가까운 시드의 System.Random은 처음 값들이 서로 닮아 모든 판이 같은 쪽을 골랐음 → 시드 · 순번을 섞는 해시
                int picks = 0;
                _pickChoice = n => (int)(Mix((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)(++picks)) % (ulong)n);
            }
            if (s.GrowthThreat != null)
                GrowthThreatSystem.AttachTo(_sim, s.GrowthThreat, s.BaseGrades); // 13-2 (13-7 난이도 간격 배율)
            if (s.InteriorBonus != null)
                InteriorBonusSystem.AttachTo(_sim, s.InteriorBonus); // 13-6
            if (s.MidGrade != null)
                MidGradeSystem.AttachTo(_sim, s.MidGrade); // 13-7
            if (s.OreVeins != null)
            {
                var veinRng = new Random(seed * 7349 + 11); // CONNECT 1번 광맥 난수 분리 (시드별 고정 — 다른 난수 순서를 건드리지 않게)
                OreVeinSystem.AttachTo(_sim, s.OreVeins, () => (float)veinRng.NextDouble());
            }
            if (s.HeatPower != null && _sim.Heat != null)
                _sim.Heat.Comfort = s.HeatPower; // CONNECT A10 온기 · A11 과부하
            if (s.HeatPower != null)
                EndlessHomeComfort.AttachTo(_sim, s.HeatPower); // CONNECT 7번 ① 방사선 힘든 주민
            if (s.Corruption != null)
                CorruptionSystem.AttachTo(_sim, s.Corruption, () => 1f); // CONNECT 7번 ② 파업 연결만 (난수 1 = 비리는 안 일어남)
            StartGrade = -1;
            if (start != null)
            {
                RestoreMissing = StationStateSerializer.Restore(_sim, start.Station);
                StartGrade = start.Grade;
            }
            var persona = BalanceBot.Persona && !s.NeutralPersona ? BalanceBotPersona.FromSeed(seed) : BalanceBotPersona.Neutral; // 13-7 시드에 묶인 봇 성향
            _bot = new BalanceBot(_sim, s.Buildable, persona) { IgnoreAdjacency = s.IgnoreAdjacency };
            _milestones = new BalanceMilestones(_sim, s.Buildable, seed); // 13-7 새 일 간격 · 위기 빈도 · 여유 자원
            _t0 = _sim.ElapsedSeconds;
            _nextSample = _t0;
            // 구간 측정: 다음 등급에서 멈춤. 그 밖에는 설정값 (-1 = 시간 끝까지)
            _stopAtGrade = start != null ? start.Grade + 1 : s.StopAtGrade;
            _holdGrade = holdGrade; // 제자리 봇 (CONNECT_1121 0번): 이 등급에 오르면 확장을 멈추고 HoldSeconds 동안

            Result = new BalanceRunResult
            {
                Seed = seed,
                GradeReachSeconds = new float[_sim.Progression.GradeCount],
                Persona = BalanceBot.Persona ? persona.Describe() : "",
                PersonaGrowth = persona.Growth,
                PersonaSaving = persona.Saving,
                HoldGrade = holdGrade,
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

        /// <summary>SplitMix64 섞기 (13-1 해금 선택 난수).</summary>
        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>최대 <paramref name="maxTicks"/>틱 진행. 끝나면 <see cref="Done"/>.</summary>
        public void Step(int maxTicks)
        {
            for (int n = 0; n < maxTicks && !Done; n++)
            {
                // 제자리 봇: 멈춘 뒤에는 멈춘 때부터 HoldSeconds까지 (전체 상한 대신), 멈추기 전에는 전체 상한 = 그 등급에 못 감
                if (Holding ? _sim.ElapsedSeconds - _holdStart >= _s.HoldSeconds - 1e-4f : Elapsed >= _s.DurationSeconds - 1e-4f)
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
            sim.Tick(s.TickInterval);
            _milestones.Tick(Elapsed, s.TickInterval);
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
            TickHold();
            if (_stopAtGrade >= 0 && sim.Progression.HighestGradeIndex >= _stopAtGrade)
                Finish();
        }

        /// <summary>제자리 봇: 멈출 등급에 처음 오른 틱에 칸 수 상한을 걸고, 그 뒤 지표를 모은다.</summary>
        private void TickHold()
        {
            if (_holdGrade < 0)
                return;
            var sim = _sim;
            var result = Result;
            var r = sim.Resources;
            if (!Holding)
            {
                if (sim.Progression.HighestGradeIndex < _holdGrade)
                    return;
                // 사용자 결정 2026-11-26 "안정된 뒤 고정": 오른 뒤 생존 자원 · 밤 전력이 1분 이어서 안정되면 고정 (최대 10분 — 그때는 그대로 고정)
                // 첫 측정: 오른 순간 인구가 막 늘어 밤 전력이 모자란 채로 고정되면 몇 분 만에 무너짐 (중형 시드 2)
                if (float.IsNaN(_holdReach))
                {
                    _holdReach = sim.ElapsedSeconds;
                    result.HoldReachSeconds = Elapsed;
                }
                if (_bot.HoldStable())
                {
                    if (float.IsNaN(_stableSince))
                        _stableSince = sim.ElapsedSeconds;
                }
                else
                {
                    _stableSince = float.NaN;
                }
                bool settled = !float.IsNaN(_stableSince) && sim.ElapsedSeconds - _stableSince >= HoldStableSeconds;
                if (!settled && sim.ElapsedSeconds - _holdReach < HoldSettleMaxSeconds)
                    return;
                result.HoldSettled = settled;
                _holdStart = sim.ElapsedSeconds;
                _bot.StartHold();
                result.HoldStartSeconds = Elapsed;
                result.HoldCells = _bot.HoldCellCap;
                result.HoldStartPopulation = r.Population;
                result.HoldStartHousing = r.HousingCapacity;
                result.HoldStartModules = sim.Grid.ModuleCount;
                result.HoldMinPopulation = r.Population;
                result.HoldMinGrade = sim.Progression.GradeIndex;
                _holdDamaged0 = _damaged;
                _holdDestroyed0 = sim.Session.ModulesDestroyed;
                _holdRepair0 = _bot.MetalSpentOnRepairs;
                _holdUpkeep0 = _bot.MetalSpentOnUpkeep;
                return;
            }
            _holdTicks++;
            if (_bot.LastNeed.StartsWith("hold")) // 지을 것이 있는데 칸 수 상한에 막힌 틱
                result.HoldBlockedSeconds += _s.TickInterval;
            _holdIncomeSum += r.GetProduction(ResourceType.Metal);
            _holdNetSum += r.GetNetRate(ResourceType.Metal);
            if (r.GetStock(ResourceType.Metal) < BalanceRunResult.HoldLowMetal)
                result.HoldLowMetalSeconds += _s.TickInterval;
            result.HoldMinPopulation = Math.Min(result.HoldMinPopulation, r.Population);
            result.HoldMinGrade = Math.Min(result.HoldMinGrade, sim.Progression.GradeIndex);
        }

        private void FinishHold()
        {
            var result = Result;
            if (!Holding)
                return;
            float minutes = Math.Max(1e-3f, (_sim.ElapsedSeconds - _holdStart) / 60f);
            result.HoldSeconds = _sim.ElapsedSeconds - _holdStart;
            result.HoldEndCells = _bot.OccupiedCells();
            result.HoldDemolished = _bot.HoldDemolished;
            result.HoldBlocked = _bot.HoldBlocked;
            result.HoldCrisisDemolished = _bot.HoldCrisisDemolished;
            result.HoldHousingTrimmed = _bot.HoldHousingTrimmed;
            result.HoldEvents.AddRange(_bot.HoldEvents);
            foreach (var kv in _bot.HoldDemolishedKinds)
                result.HoldDemolishedKinds[kv.Key] = kv.Value;
            result.HoldMetalIncome = _holdTicks > 0 ? _holdIncomeSum / _holdTicks : 0f;
            result.HoldMetalNet = _holdTicks > 0 ? _holdNetSum / _holdTicks : 0f;
            result.HoldRepairMetalPerMin = (_bot.MetalSpentOnRepairs - _holdRepair0) / minutes;
            result.HoldUpkeepMetalPerMin = (_bot.MetalSpentOnUpkeep - _holdUpkeep0) / minutes;
            result.HoldDamaged = _damaged - _holdDamaged0;
            result.HoldDestroyed = _sim.Session.ModulesDestroyed - _holdDestroyed0;
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
            FinishHold();
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
            var veins = sim.GetSystem<OreVeinSystem>(); // CONNECT_1121 1번 광맥
            if (veins != null)
            {
                result.VeinRounds = veins.Round;
                result.VeinsExhausted = veins.ExhaustedCount;
                result.VeinFrames = bot.VeinFrames;
                result.VeinDocksBuilt = bot.VeinDocksBuilt;
                result.VeinDocksMoved = bot.VeinDocksMoved;
                result.VeinReconnects = bot.VeinReconnects;
                result.VeinFramesFolded = bot.VeinFramesFolded;
                foreach (var m in sim.Grid.Modules)
                    if (m.Data != null && m.Data.name == "MD_StructureFrame")
                        result.VeinFramesFinal++;
                veins.DockSummary(out result.VeinDocksFinal, out result.InPlaceScaleFinal);
            }
            var heat = sim.Heat;
            if (heat != null && heat.Comfort != null) // CONNECT ⑤ 끝 상태
            {
                foreach (var m in sim.Grid.Modules)
                {
                    if (heat.PreferenceOf(m) == HeatPreference.Warm)
                        result.WarmModules++;
                    if (heat.IsWarm(m))
                        result.WarmInBand++;
                    if (heat.IsOverheated(m))
                        result.Overheated++;
                    if (heat.IsOverloaded(m))
                        result.Overloaded++;
                }
            }
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
