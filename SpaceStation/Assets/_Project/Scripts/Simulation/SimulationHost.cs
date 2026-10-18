using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 씬에서 <see cref="StationSimulation"/>을 소유하고 SimulationClock 틱에 연결한다.
    /// 다른 컨트롤러(StationController, ResourceController 등)는 이 호스트를 통해 시뮬레이션에 접근한다.
    /// 디버그: F5 = 가중치 랜덤 이벤트 즉시 발생 — 에디터·Development Build에서만 (배포 빌드에서는 꺼짐).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationHost : MonoBehaviour
    {
        [SerializeField] private BalanceConfig _balance;
        [SerializeField] private StationGradeConfig _grades;
        [SerializeField] private ModuleData _coreModule;
        [SerializeField] private AdjacencyRuleSet _adjacencyRules;
        [Tooltip("랜덤 이벤트 후보")]
        [SerializeField] private List<GameEventData> _events = new List<GameEventData>();
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private bool _enableDebugEventTrigger = true;
        [Tooltip("메인 메뉴를 거치지 않고 이 씬을 바로 실행할 때의 난이도 (5-9)")]
        [SerializeField] private DifficultyPreset _defaultDifficulty;
        [Header("Research (Phase 6)")]
        [SerializeField] private List<ResearchCategoryData> _researchCategories = new List<ResearchCategoryData>();
        [SerializeField] private ResearchLevelCapConfig _researchCaps;
        [Tooltip("12-4 연구 확장 — 레시피 그림자 · 해금 연구 · 연계 연구 (메뉴 SpaceStation/Research/Setup Expansion이 채움). 비면 레시피는 자동 해금만")]
        [SerializeField] private ResearchExpansionConfig _researchExpansion;
        [Header("Save (Phase 6)")]
        [Tooltip("저장의 난이도 이름을 찾을 목록 (이지/노멀/하드)")]
        [SerializeField] private List<DifficultyPreset> _difficulties = new List<DifficultyPreset>();
        [Header("Tutorial (Phase 9)")]
        [Tooltip("첫 새 게임(설정 '다음 새 게임에서 튜토리얼')에서 시작할 튜토리얼")]
        [SerializeField] private TutorialData _tutorial;
        [Header("Residents (Phase 10)")]
        [SerializeField] private ResidentConfig _residents;
        [Header("Synthesis (12-3)")]
        [Tooltip("합성 레시피 (메뉴 SpaceStation/Recipes/Setup이 채움). 비면 합성 없음")]
        [SerializeField] private List<RecipeData> _recipes = new List<RecipeData>();
        [Header("Grade Rules (12-3c)")]
        [Tooltip("국가급 상시 정책 · 사회 위기 (메뉴 SpaceStation/Grade Rules/Setup Policies가 채움). 비면 없음")]
        [SerializeField] private PolicyConfig _policies;
        [Tooltip("지구급 방문 세력 + 평판 (메뉴 SpaceStation/Grade Rules/Setup Contact가 채움). 비면 없음")]
        [SerializeField] private ContactConfig _contact;
        [Header("Chapters (13-1)")]
        [Tooltip("장 틀 (메뉴 SpaceStation/Chapters/Setup Greybox가 채움). 비면 등급 방식 (해금 · 이벤트 규모를 등급이 정함)")]
        [SerializeField] private ChapterConfig _chapters;
        [Tooltip("13-2 코어 성격 4축 · 문장 표 (메뉴 SpaceStation/Chapters/Setup Greybox가 채움). 비면 코어 성격 없음")]
        [SerializeField] private CoreConfig _core;
        [Tooltip("13-3 이민선 시계 + 종족 (메뉴 SpaceStation/Chapters/Setup Greybox가 채움). 비면 이민선 · 종족 없음 (장 틀이 있을 때만)")]
        [SerializeField] private ArkShipConfig _arkShips;
        [Tooltip("13-5a 탐사 (메뉴 SpaceStation/Chapters/Setup Greybox가 채움). 비면 탐사 없음 (장 틀이 있을 때만)")]
        [SerializeField] private ExplorationConfig _exploration;
        [Tooltip("13-5b 기록 복원 (메뉴 SpaceStation/Chapters/Setup Greybox가 채움). 비면 연구가 공통 틀 그대로 (장 틀이 있을 때만)")]
        [SerializeField] private RecordConfig _records;
        [Tooltip("13-6a 종족 정책 반응 · 긴장도 (메뉴 SpaceStation/Chapters/Setup Greybox가 채움). 비면 반응 · 긴장도 없음 (장 정책은 그대로)")]
        [SerializeField] private SocietyConfig _society;

        public StationSimulation Simulation { get; private set; }
        public SimulationClock Clock => _clock;
        /// <summary>이번 판에 적용된 난이도 (없으면 null = 에셋 값 그대로).</summary>
        public DifficultyPreset Difficulty { get; private set; }
        /// <summary>이번 판을 저장에서 불러왔으면 그 저장 (카메라 복원·알림용), 새 게임이면 null.</summary>
        public Save.SaveFile LoadedSave { get; private set; }
        /// <summary>불러올 때 찾지 못해 건너뛴 항목 수.</summary>
        public int LoadMissingCount { get; private set; }

        private void Awake()
        {
            // 불러오기는 게임 씬에서만 (메인 메뉴 전시 정거장도 이 호스트를 쓴다)
            var pending = gameObject.scene.name == SceneNames.Game ? GameStartOptions.PendingLoad : null;
            if (pending != null)
            {
                GameStartOptions.PendingLoad = null;
                var saved = FindDifficulty(pending.Meta.Difficulty);
                if (saved != null)
                    GameStartOptions.Difficulty = saved; // 재시작해도 같은 난이도
            }
            Difficulty = GameStartOptions.Difficulty != null ? GameStartOptions.Difficulty : _defaultDifficulty;
            var balance = Difficulty != null ? Difficulty.ApplyTo(_balance) : _balance;
            var grades = Difficulty != null ? Difficulty.ApplyTo(_grades) : _grades;
            var chapters = _chapters != null && Difficulty != null ? Difficulty.ApplyTo(_chapters) : _chapters;
            Simulation = new StationSimulation(new StationSimulationSettings
            {
                Balance = balance,
                Grades = grades,
                CoreModule = _coreModule,
                Events = _events,
                AdjacencyRules = _adjacencyRules,
                Random01 = () => Random.value * 0.99999f, // [0, 1) 보장
                ResearchCategories = _researchCategories,
                ResearchCaps = _researchCaps,
                Residents = _residents,
                Recipes = _recipes.Count > 0 ? _recipes : null,
                Policies = _policies,
                Contact = _contact,
                ResearchExpansion = _researchExpansion,
                Chapters = chapters,
            });
            // 13-2 서사판 코어 성격 (공통 틀 시스템으로 위에 얹음 — 세이브 복원 전에 붙여야 상태가 돌아옴)
            if (_core != null && gameObject.scene.name == SceneNames.Game)
                Simulation.AddSystem(new CorePersonalitySystem(_core), TickStepNames.Progression);
            // 13-3 서사판 이민선 시계 + 종족 (장 틀 위에서만)
            if (_arkShips != null && Simulation.Chapters != null && gameObject.scene.name == SceneNames.Game)
                Simulation.AddSystem(new ArkShipSystem(_arkShips), TickStepNames.Population);
            // 13-5a 서사판 탐사 (장 틀 위에서만)
            if (_exploration != null && Simulation.Chapters != null && gameObject.scene.name == SceneNames.Game)
                Simulation.AddSystem(new ExplorationSystem(_exploration), TickStepNames.Population);
            // 13-5c 서사판 장 유성우 규칙 (첫 유성우 앞당김 · '유성우 버티기' 표시, 장 틀 위에서만)
            if (Simulation.Chapters != null && gameObject.scene.name == SceneNames.Game)
                Simulation.AddSystem(new ChapterShowerSystem(), TickStepNames.Events);
            // 13-5b 서사판 기록 복원 (장 틀 위에서만 — 연구 판 고리 M-19를 꽂음)
            if (_records != null && Simulation.Chapters != null && gameObject.scene.name == SceneNames.Game)
                Simulation.AddSystem(new RecordArchiveSystem(_records), TickStepNames.Research);
            // 13-6a 서사판 장 정책 (정책 판 고리 M-22를 꽂음) · 긴장도 (장 틀 위에서만)
            if (Simulation.Policies != null && Simulation.Chapters != null && gameObject.scene.name == SceneNames.Game)
                Simulation.AddSystem(new ChapterPolicySystem(_society), TickStepNames.Population);
            if (_society != null && Simulation.Chapters != null && gameObject.scene.name == SceneNames.Game)
                Simulation.AddSystem(new TensionSystem(_society), TickStepNames.Population);
            // Phase 9: 새 게임은 설정 값, 불러온 판은 저장에 진행 중 튜토리얼이 있을 때만 (게임 씬에서만)
            if (_tutorial != null && gameObject.scene.name == SceneNames.Game)
            {
                bool start = pending != null
                    ? pending.Station != null && pending.Station.Tutorial != null && pending.Station.Tutorial.Active
                    : Settings.GameSettings.TutorialPending;
                if (start)
                    Simulation.StartTutorial(_tutorial);
            }
            // 11-18 튜토리얼 중에는 그 단계의 조작만 (화면 쪽이 묻는 공용 관문)
            var sim = Simulation;
            _gate = (action, data) => sim.Tutorial == null || sim.Tutorial.Allows(action, data);
            Core.TutorialGate.Allows = _gate;
            if (pending != null)
            {
                LoadedSave = pending;
                LoadMissingCount = StationStateSerializer.Restore(Simulation, pending.Station); // 뷰·UI가 생기기 전에 복원
                if (LoadMissingCount > 0)
                    Debug.LogWarning($"[Save] 불러오기: 찾을 수 없는 항목 {LoadMissingCount}개 건너뜀");
            }
            if (Debug.isDebugBuild) // 진단 로그는 개발 빌드에서만
            {
                Simulation.Resources.DepletionChanged += HandleDepletionChanged;
                Simulation.Events.EventStarted += HandleEventStarted;
                Simulation.Events.EventEnded += HandleEventEnded;
            }
        }

        private void Start()
        {
            // SimulationClock.Awake 이후
            _clock.Clock.Ticked += HandleTicked;
        }

        private System.Func<Core.TutorialAction, ModuleData, bool> _gate;

        private void OnDestroy()
        {
            if (_clock != null && _clock.Clock != null)
                _clock.Clock.Ticked -= HandleTicked;
            if (Core.TutorialGate.Allows == _gate) // 다음 씬 호스트가 이미 바꿔 끼웠으면 그대로
                Core.TutorialGate.Allows = null;
        }

        private void Update()
        {
            // 배포 빌드에서는 끔 (에디터·Development Build에서만)
            if (!_enableDebugEventTrigger || !Debug.isDebugBuild || _clock.InputLocked)
                return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f5Key.wasPressedThisFrame && Simulation.TriggerRandomEvent() == null)
                Debug.Log("[이벤트] 발생 가능한 이벤트 없음 (모두 진행 중)");
        }

        /// <summary>난이도 에셋 이름 → 프리셋 (목록·기본값에서 찾음).</summary>
        public DifficultyPreset FindDifficulty(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
                return null;
            foreach (var d in _difficulties)
                if (d != null && d.name == assetName)
                    return d;
            return _defaultDifficulty != null && _defaultDifficulty.name == assetName ? _defaultDifficulty : null;
        }

        private void HandleTicked(long tick)
        {
            Simulation.Tick(_clock.Clock.TickInterval);
        }

        private static void HandleDepletionChanged(ResourceType type, bool depleted)
        {
            if (type == ResourceType.Metal)
                return; // 금속 0은 건설로 다 쓴 정상 상황
            if (depleted)
                Debug.LogWarning($"[자원] {type} 고갈");
            else
                Debug.Log($"[자원] {type} 고갈 해소");
        }

        private static void HandleEventStarted(GameEventData data)
        {
            Debug.Log($"[이벤트] 발생: {data.DisplayName}" + (data.IsTimed ? $" ({data.Duration:0}초)" : ""));
        }

        private static void HandleEventEnded(ActiveEvent active)
        {
            Debug.Log($"[이벤트] 종료: {active.Data.DisplayName}");
        }
    }
}
