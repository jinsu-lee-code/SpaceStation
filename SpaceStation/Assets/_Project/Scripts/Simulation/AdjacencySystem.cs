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
        /// <summary>규칙이 걸린 모듈 종류 (CONNECT A2 — 태그로 정한 규칙은 <see cref="AdjacencyRule.Target"/>이 비어 있음).</summary>
        public ModuleData Target;
        /// <summary>실제 적용된 중첩 수 (면제·최대 반영).</summary>
        public int Stacks;
        /// <summary>연구로 더해진 이웃당 값 (좋은 효과 쪽 부호, Phase 6).</summary>
        public float Boost;
        public float PerNeighbor => Rule.ValuePerNeighbor + Boost;
        public float Total => PerNeighbor * Stacks;
    }

    /// <summary>CONNECT A5 안쪽 얼굴 하나: 이 방(대상)과 이웃 방 사이의 관계 (사고가 생길 때 변형 · 원인 한 줄 · 번짐).</summary>
    public struct InnerHit
    {
        public AdjacencyRule Rule;
        public ModuleInstance Neighbor;
        /// <summary>그 관계의 안쪽 얼굴 (없을 수 있음 — 벌칙 관계는 <see cref="Bad"/>만).</summary>
        public InnerFace Face;
        /// <summary>벌칙 관계 (바깥 효과가 나쁨) — 놓친 사고가 이 이웃 방으로 번짐 (사용자 결정 2026-11-28).</summary>
        public bool Bad;
    }

    /// <summary>배치 미리보기: 새 모듈 때문에 이웃 모듈 하나에 새로 생길 효과 (7-3 화면 표시용).</summary>
    public struct NeighborAdjacencyPreview
    {
        public ModuleInstance Module;
        public AdjacencyRule Rule;
        /// <summary>새로 더해지는 중첩 수.</summary>
        public int AddedStacks;
        public float Boost;
        public float Total => (Rule.ValuePerNeighbor + Boost) * AddedStacks;
    }

    /// <summary>
    /// 공간 인접 효과 (GDD 13번, BALANCE 16번). 면이 맞닿은 이웃 모듈(물리적 인접, 활성 여부 무관)을 세어 규칙을 적용한다.
    /// 그리드가 바뀔 때만 <see cref="Recalculate"/>로 다시 계산하고 결과를 캐시한다.
    /// </summary>
    public sealed class AdjacencySystem
    {
        private sealed class Cached
        {
            /// <summary>효과 종류별 합 (CONNECT A14-2 — 생산 · 소비 · 수용 + 14).</summary>
            public readonly float[] Sum = new float[AdjacencyEffects.Count];
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

        /// <summary>
        /// 대상 모듈의 생산을 늘려 주는 이웃 모듈 (생산 + 규칙의 첫 이웃, 예: 채굴 도킹 → 제련소). 없으면 null.
        /// 이웃을 태그로만 정한 규칙은 candidates 가운데 그 태그를 가진 첫 모듈 (candidates가 없으면 건너뜀).
        /// </summary>
        public ModuleData FindProductionBooster(ModuleData target, IReadOnlyList<ModuleData> candidates = null)
        {
            if (!HasRules || target == null)
                return null;
            foreach (var rule in _rules.Rules)
            {
                if (!rule.AppliesTo(target) || rule.AnyNeighbor || rule.Effect != AdjacencyEffect.Production || rule.ValuePerNeighbor <= 0f)
                    continue;
                if (rule.Neighbor != null)
                {
                    if (rule.Neighbor != target && rule.Matches(target, rule.Neighbor))
                        return rule.Neighbor;
                    continue;
                }
                if (candidates == null)
                    continue;
                foreach (var c in candidates)
                {
                    if (c != null && c != target && rule.Matches(target, c))
                        return c;
                }
            }
            return null;
        }

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
                AddRelayed(grid, module, _neighbors);
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

        private readonly List<ModuleInstance> _relayScratch = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _relays = new List<ModuleInstance>();

        /// <summary>
        /// 12-5 연결 허브: 이웃 중 허브가 있으면 그 허브에 맞닿은 다른 모듈도 이웃으로 (한 단계 — 허브끼리 이어도 더 퍼지지 않음, 허브 자신은 남김).
        /// </summary>
        private void AddRelayed(StationGrid grid, ModuleInstance self, List<ModuleInstance> neighbors)
        {
            _relays.Clear();
            foreach (var n in neighbors)
                if (n.Data != null && n.Data.AdjacencyRelay)
                    _relays.Add(n);
            foreach (var hub in _relays)
            {
                grid.GetNeighborModules(hub, _relayScratch);
                foreach (var m in _relayScratch)
                {
                    if (m != self && m.Data != null && !m.Data.AdjacencyRelay && !neighbors.Contains(m))
                        neighbors.Add(m);
                }
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
                if (rule.Effect == AdjacencyEffect.InnerOnly || !rule.AppliesTo(target))
                    continue; // 안쪽만 관계는 바깥 효과 없음 (InnerFor)
                int count = 0;
                foreach (var n in neighbors)
                {
                    if (n != null && rule.Matches(target, n))
                        count++;
                }
                int stacks = Math.Min(rule.MaxStacks, Math.Max(0, count - rule.FreeNeighbors));
                if (stacks > 0)
                    results.Add(new AppliedAdjacency { Rule = rule, Target = target, Stacks = stacks, Boost = BoostFor(rule) });
            }
        }

        public float GetProductionMultiplier(ModuleInstance module) => Multiplier(module, AdjacencyEffect.Production);

        public float GetConsumptionMultiplier(ModuleInstance module) => Multiplier(module, AdjacencyEffect.Consumption);

        /// <summary>수용 인구 가감 (정수로 반올림, 모듈 기본 수용 인구 아래로는 내려가지 않게 호출자가 처리).</summary>
        public int GetHousingBonus(ModuleInstance module) => Mathf.RoundToInt(Addend(module, AdjacencyEffect.Housing));

        /// <summary>CONNECT A14-2: 배율형 효과 (1 + 합, 규칙 묶음의 범위로 자름). 효과가 없으면 1.</summary>
        public float Multiplier(ModuleInstance module, AdjacencyEffect effect)
            => module != null && _cache.TryGetValue(module, out var c) ? ClampMultiplier(1f + c.Sum[(int)effect]) : 1f;

        /// <summary>CONNECT A14-2: 더하는 효과의 합 (수용 · 승객 · 연구 속도). 효과가 없으면 0.</summary>
        public float Addend(ModuleInstance module, AdjacencyEffect effect)
            => module != null && _cache.TryGetValue(module, out var c) ? c.Sum[(int)effect] : 0f;

        /// <summary>그 효과의 값 — 더하는 효과는 합, 나머지는 배율 (시스템 조회용).</summary>
        public float Value(ModuleInstance module, AdjacencyEffect effect)
            => AdjacencyEffects.IsAdditive(effect) ? Addend(module, effect) : Multiplier(module, effect);

        public IReadOnlyList<AppliedAdjacency> GetApplied(ModuleInstance module)
            => _cache.TryGetValue(module, out var c) ? c.Applied : (IReadOnlyList<AppliedAdjacency>)Array.Empty<AppliedAdjacency>();

        /// <summary>
        /// 배치 미리보기: 새 모듈 자신에게 적용될 효과(self)와, 새 모듈 때문에 이웃에게 새로 생길 효과 설명(neighborLines).
        /// </summary>
        public void Preview(StationGrid grid, ModuleData data, Vector3Int origin, int rotation,
            List<AppliedAdjacency> self, List<string> neighborLines)
        {
            Preview(grid, data, origin, rotation, self, _previewScratch);
            neighborLines.Clear();
            foreach (var p in _previewScratch)
                neighborLines.Add($"{p.Module.Data.DisplayName} {Describe(p.Rule, p.Module.Data, p.AddedStacks, p.Boost)}");
        }

        private readonly List<NeighborAdjacencyPreview> _previewScratch = new List<NeighborAdjacencyPreview>();

        /// <summary>배치 미리보기 (구조체 버전): 이웃 효과를 모듈·규칙 단위로 돌려준다 (7-3 모듈 위 아이콘 표시).</summary>
        public void Preview(StationGrid grid, ModuleData data, Vector3Int origin, int rotation,
            List<AppliedAdjacency> self, List<NeighborAdjacencyPreview> neighborEffects)
        {
            self.Clear();
            neighborEffects.Clear();
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
            AddRelayed(grid, null, _neighbors); // 12-5 허브 옆에 놓으면 허브 건너편 모듈도 이웃
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
                    if (rule.Effect == AdjacencyEffect.InnerOnly || !rule.AppliesTo(neighbor.Data) || !rule.Matches(neighbor.Data, data))
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
                        if (n.Data != null && rule.Matches(neighbor.Data, n.Data))
                            matching++;
                    }
                    int after = Math.Min(rule.MaxStacks, Math.Max(0, matching + 1 - rule.FreeNeighbors));
                    if (after > current)
                        neighborEffects.Add(new NeighborAdjacencyPreview
                        {
                            Module = neighbor, Rule = rule, AddedStacks = after - current, Boost = BoostFor(rule),
                        });
                }
            }
        }

        private readonly List<ModuleInstance> _scratchNeighbors = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _innerNeighbors = new List<ModuleInstance>();

        /// <summary>
        /// CONNECT A5 안쪽 얼굴 (3번 ④ V2): module 방의 이웃(허브 건너편 포함)과 맺은 관계 가운데 안쪽 얼굴이 있거나 벌칙인 것 — 이웃 방마다 하나씩.
        /// 그늘처럼 이웃을 가리지 않는 관계는 뺌 (사고와 무관).
        /// </summary>
        public void InnerFor(StationGrid grid, ModuleInstance module, List<InnerHit> hits)
        {
            hits.Clear();
            if (!HasRules || grid == null || module?.Data == null)
                return;
            grid.GetNeighborModules(module, _innerNeighbors);
            AddRelayed(grid, module, _innerNeighbors);
            foreach (var rule in _rules.Rules)
            {
                if (rule.AnyNeighbor || !rule.AppliesTo(module.Data))
                    continue;
                bool bad = rule.Effect != AdjacencyEffect.InnerOnly && !IsBeneficial(rule, rule.ValuePerNeighbor);
                if (rule.Inner == InnerFace.None && !bad)
                    continue;
                foreach (var n in _innerNeighbors)
                {
                    if (n.Data != null && rule.Matches(module.Data, n.Data))
                        hits.Add(new InnerHit { Rule = rule, Neighbor = n, Face = rule.Inner, Bad = bad });
                }
            }
        }

        /// <summary>이 모듈이 받을 수 있는 그 안쪽 얼굴의 관계 (없으면 null) — "옆에 ○○가 있었다면" 한 줄.</summary>
        public AdjacencyRule FindInnerRule(ModuleData target, InnerFace face)
        {
            if (!HasRules || target == null || face == InnerFace.None)
                return null;
            foreach (var rule in _rules.Rules)
            {
                if ((rule.Inner & face) != 0 && !rule.AnyNeighbor && rule.AppliesTo(target))
                    return rule;
            }
            return null;
        }

        /// <summary>그 관계의 이웃을 사람이 읽는 이름으로 ("정비(정비 베이)", 모듈이면 모듈 이름).</summary>
        public static string NeighborLabel(AdjacencyRule rule)
            => rule == null ? "-" : rule.Neighbor != null ? rule.NeighborName : Tagged(rule.NeighborTag);

        /// <summary>적용 중인 효과 설명 (연구 보정 포함).</summary>
        public static string Describe(AppliedAdjacency applied) => Describe(applied.Rule, applied.Target ?? applied.Rule.Target, applied.Stacks, applied.Boost);

        /// <summary>"물 소비 -25%" 같은 짧은 설명 (대상 = 규칙의 대상 모듈). boost = 연구로 더해진 이웃당 값.</summary>
        public static string Describe(AdjacencyRule rule, int stacks, float boost = 0f) => Describe(rule, rule.Target, stacks, boost);

        /// <summary>"물 소비 -25%" 같은 짧은 설명. target = 효과를 받는 모듈 종류 (태그로 정한 규칙 — 자원 이름을 여기서 고름).</summary>
        public static string Describe(AdjacencyRule rule, ModuleData target, int stacks, float boost = 0f)
        {
            float total = (rule.ValuePerNeighbor + boost) * stacks;
            target = target ?? rule.Target;
            string what;
            switch (rule.Effect)
            {
                case AdjacencyEffect.Production: what = ProductionName(target); break;
                case AdjacencyEffect.Consumption: what = ConsumptionName(target); break;
                default: what = AdjacencyEffects.Name(rule.Effect); break;
            }
            string amount = ShortValue(rule, total);
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
                if (rule.AppliesTo(data) && !rule.AnyTarget) // 모든 모듈 규칙은 이웃 쪽 줄로만 (모든 모듈 설명에 같은 줄이 붙지 않게)
                {
                    string who = !rule.AnyNeighbor ? (rule.Neighbor == null ? Tagged(rule.NeighborTag) : rule.NeighborName)
                        : rule.ExcludeSameType ? $"{data.DisplayName} 외 모든 모듈" : "모든 모듈";
                    string free = rule.FreeNeighbors > 0 ? $", {rule.FreeNeighbors}개 면제" : "";
                    string inner = rule.Inner != InnerFace.None ? $" · 안: {AdjacencyEffects.InnerText(rule.Inner)}" : "";
                    lines.Add(rule.Effect == AdjacencyEffect.InnerOnly
                        ? $"{who} 옆: 안: {AdjacencyEffects.InnerText(rule.Inner)}"
                        : $"{who} 옆: {Describe(rule, data, 1, BoostFor(rule))} / 개 (최대 {rule.MaxStacks}회{free}){inner}");
                }
                else if (!rule.AnyNeighbor && rule.CouldNeighbor(data) && (rule.Target != null || rule.TargetTag != null || rule.AnyTarget))
                {
                    string what = rule.Effect == AdjacencyEffect.InnerOnly ? "" : Describe(rule, rule.Target, 1, BoostFor(rule));
                    string inner = rule.Inner != InnerFace.None ? $"{(what.Length > 0 ? " · " : "")}안: {AdjacencyEffects.InnerText(rule.Inner)}" : "";
                    lines.Add(rule.AnyTarget
                        ? $"옆 모듈마다 {what}{inner}"
                        : $"{(rule.Target == null ? Tagged(rule.TargetTag) : rule.TargetName)} 옆에 두면 그 모듈 {what}{inner}");
                }
            }
        }

        /// <summary>태그 이름 + 해당 모듈 ("소음원(채굴 도킹 · 제련소 · 방어 포탑)") — 태그만으로는 어떤 모듈인지 모름 (CONNECT A14-8 읽히게).</summary>
        public static string Tagged(AdjacencyTag tag)
        {
            if (tag == null)
                return "-";
            string members = tag.Members;
            return string.IsNullOrEmpty(members) ? tag.DisplayName : $"{tag.DisplayName}({members})";
        }

        /// <summary>좋은 효과인지 (생산 +, 소비 −, 수용 인구 + … 작을수록 좋은 효과는 −). 색 구분용.</summary>
        public static bool IsBeneficial(AdjacencyRule rule, float total)
            => AdjacencyEffects.LowerIsBetter(rule.Effect) ? total < 0f : total >= 0f;

        /// <summary>"+15%" / "-2" 같은 수치만 (7-3 아이콘 옆).</summary>
        public static string ShortValue(AdjacencyRule rule, float total)
            => AdjacencyEffects.IsPercent(rule.Effect) ? (total * 100f).ToString("+0;-0") + "%" : total.ToString("+0;-0");

        /// <summary>효과가 걸리는 자원 (아이콘 선택용). 수용 인구면 null.</summary>
        public static ResourceType? EffectResource(AdjacencyRule rule, ModuleData target = null)
        {
            target = target ?? rule.Target;
            if (target == null || (rule.Effect != AdjacencyEffect.Production && rule.Effect != AdjacencyEffect.Consumption))
                return null; // 수용 · CONNECT A14-2 새 효과는 자원 아이콘이 아니라 효과 모양 (AdjacencyEffects.IconName)
            var list = rule.Effect == AdjacencyEffect.Production ? target.Production : target.Consumption;
            foreach (var a in list)
            {
                if (a.Amount > 0f && (rule.Effect == AdjacencyEffect.Production || a.Type != ResourceType.Power))
                    return a.Type;
            }
            return null;
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
            int i = (int)a.Rule.Effect;
            if (i >= 0 && i < cached.Sum.Length)
                cached.Sum[i] += a.Total;
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
