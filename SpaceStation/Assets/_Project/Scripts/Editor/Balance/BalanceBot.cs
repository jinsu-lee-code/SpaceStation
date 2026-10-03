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
        private readonly ModuleData _fusion; // 8-1 상시 대량 발전 (태양광이 아닌 발전 모듈 중 가장 큰 것)
        private readonly ModuleData _refinery; // 8-2 맞닿은 채굴 도킹 생산 증폭
        private readonly ModuleData _control;  // 8-3 손상 통제실 (방어 모듈 순환에 포함)
        private readonly ModuleData _ring;     // 8-4 회전 링 (대량 수용 + 여가 + 인구 증가 속도)
        private readonly ModuleData _fuelCell; // 8-5 보조 발전 (부족할 때만, 물 소비)
        private const int FuelCellMax = 2;          // 밤·폭풍 대비 보조 발전은 이만큼까지
        private const float FuelCellWaterMargin = 1f; // 물 순증가가 이 이상일 때만 (주민 몫 보호)
        private readonly ModuleData _cargo;    // 8-5 화물 터미널
        private const int CargoMax = 2;
        private readonly ModuleData _armor;    // 8-5 장갑 격벽 (운석 미끼)
        private const int ModulesPerArmor = 20;
        private const float RingMetalSpare = 100f; // 두 번째 링부터: 건설비 + 이만큼 금속이 있을 때만
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
            _buildable = buildable;
            foreach (var m in buildable)
            {
                if (m == null) continue;
                if (_power == null && Produces(m, ResourceType.Power) && m.SolarPowered && !m.OnDemandPower) _power = m; // 기본 발전 = 태양광 (목록 순서와 무관)
                if (_oxygen == null && Produces(m, ResourceType.Oxygen)) _oxygen = m;
                if (_water == null && Produces(m, ResourceType.Water)) _water = m;
                if (_food == null && Produces(m, ResourceType.Food)) _food = m;
                if (_metal == null && Produces(m, ResourceType.Metal)) _metal = m;
                if (_housing == null && m.HousingCapacity > 0 && m.GrowthIntervalMultiplier >= 1f) _housing = m;
                if (_storage == null && m.StorageBonus > 0f) _storage = m;
                if (_battery == null && m.BatteryCapacity > 0f) _battery = m;
                if (_bay == null && m.RepairSlots > 0) _bay = m;
                if (_shield == null && m.IsShield) _shield = m;
                if (_turret == null && m.IsTurret) _turret = m;
                if (_control == null && m.IsDamageControl) _control = m;
                if (_lab == null && m.ResearchSlots > 0) _lab = m;
                if (m.IsService && m.HousingCapacity <= 0) _services.Add(m);
                if (_ring == null && m.HousingCapacity > 0 && m.GrowthIntervalMultiplier < 1f) _ring = m; // 8-4 (거주 겸 서비스)
                if (_fuelCell == null && m.OnDemandPower && Produces(m, ResourceType.Power)) _fuelCell = m; // 8-5
                if (_cargo == null && m.IsCargoTerminal) _cargo = m;
                if (_armor == null && m.MeteorWeightMultiplier > 1f) _armor = m;
                if (!m.SolarPowered && !m.OnDemandPower && m.Removable && Produces(m, ResourceType.Power)
                    && (_fusion == null || PowerOutput(m) > PowerOutput(_fusion)))
                    _fusion = m;
            }
            if (_power == null)
                _power = _fusion; // 태양광이 없는 목록 (테스트 등)
            if (_fusion == _power)
                _fusion = null;
            // 8-2 채굴 도킹을 늘려 주는 모듈 (인접 규칙에서 찾음, 건설 목록에 있을 때만)
            var booster = sim.Adjacency.FindProductionBooster(_metal);
            foreach (var m in buildable)
                if (m != null && m == booster)
                    _refinery = m;
        }

        /// <summary>8-2: 새 제련소가 증폭할 수 있는(아직 제련소가 없는) 채굴 도킹이 이만큼 이상 맞닿는 자리가 있을 때 짓는다 (비교 측정용으로 바꿀 수 있음).</summary>
        public static int RefineryMinDocks = 1;

        private static float PowerOutput(ModuleData m)
        {
            float sum = 0f;
            foreach (var a in m.Production)
                if (a.Type == ResourceType.Power)
                    sum += a.Amount;
            return sum;
        }

        private bool LifeSupportStable(ResourceSimulation r)
        {
            return (_oxygen == null || r.GetNetRate(ResourceType.Oxygen) >= NetMargin)
                && (_water == null || r.GetNetRate(ResourceType.Water) >= NetMargin)
                && (_food == null || r.GetNetRate(ResourceType.Food) >= NetMargin);
        }

        /// <summary>8-1: 밤 부족량이 크고 금속 여유가 있으면 배터리·태양광 대신 핵융합로 (상시 발전).</summary>
        private const float FusionNightDeficit = 15f;
        private const float FusionMetalReserve = 60f;

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
            // 8-1: 밤 부족이 크면 핵융합로 (금속을 모아서 산다 — 저장 한도가 모자라면 창고부터)
            if (_fusion != null && _nightDeficit >= FusionNightDeficit && _sim.CheckBuildable(_fusion) == PlacementResult.Valid
                && LifeSupportStable(r)) // 생존 자원이 줄고 있으면 큰 구매(저축)보다 그쪽이 먼저
            {
                if (r.GetCapacity(ResourceType.Metal) >= Cost(_fusion) + FusionMetalReserve)
                    return _fusion;
                if (_storage != null && _sim.CheckBuildable(_storage) == PlacementResult.Valid)
                    return _storage;
            }
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
            // 8-5: 폭풍·배터리 부족 대비 보조 발전(연료전지) 2개까지 — 금속 여유(건설비 + 거주 1개분)와 물 순증가가 있을 때만
            // (측정: 배터리 대신·초반 우선으로 지으면 물 고갈 또는 확장 지연)
            if (_fuelCell != null && CountOf(_fuelCell) < FuelCellMax && _sim.CheckBuildable(_fuelCell) == PlacementResult.Valid
                && r.GetNetRate(ResourceType.Water) >= FuelCellWaterMargin
                && r.GetStock(ResourceType.Metal) >= Cost(_fuelCell) + (_housing != null ? Cost(_housing) : 0f))
                return _fuelCell;
            // 8-5: 장갑 격벽 — 모듈 20개당 1개, 금속 여유가 있을 때 (외곽 미끼)
            if (_armor != null && (CountOf(_armor) + 1) * ModulesPerArmor <= _sim.Grid.ModuleCount
                && _sim.CheckBuildable(_armor) == PlacementResult.Valid
                && r.GetStock(ResourceType.Metal) >= Cost(_armor) + (_housing != null ? Cost(_housing) : 0f))
                return _armor;
            // 8-5: 화물 터미널 2개까지 — 금속 여유가 있고 도킹 규칙을 만족하는 자리가 있을 때만
            if (_cargo != null && CountOf(_cargo) < CargoMax && _sim.CheckBuildable(_cargo) == PlacementResult.Valid
                && r.GetStock(ResourceType.Metal) >= Cost(_cargo) + (_housing != null ? Cost(_housing) : 0f)
                && CachedPlacement(_cargo, out _, out _))
                return _cargo;
            // 8-2: 도킹 한도에 닿았으면, 증폭 안 된 도킹에 붙일 자리가 있는 동안 제련소 (금속 수입을 더 늘리는 유일한 방법 → 저축해서 산다)
            if (_refinery != null && _sim.CheckBuildable(_refinery) == PlacementResult.Valid
                && CachedPlacement(_refinery, out var refOrigin, out int refRot) && FreshDocksAt(refOrigin, refRot) >= RefineryMinDocks)
                return _refinery;
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
                return PickHousing(r);
            // 금속이 차 있어도, 저장 한도가 해금된 가장 비싼 모듈을 살 만큼 되면 창고는 그만 (8-6: 90분에 창고 167개 → 한도만큼만)
            if (_storage != null && _sim.CheckBuildable(_storage) == PlacementResult.Valid
                && r.GetStock(ResourceType.Metal) >= r.GetCapacity(ResourceType.Metal) * 0.95f
                && r.GetCapacity(ResourceType.Metal) < MaxUnlockedCost() + FusionMetalReserve)
                return _storage;
            // 금속이 넘치는데 할 일이 없으면 수용을 늘려 인구 성장 (넘치는 금속을 버리지 않게)
            if (_housing != null && r.GetStock(ResourceType.Metal) >= r.GetCapacity(ResourceType.Metal) * 0.95f)
                return PickHousing(r);
            // 다음 등급 모듈 수가 모자라면 수용 인구도 늘리는 거주 모듈로 채움
            var next = _sim.Progression.Next;
            if (next != null && StationProgression.CountGradeModules(_sim.Grid) < next.MinModules)
                return _housing;
            return null;
        }

        /// <summary>8-4: 수용이 필요할 때 첫 회전 링은 저축해서라도(인구 증가 속도는 1개면 충분), 그다음 링은 금속 여유가 있을 때만. 아니면 거주 모듈.</summary>
        private ModuleData PickHousing(ResourceSimulation r)
        {
            if (_ring != null && _sim.CheckBuildable(_ring) == PlacementResult.Valid
                && (CountOf(_ring) == 0 || r.GetStock(ResourceType.Metal) >= Cost(_ring) + RingMetalSpare)
                && RingFits()) // 3×3 자리가 없으면 거주 모듈로 (저축하다 멈추지 않게)
                return _ring;
            return _housing;
        }

        // 3×3 자리 탐색은 비싸므로 그리드가 바뀔 때만 다시 (8-6: 90분 측정이 매 결정마다 탐색해 매우 느려짐)
        private int _ringCheckedVersion = -1;
        private bool _ringFits;

        private bool RingFits()
        {
            int version = _sim.Grid.ModuleCount;
            if (version != _ringCheckedVersion)
            {
                _ringCheckedVersion = version;
                _ringFits = TryFindPlacement(_ring, out _, out _);
            }
            return _ringFits;
        }

        /// <summary>배치 탐색 결과를 모듈 수가 바뀔 때까지 재사용 (규칙 판단용, 실제 배치는 매번 새로 탐색).</summary>
        private bool CachedPlacement(ModuleData data, out Vector3Int origin, out int rotation)
        {
            int version = _sim.Grid.ModuleCount;
            if (!_placementCache.TryGetValue(data, out var c) || c.version != version)
            {
                bool ok = TryFindPlacement(data, out var o, out int rot);
                c = (version, ok, o, rot);
                _placementCache[data] = c;
            }
            origin = c.origin;
            rotation = c.rotation;
            return c.ok;
        }

        private readonly Dictionary<ModuleData, (int version, bool ok, Vector3Int origin, int rotation)> _placementCache
            = new Dictionary<ModuleData, (int, bool, Vector3Int, int)>();

        private int CountOf(ModuleData data)
        {
            int n = 0;
            foreach (var m in _sim.Grid.Modules)
                if (m.Data == data)
                    n++;
            return n;
        }

        private readonly IReadOnlyList<ModuleData> _buildable;

        /// <summary>지금 지을 수 있는(해금된) 모듈 중 가장 비싼 금속 비용.</summary>
        private float MaxUnlockedCost()
        {
            float max = 0f;
            foreach (var m in _buildable)
                if (m != null && _sim.Progression.IsUnlocked(m))
                    max = Mathf.Max(max, Cost(m));
            return max;
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
            int shields = 0, turrets = 0, controls = 0;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data == null) continue;
                if (m.Data.IsShield) shields++;
                if (m.Data.IsTurret) turrets++;
                if (m.Data.IsDamageControl) controls++;
            }
            if ((shields + turrets + controls + 1) * ModulesPerDefense > _sim.Grid.ModuleCount)
                return null;
            // 실드 → 포탑 → 손상 통제실(8-3) 순으로 가장 적은 종류
            ModuleData best = null;
            int bestCount = int.MaxValue;
            void Consider(ModuleData data, int count)
            {
                if (data != null && count < bestCount && _sim.CheckBuildable(data) == PlacementResult.Valid)
                {
                    best = data;
                    bestCount = count;
                }
            }
            Consider(_shield, shields);
            Consider(_turret, turrets);
            Consider(_control, controls);
            return best;
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
                bool already = data.IsShield ? _sim.Defense.GetShieldBlockChance(_sim.Grid, m) > 0.001f
                    : data.IsDamageControl ? _sim.Defense.GetDamageControl(_sim.Grid, m).DestroyMultiplier > 1.001f
                    : data.IsDecoy ? IsNearDecoy(m)
                    : _sim.Defense.GetInterceptChance(_sim.Grid, m) >= _sim.Effects.TurretMaxIntercept - 1e-3f;
                if (!already)
                    fresh++;
            }
            return fresh * 8f;
        }

        /// <summary>이미 다른 장갑 격벽의 끌어오기 범위 안인지.</summary>
        private bool IsNearDecoy(ModuleInstance m)
        {
            foreach (var d in _sim.Grid.Modules)
                if (d != m && d.Data != null && d.Data.IsDecoy
                    && DefenseSystem.Distance(d.Cells, m.Cells) <= _sim.Effects.DecoyRadius(d.Data))
                    return true;
            return false;
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
                        if (_sim.Grid.IsOccupied(c))
                            continue;
                        _candidates.Add(c);
                        // 8-4: 3칸 이상 모듈(회전 링, 원점 = 가운데)은 다른 칸이 정거장에 닿는 원점도 후보
                        if (data.CellOffsets.Count > 2)
                            foreach (var o in data.CellOffsets)
                                if (o.y == 0)
                                    _candidates.Add(c - o);
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
                    if (data == _refinery)
                        score += FreshDocksAt(origin, rot) * 40f;
                    if (data == _armor) // 미끼는 바깥으로 튀어나올수록 (조밀 가산을 뒤집음)
                        score -= Contacts(data, origin, rot) * contactWeight * 2f;
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

        /// <summary>
        /// 인접 효과 점수: 자신에게 생길 효과와 이웃에게 새로 생길 효과를 같은 가중치로 (생산 +, 소비 −, 수용 인구 +).
        /// 7-6: 이웃 효과도 부호를 반영 (예전엔 개수만 세어 핵융합로를 거주 옆에 두는 것도 가산됐음).
        /// </summary>
        private float AdjacencyScore(ModuleData data, Vector3Int origin, int rotation)
        {
            _sim.PreviewAdjacency(data, origin, rotation, _previewSelf, _previewNeighbors);
            float score = 0f;
            foreach (var a in _previewSelf)
                score += EffectScore(a.Rule.Effect, a.Total);
            foreach (var n in _previewNeighbors)
                score += EffectScore(n.Rule.Effect, n.Total);
            return score;
        }

        private static float EffectScore(AdjacencyEffect effect, float total)
        {
            switch (effect)
            {
                case AdjacencyEffect.Production: return total * 30f;
                case AdjacencyEffect.Consumption: return -total * 15f;
                case AdjacencyEffect.Housing: return total * 3f;
                default: return 0f;
            }
        }

        private readonly List<AppliedAdjacency> _previewSelf = new List<AppliedAdjacency>();
        private readonly List<NeighborAdjacencyPreview> _previewNeighbors = new List<NeighborAdjacencyPreview>();

        /// <summary>제련소를 이 자리에 놓으면 맞닿게 되는 채굴 도킹 중 아직 제련소와 맞닿지 않은 것의 수.</summary>
        private int FreshDocksAt(Vector3Int origin, int rotation)
        {
            var cells = StationGrid.ResolveCells(_refinery.CellOffsets, origin, rotation);
            _freshDocks.Clear();
            foreach (var c in cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    if (_sim.Grid.TryGetModule(c + dir, out var n) && n.Data == _metal && !_freshDocks.Contains(n))
                    {
                        _sim.Grid.GetNeighborModules(n, _neighborScratch);
                        bool boosted = false;
                        foreach (var nn in _neighborScratch)
                            boosted |= nn.Data == _refinery;
                        if (!boosted)
                            _freshDocks.Add(n);
                    }
                }
            }
            return _freshDocks.Count;
        }

        private readonly List<ModuleInstance> _freshDocks = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _neighborScratch = new List<ModuleInstance>();

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
                    if (m.Data.OnDemandPower) continue; // 8-5 보조 발전: 계획에 넣지 않음 (아래 밤 부족 주석)
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
            // 8-5: 보조 발전은 물을 쓰므로 밤 계획(배터리)에는 넣지 않는다 — 폭풍·배터리 부족 때의 예비 (측정: 넣으면 물 고갈)
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
