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
        /// <summary>[0, 1) 난수. 이벤트 선택·간격, 운석 대상에 사용 (시드 고정 가능).</summary>
        public Func<float> Random01;
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
        private readonly List<ModuleInstance> _meteorCandidates = new List<ModuleInstance>();
        private readonly List<GameEventData> _eventPool;
        private readonly Func<float> _random01;

        /// <summary>(요약 메시지, 긍정 여부). 이벤트 효과 알림.</summary>
        public event Action<string, bool> EffectReported;
        /// <summary>등급·승패 재판정 직후 (UI 갱신용).</summary>
        public event Action ProgressionEvaluated;

        public BalanceConfig Balance { get; }
        public StationGrid Grid { get; }
        public StationConnectivity Connectivity { get; }
        public ResourceSimulation Resources { get; }
        public PopulationSimulation Population { get; }
        public DamageSystem Damage { get; }
        public EventScheduler Events { get; }
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
            Events = new EventScheduler(Balance.EventGracePeriod, Balance.EventIntervalMin, Balance.EventIntervalMax, _random01);
            Progression = new StationProgression(settings.Grades);
            Session = new GameSession();
            DayNight = new DayNightCycle(Balance.DayNightPeriod, Balance.DayLength, Balance.DayNightTransition, Balance.NightSolarMultiplier);

            // 4-1: 등급별 이벤트 빈도·강도 (새 간격을 정할 때 / 지속형 이벤트가 시작될 때의 등급 기준)
            Events.IntervalMultiplier = () => Progression.Current.EventIntervalMultiplier;
            Events.DurationProvider = data => data.Duration * EventIntensity;

            // 이 구독들이 뷰(StationController)보다 먼저 등록되어, 뷰가 생성될 때 연결 상태가 이미 최신이다.
            Grid.ModulePlaced += HandleModulePlaced;
            Grid.ModuleRemoved += HandleModuleRemoved;
            Damage.Destroyed += HandleDestroyed;
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
            EvaluateProgression();
        }

        // ---------------- 틱 ----------------

        public void Tick(float dt)
        {
            Damage.Tick(dt);
            CollectActiveModules();
            Resources.SetExternalDrain(ResourceType.Oxygen, Damage.OxygenLeakPerSecond);
            Resources.SolarMultiplier = DayNight.SolarMultiplier(ElapsedSeconds); // 이번 틱 시작 시점의 낮/밤
            Resources.Tick(_activeModules, _productionMultipliers, dt);
            Population.Tick(dt);
            Events.Tick(dt, _eventPool);
            ElapsedSeconds += dt;
            EvaluateProgression();
        }

        // ---------------- 명령 ----------------

        /// <summary>공간 규칙 → 등급 해금·설치 제한 → 비용 순으로 판정.</summary>
        public PlacementResult EvaluatePlacement(ModuleData data, UnityEngine.Vector3Int origin, int rotation)
        {
            var result = PlacementRules.Evaluate(Grid, data, origin, rotation);
            if (result == PlacementResult.Valid)
                result = CheckBuildable(data);
            if (result == PlacementResult.Valid && !Resources.CanAfford(data.BuildCost))
                return PlacementResult.InsufficientResources;
            return result;
        }

        /// <summary>위치와 무관한 건설 가능 여부 (해금·최대 설치 수).</summary>
        public PlacementResult CheckBuildable(ModuleData data) => Progression.CheckBuildable(data, Grid);

        public bool CanAfford(ModuleData data) => data != null && Resources.CanAfford(data.BuildCost);

        public bool TryPlace(ModuleData data, UnityEngine.Vector3Int origin, int rotation, out ModuleInstance module)
        {
            module = null;
            if (EvaluatePlacement(data, origin, rotation) != PlacementResult.Valid)
                return false;
            if (!Resources.TrySpend(data.BuildCost))
                return false;
            return Grid.TryPlace(data, origin, rotation, out module);
        }

        public bool CanRemove(ModuleInstance module)
        {
            return module != null && module != Core && (module.Data == null || module.Data.Removable);
        }

        /// <summary>철거: 건설 비용 × 환급률을 돌려받는다.</summary>
        public bool TryRemove(ModuleInstance module)
        {
            if (!CanRemove(module) || !Grid.Remove(module))
                return false;
            if (module.Data != null)
                Resources.RefundBuildCost(module.Data.BuildCost);
            return true;
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
            if (!Resources.TrySpend(Damage.GetRepairCost(module)))
                return RepairResult.InsufficientResources;
            Damage.StartRepair(module);
            return RepairResult.Started;
        }

        /// <summary>가중치 랜덤 이벤트 즉시 발생 (디버그 F5).</summary>
        public GameEventData TriggerRandomEvent() => Events.TriggerRandom(_eventPool);

        // ---------------- 내부 ----------------

        private void HandleModulePlaced(ModuleInstance module)
        {
            if (Connectivity.Root != null)
                Connectivity.Recalculate();
            RefreshCapacities();
            EvaluateProgression();
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            Damage.Forget(module); // 파손 중 철거된 경우
            Connectivity.Recalculate();
            RefreshCapacities();
            EvaluateProgression();
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
            foreach (var module in Grid.Modules)
            {
                if (module.Data == null || !Connectivity.IsActive(module))
                    continue;
                _activeModules.Add(module.Data);
                _productionMultipliers.Add(Damage.GetProductionMultiplier(module));
            }
        }

        private void EvaluateProgression()
        {
            if (Progression == null || Session == null)
                return; // 생성자 도중
            int population = Resources.Population;
            Progression.Evaluate(population, Grid.ModuleCount);
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
                    float ratio = Math.Min(1f, leak.StockLossRatio * EventIntensity);
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

        /// <summary>
        /// 4-1: 등급별 개수만큼 외곽 모듈을 노출 면 수로 가중해 서로 다르게 선택 (분산 타격).
        /// </summary>
        private void ApplyMeteor()
        {
            var grade = Progression.Current;
            int span = grade.MeteorHitsMax - grade.MeteorHitsMin + 1;
            int hits = grade.MeteorHitsMin + Math.Min(span - 1, (int)(_random01() * span));

            Damage.PickMeteorTargets(Grid, Core, hits, _random01, _meteorCandidates);
            if (_meteorCandidates.Count == 0)
            {
                Report("운석이 정거장을 빗나갔습니다", true);
                return;
            }

            var sb = new StringBuilder();
            foreach (var target in _meteorCandidates)
            {
                Damage.Damage(target);
                if (sb.Length > 0)
                    sb.Append(", ");
                sb.Append(target.Data != null ? target.Data.DisplayName : target.ToString());
            }
            string head = _meteorCandidates.Count > 1 ? $"운석 {_meteorCandidates.Count}개 충돌! 파손: " : "파손: ";
            Report($"{head}{sb}  ·  {Balance.DestroyAfterSeconds:0}초 안에 수리 (선택 후 R)", false);
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
            Report(sb.ToString(), true);
        }

        private void Report(string message, bool positive)
        {
            EffectReported?.Invoke(message, positive);
        }
    }
}
