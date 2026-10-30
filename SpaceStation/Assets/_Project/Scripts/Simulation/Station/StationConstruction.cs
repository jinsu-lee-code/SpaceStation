using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>12-3b: 모듈을 옮길 수 있는지 (자리와 무관한 조건).</summary>
    public enum MoveBlock
    {
        None,
        /// <summary>코어 · 철거 불가 모듈</summary>
        NotMovable,
        /// <summary>합성 중인 재료</summary>
        Synthesizing,
        /// <summary>파손 · 수리 대기 · 수리 중</summary>
        Damaged,
        /// <summary>다른 모듈의 받침 (철거와 같은 규칙)</summary>
        Supporting,
    }

    /// <summary>
    /// 12-0b (StationSimulation에서 분리): 건설 명령 — 배치 판정(공간 규칙 → 등급 해금/설치 제한 → 비용) · 배치 · 철거 · 환급 ·
    /// 정비 · 재건축 · 파괴. 바깥에서는 StationSimulation의 같은 이름 함수로 부른다.
    /// </summary>
    internal sealed class StationConstruction
    {
        private readonly StationSimulation _sim;

        public StationConstruction(StationSimulation sim) => _sim = sim;

        public PlacementResult EvaluatePlacement(ModuleData data, UnityEngine.Vector3Int origin, int rotation)
        {
            var result = PlacementRules.Evaluate(_sim.Grid, data, origin, rotation);
            if (result == PlacementResult.Valid && !InZone(data, origin, rotation))
                result = PlacementResult.OutsideZone;
            if (result == PlacementResult.Valid)
                result = CheckBuildable(data);
            if (result == PlacementResult.Valid && !_sim.Resources.CanAfford(GetBuildCost(data)))
                return PlacementResult.InsufficientResources;
            return result;
        }

        public List<ResourceAmount> GetBuildCost(ModuleData data) => _sim.Effects.BuildCost(data);

        /// <summary>M-50: 칸을 정하는 쪽이 있으면 모든 칸이 허용돼야 함.</summary>
        private bool InZone(ModuleData data, UnityEngine.Vector3Int origin, int rotation)
        {
            var zone = _sim.BuildZone;
            if (zone == null)
                return true;
            foreach (var c in StationGrid.ResolveCells(data.CellOffsets, origin, rotation))
                if (!zone.Allows(c))
                    return false;
            return true;
        }

        public PlacementResult CheckBuildable(ModuleData data)
        {
            var result = _sim.Progression.CheckBuildable(data, _sim.Grid);
            // 11-18: 튜토리얼 중에는 지금 단계의 모듈만 (등급 잠김이 먼저)
            var tutorial = _sim.Tutorial;
            if (result == PlacementResult.Valid && tutorial != null && !tutorial.Allows(TutorialAction.Build, data))
                return PlacementResult.TutorialLocked;
            return result;
        }

        public bool CanAfford(ModuleData data) => data != null && _sim.Resources.CanAfford(GetBuildCost(data));

        public bool TryPlace(ModuleData data, UnityEngine.Vector3Int origin, int rotation, out ModuleInstance module)
        {
            module = null;
            if (EvaluatePlacement(data, origin, rotation) != PlacementResult.Valid)
                return false;
            if (!_sim.Resources.TrySpend(GetBuildCost(data)))
                return false;
            return _sim.Grid.TryPlace(data, origin, rotation, out module);
        }

        /// <summary>12-3: 합성 중 재료는 철거 불가 (분해 불가 원칙은 진화 모듈 자체 — 진화 모듈은 철거만 가능).</summary>
        public bool CanRemove(ModuleInstance module) => IsRemovableKind(module) && !_sim.IsHalted(module) && !PlacementRules.SupportsOthers(_sim.Grid, module);

        public bool IsRemovableKind(ModuleInstance module) => module != null && module != _sim.Core && (module.Data == null || module.Data.Removable);

        public bool IsSupportingOthers(ModuleInstance module) => PlacementRules.SupportsOthers(_sim.Grid, module);

        public bool TryRemove(ModuleInstance module)
        {
            if (!CanRemove(module))
                return false;
            float refundMultiplier = _sim.Durability.GetRefundMultiplier(module); // 제거 전에 읽음
            if (!_sim.Grid.Remove(module))
                return false;
            if (module.Data != null)
                _sim.Resources.RefundBuildCost(module.Data.BuildCost, refundMultiplier);
            return true;
        }

        public List<ResourceAmount> GetRefund(ModuleInstance module)
        {
            var refund = new List<ResourceAmount>();
            if (module?.Data == null)
                return refund;
            float rate = _sim.Effects.DemolishRefundRate * _sim.Durability.GetRefundMultiplier(module);
            foreach (var a in module.Data.BuildCost)
                refund.Add(new ResourceAmount(a.Type, a.Amount * rate));
            return refund;
        }

        // ---------------- 이동 (12-3b) ----------------

        /// <summary>자리와 무관하게 옮길 수 있는지 (사용자 결정 2026-10-13: 진화 모듈 · 도킹 포함, 파손 · 합성 중 · 받침은 불가).</summary>
        public MoveBlock CheckMovable(ModuleInstance module)
        {
            if (!IsRemovableKind(module) || module.Data == null)
                return MoveBlock.NotMovable;
            if (_sim.IsHalted(module))
                return MoveBlock.Synthesizing;
            if (_sim.Damage.TryGetInfo(module, out _))
                return MoveBlock.Damaged;
            if (PlacementRules.SupportsOthers(_sim.Grid, module))
                return MoveBlock.Supporting;
            return MoveBlock.None;
        }

        /// <summary>이동 비용 = 건설비(건설 연구 할인 반영) × 이동 비율 (BALANCE 33번, 기본 20%).</summary>
        public List<ResourceAmount> GetMoveCost(ModuleInstance module)
        {
            var cost = new List<ResourceAmount>();
            if (module?.Data == null)
                return cost;
            foreach (var a in GetBuildCost(module.Data))
                cost.Add(new ResourceAmount(a.Type, a.Amount * _sim.Balance.MoveCostRate));
            return cost;
        }

        /// <summary>
        /// 옮길 자리 판정: 공간 규칙(자기 칸 · 접근로 · 받침은 없다고 봄) → 다른 모듈과 면 접촉 → 비용.
        /// 옮길 수 없는 모듈이면 InvalidDefinition (먼저 <see cref="CheckMovable"/>로 사유를 본다).
        /// </summary>
        public PlacementResult EvaluateMove(ModuleInstance module, UnityEngine.Vector3Int origin, int rotation)
        {
            if (CheckMovable(module) != MoveBlock.None)
                return PlacementResult.InvalidDefinition;
            rotation = GridDirections.NormalizeRotation(rotation);
            if (origin == module.Origin && rotation == module.Rotation)
                return PlacementResult.NoChange;
            var data = module.Data;
            var result = PlacementRules.Evaluate(_sim.Grid, data, origin, rotation, module);
            if (result != PlacementResult.Valid)
                return result;
            if (!InZone(data, origin, rotation))
                return PlacementResult.OutsideZone;
            if (!PlacementRules.TouchesOther(_sim.Grid, StationGrid.ResolveCells(data.CellOffsets, origin, rotation), module))
                return PlacementResult.NeedsContact;
            if (!_sim.Resources.CanAfford(GetMoveCost(module)))
                return PlacementResult.InsufficientResources;
            return PlacementResult.Valid;
        }

        /// <summary>비용을 내고 즉시 옮김 (같은 모듈 — 내구도 · 노후 · 주민 · 상자 그대로).</summary>
        public bool TryMove(ModuleInstance module, UnityEngine.Vector3Int origin, int rotation)
        {
            if (EvaluateMove(module, origin, rotation) != PlacementResult.Valid)
                return false;
            if (!_sim.Resources.TrySpend(GetMoveCost(module)))
                return false;
            return _sim.Grid.TryMove(module, origin, rotation);
        }

        // ---------------- 정비 / 재건축 (4-3) ----------------

        public MaintainResult TryMaintain(ModuleInstance module)
        {
            if (!_sim.Durability.TryGetInfo(module, out var info))
                return MaintainResult.NotApplicable;
            if (info.Current >= info.Max - 1e-4f)
                return MaintainResult.AlreadyAtMax;
            if (!_sim.Resources.TrySpend(_sim.Durability.GetMaintenanceCost(module)))
                return MaintainResult.InsufficientResources;
            _sim.Durability.Maintain(module);
            _sim.EvaluateProgression(); // UI 갱신 트리거
            return MaintainResult.Done;
        }

        public List<ResourceAmount> GetRebuildCost(ModuleInstance module)
        {
            var net = new List<ResourceAmount>();
            if (module?.Data == null)
                return net;
            float rate = _sim.Effects.DemolishRefundRate * _sim.Durability.GetRefundMultiplier(module);
            float discount = _sim.Effects.RebuildCostMultiplier;
            foreach (var a in module.Data.BuildCost)
                net.Add(new ResourceAmount(a.Type, Math.Max(0f, a.Amount * (1f - rate)) * discount));
            return net;
        }

        /// <summary>비용을 빼고 재건축할 수 있는지 (가능하면 Done).</summary>
        private RebuildResult CheckRebuild(ModuleInstance module)
        {
            if (!IsRemovableKind(module) || module.Data == null || !_sim.Durability.TryGetInfo(module, out _) || _sim.IsHalted(module))
                return RebuildResult.NotAllowed; // 12-3: 합성 중 재료는 재건축 불가
            var data = module.Data;
            var progression = _sim.Progression;
            // 12-3: 진화 모듈은 건설 메뉴(등급 해금 목록)에 없음 → 그 레시피가 열려 있으면 재건축 가능
            bool unlocked = _sim.Synthesis != null && _sim.Synthesis.IsRecipeResult(data)
                ? _sim.Synthesis.IsUnlocked(_sim.Synthesis.RecipeFor(data))
                : progression.IsUnlocked(data);
            if (!unlocked)
                return RebuildResult.Locked;
            // 철거 후 다시 지으므로 한도를 넘은 상태(등급 하락)면 막음 — 진화 모듈은 도킹 몫만큼
            int share = data == progression.LimitedModule ? 1 : data.LimitedModuleShare;
            if (share > 0 && progression.CountLimited(_sim.Grid) > progression.CurrentLimit(_sim.Grid))
                return RebuildResult.Locked;
            return RebuildResult.Done;
        }

        public bool CanRebuildKind(ModuleInstance module) => CheckRebuild(module) == RebuildResult.Done;

        public RebuildResult TryRebuild(ModuleInstance module, out ModuleInstance rebuilt)
        {
            rebuilt = null;
            var check = CheckRebuild(module);
            if (check != RebuildResult.Done)
                return check;
            var data = module.Data;
            if (!_sim.Resources.TrySpend(GetRebuildCost(module)))
                return RebuildResult.InsufficientResources;

            var origin = module.Origin;
            int rotation = module.Rotation;
            _sim.Grid.Remove(module);
            _sim.Grid.TryPlace(data, origin, rotation, out rebuilt); // 같은 셀이므로 항상 성공
            return RebuildResult.Done;
        }

        public bool DestroyModule(ModuleInstance module)
        {
            if (module == null || module == _sim.Core)
                return false;
            return _sim.Grid.Remove(module);
        }
    }
}
