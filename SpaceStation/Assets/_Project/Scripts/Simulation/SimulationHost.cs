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
        [Header("Tycoon (13-1)")]
        [Tooltip("타이쿤판 해금 선택 (메뉴 SpaceStation/Tycoon/Setup Unlock Choices가 채움). 비면 등급 숫자로 시스템이 열림")]
        [SerializeField] private UnlockChoiceConfig _unlockChoices;
        [Tooltip("타이쿤판 성장 비례 위협 (메뉴 SpaceStation/Tycoon/Setup Growth Threat가 채움). 비면 등급 표가 위기 크기를 정함")]
        [SerializeField] private GrowthThreatConfig _growthThreat;
        [Tooltip("타이쿤판 사회 사건 '역병' (메뉴 SpaceStation/Tycoon/Setup Needs가 채움). 비면 없음")]
        [SerializeField] private PlagueConfig _plague;
        [Tooltip("타이쿤판 1인칭 가볍게 — 사고 손실 없음 · 보급 상자 자동 회수 · 등급 보상 연구 포인트 (메뉴 SpaceStation/Tycoon/Setup Interior Bonus가 채움). 비면 지금 동작 그대로")]
        [SerializeField] private InteriorBonusConfig _interiorBonus;

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
            });
            if (_unlockChoices != null)
                UnlockChoiceSystem.AttachTo(Simulation, _unlockChoices); // 13-1 타이쿤판 (세이브 복원 전에)
            if (_growthThreat != null)
                GrowthThreatSystem.AttachTo(Simulation, _growthThreat); // 13-2 타이쿤판
            if (_plague != null)
                PlagueSystem.AttachTo(Simulation, _plague, () => Random.value * 0.99999f); // 13-3 타이쿤판
            if (_interiorBonus != null)
                InteriorBonusSystem.AttachTo(Simulation, _interiorBonus); // 13-6 타이쿤판
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
