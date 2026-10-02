using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>한 모듈에 적용 중인 인접 규칙 하나.</summary>
    public struct AppliedAdjacency
    {
        public AdjacencyRule Rule;
        /// <summary>실제 적용된 중첩 수 (면제·최대 반영).</summary>
        public int Stacks;
        /// <summary>연구로 더해진 이웃당 값 (좋은 효과 쪽 부호, Phase 6).</summary>
        public float Boost;
        public float PerNeighbor => Rule.ValuePerNeighbor + Boost;
        public float Total => PerNeighbor * Stacks;
    }

    /// <summary>
    /// 공간 인접 효과 (GDD 13번, BALANCE 16번). 면이 맞닿은 이웃 모듈(물리적 인접, 활성 여부 무관)을 세어 규칙을 적용한다.
    /// 그리드가 바뀔 때만 <see cref="Recalculate"/>로 다시 계산하고 결과를 캐시한다.
    /// </summary>
    public sealed class AdjacencySystem
    {
        private sealed class Cached
        {
            public float Production;
            public float Consumption;
            public float Housing;
            public readonly List<AppliedAdjacency> Applied = new List<AppliedAdjacency>();
        }

        private readonly AdjacencyRuleSet _rules;
        private readonly Dictionary<ModuleInstance, Cached> _cache = new Dictionary<ModuleInstance, Cached>();
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private readonly List<ModuleData> _neighborData = new List<ModuleData>();
        private readonly List<AppliedAdjacency> _scratch = new List<AppliedAdjacency>();

        public AdjacencySystem(AdjacencyRuleSet rules)
        {
            _rules = rules; // null이면 효과 없음
        }

        public bool HasRules => _rules != null && _rules.Rules.Count > 0;

        /// <summary>Phase 6 생산 연구: 좋은 인접 효과(생산 +, 소비 −)를 이웃당 이만큼 더 강하게. null이면 0.</summary>
        public ResearchEffects Effects { get; set; }

        /// <summary>규칙에 더해질 연구 보정 (이웃당, 부호 포함). 수용 인구·나쁜 효과는 0.</summary>
        public float BoostFor(AdjacencyRule rule)
        {
            float boost = Effects != null ? Effects.AdjacencyBonusBoost : 0f;
            if (boost <= 0f || rule == null)
                return 0f;
            if (rule.Effect == AdjacencyEffect.Production && rule.ValuePerNeighbor > 0f)
                return boost;
            if (rule.Effect == AdjacencyEffect.Consumption && rule.ValuePerNeighbor < 0f)
                return -boost;
            return 0f;
        }

        public void Recalculate(StationGrid grid)
        {
            _cache.Clear();
            if (!HasRules)
                return;
            foreach (var module in grid.Modules)
            {
                if (module.Data == null)
                    continue;
                grid.GetNeighborModules(module, _neighbors);
                _neighborData.Clear();
                foreach (var n in _neighbors)
                    _neighborData.Add(n.Data);

                var cached = new Cached();
                Evaluate(module.Data, _neighborData, cached.Applied);
                foreach (var a in cached.Applied)
                    Accumulate(cached, a);
                if (cached.Applied.Count > 0)
                    _cache.Add(module, cached);
            }
        }

        /// <summary>대상 데이터와 이웃 목록으로 적용될 규칙을 계산한다 (규칙별 1항목).</summary>
        public void Evaluate(ModuleData target, IReadOnlyList<ModuleData> neighbors, List<AppliedAdjacency> results)
        {
            results.Clear();
            if (!HasRules || target == null)
                return;
            foreach (var rule in _rules.Rules)
            {
                if (rule.Target != target)
                    continue;
                int count = 0;
                foreach (var n in neighbors)
                {
                    if (n != null && rule.Matches(n))
                        count++;
                }
                int stacks = Math.Min(rule.MaxStacks, Math.Max(0, count - rule.FreeNeighbors));
                if (stacks > 0)
                    results.Add(new AppliedAdjacency { Rule = rule, Stacks = stacks, Boost = BoostFor(rule) });
            }
        }

        public float GetProductionMultiplier(ModuleInstance module)
            => _cache.TryGetValue(module, out var c) ? ClampMultiplier(1f + c.Production) : 1f;

        public float GetConsumptionMultiplier(ModuleInstance module)
            => _cache.TryGetValue(module, out var c) ? ClampMultiplier(1f + c.Consumption) : 1f;

        /// <summary>수용 인구 가감 (정수로 반올림, 모듈 기본 수용 인구 아래로는 내려가지 않게 호출자가 처리).</summary>
        public int GetHousingBonus(ModuleInstance module)
            => _cache.TryGetValue(module, out var c) ? Mathf.RoundToInt(c.Housing) : 0;

        public IReadOnlyList<AppliedAdjacency> GetApplied(ModuleInstance module)
            => _cache.TryGetValue(module, out var c) ? c.Applied : (IReadOnlyList<AppliedAdjacency>)Array.Empty<AppliedAdjacency>();

        /// <summary>
        /// 배치 미리보기: 새 모듈 자신에게 적용될 효과(self)와, 새 모듈 때문에 이웃에게 새로 생길 효과 설명(neighborLines).
        /// </summary>
        public void Preview(StationGrid grid, ModuleData data, Vector3Int origin, int rotation,
            List<AppliedAdjacency> self, List<string> neighborLines)
        {
            self.Clear();
            neighborLines.Clear();
            if (!HasRules || data == null)
                return;

            var cells = StationGrid.ResolveCells(data.CellOffsets, origin, rotation);
            _neighbors.Clear();
            foreach (var cell in cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    var n = cell + dir;
                    if (Array.IndexOf(cells, n) >= 0 || !grid.TryGetModule(n, out var other))
                        continue;
                    if (!_neighbors.Contains(other))
                        _neighbors.Add(other);
                }
            }
            _neighborData.Clear();
            foreach (var n in _neighbors)
                _neighborData.Add(n.Data);
            Evaluate(data, _neighborData, self);

            // 이웃 쪽: 이웃을 대상으로 하고 새 모듈을 이웃으로 인정하는 규칙 (중첩 여유가 있을 때만)
            foreach (var neighbor in _neighbors)
            {
                if (neighbor.Data == null)
                    continue;
                foreach (var rule in _rules.Rules)
                {
                    if (rule.Target != neighbor.Data || !rule.Matches(data))
                        continue;
                    int current = 0;
                    foreach (var a in GetApplied(neighbor))
                    {
                        if (a.Rule == rule)
                            current = a.Stacks;
                    }
                    grid.GetNeighborModules(neighbor, _scratchNeighbors);
                    int matching = 0;
                    foreach (var n in _scratchNeighbors)
                    {
                        if (n.Data != null && rule.Matches(n.Data))
                            matching++;
                    }
                    int after = Math.Min(rule.MaxStacks, Math.Max(0, matching + 1 - rule.FreeNeighbors));
                    if (after > current)
                        neighborLines.Add($"{neighbor.Data.DisplayName} {Describe(rule, after - current, BoostFor(rule))}");
                }
            }
        }

        private readonly List<ModuleInstance> _scratchNeighbors = new List<ModuleInstance>();

        /// <summary>적용 중인 효과 설명 (연구 보정 포함).</summary>
        public static string Describe(AppliedAdjacency applied) => Describe(applied.Rule, applied.Stacks, applied.Boost);

        /// <summary>"물 소비 -25%" 같은 짧은 설명. boost = 연구로 더해진 이웃당 값.</summary>
        public static string Describe(AdjacencyRule rule, int stacks, float boost = 0f)
        {
            float total = (rule.ValuePerNeighbor + boost) * stacks;
            string what;
            switch (rule.Effect)
            {
                case AdjacencyEffect.Production: what = ProductionName(rule.Target); break;
                case AdjacencyEffect.Consumption: what = ConsumptionName(rule.Target); break;
                default: what = "수용 인구"; break;
            }
            string amount = rule.Effect == AdjacencyEffect.Housing
                ? total.ToString("+0;-0")
                : (total * 100f).ToString("+0;-0") + "%";
            string label = string.IsNullOrEmpty(rule.Label) ? "" : $" ({rule.Label})";
            return $"{what} {amount}{label}";
        }

        /// <summary>건설 메뉴 툴팁용: 이 모듈이 대상이거나 이웃으로 영향을 주는 규칙 설명.</summary>
        public void DescribeRulesFor(ModuleData data, List<string> lines)
        {
            lines.Clear();
            if (!HasRules || data == null)
                return;
            foreach (var rule in _rules.Rules)
            {
                if (rule.Target == data)
                {
                    string who = rule.Neighbor != null ? rule.Neighbor.DisplayName
                        : rule.ExcludeSameType ? $"{data.DisplayName} 외 모든 모듈" : "모든 모듈";
                    string free = rule.FreeNeighbors > 0 ? $", {rule.FreeNeighbors}개 면제" : "";
                    lines.Add($"{who} 옆: {Describe(rule, 1, BoostFor(rule))} / 개 (최대 {rule.MaxStacks}회{free})");
                }
                else if (rule.Neighbor == data && rule.Target != null)
                {
                    lines.Add($"{rule.Target.DisplayName} 옆에 두면 그 모듈 {Describe(rule, 1, BoostFor(rule))}");
                }
            }
        }

        public static string DescribeAll(IReadOnlyList<AppliedAdjacency> applied)
        {
            var sb = new StringBuilder();
            foreach (var a in applied)
            {
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(Describe(a));
            }
            return sb.ToString();
        }

        private float ClampMultiplier(float value)
        {
            return Mathf.Clamp(value, _rules.MinMultiplier, _rules.MaxMultiplier);
        }

        private static void Accumulate(Cached cached, AppliedAdjacency a)
        {
            switch (a.Rule.Effect)
            {
                case AdjacencyEffect.Production: cached.Production += a.Total; break;
                case AdjacencyEffect.Consumption: cached.Consumption += a.Total; break;
                case AdjacencyEffect.Housing: cached.Housing += a.Total; break;
            }
        }

        private static string ProductionName(ModuleData target)
        {
            if (target != null)
            {
                foreach (var p in target.Production)
                {
                    if (p.Amount > 0f)
                        return p.Type == ResourceType.Power ? "발전" : p.Type.DisplayName() + " 생산";
                }
            }
            return "생산";
        }

        private static string ConsumptionName(ModuleData target)
        {
            if (target != null)
            {
                foreach (var c in target.Consumption)
                {
                    if (c.Type != ResourceType.Power && c.Amount > 0f)
                        return c.Type.DisplayName() + " 소비";
                }
            }
            return "소비";
        }
    }
}
