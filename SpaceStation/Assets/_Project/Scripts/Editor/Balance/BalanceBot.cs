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
        private const float MaintainAt = 55f;      // 이 내구도 미만이면 정비/재건축 (효율 저하 기준 50 직전)
        // 정비 후 최대 내구도가 이보다 낮으면 재건축. 정비의 초당 비용은 최대치와 무관하게 일정하므로
        // 효율 기준(50)을 유지할 수 있는 동안(최대 55 이상)은 정비가 재건축보다 싸다.
        private const float RebuildBelowMax = 55f;

        private readonly List<DurabilityInfo> _maintainQueue = new List<DurabilityInfo>();

        private readonly StationSimulation _sim;
        private readonly ModuleData _power, _oxygen, _water, _food, _housing, _storage, _metal, _battery, _bay, _shield, _turret, _lab;
        /// <summary>Phase 6: 연구를 하는지 (비교 측정용, 기본 true).</summary>
        public static bool UseResearch = true;
        /// <summary>연구 우선순위: 유지보수 → 생산 → 에너지 → 건설·경제 → 거주 → 방어.</summary>
        private static readonly ResearchCategory[] ResearchOrder =
        {
            ResearchCategory.Maintenance, ResearchCategory.Production, ResearchCategory.Energy,
            ResearchCategory.Construction, ResearchCategory.Habitation, ResearchCategory.Defense,
        };
        public int ResearchStarted { get; private set; }
        private const int ModulesPerDefense = 15; // 방어 모듈 1개당 모듈 수 (4-8)
        /// <summary>비교 측정용: 방어 모듈을 금속 여유와 무관하게 우선 건설 (기본 false).</summary>
        public static bool DefenseFirst;
        private readonly List<ModuleInstance> _covered = new List<ModuleInstance>();
        private readonly List<ModuleData> _services = new List<ModuleData>(); // 4-9 의료·여가
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
                if (_bay == null && m.RepairSlots > 0) _bay = m;
                if (_shield == null && m.IsShield) _shield = m;
                if (_turret == null && m.IsTurret) _turret = m;
                if (_lab == null && m.ResearchSlots > 0) _lab = m;
                if (m.IsService) _services.Add(m);
            }
        }

        public int Maintenances { get; private set; }
        public int Rebuilds { get; private set; }
        public float MetalSpentOnUpkeep { get; private set; }

        public void Decide()
        {
            RepairDamaged();
            bool upkeepPending = MaintainWorn();
            UpdatePowerModel();
            if (upkeepPending)
            {
                LastNeed = "upkeep";
                return; // 정비비를 못 냈으면 새 건설 대신 저축
            }
            TryResearch();

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
            // 4-9: 충족률이 낮은 요구의 서비스 모듈 (만족도 상한 → 성장 속도)
            var service = PickService();
            if (service != null)
                return service;
            // 4-6: 현재 등급 최대 운석 수를 파괴 전에(여유 25%) 다 고칠 수 있을 만큼 수리 슬롯
            if (_bay != null && _sim.CheckBuildable(_bay) == PlacementResult.Valid && _sim.CountRepairSlots() < DesiredRepairSlots())
                return _bay;
            if (CanBuildMetal())
                return _metal; // 설치 한도까지 채굴 도킹
            // Phase 6: 소형부터 연구소 1개, 중형부터 2개
            if (UseResearch && _lab != null && _sim.Research.Categories.Count > 0 && CountLabs() < LabsWanted()
                && _sim.CheckBuildable(_lab) == PlacementResult.Valid)
                return _lab;
            // 4-8: 모듈 15개당 방어 모듈 1개 (실드·포탑 번갈아). 금속이 건설비 + 거주 모듈 1개분 이상 남을 때만 → 성장을 막지 않음
            // DefenseFirst(비교 측정용)이면 금속 여유와 무관하게 먼저 짓는다
            var defense = PickDefense();
            if (defense != null && (DefenseFirst || r.GetStock(ResourceType.Metal) >= Cost(defense) + (_housing != null ? Cost(_housing) : 0f)))
                return defense;
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

        private int LabsWanted() => _sim.Progression.GradeIndex >= 2 ? 2 : _sim.Progression.GradeIndex >= 1 ? 1 : 0;

        private int CountLabs()
        {
            int n = 0;
            foreach (var m in _sim.Grid.Modules)
                if (m.Data == _lab)
                    n++;
            return n;
        }

        /// <summary>
        /// 빈 연구소가 있으면 우선순위대로 시작 가능한 연구 하나. 전력 여유가 연구 수요 이상이고,
        /// 시작 비용을 내도 산소·물이 저장 한도의 40%, 금속이 40 이상 남을 때만 (위기 방지).
        /// </summary>
        private void TryResearch()
        {
            var research = _sim.Research;
            if (!UseResearch || research.Categories.Count == 0 || !research.HasFreeSlot)
                return;
            var r = _sim.Resources;
            foreach (var kind in ResearchOrder)
            {
                var category = research.Find(kind);
                if (category == null || _sim.CanStartResearch(category) != ResearchStartResult.Ok)
                    continue;
                var level = category.GetLevel(research.GetLevel(category) + 1);
                if (PowerSurplus() < level.PowerDemand)
                    continue;
                bool safe = true;
                foreach (var a in level.StartCost)
                {
                    float reserve = a.Type == ResourceType.Metal ? 40f : r.GetCapacity(a.Type) * 0.4f;
                    if (r.GetStock(a.Type) - a.Amount < reserve)
                        safe = false;
                }
                if (!safe)
                    continue;
                if (_sim.TryStartResearch(category) == ResearchStartResult.Ok)
                {
                    ResearchStarted++;
                    return;
                }
            }
        }

        private ModuleData PickService()
        {
            foreach (var status in _sim.Needs.Statuses)
            {
                if (status.Ratio >= 0.95f)
                    continue;
                foreach (var m in _services)
                {
                    if (m.ServiceNeed == status.Need && _sim.CheckBuildable(m) == PlacementResult.Valid)
                        return m;
                }
            }
            return null;
        }

        /// <summary>서비스 모듈 배치 점수: 범위 안 거주 모듈의 아직 충족되지 않은 수용 인원.</summary>
        private float ServiceScore(ModuleData data, Vector3Int origin, int rotation)
        {
            if (!data.IsService)
                return 0f;
            var cells = StationGrid.ResolveCells(data.CellOffsets, origin, rotation);
            float unmet = 0f;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data == null || m.Data.HousingCapacity <= 0 || !DefenseSystem.InRange(cells, m.Cells, _sim.Effects.ServiceRadius(data)))
                    continue;
                float coverage = _sim.Needs.GetHabitatCoverage(m, data.ServiceNeed);
                unmet += m.Data.HousingCapacity * (1f - Mathf.Max(0f, coverage));
            }
            return unmet * 5f;
        }

        private static float Cost(ModuleData data)
        {
            float metal = 0f;
            foreach (var c in data.BuildCost)
                metal += c.Type == ResourceType.Metal ? c.Amount : 0f;
            return metal;
        }

        private ModuleData PickDefense()
        {
            int shields = 0, turrets = 0;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data == null) continue;
                if (m.Data.IsShield) shields++;
                if (m.Data.IsTurret) turrets++;
            }
            if ((shields + turrets + 1) * ModulesPerDefense > _sim.Grid.ModuleCount)
                return null;
            bool shieldOk = _shield != null && _sim.CheckBuildable(_shield) == PlacementResult.Valid;
            bool turretOk = _turret != null && _sim.CheckBuildable(_turret) == PlacementResult.Valid;
            if (shieldOk && (!turretOk || shields <= turrets))
                return _shield;
            return turretOk ? _turret : null;
        }

        /// <summary>방어 모듈 배치 점수: 아직 같은 종류의 보호를 받지 않는 모듈을 범위에 많이 넣을수록.</summary>
        private float DefenseScore(ModuleData data, Vector3Int origin, int rotation)
        {
            if (!data.IsDefense)
                return 0f;
            var cells = StationGrid.ResolveCells(data.CellOffsets, origin, rotation);
            DefenseSystem.CountCovered(_sim.Grid, data, cells, _covered, _sim.Effects.DefenseRadiusBonus);
            int fresh = 0;
            foreach (var m in _covered)
            {
                bool already = data.IsShield
                    ? _sim.Defense.GetShieldBlockChance(_sim.Grid, m) > 0.001f
                    : _sim.Defense.GetInterceptChance(_sim.Grid, m) >= _sim.Effects.TurretMaxIntercept - 1e-3f;
                if (!already)
                    fresh++;
            }
            return fresh * 8f;
        }

        private int DesiredRepairSlots()
        {
            var b = _sim.Balance;
            float hits = _sim.Progression.Current.MeteorHitsMax;
            int desired = Mathf.CeilToInt(hits * _sim.Effects.RepairDuration / (b.DestroyAfterSeconds * 0.75f));
            if (_sim.Damage.Queue.Count > 0)
                desired = Mathf.Max(desired, _sim.CountRepairSlots() + 1); // 지금 대기가 생겼으면 하나 더
            return desired;
        }

        private void RepairDamaged()
        {
            _repairQueue.Clear();
            foreach (var info in _sim.Damage.DamagedModules)
            {
                if (!info.IsRepairing && !info.IsQueued)
                    _repairQueue.Add(info.Module);
            }
            _repairQueue.Sort((a, b) =>
            {
                _sim.Damage.TryGetInfo(a, out var ia);
                _sim.Damage.TryGetInfo(b, out var ib);
                return ia.TimeUntilDestroyed.CompareTo(ib.TimeUntilDestroyed);
            });
            var damage = _sim.Damage;
            foreach (var module in _repairQueue)
            {
                // 대기열에 넣어도 파괴 전에 시작 못 할 모듈에는 돈을 쓰지 않음
                // (보수적 예상 시작 = ceil((대기 수 + 1) / 슬롯) × 수리 시간)
                damage.TryGetInfo(module, out var info);
                int capacity = Mathf.Max(1, damage.RepairCapacity);
                float expectedStart = damage.HasFreeRepairSlot ? 0f
                    : Mathf.Ceil((damage.Queue.Count + 1f) / capacity) * _sim.Effects.RepairDuration;
                // 단, 대기열 등록만으로 확산이 멈추는 설정이면 확산 전 모듈은 등록한다 (4-7)
                if (expectedStart >= info.TimeUntilDestroyed && !(info.SpreadPending && _sim.Balance.QueuePausesSpread))
                    continue;
                var cost = damage.GetRepairCost(module);
                var result = _sim.TryRepair(module);
                if (result == RepairResult.Started || result == RepairResult.Queued)
                {
                    RepairsStarted++;
                    foreach (var c in cost)
                        MetalSpentOnRepairs += c.Type == ResourceType.Metal ? c.Amount : 0f;
                }
            }

            // 대기 중에도 확산이 흐르는 설정이면, 확산이 가장 임박한 대기 모듈을 우선 수리
            if (!_sim.Balance.QueuePausesSpread && damage.Queue.Count > 1)
            {
                DamageInfo urgent = null;
                foreach (var q in damage.Queue)
                {
                    if (q.SpreadPending && (urgent == null || q.TimeUntilSpread < urgent.TimeUntilSpread))
                        urgent = q;
                }
                if (urgent != null)
                    _sim.TryPrioritizeRepair(urgent.Module);
            }
        }

        /// <summary>
        /// 효율이 떨어지기 직전(내구도 55 미만)인 모듈을 관리. 금속 생산 모듈(수입원)을 먼저, 그다음 내구도 낮은 순.
        /// 정비해도 최대치가 60 미만으로 떨어지면(곧 다시 효율 저하) 재건축, 아니면 정비.
        /// 반환: 비용이 모자라 처리하지 못한 모듈이 있는지 (있으면 새 건설을 멈추고 저축).
        /// </summary>
        private bool MaintainWorn()
        {
            var durability = _sim.Durability;
            _maintainQueue.Clear();
            foreach (var info in durability.Modules)
            {
                if (info.Current < MaintainAt)
                    _maintainQueue.Add(info);
            }
            if (_maintainQueue.Count == 0)
                return false;
            _maintainQueue.Sort((a, b) =>
            {
                bool am = a.Module.Data == _metal, bm = b.Module.Data == _metal;
                if (am != bm) return am ? -1 : 1;
                return a.Current.CompareTo(b.Current);
            });

            bool pending = false;
            foreach (var info in _maintainQueue)
            {
                var module = info.Module;
                if (durability.MaxAfterMaintenance(info) < RebuildBelowMax)
                {
                    float cost = MetalOf(_sim.GetRebuildCost(module));
                    var result = _sim.TryRebuild(module, out _);
                    if (result == RebuildResult.Done)
                    {
                        Rebuilds++;
                        MetalSpentOnUpkeep += cost;
                    }
                    else if (result == RebuildResult.InsufficientResources)
                    {
                        pending = true;
                        break; // 우선순위가 높은 것부터 돈을 모음
                    }
                }
                else
                {
                    float cost = MetalOf(durability.GetMaintenanceCost(module));
                    var result = _sim.TryMaintain(module);
                    if (result == MaintainResult.Done)
                    {
                        Maintenances++;
                        MetalSpentOnUpkeep += cost;
                    }
                    else if (result == MaintainResult.InsufficientResources)
                    {
                        pending = true;
                        break;
                    }
                }
            }
            return pending;
        }

        private static float MetalOf(List<ResourceAmount> cost)
        {
            float total = 0f;
            foreach (var c in cost)
                if (c.Type == ResourceType.Metal) total += c.Amount;
            return total;
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
                    // 조밀 배치(운석 노출 감소) 선호. 태양광은 그늘 때문에 접촉 가산을 줄임.
                    float contactWeight = data.SolarPowered ? 1f : 10f;
                    float score = Contacts(data, origin, rot) * contactWeight - origin.sqrMagnitude * 0.01f
                                  + AdjacencyScore(data, origin, rot) + DefenseScore(data, origin, rot) + ServiceScore(data, origin, rot);
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

        /// <summary>인접 효과 점수: 자신에게 생길 효과(생산 +, 소비 −, 수용 인구 +)와 이웃에게 줄 효과 개수.</summary>
        private float AdjacencyScore(ModuleData data, Vector3Int origin, int rotation)
        {
            _sim.PreviewAdjacency(data, origin, rotation, _previewSelf, _previewNeighbors);
            float score = 0f;
            foreach (var a in _previewSelf)
            {
                switch (a.Rule.Effect)
                {
                    case AdjacencyEffect.Production: score += a.Total * 30f; break;
                    case AdjacencyEffect.Consumption: score -= a.Total * 15f; break;
                    case AdjacencyEffect.Housing: score += a.Total * 3f; break;
                }
            }
            return score + _previewNeighbors.Count * 2f; // 이웃 효과는 부호를 모르므로 약하게
        }

        private readonly List<AppliedAdjacency> _previewSelf = new List<AppliedAdjacency>();
        private readonly List<string> _previewNeighbors = new List<string>();

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
                float adjacency = _sim.Adjacency.GetProductionMultiplier(m); // 태양광 그늘 등 (4-4)
                foreach (var a in m.Data.Production)
                {
                    if (a.Type != ResourceType.Power) continue;
                    if (m.Data.SolarPowered) solar += a.Amount * adjacency;
                    else other += a.Amount * adjacency;
                }
                demand += PowerDemandOf(m.Data);
            }
            solar *= _sim.Effects.SolarMultiplier;          // 에너지 연구
            demand += _sim.Research.RunningPowerDemand;     // 진행 중인 연구 (Phase 6)

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
