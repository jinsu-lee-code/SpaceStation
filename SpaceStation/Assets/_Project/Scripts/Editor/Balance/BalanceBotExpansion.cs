using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 12-4 봇 확장 (사용자 결정 2026-10-14: 봇이 연구 · 해금 · 합성까지). 도시급(5)부터만 동작해 그 전 기준선 결과는 그대로.
    /// - 보급 상자: 플레이어가 내부에서 줍는다고 보고 매 결정마다 모두 주움 (연구 포인트 · 자원).
    /// - 레시피 해금 연구: 그림자 레시피를 단계 · 목록 순으로, 자원이 보호선(산소 · 물 40%, 금속 40) 위로 남을 때.
    /// - 연계 연구: 필요한 상위 모듈이 있고 빈 연구소가 있으면.
    /// - 합성: 해금된 레시피마다 결과 모듈 1개까지. 금속이 저장 한도의 60% 이상이고 생활 자원이 안정적일 때 규격 자리를 정해
    ///   (결과 모듈을 놓을 수 있는 자리 = 기존 배치 탐색) 재료 모듈로 빈틈없이 채우는 배치를 찾아(작은 완전 탐색) 하나씩 짓고, 다 지으면 합성 시작.
    ///   다른 건설이 규격 칸을 차지하면 계획을 버림.
    /// </summary>
    public sealed partial class BalanceBot
    {
        /// <summary>12-4: 연구 확장 · 합성을 하는지 (비교 측정용, 기본 true).</summary>
        public static bool UseExpansion = true;
        private const int ExpansionMinGrade = 5;
        private const float SynthesisMetalRatio = 0.6f;
        private const float PieceMetalRatio = 0.3f;

        public int CratesPicked { get; private set; }
        public int RecipesResearched { get; private set; }
        public int LinkedResearched { get; private set; }
        public int SynthesesStarted { get; private set; }

        private sealed class SynthesisPlan
        {
            public RecipeData Recipe;
            public Vector3Int Origin;
            public int Rotation;
            public HashSet<Vector3Int> Cells = new HashSet<Vector3Int>();
            public readonly List<(ModuleData Data, Vector3Int Origin, int Rotation)> Pieces = new List<(ModuleData, Vector3Int, int)>();
        }

        private SynthesisPlan _plan;
        private readonly HashSet<RecipeData> _failedRecipes = new HashSet<RecipeData>();
        private int _failedAtModules = -1;

        private void DecideExpansion()
        {
            if (!UseExpansion || _sim.Progression.GradeIndex < ExpansionMinGrade)
                return;
            PickUpCrates();
            var ex = _sim.ResearchExpansion;
            if (ex != null)
            {
                TryRecipeResearch(ex);
                TryLinkedResearch(ex);
            }
            if (_sim.Synthesis != null)
                TrySynthesis();
        }

        private readonly List<int> _crateIds = new List<int>();

        private void PickUpCrates()
        {
            _crateIds.Clear();
            foreach (var c in _sim.Supply.Crates)
                _crateIds.Add(c.Id);
            foreach (int id in _crateIds)
                if (_sim.TryPickupCrate(id, out _))
                    CratesPicked++;
        }

        private bool SafeToSpend(IReadOnlyList<ResourceAmount> cost)
        {
            var r = _sim.Resources;
            foreach (var a in cost)
            {
                float reserve = a.Type == ResourceType.Metal ? 40f : r.GetCapacity(a.Type) * 0.4f;
                if (r.GetStock(a.Type) - a.Amount < reserve)
                    return false;
            }
            return true;
        }

        private void TryRecipeResearch(ResearchExpansionSystem ex)
        {
            for (int stage = 1; stage <= 4; stage++)
            {
                foreach (var recipe in _sim.Synthesis.Recipes)
                {
                    if (recipe.Stage != stage || ex.CanStart(recipe) != ExpansionStartResult.Ok)
                        continue;
                    var cost = ex.CostOf(recipe);
                    if (PowerSurplus() < cost.PowerDemand || !SafeToSpend(cost.StartCost))
                        continue;
                    if (ex.TryStart(recipe) == ExpansionStartResult.Ok)
                    {
                        RecipesResearched++;
                        return;
                    }
                }
            }
        }

        private void TryLinkedResearch(ResearchExpansionSystem ex)
        {
            foreach (var l in ex.Linked)
            {
                if (l == null || ex.CanStart(l) != ExpansionStartResult.Ok)
                    continue;
                if (PowerSurplus() < l.Level.PowerDemand || !SafeToSpend(l.Level.StartCost))
                    continue;
                if (ex.TryStart(l) == ExpansionStartResult.Ok)
                {
                    LinkedResearched++;
                    return;
                }
            }
        }

        // ---------------- 합성 ----------------

        private void TrySynthesis()
        {
            var r = _sim.Resources;
            if (_plan != null && !ContinuePlan())
                _plan = null;
            if (_plan != null)
                return;
            if (r.GetStock(ResourceType.Metal) < r.GetCapacity(ResourceType.Metal) * SynthesisMetalRatio || !LifeSupportStable(r))
                return;
            if (_failedAtModules != _sim.Grid.ModuleCount)
            {
                _failedRecipes.Clear(); // 정거장 모양이 바뀌면 자리 탐색을 다시 해 봄
                _failedAtModules = _sim.Grid.ModuleCount;
            }
            foreach (var recipe in _sim.Synthesis.Recipes)
            {
                if (recipe.Result == null || _failedRecipes.Contains(recipe) || !_sim.Synthesis.IsUnlocked(recipe))
                    continue;
                if (CountOf(recipe.Result) + RunningJobs(recipe) >= 1)
                    continue;
                bool buildable = true;
                foreach (var ing in recipe.Ingredients)
                    buildable &= ing.Module != null && _sim.CheckBuildable(ing.Module) == PlacementResult.Valid;
                if (!buildable)
                    continue;
                var plan = MakePlan(recipe);
                if (plan == null)
                {
                    _failedRecipes.Add(recipe);
                    continue;
                }
                _plan = plan;
                if (!ContinuePlan())
                    _plan = null;
                return;
            }
        }

        private int RunningJobs(RecipeData recipe)
        {
            int n = 0;
            foreach (var job in _sim.Synthesis.Jobs)
                if (job.Recipe == recipe)
                    n++;
            return n;
        }

        /// <summary>계획을 한 걸음 진행 (재료 하나 짓기 또는 합성 시작). false = 계획을 버림.</summary>
        private bool ContinuePlan()
        {
            var p = _plan;
            var grid = _sim.Grid;
            // 다른 건설이 규격 칸을 차지했는지
            foreach (var c in p.Cells)
            {
                if (!grid.TryGetModule(c, out var m))
                    continue;
                bool ours = false;
                foreach (var piece in p.Pieces)
                    ours |= m.Data == piece.Data && m.Origin == piece.Origin;
                if (!ours)
                    return false;
            }
            // 아직 안 지은 재료: 정거장에 맞닿은 것부터 (연결 · 받침)
            bool remaining = false;
            foreach (var piece in p.Pieces)
            {
                if (grid.TryGetModule(piece.Origin, out var existing) && existing.Data == piece.Data && existing.Origin == piece.Origin)
                    continue;
                remaining = true;
                if (_sim.Resources.GetStock(ResourceType.Metal) < _sim.Resources.GetCapacity(ResourceType.Metal) * PieceMetalRatio || !_sim.CanAfford(piece.Data))
                    return true; // 저축
                if (_sim.TryPlace(piece.Data, piece.Origin, piece.Rotation, out _))
                {
                    ModulesBuilt++;
                    return true;
                }
            }
            if (remaining)
                return true; // 아직 놓을 수 없는 조각 (받침 · 연결 대기) — 다음 결정에
            var result = _sim.Synthesis.Evaluate(p.Recipe, p.Origin, p.Rotation);
            if (result != SynthesisResult.Valid)
                return result == SynthesisResult.Damaged; // 파손 재료는 수리를 기다림
            if (!_sim.Resources.CanAfford(_sim.Synthesis.GetCost(p.Recipe)))
                return true;
            if (_sim.Synthesis.TryStart(p.Recipe, p.Origin, p.Rotation, out _) == SynthesisResult.Valid)
                SynthesesStarted++;
            return false; // 끝 (다음 레시피로)
        }

        /// <summary>결과 모듈을 놓을 수 있는 자리 = 규격 자리, 재료로 빈틈없이 채우는 배치 (없으면 null).</summary>
        private SynthesisPlan MakePlan(RecipeData recipe)
        {
            if (!TryFindPlacement(recipe.Result, out var origin, out int rotation))
                return null;
            var plan = new SynthesisPlan { Recipe = recipe, Origin = origin, Rotation = rotation };
            foreach (var c in StationGrid.ResolveCells(recipe.Result.CellOffsets, origin, rotation))
                plan.Cells.Add(c);
            var pieces = new List<ModuleData>();
            foreach (var ing in recipe.Ingredients)
                for (int i = 0; i < ing.Count; i++)
                    pieces.Add(ing.Module);
            pieces.Sort((a, b) => b.CellOffsets.Count.CompareTo(a.CellOffsets.Count)); // 큰 조각부터
            var covered = new HashSet<Vector3Int>();
            return Tile(plan, pieces, new bool[pieces.Count], covered) ? plan : null;
        }

        /// <summary>남은 첫 빈 칸을 덮을 재료 · 회전 · 위치를 하나씩 시도 (규격 칸 ≤ 9개라 금방 끝남).</summary>
        private bool Tile(SynthesisPlan plan, List<ModuleData> pieces, bool[] used, HashSet<Vector3Int> covered)
        {
            Vector3Int? target = null;
            foreach (var c in plan.Cells)
            {
                if (!covered.Contains(c) && (target == null || Less(c, target.Value)))
                    target = c;
            }
            if (target == null)
                return true;
            var tried = new HashSet<ModuleData>();
            for (int i = 0; i < pieces.Count; i++)
            {
                if (used[i] || !tried.Add(pieces[i]))
                    continue;
                var data = pieces[i];
                for (int rot = 0; rot < 4; rot++)
                {
                    var rel = StationGrid.ResolveCells(data.CellOffsets, Vector3Int.zero, rot);
                    foreach (var anchor in rel)
                    {
                        var origin = target.Value - anchor;
                        bool fits = true;
                        foreach (var c in rel)
                        {
                            var w = c + origin;
                            if (!plan.Cells.Contains(w) || covered.Contains(w))
                            {
                                fits = false;
                                break;
                            }
                        }
                        if (!fits)
                            continue;
                        foreach (var c in rel)
                            covered.Add(c + origin);
                        used[i] = true;
                        plan.Pieces.Add((data, origin, rot));
                        if (Tile(plan, pieces, used, covered))
                            return true;
                        plan.Pieces.RemoveAt(plan.Pieces.Count - 1);
                        used[i] = false;
                        foreach (var c in rel)
                            covered.Remove(c + origin);
                    }
                }
            }
            return false;
        }

        private static bool Less(Vector3Int a, Vector3Int b)
        {
            if (a.y != b.y) return a.y < b.y;
            if (a.z != b.z) return a.z < b.z;
            return a.x < b.x;
        }
    }
}
