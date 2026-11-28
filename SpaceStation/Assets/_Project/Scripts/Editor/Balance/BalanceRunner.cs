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
        /// <summary>12-4 합성 레시피 · 연구 확장 (null이면 없음 — 봇은 도시급부터 레시피 해금 연구 · 합성).</summary>
        public IReadOnlyList<RecipeData> Recipes;
        public ResearchExpansionConfig ResearchExpansion;
        /// <summary>13-1 타이쿤판 해금 선택 (null이면 등급 숫자). 봇은 시드로 무작위로 고른다 (사용자 결정 2026-10-15).</summary>
        public UnlockChoiceConfig UnlockChoices;
        /// <summary>13-2 타이쿤판 성장 비례 위협 (null이면 등급 표).</summary>
        public GrowthThreatConfig GrowthThreat;
        /// <summary>CONNECT_1121 1번 타이쿤판 광맥 (null이면 없음 — 제자리 도킹이 끝없이 냄).</summary>
        public OreVeinConfig OreVeins;
        /// <summary>CONNECT_1121 A10 · A11 타이쿤판 열 온기 · 과부하 (null이면 예전 열).</summary>
        public HeatPowerConfig HeatPower;
        /// <summary>CONNECT_1121 7번 ② 비리 (null이면 없음) — 봇 판은 파업 연결(소비 부족 → 파업 기준)만, 비리 사건은 굴리지 않음 (역병처럼 선택지 사건은 봇 밖).</summary>
        public CorruptionConfig Corruption;
        /// <summary>13-7 난이도를 적용하기 전 등급표 (난이도 측정에서 성장 곡선에 간격 배율을 곱할 때, null이면 곱하지 않음).</summary>
        public StationGradeConfig BaseGrades;
        /// <summary>13-6 타이쿤판 1인칭 가볍게 (null이면 없음) — 봇은 상자를 직접 줍지만 등급 보상 포인트 · 자동 회수가 붙음.</summary>
        public InteriorBonusConfig InteriorBonus;
        /// <summary>13-7 타이쿤판 등급 중간 단계 (null이면 없음).</summary>
        public MidGradeConfig MidGrade;
        /// <summary>Phase 10 주민 특성 (null이면 명단 없음).</summary>
        public ResidentConfig Residents;
        public float DurationSeconds = 1800f;
        public int Runs = 20;
        public int BaseSeed = 1;
        public float TickInterval = 1f;
        public float SampleInterval = 10f;
        /// <summary>12-2 긴 세션: 이 등급에 도달하면 그 판을 끝냄 (-1 = 시간 끝까지).</summary>
        public int StopAtGrade = -1;
        /// <summary>12-2 구간 시작 만들기: 이 등급들에 처음 도달하는 순간의 상태를 캡처 (null = 안 함).</summary>
        public int[] CaptureGrades;
        /// <summary>12-2 구간 측정: 시드마다 이 상태에서 시작해 다음 등급까지 (null = 새 판, BaseSeed부터 Runs개).
        /// DurationSeconds는 시작 시점부터의 상한.</summary>
        public IReadOnlyList<BalanceStartState> Starts;
        /// <summary>
        /// 제자리 봇 (CONNECT_1121 0번, 사용자 결정 2026-11-26): 등급마다 <see cref="Runs"/>판씩, 처음부터 플레이하다 그 등급에 오르면
        /// 확장을 멈추고(칸 수 상한) <see cref="HoldSeconds"/> 동안 버티는지 본다 (null = 안 함). DurationSeconds = 그 등급에 오르기까지의 상한.
        /// </summary>
        public int[] HoldGrades;
        public float HoldSeconds = 7200f;
        /// <summary>시드에 묶인 봇 성향을 끄고 중립으로 (제자리 봇 측정 — 판 차이를 사건으로만).</summary>
        public bool NeutralPersona;
        /// <summary>CONNECT_1121 3번 ④ 비교: 인접 관계를 보지 않고 짓는 봇 (<see cref="BalanceBot.IgnoreAdjacency"/>).</summary>
        public bool IgnoreAdjacency;
    }

    /// <summary>한 판(시드 1개)의 결과 지표.</summary>
    public sealed class BalanceRunResult
    {
        public int Seed;
        /// <summary>13-7 봇 성향 요약 (성향을 껐으면 빈 문자열) · 성장 ↔ 안전 · 저축 (-1 ~ 1).</summary>
        public string Persona = "";
        public float PersonaGrowth;
        public float PersonaSaving;
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
        /// <summary>CONNECT_1121 1번 광맥: 잡힌 신호 묶음 · 다 캔 광맥 · 봇이 깐 길 프레임 · 광맥에 지은 · 옮긴 도킹 · 끝 제자리 생산 평균 (광맥이 없으면 0).</summary>
        public int VeinRounds, VeinsExhausted, VeinFrames, VeinDocksBuilt, VeinDocksMoved, VeinDocksFinal, VeinReconnects, VeinFramesFolded, VeinFramesFinal;
        public float InPlaceScaleFinal = 1f;
        /// <summary>CONNECT ⑤ 끝 상태: 따뜻함 모듈 · 그중 온기 구간 · 과열 모듈 · 배전 범위 밖 대전력 (설정이 없으면 0).</summary>
        public int WarmModules, WarmInBand, Overheated, Overloaded;
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
        /// <summary>Phase 10 주민 특성 만족도 보정 (평균·최저·최종).</summary>
        public float AverageResidentMood;
        public float MinResidentMood = float.MaxValue;
        public float FinalResidentMood;
        /// <summary>8-x: 마지막 시점 모듈 종류별 개수 (표시 이름 → 개수). 신규 모듈을 봇이 실제로 짓는지 확인용.</summary>
        public Dictionary<string, int> FinalModuleCounts = new Dictionary<string, int>();
        /// <summary>13-1 타이쿤판: 고른 해금 순서 ("등급:키").</summary>
        public List<string> Choices = new List<string>();

        // ---- 제자리 봇 (CONNECT_1121 0번, 2026-11-26) — HoldGrade < 0이면 해당 없음 ----
        public const float HoldLowMetal = 30f;
        public int HoldGrade = -1;
        /// <summary>그 등급에 올라 멈춘 시각(초, 판 시작부터). 못 오르면 NaN.</summary>
        public float HoldStartSeconds = float.NaN;
        /// <summary>멈춘 뒤 실제로 버틴 시간(초) — 게임 오버면 HoldSeconds보다 짧음.</summary>
        public float HoldSeconds;
        public int HoldCells, HoldEndCells;
        public int HoldStartPopulation, HoldStartHousing, HoldStartModules, HoldMinPopulation;
        public int HoldMinGrade;
        public int HoldDemolished, HoldBlocked, HoldCrisisDemolished, HoldHousingTrimmed;
        /// <summary>그 등급에 처음 오른 시각(초) · 고정 전에 1분 안정을 이뤘는지 (못 이루면 10분 뒤 그대로 고정).</summary>
        public float HoldReachSeconds = float.NaN;
        public bool HoldSettled;
        /// <summary>지을 것이 있는데 칸 수 상한에 막혀 못 지은 시간(초).</summary>
        public float HoldBlockedSeconds;
        public Dictionary<string, int> HoldDemolishedKinds = new Dictionary<string, int>();
        /// <summary>철거 기록 (봇 <see cref="BalanceBot.HoldEvents"/>).</summary>
        public List<string> HoldEvents = new List<string>();
        /// <summary>멈춘 뒤 금속 생산 평균 · 순수지 평균 (초당), 금속이 HoldLowMetal 미만이던 시간(초).</summary>
        public float HoldMetalIncome, HoldMetalNet, HoldLowMetalSeconds;
        /// <summary>멈춘 뒤 수리 · 정비(재건축 포함)에 쓴 금속 (분당).</summary>
        public float HoldRepairMetalPerMin, HoldUpkeepMetalPerMin;
        public int HoldDamaged, HoldDestroyed;
        public bool HoldReached => !float.IsNaN(HoldStartSeconds);
    }

    public sealed class BalanceReport
    {
        public readonly List<BalanceRunResult> Results = new List<BalanceRunResult>();
        public readonly StringBuilder TimeSeriesCsv = new StringBuilder();
        public string[] GradeNames;
        /// <summary>12-2 구간 측정이면 시작 등급, 아니면 -1.</summary>
        public int StartGrade = -1;
        /// <summary>12-2 구간 시작 만들기에서 캡처한 상태.</summary>
        public readonly List<BalanceStartState> Captured = new List<BalanceStartState>();
        /// <summary>시작 상태 복원 때 찾지 못한 항목 수 합 (0이 아니면 시작 상태를 다시 만들 때).</summary>
        public int RestoreMissing;

        public const string TimeSeriesHeader = "seed,time,population,housing,satisfaction,grade,modules,oxygen,water,food,metal,power_supply,power_demand,efficiency,damaged,active_events";

        public static BalanceReport Create(BalanceRunSettings s)
        {
            var report = new BalanceReport();
            report.GradeNames = new string[s.Grades.Grades.Count];
            for (int i = 0; i < report.GradeNames.Length; i++)
                report.GradeNames[i] = s.Grades.Grades[i].DisplayName;
            if (s.Starts != null && s.Starts.Count > 0)
                report.StartGrade = s.Starts[0].Grade;
            report.TimeSeriesCsv.AppendLine(TimeSeriesHeader);
            return report;
        }

        /// <summary>이 설정으로 돌릴 판 수.</summary>
        public static int RunCount(BalanceRunSettings s) => s.Starts != null ? s.Starts.Count : s.HoldGrades != null ? s.HoldGrades.Length * s.Runs : s.Runs;

        /// <summary>i번째 판 만들기 (구간 측정이면 i번째 시작 상태에서).</summary>
        public static BalanceRun CreateRun(BalanceRunSettings s, int i, StringBuilder series)
        {
            if (s.Starts != null)
                return new BalanceRun(s, s.Starts[i].Seed, series, s.Starts[i]);
            if (s.HoldGrades != null) // 제자리 봇: 등급마다 Runs판 (시드는 등급마다 같은 BaseSeed부터)
                return new BalanceRun(s, s.BaseSeed + i % s.Runs, series, null, s.HoldGrades[i / s.Runs]);
            return new BalanceRun(s, s.BaseSeed + i, series);
        }

        public void Add(BalanceRun run)
        {
            Results.Add(run.Result);
            Captured.AddRange(run.Captured);
            RestoreMissing += run.RestoreMissing;
        }
    }

    /// <summary>
    /// 측정 도구 본체: 시드마다 StationSimulation + BalanceBot으로 지정 시간을 시뮬레이션하고 지표를 모은다.
    /// </summary>
    public static class BalanceRunner
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>한 번에 끝까지 실행 (에디터가 멈춤). 백그라운드 실행은 <see cref="BalanceJob"/>.</summary>
        public static BalanceReport Run(BalanceRunSettings s, Action<int, float> progress = null)
        {
            BalanceMilestones.Clear(); // 13-7
            var report = BalanceReport.Create(s);
            int count = BalanceReport.RunCount(s);
            for (int i = 0; i < count; i++)
            {
                progress?.Invoke(i, (float)i / count);
                var run = BalanceReport.CreateRun(s, i, report.TimeSeriesCsv);
                while (!run.Done)
                    run.Step(int.MaxValue);
                report.Add(run);
            }
            progress?.Invoke(count, 1f);
            return report;
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
                          "low_power_s,avg_efficiency,final_satisfaction,min_satisfaction,persona_growth,persona_saving"
                          + (HasHold(report) ? ",hold_grade,hold_reach_s,hold_settled,hold_start_s,hold_s,hold_cells,hold_end_cells,hold_start_pop,hold_start_housing,hold_min_pop,hold_min_grade," +
                                               "hold_demolished,hold_crisis_demolished,hold_blocked_s,hold_metal_income,hold_metal_net,hold_low_metal_s,hold_repair_metal_per_min,hold_upkeep_metal_per_min,hold_damaged,hold_destroyed" : ""));

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
                  .Append(',').Append(F(r.FinalSatisfaction)).Append(',').Append(F(r.MinSatisfaction))
                  .Append(',').Append(F(r.PersonaGrowth)).Append(',').Append(F(r.PersonaSaving));
                if (HasHold(report))
                    sb.Append(',').Append(r.HoldGrade).Append(',').Append(float.IsNaN(r.HoldReachSeconds) ? "" : F(r.HoldReachSeconds)).Append(',').Append(r.HoldSettled ? 1 : 0).Append(',').Append(r.HoldReached ? F(r.HoldStartSeconds) : "").Append(',').Append(F(r.HoldSeconds))
                      .Append(',').Append(r.HoldCells).Append(',').Append(r.HoldEndCells)
                      .Append(',').Append(r.HoldStartPopulation).Append(',').Append(r.HoldStartHousing).Append(',').Append(r.HoldMinPopulation).Append(',').Append(r.HoldMinGrade)
                      .Append(',').Append(r.HoldDemolished).Append(',').Append(r.HoldCrisisDemolished).Append(',').Append(F(r.HoldBlockedSeconds))
                      .Append(',').Append(F(r.HoldMetalIncome)).Append(',').Append(F(r.HoldMetalNet)).Append(',').Append(F(r.HoldLowMetalSeconds))
                      .Append(',').Append(F(r.HoldRepairMetalPerMin)).Append(',').Append(F(r.HoldUpkeepMetalPerMin))
                      .Append(',').Append(r.HoldDamaged).Append(',').Append(r.HoldDestroyed);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static bool HasHold(BalanceReport report)
        {
            foreach (var r in report.Results)
                if (r.HoldGrade >= 0)
                    return true;
            return false;
        }

        /// <summary>
        /// 제자리 봇 요약 (CONNECT_1121 0번): 멈춘 등급마다 버틴 판 · 인구 · 칸 · 금속 수입 대 유지비 · 철거, 그리고 판마다 한 줄.
        /// "넓히지 않아도 사는가" — 버티면 넓힐 압력이 없다는 뜻, 무너지면 무엇이 무너뜨렸는지가 놓친 압력.
        /// </summary>
        public static string BuildHoldText(BalanceReport report, float holdSeconds)
        {
            var sb = new StringBuilder();
            var grades = new List<int>();
            foreach (var r in report.Results)
                if (r.HoldGrade >= 0 && !grades.Contains(r.HoldGrade))
                    grades.Add(r.HoldGrade);
            grades.Sort();
            sb.AppendLine($"제자리 봇: 등급에 오르면 칸 수를 고정하고 {holdSeconds / 60f:0}분 (금속 부족 = {BalanceRunResult.HoldLowMetal:0} 미만)");
            foreach (int g in grades)
            {
                var list = report.Results.FindAll(r => r.HoldGrade == g);
                var reached = list.FindAll(r => r.HoldReached);
                int overs = reached.FindAll(r => r.GameOver).Count;
                int full = reached.FindAll(r => !r.GameOver && r.HoldSeconds >= holdSeconds - 1f).Count;
                sb.Append($"- {report.GradeNames[g]}: 도달 {reached.Count}/{list.Count} · 끝까지 버팀 {full} · 게임 오버 {overs}");
                if (reached.Count > 0)
                {
                    sb.Append($" · 오름 {Avg(reached, r => r.HoldReachSeconds) / 60f:0.0}분 → 고정 {Avg(reached, r => r.HoldStartSeconds) / 60f:0.0}분 (1분 안정 {reached.FindAll(r => r.HoldSettled).Count}/{reached.Count}) · 칸 {Avg(reached, r => r.HoldCells):0}")
                      .Append($" · 인구 {Avg(reached, r => r.HoldStartPopulation):0} → 끝 {Avg(reached, r => r.FinalPopulation):0} (최저 {Avg(reached, r => r.HoldMinPopulation):0})")
                      .Append($" · 금속 수입 {Avg(reached, r => r.HoldMetalIncome) * 60f:0}/분")
                      .Append($" · 수리 {Avg(reached, r => r.HoldRepairMetalPerMin):0} + 정비 {Avg(reached, r => r.HoldUpkeepMetalPerMin):0}/분")
                      .Append($" · 금속 부족 {Avg(reached, r => r.HoldLowMetalSeconds) / Math.Max(1f, Avg(reached, r => r.HoldSeconds)) * 100f:0}%")
                      .Append($" · 바꿔 짓기 철거 {Avg(reached, r => r.HoldDemolished):0.0} (위기 {Avg(reached, r => r.HoldCrisisDemolished):0.0} · 인구 묶기 {Avg(reached, r => r.HoldHousingTrimmed):0.0}) · 칸에 막힌 시간 {Avg(reached, r => r.HoldBlockedSeconds) / Math.Max(1f, Avg(reached, r => r.HoldSeconds)) * 100f:0}%");
                }
                sb.AppendLine();
            }
            sb.AppendLine("판마다:");
            foreach (var r in report.Results)
            {
                if (r.HoldGrade < 0)
                    continue;
                if (!r.HoldReached)
                {
                    string why = !float.IsNaN(r.HoldReachSeconds) ? $"{r.HoldReachSeconds / 60f:0.0}분에 올랐지만 고정 전에" : $"못 오름 (최고 {report.GradeNames[r.MaxGrade]})";
                    sb.AppendLine($"  {report.GradeNames[r.HoldGrade]} 시드 {r.Seed}: {why}" + (r.GameOver ? $" 게임 오버 {r.Reason} {r.EndSeconds / 60f:0.0}분" : ""));
                    continue;
                }
                string end = r.GameOver ? $"게임 오버 {r.Reason} (멈춘 뒤 {r.HoldSeconds / 60f:0.0}분)" : $"버팀 {r.HoldSeconds / 60f:0}분";
                var kinds = new List<string>();
                foreach (var kv in r.HoldDemolishedKinds)
                    kinds.Add($"{kv.Key} {kv.Value}");
                sb.AppendLine($"  {report.GradeNames[r.HoldGrade]} 시드 {r.Seed}: {end} · {r.HoldReachSeconds / 60f:0.0}분에 오름 → {r.HoldStartSeconds / 60f:0.0}분에 고정 ({(r.HoldSettled ? "1분 안정" : "10분 지나 그대로")}) · 칸 {r.HoldCells} → {r.HoldEndCells}"
                              + $" · 인구 {r.HoldStartPopulation}/{r.HoldStartHousing} → {r.FinalPopulation} (최저 {r.HoldMinPopulation}) · 등급 {report.GradeNames[r.FinalGrade]} (최저 {report.GradeNames[r.HoldMinGrade]})"
                              + $" · 금속 수입 {r.HoldMetalIncome * 60f:0}/분 · 수리 {r.HoldRepairMetalPerMin:0} + 정비 {r.HoldUpkeepMetalPerMin:0}/분"
                              + $" · 금속 부족 {r.HoldLowMetalSeconds / Math.Max(1f, r.HoldSeconds) * 100f:0}% · 파손 {r.HoldDamaged} · 파괴 {r.HoldDestroyed}"
                              + $" · 철거 {r.HoldDemolished} (위기 {r.HoldCrisisDemolished} · 인구 묶기 {r.HoldHousingTrimmed})" + (kinds.Count > 0 ? $" ({string.Join(", ", kinds)})" : "") + $" · 칸에 막힌 시간 {r.HoldBlockedSeconds / Math.Max(1f, r.HoldSeconds) * 100f:0}%");
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
            if (report.StartGrade >= 0) // 12-2 구간 측정: 시간은 모두 시작 상태부터
                sb.AppendLine($"구간 측정: {report.GradeNames[report.StartGrade]} 시작 상태 {n}개 → 다음 등급까지 (시간은 시작부터)"
                              + (report.RestoreMissing > 0 ? $"  ※ 복원 때 못 찾은 항목 {report.RestoreMissing}개 — 시작 상태를 다시 만들 것" : ""));
            sb.AppendLine($"runs={n}  game_over={overs}/{n}" + (overs > 0 ? $" (평균 {overAt / overs / 60f:0.0}분,{why})" : ""));
            for (int g = Math.Max(1, report.StartGrade + 1); g < report.GradeNames.Length; g++)
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
            if (Avg(results, r => r.VeinRounds) > 0f)
                sb.AppendLine($"광맥 평균: 신호 {Avg(results, r => r.VeinRounds):0.0}번 · 다 캔 광맥 {Avg(results, r => r.VeinsExhausted):0.0} · 길 프레임 {Avg(results, r => r.VeinFrames):0.0} · 광맥 도킹 지음 {Avg(results, r => r.VeinDocksBuilt):0.0} · 옮김 {Avg(results, r => r.VeinDocksMoved):0.0} · 다시 이음 {Avg(results, r => r.VeinReconnects):0.0} · 끝에 광맥 도킹 {Avg(results, r => r.VeinDocksFinal):0.0} · 끝 제자리 생산 {Avg(results, r => r.InPlaceScaleFinal) * 100f:0}% · 접은 길 프레임 {Avg(results, r => r.VeinFramesFolded):0.0} · 끝 구조 프레임 {Avg(results, r => r.VeinFramesFinal):0.0}");
            if (Avg(results, r => r.WarmModules) > 0f)
                sb.AppendLine($"열 · 배전 (끝): 따뜻함 모듈 {Avg(results, r => r.WarmModules):0.0} 중 온기 {Avg(results, r => r.WarmInBand):0.0} · 과열 {Avg(results, r => r.Overheated):0.0} · 배전 범위 밖 대전력 {Avg(results, r => r.Overloaded):0.0}");
            sb.AppendLine($"최저 재고 평균  산소 {Avg(results, r => r.MinStock[0]):0}  물 {Avg(results, r => r.MinStock[1]):0}  식량 {Avg(results, r => r.MinStock[2]):0}  금속 {Avg(results, r => r.MinStock[3]):0}");
            sb.AppendLine($"고갈 시간 평균(초)  산소 {Avg(results, r => r.DepletedSeconds[0]):0}  물 {Avg(results, r => r.DepletedSeconds[1]):0}  식량 {Avg(results, r => r.DepletedSeconds[2]):0}  · 고갈 횟수 {Avg(results, r => r.DepletionEvents):0.0}회");
            sb.AppendLine($"전력 효율<100% 시간 평균 {Avg(results, r => r.LowPowerSeconds):0}초, 평균 효율 {Avg(results, r => r.AverageEfficiency) * 100f:0.0}%");
            sb.AppendLine($"만족도  최종 평균 {Avg(results, r => r.FinalSatisfaction):0}, 최저 평균 {Avg(results, r => r.MinSatisfaction):0}");
            sb.AppendLine($"주민 특성 보정  평균 {Avg(results, r => r.AverageResidentMood):+0.0;-0.0;0}, 최저 평균 {Avg(results, r => r.MinResidentMood):+0.0;-0.0;0}, 최종 평균 {Avg(results, r => r.FinalResidentMood):+0.0;-0.0;0}");
            foreach (var r in results) // 13-7 시드에 묶인 봇 성향
                if (!string.IsNullOrEmpty(r.Persona))
                    sb.AppendLine($"봇 성향 시드 {r.Seed}: {r.Persona}");
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
            foreach (var r in results) // 13-1 타이쿤판 해금 선택 경로
                if (r.Choices.Count > 0)
                    sb.Append($"\n해금 선택 시드 {r.Seed}: {string.Join(" → ", r.Choices)}");
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
