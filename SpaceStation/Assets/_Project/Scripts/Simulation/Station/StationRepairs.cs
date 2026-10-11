using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-0b (StationSimulation에서 분리): 수리 명령 — 수리 시작 · 대기 · 우선 · 취소 환불, 내부 현장 수리, 무료 수리권(11-17 ②).
    /// </summary>
    internal sealed class StationRepairs
    {
        private readonly StationSimulation _sim;
        private readonly StationModuleStats _stats;

        public StationRepairs(StationSimulation sim, StationModuleStats stats)
        {
            _sim = sim;
            _stats = stats;
        }

        public RepairResult TryRepair(ModuleInstance module)
        {
            var damage = _sim.Damage;
            if (!damage.TryGetInfo(module, out var info))
                return RepairResult.NotDamaged;
            if (info.IsRepairing)
                return RepairResult.AlreadyRepairing;
            if (info.IsQueued)
                return RepairResult.AlreadyQueued;
            if (!UseFreeRepair() && !_sim.Resources.TrySpend(damage.GetRepairCost(module))) // 11-17 ② 무료 수리권 먼저
                return RepairResult.InsufficientResources;
            _stats.RefreshRepairCapacity();
            damage.StartRepair(module);
            return info.IsRepairing ? RepairResult.Started : RepairResult.Queued;
        }

        public List<ResourceAmount> GetFieldRepairCost(ModuleInstance module)
        {
            if (!_sim.Damage.TryGetInfo(module, out var info) || info.IsQueued || info.IsRepairing || _sim.Supply.FreeRepairs > 0)
                return new List<ResourceAmount>();
            var cost = _sim.Damage.GetRepairCost(module);
            for (int i = 0; i < cost.Count; i++)
                cost[i] = new ResourceAmount(cost[i].Type, cost[i].Amount * _sim.Balance.FieldRepairCostRate);
            return cost;
        }

        public bool FieldRepairUsesFreeRepair(ModuleInstance module)
            => _sim.Supply.FreeRepairs > 0 && _sim.Damage.TryGetInfo(module, out var info) && !info.IsQueued && !info.IsRepairing;

        public RepairResult TryFieldRepair(ModuleInstance module)
        {
            if (!_sim.Damage.TryGetInfo(module, out var info))
                return RepairResult.NotDamaged;
            bool paid = info.IsQueued || info.IsRepairing;
            if (FieldRepairUsesFreeRepair(module))
                UseFreeRepair();
            else if (!paid && !_sim.Resources.TrySpend(GetFieldRepairCost(module)))
                return RepairResult.InsufficientResources;
            _sim.Damage.CompleteNow(module);
            _stats.RefreshRepairCapacity();
            return RepairResult.Completed;
        }

        public bool TryPrioritizeRepair(ModuleInstance module) => _sim.Damage.Prioritize(module);

        public bool TryCancelRepair(ModuleInstance module)
        {
            if (!_sim.Damage.CancelQueued(module))
                return false;
            foreach (var a in GetCancelRefund(module))
                _sim.Resources.AddStock(a.Type, a.Amount); // 철거 환급률과 별개
            return true;
        }

        public List<ResourceAmount> GetCancelRefund(ModuleInstance module)
        {
            var refund = _sim.Damage.GetRepairCost(module);
            for (int i = 0; i < refund.Count; i++)
                refund[i] = new ResourceAmount(refund[i].Type, refund[i].Amount * _sim.Balance.RepairCancelRefundRate);
            return refund;
        }

        /// <summary>11-17 ② 무료 수리권 1장을 씀 (있으면 true).</summary>
        private bool UseFreeRepair()
        {
            var supply = _sim.Supply;
            if (supply.FreeRepairs <= 0)
                return false;
            supply.SetCounts(supply.ResearchPoints, supply.FreeRepairs - 1);
            _sim.Report($"무료 수리권 사용 · 남은 {supply.FreeRepairs}", true);
            return true;
        }
    }
}
