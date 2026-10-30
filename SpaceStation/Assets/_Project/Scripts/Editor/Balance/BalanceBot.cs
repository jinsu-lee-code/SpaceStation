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
    public sealed partial class BalanceBot
    {
        private const float NetMargin = 0.2f;      // 순수지가 이보다 낮으면 생산 모듈 추가
        private const float PowerHeadroom = 2f;    // 전력 여유가 이보다 낮으면 발전 추가
        private const float MaintainAt = 55f;      // 이 내구도 미만이면 정비/재건축 (효율 저하 기준 50 직전)
        // 정비 후 최대 내구도가 이보다 낮으면 재건축. 정비의 초당 비용은 최대치와 무관하게 일정하므로
        // 효율 기준(50)을 유지할 수 있는 동안(최대 55 이상)은 정비가 재건축보다 싸다.
        private const float RebuildBelowMax = 55f;
        public const float DefaultMaintainAt = MaintainAt;
        public const float DefaultRebuildBelowMax = RebuildBelowMax;

        /// <summary>
        /// 13-8 측정 확장 고리 (M-47): 모듈마다 정비 기준 · 재건축 기준 (null = 모두 <see cref="DefaultMaintainAt"/> · <see cref="DefaultRebuildBelowMax"/>, 예전과 같음).
        /// 예: 서사판 캠페인 봇은 내구도 효율이 하는 일에 걸리지 않는 모듈(<see cref="WearMatters"/> 거짓)을 늦게 정비한다.
        /// </summary>
        public System.Func<ModuleInstance, float> MaintainAtFor { get; set; }
        public System.Func<ModuleInstance, float> RebuildBelowFor { get; set; }

        /// <summary>내구도 효율이 하는 일에 걸리는 모듈인지 (생산 · 실드 · 포탑 · 손상 통제실 · 서비스). 거짓이면(거주 · 배터리 · 창고 · 장갑 격벽 · 센서 …) 파괴(내구도 0) 전까지 하는 일이 같다.</summary>
        public static bool WearMatters(ModuleData data) =>
            data != null && (data.Production.Count > 0 || data.IsShield || data.IsTurret || data.IsDamageControl || data.IsService);

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
        // 두 번째 링부터: 건설비 + 이만큼 금속이 있을 때만 = _persona.RingMetalSpare (기준 100)
        /// <summary>Phase 6: 연구를 하는지 (비교 측정용, 기본 true).</summary>
        public static bool UseResearch = true;
        // 연구 우선순위 = _persona.ResearchOrder (기준: 유지보수 → 생산 → 에너지 → 건설·경제 → 거주 → 방어)
        public int ResearchStarted { get; private set; }
        // 방어 모듈 1개당 모듈 수 (4-8) = _persona.ModulesPerDefense (기준 15)

        /// <summary>13-7 봇 성향 (사용자 결정 2026-10-19, 기본 켬 · 끄면 중립 = 이전 봇): 시드마다 성장 ↔ 안전 · 저축 · 연구 순서가 다름 (<see cref="BalanceBotPersona"/>).</summary>
        public static bool Persona = true;
        private readonly BalanceBotPersona _persona;
        /// <summary>이 봇이 쓰는 성향 (요약 기록용).</summary>
        public BalanceBotPersona PersonaInUse => _persona;
        /// <summary>비교 측정용: 방어 모듈을 금속 여유와 무관하게 우선 건설 (기본 false).</summary>
        public static bool DefenseFirst;
        private readonly List<ModuleInstance> _covered = new List<ModuleInstance>();
        private readonly List<ModuleData> _services = new List<ModuleData>(); // 4-9 의료·여가
        private readonly List<ModuleData> _dietFoods = new List<ModuleData>(); // 남은 요구 식단: 식품 종류가 붙은 식량 모듈
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

        public BalanceBot(StationSimulation sim, IReadOnlyList<ModuleData> buildable, BalanceBotPersona persona = null)
        {
            _sim = sim;
            _buildable = buildable;
            _persona = persona ?? BalanceBotPersona.Neutral;
            // 역할마다 가장 이른 등급에 해금되는 모듈 (같으면 목록 앞쪽). 12-5에서 물 추출기(도시급)가 이름순으로 물 재활용기보다 앞에 와
            // 봇이 처음부터 못 짓는 모듈을 물 담당으로 잡아 5분 만에 물 고갈로 끝났음 (2026-10-15 수정)
            bool Pick(ModuleData current, ModuleData candidate) => current == null || UnlockOrder(candidate) < UnlockOrder(current);
            foreach (var m in buildable)
            {
                if (m == null) continue;
                if (_power == null && Produces(m, ResourceType.Power) && m.SolarPowered && !m.OnDemandPower) _power = m; // 기본 발전 = 태양광 (목록 순서와 무관)
                if (Produces(m, ResourceType.Oxygen) && Pick(_oxygen, m)) _oxygen = m;
                if (Produces(m, ResourceType.Water) && Pick(_water, m)) _water = m;
                if (Produces(m, ResourceType.Food) && Pick(_food, m)) _food = m;
                if (Produces(m, ResourceType.Metal) && Pick(_metal, m)) _metal = m;
                if (m.HousingCapacity > 0 && m.GrowthIntervalMultiplier >= 1f && Pick(_housing, m)) _housing = m;
                if (m.StorageBonus > 0f && Pick(_storage, m)) _storage = m;
                if (m.BatteryCapacity > 0f && Pick(_battery, m)) _battery = m;
                if (m.RepairSlots > 0 && Pick(_bay, m)) _bay = m;
                if (m.IsShield && Pick(_shield, m)) _shield = m;
                if (m.IsTurret && Pick(_turret, m)) _turret = m;
                if (m.IsDamageControl && Pick(_control, m)) _control = m;
                if (m.ResearchSlots > 0 && Pick(_lab, m)) _lab = m;
                if (m.IsService && m.HousingCapacity <= 0) _services.Add(m);
                if (m.FoodKinds != FoodKind.None && Produces(m, ResourceType.Food)) _dietFoods.Add(m); // 남은 요구 식단
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

        /// <summary>역할 고르기 순서: 해금 등급 (준비 중 · 해금 목록에 없음 = 맨 뒤).</summary>
        private int UnlockOrder(ModuleData m)
        {
            if (m.ComingSoon)
                return int.MaxValue;
            int grade = _sim.Progression.GetUnlockGrade(m);
            return grade < 0 ? int.MaxValue - 1 : grade;
        }

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

        /// <summary>밤을 버틸 배터리가 모자람: 용량(밤 길이 × 부족량 × 1.1)이나 방전 속도(부족량) 중 하나라도.</summary>
        private bool NightUncovered(ResourceSimulation r)
        {
            return _battery != null && _nightDeficit > 0f
                && (r.BatteryCapacity < _nightDeficit * _sim.DayNight.NightLength * 1.1f || r.BatteryRate < _nightDeficit);
        }

        // 8-1: 밤 부족량이 크고 금속 여유가 있으면 배터리·태양광 대신 핵융합로 (상시 발전)
        //   = _persona.FusionNightDeficit (기준 15) · _persona.FusionMetalReserve (기준 60)

        public int Maintenances { get; private set; }
        public int Rebuilds { get; private set; }
        public float MetalSpentOnUpkeep { get; private set; }

        /// <summary>
        /// 13-7 플레이어처럼 (사용자 결정 2026-10-16, 기본 켬 · 끄면 이전 봇): 유성우 예보 방향 앞면을 장갑 격벽으로 막기 ·
        /// 보급 상자를 처음부터 줍기 · 파손은 현장 수리 먼저(걸어가는 시간을 흉내 내 <see cref="FieldRepairInterval"/>초에 1번) ·
        /// 파손을 겪었는데 방어가 0개면 첫 방어를 금속 여유 없이 먼저 (2026-10-17).
        /// </summary>
        public static bool PlayerHabits = true;
        private const int ShowerBlocksPerWarning = 6;
        private const float BlockMetalReserve = 40f; // 막고도 수리할 금속은 남김
        private const float FieldRepairInterval = 30f;
        private bool _warned;
        private int _blocksThisWarning;
        private float _nextFieldRepair;
        private readonly List<ModuleInstance> _facing = new List<ModuleInstance>();
        public int ShowerBlocks { get; private set; }
        public int FieldRepairs { get; private set; }
        /// <summary>13-7 한 번이라도 파손을 겪었는지 (방어가 0개면 첫 방어를 금속 여유 없이 먼저).</summary>
        private bool _wasHit;

        public void Decide()
        {
            if (PlayerHabits)
            {
                BlockShower();
                PickUpCrates();
            }
            RepairDamaged();
            bool upkeepPending = MaintainWorn();
            UpdatePowerModel();
            if (upkeepPending)
            {
                LastNeed = "upkeep";
                return; // 정비비를 못 냈으면 새 건설 대신 저축
            }
            TryResearch();
            DecideExpansion(); // 12-4 도시급부터: 보급 상자 · 레시피 해금 연구 · 연계 연구 · 합성

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
            // 13-7 플레이어처럼 (2026-10-17): 다음 등급 조건 중 인구만 모자라고 수용이 꽉 찼으면, 생존 자원이 안정적이고 금속 수입이 있을 때는
            // 큰 저축(핵융합로)보다 거주가 먼저 — 저축 중에도 싼 거주는 짓는다. 생존 자원이 줄 때는 아래 순서 그대로 (생존 먼저)
            // 측정: 시드 8 · 9가 밤 전력 부족으로 핵융합로를 모으는 15 ~ 20분 동안 거주를 못 지어 중형에서 인구가 멈췄음 (BALANCE 47번)
            // 밤을 배터리로 못 버티면 거주보다 아래 발전 · 배터리가 먼저 (측정: 배터리까지 건너뛰어 12판 중 3판이 밤 정전 → 산소 고갈로 게임 오버)
            if (PlayerHabits && _housing != null && r.Population >= r.HousingCapacity - _persona.HousingAhead && OnlyPopulationShort(r) && LifeSupportStable(r)
                && !NightUncovered(r) && (!CanBuildMetal() || r.GetProduction(ResourceType.Metal) > 0f))
                return PickHousing(r);
            // 8-1: 밤 부족이 크면 핵융합로 (금속을 모아서 산다 — 저장 한도가 모자라면 창고부터)
            if (_fusion != null && _nightDeficit >= _persona.FusionNightDeficit && _sim.CheckBuildable(_fusion) == PlacementResult.Valid
                && LifeSupportStable(r)) // 생존 자원이 줄고 있으면 큰 구매(저축)보다 그쪽이 먼저
            {
                if (r.GetCapacity(ResourceType.Metal) >= Cost(_fusion) + _persona.FusionMetalReserve)
                    return _fusion;
                if (_storage != null && _sim.CheckBuildable(_storage) == PlacementResult.Valid)
                    return _storage;
            }
            // 밤을 버틸 배터리: 용량(밤 길이 × 부족량 × 1.1)과 방전 속도(부족량) 둘 다
            if (NightUncovered(r) && _sim.CheckBuildable(_battery) == PlacementResult.Valid)
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
            // 13-7 플레이어처럼: 다음 등급 조건 중 인구만 모자라고 수용이 꽉 찼으면 거주가 먼저 (방어 · 창고 · 채굴보다 앞)
            // 측정: 중형에서 모듈은 대형 조건을 넘었는데 수리 · 정비에 금속을 쓰느라 거주를 미뤄 인구가 30분 넘게 멈췄음
            if (PlayerHabits && _housing != null && r.Population >= r.HousingCapacity - _persona.HousingAhead && OnlyPopulationShort(r))
                return PickHousing(r);
            // 13-7 플레이어처럼: 파손을 겪었는데 방어 모듈이 하나도 없으면 첫 방어를 금속 여유와 무관하게 먼저 (저축해서 산다)
            // 측정: 시드 1 · 7 · 10이 초반 수리비로 금속 여유를 끝내 못 채워 방어 0개(격추 0) → 파손 누적 · 정체 · 게임 오버
            if (PlayerHabits && _wasHit && CountDefense() == 0)
            {
                var first = PickDefense();
                if (first != null)
                    return first;
            }
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
                && r.GetStock(ResourceType.Metal) >= Cost(_fuelCell) + (_housing != null ? Cost(_housing) * _persona.SafetySpareScale : 0f))
                return _fuelCell;
            // 8-5: 장갑 격벽 — 모듈 20개당 1개, 금속 여유가 있을 때 (외곽 미끼)
            if (_armor != null && (CountOf(_armor) + 1) * ModulesPerArmor <= _sim.Grid.ModuleCount
                && _sim.CheckBuildable(_armor) == PlacementResult.Valid
                && r.GetStock(ResourceType.Metal) >= Cost(_armor) + (_housing != null ? Cost(_housing) * _persona.SafetySpareScale : 0f))
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
            if (defense != null && (DefenseFirst || r.GetStock(ResourceType.Metal) >= Cost(defense) + (_housing != null ? Cost(_housing) * _persona.SafetySpareScale : 0f)))
                return defense;
            if (_housing != null && r.Population >= r.HousingCapacity - _persona.HousingAhead)
                return PickHousing(r);
            // 금속이 차 있어도, 저장 한도가 해금된 가장 비싼 모듈을 살 만큼 되면 창고는 그만 (8-6: 90분에 창고 167개 → 한도만큼만)
            if (_storage != null && _sim.CheckBuildable(_storage) == PlacementResult.Valid
                && r.GetStock(ResourceType.Metal) >= r.GetCapacity(ResourceType.Metal) * 0.95f
                && r.GetCapacity(ResourceType.Metal) < MaxUnlockedCost() + _persona.FusionMetalReserve)
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

        /// <summary>13-7 다음 등급 조건 중 모듈 수는 채웠고 인구만 모자람.</summary>
        private bool OnlyPopulationShort(ResourceSimulation r)
        {
            var next = _sim.Progression.Next;
            return next != null && StationProgression.CountGradeModules(_sim.Grid) >= next.MinModules && r.Population < next.MinPopulation;
        }

        /// <summary>8-4: 수용이 필요할 때 첫 회전 링은 저축해서라도(인구 증가 속도는 1개면 충분), 그다음 링은 금속 여유가 있을 때만. 아니면 거주 모듈.</summary>
        private ModuleData PickHousing(ResourceSimulation r)
        {
            if (_ring != null && _sim.CheckBuildable(_ring) == PlacementResult.Valid
                && (CountOf(_ring) == 0 || r.GetStock(ResourceType.Metal) >= Cost(_ring) + _persona.RingMetalSpare)
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
                _ringFits = HasAnyPlacement(_ring);
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
            foreach (var kind in _persona.ResearchOrder)
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
                if (status.Need == ResidentNeed.Diet)
                {
                    // 식단은 서비스 모듈이 아니라 식량 종류 비중 → 비중이 가장 낮은 종류를 만드는 식량 모듈 (가장 이른 해금)
                    var diet = PickDietFood();
                    if (diet != null)
                        return diet;
                    continue;
                }
                foreach (var m in _services)
                {
                    if (m.ServiceNeed == status.Need && _sim.CheckBuildable(m) == PlacementResult.Valid)
                        return m;
                }
            }
            return null;
        }

        private ModuleData PickDietFood()
        {
            FoodKind lacking = FoodKind.None;
            float lowest = float.MaxValue;
            foreach (var kind in FoodKindExtensions.All)
            {
                float share = _sim.Needs.DietShare(kind);
                if (share < lowest)
                {
                    lowest = share;
                    lacking = kind;
                }
            }
            ModuleData best = null;
            foreach (var m in _dietFoods)
            {
                if ((m.FoodKinds & lacking) == 0 || _sim.CheckBuildable(m) != PlacementResult.Valid)
                    continue;
                if (best == null || UnlockOrder(m) < UnlockOrder(best))
                    best = m;
            }
            return best;
        }

        /// <summary>서비스 모듈 배치 점수: 범위 안 거주 모듈의 아직 충족되지 않은 수용 인원.</summary>
        private float ServiceScore(ModuleData data, Vector3Int origin, int rotation)
        {
            if (!data.IsService)
                return 0f;
            var cells = StationGrid.ResolveCells(data.CellOffsets, origin, rotation);
            var box = new CellBox(cells);
            float unmet = 0f;
            foreach (var h in _serviceHabitats)
            {
                // 12-2: 상자끼리 범위 밖이면 칸별 비교 생략 (결과 동일, 큰 정거장에서 배치 탐색이 매우 느렸음)
                if (CellBox.InRange(cells, box, h.Cells, h.Box, _serviceRadius))
                    unmet += h.Unmet;
            }
            return unmet * 5f;
        }

        /// <summary>배치 탐색 한 번 동안 바뀌지 않는 거주 모듈별 미충족 수용 인원 (탐색 시작 때 한 번 계산).</summary>
        private void PrepareServiceScore(ModuleData data)
        {
            _serviceHabitats.Clear();
            if (!data.IsService)
                return;
            _serviceRadius = _sim.Effects.ServiceRadius(data);
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data == null || m.Data.HousingCapacity <= 0)
                    continue;
                float coverage = _sim.Needs.GetHabitatCoverage(m, data.ServiceNeed);
                _serviceHabitats.Add(new HabitatScore
                {
                    Cells = m.Cells,
                    Box = new CellBox(m.Cells),
                    Unmet = m.Data.HousingCapacity * (1f - Mathf.Max(0f, coverage)),
                });
            }
        }

        private struct HabitatScore
        {
            public IReadOnlyList<Vector3Int> Cells;
            public CellBox Box;
            public float Unmet;
        }

        private readonly List<HabitatScore> _serviceHabitats = new List<HabitatScore>();
        private int _serviceRadius;

        private static float Cost(ModuleData data)
        {
            float metal = 0f;
            foreach (var c in data.BuildCost)
                metal += c.Type == ResourceType.Metal ? c.Amount : 0f;
            return metal;
        }

        private int CountDefense()
        {
            int count = 0;
            foreach (var m in _sim.Grid.Modules)
            {
                if (m.Data != null && (m.Data.IsShield || m.Data.IsTurret || m.Data.IsDamageControl))
                    count++;
            }
            return count;
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
            if ((shields + turrets + controls + 1) * _persona.ModulesPerDefense > _sim.Grid.ModuleCount)
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
            float hits = _sim.EventScale.MeteorHitsMax;
            int desired = Mathf.CeilToInt(hits * _sim.Effects.RepairDuration / (b.DestroyAfterSeconds * 0.75f));
            if (_sim.Damage.Queue.Count > 0)
                desired = Mathf.Max(desired, _sim.CountRepairSlots() + 1); // 지금 대기가 생겼으면 하나 더
            return desired;
        }

        /// <summary>13-7 예보 중: 오는 방향에서 보이는 앞면 모듈(비싼 것부터) 바로 앞 칸에 장갑 격벽 — 결정마다 1개, 예보 한 번에 최대 6개.</summary>
        private void BlockShower()
        {
            var shower = _sim.MeteorShower;
            if (_armor == null || shower == null || shower.Phase != MeteorShowerSystem.ShowerPhase.Warning)
            {
                _warned = false;
                return;
            }
            if (!_warned)
            {
                _warned = true;
                _blocksThisWarning = 0;
            }
            if (_blocksThisWarning >= ShowerBlocksPerWarning || _sim.CheckBuildable(_armor) != PlacementResult.Valid
                || _sim.Resources.GetStock(ResourceType.Metal) < Cost(_armor) + BlockMetalReserve)
                return;
            var dir = shower.Direction;
            _facing.Clear();
            foreach (var m in shower.FacingModules)
                if (m.Data != _armor)
                    _facing.Add(m);
            _facing.Sort((a, b) => Cost(b.Data).CompareTo(Cost(a.Data)));
            foreach (var module in _facing)
            {
                foreach (var cell in shower.FacingCells)
                {
                    if (!_sim.Grid.TryGetModule(cell, out var at) || at != module)
                        continue;
                    if (_sim.TryPlace(_armor, cell + dir, 0, out _))
                    {
                        ModulesBuilt++;
                        ShowerBlocks++;
                        _blocksThisWarning++;
                        return;
                    }
                }
            }
        }

        /// <summary>13-7 현장 수리 (비용 50% · 즉시) — 걸어가는 시간을 흉내 내 간격을 둠. 반환: 고쳤는지.</summary>
        private bool TryFieldRepair(ModuleInstance module)
        {
            if (!PlayerHabits || _sim.ElapsedSeconds < _nextFieldRepair)
                return false;
            var cost = _sim.GetFieldRepairCost(module);
            if (_sim.TryFieldRepair(module) != RepairResult.Completed)
                return false;
            _nextFieldRepair = _sim.ElapsedSeconds + FieldRepairInterval;
            FieldRepairs++;
            RepairsStarted++;
            foreach (var c in cost)
                MetalSpentOnRepairs += c.Type == ResourceType.Metal ? c.Amount : 0f;
            return true;
        }

        private void RepairDamaged()
        {
            _repairQueue.Clear();
            foreach (var info in _sim.Damage.DamagedModules)
            {
                _wasHit = true;
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
                if (TryFieldRepair(module)) // 13-7 가장 급한 것부터 현장 수리 (간격 안에서 1개)
                    continue;
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
                if (info.Current < (MaintainAtFor != null ? MaintainAtFor(info.Module) : MaintainAt))
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
                if (durability.MaxAfterMaintenance(info) < (RebuildBelowFor != null ? RebuildBelowFor(module) : RebuildBelowMax))
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

        /// <summary>
        /// 13-8 측정 확장(<see cref="IBalanceExtension"/>)이 원하는 모듈을 봇의 자리 찾기로 지음 (예: 서사판 2장 센서 어레이).
        /// 해금 · 비용 · 자리가 맞으면 짓고 true.
        /// </summary>
        public bool TryBuild(ModuleData data)
        {
            if (data == null || !_sim.CanAfford(data) || _sim.CheckBuildable(data) != PlacementResult.Valid)
                return false;
            if (!TryFindPlacement(data, out var origin, out int rotation) || !_sim.TryPlace(data, origin, rotation, out _))
                return false;
            ModulesBuilt++;
            return true;
        }

        /// <summary>13-8 (M-47): <see cref="TryBuild(ModuleData)"/>와 같되 자리 점수에 extraScore(차지할 칸)를 더함 (예: 유성우 예보 방향 앞면을 덮는 실드 자리).</summary>
        public bool TryBuild(ModuleData data, System.Func<IReadOnlyList<Vector3Int>, float> extraScore)
        {
            _extraScore = extraScore;
            try
            {
                return TryBuild(data);
            }
            finally
            {
                _extraScore = null;
            }
        }

        private System.Func<IReadOnlyList<Vector3Int>, float> _extraScore;

        /// <summary>기존 모듈 주변 빈 셀 × 회전 중 유효한 자리. 맞닿는 면이 많을수록(조밀), 원점에 가까울수록 우선.</summary>
        private bool TryFindPlacement(ModuleData data, out Vector3Int bestOrigin, out int bestRotation)
        {
            bestOrigin = default;
            bestRotation = 0;
            CollectCandidates(data);

            PrepareServiceScore(data);
            var rotations = Rotations(data);
            float bestScore = float.MinValue;
            bool found = false;
            foreach (var origin in _candidates)
            {
                foreach (var rotation in rotations)
                {
                    int rot = rotation.Rotation;
                    if (AnyOccupied(origin, rotation.Offsets) || PlacementRules.Evaluate(_sim.Grid, data, origin, rot) != PlacementResult.Valid)
                        continue;
                    // 조밀 배치(운석 노출 감소) 선호. 태양광은 그늘 때문에 접촉 가산을 줄임.
                    float contactWeight = data.SolarPowered ? 1f : 10f;
                    float score = Contacts(data, origin, rot) * contactWeight - origin.sqrMagnitude * 0.01f
                                  + AdjacencyScore(data, origin, rot) + DefenseScore(data, origin, rot) + ServiceScore(data, origin, rot);
                    if (data == _refinery)
                        score += FreshDocksAt(origin, rot) * 40f;
                    if (data == _armor) // 미끼는 바깥으로 튀어나올수록 (조밀 가산을 뒤집음)
                        score -= Contacts(data, origin, rot) * contactWeight * 2f;
                    if (_extraScore != null)
                        score += _extraScore(StationGrid.ResolveCells(data.CellOffsets, origin, rot));
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

        /// <summary>유효한 자리가 하나라도 있는지만 (점수 계산 없이 첫 자리에서 멈춤 — <see cref="TryFindPlacement"/>의 성공 여부와 같다).</summary>
        private bool HasAnyPlacement(ModuleData data)
        {
            CollectCandidates(data);
            var rotations = Rotations(data);
            foreach (var origin in _candidates)
                foreach (var rotation in rotations)
                    if (!AnyOccupied(origin, rotation.Offsets)
                        && PlacementRules.Evaluate(_sim.Grid, data, origin, rotation.Rotation) == PlacementResult.Valid)
                        return true;
            return false;
        }

        /// <summary>
        /// 탐색할 회전과 회전한 칸 오프셋. 앞 회전과 차지하는 칸이 같은 회전은 뺀다 (3×3 회전 링은 1개만 — 판정 · 점수가 칸으로만 정해지므로,
        /// 점수가 같으면 먼저 본 회전이 이기던 예전 결과와 같다). 도킹은 회전이 입구 방향을 바꾸므로 그대로 4개.
        /// </summary>
        private List<RotationOffsets> Rotations(ModuleData data)
        {
            if (_rotations.TryGetValue(data, out var list))
                return list;
            list = new List<RotationOffsets>();
            int count = data.CellOffsets.Count > 1 ? 4 : 1;
            for (int rot = 0; rot < count; rot++)
            {
                var offsets = StationGrid.ResolveCells(data.CellOffsets, Vector3Int.zero, rot);
                bool duplicate = false;
                if (!data.TerminalOnly)
                {
                    var set = new HashSet<Vector3Int>(offsets);
                    foreach (var earlier in list)
                        duplicate |= set.SetEquals(earlier.Offsets);
                }
                if (!duplicate)
                    list.Add(new RotationOffsets { Rotation = rot, Offsets = offsets });
            }
            _rotations[data] = list;
            return list;
        }

        private struct RotationOffsets
        {
            public int Rotation;
            public Vector3Int[] Offsets;
        }

        private readonly Dictionary<ModuleData, List<RotationOffsets>> _rotations = new Dictionary<ModuleData, List<RotationOffsets>>();

        /// <summary>빈 칸이 아닌 곳이 있는지 (배치 판정보다 먼저 — 판정이 거절할 자리를 배열 할당 없이 거름).</summary>
        private bool AnyOccupied(Vector3Int origin, Vector3Int[] offsets)
        {
            foreach (var o in offsets)
                if (_sim.Grid.IsOccupied(origin + o))
                    return true;
            return false;
        }

        /// <summary>기존 모듈에 맞닿는 빈 칸 (여러 칸 모듈은 다른 칸이 닿는 원점까지) → <see cref="_candidates"/>.</summary>
        private void CollectCandidates(ModuleData data)
        {
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

    /// <summary>
    /// 13-7 봇 성향 (사용자 결정 2026-10-19): 시드에 묶인 플레이 성향 — 같은 시드는 늘 같은 성향(재현 가능), 판마다 플레이 방식이 다름.
    /// 세 축, 세기 = 기준값 ±40% (<see cref="Strength"/>):
    /// - 성장 ↔ 안전 (<see cref="Growth"/> -1 안전 ~ +1 성장): 방어 모듈 빈도(모듈 9 ~ 21개마다 1개) · 대비용 건설(방어 · 장갑 격벽 · 연료전지) 전에
    ///   남겨 둘 금속(거주 1개분 × 0.6 ~ 1.4, 안전형이 적게 요구) · 거주를 꽉 차기 전에 미리 짓기(성장형만, 1 ~ 4명 전부터)
    /// - 저축 (<see cref="Saving"/> -1 바로 쓰기 ~ +1 모으기): 핵융합로를 모으기 시작하는 밤 부족량(9 ~ 21) · 큰 구매 뒤 남길 금속(핵융합로 36 ~ 84 · 회전 링 60 ~ 140)
    /// - 연구 순서: 기본 순서(유지보수 → 생산 → 에너지 → 건설 · 경제 → 거주 → 방어)에서 이웃끼리 2번 자리 바꿈
    /// <see cref="Neutral"/> = 이전 봇과 같은 값 (<see cref="BalanceBot.Persona"/>를 끄면 이것).
    /// </summary>
    public sealed class BalanceBotPersona
    {
        public const float Strength = 0.4f;
        private const int ResearchSwaps = 2;
        private static readonly ResearchCategory[] BaseResearchOrder =
        {
            ResearchCategory.Maintenance, ResearchCategory.Production, ResearchCategory.Energy,
            ResearchCategory.Construction, ResearchCategory.Habitation, ResearchCategory.Defense,
        };

        public static readonly BalanceBotPersona Neutral = new BalanceBotPersona(0f, 0f, BaseResearchOrder);

        public float Growth { get; }
        public float Saving { get; }
        public IReadOnlyList<ResearchCategory> ResearchOrder { get; }

        public int ModulesPerDefense { get; }
        public float SafetySpareScale { get; }
        public int HousingAhead { get; }
        public float FusionNightDeficit { get; }
        public float FusionMetalReserve { get; }
        public float RingMetalSpare { get; }

        private BalanceBotPersona(float growth, float saving, ResearchCategory[] order)
        {
            Growth = growth;
            Saving = saving;
            ResearchOrder = order;
            ModulesPerDefense = Mathf.RoundToInt(15f * (1f + Strength * growth));
            SafetySpareScale = 1f + Strength * growth;
            HousingAhead = 1 + Mathf.RoundToInt(Mathf.Max(0f, growth) * Strength * 7.5f);
            FusionNightDeficit = 15f * (1f - Strength * saving);
            FusionMetalReserve = 60f * (1f + Strength * saving);
            RingMetalSpare = 100f * (1f + Strength * saving);
        }

        /// <summary>시드에서 성향 (SplitMix64 — 다른 난수 흐름을 쓰지 않으므로 이벤트 · 상자 결과와 독립).</summary>
        public static BalanceBotPersona FromSeed(int seed)
        {
            ulong n = 0;
            float Next() => (float)(Mix((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0xB07B07UL + (++n)) >> 11) * (1f / 9007199254740992f);
            float growth = Next() * 2f - 1f;
            float saving = Next() * 2f - 1f;
            var order = (ResearchCategory[])BaseResearchOrder.Clone();
            for (int i = 0; i < ResearchSwaps; i++)
            {
                int a = Mathf.Min((int)(Next() * (order.Length - 1)), order.Length - 2);
                (order[a], order[a + 1]) = (order[a + 1], order[a]);
            }
            return new BalanceBotPersona(growth, saving, order);
        }

        /// <summary>보급 상자 · 내부 결과 난수 시드 (<see cref="StationSimulationSettings.CrateSeed"/>).</summary>
        public static int CrateSeedFor(int seed) => (int)(Mix((ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0xC4A7EUL) & 0x7FFFFFFF);

        private static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static readonly Dictionary<ResearchCategory, string> ResearchNames = new Dictionary<ResearchCategory, string>
        {
            { ResearchCategory.Maintenance, "유지보수" }, { ResearchCategory.Production, "생산" }, { ResearchCategory.Energy, "에너지" },
            { ResearchCategory.Construction, "건설" }, { ResearchCategory.Habitation, "거주" }, { ResearchCategory.Defense, "방어" },
        };

        /// <summary>요약 한 줄: "성장 +0.62 · 저축 -0.31 (방어 19개마다 · 미리 거주 3 · 핵융합로 부족 17 여유 52) · 연구 유지보수 > 에너지 > …".</summary>
        public string Describe()
        {
            var names = new List<string>();
            foreach (var c in ResearchOrder)
                names.Add(ResearchNames.TryGetValue(c, out var s) ? s : c.ToString());
            return $"성장 {Growth:+0.00;-0.00;0.00} · 저축 {Saving:+0.00;-0.00;0.00} (방어 {ModulesPerDefense}개마다 · 미리 거주 {HousingAhead} · " +
                   $"핵융합로 부족 {FusionNightDeficit:0} 여유 {FusionMetalReserve:0}) · 연구 {string.Join(" > ", names)}";
        }
    }
}
