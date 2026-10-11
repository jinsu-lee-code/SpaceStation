using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>StationSimulation 생성 설정.</summary>
    public sealed class StationSimulationSettings
    {
        public BalanceConfig Balance;
        public StationGradeConfig Grades;
        public ModuleData CoreModule;
        public IReadOnlyList<GameEventData> Events;
        /// <summary>공간 인접 효과 규칙 (null이면 효과 없음).</summary>
        public AdjacencyRuleSet AdjacencyRules;
        /// <summary>[0, 1) 난수. 이벤트 선택·간격, 운석 대상에 사용 (시드 고정 가능).</summary>
        public Func<float> Random01;
        /// <summary>Phase 6 연구 카테고리 (null·비면 연구 없음).</summary>
        public IReadOnlyList<ResearchCategoryData> ResearchCategories;
        /// <summary>연구 레벨 상한 (null이면 조건 없음).</summary>
        public ResearchLevelCapConfig ResearchCaps;
        /// <summary>Phase 10 주민 특성 (null이면 명단 없음 = 이전과 같은 결과).</summary>
        public ResidentConfig Residents;
        /// <summary>주민 이름·특성 난수 [0, 1). null이면 시스템 난수 (이벤트 난수 순서와 분리).</summary>
        public Func<float> ResidentRandom01;
    }

    /// <summary>
    /// 정거장 한 판의 전체 시뮬레이션 (MonoBehaviour 없음). 게임(SimulationHost)과 측정 도구(밸런스 봇)가 공유한다.
    /// 틱 순서: 파손(수리 진행·방치 파괴) → 자원(활성 모듈 + 파손 배율 + 누출 + 낮/밤·배터리) → 인구 → 이벤트(효과 즉시 적용) → 등급/승패.
    /// 배치·철거·수리 명령도 여기서만 처리한다 (공간 규칙 → 등급 해금/설치 제한 → 비용).
    /// </summary>
    public sealed class StationSimulation
    {
        private readonly List<ModuleData> _activeModules = new List<ModuleData>();
        private readonly List<float> _productionMultipliers = new List<float>();
        private readonly List<float> _consumptionMultipliers = new List<float>();
        private readonly List<ModuleInstance> _meteorCandidates = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _meteorTargetsCopy = new List<ModuleInstance>();
        private readonly List<GameEventData> _eventPool;
        private readonly Func<float> _random01;

        /// <summary>(요약 메시지, 긍정 여부). 이벤트 효과 알림.</summary>
        public event Action<string, bool> EffectReported;
        /// <summary>파손이 번짐 (원본, 대상). 4-7</summary>
        public event Action<ModuleInstance, ModuleInstance> DamageSpread;
        /// <summary>등급·승패 재판정 직후 (UI 갱신용).</summary>
        public event Action ProgressionEvaluated;
        /// <summary>실드가 운석을 빗겨냄 (빗겨낸 실드 모듈). 5-5 실드 연출용.</summary>
        public event Action<ModuleInstance> ShieldDeflected;
        /// <summary>운석 한 발의 경로 (5-7 연출). 피해 적용 전에 발생한다.</summary>
        public event Action<MeteorFlight> MeteorResolved;

        public BalanceConfig Balance { get; }
        public StationGrid Grid { get; }
        public StationConnectivity Connectivity { get; }
        public ResourceSimulation Resources { get; }
        public PopulationSimulation Population { get; }
        public DamageSystem Damage { get; }
        public DurabilitySystem Durability { get; }
        public AdjacencySystem Adjacency { get; }
        public DefenseSystem Defense { get; }
        /// <summary>8-5 화물 터미널 화물선.</summary>
        public CargoSystem Cargo { get; } = new CargoSystem();
        /// <summary>11-17 ② 내부 보급 상자 · 연구 포인트 · 무료 수리권.</summary>
        public SupplyCrateSystem Supply { get; } = new SupplyCrateSystem();
        // 상자 내용 난수는 이벤트 난수와 따로 (밸런스 봇의 시드 결과가 바뀌지 않게). 고정 시드면 매 판 처음 상자들이 늘 같았음(물 4개)
        private readonly Random _crateRandom = new Random();
        public NeedsSystem Needs { get; }
        public FailureMonitor Failure { get; }
        /// <summary>Phase 6 연구 (진행·레벨). 효과는 <see cref="Effects"/>.</summary>
        public ResearchSystem Research { get; }
        /// <summary>현재 연구 효과 (각 시스템이 공유).</summary>
        public ResearchEffects Effects { get; }
        /// <summary>자동 정비·재건축·일괄 정비 (자동화 연구 완료 후 동작).</summary>
        public MaintenanceAutomation Automation { get; }
        private readonly List<ModuleInstance> _coreNeighbors = new List<ModuleInstance>();
        private readonly List<ResidentNeed> _activeNeeds = new List<ResidentNeed>();
        /// <summary>현재 등급까지 생긴 거주자 요구 (4-9).</summary>
        public IReadOnlyList<ResidentNeed> ActiveNeeds => _activeNeeds;
        public EventScheduler Events { get; }
        /// <summary>Phase 10 주민 명단 (설정에 ResidentConfig가 없으면 null).</summary>
        public ResidentRoster Residents { get; }
        public StationProgression Progression { get; }
        public GameSession Session { get; }
        public DayNightCycle DayNight { get; }
        public ModuleInstance Core { get; }
        public IReadOnlyList<GameEventData> EventPool => _eventPool;
        /// <summary>시뮬레이션 경과 시간(초).</summary>
        public float ElapsedSeconds { get; private set; }
        /// <summary>현재 등급의 이벤트 강도 배율.</summary>
        public float EventIntensity => Progression.Current.EventIntensityMultiplier;

        public StationSimulation(StationSimulationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            Balance = settings.Balance ?? throw new ArgumentException("Balance 필요", nameof(settings));
            _random01 = settings.Random01 ?? throw new ArgumentException("Random01 필요", nameof(settings));
            _eventPool = settings.Events != null ? new List<GameEventData>(settings.Events) : new List<GameEventData>();

            Grid = new StationGrid();
            Connectivity = new StationConnectivity(Grid, new FaceAdjacencyConnectionRule());
            Resources = new ResourceSimulation(Balance);
            Population = new PopulationSimulation(Balance, Resources);
            Damage = new DamageSystem(Balance);
            Durability = new DurabilitySystem(Balance);
            Adjacency = new AdjacencySystem(settings.AdjacencyRules);
            Defense = new DefenseSystem(Balance, ModuleStrength);
            Damage.ControlLookup = m => Defense.GetDamageControl(Grid, m); // 8-3 손상 통제실
            Needs = new NeedsSystem(Balance, ModuleStrength, EffectiveHousing);
            Failure = new FailureMonitor(Balance);
            Events = new EventScheduler(Balance.EventGracePeriod, Balance.EventIntervalMin, Balance.EventIntervalMax, _random01);
            Progression = new StationProgression(settings.Grades);
            Session = new GameSession();
            DayNight = new DayNightCycle(Balance.DayNightPeriod, Balance.DayLength, Balance.DayNightTransition, Balance.NightSolarMultiplier);

            // Phase 6 연구: 효과 하나를 모든 시스템이 공유 (연구가 없으면 밸런스 값 그대로)
            Effects = new ResearchEffects(Balance);
            Resources.Effects = Effects;
            Damage.Effects = Effects;
            Durability.Effects = Effects;
            Adjacency.Effects = Effects;
            Defense.Effects = Effects;
            Needs.Effects = Effects;
            Research = new ResearchSystem(settings.ResearchCategories, settings.ResearchCaps, Effects);
            Effects.Changed += HandleResearchEffectsChanged;
            Automation = new MaintenanceAutomation(this);

            // 4-1: 등급별 이벤트 빈도·강도 (새 간격을 정할 때 / 지속형 이벤트가 시작될 때의 등급 기준)
            Events.IntervalMultiplier = () => Progression.Current.EventIntervalMultiplier;
            Events.DurationProvider = data => data.Duration * EventIntensity;
            // Phase 6 방어 연구: 바깥에서 오는 위협(운석·태양 폭풍)만 조기 경보
            Events.WarningFilter = data => data is MeteorEventData || data is SolarStormEventData;
            Events.UpcomingChanged += HandleUpcomingChanged;

            // 이 구독들이 뷰(StationController)보다 먼저 등록되어, 뷰가 생성될 때 연결 상태가 이미 최신이다.
            Grid.ModulePlaced += HandleModulePlaced;
            Grid.ModuleRemoved += HandleModuleRemoved;
            Damage.Destroyed += HandleDestroyed;
            // 4-6: 정비 베이가 파손·복구되면 즉시 슬롯 수 갱신
            Damage.Damaged += _ => RefreshRepairCapacity();
            Damage.Repaired += _ => RefreshRepairCapacity();
            Damage.SpreadDue += HandleSpreadDue; // 4-7
            Durability.WornOut += HandleDestroyed;
            Events.EventStarted += HandleEventStarted;
            Events.EventEnded += HandleEventEnded;
            Resources.Changed += EvaluateProgression;

            if (settings.CoreModule == null)
                throw new ArgumentException("CoreModule 필요", nameof(settings));
            Grid.TryPlace(settings.CoreModule, UnityEngine.Vector3Int.zero, 0, out var core);
            Core = core;
            Connectivity.Root = core;
            Connectivity.Recalculate();
            RefreshCapacities();

            if (settings.Residents != null)
            {
                Func<float> residentRandom = settings.ResidentRandom01;
                if (residentRandom == null)
                {
                    var rng = new Random();
                    residentRandom = () => (float)rng.NextDouble();
                }
                Residents = new ResidentRoster(settings.Residents, residentRandom)
                {
                    Grid = Grid,
                    Connectivity = Connectivity,
                    Housing = EffectiveHousing,
                    Strength = ModuleStrength,
                    Effects = Effects,
                };
                Population.PopulationChanged += (delta, reason) => Residents.Update(Resources.Population, reason);
                Residents.Update(Resources.Population);
                ApplyResidentEffects();
            }
            EvaluateProgression();
        }

        /// <summary>Phase 10: 명단의 특성 효과를 각 시스템에 반영 (다음 계산부터).</summary>
        private void ApplyResidentEffects()
        {
            if (Residents == null)
                return;
            Effects.TraitRepairMultiplier = 1f - Residents.Ability(ResidentTrait.Technician);
            Effects.TraitDecayMultiplier = 1f - Residents.Ability(ResidentTrait.Mechanic);
            Effects.TraitFoodProductionMultiplier = 1f + Residents.Ability(ResidentTrait.Gardener);
            Effects.TraitResearchSpeedMultiplier = 1f + Residents.Ability(ResidentTrait.Scientist);
            Resources.SetResidentConsumptionMultiplier(ResourceType.Food, Residents.ConsumptionMultiplier(ResourceType.Food));
            Resources.SetResidentConsumptionMultiplier(ResourceType.Water, Residents.ConsumptionMultiplier(ResourceType.Water));
        }

        // ---------------- 틱 ----------------

        /// <summary>Phase 9 튜토리얼 (시작하지 않았으면 null).</summary>
        public TutorialRunner Tutorial { get; private set; }

        /// <summary>튜토리얼 시작 (무작위 이벤트 정지). 세이브 복원 전에 부르면 복원이 진행 단계를 덮어쓴다.</summary>
        public TutorialRunner StartTutorial(TutorialData data)
        {
            Tutorial = new TutorialRunner(this, data);
            return Tutorial;
        }

        public void Tick(float dt)
        {
            // Phase 9: 첫 밤 대비 단계가 끝나기 전 해질녘 직전이면 시간이 흐르지 않는다 (건설·안내만)
            if (Tutorial != null && dt > 0f && Tutorial.UpdateHold())
            {
                Tutorial.Tick(dt);
                EvaluateProgression();
                return;
            }
            Damage.Tick(dt);
            Durability.Tick(dt); // 내구도 0 → 파괴
            CollectActiveModules();
            Resources.SetExternalDrain(ResourceType.Oxygen, Damage.OxygenLeakPerSecond);
            Resources.SolarMultiplier = DayNight.SolarMultiplier(ElapsedSeconds); // 이번 틱 시작 시점의 낮/밤
            Resources.ExtraPowerDemand = Research.RunningPowerDemand; // 진행 중인 연구 (Phase 6)
            Resources.Tick(_activeModules, _productionMultipliers, _consumptionMultipliers, dt);
            Research.Tick(dt, Resources.PowerEfficiency * Effects.TraitResearchSpeedMultiplier); // 이번 틱 전력 효율만큼 진행 (Phase 10 과학자 배율 포함)
            Cargo.Tick(Grid, Resources, ModuleStrength, dt); // 8-5 화물선 (가동률만큼)
            Automation.Tick(dt); // 자동화 연구: 기준값 미만 모듈 정비·재건축
            RefreshNeeds(); // 4-9: 이번 틱 전력 효율·인구 기준 요구 충족 → 만족도 상한
            Population.Tick(dt);
            if (Residents != null)
            {
                Residents.Update(Resources.Population); // 집 검사·환경·효과 (인원 변화는 PopulationChanged에서 사유와 함께)
                ApplyResidentEffects();
            }
            Events.WarningLead = Effects.EarlyWarningSeconds;
            Events.Tick(dt, _eventPool);
            ElapsedSeconds += dt;
            Tutorial?.Tick(dt);
            EvaluateProgression();
            EvaluateFailure(dt);
        }

        /// <summary>세이브 복원: 경과 시간 (낮/밤 위상·플레이 시간).</summary>
        internal void RestoreElapsed(float seconds)
        {
            ElapsedSeconds = Math.Max(0f, seconds);
        }

        /// <summary>4-10 추가 실패 조건 (산소 고갈·만족도 0·코어 주변 붕괴 지속).</summary>
        private void EvaluateFailure(float dt)
        {
            Grid.GetNeighborModules(Core, _coreNeighbors);
            int down = 0;
            foreach (var n in _coreNeighbors)
            {
                if (Damage.TryGetInfo(n, out var info) && !info.IsRepairing)
                    down++;
            }
            var reason = Failure.Tick(dt, Resources.IsDepleted(ResourceType.Oxygen), Population.Satisfaction, _coreNeighbors.Count, down);
            if (reason != GameOverReason.None)
                Session.Fail(reason);
        }

        // ---------------- 명령 ----------------

        /// <summary>공간 규칙 → 등급 해금·설치 제한 → 비용 순으로 판정.</summary>
        public PlacementResult EvaluatePlacement(ModuleData data, UnityEngine.Vector3Int origin, int rotation)
        {
            var result = PlacementRules.Evaluate(Grid, data, origin, rotation);
            if (result == PlacementResult.Valid)
                result = CheckBuildable(data);
            if (result == PlacementResult.Valid && !Resources.CanAfford(GetBuildCost(data)))
                return PlacementResult.InsufficientResources;
            return result;
        }

        /// <summary>실제 건설 비용 (건설·경제 연구 할인 반영).</summary>
        public List<ResourceAmount> GetBuildCost(ModuleData data) => Effects.BuildCost(data);

        /// <summary>배치 미리보기: 새 모듈 자신의 인접 효과와, 이웃에게 새로 생길 효과 설명.</summary>
        public void PreviewAdjacency(ModuleData data, UnityEngine.Vector3Int origin, int rotation,
            List<AppliedAdjacency> self, List<string> neighborLines)
        {
            Adjacency.Preview(Grid, data, origin, rotation, self, neighborLines);
        }

        /// <summary>7-3: 배치 미리보기 (이웃 효과를 모듈 단위로).</summary>
        public void PreviewAdjacency(ModuleData data, UnityEngine.Vector3Int origin, int rotation,
            List<AppliedAdjacency> self, List<NeighborAdjacencyPreview> neighborEffects)
        {
            Adjacency.Preview(Grid, data, origin, rotation, self, neighborEffects);
        }

        /// <summary>위치와 무관한 건설 가능 여부 (해금·최대 설치 수).</summary>
        public PlacementResult CheckBuildable(ModuleData data) => Progression.CheckBuildable(data, Grid);

        public bool CanAfford(ModuleData data) => data != null && Resources.CanAfford(GetBuildCost(data));

        public bool TryPlace(ModuleData data, UnityEngine.Vector3Int origin, int rotation, out ModuleInstance module)
        {
            module = null;
            if (EvaluatePlacement(data, origin, rotation) != PlacementResult.Valid)
                return false;
            if (!Resources.TrySpend(GetBuildCost(data)))
                return false;
            return Grid.TryPlace(data, origin, rotation, out module);
        }

        /// <summary>철거 가능: 철거 가능한 종류이고, 다른 모듈을 받치고 있지 않음 (5-5 받침 규칙).</summary>
        public bool CanRemove(ModuleInstance module)
        {
            return IsRemovableKind(module) && !PlacementRules.SupportsOthers(Grid, module);
        }

        /// <summary>코어·철거 불가 모듈이 아님 (재건축은 같은 자리에 다시 짓으므로 받침 여부와 무관).</summary>
        public bool IsRemovableKind(ModuleInstance module)
        {
            return module != null && module != Core && (module.Data == null || module.Data.Removable);
        }

        /// <summary>위·아래 모듈의 받침이라 철거할 수 없는지.</summary>
        public bool IsSupportingOthers(ModuleInstance module) => PlacementRules.SupportsOthers(Grid, module);

        /// <summary>철거: 건설 비용 × 환급률 × 내구도 비율을 돌려받는다.</summary>
        public bool TryRemove(ModuleInstance module)
        {
            if (!CanRemove(module))
                return false;
            float refundMultiplier = Durability.GetRefundMultiplier(module); // 제거 전에 읽음
            if (!Grid.Remove(module))
                return false;
            if (module.Data != null)
                Resources.RefundBuildCost(module.Data.BuildCost, refundMultiplier);
            return true;
        }

        /// <summary>철거 시 실제 환급액 (미리보기).</summary>
        public List<ResourceAmount> GetRefund(ModuleInstance module)
        {
            var refund = new List<ResourceAmount>();
            if (module?.Data == null)
                return refund;
            float rate = Effects.DemolishRefundRate * Durability.GetRefundMultiplier(module);
            foreach (var a in module.Data.BuildCost)
                refund.Add(new ResourceAmount(a.Type, a.Amount * rate));
            return refund;
        }

        // ---------------- 정비 / 재건축 (4-3) ----------------

        public MaintainResult TryMaintain(ModuleInstance module)
        {
            if (!Durability.TryGetInfo(module, out var info))
                return MaintainResult.NotApplicable;
            if (info.Current >= info.Max - 1e-4f)
                return MaintainResult.AlreadyAtMax;
            if (!Resources.TrySpend(Durability.GetMaintenanceCost(module)))
                return MaintainResult.InsufficientResources;
            Durability.Maintain(module);
            EvaluateProgression(); // UI 갱신 트리거
            return MaintainResult.Done;
        }

        /// <summary>재건축 순비용 = 건설비 − 철거 환급 (자원별, 0 이상) × 유지보수 연구 할인.</summary>
        public List<ResourceAmount> GetRebuildCost(ModuleInstance module)
        {
            var net = new List<ResourceAmount>();
            if (module?.Data == null)
                return net;
            float rate = Effects.DemolishRefundRate * Durability.GetRefundMultiplier(module);
            float discount = Effects.RebuildCostMultiplier;
            foreach (var a in module.Data.BuildCost)
                net.Add(new ResourceAmount(a.Type, Math.Max(0f, a.Amount * (1f - rate)) * discount));
            return net;
        }

        /// <summary>비용을 빼고 재건축할 수 있는지 (가능하면 Done).</summary>
        private RebuildResult CheckRebuild(ModuleInstance module)
        {
            if (!IsRemovableKind(module) || module.Data == null || !Durability.TryGetInfo(module, out _))
                return RebuildResult.NotAllowed;
            var data = module.Data;
            if (!Progression.IsUnlocked(data))
                return RebuildResult.Locked;
            if (data == Progression.LimitedModule && Progression.CountLimited(Grid) - 1 >= Progression.CurrentLimit(Grid))
                return RebuildResult.Locked;
            return RebuildResult.Done;
        }

        /// <summary>비용과 무관하게 재건축이 허용되는 모듈인지 (자동화 계획용).</summary>
        public bool CanRebuildKind(ModuleInstance module) => CheckRebuild(module) == RebuildResult.Done;

        /// <summary>같은 자리에 철거 후 새로 건설 (내구도·최대 내구도 100, 파손 해제). 순비용만 지불.</summary>
        public RebuildResult TryRebuild(ModuleInstance module, out ModuleInstance rebuilt)
        {
            rebuilt = null;
            var check = CheckRebuild(module);
            if (check != RebuildResult.Done)
                return check;
            var data = module.Data;
            if (!Resources.TrySpend(GetRebuildCost(module)))
                return RebuildResult.InsufficientResources;

            var origin = module.Origin;
            int rotation = module.Rotation;
            Grid.Remove(module);
            Grid.TryPlace(data, origin, rotation, out rebuilt); // 같은 셀이므로 항상 성공
            return RebuildResult.Done;
        }

        /// <summary>파괴 (방치 등): 환급 없음. 코어는 파괴되지 않는다.</summary>
        public bool DestroyModule(ModuleInstance module)
        {
            if (module == null || module == Core)
                return false;
            return Grid.Remove(module);
        }

        public RepairResult TryRepair(ModuleInstance module)
        {
            if (!Damage.TryGetInfo(module, out var info))
                return RepairResult.NotDamaged;
            if (info.IsRepairing)
                return RepairResult.AlreadyRepairing;
            if (info.IsQueued)
                return RepairResult.AlreadyQueued;
            if (!UseFreeRepair() && !Resources.TrySpend(Damage.GetRepairCost(module))) // 11-17 ② 무료 수리권 먼저
                return RepairResult.InsufficientResources;
            RefreshRepairCapacity();
            Damage.StartRepair(module);
            return info.IsRepairing ? RepairResult.Started : RepairResult.Queued;
        }

        /// <summary>
        /// 11-17 내부 현장 수리 비용 = 보통 수리 비용 × 현장 비율 (BALANCE 28번). 이미 수리 비용을 낸 모듈(대기 · 수리 중)은 0.
        /// </summary>
        public List<ResourceAmount> GetFieldRepairCost(ModuleInstance module)
        {
            if (!Damage.TryGetInfo(module, out var info) || info.IsQueued || info.IsRepairing || Supply.FreeRepairs > 0)
                return new List<ResourceAmount>();
            var cost = Damage.GetRepairCost(module);
            for (int i = 0; i < cost.Count; i++)
                cost[i] = new ResourceAmount(cost[i].Type, cost[i].Amount * Balance.FieldRepairCostRate);
            return cost;
        }

        /// <summary>11-17 ② 현장 수리 마무리에 무료 수리권을 쓰는지 (아직 수리 비용을 내지 않았고 수리권이 있을 때).</summary>
        public bool FieldRepairUsesFreeRepair(ModuleInstance module)
            => Supply.FreeRepairs > 0 && Damage.TryGetInfo(module, out var info) && !info.IsQueued && !info.IsRepairing;

        /// <summary>11-17 내부 현장 수리 완료: 비용을 내고 수리 슬롯 · 시간 없이 바로 복구 (손상 지점을 다 고친 뒤 부름).</summary>
        public RepairResult TryFieldRepair(ModuleInstance module)
        {
            if (!Damage.TryGetInfo(module, out var info))
                return RepairResult.NotDamaged;
            bool paid = info.IsQueued || info.IsRepairing;
            if (FieldRepairUsesFreeRepair(module))
                UseFreeRepair();
            else if (!paid && !Resources.TrySpend(GetFieldRepairCost(module)))
                return RepairResult.InsufficientResources;
            Damage.CompleteNow(module);
            RefreshRepairCapacity();
            return RepairResult.Completed;
        }

        /// <summary>대기 중인 수리를 맨 앞으로 (4-6).</summary>
        public bool TryPrioritizeRepair(ModuleInstance module) => Damage.Prioritize(module);

        /// <summary>대기 취소: 낸 수리 비용 × 취소 환불률(BALANCE 18번, 50%)을 돌려받는다.</summary>
        public bool TryCancelRepair(ModuleInstance module)
        {
            if (!Damage.CancelQueued(module))
                return false;
            foreach (var a in GetCancelRefund(module))
                Resources.AddStock(a.Type, a.Amount); // 철거 환급률과 별개
            return true;
        }

        /// <summary>대기 취소 시 돌려받을 양 (미리보기).</summary>
        public List<ResourceAmount> GetCancelRefund(ModuleInstance module)
        {
            var refund = Damage.GetRepairCost(module);
            for (int i = 0; i < refund.Count; i++)
                refund[i] = new ResourceAmount(refund[i].Type, refund[i].Amount * Balance.RepairCancelRefundRate);
            return refund;
        }

        /// <summary>동시 수리 슬롯 = 기본(코어) + 활성이고 파손되지 않은 정비 베이의 슬롯 합 (BALANCE 17번).</summary>
        public int CountRepairSlots()
        {
            int slots = Balance.BaseRepairSlots;
            foreach (var module in Grid.Modules)
            {
                if (module.Data == null || module.Data.RepairSlots <= 0)
                    continue;
                if (Connectivity.IsActive(module) && !Damage.IsDamaged(module))
                    slots += module.Data.RepairSlots;
            }
            return slots;
        }

        private void RefreshRepairCapacity() => Damage.RepairCapacity = CountRepairSlots();

        /// <summary>가중치 랜덤 이벤트 즉시 발생 (디버그 F5).</summary>
        public GameEventData TriggerRandomEvent() => Events.TriggerRandom(_eventPool);

        // ---------------- 내부 ----------------

        private void HandleModulePlaced(ModuleInstance module)
        {
            if (module.Data != null && module.Data.Removable)
                Durability.Track(module); // 코어(철거 불가)는 노후화 없음
            Adjacency.Recalculate(Grid);
            if (Connectivity.Root != null)
                Connectivity.Recalculate();
            RefreshCapacities();
            EvaluateProgression();
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            Damage.Forget(module); // 파손 중 철거된 경우
            Durability.Forget(module);
            Cargo.Forget(module);
            Supply.Relocate(module, Core); // 그 방에 있던 보급 상자는 코어로
            Adjacency.Recalculate(Grid);
            Connectivity.Recalculate();
            RefreshCapacities();
            EvaluateProgression();
            if (_plannedHits > 0 && _plannedTargets.Contains(module))
            {
                RefillPlannedTargets(); // 예정 대상이 철거·재건축되면 다른 모듈로 다시 뽑음
                MeteorPlanChanged?.Invoke();
            }
        }

        /// <summary>
        /// 4-7 연쇄 파손: 면이 맞닿은 정상 모듈(코어 제외) 중 1곳을 무작위로 파손시키고 내구도를 깎는다.
        /// 번진 파손도 60초 방치되면 다시 번진다. 대상이 없으면 조용히 끝.
        /// </summary>
        private void HandleSpreadDue(ModuleInstance source)
        {
            if (!Grid.TryGetModule(source.Origin, out var placed) || placed != source)
                return; // 같은 틱에 파괴됨
            Grid.GetNeighborModules(source, _spreadCandidates);
            for (int i = _spreadCandidates.Count - 1; i >= 0; i--)
            {
                var n = _spreadCandidates[i];
                if (n == Core || Damage.IsDamaged(n))
                    _spreadCandidates.RemoveAt(i);
            }
            if (_spreadCandidates.Count == 0)
                return;
            int index = Math.Min(_spreadCandidates.Count - 1, (int)(_random01() * _spreadCandidates.Count));
            var target = _spreadCandidates[index];
            Damage.Damage(target);
            Durability.ApplyImpact(target, Balance.SpreadDurabilityDamage);
            Session.RecordSpread();
            DamageSpread?.Invoke(source, target);
            string from = source.Data != null ? source.Data.DisplayName : source.ToString();
            string to = target.Data != null ? target.Data.DisplayName : target.ToString();
            Report($"파손 확산! {from} → {to}  ·  방치된 파손은 번집니다 (수리 또는 대기열 등록으로 멈춤)", false);
        }

        private readonly List<ModuleInstance> _spreadCandidates = new List<ModuleInstance>();
        private readonly HashSet<ModuleInstance> _immuneHits = new HashSet<ModuleInstance>();
        private readonly List<ModuleInstance> _ricochetCandidates = new List<ModuleInstance>();

        /// <summary>
        /// 운석 1발의 결과 (4-8). 맞을 모듈을 돌려주고, 격추·빗겨냄으로 피해가 없으면 null.
        /// 실드가 빗겨내면 튕김 확률로 그 실드 범위 밖 외곽 모듈(노출 가중, 이번 운석 무리에서 이미 맞을 곳 제외)을 다시 판정한다.
        /// 튕긴 운석도 포탑 격추는 받지만, 다른 실드에 또 막히면 우주로 (튕김 1회).
        /// </summary>
        private ModuleInstance ResolveMeteor(ModuleInstance target, bool canRicochet, ref int intercepted, ref int deflected, ref int ricochets,
            MeteorFlight flight)
        {
            float turret = Defense.GetInterceptChance(Grid, target);
            if (turret > 0f && _random01() < turret)
            {
                intercepted++;
                flight.InterceptedAt = target;
                return null;
            }
            float shield = Defense.GetShieldBlockChance(Grid, target, out var shieldModule);
            if (shield <= 0f || _random01() >= shield)
                return target;

            deflected++;
            ShieldDeflected?.Invoke(shieldModule);
            if (flight.DeflectedBy == null)
                flight.DeflectedBy = shieldModule;
            if (!canRicochet || _random01() >= Effects.RicochetChance)
                return null; // 우주로

            Damage.FindMeteorCandidates(Grid, Core, _ricochetCandidates);
            for (int i = _ricochetCandidates.Count - 1; i >= 0; i--)
            {
                var c = _ricochetCandidates[i];
                if (Defense.IsInShieldRange(shieldModule, c) || _meteorTargetsCopy.Contains(c) || _meteorCandidates.Contains(c))
                    _ricochetCandidates.RemoveAt(i);
            }
            var next = Damage.PickWeighted(Grid, _ricochetCandidates, _random01);
            if (next == null)
                return null; // 튕길 곳 없음 → 우주로
            flight.RicochetTarget = next;
            var hit = ResolveMeteor(next, false, ref intercepted, ref deflected, ref ricochets, flight);
            if (hit != null)
                ricochets++;
            return hit;
        }

        /// <summary>활성 모듈의 실제 수용 인구 (인접 가감 포함, 비활성 0). 4-9 주민 배분용.</summary>
        private float EffectiveHousing(ModuleInstance module)
        {
            if (module.Data == null || module.Data.HousingCapacity <= 0 || !Connectivity.IsActive(module))
                return 0f;
            int housing = Effects.Housing(module.Data); // 거주 연구 +N
            return housing + Math.Max(-housing, Adjacency.GetHousingBonus(module));
        }

        private void RefreshNeeds()
        {
            _activeNeeds.Clear();
            for (int i = 0; i <= Progression.GradeIndex; i++)
            {
                foreach (var need in Progression.GetGrade(i).NewNeeds)
                {
                    if (need != ResidentNeed.None && !_activeNeeds.Contains(need))
                        _activeNeeds.Add(need);
                }
            }
            Needs.Evaluate(Grid, Resources.Population, _activeNeeds);
            // Phase 10: 주민 특성 보정은 요구·연구를 반영한 상한에 더함 (0~100)
            float mood = Residents != null ? Residents.MoodTotal : 0f;
            Population.SatisfactionCap = Math.Max(0f, Math.Min(PopulationSimulation.MaxSatisfaction, Needs.SatisfactionCap + mood));
            Population.GrowthIntervalMultiplier = GrowthIntervalMultiplier();
        }

        /// <summary>
        /// 8-4 회전 링: 가동 중인(활성·정상) 모듈 중 가장 강한 인구 증가 간격 배율 (중첩 없음).
        /// 배율 = 1 − (1 − 값) × 가동률. 없으면 1.
        /// </summary>
        public float GrowthIntervalMultiplier()
        {
            float best = 1f;
            foreach (var m in Grid.Modules)
            {
                var data = m.Data;
                if (data == null || data.GrowthIntervalMultiplier >= 1f)
                    continue;
                float s = ModuleStrength(m);
                if (s <= 0f)
                    continue;
                float value = 1f - (1f - data.GrowthIntervalMultiplier) * s;
                if (value < best)
                    best = value;
            }
            return best;
        }

        /// <summary>
        /// 방어·서비스 모듈 가동률 (4-8, 4-9): 비활성·파손이면 0, 아니면 전력 효율(전력을 쓰는 경우) × 내구도 효율.
        /// </summary>
        private float ModuleStrength(ModuleInstance defender)
        {
            if (defender.Data == null || !Connectivity.IsActive(defender) || Damage.IsDamaged(defender))
                return 0f;
            float power = 1f;
            foreach (var c in defender.Data.Consumption)
            {
                if (c.Type == ResourceType.Power && c.Amount > 0f)
                    power = Resources.PowerEfficiency;
            }
            return power * Durability.GetEfficiency(defender);
        }

        /// <summary>
        /// 7-1: 모듈 자체 효율 (선택 테두리·패널 색). 비활성 0.
        /// 방어·서비스 모듈은 가동률(파손 시 0), 나머지는 자원 틱과 같은 최종 생산 배율
        /// = 전력 효율(전력을 쓰는 경우) × 파손 × 내구도 × 인접 × 생산 연구.
        /// 낮/밤·태양 폭풍처럼 정거장 전체에 걸리는 배율은 모듈 탓이 아니므로 제외한다.
        /// </summary>
        public float GetModuleEfficiency(ModuleInstance module)
        {
            if (module == null || module.Data == null || !Connectivity.IsActive(module))
                return 0f;
            if (module.Data.IsDefense || module.Data.IsService)
                return ModuleStrength(module);
            float power = 1f;
            foreach (var c in module.Data.Consumption)
            {
                if (c.Type == ResourceType.Power && c.Amount > 0f)
                    power = Resources.PowerEfficiency;
            }
            return power * Damage.GetProductionMultiplier(module) * Durability.GetEfficiency(module)
                   * Adjacency.GetProductionMultiplier(module) * Effects.ProductionMultiplier(module.Data);
        }

        private void HandleDestroyed(ModuleInstance module)
        {
            Session.RecordDestroyed();
            DestroyModule(module);
        }

        private void RefreshCapacities()
        {
            CollectActiveModules();
            Resources.RefreshCapacities(_activeModules);
        }

        private void CollectActiveModules()
        {
            _activeModules.Clear();
            _productionMultipliers.Clear();
            _consumptionMultipliers.Clear();
            int extraHousing = 0;
            int labSlots = 0;
            foreach (var module in Grid.Modules)
            {
                if (module.Data == null || !Connectivity.IsActive(module))
                    continue;
                _activeModules.Add(module.Data);
                // 파손 배율 × 내구도 효율 × 인접 효과 × 생산 연구 (BALANCE 1번: 곱셈)
                _productionMultipliers.Add(Damage.GetProductionMultiplier(module) * Durability.GetEfficiency(module)
                                           * Adjacency.GetProductionMultiplier(module) * Effects.ProductionMultiplier(module.Data));
                if (module.Data.ResearchSlots > 0 && !Damage.IsDamaged(module))
                    labSlots += module.Data.ResearchSlots; // 연결되고 파손되지 않은 연구소만
                _consumptionMultipliers.Add(Adjacency.GetConsumptionMultiplier(module));
                // 인접 수용 인구 가감 (모듈 자체 수용 인구 아래로는 내려가지 않음)
                extraHousing += Math.Max(-module.Data.HousingCapacity, Adjacency.GetHousingBonus(module));
            }
            Resources.ExtraHousing = extraHousing;
            Research.LabSlots = labSlots;
            RefreshRepairCapacity(); // 연결·파손 상태 반영 (4-6)
        }

        // ---------------- 연구 (Phase 6) ----------------

        /// <summary>다음 레벨 연구를 시작할 수 있는지 (등급·인구·연구소·자원).</summary>
        /// <param name="usePoint">11-17 ②: 연구 포인트 1개로 시작 비용 할인 (포인트가 없으면 무시).</param>
        public ResearchStartResult CanStartResearch(ResearchCategoryData category, bool usePoint = false)
            => Research.CanStart(category, Progression.GradeIndex, Resources.Population,
                cost => Resources.CanAfford(DiscountResearchCost(cost, usePoint)));

        /// <summary>11-17 ②: 다음 레벨 연구 시작 비용 (연구 포인트를 쓰면 할인된 값). 최고 레벨이면 빈 목록.</summary>
        public List<ResourceAmount> GetResearchStartCost(ResearchCategoryData category, bool usePoint)
        {
            var level = category != null ? category.GetLevel(Research.GetLevel(category) + 1) : null;
            return level != null ? DiscountResearchCost(level.StartCost, usePoint) : new List<ResourceAmount>();
        }

        private List<ResourceAmount> DiscountResearchCost(IReadOnlyList<ResourceAmount> cost, bool usePoint)
        {
            float k = usePoint && Supply.ResearchPoints > 0 ? 1f - Balance.ResearchPointDiscount : 1f;
            var result = new List<ResourceAmount>(cost.Count);
            foreach (var a in cost)
                result.Add(new ResourceAmount(a.Type, a.Amount * k));
            return result;
        }

        /// <summary>시작 비용을 내고 다음 레벨 연구 시작 (환급 없음). usePoint = 연구 포인트 1개로 할인 (11-17 ②).</summary>
        public ResearchStartResult TryStartResearch(ResearchCategoryData category, bool usePoint = false)
        {
            CollectActiveModules(); // 방금 지은 연구소도 슬롯으로 인정
            usePoint &= Supply.ResearchPoints > 0;
            var result = CanStartResearch(category, usePoint);
            if (result != ResearchStartResult.Ok)
                return result;
            if (!Resources.TrySpend(GetResearchStartCost(category, usePoint)))
                return ResearchStartResult.InsufficientResources;
            if (usePoint)
                Supply.SetCounts(Supply.ResearchPoints - 1, Supply.FreeRepairs);
            Research.Begin(category);
            EvaluateProgression(); // UI 갱신 트리거
            return ResearchStartResult.Ok;
        }

        /// <summary>진행 중인 연구 취소 (시작 비용·진행률 모두 잃음).</summary>
        public bool CancelResearch(ResearchCategoryData category)
        {
            bool done = Research.Cancel(category);
            if (done)
                EvaluateProgression();
            return done;
        }

        /// <summary>연구 효과가 바뀌면 캐시된 값(인접 효과, 저장 한도·수용 인구·배터리)을 다시 계산.</summary>
        private void HandleResearchEffectsChanged()
        {
            Adjacency.Recalculate(Grid);
            if (Core != null)
                RefreshCapacities();
        }

        private void EvaluateProgression()
        {
            if (Progression == null || Session == null)
                return; // 생성자 도중
            int population = Resources.Population;
            Progression.Evaluate(population, StationProgression.CountGradeModules(Grid)); // 8-6: 장갑 격벽 제외
            Session.ObservePopulation(population);
            ProgressionEvaluated?.Invoke();
        }

        // ---------------- 이벤트 효과 (3-3) ----------------

        private void HandleEventStarted(GameEventData data)
        {
            Session.RecordEvent();
            switch (data)
            {
                case MeteorEventData _:
                    ApplyMeteor();
                    break;
                case OxygenLeakEventData leak:
                    float ratio = Math.Min(leak.MaxLossRatio, leak.StockLossRatio * EventIntensity); // 8-6 상한 (노멀 70%)
                    float lost = Resources.RemoveStock(ResourceType.Oxygen, Resources.GetStock(ResourceType.Oxygen) * ratio);
                    Report($"산소 -{lost:0.#} ({ratio * 100f:0}%)", false);
                    break;
                case SolarStormEventData storm:
                    Resources.PowerSupplyMultiplier = storm.PowerSupplyMultiplier;
                    Report($"전력 생산 -{(1f - storm.PowerSupplyMultiplier) * 100f:0}% ({ActiveDuration(storm):0}초)", false);
                    break;
                case SupplyShipEventData supply:
                    ApplySupply(supply);
                    break;
            }
        }

        private void HandleEventEnded(ActiveEvent active)
        {
            if (active.Data is SolarStormEventData)
                Resources.PowerSupplyMultiplier = 1f;
        }

        // ---------------- 조기 경보 (Phase 6 방어 연구) ----------------

        private readonly List<ModuleInstance> _plannedTargets = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _planBuffer = new List<ModuleInstance>();
        private int _plannedHits;

        /// <summary>경보 중인 운석이 맞을 예정인 모듈 (대상 표시 연구가 있을 때만, 없으면 비어 있음).</summary>
        public IReadOnlyList<ModuleInstance> PlannedMeteorTargets => _plannedTargets;
        /// <summary>예정 대상이 바뀜 (경보 시작·해제, 대상 철거로 다시 뽑음).</summary>
        public event Action MeteorPlanChanged;

        private void HandleUpcomingChanged(GameEventData upcoming)
        {
            bool had = _plannedHits > 0;
            _plannedTargets.Clear();
            _plannedHits = 0;
            if (upcoming is MeteorEventData && Effects.MeteorTargetPreview)
            {
                _plannedHits = RollMeteorHits();
                Damage.PickMeteorTargets(Grid, Core, _plannedHits, _random01, _plannedTargets);
                Defense.RedirectToDecoys(Grid, _plannedTargets, _random01); // 8-5 장갑 격벽 (예정 표시에도 반영)
            }
            if (had || _plannedHits > 0)
                MeteorPlanChanged?.Invoke();
        }

        private int RollMeteorHits()
        {
            var grade = Progression.Current;
            int span = grade.MeteorHitsMax - grade.MeteorHitsMin + 1;
            return grade.MeteorHitsMin + Math.Min(span - 1, (int)(_random01() * span));
        }

        /// <summary>예정 대상이 사라지거나(철거·파괴) 맞을 수 없게 되면(파손·내부로 묻힘) 남은 후보에서 다시 뽑아 개수를 채운다.</summary>
        private void RefillPlannedTargets()
        {
            for (int i = _plannedTargets.Count - 1; i >= 0; i--)
            {
                var m = _plannedTargets[i];
                if (!Grid.TryGetModule(m.Origin, out var placed) || placed != m || Damage.IsDamaged(m) || !DamageSystem.IsExterior(Grid, m))
                    _plannedTargets.RemoveAt(i);
            }
            if (_plannedTargets.Count >= _plannedHits)
                return;
            Damage.FindMeteorCandidates(Grid, Core, _planBuffer);
            _planBuffer.RemoveAll(_plannedTargets.Contains);
            while (_plannedTargets.Count < _plannedHits && _planBuffer.Count > 0)
            {
                var pick = Damage.PickWeighted(Grid, _planBuffer, _random01);
                if (pick == null)
                    break;
                _plannedTargets.Add(pick);
                _planBuffer.Remove(pick);
            }
        }

        /// <summary>세이브 복원: 경보 중이던 운석의 예정 대상.</summary>
        internal void RestoreMeteorPlan(int hits, IEnumerable<ModuleInstance> targets)
        {
            _plannedTargets.Clear();
            _plannedHits = Math.Max(0, hits);
            if (_plannedHits == 0)
                return;
            foreach (var t in targets)
                if (t != null && !_plannedTargets.Contains(t))
                    _plannedTargets.Add(t);
            RefillPlannedTargets();
        }

        public int PlannedMeteorHits => _plannedHits;

        /// <summary>
        /// 4-1: 등급별 개수만큼 외곽 모듈을 노출 면 수로 가중해 서로 다르게 선택 (분산 타격).
        /// 조기 경보로 대상을 미리 정해 두었으면 그 대상 (사라진 대상은 다시 뽑아 채움).
        /// </summary>
        private void ApplyMeteor()
        {
            if (_plannedHits > 0)
            {
                RefillPlannedTargets();
                _meteorCandidates.Clear();
                _meteorCandidates.AddRange(_plannedTargets);
                _plannedTargets.Clear();
                _plannedHits = 0;
                MeteorPlanChanged?.Invoke();
            }
            else
            {
                Damage.PickMeteorTargets(Grid, Core, RollMeteorHits(), _random01, _meteorCandidates);
                Defense.RedirectToDecoys(Grid, _meteorCandidates, _random01); // 8-5 장갑 격벽 끌어오기
            }
            if (_meteorCandidates.Count == 0)
            {
                Report("운석이 정거장을 빗나갔습니다", true);
                return;
            }

            // 4-8: 포탑 격추(완전 제거) → 실드 빗겨냄(일부는 범위 밖 모듈로 튕김). 피해 적용 전에 모두 판정
            _meteorTargetsCopy.Clear();
            int intercepted = 0, deflected = 0, ricochets = 0;
            foreach (var target in _meteorCandidates)
            {
                var flight = new MeteorFlight { Target = target };
                var hit = ResolveMeteor(target, true, ref intercepted, ref deflected, ref ricochets, flight);
                flight.Hit = hit;
                // Phase 6 방어 연구: 명중해도 일정 확률로 파손 면역 (확률이 0이면 난수를 쓰지 않아 기존 결과 유지)
                if (hit != null && Effects.HitImmunityChance > 0f && _random01() < Effects.HitImmunityChance)
                {
                    flight.Immune = true;
                    _immuneHits.Add(hit);
                }
                MeteorResolved?.Invoke(flight);
                if (hit != null)
                    _meteorTargetsCopy.Add(hit); // 내구도 0으로 파괴되면 목록이 바뀔 수 있어 복사
            }
            Session.RecordIntercepted(intercepted);
            Session.RecordShieldBlocked(deflected);
            Session.RecordRicochet(ricochets);
            string interceptText = (intercepted > 0 ? $"포탑 격추 {intercepted} · " : "")
                                   + (deflected > 0 ? $"실드 빗겨냄 {deflected}" + (ricochets > 0 ? $"(튕겨서 {ricochets}개 명중)" : "") + " · " : "");
            if (_meteorTargetsCopy.Count == 0)
            {
                Report($"운석 {_meteorCandidates.Count}개 접근 · {interceptText}피해 없음", true);
                return;
            }

            var sb = new StringBuilder();
            int immune = 0;
            foreach (var target in _meteorTargetsCopy)
            {
                bool isImmune = _immuneHits.Contains(target);
                if (!isImmune)
                    Damage.Damage(target);
                else
                    immune++;
                Durability.ApplyImpact(target, Balance.MeteorDurabilityDamage); // 4-3: 내구도도 깎음
                if (isImmune)
                    continue;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(target.Data != null ? target.Data.DisplayName : target.ToString());
            }
            _immuneHits.Clear();
            if (immune > 0)
                interceptText += $"방어 연구로 파손 면함 {immune} · ";
            if (sb.Length == 0)
            {
                Report($"운석 {_meteorCandidates.Count}개 충돌 · {interceptText}파손 없음", true);
                return;
            }
            string head = (_meteorCandidates.Count > 1 ? $"운석 {_meteorCandidates.Count}개 충돌! " : "") + interceptText + "파손: ";
            string spread = Balance.SpreadAfterSeconds > 0f ? $", {Balance.SpreadAfterSeconds:0}초 방치 시 이웃으로 확산" : "";
            Report($"{head}{sb}  ·  {Balance.DestroyAfterSeconds:0}초 안에 수리 (선택 후 R){spread}", false);
        }

        private float ActiveDuration(GameEventData data)
        {
            foreach (var a in Events.ActiveEvents)
            {
                if (a.Data == data)
                    return a.Duration;
            }
            return data.Duration;
        }

        private void ApplySupply(SupplyShipEventData supply)
        {
            var sb = new StringBuilder();
            bool clamped = false;
            float intensity = EventIntensity;
            foreach (var reward in supply.Rewards)
            {
                float amount = reward.Amount * intensity;
                float added = Resources.AddStock(reward.Type, amount);
                if (added < amount - 1e-3f)
                    clamped = true;
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(reward.Type.DisplayName()).Append(" +").Append(added.ToString("0.#"));
            }
            if (clamped)
                sb.Append(" (저장 한도 초과분 제외)");
            // 11-17 ②: 내부 화물 터미널 · 창고에 보급 상자 (특별 상자 1개 확정)
            int crates = Supply.Spawn(Grid, Connectivity.IsActive, Core, Balance, intensity, () => (float)_crateRandom.NextDouble());
            if (crates > 0)
                sb.Append($" · 내부에 보급 상자 {crates}개");
            Report(sb.ToString(), true);
        }

        /// <summary>11-17 ② 보급 상자 줍기: 보상 적용 후 상자 제거. text = 받은 것 (알림용).</summary>
        public bool TryPickupCrate(int id, out string text)
        {
            text = null;
            var crate = Supply.Find(id);
            if (crate == null)
                return false;
            switch (crate.Bonus)
            {
                case CrateBonus.ResearchPoints:
                    Supply.SetCounts(Supply.ResearchPoints + Balance.CrateResearchPoints, Supply.FreeRepairs);
                    text = $"연구 포인트 +{Balance.CrateResearchPoints}  (보유 {Supply.ResearchPoints} · 연구 시작 비용 할인)";
                    break;
                case CrateBonus.FreeRepair:
                    Supply.SetCounts(Supply.ResearchPoints, Supply.FreeRepairs + 1);
                    text = $"무료 수리권 +1  (보유 {Supply.FreeRepairs} · 다음 수리에 자동 사용)";
                    break;
                case CrateBonus.Satisfaction:
                    float before = Population.Satisfaction;
                    Population.SetSatisfaction(Math.Min(PopulationSimulation.MaxSatisfaction, before + Balance.CrateSatisfaction));
                    text = $"만족도 +{Population.Satisfaction - before:0.#}  (보급품 속 간식과 생필품)";
                    break;
                default:
                    float added = Resources.AddStock(crate.Resource, crate.Amount);
                    text = $"{crate.Resource.DisplayName()} +{added:0.#}" + (added < crate.Amount - 1e-3f ? "  (저장 한도 초과분 제외)" : "");
                    break;
            }
            Supply.Remove(crate);
            return true;
        }

        // 11-17 ③ 주민 요청을 들어준 주민 → 기분 보너스가 끝나는 게임 시각 (저장하지 않음 — 겉모습 · 반응만)
        private readonly Dictionary<int, float> _cheerUntil = new Dictionary<int, float>();

        /// <summary>
        /// 11-17 ③ 주민 요청을 들어줌 (BALANCE 30번): 정거장 만족도 + 그 주민 기분 보너스, 가끔 연구 포인트.
        /// roll01 = 연구 포인트 판정 난수 (없으면 상자 난수). text = 받은 것 (알림용).
        /// </summary>
        public bool CompleteResidentRequest(Resident resident, out string text, float? roll01 = null)
        {
            text = null;
            if (resident == null || Residents == null)
                return false;
            bool listed = false;
            foreach (var r in Residents.Residents)
                listed |= r == resident;
            if (!listed)
                return false;
            float before = Population.Satisfaction;
            Population.SetSatisfaction(Math.Min(PopulationSimulation.MaxSatisfaction, before + Balance.RequestSatisfaction));
            _cheerUntil[resident.Id] = ElapsedSeconds + Balance.RequestCheerSeconds;
            text = $"만족도 +{Population.Satisfaction - before:0.#}";
            float roll = roll01 ?? (float)_crateRandom.NextDouble();
            if (roll < Balance.RequestResearchPointChance)
            {
                Supply.SetCounts(Supply.ResearchPoints + 1, Supply.FreeRepairs);
                text += $"  ·  연구 포인트 +1 (보유 {Supply.ResearchPoints})";
            }
            return true;
        }

        /// <summary>11-17 ③ 요청을 들어준 주민의 기분 보너스 (끝났으면 0).</summary>
        public float CheerOf(Resident resident) =>
            resident != null && _cheerUntil.TryGetValue(resident.Id, out var until) && ElapsedSeconds < until ? Balance.RequestCheer : 0f;

        /// <summary>11-17 ② 무료 수리권 1장을 씀 (있으면 true).</summary>
        private bool UseFreeRepair()
        {
            if (Supply.FreeRepairs <= 0)
                return false;
            Supply.SetCounts(Supply.ResearchPoints, Supply.FreeRepairs - 1);
            Report($"무료 수리권 사용 · 남은 {Supply.FreeRepairs}", true);
            return true;
        }

        private void Report(string message, bool positive)
        {
            EffectReported?.Invoke(message, positive);
        }
    }
}
