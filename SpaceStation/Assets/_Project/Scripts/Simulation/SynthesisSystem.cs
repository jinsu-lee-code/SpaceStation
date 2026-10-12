using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>12-3 합성 판정 결과 (UI 사유 표시 순서와 같음).</summary>
    public enum SynthesisResult
    {
        Valid,
        /// <summary>레시피 · 결과 모듈 · 재료가 비어 있음.</summary>
        InvalidRecipe,
        /// <summary>등급 미달 또는 해금 전.</summary>
        Locked,
        /// <summary>규격 안에 빈 칸이 있음.</summary>
        EmptyCell,
        /// <summary>규격 밖으로 튀어나온 모듈이 있음 (모듈이 통째로 규격 안에 있어야 함).</summary>
        Overhang,
        /// <summary>레시피에 없는 모듈(코어 포함)이 섞여 있음.</summary>
        Mixed,
        /// <summary>재료 구성(종류별 개수)이 레시피와 다름.</summary>
        Missing,
        /// <summary>이미 다른 합성에 쓰이는 중인 재료가 있음.</summary>
        Busy,
        /// <summary>파손 · 수리 중(대기 포함)인 재료가 있음 (A8).</summary>
        Damaged,
        InsufficientResources,
    }

    /// <summary>재료 한 종류의 필요 · 현재 개수 (규격 안 기준). 레시피에 없는 모듈은 Needed 0.</summary>
    public struct IngredientStatus
    {
        public ModuleData Module;
        public int Needed;
        public int Present;
    }

    /// <summary>진행 중인 합성 하나.</summary>
    public sealed class SynthesisJob
    {
        public RecipeData Recipe { get; internal set; }
        public Vector3Int Origin { get; internal set; }
        public int Rotation { get; internal set; }
        public IReadOnlyList<ModuleInstance> Materials => _materials;
        /// <summary>남은 시간 (게임 초).</summary>
        public float Remaining { get; internal set; }
        /// <summary>파손된 재료가 있어 멈춰 있음 (마지막 틱 기준).</summary>
        public bool Paused { get; internal set; }
        public float Progress01 => Recipe == null || Recipe.Duration <= 0f ? 1f : Mathf.Clamp01(1f - Remaining / Recipe.Duration);

        internal readonly List<ModuleInstance> _materials = new List<ModuleInstance>();
    }

    /// <summary>
    /// 12-3 합성 시스템 (PHASESET.md 3번 A, 12-0b 공통 틀). 레시피 규격(결과 모듈의 점유 칸)을 재료 모듈로 빈틈없이 채운 자리에서
    /// 비용을 내고 합성을 시작하면, 합성 시간 동안 재료는 가동이 멈추고(수리 중과 같음 — 생산 · 가동률 · 슬롯 0, 수용 · 저장은 유지)
    /// 끝나면 재료가 사라지고 같은 칸에 결과 모듈이 내구도 100%로 생긴다 (모듈 수 = 재료 수, 분해 불가).
    /// 사용자 결정 (2026-10-12): 합성 중 재료가 파손되면 합성 시간이 멈추고 수리되면 이어감 / 합성 중 재료는 철거 · 재건축 불가 /
    /// 진화 모듈 건설비(수리 · 정비 · 재건축 · 철거 환급 기준) = 재료 건설비 합 + 합성 비용 (결과 ModuleData의 BuildCost에 넣어 둠).
    /// 재료가 파괴되면(방치 등) 그 합성은 취소되고 비용은 돌려받지 못한다.
    /// </summary>
    public sealed class SynthesisSystem : StationSystem
    {
        public const string SystemKey = "synthesis";

        private readonly List<RecipeData> _recipes = new List<RecipeData>();
        private readonly List<SynthesisJob> _jobs = new List<SynthesisJob>();
        private readonly Dictionary<ModuleInstance, SynthesisJob> _jobOf = new Dictionary<ModuleInstance, SynthesisJob>();
        private readonly HashSet<ModuleInstance> _seen = new HashSet<ModuleInstance>();
        private readonly List<ModuleInstance> _scratch = new List<ModuleInstance>();
        private bool _completing;

        /// <summary>합성 시작.</summary>
        public event Action<SynthesisJob> Started;
        /// <summary>합성 끝 (결과 모듈).</summary>
        public event Action<SynthesisJob, ModuleInstance> Completed;
        /// <summary>재료가 파괴되어 취소됨.</summary>
        public event Action<SynthesisJob> Cancelled;
        /// <summary>합성이 멈춤 · 다시 진행 (파손 재료).</summary>
        public event Action<SynthesisJob> PausedChanged;

        public SynthesisSystem(IEnumerable<RecipeData> recipes)
        {
            if (recipes != null)
                foreach (var r in recipes)
                    if (r != null && !_recipes.Contains(r))
                        _recipes.Add(r);
        }

        public override string Key => SystemKey;
        public IReadOnlyList<RecipeData> Recipes => _recipes;
        public IReadOnlyList<SynthesisJob> Jobs => _jobs;

        // ---------------- 조회 ----------------

        /// <summary>등급(단계)이 열렸고 해금된 레시피인지 (12-3: 연구 해금 전이라 <see cref="RecipeData.UnlockedByDefault"/>만).</summary>
        public bool IsUnlocked(RecipeData recipe)
            => recipe != null && recipe.UnlockedByDefault && Sim.Progression.GradeIndex >= recipe.UnlockGrade;

        /// <summary>합성으로만 만드는 진화 모듈인지.</summary>
        public bool IsRecipeResult(ModuleData data)
        {
            if (data == null)
                return false;
            foreach (var r in _recipes)
                if (r.Result == data)
                    return true;
            return false;
        }

        /// <summary>이 모듈을 결과로 내는 레시피 (없으면 null).</summary>
        public RecipeData RecipeFor(ModuleData result)
        {
            foreach (var r in _recipes)
                if (r.Result != null && r.Result == result)
                    return r;
            return null;
        }

        public ModuleData FindResult(string assetName)
        {
            foreach (var r in _recipes)
                if (r.Result != null && r.Result.name == assetName)
                    return r.Result;
            return null;
        }

        public RecipeData FindRecipe(string assetName)
        {
            foreach (var r in _recipes)
                if (r.name == assetName)
                    return r;
            return null;
        }

        /// <summary>합성에 쓰이는 중 (가동 정지 · 철거 · 재건축 불가).</summary>
        public bool IsHalted(ModuleInstance module) => module != null && _jobOf.ContainsKey(module);

        public SynthesisJob JobOf(ModuleInstance module) => module != null && _jobOf.TryGetValue(module, out var job) ? job : null;

        /// <summary>실제 합성 비용 (건설 연구 할인 적용).</summary>
        public List<ResourceAmount> GetCost(RecipeData recipe)
        {
            var cost = new List<ResourceAmount>();
            if (recipe == null)
                return cost;
            float m = Sim.Effects.BuildCostMultiplier;
            foreach (var a in recipe.Cost)
                cost.Add(new ResourceAmount(a.Type, a.Amount * m));
            return cost;
        }

        // ---------------- 판정 ----------------

        /// <summary>
        /// 이 자리 · 회전에서 합성할 수 있는지. materials = 규격 안 모듈(서로 다른 것만), statuses = 재료 종류별 필요 · 현재 개수
        /// (레시피에 없는 모듈도 Needed 0으로 들어감). 둘 다 null 가능.
        /// </summary>
        public SynthesisResult Evaluate(RecipeData recipe, Vector3Int origin, int rotation,
            List<ModuleInstance> materials = null, List<IngredientStatus> statuses = null)
        {
            materials?.Clear();
            statuses?.Clear();
            if (recipe == null || recipe.Result == null || recipe.Ingredients.Count == 0)
                return SynthesisResult.InvalidRecipe;

            var grid = Sim.Grid;
            var cells = StationGrid.ResolveCells(recipe.Result.CellOffsets, origin, rotation);
            var found = materials ?? _scratch;
            if (materials == null)
                _scratch.Clear();
            _seen.Clear();
            bool empty = false;
            foreach (var c in cells)
            {
                if (!grid.TryGetModule(c, out var m))
                {
                    empty = true;
                    continue;
                }
                if (_seen.Add(m))
                    found.Add(m);
            }

            // 종류별 개수 (레시피 순서 → 섞인 모듈)
            if (statuses != null)
            {
                foreach (var ing in recipe.Ingredients)
                {
                    if (ing.Module == null)
                        continue;
                    int at = IndexOf(statuses, ing.Module);
                    if (at >= 0)
                    {
                        var s = statuses[at];
                        s.Needed += ing.Count;
                        statuses[at] = s;
                    }
                    else
                        statuses.Add(new IngredientStatus { Module = ing.Module, Needed = ing.Count });
                }
                foreach (var m in found)
                {
                    int at = IndexOf(statuses, m.Data);
                    if (at >= 0)
                    {
                        var s = statuses[at];
                        s.Present++;
                        statuses[at] = s;
                    }
                    else
                        statuses.Add(new IngredientStatus { Module = m.Data, Needed = 0, Present = 1 });
                }
            }

            if (!IsUnlocked(recipe))
                return SynthesisResult.Locked;
            if (empty)
                return SynthesisResult.EmptyCell;
            foreach (var m in found)
            {
                foreach (var c in m.Cells)
                    if (Array.IndexOf(cells, c) < 0)
                        return SynthesisResult.Overhang;
            }
            foreach (var m in found)
            {
                if (m == Sim.Core || m.Data == null || recipe.CountOf(m.Data) == 0)
                    return SynthesisResult.Mixed;
            }
            foreach (var ing in recipe.Ingredients)
            {
                if (ing.Module == null)
                    continue;
                int present = 0;
                foreach (var m in found)
                    if (m.Data == ing.Module)
                        present++;
                if (present != recipe.CountOf(ing.Module))
                    return SynthesisResult.Missing;
            }
            foreach (var m in found)
                if (IsHalted(m))
                    return SynthesisResult.Busy;
            foreach (var m in found)
                if (Sim.Damage.IsDamaged(m))
                    return SynthesisResult.Damaged;
            if (!Sim.Resources.CanAfford(GetCost(recipe)))
                return SynthesisResult.InsufficientResources;
            return SynthesisResult.Valid;
        }

        private static int IndexOf(List<IngredientStatus> list, ModuleData data)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i].Module == data)
                    return i;
            return -1;
        }

        /// <summary>비용을 내고 합성 시작 (재료 가동 정지).</summary>
        public SynthesisResult TryStart(RecipeData recipe, Vector3Int origin, int rotation, out SynthesisJob job)
        {
            job = null;
            var materials = new List<ModuleInstance>();
            var result = Evaluate(recipe, origin, rotation, materials);
            if (result != SynthesisResult.Valid)
                return result;
            if (!Sim.Resources.TrySpend(GetCost(recipe)))
                return SynthesisResult.InsufficientResources;
            job = new SynthesisJob { Recipe = recipe, Origin = origin, Rotation = GridDirections.NormalizeRotation(rotation), Remaining = recipe.Duration };
            job._materials.AddRange(materials);
            Add(job);
            Sim.RefreshModuleStats(); // 가동 정지를 바로 반영 (슬롯 · 저장 한도)
            Started?.Invoke(job);
            if (job.Remaining <= 0f)
                Complete(job);
            return SynthesisResult.Valid;
        }

        private void Add(SynthesisJob job)
        {
            _jobs.Add(job);
            foreach (var m in job._materials)
                _jobOf[m] = job;
        }

        private void Remove(SynthesisJob job)
        {
            _jobs.Remove(job);
            foreach (var m in job._materials)
                _jobOf.Remove(m);
        }

        // ---------------- 진행 ----------------

        public override void Tick(float dt)
        {
            for (int i = 0; i < _jobs.Count; i++)
            {
                var job = _jobs[i];
                bool paused = false;
                foreach (var m in job._materials)
                    if (Sim.Damage.IsDamaged(m))
                        paused = true;
                if (paused != job.Paused)
                {
                    job.Paused = paused;
                    PausedChanged?.Invoke(job);
                }
                if (paused || dt <= 0f)
                    continue;
                job.Remaining -= dt;
                if (job.Remaining <= 1e-4f)
                {
                    Complete(job);
                    i--; // 목록에서 빠짐
                }
            }
        }

        /// <summary>재료를 치우고 같은 칸에 결과 모듈을 놓는다 (등급 재판정은 교체가 끝난 뒤 한 번).</summary>
        private void Complete(SynthesisJob job)
        {
            Remove(job);
            ModuleInstance result;
            _completing = true;
            Sim.HoldProgression(true); // 재료를 하나씩 치우는 동안 모듈 수가 잠깐 줄어 등급이 오르내리지 않게
            try
            {
                foreach (var m in job._materials)
                    Sim.Grid.Remove(m);
                Sim.Grid.TryPlace(job.Recipe.Result, job.Origin, job.Rotation, out result); // 같은 칸이 비었으므로 항상 성공
            }
            finally
            {
                _completing = false;
                Sim.HoldProgression(false);
            }
            Sim.Session.RecordSynthesis();
            Sim.Report($"합성 완료: {job.Recipe.DisplayName}", true);
            Completed?.Invoke(job, result);
        }

        public override void OnModuleRemoved(ModuleInstance module)
        {
            if (_completing)
                return;
            var job = JobOf(module);
            if (job == null)
                return;
            // 재료가 파괴됨 (방치 · 노후 등) → 합성 취소, 비용은 돌려받지 못함
            Remove(job);
            Sim.Report($"합성 취소: {job.Recipe.DisplayName} (재료 파괴)", false);
            Cancelled?.Invoke(job);
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public List<JobState> Jobs = new List<JobState>();
        }

        [Serializable]
        private sealed class JobState
        {
            public string Recipe;
            public Vector3Int Origin;
            public int Rotation;
            public float Remaining;
            public List<int> Materials = new List<int>();
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            if (_jobs.Count == 0)
                return null;
            var s = new State();
            foreach (var job in _jobs)
            {
                var j = new JobState { Recipe = job.Recipe.name, Origin = job.Origin, Rotation = job.Rotation, Remaining = job.Remaining };
                foreach (var m in job._materials)
                    j.Materials.Add(indexOf(m));
                s.Jobs.Add(j);
            }
            return JsonUtility.ToJson(s);
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s?.Jobs == null)
                return;
            foreach (var j in s.Jobs)
            {
                var recipe = FindRecipe(j.Recipe);
                if (recipe == null)
                    continue; // 레시피가 사라짐 → 재료는 그대로 남음
                var job = new SynthesisJob { Recipe = recipe, Origin = j.Origin, Rotation = j.Rotation, Remaining = j.Remaining };
                bool ok = j.Materials.Count > 0;
                foreach (int i in j.Materials)
                {
                    var m = moduleAt(i);
                    if (m == null || _jobOf.ContainsKey(m))
                        ok = false;
                    else
                        job._materials.Add(m);
                }
                if (ok)
                    Add(job);
            }
        }
    }
}
