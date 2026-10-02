using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SpaceStation.Data;
using SpaceStation.Simulation;

namespace SpaceStation.Editor.Balance
{
    public sealed class BalanceRunSettings
    {
        public BalanceConfig Balance;
        public StationGradeConfig Grades;
        public ModuleData CoreModule;
        public IReadOnlyList<GameEventData> Events;
        public IReadOnlyList<ModuleData> Buildable;
        public AdjacencyRuleSet AdjacencyRules;
        /// <summary>Phase 6 연구 (null이면 연구 없음).</summary>
        public IReadOnlyList<ResearchCategoryData> ResearchCategories;
        public ResearchLevelCapConfig ResearchCaps;
        public float DurationSeconds = 1800f;
        public int Runs = 20;
        public int BaseSeed = 1;
        public float TickInterval = 1f;
        public float SampleInterval = 10f;
    }

    /// <summary>한 판(시드 1개)의 결과 지표.</summary>
    public sealed class BalanceRunResult
    {
        public int Seed;
        public bool GameOver;
        public GameOverReason Reason; // 4-10
        public float EndSeconds;
        /// <summary>등급 인덱스별 첫 도달 시간(초). 미도달은 NaN. [0]은 항상 0.</summary>
        public float[] GradeReachSeconds;
        public int FinalGrade;
        public int MaxGrade;
        public int FinalPopulation;
        public int MaxPopulation;
        public int FinalModules;
        public int ModulesBuilt;
        public int Events;
        public int ModulesDamaged;
        public int ModulesDestroyed;
        public int DamageSpreads; // 4-7
        public int MeteorsIntercepted; // 4-8
        public int MeteorsBlocked;     // 4-8
        public int Ricochets;          // 4-8 실드 튕김 명중
        public int RepairsStarted;
        public float MetalSpentOnRepairs;
        public int Maintenances;
        public int Rebuilds;
        /// <summary>Phase 6: 카테고리별 최종 연구 레벨 (ResearchCategory 순서), 시작한 연구 수.</summary>
        public int[] ResearchLevels = new int[Enum.GetValues(typeof(ResearchCategory)).Length];
        public int ResearchStarted;
        public float MetalSpentOnUpkeep;
        /// <summary>산소·물·식량·금속 최저 재고 (첫 등급 이후 전체 구간).</summary>
        public float[] MinStock = new float[4];
        /// <summary>산소·물·식량 고갈 상태였던 누적 시간(초).</summary>
        public float[] DepletedSeconds = new float[3];
        public int DepletionEvents; // 4-10
        public const float RearmStock = 20f;
        public float LowPowerSeconds;
        public float AverageEfficiency;
        public float FinalSatisfaction;
        public float MinSatisfaction;
        /// <summary>8-x: 마지막 시점 모듈 종류별 개수 (표시 이름 → 개수). 신규 모듈을 봇이 실제로 짓는지 확인용.</summary>
        public Dictionary<string, int> FinalModuleCounts = new Dictionary<string, int>();
    }

    public sealed class BalanceReport
    {
        public readonly List<BalanceRunResult> Results = new List<BalanceRunResult>();
        public readonly StringBuilder TimeSeriesCsv = new StringBuilder();
        public string[] GradeNames;
    }

    /// <summary>
    /// 측정 도구 본체: 시드마다 StationSimulation + BalanceBot으로 지정 시간을 시뮬레이션하고 지표를 모은다.
    /// </summary>
    public static class BalanceRunner
    {
        private static readonly ResourceType[] Stocks = { ResourceType.Oxygen, ResourceType.Water, ResourceType.Food, ResourceType.Metal };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static BalanceReport Run(BalanceRunSettings s, Action<int, float> progress = null)
        {
            var report = new BalanceReport();
            report.GradeNames = new string[s.Grades.Grades.Count];
            for (int i = 0; i < report.GradeNames.Length; i++)
                report.GradeNames[i] = s.Grades.Grades[i].DisplayName;

            report.TimeSeriesCsv.AppendLine("seed,time,population,housing,satisfaction,grade,modules,oxygen,water,food,metal,power_supply,power_demand,efficiency,damaged,active_events");
            for (int run = 0; run < s.Runs; run++)
            {
                progress?.Invoke(run, (float)run / s.Runs);
                report.Results.Add(RunOne(s, s.BaseSeed + run, report.TimeSeriesCsv));
            }
            progress?.Invoke(s.Runs, 1f);
            return report;
        }

