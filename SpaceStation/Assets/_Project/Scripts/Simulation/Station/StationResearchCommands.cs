using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>12-0b (StationSimulation에서 분리): 연구 명령 (Phase 6) — 시작 가능 판정 · 시작 비용(연구 포인트 할인, 11-17 ②) · 시작 · 취소.</summary>
    internal sealed class StationResearchCommands
    {
        private readonly StationSimulation _sim;
        private readonly StationModuleStats _stats;

        public StationResearchCommands(StationSimulation sim, StationModuleStats stats)
        {
            _sim = sim;
            _stats = stats;
        }

        public ResearchStartResult CanStart(ResearchCategoryData category, bool usePoint)
            => _sim.Research.CanStart(category, _sim.Progression.GradeIndex, _sim.Resources.Population,
                cost => _sim.Resources.CanAfford(Discount(cost, usePoint)));

        public List<ResourceAmount> GetStartCost(ResearchCategoryData category, bool usePoint)
        {
            var level = category != null ? category.GetLevel(_sim.Research.GetLevel(category) + 1) : null;
            return level != null ? Discount(level.StartCost, usePoint) : new List<ResourceAmount>();
        }

        private List<ResourceAmount> Discount(IReadOnlyList<ResourceAmount> cost, bool usePoint)
        {
            float k = usePoint && _sim.Supply.ResearchPoints > 0 ? 1f - _sim.Balance.ResearchPointDiscount : 1f;
            var result = new List<ResourceAmount>(cost.Count);
            foreach (var a in cost)
                result.Add(new ResourceAmount(a.Type, a.Amount * k));
            return result;
        }

        public ResearchStartResult TryStart(ResearchCategoryData category, bool usePoint)
        {
            _stats.CollectActiveModules(); // 방금 지은 연구소도 슬롯으로 인정
            var supply = _sim.Supply;
            usePoint &= supply.ResearchPoints > 0;
            var result = CanStart(category, usePoint);
            if (result != ResearchStartResult.Ok)
                return result;
            if (!_sim.Resources.TrySpend(GetStartCost(category, usePoint)))
                return ResearchStartResult.InsufficientResources;
            if (usePoint)
                supply.SetCounts(supply.ResearchPoints - 1, supply.FreeRepairs);
            _sim.Research.Begin(category);
            _sim.EvaluateProgression(); // UI 갱신 트리거
            return ResearchStartResult.Ok;
        }

        public bool Cancel(ResearchCategoryData category)
        {
            bool done = _sim.Research.Cancel(category);
            if (done)
                _sim.EvaluateProgression();
            return done;
        }
    }
}
