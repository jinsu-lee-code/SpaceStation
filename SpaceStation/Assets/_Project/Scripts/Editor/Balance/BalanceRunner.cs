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
        public int RepairsStarted;
        public float MetalSpentOnRepairs;
        /// <summary>산소·물·식량·금속 최저 재고 (첫 등급 이후 전체 구간).</summary>
        public float[] MinStock = new float[4];
        /// <summary>산소·물·식량 고갈 상태였던 누적 시간(초).</summary>
        public float[] DepletedSeconds = new float[3];
        public float LowPowerSeconds;
        public float AverageEfficiency;
        public float FinalSatisfaction;
        public float MinSatisfaction;
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
                Random01 = () => (float)rng.NextDouble(),
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
                    if (r.IsDepleted(Stocks[i]))
                        result.DepletedSeconds[i] += s.TickInterval;
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
            result.RepairsStarted = bot.RepairsStarted;
            result.MetalSpentOnRepairs = bot.MetalSpentOnRepairs;
            result.AverageEfficiency = ticks > 0 ? effSum / ticks : 1f;
            result.FinalSatisfaction = sim.Population.Satisfaction;
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
            sb.AppendLine(",final_grade,max_grade,final_pop,max_pop,final_modules,modules_built,events,damaged,destroyed,repairs,repair_metal," +
                          "min_oxygen,min_water,min_food,min_metal,oxygen_depleted_s,water_depleted_s,food_depleted_s," +
                          "low_power_s,avg_efficiency,final_satisfaction,min_satisfaction");

            foreach (var r in report.Results)
            {
                sb.Append(r.Seed).Append(',').Append(r.GameOver ? 1 : 0).Append(',').Append(F(r.EndSeconds));
                for (int i = 1; i < r.GradeReachSeconds.Length; i++)
                    sb.Append(',').Append(float.IsNaN(r.GradeReachSeconds[i]) ? "" : F(r.GradeReachSeconds[i]));
                sb.Append(',').Append(r.FinalGrade).Append(',').Append(r.MaxGrade)
                  .Append(',').Append(r.FinalPopulation).Append(',').Append(r.MaxPopulation)
                  .Append(',').Append(r.FinalModules).Append(',').Append(r.ModulesBuilt)
                  .Append(',').Append(r.Events).Append(',').Append(r.ModulesDamaged).Append(',').Append(r.ModulesDestroyed)
                  .Append(',').Append(r.RepairsStarted).Append(',').Append(F(r.MetalSpentOnRepairs));
                foreach (var v in r.MinStock) sb.Append(',').Append(F(v));
                foreach (var v in r.DepletedSeconds) sb.Append(',').Append(F(v));
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
            sb.AppendLine($"runs={n}  game_over={overs}/{n}");
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
            sb.AppendLine($"이벤트 평균 {Avg(results, r => r.Events):0.0}, 파손 {Avg(results, r => r.ModulesDamaged):0.0}, 파괴 {Avg(results, r => r.ModulesDestroyed):0.00}");
            sb.AppendLine($"최저 재고 평균  산소 {Avg(results, r => r.MinStock[0]):0}  물 {Avg(results, r => r.MinStock[1]):0}  식량 {Avg(results, r => r.MinStock[2]):0}  금속 {Avg(results, r => r.MinStock[3]):0}");
            sb.AppendLine($"고갈 시간 평균(초)  산소 {Avg(results, r => r.DepletedSeconds[0]):0}  물 {Avg(results, r => r.DepletedSeconds[1]):0}  식량 {Avg(results, r => r.DepletedSeconds[2]):0}");
            sb.AppendLine($"전력 효율<100% 시간 평균 {Avg(results, r => r.LowPowerSeconds):0}초, 평균 효율 {Avg(results, r => r.AverageEfficiency) * 100f:0.0}%");
            sb.Append($"만족도  최종 평균 {Avg(results, r => r.FinalSatisfaction):0}, 최저 평균 {Avg(results, r => r.MinSatisfaction):0}");
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
