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
        private const int ExpansionMinGrade = 4; // 도시급 (13-0 7단계)
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
            /// <summary>조각마다: 먼저 할 하위 합성 번호 (-1 = 바로 쓰는 재료).</summary>
            public readonly List<int> PieceSub = new List<int>();
            /// <summary>13-4 2단계: 규격 안의 진화 모듈 자리마다 그 자리에서 먼저 할 1단계 합성.</summary>
            public readonly List<(RecipeData Recipe, Vector3Int Origin, int Rotation)> Subs = new List<(RecipeData, Vector3Int, int)>();
        }

        private SynthesisPlan _plan;
        private readonly HashSet<RecipeData> _failedRecipes = new HashSet<RecipeData>();
        private int _failedAtModules = -1;

        private void DecideExpansion()
        {
            // 13-4 타이쿤판: 규칙을 정하는 쪽(해금 선택)이 있으면 '합성 1단계'가 열린 때부터 (중형에도 가능)
            bool open = _sim.Rules != null ? _sim.Rules.Allows(StationRule.Synthesis) : _sim.Progression.GradeIndex >= ExpansionMinGrade;
            if (!UseExpansion || !open)
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
                    buildable &= ing.Module != null && (_sim.CheckBuildable(ing.Module) == PlacementResult.Valid || SubRecipe(ing.Module) != null);
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

        /// <summary>13-4: 이 진화 모듈을 만드는 열린 레시피 (재료가 모두 건설 가능할 때만 — 한 단계 아래까지). 없으면 null.</summary>
        private RecipeData SubRecipe(ModuleData result)
        {
            foreach (var r in _sim.Synthesis.Recipes)
            {
                if (r.Result != result || !_sim.Synthesis.IsUnlocked(r))
                    continue;
                bool ok = true;
                foreach (var ing in r.Ingredients)
                    ok &= ing.Module != null && _sim.CheckBuildable(ing.Module) == PlacementResult.Valid;
                if (ok)
                    return r;
            }
            return null;
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
            // 다른 건설이 규격 칸을 차지했는지 (재료 조각 또는 하위 합성 결과만 허용)
            foreach (var c in p.Cells)
            {
                if (!grid.TryGetModule(c, out var m))
                    continue;
                bool ours = false;
                foreach (var piece in p.Pieces)
                    ours |= m.Data == piece.Data && m.Origin == piece.Origin;
                foreach (var sub in p.Subs)
                    ours |= m.Data == sub.Recipe.Result && m.Origin == sub.Origin;
                if (!ours)
                    return false;
            }
            // 아직 안 지은 재료: 정거장에 맞닿은 것부터 (연결 · 받침). 하위 합성이 끝난 자리의 조각은 이미 쓰였으므로 건너뜀
            bool remaining = false;
            for (int pi = 0; pi < p.Pieces.Count; pi++)
            {
                var piece = p.Pieces[pi];
                if (p.PieceSub[pi] >= 0 && SubDone(p.Subs[p.PieceSub[pi]]))
                    continue;
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
            // 13-4 2단계: 진화 모듈 자리마다 1단계 합성을 먼저
            foreach (var sub in p.Subs)
            {
                if (SubDone(sub))
                    continue;
                foreach (var job in _sim.Synthesis.Jobs)
                    if (job.Recipe == sub.Recipe && job.Origin == sub.Origin)
                        return true; // 진행 중
                var subResult = _sim.Synthesis.Evaluate(sub.Recipe, sub.Origin, sub.Rotation);
                if (subResult != SynthesisResult.Valid)
                    return subResult == SynthesisResult.Damaged;
                if (!_sim.Resources.CanAfford(_sim.Synthesis.GetCost(sub.Recipe)))
                    return true;
                if (_sim.Synthesis.TryStart(sub.Recipe, sub.Origin, sub.Rotation, out _) == SynthesisResult.Valid)
                    SynthesesStarted++;
                return true;
            }
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
            var top = new List<(ModuleData Data, Vector3Int Origin, int Rotation)>();
            if (!TileShape(plan.Cells, Pieces(recipe), top))
                return null;
            // 13-4 2단계: 진화 모듈 조각은 그 자리를 1단계 재료로 다시 채우고, 그 자리에서 1단계 합성
            foreach (var piece in top)
            {
                if (_sim.CheckBuildable(piece.Data) == PlacementResult.Valid)
                {
                    plan.Pieces.Add(piece);
                    plan.PieceSub.Add(-1);
                    continue;
                }
                var sub = SubRecipe(piece.Data);
                if (sub == null)
                    return null;
                var subCells = new HashSet<Vector3Int>(StationGrid.ResolveCells(piece.Data.CellOffsets, piece.Origin, piece.Rotation));
                var subPieces = new List<(ModuleData Data, Vector3Int Origin, int Rotation)>();
                if (!TileShape(subCells, Pieces(sub), subPieces))
                    return null;
                plan.Subs.Add((sub, piece.Origin, piece.Rotation));
                foreach (var sp in subPieces)
                {
                    plan.Pieces.Add(sp);
                    plan.PieceSub.Add(plan.Subs.Count - 1);
                }
            }
            return plan;
        }

        private static List<ModuleData> Pieces(RecipeData recipe)
        {
            var pieces = new List<ModuleData>();
            foreach (var ing in recipe.Ingredients)
                for (int i = 0; i < ing.Count; i++)
                    pieces.Add(ing.Module);
            pieces.Sort((a, b) => b.CellOffsets.Count.CompareTo(a.CellOffsets.Count)); // 큰 조각부터
            return pieces;
        }

        /// <summary>규격 칸을 조각들로 빈틈없이 채우는 배치 (output에 채움, 실패면 false).</summary>
        private static bool TileShape(HashSet<Vector3Int> cells, List<ModuleData> pieces, List<(ModuleData Data, Vector3Int Origin, int Rotation)> output)
            => Tile(cells, pieces, new bool[pieces.Count], new HashSet<Vector3Int>(), output);

        /// <summary>13-4 하위 합성이 끝났는지 (그 자리에 결과 진화 모듈이 있음).</summary>
        private bool SubDone((RecipeData Recipe, Vector3Int Origin, int Rotation) sub)
            => _sim.Grid.TryGetModule(sub.Origin, out var m) && m.Data == sub.Recipe.Result && m.Origin == sub.Origin;

        /// <summary>남은 첫 빈 칸을 덮을 재료 · 회전 · 위치를 하나씩 시도 (규격 칸이 작아 금방 끝남).</summary>
        private static bool Tile(HashSet<Vector3Int> cells, List<ModuleData> pieces, bool[] used, HashSet<Vector3Int> covered,
            List<(ModuleData Data, Vector3Int Origin, int Rotation)> output)
        {
            Vector3Int? target = null;
            foreach (var c in cells)
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
                            if (!cells.Contains(w) || covered.Contains(w))
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
                        output.Add((data, origin, rot));
                        if (Tile(cells, pieces, used, covered, output))
                            return true;
                        output.RemoveAt(output.Count - 1);
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
