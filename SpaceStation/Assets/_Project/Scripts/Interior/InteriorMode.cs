using System;
using System.Collections;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Settings;
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
        [Tooltip("내부를 만드는 위치 (외부 정거장이 카메라 시야 거리 밖에 있도록 멀리)")]
        [SerializeField] private Vector3 _origin = new Vector3(0f, -5000f, 0f);
        [Tooltip("11-2a 벽 키트. 비어 있거나 불완전하면 그레이박스 큐브")]
        [SerializeField] private Data.InteriorKit _kit;
        [Tooltip("키트가 없을 때 쓰는 그레이박스 재질")]
        [SerializeField] private Material _material;
        [Tooltip("들어가 있는 동안 카메라가 쓰는 렌더러 번호 (파이프라인 에셋 목록, SSAO 강한 내부 전용). -1이면 바꾸지 않음")]
        [SerializeField] private int _interiorRenderer = -1;
        [Tooltip("11-3 모듈별 내부 템플릿 (없는 모듈은 벽 키트 대체 방)")]
        [SerializeField] private List<Data.InteriorTemplate> _templates = new List<Data.InteriorTemplate>();
        [SerializeField] private float _interactDistance = 2.6f;

        [Header("HUD")]
        [SerializeField] private RectTransform _hud;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [Tooltip("들어가 있는 동안 항상 숨김 (조작·세계 좌표 표시)")]
        [SerializeField] private string[] _hiddenPanels =
            { "BuildMenu", "BuildTabs", "SelectionActions", "Tooltip", "Tutorial", "ResearchPanel", "RosterPanel", "DamageMarkers", "AdjacencyMarkers", "EarlyWarning" };
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
        private bool _sunEnabled;
        private OrbitCameraController _orbit;
        private InteriorExteriorView _exteriorView;
        private readonly List<GameObject> _hiddenNow = new List<GameObject>();

        // 오버레이
        private RectTransform _overlay;
        private TMP_Text _title;
        private TMP_Text _prompt;
        private TMP_Text _hint;
        private CanvasGroup _fade;

        public bool IsInside => _inside;

        /// <summary>들어가기 실패 사유·강제로 나온 이유 (상태 표시줄 알림용).</summary>
        public event Action<string> Notice;
        public event Action<bool> InsideChanged;

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
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            if (_station != null && _station.Simulation != null)
            {
                _station.Grid.ModulePlaced -= HandleGridChanged;
                _station.Grid.ModuleRemoved -= HandleGridChanged;
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
            yield return Fade(0f, 1f, 0.2f);

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

            _layout = InteriorLayout.Build(_station.Grid, _station.Connectivity.IsActive, module);
            _builder.Build(_layout, _player.transform);
            RefreshAllDim();
            var spawn = _builder.SpawnPoint(module, out float spawnYaw);
            _player.Teleport(spawn + Vector3.up * 0.05f, spawnYaw);

            _orbit = _camera.GetComponent<OrbitCameraController>();
            if (_orbit != null)
                _orbit.enabled = false;
            var cam = _camera.transform;
            _cameraParent = cam.parent;
            _cameraPosition = cam.position;
            _cameraRotation = cam.rotation;
            _cameraNear = _camera.nearClipPlane;
            cam.SetParent(_player.Eye, false);
            cam.localPosition = Vector3.zero;
            cam.localRotation = Quaternion.identity;
            _camera.nearClipPlane = 0.05f;
            if (_interiorRenderer >= 0)
                _camera.GetUniversalAdditionalCameraData().SetRenderer(_interiorRenderer);

            if (_sun != null)
            {
                _sunEnabled = _sun.enabled;
                _sun.enabled = false;
            }
            _exteriorView = InteriorExteriorView.Create(_camera, _root, _station, _sun, _camera.farClipPlane,
                p => _layout != null && _layout.TryGetRoom(_builder.WorldToCell(p), out var r) ? r.Module : null,
                _builder.CollectLinkedRooms);
            _exteriorView.transform.SetParent(transform, false);
            _exteriorView.CollectWindows();
            HidePanels(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _currentModule = null;
            InsideChanged?.Invoke(true);

            yield return Fade(1f, 0f, 0.3f);
            _busy = false;
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
            var cam = _camera.transform;
            cam.SetParent(_cameraParent, false);
            cam.SetPositionAndRotation(_cameraPosition, _cameraRotation);
            _camera.nearClipPlane = _cameraNear;
            if (_interiorRenderer >= 0)
                _camera.GetUniversalAdditionalCameraData().SetRenderer(-1); // 기본 렌더러로
            if (_orbit != null)
                _orbit.enabled = true;
            if (_exteriorView != null)
                Destroy(_exteriorView.gameObject);
            _exteriorView = null;
            if (_sun != null)
                _sun.enabled = _sunEnabled;

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
            // 마지막에 있던 방을 선택해 둔다 (밖에서 위치 확인)
            if (lastModule != null && _station.Grid.TryGetModule(lastModule.Origin, out var still) && still == lastModule)
                _selection.Select(lastModule);
            if (notice != null)
                Notice?.Invoke(notice);

            yield return Fade(1f, 0f, 0.25f);
            _busy = false;
        }

        private void Update()
        {
            if (!_inside || _busy)
                return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
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
            UpdateHatchPrompt();
        }

        // ---------------- 방·해치 ----------------

        private void UpdateRoomTitle()
        {
            var cell = _builder.WorldToCell(_player.transform.position + Vector3.up * FirstPersonController.EyeHeight);
            if (!_layout.TryGetRoom(cell, out var room) || room.Module == _currentModule)
                return;
            _currentModule = room.Module;
            string name = room.Module.Data != null ? room.Module.Data.DisplayName : room.Module.ToString();
            string state = !_station.Connectivity.IsActive(room.Module) ? $"  <color={HudText.Orange}>비활성</color>"
                : _station.Simulation.Damage.TryGetInfo(room.Module, out _) ? $"  <color={HudText.Red}>파손</color>" : "";
            _title.SetText($"<b>{name}</b>{state}\n<size=75%><color={HudText.Muted}>방 {_layout.Rooms.Count}개 연결</color></size>");
        }

        private void UpdateHatchPrompt()
        {
            var eye = _player.Eye;
            InteriorHatch hatch = null;
            if (Physics.Raycast(eye.position, eye.forward, out var hit, _interactDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                hatch = hit.collider.GetComponent<InteriorHatch>();
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
            if (_exteriorView != null)
                _exteriorView.CollectWindows();
            _currentModule = null; // 제목 다시
        }

        private void HandleGridChanged(ModuleInstance _)
        {
            if (_inside)
                _rebuildPending = true;
        }

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
            if (_pausedByUs)
                HideList(_pausedHiddenPanels);
        }

        private void HideList(string[] names)
        {
            foreach (var n in names)
            {
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

            _prompt = ui.Label(_overlay, "", 22f, TextAlignmentOptions.Center);
            var prt = _prompt.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, -60f);
            prt.sizeDelta = new Vector2(600f, 40f);

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
            hint.SetText($"{move} 이동 · 마우스 둘러보기 · Shift 달리기 · {KeyBindings.Label(GameAction.Interact)} 해치 · ESC 나가기");
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
