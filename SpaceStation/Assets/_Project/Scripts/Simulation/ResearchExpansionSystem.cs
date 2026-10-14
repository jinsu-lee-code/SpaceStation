using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>레시피 창 상태 (12-4, PHASESET E2).</summary>
    public enum RecipeState
    {
        /// <summary>미공개 (표시 안 함).</summary>
        Hidden,
        /// <summary>그림자 (이름 + 실루엣, 해금 연구 가능).</summary>
        Shadow,
        /// <summary>해금 (전체 정보, 합성 가능 — 등급 조건은 따로).</summary>
        Unlocked,
    }

    /// <summary>진행 중인 레시피 해금 연구 · 연계 연구 하나.</summary>
    public sealed class ExpansionProject
    {
        public RecipeData Recipe { get; internal set; }
        public LinkedResearchData Linked { get; internal set; }
        public float Progress { get; internal set; }
        public bool Paused { get; internal set; }
        public float Rate { get; internal set; }
        public float Duration { get; internal set; }
        public float PowerDemand { get; internal set; }
        public float RemainingSeconds => Rate > 1e-4f ? (1f - Progress) * Duration / Rate : float.PositiveInfinity;
        public string DisplayName => Recipe != null ? Recipe.DisplayName : Linked != null ? Linked.DisplayName : "";
    }

    /// <summary>시작할 수 없는 까닭 (UI 사유 표시).</summary>
    public enum ExpansionStartResult
    {
        Ok,
        Unknown,
        AlreadyDone,
        AlreadyResearching,
        NotRevealed,
        GradeTooLow,
        ModuleMissing,
        NoFreeSlot,
        NoResearchPoints,
        InsufficientResources,
    }

    /// <summary>
    /// 12-4 연구 확장 (사용자 결정 2026-10-14, PHASESET E2, RESEARCH.md 10번, BALANCE 38번).
    /// - 레시피 상태: 담당 카테고리 레벨이 (4 + 단계) 이상이면 그림자, 해금 연구를 마치면(또는 자동 해금 레시피) 해금.
    /// - 레시피 해금 연구: 레시피 연구 슬롯(가동 중인 연구소 계통 진화 모듈의 <see cref="ModuleData.RecipeSlots"/> 합, 레벨 연구와 따로)에서
    ///   단계별 비용(자원 + 연구 포인트 + 전력 + 시간). 슬롯이 모자라면 나중에 시작한 것부터 멈춤.
    /// - 연계 연구: 필요한 상위 모듈이 가동 중일 때 일반 연구 슬롯에서 1회 연구. 효과는 그 모듈이 가동 중일 때만.
    ///   일반 슬롯은 레벨 연구가 먼저 쓰고 남은 슬롯에서 진행 (새 레벨 연구는 연계 연구가 쓰는 슬롯을 빼고 판정).
    /// 설정이 없으면 시스템 없음 (레시피는 자동 해금 표시만 = 12-3과 같음).
    /// </summary>
    public sealed class ResearchExpansionSystem : StationSystem
    {
        public const string SystemKey = "researchExpansion";

        private readonly ResearchExpansionConfig _config;
        private readonly HashSet<RecipeData> _researched = new HashSet<RecipeData>();
        private readonly HashSet<LinkedResearchData> _linkedDone = new HashSet<LinkedResearchData>();
        private readonly List<ExpansionProject> _recipeProjects = new List<ExpansionProject>();
        private readonly List<ExpansionProject> _linkedProjects = new List<ExpansionProject>();
        private readonly List<ExpansionProject> _finished = new List<ExpansionProject>();
        private readonly HashSet<ModuleData> _activeData = new HashSet<ModuleData>();
        private readonly List<ResearchModifier> _linkedModifiers = new List<ResearchModifier>();
        private string _linkedKey = "";

        /// <summary>레시피가 해금됨 · 연계 연구가 끝남 (UI 알림).</summary>
        public event Action<RecipeData> RecipeUnlocked;
        public event Action<LinkedResearchData> LinkedCompleted;
        public event Action Changed;

        public ResearchExpansionSystem(ResearchExpansionConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override string Key => SystemKey;
        public ResearchExpansionConfig Config => _config;
        public IReadOnlyList<ExpansionProject> RecipeProjects => _recipeProjects;
        public IReadOnlyList<ExpansionProject> LinkedProjects => _linkedProjects;
        public IReadOnlyList<LinkedResearchData> Linked => _config.Linked;
        /// <summary>레시피 연구 슬롯 (마지막 틱 기준).</summary>
        public int RecipeSlots { get; private set; }

        /// <summary>진행 중인 연구(레시피 · 연계, 멈추지 않은 것)의 전력 수요 합.</summary>
        public float RunningPowerDemand
        {
            get
            {
                float sum = 0f;
                foreach (var p in _recipeProjects)
                    if (!p.Paused)
                        sum += p.PowerDemand;
                foreach (var p in _linkedProjects)
                    if (!p.Paused)
                        sum += p.PowerDemand;
                return sum;
            }
        }

        // ---------------- 레시피 ----------------

        public bool IsResearched(RecipeData recipe) => recipe != null && _researched.Contains(recipe);

        /// <summary>그림자 공개에 필요한 담당 카테고리 레벨.</summary>
        public int RevealLevel(RecipeData recipe) => _config.ShadowLevelOffset + (recipe != null ? recipe.Stage : 1);

        public RecipeState StateOf(RecipeData recipe)
        {
            if (recipe == null)
                return RecipeState.Hidden;
            if (recipe.UnlockedByDefault || _researched.Contains(recipe))
                return RecipeState.Unlocked;
            var category = Sim.Research.Find(recipe.ResearchCategory);
            return category != null && Sim.Research.GetLevel(category) >= RevealLevel(recipe) ? RecipeState.Shadow : RecipeState.Hidden;
        }

        public RecipeResearchCost CostOf(RecipeData recipe) => _config.CostFor(recipe != null ? recipe.Stage : 1);

        public List<ResourceAmount> StartCost(RecipeData recipe)
        {
            var cost = new List<ResourceAmount>();
            foreach (var a in CostOf(recipe).StartCost)
                cost.Add(new ResourceAmount(a.Type, a.Amount));
            return cost;
        }

        public ExpansionProject ProjectOf(RecipeData recipe)
        {
            foreach (var p in _recipeProjects)
                if (p.Recipe == recipe)
                    return p;
            return null;
        }

        public ExpansionStartResult CanStart(RecipeData recipe)
        {
            if (recipe == null)
                return ExpansionStartResult.Unknown;
            var state = StateOf(recipe);
            if (state == RecipeState.Unlocked)
                return ExpansionStartResult.AlreadyDone;
            if (ProjectOf(recipe) != null)
                return ExpansionStartResult.AlreadyResearching;
            if (state == RecipeState.Hidden)
                return ExpansionStartResult.NotRevealed;
            if (_recipeProjects.Count >= CountRecipeSlots())
                return ExpansionStartResult.NoFreeSlot;
            var cost = CostOf(recipe);
            if (Sim.Supply.ResearchPoints < cost.ResearchPoints)
                return ExpansionStartResult.NoResearchPoints;
            if (!Sim.Resources.CanAfford(cost.StartCost))
                return ExpansionStartResult.InsufficientResources;
            return ExpansionStartResult.Ok;
        }

        public ExpansionStartResult TryStart(RecipeData recipe)
        {
            var result = CanStart(recipe);
            if (result != ExpansionStartResult.Ok)
                return result;
            var cost = CostOf(recipe);
            if (!Sim.Resources.TrySpend(cost.StartCost))
                return ExpansionStartResult.InsufficientResources;
            Sim.Supply.SetCounts(Sim.Supply.ResearchPoints - cost.ResearchPoints, Sim.Supply.FreeRepairs);
            _recipeProjects.Add(new ExpansionProject { Recipe = recipe, Duration = cost.Duration, PowerDemand = cost.PowerDemand });
            Sim.Report($"레시피 연구 시작: {recipe.DisplayName}", true);
            Changed?.Invoke();
            return ExpansionStartResult.Ok;
        }

        /// <summary>진행 중인 레시피 연구 취소 (비용 · 포인트 환급 없음).</summary>
        public bool Cancel(RecipeData recipe)
        {
            var p = ProjectOf(recipe);
            if (p == null)
                return false;
            _recipeProjects.Remove(p);
            Changed?.Invoke();
            return true;
        }

        // ---------------- 연계 연구 ----------------

        public bool IsDone(LinkedResearchData linked) => linked != null && _linkedDone.Contains(linked);

        /// <summary>필요한 상위 모듈이 하나라도 가동 중인지.</summary>
        public bool HasModule(LinkedResearchData linked)
        {
            if (linked == null || linked.RequiredModule == null)
                return false;
            RefreshActiveData();
            return _activeData.Contains(linked.RequiredModule);
        }

        /// <summary>효과가 켜져 있는지 (연구 완료 + 모듈 가동 중).</summary>
        public bool IsActive(LinkedResearchData linked) => IsDone(linked) && HasModule(linked);

        public ExpansionProject ProjectOf(LinkedResearchData linked)
        {
            foreach (var p in _linkedProjects)
                if (p.Linked == linked)
                    return p;
            return null;
        }

        public ExpansionStartResult CanStart(LinkedResearchData linked)
        {
            if (linked == null || linked.Level == null)
                return ExpansionStartResult.Unknown;
            if (IsDone(linked))
                return ExpansionStartResult.AlreadyDone;
            if (ProjectOf(linked) != null)
                return ExpansionStartResult.AlreadyResearching;
            if (!HasModule(linked))
                return ExpansionStartResult.ModuleMissing;
            if (Sim.Research.Projects.Count + _linkedProjects.Count >= Sim.Research.LabSlots)
                return ExpansionStartResult.NoFreeSlot;
            if (!Sim.Resources.CanAfford(linked.Level.StartCost))
                return ExpansionStartResult.InsufficientResources;
            return ExpansionStartResult.Ok;
        }

        public ExpansionStartResult TryStart(LinkedResearchData linked)
        {
            var result = CanStart(linked);
            if (result != ExpansionStartResult.Ok)
                return result;
            if (!Sim.Resources.TrySpend(linked.Level.StartCost))
                return ExpansionStartResult.InsufficientResources;
            _linkedProjects.Add(new ExpansionProject { Linked = linked, Duration = Math.Max(1f, linked.Level.Duration), PowerDemand = linked.Level.PowerDemand });
            Sim.Research.ReservedSlots = _linkedProjects.Count;
            Sim.Report($"연계 연구 시작: {linked.DisplayName}", true);
            Changed?.Invoke();
            return ExpansionStartResult.Ok;
        }

        public bool Cancel(LinkedResearchData linked)
        {
            var p = ProjectOf(linked);
            if (p == null)
                return false;
            _linkedProjects.Remove(p);
            Sim.Research.ReservedSlots = _linkedProjects.Count;
            Changed?.Invoke();
            return true;
        }

        // ---------------- 틱 ----------------

        public override void Tick(float dt)
        {
            RefreshActiveData();
            RecipeSlots = CountRecipeSlots();
            Sim.Research.ReservedSlots = _linkedProjects.Count;
            float rate = Math.Max(0f, Sim.Resources.PowerEfficiency) * Sim.Effects.ResearchSpeedMultiplier;
            _finished.Clear();
            for (int i = 0; i < _recipeProjects.Count; i++)
                Advance(_recipeProjects[i], i >= RecipeSlots, rate, dt);
            int free = Sim.Research.LabSlots - Sim.Research.Projects.Count;
            for (int i = 0; i < _linkedProjects.Count; i++)
            {
                var p = _linkedProjects[i];
                // 필요한 모듈을 잃으면 멈춤 (진행률 유지)
                Advance(p, i >= free || !_activeData.Contains(p.Linked.RequiredModule), rate, dt);
            }
            foreach (var p in _finished)
            {
                if (p.Recipe != null)
                {
                    _recipeProjects.Remove(p);
                    _researched.Add(p.Recipe);
                    Sim.Report($"레시피 해금: {p.Recipe.DisplayName} · 합성 창(K)에서 재료 · 규격 확인", true);
                    RecipeUnlocked?.Invoke(p.Recipe);
                }
                else
                {
                    _linkedProjects.Remove(p);
                    _linkedDone.Add(p.Linked);
                    Sim.Report($"연계 연구 완료: {p.Linked.DisplayName}", true);
                    LinkedCompleted?.Invoke(p.Linked);
                }
            }
            Sim.Research.ReservedSlots = _linkedProjects.Count;
            RefreshLinkedEffects(_finished.Count > 0);
            if (_finished.Count > 0 || _recipeProjects.Count > 0 || _linkedProjects.Count > 0)
                Changed?.Invoke();
        }

        private void Advance(ExpansionProject p, bool paused, float rate, float dt)
        {
            p.Paused = paused;
            p.Rate = paused ? 0f : rate;
            if (paused || dt <= 0f)
                return;
            p.Progress = Math.Min(1f, p.Progress + dt * p.Rate / Math.Max(1f, p.Duration));
            if (p.Progress >= 1f - 1e-5f)
                _finished.Add(p);
        }

        /// <summary>가동 중(연결 · 파손 아님 · 합성 중 아님)인 모듈 종류.</summary>
        private void RefreshActiveData()
        {
            _activeData.Clear();
            foreach (var m in Sim.Grid.Modules)
            {
                if (m.Data == null || !Sim.Connectivity.IsActive(m) || Sim.Damage.IsDamaged(m) || Sim.IsHalted(m))
                    continue;
                _activeData.Add(m.Data);
            }
        }

        private int CountRecipeSlots()
        {
            int n = 0;
            foreach (var m in Sim.Grid.Modules)
            {
                if (m.Data == null || m.Data.RecipeSlots <= 0)
                    continue;
                if (!Sim.Connectivity.IsActive(m) || Sim.Damage.IsDamaged(m) || Sim.IsHalted(m))
                    continue;
                n += m.Data.RecipeSlots;
            }
            return n;
        }

        /// <summary>켜진 연계 연구가 바뀌면 효과를 다시 넣음 (모듈을 잃거나 다시 지었을 때).</summary>
        private void RefreshLinkedEffects(bool force)
        {
            var key = new System.Text.StringBuilder();
            _linkedModifiers.Clear();
            foreach (var l in _config.Linked)
            {
                if (l == null || !_linkedDone.Contains(l) || l.RequiredModule == null || !_activeData.Contains(l.RequiredModule))
                    continue;
                key.Append(l.name).Append('|');
                _linkedModifiers.AddRange(l.Level.Modifiers);
            }
            string k = key.ToString();
            if (!force && k == _linkedKey)
                return;
            _linkedKey = k;
            Sim.Effects.SetLinked(_linkedModifiers);
            Sim.ApplyConsumptionMultipliers(); // 폐쇄 순환 = 주민 소비 배율
        }

        public override void OnModulePlaced(ModuleInstance module) => Changed?.Invoke();

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class ProjectSave
        {
            public string Name;
            public float Progress;
        }

        [Serializable]
        private sealed class State
        {
            public List<string> Researched = new List<string>();
            public List<string> LinkedDone = new List<string>();
            public List<ProjectSave> Recipes = new List<ProjectSave>();
            public List<ProjectSave> LinkedProjects = new List<ProjectSave>();
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            if (_researched.Count == 0 && _linkedDone.Count == 0 && _recipeProjects.Count == 0 && _linkedProjects.Count == 0)
                return null;
            var s = new State();
            foreach (var r in _researched)
                s.Researched.Add(r.name);
            foreach (var l in _linkedDone)
                s.LinkedDone.Add(l.name);
            foreach (var p in _recipeProjects)
                s.Recipes.Add(new ProjectSave { Name = p.Recipe.name, Progress = p.Progress });
            foreach (var p in _linkedProjects)
                s.LinkedProjects.Add(new ProjectSave { Name = p.Linked.name, Progress = p.Progress });
            return JsonUtility.ToJson(s);
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            foreach (var name in s.Researched)
            {
                var r = Sim.Synthesis != null ? Sim.Synthesis.FindRecipe(name) : null;
                if (r != null)
                    _researched.Add(r);
            }
            foreach (var name in s.LinkedDone)
            {
                var l = FindLinked(name);
                if (l != null)
                    _linkedDone.Add(l);
            }
            foreach (var p in s.Recipes)
            {
                var r = Sim.Synthesis != null ? Sim.Synthesis.FindRecipe(p.Name) : null;
                if (r == null || _researched.Contains(r) || ProjectOf(r) != null)
                    continue;
                var cost = CostOf(r);
                _recipeProjects.Add(new ExpansionProject { Recipe = r, Progress = Mathf.Clamp(p.Progress, 0f, 0.999f), Duration = cost.Duration, PowerDemand = cost.PowerDemand });
            }
            foreach (var p in s.LinkedProjects)
            {
                var l = FindLinked(p.Name);
                if (l == null || _linkedDone.Contains(l) || ProjectOf(l) != null)
                    continue;
                _linkedProjects.Add(new ExpansionProject { Linked = l, Progress = Mathf.Clamp(p.Progress, 0f, 0.999f), Duration = Math.Max(1f, l.Level.Duration), PowerDemand = l.Level.PowerDemand });
            }
            Sim.Research.ReservedSlots = _linkedProjects.Count;
            RefreshActiveData();
            RefreshLinkedEffects(true);
        }

        private LinkedResearchData FindLinked(string name)
        {
            foreach (var l in _config.Linked)
                if (l != null && l.name == name)
                    return l;
            return null;
        }
    }
}
