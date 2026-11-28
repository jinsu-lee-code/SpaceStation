using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-0b (StationSimulation에서 분리): 1인칭 내부 활동의 결과 (11-17) — 보급 상자 줍기, 주민 부탁 보상 · 기분 보너스, 사고 처리 · 놓침 손실.
    /// </summary>
    internal sealed class StationInteriorOutcomes
    {
        private readonly StationSimulation _sim;
        private readonly Random _crateRandom;
        // 11-17 ③ 주민 요청을 들어준 주민 → 기분 보너스가 끝나는 게임 시각 (저장하지 않음 — 겉모습 · 반응만)
        private readonly Dictionary<int, float> _cheerUntil = new Dictionary<int, float>();

        public StationInteriorOutcomes(StationSimulation sim, Random crateRandom)
        {
            _sim = sim;
            _crateRandom = crateRandom;
        }

        private PopulationSimulation Population => _sim.Population;
        private BalanceConfig Balance => _sim.Balance;

        public bool TryPickupCrate(int id, out string text)
        {
            text = null;
            var supply = _sim.Supply;
            var crate = supply.Find(id);
            if (crate == null)
                return false;
            switch (crate.Bonus)
            {
                case CrateBonus.ResearchPoints:
                    supply.SetCounts(supply.ResearchPoints + Balance.CrateResearchPoints, supply.FreeRepairs);
                    text = $"연구 포인트 +{Balance.CrateResearchPoints}  (보유 {supply.ResearchPoints} · 연구 시작 비용 할인)";
                    break;
                case CrateBonus.FreeRepair:
                    supply.SetCounts(supply.ResearchPoints, supply.FreeRepairs + 1);
                    text = $"무료 수리권 +1  (보유 {supply.FreeRepairs} · 다음 수리에 자동 사용)";
                    break;
                case CrateBonus.Satisfaction:
                    float before = Population.Satisfaction;
                    Population.SetSatisfaction(Math.Min(PopulationSimulation.MaxSatisfaction, before + Balance.CrateSatisfaction));
                    text = $"만족도 +{Population.Satisfaction - before:0.#}  (보급품 속 간식과 생필품)";
                    break;
                default:
                    float added = _sim.Resources.AddStock(crate.Resource, crate.Amount);
                    text = $"{crate.Resource.DisplayName()} +{added:0.#}" + (added < crate.Amount - 1e-3f ? "  (저장 한도 초과분 제외)" : "");
                    break;
            }
            supply.Remove(crate);
            return true;
        }

        public bool CompleteResidentRequest(Resident resident, out string text, float? roll01, float scale = 1f)
        {
            text = null;
            var roster = _sim.Residents;
            if (resident == null || roster == null)
                return false;
            bool listed = false;
            foreach (var r in roster.Residents)
                listed |= r == resident;
            if (!listed)
                return false;
            float before = Population.Satisfaction;
            Population.SetSatisfaction(Math.Min(PopulationSimulation.MaxSatisfaction, before + Balance.RequestSatisfaction * Math.Max(0f, scale))); // CONNECT A8 부탁 보상 ↑ ↓ (이웃)
            _cheerUntil[resident.Id] = _sim.ElapsedSeconds + Balance.RequestCheerSeconds;
            text = $"만족도 +{Population.Satisfaction - before:0.#}";
            float roll = roll01 ?? (float)_crateRandom.NextDouble();
            if (roll < Balance.RequestResearchPointChance * Math.Max(0f, scale))
            {
                var supply = _sim.Supply;
                supply.SetCounts(supply.ResearchPoints + 1, supply.FreeRepairs);
                text += $"  ·  연구 포인트 +1 (보유 {supply.ResearchPoints})";
            }
            return true;
        }

        public string ResolveIncident()
        {
            float before = Population.Satisfaction;
            Population.SetSatisfaction(Math.Min(PopulationSimulation.MaxSatisfaction, before + Balance.IncidentResolveSatisfaction));
            return $"만족도 +{Population.Satisfaction - before:0.#}";
        }

        public string MissIncident(IncidentKind kind, ModuleInstance module, ResourceType leakResource, IncidentMiss mods = IncidentMiss.None)
        {
            var b = Balance;
            float seconds = b.IncidentPenaltySeconds;
            float k = Math.Max(0f, _sim.Incidents.MissScale); // 13-6 고리
            if (k <= 0f || (mods & IncidentMiss.NoLoss) != 0)
                return "손실 없음";
            if ((mods & IncidentMiss.Half) != 0)
                k *= 0.5f; // CONNECT A8 통제 ← 정비
            bool noMood = (mods & IncidentMiss.NoMood) != 0; // CONNECT A8 아무 방 ← 의료
            float satisfaction = noMood ? 0f : b.IncidentMissSatisfaction * k;
            string mood = noMood ? " · 만족도 지킴" : $" · 만족도 -{satisfaction:0.#}";
            // CONNECT A6 단계별 상황: 새 상황은 지금 넷의 손실을 다시 씀 (IncidentCatalog.LossOf)
            var loss = IncidentCatalog.LossOf(kind);
            if (loss == IncidentLoss.Production && kind != IncidentKind.Breaker && !MakesOrUsesPower(module))
                loss = IncidentLoss.Mood; // 만들거나 전력을 쓰지 않는 방 (차단기는 그런 방에 안 생김)
            switch (loss)
            {
                case IncidentLoss.Drain:
                    if ((mods & IncidentMiss.Recovered) != 0 && leakResource == ResourceType.Water)
                        return "물 손실 없음 (회수)"; // CONNECT A8 물 사용 ← 수원
                    _sim.Incidents.AddDrain(leakResource, b.IncidentLeakLoss * k, seconds);
                    return $"{leakResource.DisplayName()} -{b.IncidentLeakLoss * k:0} (2분 동안)";
                case IncidentLoss.Production:
                    float multiplier = Math.Max(0f, 1f - (1f - b.IncidentBreakerMultiplier) * k);
                    _sim.Incidents.Trip(module, multiplier, seconds);
                    return $"생산 {Math.Round(multiplier * 100f)}% (2분 동안)";
                case IncidentLoss.Damage:
                    float durability = b.IncidentFireDurability * k;
                    if (module != null && _sim.Durability.TryGetInfo(module, out var info))
                        _sim.Durability.ApplyImpact(module, Math.Min(durability, Math.Max(0f, info.Current - 5f)));
                    if (!noMood)
                        LoseSatisfaction(satisfaction);
                    return $"내구도 -{durability:0}{mood}";
                case IncidentLoss.Mood:
                    if (noMood)
                        return "손실 없음 (만족도 지킴)";
                    LoseSatisfaction(satisfaction);
                    return $"만족도 -{satisfaction:0.#}";
                default:
                    _sim.Incidents.AddDrain(ResourceType.Oxygen, b.IncidentFilterOxygenLoss * k, seconds);
                    if (!noMood)
                        LoseSatisfaction(satisfaction);
                    return $"산소 -{b.IncidentFilterOxygenLoss * k:0} (2분 동안){mood}";
            }
        }

        /// <summary>무언가를 만들거나 전력을 쓰는 방인지 (생산 배율 손실이 의미 있는 방 — 내부 차단기 규칙과 같은 기준).</summary>
        private static bool MakesOrUsesPower(ModuleInstance module)
        {
            var data = module?.Data;
            if (data == null)
                return false;
            foreach (var p in data.Production)
            {
                if (p.Amount > 0f)
                    return true;
            }
            foreach (var c in data.Consumption)
            {
                if (c.Type == ResourceType.Power && c.Amount > 0f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 13-6 고리: 줍지 않은 보급 상자를 바깥에서 모두 회수 (<see cref="SupplyCrateSystem.AutoCollectRate"/>) — 자원 상자 양 × rate, 특별 상자는 그대로.
        /// 반환 = 받은 것 요약 (알림용), 상자가 없으면 null.
        /// </summary>
        public string AutoCollectCrates(float rate)
        {
            var supply = _sim.Supply;
            if (supply.Crates.Count == 0)
                return null;
            var crates = new List<SupplyCrate>(supply.Crates);
            var added = new Dictionary<ResourceType, float>();
            int points = 0, repairs = 0;
            float satisfaction = 0f;
            foreach (var c in crates)
            {
                switch (c.Bonus)
                {
                    case CrateBonus.ResearchPoints: points += Balance.CrateResearchPoints; break;
                    case CrateBonus.FreeRepair: repairs++; break;
                    case CrateBonus.Satisfaction: satisfaction += Balance.CrateSatisfaction; break;
                    default:
                        float got = _sim.Resources.AddStock(c.Resource, c.Amount * Math.Max(0f, rate));
                        added[c.Resource] = (added.TryGetValue(c.Resource, out var v) ? v : 0f) + got;
                        break;
                }
                supply.Remove(c);
            }
            if (points > 0 || repairs > 0)
                supply.SetCounts(supply.ResearchPoints + points, supply.FreeRepairs + repairs);
            var parts = new List<string>();
            if (points > 0)
                parts.Add($"연구 포인트 +{points}");
            if (repairs > 0)
                parts.Add($"무료 수리권 +{repairs}");
            if (satisfaction > 0f)
            {
                float before = Population.Satisfaction;
                Population.SetSatisfaction(Math.Min(PopulationSimulation.MaxSatisfaction, before + satisfaction));
                parts.Add($"만족도 +{Population.Satisfaction - before:0.#}");
            }
            foreach (var kv in added)
            {
                if (kv.Value > 1e-3f)
                    parts.Add($"{kv.Key.DisplayName()} +{kv.Value:0.#}");
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "받은 것 없음";
        }

        private void LoseSatisfaction(float amount) => Population.SetSatisfaction(Math.Max(0f, Population.Satisfaction - amount));

        public float CheerOf(Resident resident) =>
            resident != null && _cheerUntil.TryGetValue(resident.Id, out var until) && _sim.ElapsedSeconds < until ? Balance.RequestCheer : 0f;
    }
}