        private static BalanceRunResult RunOne(BalanceRunSettings s, int seed, StringBuilder series)
        {
            var rng = new Random(seed);
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = s.Balance,
                Grades = s.Grades,
                CoreModule = s.CoreModule,
                Events = s.Events,
                AdjacencyRules = s.AdjacencyRules,
                Random01 = () => (float)rng.NextDouble(),
                ResearchCategories = s.ResearchCategories,
                ResearchCaps = s.ResearchCaps,
            });
            var bot = new BalanceBot(sim, s.Buildable);

            var result = new BalanceRunResult
            {
                Seed = seed,
                GradeReachSeconds = new float[sim.Progression.GradeCount],
            };
            for (int i = 1; i < result.GradeReachSeconds.Length; i++)
                result.GradeReachSeconds[i] = float.NaN;
            for (int i = 0; i < 4; i++)
                result.MinStock[i] = float.MaxValue;
            result.MinSatisfaction = float.MaxValue;

            int damaged = 0;
            sim.Damage.Damaged += _ => damaged++;
            sim.Progression.GradeChanged += (prev, next) =>
            {
                if (float.IsNaN(result.GradeReachSeconds[next]))
                    result.GradeReachSeconds[next] = sim.ElapsedSeconds;
            };

            float effSum = 0f;
            int ticks = 0;
            var wasDepleted = new bool[3];
            float nextSample = 0f;
            while (sim.ElapsedSeconds < s.DurationSeconds - 1e-4f)
            {
                bot.Decide();
                sim.Tick(s.TickInterval);
                ticks++;

                var r = sim.Resources;
                effSum += r.PowerEfficiency;
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
                    if (depleted && !wasDepleted[i])
                    {
                        result.DepletionEvents++;
                        wasDepleted[i] = true;
                    }
                    else if (wasDepleted[i] && r.GetStock(Stocks[i]) >= BalanceRunResult.RearmStock)
                    {
                        wasDepleted[i] = false;
                    }
                }
                result.MaxGrade = Math.Max(result.MaxGrade, sim.Progression.GradeIndex);
                result.MinSatisfaction = Math.Min(result.MinSatisfaction, sim.Population.Satisfaction);

