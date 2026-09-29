using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Editor.Balance
{
    /// <summary>
    /// 규칙 기반 자동 플레이어 ("적당히 잘하는 플레이어" 기준선).
    /// 모듈 역할은 데이터(생산/소비/수용/저장/말단 여부)로 판단하므로 모듈이 추가돼도 코드 수정이 필요 없다.
    /// 매 결정: 수리(파괴 임박 순) → 가장 급한 필요 1개 건설. 급한 것을 살 돈이 없으면 저축(다른 것 안 지음).
    /// </summary>
    public sealed class BalanceBot
    {
        private const float NetMargin = 0.2f;      // 순수지가 이보다 낮으면 생산 모듈 추가
        private const float PowerHeadroom = 2f;    // 전력 여유가 이보다 낮으면 발전 추가

        private readonly StationSimulation _sim;
        private readonly ModuleData _power, _oxygen, _water, _food, _housing, _storage, _metal, _battery;
        // 전력 모델 (Decide마다 갱신): 낮 여유 = 낮 발전 - 수요 - 밤 대비 충전분
        private float _nightDeficit;     // 밤 동안 초당 부족량
        private float _spareDayPower;    // 밤 충전분을 뺀 낮 여유 (초당)
        private readonly List<ModuleInstance> _repairQueue = new List<ModuleInstance>();
        private readonly HashSet<Vector3Int> _candidates = new HashSet<Vector3Int>();

        public int ModulesBuilt { get; private set; }
        public int RepairsStarted { get; private set; }
        public float MetalSpentOnRepairs { get; private set; }
        /// <summary>마지막 결정에서 원했던 모듈 (디버그용).</summary>
        public string LastNeed { get; private set; } = "-";

        public BalanceBot(StationSimulation sim, IReadOnlyList<ModuleData> buildable)
        {
            _sim = sim;
            foreach (var m in buildable)
            {
                if (m == null) continue;
                if (_power == null && Produces(m, ResourceType.Power)) _power = m;
                if (_oxygen == null && Produces(m, ResourceType.Oxygen)) _oxygen = m;
                if (_water == null && Produces(m, ResourceType.Water)) _water = m;
                if (_food == null && Produces(m, ResourceType.Food)) _food = m;
                if (_metal == null && Produces(m, ResourceType.Metal)) _metal = m;
                if (_housing == null && m.HousingCapacity > 0) _housing = m;
                if (_storage == null && m.StorageBonus > 0f) _storage = m;
                if (_battery == null && m.BatteryCapacity > 0f) _battery = m;
            }
        }

        public void Decide()
        {
            RepairDamaged();
            UpdatePowerModel();

            var need = PickNeed();
            if (need == null)
            {
                LastNeed = "-";
                return;
            }
            // 전력 소비 모듈인데 전력 여유가 부족하면 발전을 먼저
            if (need != _power && _power != null && PowerDemandOf(need) > PowerSurplus())
                need = _power;

            // 급한 것을 살 돈이 없으면, 금속 수입을 늘리는 채굴 도킹을 먼저 (지을 수 있을 때)
            if (!_sim.CanAfford(need) && CanBuildMetal() && _sim.CanAfford(_metal)
                && PowerDemandOf(_metal) <= PowerSurplus())
                need = _metal;

            LastNeed = need.name;
            if (!_sim.CanAfford(need) || _sim.CheckBuildable(need) != PlacementResult.Valid)
                return; // 저축
            if (TryFindPlacement(need, out var origin, out int rotation) && _sim.TryPlace(need, origin, rotation, out _))
                ModulesBuilt++;
        }

        private ModuleData PickNeed()
        {
            var r = _sim.Resources;
            if (_power != null && PowerSurplus() < PowerHeadroom)
                return _power;
            // 밤을 버틸 배터리: 용량(밤 길이 × 부족량 × 1.1)과 방전 속도(부족량) 둘 다
            if (_battery != null && _nightDeficit > 0f && _sim.CheckBuildable(_battery) == PlacementResult.Valid
                && (r.BatteryCapacity < _nightDeficit * _sim.DayNight.NightLength * 1.1f || r.BatteryRate < _nightDeficit))
                return _battery;
            // 금속 수입이 없으면 다른 건설이 모두 막히므로 최우선
            if (CanBuildMetal() && r.GetProduction(ResourceType.Metal) <= 0f)
                return _metal;
            if (_oxygen != null && r.GetNetRate(ResourceType.Oxygen) < NetMargin)
                return _oxygen;
            if (_water != null && r.GetNetRate(ResourceType.Water) < NetMargin)
                return _water;
            if (_food != null && r.GetNetRate(ResourceType.Food) < NetMargin)
                return _food;
            if (CanBuildMetal())
                return _metal; // 설치 한도까지 채굴 도킹
            if (_housing != null && r.Population >= r.HousingCapacity - 1)
                return _housing;
            if (_storage != null && _sim.CheckBuildable(_storage) == PlacementResult.Valid
                && r.GetStock(ResourceType.Metal) >= r.GetCapacity(ResourceType.Metal) * 0.95f)
                return _storage;
            // 다음 등급 모듈 수가 모자라면 수용 인구도 늘리는 거주 모듈로 채움
            var next = _sim.Progression.Next;
            if (next != null && _sim.Grid.ModuleCount < next.MinModules)
                return _housing;
            return null;
        }

        private void RepairDamaged()
        {
            _repairQueue.Clear();
            foreach (var info in _sim.Damage.DamagedModules)
            {
                if (!info.IsRepairing)
                    _repairQueue.Add(info.Module);
            }
            _repairQueue.Sort((a, b) =>
            {
                _sim.Damage.TryGetInfo(a, out var ia);
                _sim.Damage.TryGetInfo(b, out var ib);
                return ia.TimeUntilDestroyed.CompareTo(ib.TimeUntilDestroyed);
            });
            foreach (var module in _repairQueue)
            {
                var cost = _sim.Damage.GetRepairCost(module);
                if (_sim.TryRepair(module) == RepairResult.Started)
                {
                    RepairsStarted++;
                    foreach (var c in cost)
                        MetalSpentOnRepairs += c.Type == ResourceType.Metal ? c.Amount : 0f;
                }
            }
        }

        /// <summary>기존 모듈 주변 빈 셀 × 회전 중 유효한 자리. 맞닿는 면이 많을수록(조밀), 원점에 가까울수록 우선.</summary>
        private bool TryFindPlacement(ModuleData data, out Vector3Int bestOrigin, out int bestRotation)
        {
            bestOrigin = default;
            bestRotation = 0;
            _candidates.Clear();
            foreach (var module in _sim.Grid.Modules)
            {
                foreach (var cell in module.Cells)
                {
                    foreach (var dir in GridDirections.Faces)
                    {
                        var c = cell + dir;
                        if (!_sim.Grid.IsOccupied(c))
                            _candidates.Add(c);
                    }
                }
            }

            int rotations = data.CellOffsets.Count > 1 ? 4 : 1;
            float bestScore = float.MinValue;
            bool found = false;
            foreach (var origin in _candidates)
            {
                for (int rot = 0; rot < rotations; rot++)
                {
                    if (PlacementRules.Evaluate(_sim.Grid, data, origin, rot) != PlacementResult.Valid)
                        continue;
                    float score = Contacts(data, origin, rot) * 10f - origin.sqrMagnitude * 0.01f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestOrigin = origin;
                        bestRotation = rot;
                        found = true;
                    }
                }
            }
            return found;
        }

        private int Contacts(ModuleData data, Vector3Int origin, int rotation)
        {
            var cells = StationGrid.ResolveCells(data.CellOffsets, origin, rotation);
            int contacts = 0;
            foreach (var c in cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    var n = c + dir;
                    if (System.Array.IndexOf(cells, n) < 0 && _sim.Grid.IsOccupied(n))
                        contacts++;
                }
            }
            return contacts;
        }

        private bool CanBuildMetal() => _metal != null && _sim.CheckBuildable(_metal) == PlacementResult.Valid;

        /// <summary>새 전력 소비 모듈을 감당할 수 있는 여유 (낮 발전 − 수요 − 밤 대비 충전분).</summary>
        private float PowerSurplus() => _spareDayPower;

        /// <summary>
        /// 활성 모듈 기준 낮 발전량(태양광 100%)과 밤 부족량을 계산한다 (폭풍·파손은 무시하는 "정상 상태" 기준).
        /// 낮 동안 밤 부족분을 충전해야 하므로 필요 충전량 = 밤 부족량 × 밤/낮 길이 비.
        /// </summary>
        private void UpdatePowerModel()
        {
            float solar = 0f, other = 0f, demand = 0f;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data == null || !_sim.Connectivity.IsActive(m))
                    continue;
                foreach (var a in m.Data.Production)
                {
                    if (a.Type != ResourceType.Power) continue;
                    if (m.Data.SolarPowered) solar += a.Amount;
                    else other += a.Amount;
                }
                demand += PowerDemandOf(m.Data);
            }

            var cycle = _sim.DayNight;
            if (!cycle.Enabled)
            {
                _nightDeficit = 0f;
                _spareDayPower = solar + other - demand;
                return;
            }
            _nightDeficit = System.Math.Max(0f, demand - other - solar * cycle.NightMultiplier);
            float chargeNeed = _nightDeficit * cycle.NightLength / System.Math.Max(1f, cycle.DayLength);
            _spareDayPower = solar + other - demand - chargeNeed;
        }

        private static float PowerDemandOf(ModuleData m)
        {
            float total = 0f;
            foreach (var a in m.Consumption)
            {
                if (a.Type == ResourceType.Power)
                    total += a.Amount;
            }
            return total;
        }

        private static bool Produces(ModuleData m, ResourceType type)
        {
            foreach (var a in m.Production)
            {
                if (a.Type == type && a.Amount > 0f)
                    return true;
            }
            return false;
        }
    }
}
