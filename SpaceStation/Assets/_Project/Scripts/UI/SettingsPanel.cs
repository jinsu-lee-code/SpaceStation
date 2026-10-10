using System;
using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 5-10 설정창 (메인 메뉴·ESC 메뉴 공용). 탭 4개(사운드 / 화면 / 게임 / 조작), 바꾸면 바로 적용·저장.
    /// 조작 탭(7-5): 동작별 키 버튼 → 다음에 누른 키로 변경(KeyBindings), 아래에 마우스·고정 키 안내.
    /// 해상도·화면 모드만 바꾼 뒤 "유지할까요?"를 묻고 10초 안에 답이 없으면 되돌린다.
    /// 화면 탭: 품질 프리셋(낮음/중간/높음) 아래에 세부 항목 — 세부 항목을 바꾸면 프리셋이 "사용자 지정".
    /// 내용은 처음 열 때 코드로 만든다 (씬에는 글꼴·스프라이트만 연결된 빈 오브젝트).
    /// ESC: 확인창 → 되돌리기, 아니면 닫기. 같은 프레임의 다른 ESC 처리(일시정지 메뉴 등)는 EscapeConsumedThisFrame으로 건너뛴다.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public sealed class SettingsPanel : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [Tooltip("11-15 테크 홀로그램 창 (없으면 5-8 기본 모양)")]
        [SerializeField] private HoloArt _holoArt;
        [SerializeField] private float _confirmSeconds = 10f;

        private static readonly Color TextColor = new Color(0.91f, 0.96f, 1f, 1f);
        private static readonly Color MutedColor = new Color(0.68f, 0.76f, 0.85f, 1f);
        private static readonly string[] TabNames = { "사운드", "화면", "게임", "조작" };
        private const float WindowHeight = 900f;

        private static int _escapeConsumedFrame = -1;
        /// <summary>이번 프레임 ESC를 설정창이 썼는지.</summary>
        public static bool EscapeConsumedThisFrame => _escapeConsumedFrame == Time.frameCount;

        private bool _built;
        private bool _open;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;         // 어두운 바탕 (부드러운 페이드만)
        private UiTween _windowTween;   // 창 (페이드 + 지지직, 11-15)
        private HoloUi _holo;   // 11-15 ② 테크 테마 (아트가 있을 때만)
        private HoloFx _fx;
        private int _tab;
        private readonly RectTransform[] _pages = new RectTransform[TabNames.Length];
        private readonly Button[] _tabButtons = new Button[TabNames.Length];

        // 7-5 조작 탭: 키 변경 대기
        private SpaceStation.Core.GameAction? _capturing;
        private TMP_Text _controlsStatus;
        private string _controlsNotice;
        // 맞바꿈·거부·초기화 알림 팝업 (창 가운데, 잠시 뒤 사라짐)
        private const float PopupSeconds = 2.6f;
        private const float PopupFade = 0.4f;
        private CanvasGroup _popup;
        private TMP_Text _popupText;
        private Image _popupFrame;
        private float _popupUntil = -1f;
        private ScrollRect _scroll;
        private readonly List<Action> _refreshers = new List<Action>();

        // 해상도
        private readonly List<Vector2Int> _resolutions = new List<Vector2Int>();
        private int _resolutionIndex;
        private WindowMode _windowMode;
        private GameObject _confirm;
        private TMP_Text _confirmText;
        private float _confirmUntil = -1f;
        private Vector2Int _previousResolution;
        private FullScreenMode _previousMode;

        public bool IsOpen => _open;
        public event Action Closed;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            if (_group == null)
                _group = gameObject.AddComponent<CanvasGroup>();
            SetVisible(false);
            _group.alpha = 0f;
        }

        public void Open()
        {
            if (!_built)
                Build();
            ReadScreen();
            _open = true;
            transform.SetAsLastSibling();
            SetVisible(true);
            _tween.Play();
            _windowTween.Play();
            _fx?.Replay();
            SelectTab(_tab);
            RefreshAll();
            SettingsApplier.Refresh();
            AudioService.TryPlay(l => l.UiOpen);
        }

        public void Close()
        {
            if (!_open)
                return;
            if (_confirmUntil > 0f)
                RevertScreen();
            _capturing = null;
            _controlsNotice = null;
            HidePopup();
            _open = false;
            SetVisible(false);
            _tween.Hide();
            _windowTween.Hide();
            AudioService.TryPlay(l => l.UiClose);
            Closed?.Invoke();
        }

        private void Update()
        {
            if (!_built)
                return;
            _tween.Update();
            _windowTween.Update();
            UpdatePopup();
            if (_confirmUntil > 0f)
            {
                float left = _confirmUntil - Time.unscaledTime;
                if (left <= 0f)
                    RevertScreen();
                else
                    _confirmText.SetText($"이 화면 설정을 유지할까요?\n<size=75%><color=#AFC4D8>{Mathf.CeilToInt(left)}초 후 원래대로 돌아갑니다</color></size>");
            }
            var keyboard = Keyboard.current;
            if (_open && _capturing.HasValue && keyboard != null)
            {
                UpdateCapture(keyboard);
                return;
            }
            if (!_open || keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
                return;
            _escapeConsumedFrame = Time.frameCount;
            if (_confirmUntil > 0f)
                RevertScreen();
            else
                Close();
        }

        private void SetVisible(bool on)
        {
            _group.blocksRaycasts = on;
            _group.interactable = on;
        }

        // ---------------- 구성 ----------------

        private void Build()
        {
            _built = true;
            _holo = _holoArt != null ? new HoloUi(_holoArt, tech: true) : null;
            var root = (RectTransform)transform;
            var dim = gameObject.GetComponent<Image>();
            if (dim == null)
                dim = gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0.01f, 0.03f, 0.72f);

            _window = Rect("Window", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(920f, WindowHeight);
            if (_holo != null)
                _fx = _holo.Window(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.97f), "SETTINGS");
            else
                Panel(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.97f), HudTheme.Accent);

            var title = Label(_window, "설정", 34f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            Place(title.rectTransform, new Vector2(44f, -28f), new Vector2(400f, 48f));

            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var b = MakeButton(_window, TabNames[i], 20f, () => { SelectTab(index); AudioService.TryPlay(l => l.UiTab); }, click: false);
                Place((RectTransform)b.transform, new Vector2(44f + i * 192f, -92f), new Vector2(180f, 44f));
                _tabButtons[i] = b;
            }

            // 스크롤 영역
            var viewport = Rect("Viewport", _window);
            viewport.anchorMin = new Vector2(0f, 0f);
            viewport.anchorMax = new Vector2(1f, 1f);
            viewport.offsetMin = new Vector2(44f, 96f);
            viewport.offsetMax = new Vector2(-44f, -156f);
            viewport.gameObject.AddComponent<RectMask2D>();
            _scroll = _window.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30f;

            for (int i = 0; i < TabNames.Length; i++)
            {
                var page = Rect(TabNames[i] + "Page", viewport);
                page.anchorMin = new Vector2(0f, 1f);
                page.anchorMax = new Vector2(1f, 1f);
                page.pivot = new Vector2(0.5f, 1f);
                page.sizeDelta = Vector2.zero;
                var vl = page.gameObject.AddComponent<VerticalLayoutGroup>();
                vl.spacing = 6;
                vl.childControlWidth = true;
                vl.childControlHeight = true;
                vl.childForceExpandWidth = true;
                vl.childForceExpandHeight = false;
                page.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                _pages[i] = page;
            }
            BuildSoundPage(_pages[0]);
            BuildDisplayPage(_pages[1]);
            BuildGamePage(_pages[2]);
            BuildControlsPage(_pages[3]);

            var reset = MakeButton(_window, "기본값 복원", 19f, ResetCurrentTab);
            Place((RectTransform)reset.transform, new Vector2(44f, -(WindowHeight - 30f - 50f)), new Vector2(220f, 50f));
            var close = MakeButton(_window, "닫기 (ESC)", 19f, Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-44f, 30f);
            crt.sizeDelta = new Vector2(200f, 50f);

            BuildConfirm(root);
            BuildPopup();
            // 11-15 어두운 바탕은 지지직 없이 부드럽게, 창만 지지직 (바탕까지 깜박이면 신경 쓰였음)
            _tween = new UiTween(null, _group, Vector2.zero, 0.2f, 0.25f);
            HoloGlitch.Add(_window, _holoArt);
            _windowTween = new UiTween(_window, _window.gameObject.AddComponent<CanvasGroup>(), Vector2.zero, 0.2f, 0.15f, glitch: true);
        }

        private void BuildSoundPage(RectTransform page)
        {
            SliderRow(page, "마스터", 0f, 100f, 5f, () => AudioVolumes.Master * 100f, v => AudioVolumes.Master = v / 100f, v => $"{v:0}%");
            SliderRow(page, "음악", 0f, 100f, 5f, () => AudioVolumes.Music * 100f, v => AudioVolumes.Music = v / 100f, v => $"{v:0}%");
            SliderRow(page, "효과음", 0f, 100f, 5f, () => AudioVolumes.Effects * 100f, v => AudioVolumes.Effects = v / 100f, v => $"{v:0}%");
            SliderRow(page, "환경음", 0f, 100f, 5f, () => AudioVolumes.Ambient * 100f, v => AudioVolumes.Ambient = v / 100f, v => $"{v:0}%");
        }

        private void BuildDisplayPage(RectTransform page)
        {
            ChoiceRow(page, "해상도", () => _resolutions.Count,
                i => $"{_resolutions[i].x} × {_resolutions[i].y}", () => _resolutionIndex, i => ChangeScreen(i, _windowMode));
            ChoiceRow(page, "화면 모드", () => 3, i => new[] { "전체 화면", "테두리 없는 창", "창 모드" }[i],
                () => (int)_windowMode, i => ChangeScreen(_resolutionIndex, (WindowMode)i));

            Header(page, "그래픽");
            ChoiceRow(page, "품질 프리셋", () => 3, i => new[] { "낮음", "중간", "높음" }[i],
                () => (int)GameSettings.Preset, i => GameSettings.ApplyPreset((GraphicsPreset)i),
                display: () => GameSettings.Preset == GraphicsPreset.Custom ? "사용자 지정" : null);
            ChoiceRow(page, "그림자", () => 4, i => new[] { "끔", "낮음", "중간", "높음" }[i],
                () => (int)GameSettings.Shadows, i => GameSettings.Shadows = (ShadowLevel)i);
            ChoiceRow(page, "안티에일리어싱", () => 4, i => new[] { "끔", "FXAA", "SMAA", "MSAA 4x" }[i],
                () => (int)GameSettings.AntiAliasing, i => GameSettings.AntiAliasing = (AntiAliasingMode)i);
            SliderRow(page, "렌더 해상도", 50f, 100f, 5f, () => GameSettings.RenderScale * 100f, v => GameSettings.RenderScale = v / 100f, v => $"{v:0}%");
            ToggleRow(page, "블룸", () => GameSettings.Bloom, v => GameSettings.Bloom = v);
            ToggleRow(page, "비네팅 (화면 가장자리 어둡게)", () => GameSettings.Vignette, v => GameSettings.Vignette = v);

            Header(page, "성능 · UI");
            ChoiceRow(page, "프레임 제한", () => GameSettings.FrameLimits.Length,
                i => GameSettings.FrameLimits[i] > 0 ? $"{GameSettings.FrameLimits[i]} FPS" : "무제한",
                () => GameSettings.FrameLimitIndex, i => GameSettings.FrameLimitIndex = i,
                display: () => GameSettings.VSync ? "수직 동기화 사용 중" : null);
            ToggleRow(page, "수직 동기화", () => GameSettings.VSync, v => GameSettings.VSync = v);
            ChoiceRow(page, "UI 크기", () => UiScales.Length, i => $"{UiScales[i] * 100f:0}%",
                () => NearestIndex(UiScales, GameSettings.UiScale), i => GameSettings.UiScale = UiScales[i]);
        }

        private static readonly float[] UiScales = { 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f };

        private void BuildGamePage(RectTransform page)
        {
            ChoiceRow(page, "조작 안내", () => 3, i => new[] { "항상 표시", "처음 1분", "끔" }[i],
                () => (int)GameSettings.Hints, i => GameSettings.Hints = (HintMode)i);
            SliderRow(page, "카메라 회전 감도", 50f, 200f, 10f, () => GameSettings.OrbitSensitivity * 100f, v => GameSettings.OrbitSensitivity = v / 100f, v => $"{v:0}%");
            SliderRow(page, "줌 감도", 50f, 200f, 10f, () => GameSettings.ZoomSensitivity * 100f, v => GameSettings.ZoomSensitivity = v / 100f, v => $"{v:0}%");
            ChoiceRow(page, "기본 배속 (게임 시작 시)", () => GameSettings.SpeedChoices.Length, i => $"{GameSettings.SpeedChoices[i]:0}x",
                () => GameSettings.DefaultSpeedIndex, i => GameSettings.DefaultSpeedIndex = i);
            ToggleRow(page, "창 비활성 시 자동 일시정지", () => GameSettings.PauseWhenUnfocused, v => GameSettings.PauseWhenUnfocused = v);
            ChoiceRow(page, "자동 저장 간격 (게임 시간)", () => GameSettings.AutosaveChoices.Length,
                i => GameSettings.AutosaveChoices[i] == 0 ? "끔" : $"{GameSettings.AutosaveChoices[i]}분",
                () => GameSettings.AutosaveIndex, i => GameSettings.AutosaveIndex = i);
            ChoiceRow(page, "노후 모듈 표시", () => 2, i => new[] { "갈색 틴트 (기존)", "밝은 테두리" }[i],
                () => (int)GameSettings.WornDisplay, i => GameSettings.WornDisplay = (WornDisplay)i);
            ToggleRow(page, "내부 방문 중 일시정지", () => GameSettings.InteriorPause, v => GameSettings.InteriorPause = v);
            ToggleRow(page, "내부 걸음 시점 흔들림", () => GameSettings.HeadBob, v => GameSettings.HeadBob = v);
            ChoiceRow(page, "휴대 패드 열기", () => 2, i => new[] { "단계별 (들기 → 확대)", "바로 확대" }[i],
                () => GameSettings.PadDirectZoom ? 1 : 0, i => GameSettings.PadDirectZoom = i == 1);
            ToggleRow(page, "다음 새 게임에서 튜토리얼", () => GameSettings.TutorialPending, v => GameSettings.TutorialPending = v);
        }

        // ---------------- 7-5 조작 탭 ----------------

        private void BuildControlsPage(RectTransform page)
        {
            var statusRow = Rect("Status", page);
            statusRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            _controlsStatus = Label(statusRow, "", 16f, TextAlignmentOptions.MidlineLeft);
            var srt = _controlsStatus.rectTransform;
            srt.anchorMin = Vector2.zero;
            srt.anchorMax = Vector2.one;
            srt.offsetMin = new Vector2(6f, 0f);
            srt.offsetMax = Vector2.zero;
            _refreshers.Add(RefreshControlsStatus);

            string group = null;
            foreach (var info in SpaceStation.Core.KeyBindings.All)
            {
                if (info.Group != group)
                {
                    group = info.Group;
                    Header(page, group);
                }
                KeyRow(page, info.Label, info.Action);
            }
            Header(page, "마우스 · 고정 키 (변경 불가)");
            foreach (var (label, keys) in SpaceStation.Core.KeyBindings.Fixed)
            {
                Row(page, label, out var control);
                var value = Label(control, keys, 18f, TextAlignmentOptions.Center);
                value.color = MutedColor;
                var vrt = value.rectTransform;
                vrt.anchorMin = Vector2.zero;
                vrt.anchorMax = Vector2.one;
                vrt.offsetMin = Vector2.zero;
                vrt.offsetMax = Vector2.zero;
            }
        }

        private void KeyRow(RectTransform page, string label, SpaceStation.Core.GameAction action)
        {
            Row(page, label, out var control);
            var button = MakeButton(control, "", 19f, () => BeginCapture(action));
            var brt = (RectTransform)button.transform;
            brt.anchorMin = new Vector2(0.5f, 0.5f);
            brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = Vector2.zero;
            brt.sizeDelta = new Vector2(220f, 34f);
            var text = button.GetComponentInChildren<TMP_Text>();
            _refreshers.Add(() =>
            {
                bool waiting = _capturing == action;
                text.SetText(waiting ? "키를 누르세요…" : SpaceStation.Core.KeyBindings.Label(action));
                text.color = waiting ? HudTheme.Accent : TextColor;
                if (button.targetGraphic != null)
                    button.targetGraphic.color = waiting ? HudTheme.ButtonSelected : HudTheme.ButtonNormal;
            });
        }

        private void BeginCapture(SpaceStation.Core.GameAction action)
        {
            _capturing = action;
            _controlsNotice = null;
            RefreshAll();
        }

        /// <summary>키 변경 대기 중: 다음에 누른 키로 바꾼다. ESC = 취소 (설정창은 닫지 않음).</summary>
        private void UpdateCapture(Keyboard keyboard)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                _escapeConsumedFrame = Time.frameCount;
                _capturing = null;
                _controlsNotice = null;
                RefreshAll();
                return;
            }
            var key = SpaceStation.Core.KeyBindings.PressedThisFrame();
            if (key == Key.None)
                return;
            var action = _capturing.Value;
            string name = SpaceStation.Core.KeyBindings.InfoOf(action).Label;
            if (!SpaceStation.Core.KeyBindings.Set(action, key))
            {
                string keyName = SpaceStation.Core.KeyBindings.KeyName(key);
                _controlsNotice = $"<color={HudText.Orange}>{keyName} 키는 고정 키라 쓸 수 없습니다 (ESC · 숫자 1~9 · Shift · F5)</color>";
                ShowPopup($"<b>{keyName}</b> 키는 쓸 수 없습니다\n<size=75%><color=#AFC4D8>고정 키: ESC · 숫자 1~9 · Shift · F5  ·  다른 키를 누르세요</color></size>", HudTheme.Negative);
                AudioService.TryPlay(l => l.UiError);
                RefreshAll();
                return; // 계속 대기
            }
            _capturing = null;
            var swapped = SpaceStation.Core.KeyBindings.LastSwapped;
            string newKey = SpaceStation.Core.KeyBindings.Label(action);
            if (swapped.HasValue)
            {
                string other = SpaceStation.Core.KeyBindings.InfoOf(swapped.Value).Label;
                string otherKey = SpaceStation.Core.KeyBindings.Label(swapped.Value);
                _controlsNotice = $"<color={HudText.Yellow}>{name} = {newKey}  ·  겹치던 '{other}'은(는) {otherKey}(으)로 바뀜</color>";
                ShowPopup($"{name} = <b>{newKey}</b>\n<color={HudText.Yellow}>겹치던 '{other}'은(는) <b>{otherKey}</b>(으)로 바뀜</color>", HudTheme.ButtonWarning);
            }
            else
            {
                _controlsNotice = $"<color={HudTheme.GreenHex}>{name} = {newKey}</color>";
            }
            RefreshAll();
        }

        private void BuildPopup()
        {
            var box = Rect("KeyNotice", _window);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(600f, 150f);
            Panel(box.gameObject, new Color(0.02f, 0.05f, 0.08f, 0.97f), HudTheme.ButtonWarning);
            var fill = box.GetComponent<Image>();
            fill.sprite = null; // 홀로그램 바탕은 반투명이라 뒤의 버튼이 비침 → 단색
            fill.color = new Color(0.02f, 0.05f, 0.08f, 1f);
            fill.raycastTarget = false; // 뒤의 키 버튼 클릭을 막지 않음
            _popupFrame = box.Find("Frame").GetComponent<Image>();
            _popupText = Label(box, "", 24f, TextAlignmentOptions.Center);
            _popupText.textWrappingMode = TextWrappingModes.Normal;
            var trt = _popupText.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(24f, 12f);
            trt.offsetMax = new Vector2(-24f, -12f);
            _popup = box.gameObject.AddComponent<CanvasGroup>();
            _popup.blocksRaycasts = false;
            _popup.interactable = false;
            _popup.alpha = 0f;
        }

        /// <summary>맞바꿈·거부·초기화 알림을 창 가운데에 잠시 띄운다.</summary>
        private void ShowPopup(string text, Color frame)
        {
            if (_popup == null)
                return;
            _popupText.SetText(text);
            _popupFrame.color = frame;
            _popup.transform.SetAsLastSibling();
            _popup.alpha = 1f;
            _popupUntil = Time.unscaledTime + PopupSeconds;
        }

        private void HidePopup()
        {
            _popupUntil = -1f;
            if (_popup != null)
                _popup.alpha = 0f;
        }

        private void UpdatePopup()
        {
            if (_popupUntil < 0f)
                return;
            float left = _popupUntil - Time.unscaledTime;
            if (left <= 0f)
                HidePopup();
            else
                _popup.alpha = Mathf.Clamp01(left / PopupFade);
        }

        private void RefreshControlsStatus()
        {
            if (_controlsStatus == null)
                return;
            _controlsStatus.SetText(_controlsNotice
                ?? (_capturing.HasValue
                    ? $"<color={HudTheme.AccentHex}>'{SpaceStation.Core.KeyBindings.InfoOf(_capturing.Value).Label}'에 쓸 키를 누르세요  ·  ESC: 취소</color>"
                    : $"<color=#AFC4D8>바꿀 동작의 키 버튼을 누른 뒤 새 키를 누르세요. 같은 상황에서 겹치는 키는 서로 맞바뀝니다</color>"));
        }

        private void SelectTab(int tab)
        {
            _tab = tab;
            HidePopup();
            if (_capturing.HasValue)
            {
                _capturing = null;
                _controlsNotice = null;
            }
            for (int i = 0; i < TabNames.Length; i++)
            {
                _pages[i].gameObject.SetActive(i == tab);
                if (_tabButtons[i].targetGraphic != null)
                    _tabButtons[i].targetGraphic.color = i == tab ? HudTheme.ButtonSelected : HudTheme.ButtonNormal;
            }
            _scroll.content = _pages[tab];
            _scroll.verticalNormalizedPosition = 1f;
        }

        private void ResetCurrentTab()
        {
            switch (_tab)
            {
                case 0:
                    AudioVolumes.Master = 0.8f;
                    AudioVolumes.Music = 0.7f;
                    AudioVolumes.Effects = 1f;
                    AudioVolumes.Ambient = 1f;
                    break;
                case 1:
                    GameSettings.ResetDisplay(); // 해상도·화면 모드는 그대로
                    break;
                case 2:
                    GameSettings.ResetGame();
                    break;
                default:
                    _capturing = null;
                    SpaceStation.Core.KeyBindings.ResetAll();
                    _controlsNotice = $"<color={HudTheme.GreenHex}>모든 조작키를 기본값으로 되돌렸습니다</color>";
                    ShowPopup("모든 조작키를 기본값으로 되돌렸습니다", HudTheme.Accent);
                    break;
            }
            RefreshAll();
        }

        private void RefreshAll()
        {
            foreach (var r in _refreshers)
                r();
        }

        // ---------------- 해상도 ----------------

        private void ReadScreen()
        {
            _resolutions.Clear();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!_resolutions.Contains(v) && r.width >= 1024)
                    _resolutions.Add(v);
            }
            var current = new Vector2Int(Screen.width, Screen.height);
            if (!_resolutions.Contains(current))
                _resolutions.Add(current);
            _resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            _resolutionIndex = _resolutions.IndexOf(current);
            _windowMode = Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen ? WindowMode.Fullscreen
                : Screen.fullScreenMode == FullScreenMode.FullScreenWindow ? WindowMode.Borderless : WindowMode.Windowed;
        }

        private static FullScreenMode ToMode(WindowMode mode) => mode == WindowMode.Fullscreen ? FullScreenMode.ExclusiveFullScreen
            : mode == WindowMode.Borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;

        private void ChangeScreen(int resolutionIndex, WindowMode mode)
        {
            if (_confirmUntil <= 0f)
            {
                _previousResolution = new Vector2Int(Screen.width, Screen.height);
                _previousMode = Screen.fullScreenMode;
            }
            _resolutionIndex = Mathf.Clamp(resolutionIndex, 0, _resolutions.Count - 1);
            _windowMode = mode;
            var r = _resolutions[_resolutionIndex];
            Screen.SetResolution(r.x, r.y, ToMode(mode));
            _confirmUntil = Time.unscaledTime + _confirmSeconds;
            _confirm.SetActive(true);
            _confirm.transform.SetAsLastSibling();
        }

        private void KeepScreen()
        {
            _confirmUntil = -1f;
            _confirm.SetActive(false);
        }

        private void RevertScreen()
        {
            _confirmUntil = -1f;
            _confirm.SetActive(false);
            Screen.SetResolution(_previousResolution.x, _previousResolution.y, _previousMode);
            _resolutionIndex = Mathf.Max(0, _resolutions.IndexOf(_previousResolution));
            _windowMode = _previousMode == FullScreenMode.ExclusiveFullScreen ? WindowMode.Fullscreen
                : _previousMode == FullScreenMode.FullScreenWindow ? WindowMode.Borderless : WindowMode.Windowed;
            RefreshAll();
        }

        private void BuildConfirm(RectTransform root)
        {
            var box = Rect("Confirm", root);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(560f, 220f);
            Panel(box.gameObject, new Color(0.02f, 0.05f, 0.08f, 0.98f), HudTheme.ButtonWarning);
            _confirmText = Label(box, "", 22f, TextAlignmentOptions.Center);
            Place(_confirmText.rectTransform, new Vector2(30f, -26f), new Vector2(500f, 90f));
            var keep = MakeButton(box, "유지", 20f, KeepScreen);
            Place((RectTransform)keep.transform, new Vector2(70f, -134f), new Vector2(200f, 50f));
            var revert = MakeButton(box, "되돌리기", 20f, RevertScreen);
            Place((RectTransform)revert.transform, new Vector2(290f, -134f), new Vector2(200f, 50f));
            _confirm = box.gameObject;
            _confirm.SetActive(false);
        }

        // ---------------- 행 ----------------

        private RectTransform Row(RectTransform page, string label, out RectTransform control)
        {
            var row = Rect(label, page);
            var le = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 42f;
            var text = Label(row, label, 19f, TextAlignmentOptions.MidlineLeft);
            var trt = text.rectTransform;
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(0.45f, 1f);
            trt.offsetMin = new Vector2(6f, 0f);
            trt.offsetMax = Vector2.zero;
            control = Rect("Control", row);
            control.anchorMin = new Vector2(0.45f, 0f);
            control.anchorMax = new Vector2(1f, 1f);
            control.offsetMin = Vector2.zero;
            control.offsetMax = Vector2.zero;
            return row;
        }

        private void Header(RectTransform page, string text)
        {
            var row = Rect(text, page);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
            var t = Label(row, text, 17f, TextAlignmentOptions.BottomLeft);
            t.color = HudTheme.Accent;
            t.fontStyle = FontStyles.Bold;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6f, 4f);
            rt.offsetMax = Vector2.zero;
            var line = Rect("Line", row);
            line.anchorMin = new Vector2(0f, 0f);
            line.anchorMax = new Vector2(1f, 0f);
            line.sizeDelta = new Vector2(0f, 1f);
            var img = line.gameObject.AddComponent<Image>();
            img.color = HudTheme.AccentDim;
            img.raycastTarget = false;
        }

        /// <summary>&lt; 값 &gt; 순환 선택. display가 null이 아닌 문자열을 주면 값 대신 그 문구를 보여 준다.</summary>
        private void ChoiceRow(RectTransform page, string label, Func<int> count, Func<int, string> name, Func<int> get, Action<int> set,
            Func<string> display = null)
        {
            Row(page, label, out var control);
            var value = Label(control, "", 19f, TextAlignmentOptions.Center);
            var vrt = value.rectTransform;
            vrt.anchorMin = Vector2.zero;
            vrt.anchorMax = Vector2.one;
            vrt.offsetMin = new Vector2(52f, 0f);
            vrt.offsetMax = new Vector2(-52f, 0f);

            void Step(int dir)
            {
                int n = count();
                if (n <= 0)
                    return;
                int current = Mathf.Clamp(get(), 0, n - 1);
                set((current + dir + n) % n);
                RefreshAll();
            }
            var left = MakeButton(control, "<", 20f, () => Step(-1));
            var lrt = (RectTransform)left.transform;
            lrt.anchorMin = new Vector2(0f, 0.5f);
            lrt.anchorMax = new Vector2(0f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(44f, 34f);
            var right = MakeButton(control, ">", 20f, () => Step(1));
            var rrt = (RectTransform)right.transform;
            rrt.anchorMin = new Vector2(1f, 0.5f);
            rrt.anchorMax = new Vector2(1f, 0.5f);
            rrt.pivot = new Vector2(1f, 0.5f);
            rrt.anchoredPosition = Vector2.zero;
            rrt.sizeDelta = new Vector2(44f, 34f);
            CenterLabel(left);
            CenterLabel(right);

            _refreshers.Add(() =>
            {
                string special = display?.Invoke();
                int n = count();
                value.SetText(special ?? (n > 0 ? name(Mathf.Clamp(get(), 0, n - 1)) : "-"));
                value.color = special != null ? MutedColor : TextColor;
            });
        }

        private void ToggleRow(RectTransform page, string label, Func<bool> get, Action<bool> set)
        {
            ChoiceRow(page, label, () => 2, i => i == 1 ? "켬" : "끔", () => get() ? 1 : 0, i => set(i == 1));
        }

        private void SliderRow(RectTransform page, string label, float min, float max, float step, Func<float> get, Action<float> set, Func<float, string> format)
        {
            Row(page, label, out var control);
            var slider = MakeSlider(control);
            var srt = (RectTransform)slider.transform;
            srt.anchorMin = new Vector2(0f, 0.5f);
            srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(0.5f, 0.5f);
            srt.offsetMin = new Vector2(4f, -12f);
            srt.offsetMax = new Vector2(-96f, 12f);
            slider.minValue = min / step;
            slider.maxValue = max / step;
            slider.wholeNumbers = true;
            var value = Label(control, "", 19f, TextAlignmentOptions.MidlineRight);
            var vrt = value.rectTransform;
            vrt.anchorMin = new Vector2(1f, 0f);
            vrt.anchorMax = new Vector2(1f, 1f);
            vrt.pivot = new Vector2(1f, 0.5f);
            vrt.sizeDelta = new Vector2(84f, 0f);
            vrt.anchoredPosition = Vector2.zero;

            bool refreshing = false;
            slider.onValueChanged.AddListener(v =>
            {
                value.SetText(format(v * step));
                if (!refreshing)
                    set(v * step);
            });
            _refreshers.Add(() =>
            {
                refreshing = true;
                slider.value = Mathf.Round(get() / step);
                value.SetText(format(slider.value * step));
                refreshing = false;
            });
        }

        private static int NearestIndex(float[] values, float v)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
                if (Mathf.Abs(values[i] - v) < Mathf.Abs(values[best] - v))
                    best = i;
            return best;
        }

        // ---------------- 위젯 ----------------

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }

        private TMP_Text Label(Transform parent, string text, float size, TextAlignmentOptions align)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null)
                t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.color = TextColor;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.richText = true;
            return t;
        }

        private void Panel(GameObject go, Color fill, Color frame)
        {
            if (_holo != null)
            {
                _holo.Panel(go, fill, frame); // 11-15 ② 테크 패널
                return;
            }
            var img = go.GetComponent<Image>();
            if (img == null)
                img = go.AddComponent<Image>();
            img.sprite = _fillSprite;
            img.type = Image.Type.Sliced;
            img.color = fill;
            var f = Rect("Frame", go.transform);
            f.anchorMin = Vector2.zero;
            f.anchorMax = Vector2.one;
            f.offsetMin = Vector2.zero;
            f.offsetMax = Vector2.zero;
            var fi = f.gameObject.AddComponent<Image>();
            fi.sprite = _frameSprite;
            fi.type = Image.Type.Sliced;
            fi.color = frame;
            fi.raycastTarget = false;
        }

        private Button MakeButton(Transform parent, string label, float size, Action onClick, bool click = true)
        {
            var rt = Rect(label, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _buttonSprite;
            img.type = Image.Type.Sliced;
            img.color = HudTheme.ButtonNormal;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            colors.selectedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                onClick();
                EventSystem.current?.SetSelectedGameObject(null); // 선택 색이 남지 않게
            });
            var sound = rt.gameObject.AddComponent<UiSound>();
            sound.Mode = click ? UiSound.ClickMode.Click : UiSound.ClickMode.Silent;
            var text = Label(rt, label, size, TextAlignmentOptions.Center);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.offsetMin = Vector2.zero;
            text.rectTransform.offsetMax = Vector2.zero;
            if (_holo != null && _holoArt.TechButton != null)
            {
                // 11-15 ② 테크 버튼 (HoloUi.TechButton과 같은 모양 · 디테일)
                img.sprite = _holoArt.TechButton;
                HoloUi.Glow(text, 0.3f);
                HoloButtonFx.Add(button, _holoArt);
            }
            return button;
        }

        private static void CenterLabel(Button b)
        {
            var t = b.GetComponentInChildren<TMP_Text>();
            if (t == null)
                return;
            t.richText = false; // "<", ">"를 태그로 읽지 않게
            t.SetText(t.text);
        }

        private Slider MakeSlider(Transform parent)
        {
            var root = Rect("Slider", parent);
            var bg = Rect("Background", root);
            bg.anchorMin = new Vector2(0f, 0.35f);
            bg.anchorMax = new Vector2(1f, 0.65f);
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.1f, 0.2f, 0.28f, 1f);

            var fillArea = Rect("Fill Area", root);
            fillArea.anchorMin = new Vector2(0f, 0.35f);
            fillArea.anchorMax = new Vector2(1f, 0.65f);
            fillArea.offsetMin = new Vector2(0f, 0f);
            fillArea.offsetMax = new Vector2(-8f, 0f);
            var fill = Rect("Fill", fillArea);
            fill.sizeDelta = new Vector2(8f, 0f);
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.color = HudTheme.Accent;

            var handleArea = Rect("Handle Slide Area", root);
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);
            var handle = Rect("Handle", handleArea);
            handle.sizeDelta = new Vector2(16f, 0f);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.sprite = _buttonSprite;
            handleImg.type = Image.Type.Sliced;
            handleImg.color = new Color(0.85f, 0.95f, 1f, 1f);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }
    }
}
