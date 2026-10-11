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

        public bool CompleteResidentRequest(Resident resident, out string text, float? roll01)
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
            Population.SetSatisfaction(Math.Min(PopulationSimulation.MaxSatisfaction, before + Balance.RequestSatisfaction));
            _cheerUntil[resident.Id] = _sim.ElapsedSeconds + Balance.RequestCheerSeconds;
            text = $"만족도 +{Population.Satisfaction - before:0.#}";
            float roll = roll01 ?? (float)_crateRandom.NextDouble();
            if (roll < Balance.RequestResearchPointChance)
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

        public string MissIncident(IncidentKind kind, ModuleInstance module, ResourceType leakResource)
        {
            var b = Balance;
            float seconds = b.IncidentPenaltySeconds;
            switch (kind)
            {
                case IncidentKind.Leak:
                    _sim.Incidents.AddDrain(leakResource, b.IncidentLeakLoss, seconds);
                    return $"{leakResource.DisplayName()} -{b.IncidentLeakLoss:0} (2분 동안)";
                case IncidentKind.Breaker:
                    _sim.Incidents.Trip(module, b.IncidentBreakerMultiplier, seconds);
                    return $"생산 {Math.Round(b.IncidentBreakerMultiplier * 100f)}% (2분 동안)";
                case IncidentKind.Fire:
                    if (module != null && _sim.Durability.TryGetInfo(module, out var info))
                        _sim.Durability.ApplyImpact(module, Math.Min(b.IncidentFireDurability, Math.Max(0f, info.Current - 5f)));
                    LoseSatisfaction(b.IncidentMissSatisfaction);
                    return $"내구도 -{b.IncidentFireDurability:0} · 만족도 -{b.IncidentMissSatisfaction:0.#}";
                default:
                    _sim.Incidents.AddDrain(ResourceType.Oxygen, b.IncidentFilterOxygenLoss, seconds);
                    LoseSatisfaction(b.IncidentMissSatisfaction);
                    return $"산소 -{b.IncidentFilterOxygenLoss:0} (2분 동안) · 만족도 -{b.IncidentMissSatisfaction:0.#}";
            }
        }

        private void LoseSatisfaction(float amount) => Population.SetSatisfaction(Math.Max(0f, Population.Satisfaction - amount));

        public float CheerOf(Resident resident) =>
            resident != null && _cheerUntil.TryGetValue(resident.Id, out var until) && _sim.ElapsedSeconds < until ? Balance.RequestCheer : 0f;
    }
}
