using System;
using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>11-12 휴대 패드 연출 수치 (InteriorMode 인스펙터).</summary>
    [Serializable]
    public sealed class InteriorPadTuning
    {
        [Tooltip("패드 모델 (Blender BlenderWork/Pad/pad_builder.py → SM_InteriorPad.fbx, 화면이 −z = 눈 쪽)")]
        public GameObject Model;
        [Tooltip("모델 화면 크기 (m) · 화면 앞면의 z (pad_builder.py SCREEN_W · SCREEN_H · −SCREEN_Y)")]
        public Vector2 ScreenSize = new Vector2(0.214f, 0.134f);
        public float ScreenZ = -0.0095f;
        [Tooltip("눈 기준 위치 · 회전: 든 자세 (화면 아래 오른쪽, 위쪽이 눈 쪽으로 기울어짐)")]
        public Vector3 HeldPosition = new Vector3(0.075f, -0.09f, 0.25f);   // 위 가장자리까지 0.3m 안 — 벽(몸 충돌 반지름 0.3m)에 묻히지 않음
        public Vector3 HeldEuler = new Vector3(24f, -8f, 3f);
        [Tooltip("내린 자세 (화면 밖 아래)")]
        public Vector3 LoweredPosition = new Vector3(0.1f, -0.45f, 0.22f);
        public Vector3 LoweredEuler = new Vector3(60f, -10f, 5f);
        [Tooltip("확대 자세 (화면 가운데, 패드 세로가 시야의 약 80%)")]
        public Vector3 ZoomPosition = new Vector3(0f, 0f, 0.19f);
        public Vector3 ZoomEuler = Vector3.zero;
        [Tooltip("들기 · 확대 걸리는 시간 (초)")]
        public float RaiseSeconds = 0.28f;
        public float ZoomSeconds = 0.22f;
        [Tooltip("미니맵 칸 크기 (패드 화면 픽셀)")]
        public float CellPixels = 30f;

        [Header("미니맵 색")]
        public Color Core = new Color(0.31f, 0.85f, 1f);
        public Color Power = new Color(0.95f, 0.8f, 0.32f);
        public Color Life = new Color(0.42f, 0.86f, 0.52f);
        public Color Industry = new Color(0.95f, 0.6f, 0.3f);
        public Color Defense = new Color(0.92f, 0.42f, 0.48f);
        public Color Inactive = new Color(0.38f, 0.4f, 0.44f);
        public Color Damaged = new Color(1f, 0.28f, 0.24f);
        [Tooltip("지금 내부에 없는 방 (갈 수 없음)의 밝기")]
        [Range(0f, 1f)] public float Unreachable = 0.45f;
    }

    /// <summary>
    /// 11-12 휴대 패드: M으로 손에 든 패드를 올리고 내림 (든 채 걷기 · 둘러보기 자유), 든 상태에서 배속 순환 키(기본 Tab)로 화면을 확대해 조작
    /// (커서 풀림 · 걷기 멈춤), 다시 Tab · ESC로 축소. 화면 = 층별 위에서 본 칸 지도(<see cref="PadMap"/>) + 고른 모듈 정보 · 수리 · 정비 · 재건축
    /// (<see cref="SelectionActionsPanel.Describe"/> 공용) + 빠른 이동 + 연구 · 명단 창.
    /// 패드는 본 카메라로 그림 — URP 겹침 카메라는 월드 캔버스 UI를 그리지 않았음. 든 자세는 눈에서 0.3m 안(몸 충돌 반지름)이라 벽에 묻히지 않음. ESC는 내부 나가기(<see cref="InteriorMode"/>)보다 먼저.
    /// </summary>
    [DefaultExecutionOrder(-310)]
    public sealed class InteriorPad : MonoBehaviour
    {
        private const float CanvasWidth = 856f;

        public sealed class Context
        {
            public StationController Station;
            public ModuleSelectionController Selection;
            public SelectionActionsPanel Actions;
            public Camera Main;
            public FirstPersonController Player;
            public InteriorBuilder Builder;
            public Vector3 Origin;
            public Func<InteriorLayout> Layout;
            public Func<ModuleInstance> CurrentRoom;
            public Action<ModuleInstance> Travel;
            /// <summary>"ResearchPanel" · "RosterPanel" 창을 열고 닫음 (true = 열기). 지금 열려 있는지 반환.</summary>
            public Func<string, bool?, bool> Window;
            public TMP_FontAsset Font;
            public Sprite Fill;
            public InteriorPadTuning Tuning;
        }

        private Context _c;
        private InteriorPadTuning _t;
        private Transform _pad;
        private float _raise;    // 0 내림 · 1 듦
        private float _zoom;     // 0 든 자세 · 1 확대
        private bool _wantRaise;
        private bool _wantZoom;
        private string _window;  // 열어 둔 창 이름

        // UI
        private CanvasGroup _group;
        private TMP_Text _header;
        private TMP_Text _floorLabel;
        private RectTransform _mapContent;
        private RectTransform _marker;
        private TMP_Text _info;
        private TMP_Text _message;
        private TMP_Text _hint;
        private Button _repair, _maintain, _rebuild, _travel, _floorUp, _floorDown;
        private TMP_Text _repairLabel, _maintainLabel, _rebuildLabel, _travelLabel;
        private readonly List<PadMapCell> _cells = new List<PadMapCell>();
        private readonly List<GameObject> _cellObjects = new List<GameObject>();
        private List<int> _floors = new List<int>();
        private int _floor;
        private bool _followFloor = true;
        private float _nextRefresh;
        private bool _mapDirty = true;
        private float _messageUntil;

        public bool IsRaised => _wantRaise;
        public bool IsZoomed => _wantZoom;

        public static InteriorPad Create(Transform parent, Context context)
        {
            var go = new GameObject("InteriorPad");
            go.transform.SetParent(parent, false);
            var pad = go.AddComponent<InteriorPad>();
            pad._c = context;
            pad._t = context.Tuning ?? new InteriorPadTuning();
            pad.Build();
            return pad;
        }

        private void Build()
        {

            _pad = new GameObject("Pad").transform;
            _pad.SetParent(_c.Player.Eye, false);
            if (_t.Model != null)
            {
                var model = Instantiate(_t.Model, _pad, false);
                model.name = "PadModel";
            }
            BuildCanvas();
            ApplyPose();
            _pad.gameObject.SetActive(false);
            if (_c.Actions != null)
            {
                _c.Actions.ActionDone += ShowMessage;
                _c.Actions.ActionFailed += ShowFailure;
            }
        }

        private void OnDestroy()
        {
            if (_c == null)
                return;
            if (_c.Actions != null)
            {
                _c.Actions.ActionDone -= ShowMessage;
                _c.Actions.ActionFailed -= ShowFailure;
            }

            if (_window != null)
                _c.Window(_window, false);
            if (_c.Player != null)
                _c.Player.Frozen = false;
            if (_pad != null)
                Destroy(_pad.gameObject);
        }


        // ---------------- 입력 · 상태 ----------------

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (_wantZoom && keyboard.escapeKey.wasPressedThisFrame)
                {
                    InputGate.ConsumeEscape(); // 내부 나가기보다 먼저
                    if (_window != null)
                        CloseWindow();
                    else
                        SetZoom(false);
                }
                else if (KeyBindings.WasPressed(GameAction.Pad))
                {
                    if (_wantRaise)
                        Lower();
                    else
                        Raise();
                }
                else if (_wantRaise && KeyBindings.WasPressed(GameAction.SpeedCycle))
                {
                    if (_wantZoom && _window != null)
                        CloseWindow();
                    SetZoom(!_wantZoom);
                }
            }

            float dt = Time.unscaledDeltaTime;
            _raise = Mathf.MoveTowards(_raise, _wantRaise ? 1f : 0f, dt / Mathf.Max(0.01f, _t.RaiseSeconds));
            _zoom = Mathf.MoveTowards(_zoom, _wantZoom ? 1f : 0f, dt / Mathf.Max(0.01f, _t.ZoomSeconds));
            if (!_wantRaise && _raise <= 0f && _pad.gameObject.activeSelf)
                _pad.gameObject.SetActive(false);
            if (!_pad.gameObject.activeSelf)
                return;
            ApplyPose();

            if (_mapDirty || Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.4f;
                RefreshMap();
                RefreshInfo();
            }
            UpdateMarker();
            if (_message != null && _messageUntil > 0f && Time.unscaledTime > _messageUntil)
            {
                _messageUntil = 0f;
                _message.SetText(string.Empty);
            }
        }

        public void Raise()
        {
            if (_wantRaise)
                return;
            _wantRaise = true;
            _pad.gameObject.SetActive(true);
            _followFloor = true;
            var current = _c.CurrentRoom();
            _c.Selection.Select(current); // 처음엔 지금 있는 방
            _mapDirty = true;
            SpaceStation.Audio.AudioService.TryPlay(l => l.UiOpen, 0.6f);
        }

        public void Lower()
        {
            if (!_wantRaise)
                return;
            SetZoom(false);
            _wantRaise = false;
            _c.Selection.Select(null);
            SpaceStation.Audio.AudioService.TryPlay(l => l.UiClose, 0.6f);
        }

        /// <summary>화면 확대 / 축소 (든 상태에서만).</summary>
        public void SetZoom(bool on)
        {
            if (on && !_wantRaise)
                return;
            if (_wantZoom == on)
                return;
            _wantZoom = on;
            if (!on && _window != null)
                CloseWindow();
            _c.Player.Frozen = on;
            Cursor.lockState = on ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = on;
            _group.interactable = on;
            _group.blocksRaycasts = on;
            _mapDirty = true;
            RefreshHint();
        }

        private void ApplyPose()
        {
            float r = Smooth(_raise), z = Smooth(_zoom);
            var held = Vector3.Lerp(_t.LoweredPosition, _t.HeldPosition, r);
            var heldRot = Quaternion.Slerp(Quaternion.Euler(_t.LoweredEuler), Quaternion.Euler(_t.HeldEuler), r);
            _pad.localPosition = Vector3.Lerp(held, _t.ZoomPosition, z);
            _pad.localRotation = Quaternion.Slerp(heldRot, Quaternion.Euler(_t.ZoomEuler), z);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        // ---------------- 창 · 이동 ----------------

        private void OpenWindow(string name)
        {
            if (_window == name)
            {
                CloseWindow();
                return;
            }
            if (_window != null)
                CloseWindow();
            if (_c.Window(name, true))
                _window = name;
        }

        private void CloseWindow()
        {
            if (_window == null)
                return;
            _c.Window(_window, false);
            _window = null;
        }

        private void TravelSelected()
        {
            var module = _c.Selection.Selected;
            if (module == null || !CanTravel(module))
                return;
            SetZoom(false);
            _followFloor = true;
            _c.Travel(module);
        }

        private bool CanTravel(ModuleInstance module)
        {
            var layout = _c.Layout();
            return module != null && layout != null && layout.Contains(module) && module != _c.CurrentRoom();
        }

        private void ShowMessage(string text)
        {
            if (_message == null)
                return;
            _message.SetText(text);
            _message.color = HoloUi.TextColor;
            _messageUntil = Time.unscaledTime + 4f;
            _mapDirty = true;
        }

        private void ShowFailure(string text)
        {
            ShowMessage(text);
            if (_message != null)
                _message.color = HudTheme.Negative;
        }

        // ---------------- 화면 만들기 ----------------

        private void BuildCanvas()
        {
            float scale = _t.ScreenSize.x / CanvasWidth;
            float height = _t.ScreenSize.y / scale;
            var canvasGo = new GameObject("PadScreen", typeof(RectTransform));
            canvasGo.transform.SetParent(_pad, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _c.Main;
            canvasGo.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(CanvasWidth, height);
            rt.localScale = Vector3.one * scale;
            rt.localPosition = new Vector3(0f, 0f, _t.ScreenZ - 0.0008f);
            rt.localRotation = Quaternion.identity;
            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.interactable = false;
            _group.blocksRaycasts = false;

            var ui = new HoloUi(_c.Font, _c.Fill, _c.Fill, _c.Fill);
            var bg = HoloUi.Rect("Background", rt);
            HoloUi.Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.02f, 0.05f, 0.08f, 1f);
            bgImg.raycastTarget = false;

            _header = ui.Label(rt, "", 24f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(_header.rectTransform, new Vector2(16f, -6f), new Vector2(600f, 40f));
            var title = ui.Label(rt, "<color=#4FD8FF>STATION PAD</color>", 18f, TextAlignmentOptions.MidlineRight);
            HoloUi.Place(title.rectTransform, new Vector2(CanvasWidth - 236f, -6f), new Vector2(220f, 40f));

            // 지도
            float mapW = 500f, mapH = height - 64f;
            var map = HoloUi.Rect("Map", rt);
            HoloUi.Place(map, new Vector2(12f, -50f), new Vector2(mapW, mapH));
            var mapImg = map.gameObject.AddComponent<Image>();
            mapImg.color = new Color(0.04f, 0.09f, 0.13f, 1f);
            mapImg.raycastTarget = false;
            map.gameObject.AddComponent<RectMask2D>();
            _mapContent = HoloUi.Rect("Cells", map);
            _mapContent.anchorMin = _mapContent.anchorMax = new Vector2(0.5f, 0.5f);
            _mapContent.sizeDelta = Vector2.zero;
            _marker = (RectTransform)ui.Label(map, "▲", 30f, TextAlignmentOptions.Center).transform;
            _marker.anchorMin = _marker.anchorMax = new Vector2(0.5f, 0.5f);
            _marker.sizeDelta = new Vector2(40f, 40f);
            ((TMP_Text)_marker.GetComponent<TMP_Text>()).color = Color.white;
            _floorLabel = ui.Label(map, "", 20f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_floorLabel.rectTransform, new Vector2(10f, -6f), new Vector2(200f, 30f));
            _floorUp = ui.Button(map, "▲", 20f, () => ChangeFloor(1));
            HoloUi.Place((RectTransform)_floorUp.transform, new Vector2(mapW - 52f, -8f), new Vector2(44f, 40f));
            _floorDown = ui.Button(map, "▼", 20f, () => ChangeFloor(-1));
            HoloUi.Place((RectTransform)_floorDown.transform, new Vector2(mapW - 52f, -52f), new Vector2(44f, 40f));

            // 오른쪽: 정보 · 조작
            float x = 524f, w = CanvasWidth - x - 12f;
            var infoBg = HoloUi.Rect("InfoBack", rt);
            HoloUi.Place(infoBg, new Vector2(x, -50f), new Vector2(w, 222f));
            var infoImg = infoBg.gameObject.AddComponent<Image>();
            infoImg.color = new Color(0.04f, 0.09f, 0.13f, 1f);
            infoImg.raycastTarget = false;
            _info = ui.Label(infoBg, "", 17f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Stretch(_info.rectTransform, new Vector2(10f, 8f), new Vector2(-10f, -8f));
            _info.overflowMode = TextOverflowModes.Ellipsis;

            float bw = (w - 12f) / 3f, by = -280f;
            _repair = ui.Button(rt, "수리", 15f, () => Act(a => a.RepairSelected()));
            HoloUi.Place((RectTransform)_repair.transform, new Vector2(x, by), new Vector2(bw, 58f));
            _maintain = ui.Button(rt, "정비", 15f, () => Act(a => a.MaintainSelected()));
            HoloUi.Place((RectTransform)_maintain.transform, new Vector2(x + bw + 6f, by), new Vector2(bw, 58f));
            _rebuild = ui.Button(rt, "재건축", 15f, () => Act(a => a.RebuildSelected()));
            HoloUi.Place((RectTransform)_rebuild.transform, new Vector2(x + (bw + 6f) * 2f, by), new Vector2(bw, 58f));
            _repairLabel = _repair.GetComponentInChildren<TMP_Text>();
            _maintainLabel = _maintain.GetComponentInChildren<TMP_Text>();
            _rebuildLabel = _rebuild.GetComponentInChildren<TMP_Text>();
            _travel = ui.Button(rt, "이 방으로 이동", 17f, TravelSelected);
            HoloUi.Place((RectTransform)_travel.transform, new Vector2(x, by - 64f), new Vector2(w, 44f));
            _travelLabel = _travel.GetComponentInChildren<TMP_Text>();
            var research = ui.Button(rt, "연구 창", 17f, () => OpenWindow("ResearchPanel"));
            HoloUi.Place((RectTransform)research.transform, new Vector2(x, by - 114f), new Vector2((w - 6f) / 2f, 40f));
            var roster = ui.Button(rt, "주민 명단", 17f, () => OpenWindow("RosterPanel"));
            HoloUi.Place((RectTransform)roster.transform, new Vector2(x + (w - 6f) / 2f + 6f, by - 114f), new Vector2((w - 6f) / 2f, 40f));

            _message = ui.Label(rt, "", 15f, TextAlignmentOptions.BottomLeft, wrap: true);
            HoloUi.Place(_message.rectTransform, new Vector2(x, -height + 66f), new Vector2(w, 40f));
            _hint = ui.Label(rt, "", 14f, TextAlignmentOptions.BottomRight);
            HoloUi.Place(_hint.rectTransform, new Vector2(x - 40f, -height + 30f), new Vector2(w + 28f, 22f)); // 오른쪽 끝 둥근 모서리에 잘리지 않게
            _hint.color = HoloUi.MutedColor;
            KeyBindings.Changed += RefreshHint;
            RefreshHint();
        }

        private void OnDisable() => KeyBindings.Changed -= RefreshHint;

        private void RefreshHint()
        {
            if (_hint == null)
                return;
            string zoomKey = KeyBindings.Label(GameAction.SpeedCycle);
            _hint.SetText(_wantZoom ? $"{zoomKey} · ESC 축소   {KeyBindings.Label(GameAction.Pad)} 내리기" : $"{zoomKey} 화면 확대   {KeyBindings.Label(GameAction.Pad)} 내리기");
        }

        private void Act(Action<SelectionActionsPanel> action)
        {
            if (_c.Actions == null || _c.Selection.Selected == null)
                return;
            action(_c.Actions);
            _mapDirty = true;
            RefreshInfo();
        }

        private void ChangeFloor(int direction)
        {
            _followFloor = false;
            _floor = PadMap.Step(_floors, _floor, direction);
            _mapDirty = true;
        }

        // ---------------- 지도 · 정보 ----------------

        private int PlayerFloor() => _c.Builder.WorldToCell(_c.Player.Eye.position).y;

        private void RefreshMap()
        {
            _mapDirty = false;
            var grid = _c.Station.Grid;
            _floors = PadMap.Floors(grid);
            if (_followFloor)
                _floor = PlayerFloor();
            if (_floor == PlayerFloor())
                _followFloor = true;
            var layout = _c.Layout();
            PadMap.Build(grid, _floor, m => layout != null && layout.Contains(m), (cell, dir) => HasHatch(layout, cell, dir), _cells);

            foreach (var go in _cellObjects)
                Destroy(go);
            _cellObjects.Clear();
            var selected = _c.Selection.Selected;
            var current = _c.CurrentRoom();
            float px = _t.CellPixels, gap = 3f;
            foreach (var c in _cells)
            {
                var rt = HoloUi.Rect("Cell", _mapContent);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0f, 0f);
                // 같은 모듈 이웃 쪽으로는 틈 없이 이어 그림
                float x0 = c.Cell.x * px - px * 0.5f + gap * 0.5f, y0 = c.Cell.y * px - px * 0.5f + gap * 0.5f;
                rt.anchoredPosition = new Vector2(x0, y0);
                rt.sizeDelta = new Vector2(px - gap + (c.JoinX ? gap : 0f), px - gap + (c.JoinZ ? gap : 0f));
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = _c.Fill;
                img.color = CellColor(c, c.Module == selected, c.Module == current);
                var module = c.Module;
                var button = rt.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SelectModule(module));
                if (c.HatchUp || c.HatchDown)
                {
                    var arrow = new HoloUi(_c.Font, _c.Fill, null, null).Label(rt, c.HatchUp && c.HatchDown ? "↕" : c.HatchUp ? "↑" : "↓", 14f, TextAlignmentOptions.Center);
                    HoloUi.Stretch(arrow.rectTransform);
                    arrow.color = new Color(0f, 0f, 0f, 0.65f);
                }
                _cellObjects.Add(rt.gameObject);
            }
            _floorLabel.SetText($"{FloorName(_floor)}{(_floor == PlayerFloor() ? " <color=#4FD8FF>· 지금 층</color>" : "")}");
            _floorUp.interactable = PadMap.Step(_floors, _floor, 1) != _floor;
            _floorDown.interactable = PadMap.Step(_floors, _floor, -1) != _floor;
        }

        private static string FloorName(int y) => y >= 0 ? $"{y + 1}층" : $"지하 {-y}층";

        private static bool HasHatch(InteriorLayout layout, Vector3Int cell, Vector3Int dir)
        {
            if (layout == null)
                return false;
            foreach (var f in layout.Faces)
            {
                if (f.Kind == InteriorFaceKind.Hatch && f.Cell == cell && f.Direction == dir)
                    return true;
            }
            return false;
        }

        private Color CellColor(PadMapCell c, bool selected, bool current)
        {
            var m = c.Module;
            Color color;
            if (_c.Station.Simulation.Damage.TryGetInfo(m, out _))
                color = _t.Damaged;
            else if (!_c.Station.Connectivity.IsActive(m))
                color = _t.Inactive;
            else if (m == _c.Station.Simulation.Core)
                color = _t.Core;
            else
            {
                switch (m.Data != null ? m.Data.Category : ModuleCategory.Life)
                {
                    case ModuleCategory.Power: color = _t.Power; break;
                    case ModuleCategory.Industry: color = _t.Industry; break;
                    case ModuleCategory.Defense: color = _t.Defense; break;
                    default: color = _t.Life; break;
                }
            }
            if (!c.Reachable)
                color = Color.Lerp(new Color(0.04f, 0.09f, 0.13f), color, _t.Unreachable);
            if (selected)
                color = Color.Lerp(color, Color.white, 0.45f);
            else if (current)
                color = Color.Lerp(color, Color.white, 0.18f);
            color.a = 1f;
            return color;
        }

        private void SelectModule(ModuleInstance module)
        {
            _c.Selection.Select(module);
            _mapDirty = true;
            RefreshInfo();
        }

        private void UpdateMarker()
        {
            var p = PadMap.ToMap(_c.Player.transform.position, _c.Origin, InteriorGeometry.CellSize);
            float px = _t.CellPixels;
            // 지도는 내 위치를 가운데로 (다른 층을 볼 때도 같은 x · z)
            _mapContent.anchoredPosition = -p * px;
            bool here = _floor == PlayerFloor();
            if (_marker.gameObject.activeSelf != here)
                _marker.gameObject.SetActive(here);
            _marker.anchoredPosition = Vector2.zero;
            _marker.localRotation = Quaternion.Euler(0f, 0f, -_c.Player.transform.eulerAngles.y);
        }

        private void RefreshInfo()
        {
            var module = _c.Selection.Selected;
            var current = _c.CurrentRoom();
            string room = current != null && current.Data != null ? current.Data.DisplayName : "";
            _header.SetText($"<b>{room}</b>");
            bool has = module != null && _c.Actions != null;
            _repair.gameObject.SetActive(has);
            _maintain.gameObject.SetActive(has);
            _rebuild.gameObject.SetActive(has);
            _travel.gameObject.SetActive(has);
            if (!has)
            {
                _info.SetText($"<color={HudText.Muted}>지도에서 모듈을 고르세요</color>");
                return;
            }
            var view = _c.Actions.Describe(module);
            _info.SetText(view.Info);
            _repair.interactable = view.CanRepair;
            _repairLabel.SetText(StripKey(view.RepairLabel));
            _maintain.interactable = view.CanMaintain;
            _maintainLabel.SetText(StripKey(view.MaintainLabel));
            _rebuild.interactable = view.CanRebuild;
            _rebuildLabel.SetText(StripKey(view.RebuildLabel));
            bool canTravel = CanTravel(module);
            _travel.interactable = canTravel;
            _travelLabel.SetText(module == current ? "지금 있는 방" : canTravel ? "이 방으로 이동" : "갈 수 없음 (연결되지 않은 방)");
        }

        /// <summary>바깥 패널 단추 글의 키 표시 "(R)"는 패드에서 쓰지 않으므로 뺌.</summary>
        private static string StripKey(string label)
        {
            int open = label.IndexOf(" (", StringComparison.Ordinal);
            int close = open >= 0 ? label.IndexOf(')', open) : -1;
            return open >= 0 && close > open && close - open <= 12 ? label.Remove(open, close - open + 1) : label;
        }
    }
}
