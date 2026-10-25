using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>13-1 장의 단계 (REDESIGN P3-2).</summary>
    public enum ChapterPhase
    {
        /// <summary>준비: 필수 · 선택 목표를 채움.</summary>
        Preparation,
        /// <summary>필수를 다 채움 → 절정까지 남은 시간 (선택 목표는 계속 채울 수 있음).</summary>
        Warning,
        /// <summary>절정 진행 중 (무작위 이벤트 멈춤).</summary>
        Climax,
        /// <summary>절정 결과 → 방향 선택을 기다림 (화면이 시간을 멈춤).</summary>
        Transition,
        /// <summary>마지막 장까지 끝남 (수직 단면 = 3장 끝).</summary>
        Finished,
    }

    /// <summary>13-1: 끝난 장 하나의 기록 (결과 화면 · 이후 엔딩 · 회고용).</summary>
    [Serializable]
    public sealed class ChapterRecord
    {
        public int Chapter;
        public ChapterOutcome Outcome;
        public string Summary;
        /// <summary>고른 방향 id (고르지 않았으면 빈 값).</summary>
        public string Choice;
        /// <summary>이 장에 걸린 시간 (게임 초).</summary>
        public float Seconds;
        /// <summary>13-2: 절정이 끝났을 때 코어의 독백 (그때 성향으로 고른 문장, 없으면 빈 값).</summary>
        public string Monologue;
    }

    /// <summary>
    /// 13-1 장 틀 (사용자 결정 2026-10-15, REDESIGN P3-2 · D-2). 준비(목표 필수 + 선택) → 필수 완료 시 절정 예고(남은 시간, [바로 맞이하기]) →
    /// 절정(종류별 진행 · 결과 판정) → 전환(결과 · 방향 선택, 화면이 시간을 멈춤) → 다음 장. 마지막 장 전환 뒤 캠페인 끝.
    /// 장 틀이 있으면 모듈 해금 · 새 요구 · 이벤트 규모 · 규칙(유성우 · 정책 · 합성 · 열 · 사회 위기 · 방문 세력)을 등급 대신 장이 정한다
    /// (해금 · 요구 · 규칙은 지금 장까지 누적). 등급은 실시간 규모 표시로 남고, 장 목표에 등급 도달이 들어간다.
    /// </summary>
    public sealed class ChapterSystem : StationSystem, IModuleUnlocks, IEventScale, IStationRules
    {
        public const string SystemKey = "chapters";
        /// <summary>13-4: 튜토리얼(코어 기동)이 끝났거나 없으면 저절로 올라가는 표시 (1장 필수 목표 — 건너뛰거나 끈 판도 채워짐).</summary>
        public const string TutorialFlag = "tutorial";
        /// <summary>13-4: 내부 안내(방주 안 둘러보기)를 끝까지 하면 화면이 올리는 표시 (1장 선택 목표).</summary>
        public const string InteriorFlag = "interior";

        private readonly ChapterConfig _config;
        private readonly HashSet<string> _flags = new HashSet<string>();
        private readonly List<ChapterRecord> _records = new List<ChapterRecord>();
        private bool[] _done = new bool[0];
        private ChapterClimax _climax;
        private int _earthStep; // 이 장에서 이미 알린 불빛 단계 (저장 안 함 — 장 시간에서 다시 셈)

        /// <summary>새 장 시작 (장 인덱스).</summary>
        public event Action<int> ChapterStarted;
        /// <summary>목표 하나 완료 (목표 인덱스).</summary>
        public event Action<int> GoalCompleted;
        /// <summary>필수 목표를 다 채워 절정 예고가 시작됨.</summary>
        public event Action WarningStarted;
        public event Action ClimaxStarted;
        /// <summary>절정 끝 → 전환 (방금 끝난 장의 기록).</summary>
        public event Action<ChapterRecord> ClimaxEnded;
        /// <summary>마지막 장 전환까지 끝남.</summary>
        public event Action CampaignFinished;
        /// <summary>
        /// 13-8 ③ 보완 (사용자 결정 2026-10-25): 장 안에서 지구 불빛이 한 단계 줄어듦 (이 장의 단계, 1부터) — 코어 혼잣말은 여기서 알리고, 화면은 지구를 깜빡임.
        /// 플레이 확인: 바빠서 꺼져 가는 지구에 눈이 가지 않았음.
        /// </summary>
        public event Action<int> EarthDimmed;

        public ChapterSystem(ChapterConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (config.Count == 0)
                throw new ArgumentException("장이 하나 이상 필요", nameof(config));
        }

        public override string Key => SystemKey;
        public ChapterConfig Config => _config;
        public int Count => _config.Count;
        public int Index { get; private set; }
        public ChapterData Current => _config.Chapters[Index];
        public bool IsLast => Index >= _config.Count - 1;
        public ChapterPhase Phase { get; private set; }
        /// <summary>이 장에서 흐른 게임 시간 (전환 중에는 멈춤).</summary>
        public float ChapterSeconds { get; private set; }
        /// <summary>절정까지 남은 시간 (예고 중일 때만 의미).</summary>
        public float WarningLeft { get; private set; }
        /// <summary>진행 중이거나 방금 끝난 절정 (준비 · 예고 중이면 null).</summary>
        public ChapterClimax Climax => _climax;
        /// <summary>끝난 장 기록 (차례대로).</summary>
        public IReadOnlyList<ChapterRecord> Records => _records;
        /// <summary>마지막으로 끝난 장 기록 (없으면 null).</summary>
        public ChapterRecord LastRecord => _records.Count > 0 ? _records[_records.Count - 1] : null;

        /// <summary>13-7d 이 캠페인 판의 번호 (새 캠페인마다 새로, 저장) — 체크포인트가 다른 판 것인지 가림.</summary>
        public string RunId { get; private set; } = "";

        protected override void OnAttached()
        {
            Begin(0, raise: false);
            RunId = Guid.NewGuid().ToString("N");
            Sim.ProgressLabel = ProgressText; // main M-44: 이어하기 · 저장 목록 · 로딩 화면 "3장 · 무리 합류"
            HookRecipes();
        }

        /// <summary>main M-50: 레시피는 장이 바로 엶 (14-1c). 장 시스템이 합성 시스템보다 먼저 붙으므로 붙을 때 · 틱 · 복원에서 확인.</summary>
        private void HookRecipes()
        {
            if (Sim.Synthesis != null && Sim.Synthesis.OpensDirectly == null)
                Sim.Synthesis.OpensDirectly = OpensRecipe;
        }

        /// <summary>14-1c: 지금 장까지 연구 없이 연 레시피인지 (장 데이터 누적).</summary>
        public bool OpensRecipe(RecipeData recipe)
        {
            if (recipe == null)
                return false;
            for (int i = 0; i <= Index && i < _config.Count; i++)
            {
                var c = _config.Chapters[i];
                if (c == null)
                    continue;
                foreach (var r in c.Recipes)
                    if (r == recipe)
                        return true;
            }
            return false;
        }

        /// <summary>13-7d 저장 진행 표시: "3장 · 무리 합류", 캠페인이 끝났으면 "이어질 이야기".</summary>
        public string ProgressText() => Phase == ChapterPhase.Finished ? "이어질 이야기"
            : string.IsNullOrEmpty(Current.Subtitle) ? Current.DisplayName : $"{Current.DisplayName} · {Current.Subtitle}";

        /// <summary>
        /// 13-7d 체크포인트 읽기: 저장의 장 상태에서 판 번호 · 장 번호 · 단계를 꺼냄 (다른 판 · 다른 장 체크포인트를 가리려고).
        /// 장 상태가 없거나 판 번호가 없으면(이전 저장) false.
        /// </summary>
        public static bool TryReadRun(string state, out string runId, out int index, out ChapterPhase phase)
        {
            runId = "";
            index = -1;
            phase = ChapterPhase.Preparation;
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s == null || string.IsNullOrEmpty(s.RunId))
                return false;
            runId = s.RunId;
            index = s.Index;
            phase = (ChapterPhase)s.Phase;
            return true;
        }

        // ---------------- 해금 · 요구 · 규칙 · 규모 ----------------

        public ChapterData GetChapter(int index) => _config.Chapters[index];

        /// <summary>이 모듈이 처음 열리는 장 (어느 장에도 없으면 -1).</summary>
        public int UnlockStage(ModuleData data)
        {
            for (int i = 0; i < _config.Count; i++)
            {
                var c = _config.Chapters[i];
                if (c == null)
                    continue;
                foreach (var m in c.Unlocks)
                    if (m == data)
                        return i;
            }
            return -1;
        }

        public bool IsUnlocked(ModuleData data)
        {
            int stage = UnlockStage(data);
            return stage >= 0 && stage <= Index;
        }

        public ModuleData Find(string assetName)
        {
            foreach (var c in _config.Chapters)
            {
                if (c == null)
                    continue;
                foreach (var m in c.Unlocks)
                    if (m != null && m.name == assetName)
                        return m;
            }
            return null;
        }

        /// <summary>지금 장까지 열린 규칙인지.</summary>
        public bool Allows(StationRule rule)
        {
            for (int i = 0; i <= Index && i < _config.Count; i++)
            {
                var c = _config.Chapters[i];
                if (c != null && (c.Rules & rule) != 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 지금 장까지 생긴 거주자 요구 (누적, 중복 없이): 장 시작 요구 + 등급 목표를 채운 순간의 요구
        /// (지난 장은 등급 목표를 지나왔으므로 포함, 지금 장은 <see cref="GradeUpApplied"/>일 때만).
        /// </summary>
        internal void CollectNeeds(List<ResidentNeed> into)
        {
            for (int i = 0; i <= Index && i < _config.Count; i++)
            {
                var c = _config.Chapters[i];
                if (c == null)
                    continue;
                Add(into, c.NewNeeds);
                if (i < Index || GradeUpApplied)
                    Add(into, c.GradeUpNeeds);
            }
        }

        private static void Add(List<ResidentNeed> into, IReadOnlyList<ResidentNeed> needs)
        {
            foreach (var need in needs)
                if (need != ResidentNeed.None && !into.Contains(need))
                    into.Add(need);
        }

        /// <summary>
        /// 이 장의 등급 목표를 채웠는지 (사용자 결정 2026-10-15: 새 요구 · 이벤트 규모는 등급이 오르는 순간에 맞춤).
        /// 한 번 채우면 이 장 동안 유지 (등급이 다시 내려가도).
        /// </summary>
        public bool GradeUpApplied { get; private set; }

        private bool UseGradeUpScale => GradeUpApplied && Current.HasGradeUpScale;
        public int MeteorHitsMin => UseGradeUpScale ? Current.GradeUpMeteorHitsMin : Current.MeteorHitsMin;
        public int MeteorHitsMax => UseGradeUpScale ? Current.GradeUpMeteorHitsMax : Current.MeteorHitsMax;
        public float EventIntervalMultiplier => _config.EventIntervalMultiplier(Index, GradeUpApplied);
        public float EventIntensityMultiplier => UseGradeUpScale ? Current.GradeUpEventIntensityMultiplier : Current.EventIntensityMultiplier;

        // ---------------- 목표 ----------------

        public bool IsGoalDone(int goal) => goal >= 0 && goal < _done.Length && _done[goal];

        /// <summary>필수 목표를 모두 채웠는지 (필수가 없으면 true).</summary>
        public bool RequiredDone
        {
            get
            {
                var goals = Current.Goals;
                for (int i = 0; i < goals.Count; i++)
                    if (!goals[i].Optional && !IsGoalDone(i))
                        return false;
                return true;
            }
        }

        /// <summary>목표 진행 (현재 / 필요). 완료된 목표는 필요 값을 현재로.</summary>
        public void GoalProgress(int goal, out float current, out float required)
        {
            var g = Current.Goals[goal];
            required = Required(g);
            current = Math.Min(required, Measure(g));
            if (IsGoalDone(goal))
                current = required;
        }

        /// <summary>목표 문구 (데이터에 없으면 종류에 맞춰).</summary>
        public string GoalText(int goal)
        {
            var g = Current.Goals[goal];
            if (!string.IsNullOrEmpty(g.Text))
                return g.Text;
            switch (g.Kind)
            {
                case ChapterGoalKind.ReachGrade:
                    return g.Grade < Sim.Progression.GradeCount ? $"{Sim.Progression.GetGrade(g.Grade).DisplayName} 정거장 도달" : "등급 도달";
                case ChapterGoalKind.BuildModule:
                    return g.Module != null ? $"{g.Module.DisplayName} {g.Count}개 가동" : "모듈 건설";
                case ChapterGoalKind.Population:
                    return $"인구 {g.Count}명";
                case ChapterGoalKind.Stock:
                    return $"{g.Resource.DisplayName()} {g.Amount:0} 비축";
                case ChapterGoalKind.SurviveSeconds:
                    return $"{g.Amount / 60f:0.#}분 버티기";
                case ChapterGoalKind.Synthesize:
                    return g.Count <= 1 ? "진화 모듈 하나 만들기" : $"진화 모듈 {g.Count}개 만들기";
                case ChapterGoalKind.EvolvedKinds:
                    return $"진화 모듈 {g.Count}종 가동";
                default:
                    return g.Flag;
            }
        }

        /// <summary>
        /// 다른 시스템 · 화면이 올리는 표시 (목표 종류 Flag). 한 번 올리면 유지.
        /// 14-1c: 장 안 표시(<see cref="ScopedFlag"/>, 예 <c>record@4</c>)도 함께 올림 — 장을 넘어도 남는 표시를 "이 장에서 한 번 더" 목표에 쓰려고.
        /// </summary>
        public void SetFlag(string flag)
        {
            if (string.IsNullOrEmpty(flag))
                return;
            bool added = _flags.Add(flag);
            added |= _flags.Add(ScopedFlag(flag, Index + 1));
            if (!added)
                return;
            if (Phase == ChapterPhase.Preparation || Phase == ChapterPhase.Warning)
                EvaluateGoals();
        }

        /// <summary>14-1c: 그 장(번호, 1부터) 안에서 올라간 표시 이름 ("record@4").</summary>
        public static string ScopedFlag(string flag, int chapterNumber) => $"{flag}@{chapterNumber}";

        public bool HasFlag(string flag) => !string.IsNullOrEmpty(flag) && _flags.Contains(flag);

        private float Required(ChapterGoal g)
        {
            switch (g.Kind)
            {
                case ChapterGoalKind.ReachGrade:
                case ChapterGoalKind.Flag:
                    return 1f;
                case ChapterGoalKind.BuildModule:
                case ChapterGoalKind.Population:
                case ChapterGoalKind.Synthesize:
                case ChapterGoalKind.EvolvedKinds:
                    return Math.Max(1, g.Count);
                default:
                    return Math.Max(0.001f, g.Amount);
            }
        }

        private float Measure(ChapterGoal g)
        {
            switch (g.Kind)
            {
                case ChapterGoalKind.ReachGrade:
                    return Sim.Progression.GradeIndex >= g.Grade ? 1f : 0f;
                case ChapterGoalKind.BuildModule:
                    return CountActive(g.Module);
                case ChapterGoalKind.Population:
                    return Sim.Resources.Population;
                case ChapterGoalKind.Stock:
                    return Sim.Resources.GetStock(g.Resource);
                case ChapterGoalKind.SurviveSeconds:
                    return ChapterSeconds;
                case ChapterGoalKind.Flag:
                    return HasFlag(g.Flag) ? 1f : 0f;
                case ChapterGoalKind.Synthesize:
                    return Sim.Session.Syntheses;
                case ChapterGoalKind.EvolvedKinds:
                    return CountEvolvedKinds();
                default:
                    return 0f;
            }
        }

        private readonly List<ModuleData> _evolvedKinds = new List<ModuleData>();

        /// <summary>가동 중(코어에 연결)인 진화 모듈의 종류 수.</summary>
        private int CountEvolvedKinds()
        {
            var syn = Sim.Synthesis;
            if (syn == null)
                return 0;
            _evolvedKinds.Clear();
            foreach (var m in Sim.Grid.Modules)
                if (m.Data != null && !_evolvedKinds.Contains(m.Data) && syn.IsRecipeResult(m.Data) && Sim.Connectivity.IsActive(m))
                    _evolvedKinds.Add(m.Data);
            return _evolvedKinds.Count;
        }

        private int CountActive(ModuleData data)
        {
            if (data == null)
                return 0;
            int n = 0;
            foreach (var m in Sim.Grid.Modules)
                if (m.Data == data && Sim.Connectivity.IsActive(m))
                    n++;
            return n;
        }

        private void EvaluateGoals()
        {
            var goals = Current.Goals;
            for (int i = 0; i < goals.Count && i < _done.Length; i++)
            {
                if (_done[i] || Measure(goals[i]) < Required(goals[i]) - 1e-4f)
                    continue;
                _done[i] = true;
                if (goals[i].Optional)
                    GiveReward(goals[i]);
                string reward = goals[i].Optional && goals[i].HasReward ? $" · 보상 {RewardText(goals[i])}" : "";
                Sim.Report(CoreSay("goal.done", "장 목표 완료: {0}{1}", GoalText(i), reward), true);
                if (goals[i].Kind == ChapterGoalKind.ReachGrade)
                    ApplyGradeUp();
                GoalCompleted?.Invoke(i);
            }
        }

        /// <summary>13-2 코어 성격 (붙어 있지 않으면 null — 시험 · 봇).</summary>
        public CorePersonalitySystem Core => Sim.GetSystem<CorePersonalitySystem>();

        /// <summary>
        /// 13-2 상태 줄 말투: 코어 문장 표에서 지금 성향에 맞는 문장을 골라 로그에 남기고 돌려줌.
        /// 코어가 없거나 표에 없으면 template(기본 문장)에 args를 넣은 것.
        /// </summary>
        private string CoreSay(string key, string template, params object[] args)
        {
            var core = Core;
            if (core != null)
                return core.SayAndLog(key, template, args);
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                return template;
            }
        }

        /// <summary>선택 목표 보상 한 줄 ("금속 +50 · 연구 포인트 +1").</summary>
        public static string RewardText(ChapterGoal g)
        {
            var parts = new List<string>();
            foreach (var r in g.RewardResources)
                parts.Add($"{r.Type.DisplayName()} +{r.Amount:0}");
            if (g.RewardResearchPoints > 0)
                parts.Add($"연구 포인트 +{g.RewardResearchPoints}");
            if (g.RewardFreeRepairs > 0)
                parts.Add($"무료 수리권 +{g.RewardFreeRepairs}");
            return string.Join(" · ", parts);
        }

        /// <summary>
        /// 등급 목표를 채운 순간: 새 요구 · 이벤트 규모를 올림 (한 번만). 이벤트 규모는 바로, 요구는 다음 틱부터
        /// (요구 충족 단계가 장 단계보다 앞 — 공통 바닥 틱 순서는 바꾸지 않음, 1초 차이).
        /// </summary>
        private void ApplyGradeUp()
        {
            if (GradeUpApplied)
                return;
            GradeUpApplied = true;
            var data = Current;
            var parts = new List<string>();
            foreach (var need in data.GradeUpNeeds)
                if (need != ResidentNeed.None)
                    parts.Add($"새 요구 '{need.DisplayName()}'");
            if (data.HasGradeUpScale)
                parts.Add("위협이 커집니다");
            if (parts.Count > 0)
                Sim.Report(CoreSay("gradeup", "{0}", string.Join(" · ", parts)), false);
            Sim.RefreshModuleStats();
        }

        private void GiveReward(ChapterGoal g)
        {
            foreach (var r in g.RewardResources)
                Sim.Resources.AddStock(r.Type, r.Amount);
            if (g.RewardResearchPoints > 0 || g.RewardFreeRepairs > 0)
                Sim.Supply.SetCounts(Sim.Supply.ResearchPoints + g.RewardResearchPoints, Sim.Supply.FreeRepairs + g.RewardFreeRepairs);
        }

        // ---------------- 진행 ----------------

        public override void Tick(float dt)
        {
            HookRecipes();
            if (Sim.Session.IsGameOver)
                return;
            switch (Phase)
            {
                case ChapterPhase.Preparation:
                    ChapterSeconds += dt;
                    if (!HasFlag(TutorialFlag) && (Sim.Tutorial == null || !Sim.Tutorial.Active))
                        _flags.Add(TutorialFlag); // 13-4 코어 기동 끝 (바로 아래에서 목표 판정)
                    EvaluateGoals();
                    if (RequiredDone)
                        BeginWarning();
                    break;
                case ChapterPhase.Warning:
                    ChapterSeconds += dt;
                    EvaluateGoals();
                    WarningLeft -= dt;
                    if (WarningLeft <= 0f)
                        BeginClimax();
                    break;
                case ChapterPhase.Climax:
                    ChapterSeconds += dt;
                    _climax.Tick(dt);
                    if (_climax.Finished)
                        EndClimax();
                    break;
            }
            if (Phase == ChapterPhase.Preparation || Phase == ChapterPhase.Warning || Phase == ChapterPhase.Climax)
                CheckEarthDim();
        }

        /// <summary>이 장 시간에서 지구 불빛 단계 (불빛이 줄어드는 시간을 <see cref="ChapterData.EarthDimNotices"/>로 나눈 지점마다 1씩, 줄지 않는 장은 0).</summary>
        public int EarthStepAt(float chapterSeconds)
        {
            var c = Current;
            int n = c.EarthDimNotices;
            if (n <= 0 || c.EarthLightsEnd >= c.EarthLightsStart)
                return 0;
            return Math.Min(n, Mathf.FloorToInt(chapterSeconds / (c.EarthDimSeconds / n)));
        }

        /// <summary>불빛이 한 단계 줄었으면 코어 혼잣말(상태 줄은 "코어 · " 머리 · 코어 기록) + <see cref="EarthDimmed"/>.</summary>
        private void CheckEarthDim()
        {
            int step = EarthStepAt(ChapterSeconds);
            if (step <= _earthStep)
                return;
            _earthStep = step;
            Sim.Report("코어 · " + CoreSay("earth.dim", "저 아래 불빛이 또 꺼졌다."), true);
            EarthDimmed?.Invoke(step);
        }

        /// <summary>[바로 맞이하기]: 예고 중이면 절정을 지금 시작.</summary>
        public bool StartClimaxNow()
        {
            if (Phase != ChapterPhase.Warning)
                return false;
            BeginClimax();
            return true;
        }

        /// <summary>
        /// 전환에서 방향을 고르고 다음 장으로 (마지막 장이면 캠페인 끝). 선택지가 없는 장은 -1.
        /// 잘못된 선택(범위 밖 · 전환 중이 아님)이면 false.
        /// </summary>
        public bool Choose(int choice)
        {
            if (Phase != ChapterPhase.Transition)
                return false;
            var choices = Current.Choices;
            if (choices.Count > 0 ? choice < 0 || choice >= choices.Count : choice != -1)
                return false;
            var record = LastRecord;
            if (record != null && record.Chapter == Index)
                record.Choice = choice >= 0 ? choices[choice].Id : "";
            if (choice >= 0)
            {
                // 13-2: 방향 선택 → 코어 성격 축 이동 (원인 기록) + 로그
                var picked = choices[choice];
                var core = Core;
                if (core != null)
                {
                    core.Apply(picked.Shifts, $"{Current.DisplayName} 방향: {picked.Title}");
                    core.SayAndLog("choice." + picked.Id, "{0} · 방주의 방향을 정했다: {1}", Current.DisplayName, picked.Title);
                }
            }
            if (IsLast)
            {
                Phase = ChapterPhase.Finished;
                _climax = null;
                ReleaseEvents();
                CampaignFinished?.Invoke();
                return true;
            }
            Begin(Index + 1, raise: true);
            return true;
        }

        private void Begin(int index, bool raise)
        {
            _climax?.Detach();
            _climax = null;
            Index = Math.Max(0, Math.Min(index, _config.Count - 1));
            Phase = ChapterPhase.Preparation;
            ChapterSeconds = 0f;
            _earthStep = 0;
            WarningLeft = 0f;
            GradeUpApplied = false;
            _done = new bool[Current.Goals.Count];
            ReleaseEvents();
            if (raise)
            {
                Sim.RefreshModuleStats(); // 새 요구 · 규칙이 바로 반영되게
                var sb = new System.Text.StringBuilder();
                foreach (var m in Current.Unlocks)
                    if (m != null)
                        sb.Append($"  ·  {m.DisplayName} 해금");
                if (Current.Recipes.Count > 0)
                    sb.Append($"  ·  합성 레시피 {Current.Recipes.Count}종 (합성 창 K)"); // 14-1c
                Sim.Report(CoreSay("chapter.start", "{0} 시작{1}", Current.DisplayName, sb.ToString()), true);
                ChapterStarted?.Invoke(Index);
            }
        }

        private void BeginWarning()
        {
            Phase = ChapterPhase.Warning;
            WarningLeft = Current.Climax.WarningSeconds;
            if (WarningLeft > 0f)
                Sim.Report(CoreSay("climax.warning", "위기가 다가옵니다: {0} · {1}분 뒤", Current.Climax.Title, (WarningLeft / 60f).ToString("0.#")), false);
            WarningStarted?.Invoke();
            if (WarningLeft <= 0f)
                BeginClimax();
        }

        private void BeginClimax()
        {
            Phase = ChapterPhase.Climax;
            WarningLeft = 0f;
            Sim.Events.Held = true; // 절정 동안 무작위 이벤트는 멈춤 (절정 사건만)
            _climax = ChapterClimax.Create(Current.Climax.Kind);
            _climax.Attach(Sim, Current.Climax);
            ClimaxStarted?.Invoke();
            _climax.Begin();
            if (_climax.Finished)
                EndClimax();
        }

        private void EndClimax()
        {
            _climax.Detach();
            Phase = ChapterPhase.Transition;
            var record = new ChapterRecord
            {
                Chapter = Index,
                Outcome = _climax.Outcome,
                Summary = _climax.Summary,
                Choice = "",
                Seconds = ChapterSeconds,
            };
            // 13-2: 절정 결과에 대한 코어의 독백 (지금 성향으로, 장 결과 창에 한 줄)
            var core = Core;
            if (core != null)
            {
                // 13-6c: 절정 종류 전용 문장(outcome.GreatMigration.Clean 등)이 있으면 그것 — 대이주는 '버팀'이 아니라 '맞이함'
                string specific = $"outcome.{Current.Climax.Kind}.{record.Outcome}";
                string key = string.IsNullOrEmpty(core.Say(specific, null)) ? "outcome." + record.Outcome : specific;
                record.Monologue = core.SayAndLog(key, "", Current.DisplayName, record.Summary) ?? "";
            }
            _records.Add(record);
            ClimaxEnded?.Invoke(record);
        }

        /// <summary>절정 · 전환이 아니면 무작위 이벤트를 다시 (튜토리얼이 진행 중이면 튜토리얼이 잡아 둔 대로).</summary>
        private void ReleaseEvents()
        {
            Sim.Events.Held = Sim.Tutorial != null && Sim.Tutorial.Active;
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public int Index;
            public int Phase;
            public float ChapterSeconds;
            public float WarningLeft;
            public bool GradeUp;
            public List<bool> Done = new List<bool>();
            public List<string> Flags = new List<string>();
            public List<ChapterRecord> Records = new List<ChapterRecord>();
            public int ClimaxKind;
            public string Climax;
            public string RunId; // 13-7d (이전 저장 = 비어 있음 → 불러올 때 새 번호를 그대로 둠)
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            var s = new State
            {
                Index = Index,
                Phase = (int)Phase,
                ChapterSeconds = ChapterSeconds,
                WarningLeft = WarningLeft,
                GradeUp = GradeUpApplied,
                Done = new List<bool>(_done),
                Flags = new List<string>(_flags),
                Records = new List<ChapterRecord>(_records),
                RunId = RunId,
            };
            if (Phase == ChapterPhase.Climax && _climax != null)
            {
                s.ClimaxKind = (int)Current.Climax.Kind;
                s.Climax = _climax.Capture();
            }
            return JsonUtility.ToJson(s);
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            HookRecipes();
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            Begin(s.Index, raise: false);
            if (!string.IsNullOrEmpty(s.RunId))
                RunId = s.RunId;
            Phase = (ChapterPhase)Mathf.Clamp(s.Phase, 0, (int)ChapterPhase.Finished);
            ChapterSeconds = s.ChapterSeconds;
            _earthStep = EarthStepAt(ChapterSeconds); // 불러온 순간 지난 단계를 한꺼번에 알리지 않게
            WarningLeft = s.WarningLeft;
            GradeUpApplied = s.GradeUp;
            for (int i = 0; i < _done.Length && i < s.Done.Count; i++)
                _done[i] = s.Done[i];
            _flags.Clear();
            foreach (var f in s.Flags)
                _flags.Add(f);
            _records.Clear();
            _records.AddRange(s.Records);

            if (Phase == ChapterPhase.Climax)
            {
                _climax = ChapterClimax.Create(Current.Climax.Kind);
                _climax.Attach(Sim, Current.Climax);
                if (s.ClimaxKind == (int)Current.Climax.Kind)
                    _climax.Restore(s.Climax);
                else
                    _climax.Begin(); // 데이터가 바뀌었으면 절정을 처음부터
            }
            if (Phase == ChapterPhase.Transition && LastRecord == null)
                Phase = ChapterPhase.Preparation; // 기록이 없으면 전환을 이어갈 수 없음
            if (Phase == ChapterPhase.Climax || Phase == ChapterPhase.Transition)
                Sim.Events.Held = true;
            else
                ReleaseEvents();
            Sim.RefreshModuleStats();
        }
    }
}
