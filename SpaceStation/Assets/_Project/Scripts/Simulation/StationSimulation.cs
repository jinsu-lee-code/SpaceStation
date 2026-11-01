using System;
using System.Collections.Generic;
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
        /// <summary>12-3 합성 레시피 (null이면 합성 시스템 없음 = 이전과 같은 결과).</summary>
        public IReadOnlyList<RecipeData> Recipes;
        /// <summary>12-3c 등급 위기(유성우 등) 전용 난수 [0, 1). null이면 시스템 난수 (이벤트 난수 순서와 분리, 봇은 시드 고정).</summary>
        public Func<float> HazardRandom01;
        /// <summary>12-3c 국가급 상시 정책 · 사회 위기 (null이면 시스템 없음 = 이전과 같은 결과).</summary>
        public PolicyConfig Policies;
        /// <summary>12-3c 지구급 방문 세력 + 평판 (null이면 시스템 없음 = 이전과 같은 결과).</summary>
        public ContactConfig Contact;
        /// <summary>12-4 연구 확장 (레시피 그림자 · 해금 연구 · 연계 연구). null이면 없음 = 레시피는 자동 해금만 (12-3과 같음).</summary>
        public ResearchExpansionConfig ResearchExpansion;
        /// <summary>
        /// 보급 상자 내용 · 내부 결과 난수의 시드. null이면 매 판 다른 시스템 난수 (게임). 밸런스 봇은 시드에서 섞어 넣는다 —
        /// 2026-10-17: 봇이 상자를 처음부터 줍게 된 뒤(13-7) 같은 시드도 약 10분부터 결과가 갈라졌음.
        /// </summary>
        public int? CrateSeed;
    }

    /// <summary>
    /// 정거장 한 판의 전체 시뮬레이션 (MonoBehaviour 없음). 게임(SimulationHost)과 측정 도구(밸런스 봇)가 공유한다.
    /// 12-0b (사용자 결정 2026-10-12, C-6): 시스템을 만들어 서로 잇고 틱 순서(<see cref="TickSteps"/>)를 돌리는 얼개만 맡는다.
    /// 실제 처리는 기능별 클래스 — 건설(<see cref="StationConstruction"/>) · 수리(<see cref="StationRepairs"/>) · 운석(<see cref="StationMeteors"/>) ·
    /// 이벤트 효과(<see cref="StationEventEffects"/>) · 모듈 파생값(<see cref="StationModuleStats"/>) · 연구 명령(<see cref="StationResearchCommands"/>) ·
    /// 내부 결과(<see cref="StationInteriorOutcomes"/>). 공개 함수는 이전과 같은 이름으로 위임한다 (UI · 봇 · 테스트는 그대로).
    /// 새 시스템(Phase 12 합성 · 위기 등)은 <see cref="StationSystem"/>을 상속해 <see cref="AddSystem"/>로 끼운다.
    /// 틱 순서: 파손 → 내구도 → 자원 → 사고 누출 → 연구 → 화물 → 자동화 → 요구 → 인구 → 이벤트 → 시간 → 튜토리얼 → 등급 → 실패.
    /// </summary>
    public sealed class StationSimulation
    {
        private readonly List<GameEventData> _eventPool;
        private readonly Func<float> _random01;
        // 상자 내용 난수는 이벤트 난수와 따로 (밸런스 봇의 시드 결과가 바뀌지 않게). 고정 시드면 매 판 처음 상자들이 늘 같았음(물 4개)
        // → 게임은 시스템 난수, 봇은 시드를 섞은 값(CrateSeed)
        private readonly Random _crateRandom;
        private readonly List<ModuleInstance> _coreNeighbors = new List<ModuleInstance>();
        private readonly List<TickStep> _tickSteps = new List<TickStep>();
        private readonly List<StationSystem> _systems = new List<StationSystem>();
        private bool _tutorialTick;

        private readonly StationModuleStats _stats;
        private readonly StationConstruction _construction;
        private readonly StationRepairs _repairs;
        private readonly StationMeteors _meteors;
        private readonly StationEventEffects _eventEffects;
        private readonly StationResearchCommands _researchCommands;
        private readonly StationInteriorOutcomes _interior;

        /// <summary>(요약 메시지, 긍정 여부). 이벤트 효과 알림.</summary>
        public event Action<string, bool> EffectReported;
        /// <summary>파손이 번짐 (원본, 대상). 4-7</summary>
        public event Action<ModuleInstance, ModuleInstance> DamageSpread
        {
            add => _eventEffects.DamageSpread += value;
            remove => _eventEffects.DamageSpread -= value;
        }
        /// <summary>등급·승패 재판정 직후 (UI 갱신용).</summary>
        public event Action ProgressionEvaluated;
        /// <summary>실드가 운석을 빗겨냄 (빗겨낸 실드 모듈). 5-5 실드 연출용.</summary>
        public event Action<ModuleInstance> ShieldDeflected
        {
            add => _meteors.ShieldDeflected += value;
            remove => _meteors.ShieldDeflected -= value;
        }
        /// <summary>운석 한 발의 경로 (5-7 연출). 피해 적용 전에 발생한다.</summary>
        public event Action<MeteorFlight> MeteorResolved
        {
            add => _meteors.MeteorResolved += value;
            remove => _meteors.MeteorResolved -= value;
        }
        /// <summary>예정 대상이 바뀜 (경보 시작·해제, 대상 철거로 다시 뽑음).</summary>
        public event Action MeteorPlanChanged
        {
            add => _meteors.MeteorPlanChanged += value;
            remove => _meteors.MeteorPlanChanged -= value;
        }

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
        /// <summary>11-17 ④ 놓친 내부 사고의 짧은 손실 (누출 · 차단기).</summary>
        public IncidentEffects Incidents { get; } = new IncidentEffects();
        public NeedsSystem Needs { get; }
        public FailureMonitor Failure { get; }
        /// <summary>Phase 6 연구 (진행·레벨). 효과는 <see cref="Effects"/>.</summary>
        public ResearchSystem Research { get; }
        /// <summary>현재 연구 효과 (각 시스템이 공유).</summary>
        public ResearchEffects Effects { get; }
        /// <summary>자동 정비·재건축·일괄 정비 (자동화 연구 완료 후 동작).</summary>
        public MaintenanceAutomation Automation { get; }
        /// <summary>현재 등급까지 생긴 거주자 요구 (4-9).</summary>
        public IReadOnlyList<ResidentNeed> ActiveNeeds => _stats.ActiveNeeds;
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
        /// <summary>현재 이벤트 강도 배율 (규모를 정하는 쪽이 있으면 그쪽, 없으면 등급).</summary>
        public float EventIntensity => EventScale.EventIntensityMultiplier;

        private IEventScale _eventScale;

        /// <summary>13-2 공통 고리: 이벤트 규모 (운석 개수 · 간격 · 강도). 규모를 정하는 쪽(서사판 장 · 타이쿤판 성장 비례)이 있으면 그쪽, 없으면 현재 등급.</summary>
        public IEventScale EventScale => _eventScale ?? Progression.Current;

        /// <summary>13-2: 이벤트 규모를 정하는 쪽을 붙인다 (null = 등급).</summary>
        public void SetEventScale(IEventScale scale) => _eventScale = scale;

        /// <summary>13-3 공통 고리: 자연 인구 증가 간격에 곱하는 배율 (기본 1, 서사판 = 이민선이 주된 공급원인 장에서 크게). 다음 틱부터.</summary>
        public float GrowthIntervalScale { get; set; } = 1f;

        /// <summary>M-50 공통 고리: 지을 수 있는 칸을 정하는 쪽 (서사판 4장 위기 동안 새 칸 금지). null이면 공간 규칙만.</summary>
        public IBuildZone BuildZone { get; set; }

        /// <summary>13-1 공통 판정 고리: 규칙을 정하는 쪽 (서사판 장 틀 · 타이쿤판 해금 선택). null이면 등급 조건 그대로.</summary>
        public IStationRules Rules { get; private set; }

        /// <summary>13-1: 규칙이 열렸는지 — 규칙을 정하는 쪽이 있으면 그쪽, 없으면 기존 등급 조건(byGrade).</summary>
        public bool RuleOpen(StationRule rule, bool byGrade) => Rules != null ? Rules.Allows(rule) : byGrade;

        /// <summary>13-1: 규칙을 정하는 쪽을 붙인다 (모듈 해금의 규칙 조건도 함께).</summary>
        public void SetRules(IStationRules rules)
        {
            Rules = rules;
            Progression.RuleCheck = rules != null ? rules.Allows : (Func<StationRule, bool>)null;
        }

        /// <summary>12-0b 틱 단계 (순서대로). 새 시스템은 <see cref="AddSystem"/>의 tickAfter로 이 이름들 사이에 끼운다.</summary>
        public IReadOnlyList<TickStep> TickSteps => _tickSteps;
        /// <summary>12-0b 공통 틀로 붙은 시스템.</summary>
        public IReadOnlyList<StationSystem> Systems => _systems;
        /// <summary>12-3 합성 (설정에 레시피가 없으면 null).</summary>
        public SynthesisSystem Synthesis { get; }
        /// <summary>12-3c 도시급 유성우 방향 예보.</summary>
        public MeteorShowerSystem MeteorShower { get; }
        /// <summary>12-3c 대륙급 열 관리.</summary>
        public HeatSystem Heat { get; }

        /// <summary>12-3c 과열 생산 배율 (열 관리 전 · 한도 이하면 1).</summary>
        public float HeatMultiplier(ModuleInstance module) => Heat != null ? Heat.ProductionMultiplier(module) : 1f;

        /// <summary>12-3c 국가급 상시 정책 (설정에 없으면 null).</summary>
        public PolicySystem Policies { get; }
        /// <summary>12-3c 사회 위기 선택지 (설정에 없으면 null).</summary>
        public SocialCrisisSystem Crisis { get; }
        /// <summary>12-3c 지구급 방문 세력 + 평판 (설정에 없으면 null).</summary>
        public ContactSystem Contact { get; }
        /// <summary>12-5 여객 도킹 포트 이민선.</summary>
        public ImmigrationSystem Immigration { get; }
        /// <summary>도착 안내판이 보는 이민선 일정 — 기본 = <see cref="Immigration"/>, 이민선을 따로 정하는 쪽(서사판)이 바꿈.</summary>
        public IArrivalSchedule Arrivals
        {
            get => _arrivals ?? Immigration;
            set => _arrivals = value;
        }
        private IArrivalSchedule _arrivals;
        /// <summary>
        /// 13-7d 저장 진행 표시 (이어하기 · 저장 목록 · 로딩 화면). null이면 등급 이름 — 모드가 채움 (서사판 캠페인 = "3장 · 무리 합류").
        /// </summary>
        public Func<string> ProgressLabel { get; set; }
        /// <summary>12-4 연구 확장 (설정에 없으면 null).</summary>
        public ResearchExpansionSystem ResearchExpansion { get; }

        public StationSimulation(StationSimulationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            Balance = settings.Balance ?? throw new ArgumentException("Balance 필요", nameof(settings));
            _random01 = settings.Random01 ?? throw new ArgumentException("Random01 필요", nameof(settings));
            _crateRandom = settings.CrateSeed.HasValue ? new Random(settings.CrateSeed.Value) : new Random();
            _eventPool = settings.Events != null ? new List<GameEventData>(settings.Events) : new List<GameEventData>();

            _stats = new StationModuleStats(this);
            _construction = new StationConstruction(this);
            _repairs = new StationRepairs(this, _stats);
            _meteors = new StationMeteors(this, _random01);
            _eventEffects = new StationEventEffects(this, _meteors, _random01, _crateRandom);
            _researchCommands = new StationResearchCommands(this, _stats);
            _interior = new StationInteriorOutcomes(this, _crateRandom);

            Grid = new StationGrid();
            Connectivity = new StationConnectivity(Grid, new FaceAdjacencyConnectionRule());
            Resources = new ResourceSimulation(Balance);
            Population = new PopulationSimulation(Balance, Resources);
            Damage = new DamageSystem(Balance);
            Durability = new DurabilitySystem(Balance);
            Adjacency = new AdjacencySystem(settings.AdjacencyRules);
            Defense = new DefenseSystem(Balance, _stats.Strength);
            Damage.ControlLookup = m => Defense.GetDamageControl(Grid, m); // 8-3 손상 통제실
            Needs = new NeedsSystem(Balance, _stats.Strength, _stats.EffectiveHousing);
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
            Events.IntervalMultiplier = () => EventScale.EventIntervalMultiplier;
            Events.DurationProvider = data => data.Duration * EventIntensity;
            // Phase 6 방어 연구: 바깥에서 오는 위협(운석·태양 폭풍)만 조기 경보
            Events.WarningFilter = data => data is MeteorEventData || data is SolarStormEventData;
            Events.UpcomingChanged += _meteors.HandleUpcomingChanged;

            // 이 구독들이 뷰(StationController)보다 먼저 등록되어, 뷰가 생성될 때 연결 상태가 이미 최신이다.
            Grid.ModulePlaced += HandleModulePlaced;
            Grid.ModuleRemoved += HandleModuleRemoved;
            Grid.ModuleMoved += HandleModuleMoved;
            Damage.Destroyed += HandleDestroyed;
            // 4-6: 정비 베이가 파손·복구되면 즉시 슬롯 수 갱신
            Damage.Damaged += _ => _stats.RefreshRepairCapacity();
            Damage.Repaired += _ => _stats.RefreshRepairCapacity();
            Damage.SpreadDue += _eventEffects.HandleSpreadDue; // 4-7
            Durability.WornOut += HandleDestroyed;
            Events.EventStarted += _eventEffects.HandleEventStarted;
            Events.EventEnded += _eventEffects.HandleEventEnded;
            Resources.Changed += EvaluateProgression;
            // 12-1: 결과 화면 등급 도달 → 구간 마감 (뷰보다 먼저 등록 — 결과 화면이 마감된 구간을 읽음)
            Progression.MilestoneReached += HandleMilestone;

            if (settings.CoreModule == null)
                throw new ArgumentException("CoreModule 필요", nameof(settings));
            Grid.TryPlace(settings.CoreModule, UnityEngine.Vector3Int.zero, 0, out var core);
            Core = core;
            Connectivity.Root = core;
            Connectivity.Recalculate();
            _stats.RefreshCapacities();

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
                    Housing = _stats.EffectiveHousing,
                    Strength = _stats.Strength,
                    Effects = Effects,
                    Now = () => ElapsedSeconds, // 직함 승진: 근속
                    HighestGrade = () => Progression != null ? Progression.HighestGradeIndex : 0, // 직함 승진: 단계를 여는 등급
                };
                Population.PopulationChanged += (delta, reason) => Residents.Update(Resources.Population, reason);
                Residents.Update(Resources.Population);
                ApplyResidentEffects();
            }
            BuildTickSteps();
            if (settings.Recipes != null)
                Synthesis = AddSystem(new SynthesisSystem(settings.Recipes), TickStepNames.Automation); // 요구 · 인구 계산 전에 교체가 끝나게
            MeteorShower = AddSystem(new MeteorShowerSystem(settings.HazardRandom01), TickStepNames.Events); // 도시급 전에는 아무것도 안 함 (기존 결과 그대로)
            Heat = AddSystem(new HeatSystem(), MeteorShowerSystem.SystemKey); // 대륙급 전에는 열 0 (기존 결과 그대로)
            if (settings.Policies != null)
            {
                Policies = AddSystem(new PolicySystem(settings.Policies), MeteorShowerSystem.SystemKey);
                Crisis = AddSystem(new SocialCrisisSystem(settings.Policies), PolicySystem.SystemKey);
            }
            Immigration = AddSystem(new ImmigrationSystem(), TickStepNames.Population); // 12-5 여객 도킹 포트 (포트가 없으면 아무것도 안 함)
            if (settings.ResearchExpansion != null)
                ResearchExpansion = AddSystem(new ResearchExpansionSystem(settings.ResearchExpansion), TickStepNames.Research); // 레벨 연구 바로 뒤
            if (settings.Contact != null)
                Contact = AddSystem(new ContactSystem(settings.Contact, settings.HazardRandom01), HeatSystem.SystemKey); // 지구급 전에는 아무것도 안 함
            EvaluateProgression();
        }

        /// <summary>12-3: 합성 중이라 가동이 멈춘 재료인지 (수리 중과 같은 처리).</summary>
        public bool IsHalted(ModuleInstance module) => Synthesis != null && Synthesis.IsHalted(module);

        /// <summary>세이브 · 전시용 모듈 정의 찾기: 등급 해금 목록 → 12-3 레시피 결과(진화 모듈). 없으면 null.</summary>
        public ModuleData FindModuleData(string assetName)
            => Progression.FindModule(assetName) ?? Synthesis?.FindResult(assetName);

        /// <summary>12-3: 활성 모듈 · 슬롯 · 저장 한도를 지금 다시 계산 (합성 시작으로 가동이 멈췄을 때).</summary>
        internal void RefreshModuleStats() => _stats.RefreshCapacities();

        private int _progressionHold;

        /// <summary>12-3: 모듈을 여러 개 바꾸는 동안 등급 재판정을 미룸 (풀면 한 번 판정).</summary>
        internal void HoldProgression(bool hold)
        {
            _progressionHold = Math.Max(0, _progressionHold + (hold ? 1 : -1));
            if (!hold && _progressionHold == 0)
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
            Effects.TraitSynthesisSpeedMultiplier = 1f + Residents.Ability(ResidentTrait.Artisan);
            ApplyConsumptionMultipliers();
        }

        /// <summary>주민 소비 배율 = 주민 특성(대식가 · 소식가, 명단이 없으면 1) × 12-3c 정책. 다음 틱부터.</summary>
        internal void ApplyConsumptionMultipliers()
        {
            float all = Effects.Policy(PolicyStat.ResidentConsumption);
            float foodWater = all * Effects.Policy(PolicyStat.FoodWaterConsumption);
            float life = Effects.ResidentLifeConsumption; // 12-4 연계 연구 '폐쇄 순환' (산소 · 물)
            Resources.SetResidentConsumptionMultiplier(ResourceType.Food, (Residents != null ? Residents.ConsumptionMultiplier(ResourceType.Food) : 1f) * foodWater);
            Resources.SetResidentConsumptionMultiplier(ResourceType.Water, (Residents != null ? Residents.ConsumptionMultiplier(ResourceType.Water) : 1f) * foodWater * life);
            Resources.SetResidentConsumptionMultiplier(ResourceType.Oxygen, all * life);
        }

        // ---------------- 공통 틀 (12-0b) ----------------

        /// <summary>
        /// 새 시스템을 붙인다: 틱은 tickAfter 단계 바로 뒤(null이면 맨 뒤), 모듈 배치 · 철거 알림, 세이브(<see cref="StationSystem.Key"/>)가 연결된다.
        /// 같은 키가 이미 있으면 예외.
        /// </summary>
        public T AddSystem<T>(T system, string tickAfter) where T : StationSystem
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));
            if (GetSystem(system.Key) != null)
                throw new ArgumentException($"같은 키의 시스템이 이미 있음: {system.Key}", nameof(system));
            TickStepList.Insert(_tickSteps, new TickStep(system.Key, system.Tick), tickAfter);
            _systems.Add(system);
            system.Attach(this);
            return system;
        }

        public StationSystem GetSystem(string key)
        {
            foreach (var s in _systems)
            {
                if (s.Key == key)
                    return s;
            }
            return null;
        }

        public T GetSystem<T>() where T : StationSystem
        {
            foreach (var s in _systems)
            {
                if (s is T t)
                    return t;
            }
            return null;
        }

        /// <summary>기존 시스템의 틱 순서 (이전 Tick 본문과 같은 순서 — 바꾸면 밸런스 봇 결과가 바뀐다).</summary>
        private void BuildTickSteps()
        {
            void Add(string name, Action<float> run) => _tickSteps.Add(new TickStep(name, run));

            Add(TickStepNames.Damage, dt =>
            {
                Damage.TimersHeld = _tutorialTick;
                Damage.Tick(dt);
            });
            Add(TickStepNames.Durability, dt =>
            {
                if (!_tutorialTick)
                    Durability.Tick(dt); // 내구도 0 → 파괴
            });
            Add(TickStepNames.Resources, dt =>
            {
                _stats.CollectActiveModules();
                Resources.SetExternalDrain(ResourceType.Oxygen, Damage.OxygenLeakPerSecond);
                Resources.SolarMultiplier = DayNight.SolarMultiplier(ElapsedSeconds); // 이번 틱 시작 시점의 낮/밤
                Resources.ExtraPowerDemand = Research.RunningPowerDemand // 진행 중인 연구 (Phase 6)
                    + (ResearchExpansion != null ? ResearchExpansion.RunningPowerDemand : 0f); // 12-4 레시피 · 연계 연구
                Resources.Tick(_stats.ActiveModules, _stats.ProductionMultipliers, _stats.ConsumptionMultipliers, dt);
            });
            Add(TickStepNames.Incidents, dt => Incidents.Tick(dt, Resources)); // 11-17 ④ 놓친 사고의 누출
            Add(TickStepNames.Research, dt => Research.Tick(dt, Resources.PowerEfficiency * Effects.ResearchSpeedMultiplier)); // 전력 효율 × 과학자 × 정책
            Add(TickStepNames.Cargo, dt => Cargo.Tick(Grid, Resources, _stats.Strength, dt)); // 8-5 화물선 (가동률만큼)
            Add(TickStepNames.Automation, dt => Automation.Tick(dt)); // 자동화 연구: 기준값 미만 모듈 정비·재건축
            Add(TickStepNames.Needs, dt => _stats.RefreshNeeds()); // 4-9: 이번 틱 전력 효율·인구 기준 요구 충족 → 만족도 상한
            Add(TickStepNames.Population, dt =>
            {
                Population.Tick(dt);
                if (Residents != null)
                {
                    Residents.Update(Resources.Population); // 집 검사·환경·효과 (인원 변화는 PopulationChanged에서 사유와 함께)
                    ApplyResidentEffects();
                }
            });
            Add(TickStepNames.Events, dt =>
            {
                Events.WarningLead = Effects.EarlyWarningSeconds;
                Events.Tick(dt, _eventPool);
            });
            Add(TickStepNames.Clock, dt => ElapsedSeconds += dt);
            Add(TickStepNames.Tutorial, dt => Tutorial?.Tick(dt));
            Add(TickStepNames.Progression, dt => EvaluateProgression());
            Add(TickStepNames.Failure, EvaluateFailure);
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
            // 11-18: 튜토리얼 중에는 파손 확산 · 파괴 타이머와 노후화가 멈춤 (안내대로 따라가다 무너지지 않게, 정비는 정비 단계에서)
            _tutorialTick = Tutorial != null && Tutorial.Active && !Tutorial.Data.Free; // M-51 막지 않는 안내는 그대로 흐름
            foreach (var step in _tickSteps)
                step.Run(dt);
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

        // ---------------- 건설 명령 (StationConstruction) ----------------

        /// <summary>공간 규칙 → 등급 해금·설치 제한 → 비용 순으로 판정.</summary>
        public PlacementResult EvaluatePlacement(ModuleData data, UnityEngine.Vector3Int origin, int rotation) => _construction.EvaluatePlacement(data, origin, rotation);

        /// <summary>실제 건설 비용 (건설·경제 연구 할인 반영).</summary>
        public List<ResourceAmount> GetBuildCost(ModuleData data) => _construction.GetBuildCost(data);

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
        public PlacementResult CheckBuildable(ModuleData data) => _construction.CheckBuildable(data);

        /// <summary>11-18 튜토리얼: 다음 운석 한 번의 피해 개수를 정함 (0 = 등급대로).</summary>
        internal void ForceNextMeteorHits(int hits) => _meteors.ForceNextHits(hits);

        public bool CanAfford(ModuleData data) => _construction.CanAfford(data);

        public bool TryPlace(ModuleData data, UnityEngine.Vector3Int origin, int rotation, out ModuleInstance module)
            => _construction.TryPlace(data, origin, rotation, out module);

        /// <summary>철거 가능: 철거 가능한 종류이고, 다른 모듈을 받치고 있지 않음 (5-5 받침 규칙).</summary>
        public bool CanRemove(ModuleInstance module) => _construction.CanRemove(module);

        /// <summary>코어·철거 불가 모듈이 아님 (재건축은 같은 자리에 다시 짓으므로 받침 여부와 무관).</summary>
        public bool IsRemovableKind(ModuleInstance module) => _construction.IsRemovableKind(module);

        /// <summary>위·아래 모듈의 받침이라 철거할 수 없는지.</summary>
        public bool IsSupportingOthers(ModuleInstance module) => _construction.IsSupportingOthers(module);

        /// <summary>철거: 건설 비용 × 환급률 × 내구도 비율을 돌려받는다.</summary>
        public bool TryRemove(ModuleInstance module) => _construction.TryRemove(module);

        /// <summary>철거 시 실제 환급액 (미리보기).</summary>
        public List<ResourceAmount> GetRefund(ModuleInstance module) => _construction.GetRefund(module);

        public MaintainResult TryMaintain(ModuleInstance module) => _construction.TryMaintain(module);

        /// <summary>재건축 순비용 = 건설비 − 철거 환급 (자원별, 0 이상) × 유지보수 연구 할인.</summary>
        public List<ResourceAmount> GetRebuildCost(ModuleInstance module) => _construction.GetRebuildCost(module);

        /// <summary>비용과 무관하게 재건축이 허용되는 모듈인지 (자동화 계획용).</summary>
        public bool CanRebuildKind(ModuleInstance module) => _construction.CanRebuildKind(module);

        /// <summary>같은 자리에 철거 후 새로 건설 (내구도·최대 내구도 100, 파손 해제). 순비용만 지불.</summary>
        public RebuildResult TryRebuild(ModuleInstance module, out ModuleInstance rebuilt) => _construction.TryRebuild(module, out rebuilt);

        /// <summary>12-3b: 자리와 무관하게 옮길 수 있는지 (코어 · 합성 중 · 파손 · 받침이면 불가).</summary>
        public MoveBlock CheckMovable(ModuleInstance module) => _construction.CheckMovable(module);

        /// <summary>12-3b 이동 비용 = 건설비 × 이동 비율 (기본 20%).</summary>
        public List<ResourceAmount> GetMoveCost(ModuleInstance module) => _construction.GetMoveCost(module);

        /// <summary>12-3b 옮길 자리 판정 (공간 규칙 → 다른 모듈과 면 접촉 → 비용).</summary>
        public PlacementResult EvaluateMove(ModuleInstance module, UnityEngine.Vector3Int origin, int rotation) => _construction.EvaluateMove(module, origin, rotation);

        /// <summary>12-3b 비용을 내고 즉시 옮김 (같은 모듈 — 내구도 · 주민 · 상자 그대로).</summary>
        public bool TryMove(ModuleInstance module, UnityEngine.Vector3Int origin, int rotation) => _construction.TryMove(module, origin, rotation);

        /// <summary>파괴 (방치 등): 환급 없음. 코어는 파괴되지 않는다.</summary>
        public bool DestroyModule(ModuleInstance module) => _construction.DestroyModule(module);

        // ---------------- 수리 명령 (StationRepairs) ----------------

        public RepairResult TryRepair(ModuleInstance module) => _repairs.TryRepair(module);

        /// <summary>
        /// 11-17 내부 현장 수리 비용 = 보통 수리 비용 × 현장 비율 (BALANCE 28번). 이미 수리 비용을 낸 모듈(대기 · 수리 중)은 0.
        /// </summary>
        public List<ResourceAmount> GetFieldRepairCost(ModuleInstance module) => _repairs.GetFieldRepairCost(module);

        /// <summary>11-17 ② 현장 수리 마무리에 무료 수리권을 쓰는지 (아직 수리 비용을 내지 않았고 수리권이 있을 때).</summary>
        public bool FieldRepairUsesFreeRepair(ModuleInstance module) => _repairs.FieldRepairUsesFreeRepair(module);

        /// <summary>11-17 내부 현장 수리 완료: 비용을 내고 수리 슬롯 · 시간 없이 바로 복구 (손상 지점을 다 고친 뒤 부름).</summary>
        public RepairResult TryFieldRepair(ModuleInstance module) => _repairs.TryFieldRepair(module);

        /// <summary>대기 중인 수리를 맨 앞으로 (4-6).</summary>
        public bool TryPrioritizeRepair(ModuleInstance module) => _repairs.TryPrioritizeRepair(module);

        /// <summary>대기 취소: 낸 수리 비용 × 취소 환불률(BALANCE 18번, 50%)을 돌려받는다.</summary>
        public bool TryCancelRepair(ModuleInstance module) => _repairs.TryCancelRepair(module);

        /// <summary>대기 취소 시 돌려받을 양 (미리보기).</summary>
        public List<ResourceAmount> GetCancelRefund(ModuleInstance module) => _repairs.GetCancelRefund(module);

        /// <summary>동시 수리 슬롯 = 기본(코어) + 활성이고 파손되지 않은 정비 베이의 슬롯 합 (BALANCE 17번).</summary>
        public int CountRepairSlots() => _stats.CountRepairSlots();

        /// <summary>12-5 센서 어레이: 유성우 · 포격 예보 시간 + (가동 중인 것의 합, 상한).</summary>
        public float SensorWarningBonus => _stats.SensorWarningBonus;
        /// <summary>12-5 가동 중인 여객 도킹 포트 수 (이민선).</summary>
        public int ImmigrationPorts => _stats.ImmigrationPorts;

        /// <summary>가중치 랜덤 이벤트 즉시 발생 (디버그 F5).</summary>
        public GameEventData TriggerRandomEvent() => Events.TriggerRandom(_eventPool);

        // ---------------- 모듈 파생값 (StationModuleStats) ----------------

        /// <summary>
        /// 8-4 회전 링: 가동 중인(활성·정상) 모듈 중 가장 강한 인구 증가 간격 배율 (중첩 없음).
        /// 배율 = 1 − (1 − 값) × 가동률. 없으면 1.
        /// </summary>
        public float GrowthIntervalMultiplier() => _stats.GrowthIntervalMultiplier();

        /// <summary>
        /// 7-1: 모듈 자체 효율 (선택 테두리·패널 색). 비활성 0.
        /// 방어·서비스 모듈은 가동률(파손 시 0), 나머지는 자원 틱과 같은 최종 생산 배율
        /// = 전력 효율(전력을 쓰는 경우) × 파손 × 내구도 × 인접 × 생산 연구.
        /// </summary>
        public float GetModuleEfficiency(ModuleInstance module) => _stats.Efficiency(module);

        // ---------------- 모듈 배치 · 철거 · 파괴 ----------------

        private bool _bulkPlacing;

        /// <summary>
        /// 12-3c-opt 세이브 복원: 모듈을 한꺼번에 놓는 동안 인접 효과 · 연결 · 용량 · 등급의 전체 재계산을 미루고 <see cref="EndBulkPlace"/>에서 한 번만
        /// (예전: 모듈마다 정거장 전체 재계산 — 대륙급 579개 복원 1.8초). 끝난 상태는 같다.
        /// </summary>
        internal void BeginBulkPlace() => _bulkPlacing = true;

        internal void EndBulkPlace()
        {
            if (!_bulkPlacing)
                return;
            _bulkPlacing = false;
            Adjacency.Recalculate(Grid);
            if (Connectivity.Root != null)
                Connectivity.Recalculate();
            _stats.RefreshCapacities();
            EvaluateProgression();
        }

        private void HandleModulePlaced(ModuleInstance module)
        {
            if (module.Data != null && module.Data.Removable)
                Durability.Track(module); // 코어(철거 불가)는 노후화 없음
            if (_bulkPlacing)
            {
                foreach (var s in _systems)
                    s.OnModulePlaced(module);
                return;
            }
            Adjacency.Recalculate(Grid);
            if (Connectivity.Root != null)
                Connectivity.Recalculate();
            _stats.RefreshCapacities();
            EvaluateProgression();
            foreach (var s in _systems)
                s.OnModulePlaced(module);
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            Damage.Forget(module); // 파손 중 철거된 경우
            Durability.Forget(module);
            Cargo.Forget(module);
            Supply.Relocate(module, Core); // 그 방에 있던 보급 상자는 코어로
            Adjacency.Recalculate(Grid);
            Connectivity.Recalculate();
            _stats.RefreshCapacities();
            EvaluateProgression();
            _meteors.OnModuleRemoved(module);
            foreach (var s in _systems)
                s.OnModuleRemoved(module);
        }

        /// <summary>12-3b: 같은 모듈이 자리만 바뀜 → 위치에 따라 달라지는 값만 다시 계산 (모듈별 상태는 그대로).</summary>
        private void HandleModuleMoved(ModuleInstance module, IReadOnlyList<UnityEngine.Vector3Int> oldCells)
        {
            Adjacency.Recalculate(Grid);
            Connectivity.Recalculate();
            _stats.RefreshCapacities();
            EvaluateProgression();
            foreach (var s in _systems)
                s.OnModuleMoved(module);
        }

        private void HandleDestroyed(ModuleInstance module)
        {
            Session.RecordDestroyed();
            DestroyModule(module);
        }

        // ---------------- 연구 명령 (StationResearchCommands) ----------------

        /// <summary>다음 레벨 연구를 시작할 수 있는지 (등급·인구·연구소·자원).</summary>
        /// <param name="usePoint">11-17 ②: 연구 포인트 1개로 시작 비용 할인 (포인트가 없으면 무시).</param>
        public ResearchStartResult CanStartResearch(ResearchCategoryData category, bool usePoint = false) => _researchCommands.CanStart(category, usePoint);

        /// <summary>11-17 ②: 다음 레벨 연구 시작 비용 (연구 포인트를 쓰면 할인된 값). 최고 레벨이면 빈 목록.</summary>
        public List<ResourceAmount> GetResearchStartCost(ResearchCategoryData category, bool usePoint) => _researchCommands.GetStartCost(category, usePoint);

        /// <summary>시작 비용을 내고 다음 레벨 연구 시작 (환급 없음). usePoint = 연구 포인트 1개로 할인 (11-17 ②).</summary>
        public ResearchStartResult TryStartResearch(ResearchCategoryData category, bool usePoint = false) => _researchCommands.TryStart(category, usePoint);

        /// <summary>진행 중인 연구 취소 (시작 비용·진행률 모두 잃음).</summary>
        public bool CancelResearch(ResearchCategoryData category) => _researchCommands.Cancel(category);

        /// <summary>연구 효과가 바뀌면 캐시된 값(인접 효과, 저장 한도·수용 인구·배터리)을 다시 계산.</summary>
        private void HandleResearchEffectsChanged()
        {
            Adjacency.Recalculate(Grid);
            if (Core != null)
                _stats.RefreshCapacities();
        }

        internal void EvaluateProgression()
        {
            if (Progression == null || Session == null || _progressionHold > 0)
                return; // 생성자 도중 · 합성 교체 중
            int population = Resources.Population;
            Progression.Evaluate(population, StationProgression.CountGradeModules(Grid)); // 8-6: 장갑 격벽 제외
            Session.ObservePopulation(population);
            ProgressionEvaluated?.Invoke();
        }

        private void HandleMilestone(int grade, GradeMilestone milestone)
        {
            if (milestone == GradeMilestone.Result)
                Session.CloseSegment(ElapsedSeconds, StationProgression.CountModules(Grid), Resources.Population); // 12-3: 진화 모듈은 재료 수만큼
        }

        // ---------------- 조기 경보 · 운석 (StationMeteors) ----------------

        /// <summary>경보 중인 운석이 맞을 예정인 모듈 (대상 표시 연구가 있을 때만, 없으면 비어 있음).</summary>
        public IReadOnlyList<ModuleInstance> PlannedMeteorTargets => _meteors.PlannedTargets;

        /// <summary>세이브 복원: 경보 중이던 운석의 예정 대상.</summary>
        internal void RestoreMeteorPlan(int hits, IEnumerable<ModuleInstance> targets) => _meteors.RestorePlan(hits, targets);

        public int PlannedMeteorHits => _meteors.PlannedHits;

        /// <summary>12-3c 유성우 한 파 (정해진 대상, 전용 난수).</summary>
        internal void ShowerMeteors(IReadOnlyList<ModuleInstance> targets, Func<float> random01, UnityEngine.Vector3Int incoming, string label)
            => _meteors.Shower(targets, random01, incoming, label);

        // ---------------- 내부 결과 (StationInteriorOutcomes) ----------------

        /// <summary>11-17 ② 보급 상자 줍기: 보상 적용 후 상자 제거. text = 받은 것 (알림용).</summary>
        public bool TryPickupCrate(int id, out string text) => _interior.TryPickupCrate(id, out text);

        /// <summary>13-6 고리: 남은 보급 상자를 바깥에서 회수 (<see cref="SupplyCrateSystem.AutoCollectRate"/>). 반환 = 받은 것 요약, 상자가 없으면 null.</summary>
        public string AutoCollectCrates() => _interior.AutoCollectCrates(Supply.AutoCollectRate);

        /// <summary>
        /// 11-17 ③ 주민 요청을 들어줌 (BALANCE 30번): 정거장 만족도 + 그 주민 기분 보너스, 가끔 연구 포인트.
        /// roll01 = 연구 포인트 판정 난수 (없으면 상자 난수). text = 받은 것 (알림용).
        /// </summary>
        public bool CompleteResidentRequest(Resident resident, out string text, float? roll01 = null)
            => _interior.CompleteResidentRequest(resident, out text, roll01);

        /// <summary>11-17 ④ 내부 사고를 처리함 (BALANCE 31번): 주민들이 안심 → 만족도 조금. text = 알림용.</summary>
        public string ResolveIncident() => _interior.ResolveIncident();

        /// <summary>
        /// 11-17 ④ 내부 사고를 놓침 (처리 시간이 지났거나 밖으로 나감, BALANCE 31번): 상한 있는 짧은 손실 후 정비 인력이 처리한 것으로 끝.
        /// 누출 = 물/산소가 나눠서 샘, 차단기 = 그 방 생산 배율, 화재 = 그 방 내구도(5 아래로는 안 내려감) + 만족도, 환기 = 산소 + 만족도. text = 알림용.
        /// </summary>
        public string MissIncident(IncidentKind kind, ModuleInstance module, ResourceType leakResource = ResourceType.Oxygen)
            => _interior.MissIncident(kind, module, leakResource);

        /// <summary>11-17 ③ 요청을 들어준 주민의 기분 보너스 (끝났으면 0).</summary>
        public float CheerOf(Resident resident) => _interior.CheerOf(resident);

        // ---------------- 알림 ----------------

        internal void Report(string message, bool positive)
        {
            EffectReported?.Invoke(message, positive);
        }
    }
}
