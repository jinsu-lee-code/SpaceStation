using System;
using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Settings;
using SpaceStation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        [Tooltip("확대 자세 (화면 아래쪽에 눕혀 들고, 위 빈 곳에 홀로그램 모형)")]
        public Vector3 ZoomPosition = new Vector3(0f, -0.075f, 0.2f);
        public Vector3 ZoomEuler = new Vector3(34f, 0f, 0f);
        [Tooltip("들기 · 확대 걸리는 시간 (초)")]
        public float RaiseSeconds = 0.28f;
        public float ZoomSeconds = 0.22f;

        [Header("11-13 홀로그램 모형")]
        [Tooltip("모듈 모형 메시 묶음 (메뉴 SpaceStation/Interior/Wire Pad가 구움)")]
        public PadHoloSet Holo;
        [Tooltip("홀로그램 면 · 선 재질 (URP Particles/Unlit 가산)")]
        public Material HoloFill;
        public Material HoloLine;
        [Tooltip("투사기 위치 (패드 기준 — 위 가장자리 센서 바)")]
        public Vector3 HoloAnchor = new Vector3(0f, 0.094f, -0.002f);
        [Tooltip("모형 가로 · 세로 최대 크기 (m, 확대 상태)")]
        public float HoloFootprint = 0.12f;
        [Tooltip("투사기에서 모형 바닥까지 높이 (m, 확대 상태) — 패드 화면을 가리지 않게")]
        public float HoloLift = 0.085f;
        [Tooltip("모형을 보는 사람 쪽으로 기울이는 각 (도)")]
        public float HoloTilt = 55f;
        [Tooltip("발광 세기 (Bloom과 함께)")]
        public float HoloGlow = 1.4f;

        [Header("모듈 색")]
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
    /// (커서 풀림 · 걷기 멈춤), 다시 Tab · ESC로 축소. 고른 모듈 정보 · 수리 · 정비 · 재건축(<see cref="SelectionActionsPanel.Describe"/> 공용)
    /// + 빠른 이동 + 연구 · 명단 창.
    /// 11-13: 패드 위 3D 홀로그램 모형(<see cref="PadHologram"/>)이 지도 — 지금 층 모듈을 실제 모양으로, 확대 중 모형을 클릭해 고름.
    /// 화면은 홀로그램 테마(<see cref="HoloUi"/> · <see cref="HoloFx"/> 진하게): 고른 모듈 썸네일 카드 · 분류 · 상태 꼬리표.
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
            /// <summary>11-13 홀로그램 테마 아트 (없으면 Font · Fill만으로 단순하게).</summary>
            public HoloArt Art;
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

        // 홀로그램 모형
        private PadHologram _holo;
        private Transform _holoAnchor;
        private readonly List<PadMapCell> _cells = new List<PadMapCell>();
        private readonly HashSet<ModuleInstance> _floorModules = new HashSet<ModuleInstance>();
        private List<int> _floors = new List<int>();
        private int _floor = int.MinValue;
        private bool _followFloor = true;

        // UI
        private HoloFx _fx;
        private CanvasGroup _group;
        private TMP_Text _header;
        private TMP_Text _floorLabel;
        private Image _thumb;
        private TMP_Text _thumbEmpty;
        private TMP_Text _name;
        private TMP_Text _category;
        private TMP_Text _status;
        private Image _categoryBack, _statusBack;
        private TMP_Text _info;
        private TMP_Text _message;
        private TMP_Text _hint;
        private Button _repair, _maintain, _rebuild, _travel, _floorUp, _floorDown;
        private TMP_Text _repairLabel, _maintainLabel, _rebuildLabel, _travelLabel, _researchLabel, _rosterLabel;
        private float _nextRefresh;
        private bool _dirty = true;
        private float _messageUntil;
        private float _nextWheel;
        private ModuleInstance _shown;

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
            _holoAnchor = new GameObject("HoloAnchor").transform;
            _holoAnchor.SetParent(_pad, false);
            _holoAnchor.localPosition = _t.HoloAnchor;
            if (_t.Holo != null && _t.HoloFill != null && _t.HoloLine != null)
            {
                _holo = PadHologram.Create(_holoAnchor, new PadHologram.Settings
                {
                    Set = _t.Holo,
                    Fill = _t.HoloFill,
                    Line = _t.HoloLine,
                    Font = _c.Art != null && _c.Art.Font != null ? _c.Art.Font : _c.Font,
                    Footprint = _t.HoloFootprint,
                    Glow = _t.HoloGlow,
                    Tilt = _t.HoloTilt,
                    Lift = _t.HoloLift,
                });
                _holo.transform.SetParent(transform, true);
                _holo.ColorOf = ModuleColor;
                _holo.gameObject.SetActive(false);
            }
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
                    else if (GameSettings.PadDirectZoom)
                        Lower(); // 바로 확대 설정: 축소 단계 없이 내림
                    else
                        SetZoom(false);
                }
                else if (KeyBindings.WasPressed(GameAction.Pad))
                {
                    if (_wantRaise)
                        Lower();
                    else
                    {
                        Raise();
                        if (GameSettings.PadDirectZoom)
                            SetZoom(true);
                    }
                }
                else if (_wantRaise && KeyBindings.WasPressed(GameAction.SpeedCycle))
                {
                    if (_wantZoom && _window != null)
                        CloseWindow();
                    if (_wantZoom && GameSettings.PadDirectZoom)
                        Lower();
                    else
                        SetZoom(!_wantZoom);
                }
                else if (_wantZoom && !InputGate.Blocked)
                    HandleShortcuts(); // 바깥과 같은 키 (확대 중만 — 들고 걸을 때 F는 해치)
            }

            // 마우스 휠 = 층 바꾸기 (확대 중 — 모형은 그때만 보임. 연구 · 명단 창이 열려 있으면 창 스크롤에 양보)
            var wheel = Mouse.current;
            if (_wantZoom && _window == null && wheel != null && Time.unscaledTime >= _nextWheel)
            {
                float scroll = wheel.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    _nextWheel = Time.unscaledTime + 0.18f; // 부드러운 스크롤(여러 프레임 값)로 여러 층을 한꺼번에 넘지 않게
                    ChangeFloor(scroll > 0f ? 1 : -1);
                }
            }

            float dt = Time.unscaledDeltaTime;
            _raise =Mathf.MoveTowards(_raise, _wantRaise ? 1f : 0f, dt / Mathf.Max(0.01f, _t.RaiseSeconds));
            _zoom = Mathf.MoveTowards(_zoom, _wantZoom ? 1f : 0f, dt / Mathf.Max(0.01f, _t.ZoomSeconds));
            if (!_wantRaise && _raise <= 0f && _pad.gameObject.activeSelf)
            {
                _pad.gameObject.SetActive(false);
                if (_holo != null)
                    _holo.gameObject.SetActive(false);
            }
            if (!_pad.gameObject.activeSelf)
                return;
            ApplyPose();

            if (_dirty || Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.4f;
                _dirty = false;
                RefreshFloor();
                RefreshInfo();
            }
            UpdateHologram();
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
            _dirty = true;
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
            if (_holo != null)
                _holo.Hover = null;
            _dirty = true;
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
            _dirty = true;
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

            var ui = _c.Art != null ? new HoloUi(_c.Art) : new HoloUi(_c.Font, _c.Fill, _c.Fill, _c.Fill);
            _fx = ui.Screen(rt, 1f, HudTheme.HoloDeep); // 패드 = 진하게
            var accent = HudTheme.Accent;
            var low = new Color(accent.r, accent.g, accent.b, 0.35f);

            // 위: 위치 · 층
            var tag = ui.Label(rt, $"<color={HudTheme.AccentHex}><b>//</b> STATION PAD</color>  <color={HudTheme.MutedHex}>현재 위치</color>", 13f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(tag.rectTransform, new Vector2(18f, -8f), new Vector2(420f, 20f));
            _header = ui.Label(rt, "", 26f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(_header.rectTransform, new Vector2(18f, -26f), new Vector2(480f, 34f));
            _floorDown = ui.TechButton(rt, "▼", 16f, () => ChangeFloor(-1));
            HoloUi.Place((RectTransform)_floorDown.transform, new Vector2(CanvasWidth - 236f, -14f), new Vector2(40f, 38f));
            _floorLabel = ui.Label(rt, "", 19f, TextAlignmentOptions.Center);
            HoloUi.Place(_floorLabel.rectTransform, new Vector2(CanvasWidth - 192f, -10f), new Vector2(128f, 46f));
            _floorUp = ui.TechButton(rt, "▲", 16f, () => ChangeFloor(1));
            HoloUi.Place((RectTransform)_floorUp.transform, new Vector2(CanvasWidth - 60f, -14f), new Vector2(40f, 38f));
            HoloUi.Divider(rt, new Vector2(16f, -64f), CanvasWidth - 32f, low);

            // 왼쪽: 고른 모듈 카드 (썸네일 · 이름 · 분류 · 상태)
            const float cx = 16f, cw = 280f;
            var thumbBox = HoloUi.Rect("ThumbBox", rt);
            HoloUi.Place(thumbBox, new Vector2(cx, -76f), new Vector2(cw, 168f));
            ui.TechPanel(thumbBox.gameObject, new Color(0.03f, 0.12f, 0.17f, 0.95f), new Color(accent.r, accent.g, accent.b, 0.9f), "MODULE", 0.45f);
            var thumbRt = HoloUi.Rect("Thumb", thumbBox);
            HoloUi.Stretch(thumbRt, new Vector2(14f, 10f), new Vector2(-14f, -24f));
            _thumb = thumbRt.gameObject.AddComponent<Image>();
            _thumb.preserveAspect = true;
            _thumb.raycastTarget = false;
            _thumbEmpty = ui.Label(thumbBox, "", 15f, TextAlignmentOptions.Center, wrap: true);
            HoloUi.Stretch(_thumbEmpty.rectTransform, new Vector2(16f, 10f), new Vector2(-16f, -10f));
            _thumbEmpty.color = HoloUi.MutedColor;
            _name = ui.Label(rt, "", 23f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(_name.rectTransform, new Vector2(cx, -252f), new Vector2(cw, 34f));
            _category = ui.Chip(rt, "", accent, 14f);
            HoloUi.Place((RectTransform)_category.transform.parent, new Vector2(cx, -292f), new Vector2(84f, 28f));
            _categoryBack = _category.transform.parent.GetComponent<Image>();
            _status = ui.Chip(rt, "", HudTheme.Positive, 14f);
            HoloUi.Place((RectTransform)_status.transform.parent, new Vector2(cx + 92f, -292f), new Vector2(cw - 92f, 28f));
            _statusBack = _status.transform.parent.GetComponent<Image>();

            // 오른쪽: 정보 · 조작
            float x = cx + cw + 18f, w = CanvasWidth - x - 16f;
            var infoBg = HoloUi.Rect("InfoBack", rt);
            HoloUi.Place(infoBg, new Vector2(x, -76f), new Vector2(w, 168f));
            ui.TechPanel(infoBg.gameObject, new Color(0.03f, 0.1f, 0.15f, 0.85f), new Color(accent.r, accent.g, accent.b, 0.7f), "STATUS", 0.2f);
            _info = ui.Label(infoBg, "", 16f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Stretch(_info.rectTransform, new Vector2(16f, 8f), new Vector2(-16f, -26f));
            _info.overflowMode = TextOverflowModes.Ellipsis;

            float bw = (w - 12f) / 3f, by = -256f;
            _repair = ui.TechButton(rt, "수리", 15f, () => Act(a => a.RepairSelected()));
            HoloUi.Place((RectTransform)_repair.transform, new Vector2(x, by), new Vector2(bw, 54f));
            _maintain = ui.TechButton(rt, "정비", 15f, () => Act(a => a.MaintainSelected()));
            HoloUi.Place((RectTransform)_maintain.transform, new Vector2(x + bw + 6f, by), new Vector2(bw, 54f));
            _rebuild = ui.TechButton(rt, "재건축", 15f, () => Act(a => a.RebuildSelected()));
            HoloUi.Place((RectTransform)_rebuild.transform, new Vector2(x + (bw + 6f) * 2f, by), new Vector2(bw, 54f));
            _repairLabel = _repair.GetComponentInChildren<TMP_Text>();
            _maintainLabel = _maintain.GetComponentInChildren<TMP_Text>();
            _rebuildLabel = _rebuild.GetComponentInChildren<TMP_Text>();
            _travel = ui.TechButton(rt, "이 방으로 이동", 17f, TravelSelected);
            HoloUi.Place((RectTransform)_travel.transform, new Vector2(x, by - 62f), new Vector2(w, 44f));
            _travelLabel = _travel.GetComponentInChildren<TMP_Text>();
            var research = ui.TechButton(rt, "연구 창", 16f, () => OpenWindow("ResearchPanel"));
            HoloUi.Place((RectTransform)research.transform, new Vector2(x, by - 112f), new Vector2((w - 6f) / 2f, 40f));
            var roster = ui.TechButton(rt, "주민 명단", 16f, () => OpenWindow("RosterPanel"));
            HoloUi.Place((RectTransform)roster.transform, new Vector2(x + (w - 6f) / 2f + 6f, by - 112f), new Vector2((w - 6f) / 2f, 40f));
            _researchLabel = research.GetComponentInChildren<TMP_Text>();
            _rosterLabel = roster.GetComponentInChildren<TMP_Text>();

            // 아래: 알림 · 키 안내
            HoloUi.Divider(rt, new Vector2(16f, -height + 58f), CanvasWidth - 32f, low);
            _message = ui.Label(rt, "", 15f, TextAlignmentOptions.MidlineLeft, wrap: true);
            HoloUi.Place(_message.rectTransform, new Vector2(18f, -height + 52f), new Vector2(CanvasWidth * 0.55f, 40f));
            _hint = ui.Label(rt, "", 14f, TextAlignmentOptions.MidlineRight);
            HoloUi.Place(_hint.rectTransform, new Vector2(CanvasWidth * 0.5f, -height + 46f), new Vector2(CanvasWidth * 0.5f - 34f, 26f)); // 오른쪽 끝 둥근 모서리에 잘리지 않게
            _hint.color = HoloUi.MutedColor;
            KeyBindings.Changed += RefreshHint;
            RefreshHint();
        }

        private void OnDisable() => KeyBindings.Changed -= RefreshHint;

        private void RefreshHint()
        {
            if (_hint == null)
                return;
            if (_researchLabel != null)
            {
                _researchLabel.SetText($"연구 창 ({KeyBindings.Label(GameAction.Research)})");
                _rosterLabel.SetText($"주민 명단 ({KeyBindings.Label(GameAction.Roster)})");
            }
            string zoomKey = KeyBindings.Label(GameAction.SpeedCycle);
            _hint.SetText(_wantZoom
                ? GameSettings.PadDirectZoom
                    ? $"클릭 고르기   휠 층 이동   {zoomKey} · ESC · {KeyBindings.Label(GameAction.Pad)} 닫기"
                    : $"클릭 고르기   휠 층 이동   {zoomKey} · ESC 축소   {KeyBindings.Label(GameAction.Pad)} 내리기"
                : $"{zoomKey} 확대 (홀로그램 지도)   {KeyBindings.Label(GameAction.Pad)} 내리기");
        }

        /// <summary>확대 중 단축키: 수리 · 정비 · 재건축 · 수리 대기 취소(고른 모듈) + 연구 · 명단 창. 바깥 선택 패널 · 창과 같은 키.</summary>
        private void HandleShortcuts()
        {
            if (KeyBindings.WasPressed(GameAction.Research))
                OpenWindow("ResearchPanel");
            else if (KeyBindings.WasPressed(GameAction.Roster))
                OpenWindow("RosterPanel");
            else if (_window != null)
                return; // 창이 열려 있으면 창 조작 우선
            else if (KeyBindings.WasPressed(GameAction.Repair))
                Act(a => a.RepairSelected());
            else if (KeyBindings.WasPressed(GameAction.Maintain))
                Act(a => a.MaintainSelected());
            else if (KeyBindings.WasPressed(GameAction.Rebuild))
                Act(a => a.RebuildSelected());
            else if (KeyBindings.WasPressed(GameAction.CancelRepair))
                Act(a => a.CancelRepairSelected());
        }

        private void Act(Action<SelectionActionsPanel> action)
        {
            if (_c.Actions == null || _c.Selection.Selected == null)
                return;
            action(_c.Actions);
            _dirty = true;
            RefreshInfo();
        }

        private void ChangeFloor(int direction)
        {
            int next = PadMap.Step(_floors, _floor, direction);
            if (next == _floor)
                return;
            _followFloor = false;
            ShowFloor(next, Math.Sign(next - _floor));
            RefreshFloorLabel();
        }

        // ---------------- 홀로그램 모형 ----------------

        private int PlayerFloor() => _c.Builder.WorldToCell(_c.Player.Eye.position).y;

        private void RefreshFloor()
        {
            _floors = PadMap.Floors(_c.Station.Grid);
            int player = PlayerFloor();
            if (_floor == player)
                _followFloor = true;
            int floor = _followFloor ? player : _floor;
            if (floor != _floor)
                ShowFloor(floor, _floor == int.MinValue ? 0 : Math.Sign(floor - _floor));
            else if (FloorChanged())
                ShowFloor(floor, 0); // 지금 층에 모듈이 생기거나 없어짐
            RefreshFloorLabel();
        }

        private void ShowFloor(int floor, int direction)
        {
            _floor = floor;
            var layout = _c.Layout();
            PadMap.Build(_c.Station.Grid, floor, m => layout != null && layout.Contains(m), null, _cells);
            _floorModules.Clear();
            foreach (var c in _cells)
                _floorModules.Add(c.Module);
            if (_holo != null)
                _holo.ShowFloor(floor, _cells, direction);
            if (direction != 0 && _fx != null)
                _fx.Replay();
        }

        /// <summary>지금 층의 모듈 구성이 바뀌었는지 (건설 · 파괴).</summary>
        private bool FloorChanged()
        {
            int count = 0;
            foreach (var module in _c.Station.Grid.Modules)
            {
                bool on = false;
                foreach (var cell in module.Cells)
                {
                    if (cell.y == _floor)
                    {
                        on = true;
                        break;
                    }
                }
                if (!on)
                    continue;
                if (!_floorModules.Contains(module))
                    return true;
                count++;
            }
            return count != _floorModules.Count;
        }

        private void RefreshFloorLabel()
        {
            bool here = _floor == PlayerFloor();
            _floorLabel.SetText($"<b>{FloorName(_floor)}</b>\n<size=62%>{(here ? $"<color={HudTheme.AccentHex}>지금 층</color>" : $"<color={HudTheme.MutedHex}>다른 층 보는 중</color>")}</size>");
            _floorUp.interactable = PadMap.Step(_floors, _floor, 1) != _floor;
            _floorDown.interactable = PadMap.Step(_floors, _floor, -1) != _floor;
        }

        private static string FloorName(int y) => y >= 0 ? $"{y + 1}층" : $"지하 {-y}층";

        private void UpdateHologram()
        {
            if (_holo == null)
                return;
            // 들고 걸을 때는 숨기고, 확대(패드 조작)할 때만 빛기둥이 올라오며 펼쳐짐
            bool show = _zoom > 0.001f;
            if (_holo.gameObject.activeSelf != show)
                _holo.gameObject.SetActive(show);
            if (!show)
                return;
            _holo.SetPresentation(Smooth(_zoom));
            var p = PadMap.ToMap(_c.Player.transform.position, _c.Origin, InteriorGeometry.CellSize);
            _holo.SetPlayer(p, _floor == PlayerFloor());
            _holo.Selected = _c.Selection.Selected;
            if (!_wantZoom || _zoom < 0.95f)
                return;
            var mouse = Mouse.current;
            if (mouse == null)
                return;
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            var hit = overUi ? null : _holo.Pick(_c.Main.ScreenPointToRay(mouse.position.ReadValue()));
            if (hit != null && hit != _holo.Hover)
                SpaceStation.Audio.AudioService.TryPlay(l => l.UiHover, 1f);
            _holo.Hover = hit;
            if (hit != null && mouse.leftButton.wasPressedThisFrame)
                SelectModule(hit); // 고르는 소리는 GameAudio(선택 바뀜)가 냄
        }

        private Color ModuleColor(ModuleInstance m)
        {
            var color = BaseColor(m);
            bool reachable = _c.Layout() is InteriorLayout layout && layout.Contains(m);
            if (!reachable)
                color = Color.Lerp(new Color(0.1f, 0.2f, 0.26f), color, _t.Unreachable);
            return color;
        }

        private Color BaseColor(ModuleInstance m)
        {
            if (_c.Station.Simulation.Damage.TryGetInfo(m, out _))
                return _t.Damaged;
            if (!_c.Station.Connectivity.IsActive(m))
                return _t.Inactive;
            if (m == _c.Station.Simulation.Core)
                return _t.Core;
            return CategoryColor(m.Data != null ? m.Data.Category : ModuleCategory.Life);
        }

        private Color CategoryColor(ModuleCategory category)
        {
            switch (category)
            {
                case ModuleCategory.Power: return _t.Power;
                case ModuleCategory.Industry: return _t.Industry;
                case ModuleCategory.Defense: return _t.Defense;
                default: return _t.Life;
            }
        }

        private void SelectModule(ModuleInstance module)
        {
            _c.Selection.Select(module);
            RefreshInfo();
        }

        // ---------------- 정보 ----------------

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
            _category.transform.parent.gameObject.SetActive(has);
            _status.transform.parent.gameObject.SetActive(has);
            if (module != _shown && _fx != null && has)
                _fx.Replay(); // 다른 모듈을 고르면 화면이 다시 그려지는 효과
            _shown = module;
            if (!has)
            {
                _thumb.enabled = false;
                _name.SetText("");
                _thumbEmpty.SetText(_wantZoom ? "위 홀로그램 모형에서\n모듈을 고르세요" : $"{KeyBindings.Label(GameAction.SpeedCycle)}로 화면을 확대해\n모듈을 고르세요");
                _info.SetText($"<color={HudText.Muted}>고른 모듈의 정보가 여기에 나와요</color>");
                return;
            }
            var data = module.Data;
            _thumb.sprite = data != null ? data.Icon : null;
            _thumb.enabled = _thumb.sprite != null;
            _thumbEmpty.SetText(_thumb.enabled ? "" : data != null ? data.DisplayName : "");
            _name.SetText($"<b>{(data != null ? data.DisplayName : module.ToString())}</b>");

            var categoryColor = module == _c.Station.Simulation.Core ? _t.Core : CategoryColor(data != null ? data.Category : ModuleCategory.Life);
            SetChip(_category, _categoryBack, module == _c.Station.Simulation.Core ? "코어" : (data != null ? data.Category.DisplayName() : ""), categoryColor);
            var (statusText, statusColor) = Status(module);
            SetChip(_status, _statusBack, statusText, statusColor);

            var view = _c.Actions.Describe(module);
            _info.SetText(WithoutName(view.Info));
            _repair.interactable = view.CanRepair;
            _repairLabel.SetText(view.RepairLabel); // 키 표시 "(R)" 그대로 — 확대 중 단축키가 같음
            _maintain.interactable = view.CanMaintain;
            _maintainLabel.SetText(view.MaintainLabel);
            _rebuild.interactable = view.CanRebuild;
            _rebuildLabel.SetText(view.RebuildLabel);
            bool canTravel = CanTravel(module);
            _travel.interactable = canTravel;
            _travelLabel.SetText(module == current ? "지금 있는 방" : canTravel ? "이 방으로 이동" : "갈 수 없음 (연결되지 않은 방)");
        }

        private (string, Color) Status(ModuleInstance module)
        {
            var sim = _c.Station.Simulation;
            if (sim.Damage.TryGetInfo(module, out var dmg))
                return dmg.IsRepairing ? ("수리 중", HudTheme.Accent) : ("파손", HudTheme.Negative);
            if (!_c.Station.Connectivity.IsActive(module))
                return ("비활성", _t.Inactive);
            if (sim.Durability.TryGetInfo(module, out var dur) && sim.Durability.EfficiencyFor(dur.Current) < 1f)
                return ("노후", HudTheme.Warning);
            return ("정상 가동", HudTheme.Positive);
        }

        private static void SetChip(TMP_Text text, Image back, string label, Color color)
        {
            text.SetText(label);
            text.color = Color.Lerp(color, Color.white, 0.35f);
            if (back != null)
                back.color = new Color(color.r, color.g, color.b, 0.28f);
        }

        /// <summary>정보 글 맨 앞의 "&lt;b&gt;이름&lt;/b&gt;"은 카드에 이미 있으므로 뺌 (효율 표시는 남김).</summary>
        private static string WithoutName(string info)
        {
            if (!info.StartsWith("<b>", StringComparison.Ordinal))
                return info;
            int close = info.IndexOf("</b>", StringComparison.Ordinal);
            return close > 0 ? info.Substring(close + 4).TrimStart() : info;
        }
    }
}
