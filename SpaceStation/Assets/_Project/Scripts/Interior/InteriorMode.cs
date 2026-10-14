using System;
using System.Collections;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Settings;
using SpaceStation.Simulation;
using SpaceStation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// Phase 11-1 내부 방문 모드: 선택 모듈에서 [들어가기] → 정거장 배치대로 내부를 별도 공간에 만들고 1인칭으로 걷는다. ESC로 복귀.
    /// 들어가 있는 동안: 궤도 카메라·건설·선택·창 단축키 정지(InputGate), 세계 좌표 HUD 숨김, 태양광 끔(방마다 점광원).
    /// 시간: 설정 "내부 방문 중 일시정지"가 켜져 있으면 멈춤(P·배속 키 잠금), 꺼져 있으면 계속 흐르고 자원·시간 HUD는 보인다.
    /// 시간이 흐르는 중 모듈이 바뀌면 지금 있는 방 기준으로 다시 만들고, 있던 방이 사라지면 밖으로 나온다.
    /// ESC 처리는 일시정지 메뉴(-200)보다 먼저.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public sealed class InteriorMode : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private ModuleSelectionController _selection;
        [SerializeField] private BuildController _build;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private Camera _camera;
        [Tooltip("들어가 있는 동안 끄는 태양 조명")]
        [SerializeField] private Light _sun;
        [Tooltip("내부를 만드는 위치 (외부 정거장이 내부 카메라 시야 거리 InteriorFarClip 밖에 있도록). 너무 멀면 float 정밀도가 떨어짐 — " +
                 "y -5000에서는 정점 위치가 0.5mm 단위로 끊겨 패드 글자(높이 4mm) 크기 · 기준선이 들쭉날쭉했음 (11-16 피드백) → -400 (0.03mm)")]
        [SerializeField] private Vector3 _origin = new Vector3(0f, -400f, 0f);
        /// <summary>내부에 있는 동안 카메라 시야 거리 — 내부(원점 근처 ±수십 m)는 다 보이고, 바깥 정거장(y 0 근처, 원점에서 350m 이상)은 안 보이게.</summary>
        private const float InteriorFarClip = 250f;
        /// <summary>바깥 창 카메라 시야 거리 — 정거장 · 소행성(±100m)은 보이고 아래 내부 공간은 안 보이게.</summary>
        private const float ExteriorViewFarClip = 300f;
        [Tooltip("11-2a 벽 키트. 비어 있거나 불완전하면 그레이박스 큐브")]
        [SerializeField] private Data.InteriorKit _kit;
        [Tooltip("키트가 없을 때 쓰는 그레이박스 재질")]
        [SerializeField] private Material _material;
        [Tooltip("들어가 있는 동안 카메라가 쓰는 렌더러 번호 (파이프라인 에셋 목록, SSAO 강한 내부 전용). -1이면 바꾸지 않음")]
        [SerializeField] private int _interiorRenderer = -1;
        [Tooltip("11-3 모듈별 내부 템플릿 (없는 모듈은 벽 키트 대체 방)")]
        [SerializeField] private List<Data.InteriorTemplate> _templates = new List<Data.InteriorTemplate>();
        [SerializeField] private float _interactDistance = 2.6f;

        [Header("11-10 상태 연출")]
        [SerializeField] private InteriorMoodTuning _mood = new InteriorMoodTuning();
        [Tooltip("파손 방 연기 (URP 파티클, 알파 블렌드)")]
        [SerializeField] private Material _smokeMaterial;
        [Tooltip("파손 방 스파크 (URP 파티클, 가산)")]
        [SerializeField] private Material _sparkMaterial;
        [Tooltip("경광등 몸체 (발광 Lit, 색은 MaterialPropertyBlock)")]
        [SerializeField] private Material _beaconMaterial;

        [Header("11-11 주민")]
        [SerializeField] private InteriorResidentTuning _residentTuning = new InteriorResidentTuning();
        [Tooltip("동물 주민 모델 묶음 (11-11d — 메뉴 SpaceStation/Interior/Wire Animal Residents)")]
        [SerializeField] private AnimalModelSet _animalSet;

        [Header("11-12 휴대 패드")]
        [SerializeField] private InteriorPadTuning _padTuning = new InteriorPadTuning();

        [Header("11-17 현장 긴급 수리")]
        [SerializeField] private InteriorFieldRepair.Settings _fieldRepairSettings = new InteriorFieldRepair.Settings();
        [Header("11-17 보급품 줍기")]
        [SerializeField] private InteriorSupply.Settings _supplySettings = new InteriorSupply.Settings();
        [Header("11-17 식물 (주민 요청)")]
        [SerializeField] private InteriorPlants.Settings _plantSettings = new InteriorPlants.Settings();
        [Header("11-17 주민 요청")]
        [SerializeField] private InteriorRequests.Settings _requestSettings = new InteriorRequests.Settings();
        [Header("11-17 내부 사고")]
        [SerializeField] private InteriorIncidents.Settings _incidentSettings = new InteriorIncidents.Settings();

        [Header("HUD")]
        [SerializeField] private RectTransform _hud;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [Tooltip("11-13 홀로그램 테마 아트 (메뉴 SpaceStation/Interior/Wire Pad가 연결)")]
        [SerializeField] private HoloArt _holoArt;
        [Tooltip("들어가 있는 동안 항상 숨김 (조작·세계 좌표 표시)")]
        [SerializeField] private string[] _hiddenPanels =
            { "BuildMenu", "BuildTabs", "SelectionActions", "Tooltip", "Tutorial", "ResearchPanel", "RosterPanel", "DamageMarkers", "AdjacencyMarkers", "EarlyWarning",
              "StatusBar" }; // 11-16 피드백: 바깥 건설 · 선택 안내(상태 표시줄)는 내부에서 숨김 (패드를 들면 패드 안 안내만)
        /// <summary>12-3 합성 · 12-3c 정책 창 (나중에 생긴 HUD — 씬에 저장된 _hiddenPanels에 없어 내부에서도 보였음).</summary>
        private static readonly string[] AlwaysHidden = { "RecipePanel", "PolicyPanel" };
        [Tooltip("일시정지로 들어갔을 때만 숨김 (시간이 흐르면 보임)")]
        [SerializeField] private string[] _pausedHiddenPanels = { "TimeControls", "ResourcePanel" };

        private InteriorBuilder _builder;
        private InteriorLayout _layout;
        private Transform _root;
        private FirstPersonController _player;
        private ModuleInstance _currentModule;
        private bool _inside;
        private bool _pausedByUs;
        private bool _wasPaused;
        private bool _rebuildPending;
        private bool _busy;

        // 복원용
        private Transform _cameraParent;
        private Vector3 _cameraPosition;
        private Quaternion _cameraRotation;
        private float _cameraNear;
        private float _cameraFar;
        private bool _sunEnabled;
        private OrbitCameraController _orbit;
        private InteriorExteriorView _exteriorView;
        private InteriorAtmosphere _atmosphere;
        private InteriorResidents _residents;
        private InteriorPad _pad;
        private InteriorFieldRepair _fieldRepair;
        private InteriorSupply _supply;
        private InteriorPlants _plants;
        private InteriorRequests _requests;
        private InteriorIncidents _incidents;
        // 11-17 길게 누르는 상호작용
        private InteriorInteractable _holdTarget;
        private float _holdTime;
        private bool _holdNeedsRelease;
        private RectTransform _holdBar;
        private Image _holdFill;
        private TMP_Text _notice;
        private float _noticeUntil;
        private readonly InteriorAudio _audio = new InteriorAudio(); // 11-9 방 환경음·발소리
        private readonly List<GameObject> _hiddenNow = new List<GameObject>();

        // 오버레이
        private RectTransform _overlay;
        private TMP_Text _title;
        private TMP_Text _tracker;
        private float _trackerNext;
        private float _tutorialRetry;
        private TMP_Text _prompt;
        private TMP_Text _hint;
        private CanvasGroup _fade;
        private TMP_Text _enterLabel;

        public bool IsInside => _inside;

        /// <summary>들어가기 실패 사유·강제로 나온 이유 (상태 표시줄 알림용).</summary>
        public event Action<string> Notice;
        public event Action<bool> InsideChanged;
        /// <summary>11-18: 들어가기 · 나오기 페이드가 끝나 다시 조작할 수 있음 (선택 패널의 [들어가기] 갱신용).</summary>
        public event Action Settled;

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;
            var rootGo = new GameObject("InteriorRoot");
            _root = rootGo.transform;
            _root.position = _origin;
            _builder = new InteriorBuilder(_root, _kit, _material, _templates);
        }

        private void Start()
        {
            BuildOverlay();
            _station.Grid.ModulePlaced += HandleGridChanged;
            _station.Grid.ModuleRemoved += HandleGridChanged;
            _station.Grid.ModuleMoved += HandleModuleMoved;
            _station.Connectivity.ActiveStateChanged += HandleActiveChanged;
            _station.Simulation.Damage.Damaged += HandleDamage;
            _station.Simulation.Damage.Repaired += HandleRepaired;
        }

        private void OnDestroy()
        {
            KeyBindings.Changed -= RefreshHint;
            if (_inside)
            {
                InputGate.Blocked = false;
                InputGate.SpeedCycleTaken = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (_station != null && _station.Simulation != null)
            {
                _station.Grid.ModulePlaced -= HandleGridChanged;
                _station.Grid.ModuleRemoved -= HandleGridChanged;
                _station.Grid.ModuleMoved -= HandleModuleMoved;
                _station.Connectivity.ActiveStateChanged -= HandleActiveChanged;
                _station.Simulation.Damage.Damaged -= HandleDamage;
                _station.Simulation.Damage.Repaired -= HandleRepaired;
            }
        }

        /// <summary>들어갈 수 있는지 (실패 사유 없으면 null).</summary>
        public string CheckEnter(ModuleInstance module)
        {
            if (module == null)
                return "모듈을 선택하세요";
            if (_inside || _busy || SceneFader.Busy)
                return "지금은 들어갈 수 없습니다";
            if (_clock.InputLocked)
                return "지금은 들어갈 수 없습니다";
            return null;
        }

        public void Enter(ModuleInstance module)
        {
            string reason = CheckEnter(module);
            if (reason != null)
            {
                Notice?.Invoke(reason);
                return;
            }
            StartCoroutine(EnterRoutine(module));
        }

        private IEnumerator EnterRoutine(ModuleInstance module)
        {
            _busy = true;
            InputGate.Blocked = true; // 페이드 중에도 조작 막음
            AudioService.TryPlay(l => l.UiOpen);
            if (_enterLabel != null)
                _enterLabel.gameObject.SetActive(true);
            yield return Fade(0f, 1f, 0.2f);
            yield return null; // 안내 글자가 그려진 뒤 내부 짓기 (큰 정거장은 여기서 몇 초)

            _inside = true;
            _selection.Select(null);
            if (GameSettings.InteriorPause)
            {
                _pausedByUs = true;
                _wasPaused = _clock.Clock.IsPaused;
                _clock.Clock.SetPaused(true);
                _clock.InputLocked = true;
            }

            var playerGo = new GameObject("InteriorPlayer", typeof(CharacterController));
            playerGo.transform.SetParent(transform, true); // 내부 루트 밖 (다시 만들 때 지워지지 않게)
            playerGo.layer = 2; // Ignore Raycast: 내려다볼 때 시선 광선이 자기 몸에 맞지 않게
            _player = playerGo.AddComponent<FirstPersonController>();
            _audio.Attach(_player);

            _layout = InteriorLayout.Build(_station.Grid, _station.Connectivity.IsActive, module);
            _builder.Build(_layout, _player.transform);
            RefreshAllDim();
            _atmosphere = InteriorAtmosphere.Create(transform, _station, _builder, _mood, _smokeMaterial, _sparkMaterial, _beaconMaterial);
            _atmosphere.Rebuild(_layout);
            _residents = InteriorResidents.Create(transform, _station, _builder, _residentTuning, _animalSet, _font, _player.Eye);
            _residents.Rebuild(_layout);
            _fieldRepair = InteriorFieldRepair.Create(transform, _station, _builder, _fieldRepairSettings, _smokeMaterial, _sparkMaterial);
            _fieldRepair.Message += ShowNotice;
            _fieldRepair.Rebuild(_layout);
            _atmosphere.HasDamagePoints = _fieldRepair.HasPoints;
            _supply = InteriorSupply.Create(transform, _station, _builder, _supplySettings);
            _supply.Message += ShowNotice;
            _supply.Rebuild(_layout);
            _plants = InteriorPlants.Create(transform, _builder, _plantSettings);
            _plants.Rebuild(_layout);
            _requests = InteriorRequests.Create(transform, _station, _builder, _residents, _plants, _requestSettings, _font, _player.Eye);
            _requests.Message += ShowNotice;
            _requests.Completed += HandleRequestCompleted;
            _requests.Rebuild(_layout);
            _incidents = InteriorIncidents.Create(transform, _station, _builder, _incidentSettings, _font, _player.Eye, _smokeMaterial, _sparkMaterial);
            _incidents.Message += ShowNotice;
            _incidents.Resolved += HandleIncidentResolved;
            _incidents.Rebuild(_layout);
            _requests.TutorialHold = _incidents.TutorialHold = Tutorial != null;
            var spawn = _builder.SpawnPoint(module, out float spawnYaw);
            _player.Teleport(spawn + Vector3.up * 0.05f, spawnYaw);
            UpdateNearbyRooms(module, force: true);

            _orbit = _camera.GetComponent<OrbitCameraController>();
            if (_orbit != null)
                _orbit.enabled = false;
            var cam = _camera.transform;
            _cameraParent = cam.parent;
            _cameraPosition = cam.position;
            _cameraRotation = cam.rotation;
            _cameraNear = _camera.nearClipPlane;
            _cameraFar = _camera.farClipPlane;
            cam.SetParent(_player.Eye, false);
            cam.localPosition = Vector3.zero;
            cam.localRotation = Quaternion.identity;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = InteriorFarClip;
            if (_interiorRenderer >= 0)
                _camera.GetUniversalAdditionalCameraData().SetRenderer(_interiorRenderer);

            if (_sun != null)
            {
                _sunEnabled = _sun.enabled;
                _sun.enabled = false;
            }
            _exteriorView = InteriorExteriorView.Create(_camera, _root, _station, _sun, ExteriorViewFarClip,
                p => _layout != null && _layout.TryGetRoom(_builder.WorldToCell(p), out var r) ? r.Module : null,
                _builder.CollectLinkedRooms);
            _exteriorView.transform.SetParent(transform, false);
            _exteriorView.CollectWindows();
            HidePanels(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _currentModule = null;
            InputGate.SpeedCycleTaken = true; // 배속 순환 키 = 패드 화면 확대
            _pad = InteriorPad.Create(transform, new InteriorPad.Context
            {
                Station = _station,
                Selection = _selection,
                Actions = _hud != null ? _hud.GetComponentInChildren<SelectionActionsPanel>(true) : null,
                Main = _camera,
                Player = _player,
                Builder = _builder,
                Origin = _root.position,
                Layout = () => _layout,
                CurrentRoom = RoomAtPlayer,
                Travel = TravelTo,
                Font = _font,
                Fill = _fillSprite,
                Art = _holoArt,
                Tuning = _padTuning,
                Buildable = _build != null ? _build.BuildableModules : null,
                RequestCount = m => _requests != null ? _requests.CountIn(m) : 0,
                IncidentCount = m => _incidents != null ? _incidents.CountIn(m) : 0,
            });
            InsideChanged?.Invoke(true);
            _tutorialRetry = 0f;
            _station.Simulation.Tutorial?.ReportInside(true); // 11-18 들어가기 단계 완료 (끝난 튜토리얼도 위치는 알림 — 완료 카드 표시용)

            if (_enterLabel != null)
                _enterLabel.gameObject.SetActive(false);
            yield return Fade(1f, 0f, 0.3f);
            _busy = false;
        }

        /// <summary>11-18 진행 중인 튜토리얼 (없거나 끝났으면 null).</summary>
        private TutorialRunner Tutorial
        {
            get
            {
                var t = _station != null && _station.Simulation != null ? _station.Simulation.Tutorial : null;
                return t != null && t.Active ? t : null;
            }
        }

        private void HandleRequestCompleted() => Tutorial?.ReportInterior(TutorialGoal.InteriorRequest);

        private void HandleIncidentResolved() => Tutorial?.ReportInterior(TutorialGoal.InteriorIncident);

        /// <summary>
        /// 11-18 내부 튜토리얼: 저절로 생기는 부탁 · 사고는 쉬고, 단계에 맞춰 하나씩 만든다 (못 만들면 잠시 뒤 다시).
        /// 패드 단계는 패드를 들면 완료.
        /// </summary>
        private void DriveTutorial()
        {
            var tutorial = Tutorial;
            bool hold = tutorial != null;
            if (_requests != null)
                _requests.TutorialHold = hold;
            if (_incidents != null)
                _incidents.TutorialHold = hold;
            var step = tutorial?.Current;
            if (step == null)
                return;
            switch (step.Goal)
            {
                case TutorialGoal.InteriorPad:
                    if (_pad != null && _pad.IsRaised)
                        tutorial.ReportInterior(TutorialGoal.InteriorPad);
                    break;
                case TutorialGoal.InteriorRequest:
                    if (_requests != null && _requests.ActiveCount == 0 && Time.unscaledTime >= _tutorialRetry)
                        _tutorialRetry = Time.unscaledTime + (_requests.ForceTutorialRequest() ? 0f : 2f);
                    break;
                case TutorialGoal.InteriorIncident:
                    if (_incidents != null && !_incidents.Active && Time.unscaledTime >= _tutorialRetry)
                        _tutorialRetry = Time.unscaledTime + (_incidents.ForceTutorialIncident() ? 0f : 2f);
                    break;
            }
        }

        public void Exit(string notice = null)
        {
            if (!_inside || _busy)
                return;
            StartCoroutine(ExitRoutine(notice));
        }

        private IEnumerator ExitRoutine(string notice)
        {
            _busy = true;
            AudioService.TryPlay(l => l.UiClose);
            yield return Fade(0f, 1f, 0.15f);

            var lastModule = _currentModule;
            if (_incidents != null)
            {
                string missed = _incidents.LeaveInterior(); // 처리하지 않은 사고는 놓친 것으로 (손실 알림은 밖 상태 표시줄로)
                if (notice == null)
                    notice = missed;
            }
            if (_pad != null)
                Destroy(_pad.gameObject); // 카메라 쌓기 · 컬링 마스크 · 열어 둔 창을 되돌림
            _pad = null;
            InputGate.SpeedCycleTaken = false;
            var cam = _camera.transform;
            cam.SetParent(_cameraParent, false);
            cam.SetPositionAndRotation(_cameraPosition, _cameraRotation);
            _camera.nearClipPlane = _cameraNear;
            _camera.farClipPlane = _cameraFar;
            if (_interiorRenderer >= 0)
                _camera.GetUniversalAdditionalCameraData().SetRenderer(-1); // 기본 렌더러로
            if (_orbit != null)
                _orbit.enabled = true;
            if (_exteriorView != null)
                Destroy(_exteriorView.gameObject);
            _exteriorView = null;
            if (_sun != null)
                _sun.enabled = _sunEnabled;

            _audio.Stop();
            if (_atmosphere != null)
                Destroy(_atmosphere.gameObject);
            _atmosphere = null;
            if (_residents != null)
                Destroy(_residents.gameObject);
            _residents = null;
            if (_fieldRepair != null)
                Destroy(_fieldRepair.gameObject);
            _fieldRepair = null;
            if (_supply != null)
                Destroy(_supply.gameObject);
            _supply = null;
            if (_plants != null)
                Destroy(_plants.gameObject);
            _plants = null;
            if (_requests != null)
                Destroy(_requests.gameObject);
            _requests = null;
            if (_incidents != null)
                Destroy(_incidents.gameObject);
            _incidents = null;
            ResetHold();
            if (_player != null)
                Destroy(_player.gameObject);
            _player = null;
            _builder.Clear();
            _layout = null;
            _currentModule = null;
            _rebuildPending = false;

            HidePanels(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            InputGate.Blocked = false;
            if (_pausedByUs)
            {
                _pausedByUs = false;
                _clock.InputLocked = false;
                _clock.Clock.SetPaused(_wasPaused);
            }
            _inside = false;
            InsideChanged?.Invoke(false);
            _station.Simulation.Tutorial?.ReportInside(false);
            // 마지막에 있던 방을 선택해 둔다 (밖에서 위치 확인)
            if (lastModule != null && _station.Grid.TryGetModule(lastModule.Origin, out var still) && still == lastModule)
                _selection.Select(lastModule);
            if (notice != null)
                Notice?.Invoke(notice);

            yield return Fade(1f, 0f, 0.25f);
            _busy = false;
            Settled?.Invoke();
        }

        private void Update()
        {
            if (!_inside || _busy)
                return;
            // 11-16 피드백: 가이드는 필요한 곳에만 — 패드를 들면 화면 아래 내부 이동 안내 대신 패드 안 안내만
            bool showWalkHint = _pad == null || !_pad.IsRaised;
            if (_hint != null && _hint.gameObject.activeSelf != showWalkHint)
                _hint.gameObject.SetActive(showWalkHint);
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame) // 패드 확대 중 ESC = 축소 (패드가 먼저)
            {
                InputGate.ConsumeEscape();
                Exit();
                return;
            }
            // 시간이 흐르는 중 결과 화면(게임 오버 등)이 뜨면 밖으로
            if (!_pausedByUs && _clock.InputLocked)
            {
                Exit();
                return;
            }
            if (_rebuildPending)
            {
                _rebuildPending = false;
                Rebuild();
                if (!_inside || _busy)
                    return;
            }
            UpdateRoomTitle();
            UpdateNearbyRooms(RoomAtPlayer(), force: false);
            UpdateInteraction();
            if (_atmosphere != null)
            {
                _atmosphere.CurrentRoom = _currentModule;
                _audio.SetAlarm(_atmosphere.IsAlarm(_currentModule));
            }
            if (_fieldRepair != null)
                _fieldRepair.CurrentRoom = _currentModule;
            if (_requests != null)
            {
                _requests.CurrentRoom = _currentModule;
                _requests.Suspended = _incidents != null && _incidents.Active;
            }
            if (_incidents != null)
                _incidents.CurrentRoom = _currentModule;
            DriveTutorial();
            UpdateTracker();
            if (_notice != null && _noticeUntil > 0f && Time.unscaledTime > _noticeUntil)
            {
                _noticeUntil = 0f;
                _notice.SetText(string.Empty);
            }
        }

        // ---------------- 방·해치 ----------------

        private void UpdateRoomTitle()
        {
            var cell = _builder.WorldToCell(_player.transform.position + Vector3.up * FirstPersonController.EyeHeight);
            if (!_layout.TryGetRoom(cell, out var room) || room.Module == _currentModule)
                return;
            _currentModule = room.Module;
            _audio.SetRoom(_builder.AmbienceFor(room.Module));
            string name =room.Module.Data != null ? room.Module.Data.DisplayName : room.Module.ToString();
            string state = !_station.Connectivity.IsActive(room.Module) ? $"  <color={HudText.Orange}>비활성</color>"
                : _station.Simulation.Damage.TryGetInfo(room.Module, out _) ? $"  <color={HudText.Red}>파손</color>" : "";
            _title.SetText($"<b>{name}</b>{state}\n<size=75%><color={HudText.Muted}>방 {_layout.Rooms.Count}개 연결</color></size>");
        }

        /// <summary>
        /// 11-18: 진행 중인 사고 · 부탁을 방 이름 아래에 계속 표시 (알림은 3.5초만 떠서, 그 방에 가거나 패드를 열기 전엔 화면에 안 남았음).
        /// 사고가 있으면 사고(남은 시간)만, 없으면 첫 부탁 + 나머지 수. 0.25초마다.
        /// </summary>
        private void UpdateTracker()
        {
            if (_tracker == null || Time.unscaledTime < _trackerNext)
                return;
            _trackerNext = Time.unscaledTime + 0.25f;
            string text = string.Empty;
            if (_incidents != null && _incidents.TryGetActive(out var incident, out var incidentRoom, out int left))
            {
                string time = left >= 0 ? $" · <color={(left <= 20 ? HudText.Red : HudText.Orange)}>{left}초</color>" : "";
                text = $"{HudTheme.Icon("warning")} <color={HudText.Orange}><b>{incident}</b></color> · {Where(incidentRoom)}{time}";
            }
            else if (_requests != null && _requests.TryGetPending(out var request, out var requestRoom))
            {
                int more = _requests.ActiveCount - 1;
                text = $"<color={HudTheme.AccentHex}><b>부탁</b></color> {request} · {Where(requestRoom)}" +
                       (more > 0 ? $" <color={HudText.Muted}>외 {more}건</color>" : "");
            }
            _tracker.SetText(text);
        }

        /// <summary>지금 방이면 "이 방", 아니면 방 이름 + 패드 안내.</summary>
        private string Where(ModuleInstance room)
        {
            if (room == _currentModule)
                return "이 방";
            string name = room != null && room.Data != null ? room.Data.DisplayName : "다른 방";
            return $"{name} <color={HudText.Muted}>({KeyBindings.Label(GameAction.Pad)} 패드 지도)</color>";
        }

        /// <summary>
        /// 시선이 닿은 물건: 해치(F 한 번) · 11-17 <see cref="InteriorInteractable"/>(F 길게 — 진행 막대, 다 차면 실행, 손을 뗐다 다시 눌러야 다음).
        /// </summary>
        private void UpdateInteraction()
        {
            if (_pad != null && _pad.IsZoomed)
            {
                _prompt.SetText(string.Empty);
                ResetHold();
                return;
            }
            var eye = _player.Eye;
            InteriorHatch hatch = null;
            InteriorInteractable item = null;
            if (Physics.Raycast(eye.position, eye.forward, out var hit, _interactDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                hatch = hit.collider.GetComponent<InteriorHatch>();
                if (hatch == null)
                    item = hit.collider.GetComponentInParent<InteriorInteractable>();
            }
            if (item != null)
            {
                UpdateHold(item);
                return;
            }
            ResetHold();
            if (hatch == null)
            {
                _prompt.SetText(string.Empty);
                return;
            }
            string target = _layout.TryGetRoom(hatch.ToCell, out var room) && room.Module.Data != null ? room.Module.Data.DisplayName : "";
            _prompt.SetText($"<b>{KeyBindings.Label(GameAction.Interact)}</b>  {(hatch.Up ? "위층으로" : "아래층으로")} <color={HudText.Muted}>{target}</color>");
            if (KeyBindings.WasPressed(GameAction.Interact))
                StartCoroutine(UseHatch(hatch.Arrival ?? _builder.FloorPoint(hatch.ToCell)));
        }

        private void UpdateHold(InteriorInteractable item)
        {
            if (item != _holdTarget)
            {
                _holdTarget = item;
                _holdTime = 0f;
            }
            string key = KeyBindings.Label(GameAction.Interact);
            string blocked = item.Blocked != null ? item.Blocked() : null;
            string text = item.Prompt != null ? item.Prompt() : "";
            bool pressed = KeyBindings.IsPressed(GameAction.Interact);
            if (!pressed)
                _holdNeedsRelease = false;
            if (item.Completed == null)
            {
                _prompt.SetText(text); // 안내만 (11-17 ③)
                _holdTime = 0f;
            }
            else if (blocked != null)
            {
                _prompt.SetText($"<b>{key}</b>  {text}\n<size=80%><color={HudText.Red}>{blocked}</color></size>");
                if (KeyBindings.WasPressed(GameAction.Interact))
                    AudioService.TryPlay(l => l.UiError);
                _holdTime = 0f;
            }
            else
            {
                _prompt.SetText($"<b>{key}</b> <size=80%><color={HudText.Muted}>길게</color></size>  {text}");
                if (pressed && !_holdNeedsRelease)
                {
                    _holdTime += Time.unscaledDeltaTime;
                    float progress = Mathf.Clamp01(_holdTime / Mathf.Max(0.05f, item.HoldSeconds));
                    item.Holding?.Invoke(progress);
                    if (progress >= 1f)
                    {
                        _holdTime = 0f;
                        _holdNeedsRelease = true;
                        item.Completed?.Invoke();
                    }
                }
                else
                    _holdTime = 0f;
            }
            SetHoldBar(_holdTime > 0f ? Mathf.Clamp01(_holdTime / Mathf.Max(0.05f, item.HoldSeconds)) : -1f);
        }

        private void ResetHold()
        {
            _holdTarget = null;
            _holdTime = 0f;
            SetHoldBar(-1f);
        }

        /// <summary>진행 막대 (음수 = 숨김).</summary>
        private void SetHoldBar(float progress)
        {
            if (_holdBar == null)
                return;
            bool on = progress >= 0f;
            if (_holdBar.gameObject.activeSelf != on)
                _holdBar.gameObject.SetActive(on);
            if (on)
                _holdFill.fillAmount = progress;
        }

        private void ShowNotice(string text, bool failed)
        {
            if (_notice == null)
                return;
            _notice.SetText(text);
            _notice.color = failed ? HudTheme.Negative : HoloUi.TextColor;
            _noticeUntil = Time.unscaledTime + 3.5f;
        }

        /// <param name="arrival">맞은편 해치 아래 바닥점 (월드).</param>
        private IEnumerator UseHatch(Vector3 arrival)
        {
            _busy = true;
            _prompt.SetText(string.Empty);
            yield return Fade(0f, 1f, 0.15f);
            // 해치 뚜껑(칸 가운데) 옆, 지금 바라보는 쪽으로 한 걸음
            var forward = _player.transform.forward;
            forward.y = 0f;
            var offset = forward.sqrMagnitude > 0.01f ? forward.normalized * 1.1f : Vector3.forward * 1.1f;
            _player.Teleport(arrival + offset + Vector3.up * 0.05f, _player.transform.eulerAngles.y);
            yield return Fade(1f, 0f, 0.2f);
            _busy = false;
        }

        // ---------------- 11-12 휴대 패드 ----------------

        /// <summary>지금 서 있는 방 (눈 높이 칸).</summary>
        private ModuleInstance RoomAtPlayer()
        {
            if (_player == null || _layout == null)
                return null;
            var cell = _builder.WorldToCell(_player.transform.position + Vector3.up * FirstPersonController.EyeHeight);
            return _layout.TryGetRoom(cell, out var room) ? room.Module : null;
        }

        /// <summary>빠른 이동: 연결된 방의 들어오는 자리로 (페이드).</summary>
        private void TravelTo(ModuleInstance module)
        {
            if (!_inside || _busy || module == null || _layout == null || !_layout.Contains(module))
                return;
            StartCoroutine(TravelRoutine(module));
        }

        private IEnumerator TravelRoutine(ModuleInstance module)
        {
            _busy = true;
            _prompt.SetText(string.Empty);
            AudioService.TryPlay(l => l.UiOpen, 0.5f);
            yield return Fade(0f, 1f, 0.2f);
            var spawn = _builder.SpawnPoint(module, out float yaw);
            _player.Teleport(spawn + Vector3.up * 0.05f, yaw);
            _currentModule = null; // 제목 · 환경음 다시
            yield return Fade(1f, 0f, 0.25f);
            _busy = false;
        }

        private void Rebuild()
        {
            var feet = _player.transform.position;
            var cell = _builder.WorldToCell(feet + Vector3.up * FirstPersonController.EyeHeight);
            if (!_layout.TryGetRoom(cell, out var room) || !_station.Grid.TryGetModule(cell, out var module) || module != room.Module)
            {
                Exit("있던 모듈이 사라져 밖으로 나왔습니다");
                return;
            }
            _layout = InteriorLayout.Build(_station.Grid, _station.Connectivity.IsActive, module);
            _builder.Build(_layout, _player.transform);
            RefreshAllDim();
            if (_atmosphere != null)
                _atmosphere.Rebuild(_layout);
            if (_residents != null)
                _residents.Rebuild(_layout);
            if (_fieldRepair != null)
                _fieldRepair.Rebuild(_layout);
            if (_supply != null)
                _supply.Rebuild(_layout);
            if (_plants != null)
                _plants.Rebuild(_layout);
            if (_requests != null)
                _requests.Rebuild(_layout);
            if (_incidents != null)
                _incidents.Rebuild(_layout);
            ResetHold();
            if (_exteriorView != null)
                _exteriorView.CollectWindows();
            _currentModule = null; // 제목 다시
            UpdateNearbyRooms(module, force: true);
        }

        // ---------------- 12-3c-opt 근접 활성화 ----------------
        // 내부는 정거장 전체 방을 짓는다 (패드 지도 · 빠른 이동 · 사고/부탁이 모든 방을 씀). 대륙급(모듈 579)에서는 방을 모두 켜 두면
        // 조명 약 5,900 · 문 약 2,200 · 주민 400(스키닝) · 메시 약 38,700이 한꺼번에 돌아 프레임당 약 52ms (사용자 피드백 2026-10-14: 내부에서 렉).
        // → 플레이어가 있는 방에서 열린 통로로 NearbyDepth단계까지 이어진 방만 켜고 나머지 방(그 방의 조명 · 문 · 주민 · 연출)은 꺼 둔다.
        // 벽 너머는 보이지 않고 통로로 이어진 방만 문 · 튜브 너머로 보이므로 화면은 같다.
        private const int NearbyDepth = 2;
        private ModuleInstance _nearbyCenter;
        private readonly HashSet<ModuleInstance> _nearby = new HashSet<ModuleInstance>();
        private readonly HashSet<ModuleInstance> _nearbyNext = new HashSet<ModuleInstance>();
        private readonly List<ModuleInstance> _frontier = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _frontierNext = new List<ModuleInstance>();
        private readonly HashSet<ModuleInstance> _linked = new HashSet<ModuleInstance>();

        private void UpdateNearbyRooms(ModuleInstance center, bool force)
        {
            if (_layout == null || center == null || (!force && center == _nearbyCenter))
                return;
            _nearbyCenter = center;
            _nearbyNext.Clear();
            _nearbyNext.Add(center);
            _frontier.Clear();
            _frontier.Add(center);
            for (int depth = 0; depth < NearbyDepth; depth++)
            {
                _frontierNext.Clear();
                foreach (var room in _frontier)
                {
                    _builder.CollectLinkedRooms(room, _linked);
                    foreach (var n in _linked)
                    {
                        if (_nearbyNext.Add(n))
                            _frontierNext.Add(n);
                    }
                }
                _frontier.Clear();
                _frontier.AddRange(_frontierNext);
            }
            _builder.ShowOnly(_nearbyNext); // 방 + 닿은 통로 튜브(튜브 조명 약 1,300개)
            _nearby.Clear();
            _nearby.UnionWith(_nearbyNext);
        }

        private void HandleGridChanged(ModuleInstance _)
        {
            if (_inside)
                _rebuildPending = true;
        }

        private void HandleModuleMoved(ModuleInstance module, IReadOnlyList<Vector3Int> _) => HandleGridChanged(module); // 12-3b

        private void HandleActiveChanged(ModuleInstance _, bool __)
        {
            if (_inside)
                _rebuildPending = true; // 끊기면 방 범위도 바뀜
        }

        private void HandleDamage(Simulation.DamageInfo info) => RefreshDim(info.Module);
        private void HandleRepaired(ModuleInstance module) => RefreshDim(module);

        private void RefreshAllDim()
        {
            foreach (var room in _layout.Rooms)
                RefreshDim(room.Module);
        }

        private void RefreshDim(ModuleInstance module)
        {
            if (!_inside || _layout == null || !_layout.Contains(module))
                return;
            bool dim = !_station.Connectivity.IsActive(module) || _station.Simulation.Damage.TryGetInfo(module, out _);
            _builder.SetRoomDim(module, dim);
            if (module == _currentModule)
                _currentModule = null; // 제목의 상태 갱신
        }

        // ---------------- HUD ----------------

        private void HidePanels(bool hide)
        {
            if (_hud == null)
                return;
            if (!hide)
            {
                foreach (var go in _hiddenNow)
                {
                    if (go != null)
                        go.SetActive(true);
                }
                _hiddenNow.Clear();
                return;
            }
            HideList(_hiddenPanels);
            HideList(AlwaysHidden); // 12-3 · 12-3c 창 (씬에 저장된 목록에 없어도)
            if (_pausedByUs)
                HideList(_pausedHiddenPanels);
        }

        private void HideList(string[] names)
        {
            foreach (var n in names)
            {
                if (n == "Tutorial" && Tutorial != null)
                    continue; // 11-18 내부 튜토리얼 단계 안내는 안에서도 보임
                var t = _hud.Find(n);
                if (t == null || !t.gameObject.activeSelf)
                    continue;
                t.gameObject.SetActive(false);
                _hiddenNow.Add(t.gameObject);
            }
        }

        private void BuildOverlay()
        {
            var ui = new HoloUi(_font, _fillSprite, null, null);
            _overlay = HoloUi.Rect("InteriorOverlay", _hud != null ? _hud : transform);
            HoloUi.Stretch(_overlay);
            // 결과 화면·일시정지 메뉴보다 아래
            var result = _hud != null ? _hud.Find("ResultScreen") : null;
            if (result != null)
                _overlay.SetSiblingIndex(result.GetSiblingIndex());

            var dot = HoloUi.Rect("Crosshair", _overlay);
            dot.anchorMin = dot.anchorMax = new Vector2(0.5f, 0.5f);
            dot.sizeDelta = new Vector2(6f, 6f);
            var dotImg = dot.gameObject.AddComponent<Image>();
            dotImg.sprite = _fillSprite;
            dotImg.color = new Color(1f, 1f, 1f, 0.8f);
            dotImg.raycastTarget = false;

            _title = ui.Label(_overlay, "", 26f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_title.rectTransform, new Vector2(24f, -20f), new Vector2(700f, 80f));

            // 11-18 진행 중인 사고 · 부탁 (방 이름 아래)
            _tracker = ui.Label(_overlay, "", 20f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_tracker.rectTransform, new Vector2(24f, -88f), new Vector2(700f, 30f));

            _prompt = ui.Label(_overlay, "", 22f, TextAlignmentOptions.Center);
            var prt = _prompt.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, -66f);
            prt.sizeDelta = new Vector2(700f, 56f);

            // 11-17 길게 누르기 진행 막대 (조준점 바로 아래)
            _holdBar = HoloUi.Rect("HoldBar", _overlay);
            _holdBar.anchorMin = _holdBar.anchorMax = new Vector2(0.5f, 0.5f);
            _holdBar.anchoredPosition = new Vector2(0f, -26f);
            _holdBar.sizeDelta = new Vector2(120f, 6f);
            var barBack = _holdBar.gameObject.AddComponent<Image>();
            barBack.sprite = _fillSprite;
            barBack.color = new Color(0f, 0f, 0f, 0.55f);
            barBack.raycastTarget = false;
            var fillRt = HoloUi.Rect("Fill", _holdBar);
            HoloUi.Stretch(fillRt, new Vector2(1f, 1f), new Vector2(-1f, -1f));
            _holdFill = fillRt.gameObject.AddComponent<Image>();
            _holdFill.sprite = _fillSprite;
            _holdFill.type = Image.Type.Filled;
            _holdFill.fillMethod = Image.FillMethod.Horizontal;
            _holdFill.color = HudTheme.Accent;
            _holdFill.raycastTarget = false;
            _holdBar.gameObject.SetActive(false);

            // 11-17 알림 (현장 수리 완료 · 자원 부족 등)
            _notice = ui.Label(_overlay, "", 20f, TextAlignmentOptions.Center);
            var nrt = _notice.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.5f);
            nrt.anchoredPosition = new Vector2(0f, 120f);
            nrt.sizeDelta = new Vector2(700f, 34f);

            var hint = ui.Label(_overlay, "", 18f, TextAlignmentOptions.Bottom);
            var hrt = hint.rectTransform;
            hrt.anchorMin = new Vector2(0f, 0f);
            hrt.anchorMax = new Vector2(1f, 0f);
            hrt.pivot = new Vector2(0.5f, 0f);
            hrt.anchoredPosition = new Vector2(0f, 20f);
            hrt.sizeDelta = new Vector2(0f, 30f);
            hint.color = HoloUi.MutedColor;
            _hint = hint;
            KeyBindings.Changed += RefreshHint;
            RefreshHint();

            var fade = HoloUi.Rect("Fade", _overlay);
            HoloUi.Stretch(fade);
            var black = fade.gameObject.AddComponent<Image>();
            black.color = Color.black;
            black.raycastTarget = false;
            _fade = fade.gameObject.AddComponent<CanvasGroup>();
            _fade.alpha = 0f;
            _fade.blocksRaycasts = false;
            // 12-3c-opt: 큰 정거장은 내부를 짓는 동안 화면이 검게 1~3초 멈춤 → 안내 글자 (사용자 피드백 2026-10-14)
            _enterLabel = ui.Label(fade, "내부로 들어가는 중...", 26f, TextAlignmentOptions.Center);
            HoloUi.Stretch(_enterLabel.rectTransform);
            _enterLabel.color = new Color(0.75f, 0.88f, 1f);
            _enterLabel.gameObject.SetActive(false); // 들어갈 때만 (나갈 때 · 빠른 이동 페이드에는 없음)
            // 들어갈 때 페이드가 보이도록 오버레이 자체는 켜 두고 내용만 숨김
            _overlay.gameObject.SetActive(true);
            SetContentVisible(false);
            InsideChanged += SetContentVisible;
        }

        private void SetContentVisible(bool on)
        {
            foreach (Transform child in _overlay)
            {
                if (child != _fade.transform)
                    child.gameObject.SetActive(on);
            }
        }

        private void RefreshHint()
        {
            var hint = _hint;
            if (hint == null)
                return;
            string move = $"{KeyBindings.Label(GameAction.CameraForward)}{KeyBindings.Label(GameAction.CameraLeft)}{KeyBindings.Label(GameAction.CameraBack)}{KeyBindings.Label(GameAction.CameraRight)}";
            hint.SetText($"{move} 이동 · 마우스 둘러보기 · Shift 달리기 · {KeyBindings.Label(GameAction.Interact)} 해치 · 작업 · {KeyBindings.Label(GameAction.Pad)} 패드 · ESC 나가기");
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / seconds);
                _fade.alpha = Mathf.Lerp(from, to, t);
                yield return null;
            }
        }
    }
}