                if (sim.ElapsedSeconds >= nextSample - 1e-4f)
                {
                    AppendSample(series, seed, sim);
                    nextSample += s.SampleInterval;
                }
                if (sim.Session.IsGameOver)
                {
                    result.GameOver = true;
                    result.Reason = sim.Session.Reason;
                    AppendSample(series, seed, sim);
                    break;
                }
            }

            result.EndSeconds = sim.ElapsedSeconds;
            result.FinalGrade = sim.Progression.GradeIndex;
            result.FinalPopulation = sim.Resources.Population;
            result.MaxPopulation = sim.Session.MaxPopulation;
            result.FinalModules = sim.Grid.ModuleCount;
            result.ModulesBuilt = bot.ModulesBuilt;
            result.Events = sim.Session.EventsExperienced;
            result.ModulesDamaged = damaged;
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
            result.AverageEfficiency = ticks > 0 ? effSum / ticks : 1f;
            result.FinalSatisfaction = sim.Population.Satisfaction;
            foreach (var m in sim.Grid.Modules)
            {
                if (m.Data == null || m == sim.Core)
                    continue;
                result.FinalModuleCounts.TryGetValue(m.Data.DisplayName, out int c);
                result.FinalModuleCounts[m.Data.DisplayName] = c + 1;
            }
            return result;
        }

        private static void AppendSample(StringBuilder sb, int seed, StationSimulation sim)
        {
            var r = sim.Resources;
            sb.Append(seed).Append(',')
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

        /// <summary>시드별 요약 + 평균 행.</summary>
        public static string BuildSummaryCsv(BalanceReport report)
        {
            var sb = new StringBuilder();
            sb.Append("seed,game_over,end_s");
            for (int i = 1; i < report.GradeNames.Length; i++)
                sb.Append(",reach_grade").Append(i).Append("_s");
            sb.AppendLine(",final_grade,max_grade,final_pop,max_pop,final_modules,modules_built,events,damaged,destroyed,spreads,intercepted,shield_deflected,ricochets,repairs,repair_metal," +
                          "maintenances,rebuilds,upkeep_metal," +
                          "min_oxygen,min_water,min_food,min_metal,oxygen_depleted_s,water_depleted_s,food_depleted_s,depletion_events," +
                          "low_power_s,avg_efficiency,final_satisfaction,min_satisfaction");

            foreach (var r in report.Results)
            {
                sb.Append(r.Seed).Append(',').Append(r.GameOver ? 1 : 0).Append(',').Append(F(r.EndSeconds));
                for (int i = 1; i < r.GradeReachSeconds.Length; i++)
                    sb.Append(',').Append(float.IsNaN(r.GradeReachSeconds[i]) ? "" : F(r.GradeReachSeconds[i]));
                sb.Append(',').Append(r.FinalGrade).Append(',').Append(r.MaxGrade)
                  .Append(',').Append(r.FinalPopulation).Append(',').Append(r.MaxPopulation)
                  .Append(',').Append(r.FinalModules).Append(',').Append(r.ModulesBuilt)
                  .Append(',').Append(r.Events).Append(',').Append(r.ModulesDamaged).Append(',').Append(r.ModulesDestroyed).Append(',').Append(r.DamageSpreads).Append(',').Append(r.MeteorsIntercepted).Append(',').Append(r.MeteorsBlocked).Append(',').Append(r.Ricochets)
                  .Append(',').Append(r.RepairsStarted).Append(',').Append(F(r.MetalSpentOnRepairs))
                  .Append(',').Append(r.Maintenances).Append(',').Append(r.Rebuilds).Append(',').Append(F(r.MetalSpentOnUpkeep));
                foreach (var v in r.MinStock) sb.Append(',').Append(F(v));
                foreach (var v in r.DepletedSeconds) sb.Append(',').Append(F(v));
                sb.Append(',').Append(r.DepletionEvents);
                sb.Append(',').Append(F(r.LowPowerSeconds)).Append(',').Append(F(r.AverageEfficiency))
                  .Append(',').Append(F(r.FinalSatisfaction)).Append(',').Append(F(r.MinSatisfaction)).AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>사람이 읽는 요약 (평균, 등급 도달률 등).</summary>
        public static string BuildSummaryText(BalanceReport report)
        {
            var results = report.Results;
            int n = results.Count;
            if (n == 0) return "결과 없음";
            var sb = new StringBuilder();
            int overs = 0;
            foreach (var r in results) if (r.GameOver) overs++;
            float overAt = 0f;
            var reasons = new Dictionary<GameOverReason, int>();
            foreach (var r in results)
            {
                if (!r.GameOver) continue;
                overAt += r.EndSeconds;
                reasons.TryGetValue(r.Reason, out var c);
                reasons[r.Reason] = c + 1;
            }
            string why = "";
            foreach (var kv in reasons) why += $" {kv.Key}:{kv.Value}";
            sb.AppendLine($"runs={n}  game_over={overs}/{n}" + (overs > 0 ? $" (평균 {overAt / overs / 60f:0.0}분,{why})" : ""));
            for (int g = 1; g < report.GradeNames.Length; g++)
            {
                int reached = 0; float sum = 0f;
                foreach (var r in results)
                {
                    if (float.IsNaN(r.GradeReachSeconds[g])) continue;
                    reached++; sum += r.GradeReachSeconds[g];
                }
                string avg = reached > 0 ? $"{sum / reached / 60f:0.0}분" : "-";
                sb.AppendLine($"{report.GradeNames[g]}: 도달 {reached}/{n}, 평균 {avg}");
            }
            sb.AppendLine($"최종 인구 평균 {Avg(results, r => r.FinalPopulation):0.0}, 최대 인구 평균 {Avg(results, r => r.MaxPopulation):0.0}, 최종 모듈 평균 {Avg(results, r => r.FinalModules):0.0}");
            sb.AppendLine($"이벤트 평균 {Avg(results, r => r.Events):0.0}, 파손 {Avg(results, r => r.ModulesDamaged):0.0}, 파괴 {Avg(results, r => r.ModulesDestroyed):0.00}, 확산 {Avg(results, r => r.DamageSpreads):0.00}, 격추 {Avg(results, r => r.MeteorsIntercepted):0.00}, 실드 빗겨냄 {Avg(results, r => r.MeteorsBlocked):0.00} (튕겨 명중 {Avg(results, r => r.Ricochets):0.00})");
            sb.AppendLine($"수리 금속 평균 {Avg(results, r => r.MetalSpentOnRepairs):0}, 정비 {Avg(results, r => r.Maintenances):0.0}회, 재건축 {Avg(results, r => r.Rebuilds):0.0}회, 유지비 금속 {Avg(results, r => r.MetalSpentOnUpkeep):0}");
            sb.AppendLine($"최저 재고 평균  산소 {Avg(results, r => r.MinStock[0]):0}  물 {Avg(results, r => r.MinStock[1]):0}  식량 {Avg(results, r => r.MinStock[2]):0}  금속 {Avg(results, r => r.MinStock[3]):0}");
            sb.AppendLine($"고갈 시간 평균(초)  산소 {Avg(results, r => r.DepletedSeconds[0]):0}  물 {Avg(results, r => r.DepletedSeconds[1]):0}  식량 {Avg(results, r => r.DepletedSeconds[2]):0}  · 고갈 횟수 {Avg(results, r => r.DepletionEvents):0.0}회");
            sb.AppendLine($"전력 효율<100% 시간 평균 {Avg(results, r => r.LowPowerSeconds):0}초, 평균 효율 {Avg(results, r => r.AverageEfficiency) * 100f:0.0}%");
            sb.AppendLine($"만족도  최종 평균 {Avg(results, r => r.FinalSatisfaction):0}, 최저 평균 {Avg(results, r => r.MinSatisfaction):0}");
            sb.Append($"연구  시작 {Avg(results, r => r.ResearchStarted):0.0}회, 최종 레벨 평균  유지보수 {Avg(results, r => r.ResearchLevels[0]):0.0}  방어 {Avg(results, r => r.ResearchLevels[1]):0.0}"
                      + $"  생산 {Avg(results, r => r.ResearchLevels[2]):0.0}  에너지 {Avg(results, r => r.ResearchLevels[3]):0.0}  거주 {Avg(results, r => r.ResearchLevels[4]):0.0}  건설 {Avg(results, r => r.ResearchLevels[5]):0.0}");
            // 모듈 구성 평균 (많은 순)
            var totals = new Dictionary<string, float>();
            foreach (var r in results)
                foreach (var kv in r.FinalModuleCounts)
                {
                    totals.TryGetValue(kv.Key, out float t);
                    totals[kv.Key] = t + kv.Value;
                }
            var names = new List<string>(totals.Keys);
            names.Sort((a, b) => totals[b].CompareTo(totals[a]));
            sb.AppendLine();
            sb.Append("모듈 구성 평균 ");
            foreach (var name in names)
                sb.Append($" {name} {totals[name] / n:0.0}");
            return sb.ToString();
        }

        private static float Avg(List<BalanceRunResult> list, Func<BalanceRunResult, float> f)
        {
            float sum = 0f;
            foreach (var r in list) sum += f(r);
            return sum / list.Count;
        }

        private static string F(float v) => v.ToString("0.###", Inv);
    }
}
