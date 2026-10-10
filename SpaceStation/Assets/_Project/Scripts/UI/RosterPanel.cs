using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.UI
{
    /// <summary>
    /// Phase 10 주민 명단 창. U 키 또는 왼쪽 위 [주민] 버튼으로 연다. 게임 시간은 멈추지 않는다.
    /// 창 내용은 패드 주민 탭과 같은 <see cref="RosterView"/> (11-15 ②: 인원 · 특성 효과 요약, 명단 · 이사, 쪽 넘김)를 조금 크게, 한 쪽에 더 많이 넣는다.
    /// 내용은 코드로 만든다 (씬에는 글꼴·스프라이트·테마 아트만 연결된 빈 오브젝트). ESC로 닫으면 다른 ESC 처리는 건너뛴다 (이사 고르는 중이면 그것만 취소).
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class RosterPanel : MonoBehaviour
    {
        private const float ContentScale = 1.22f;   // 패드 화면 기준 내용을 바깥 1920×1080에 맞게
        private const float ContentWidth = 856f;   // = 패드 화면 너비

        [SerializeField] private StationController _station;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [Tooltip("11-15 테크 홀로그램 (여는 버튼 · 창)")]
        [SerializeField] private HoloArt _holoArt;
        [SerializeField] private float _refreshSeconds = 0.5f;
        [Tooltip("한 쪽 주민 수 (패드는 6) — 11-15 ②에서 이름을 바꿔 옛 값 13(창 높이 초과)을 버림")]
        [SerializeField] private int _pageRows = 9;

        private HoloUi _ui;
        private ResidentRoster _roster;
        private RectTransform _window;
        private CanvasGroup _group;
        private UiTween _tween;
        private HoloFx _fx;
        private RosterView _view;
        private TMP_Text _launcherLabel;
        private bool _open;
        private bool _dirty = true;
        private float _nextRefresh;

        public bool IsOpen => _open;

        private void Start()
        {
            var sim = _station != null ? _station.Simulation : null;
            _roster = sim?.Residents;
            if (_roster == null)
            {
                gameObject.SetActive(false); // 주민 설정이 없는 씬
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            BuildLauncher();
            BuildWindow();
            HudWindows.Opened += HandleWindowOpened;
            _roster.Changed += MarkDirty;
            KeyBindings.Changed += RefreshLauncher;
        }

        private void OnDestroy()
        {
            KeyBindings.Changed -= RefreshLauncher;
            HudWindows.Opened -= HandleWindowOpened;
            if (_roster != null)
                _roster.Changed -= MarkDirty;
        }

        private void MarkDirty() => _dirty = true;

        private void Update()
        {
            if (_ui == null)
                return;
            _tween.Update();
            var keyboard = Keyboard.current;
            if (keyboard != null && !InputGate.Blocked)
            {
                if (KeyBindings.WasPressed(GameAction.Roster))
                    Toggle();
                else if (_open && keyboard.escapeKey.wasPressedThisFrame)
                {
                    InputGate.ConsumeEscape();
                    if (!_view.Cancel()) // 이사 고르는 중이면 그것만 취소
                        Close();
                }
            }
            if (_open && (_dirty || Time.unscaledTime >= _nextRefresh))
            {
                _dirty = false;
                _nextRefresh = Time.unscaledTime + _refreshSeconds;
                _view.Refresh();
            }
        }

        public void Toggle()
        {
            if (_open)
                Close();
            else
                Open();
        }

        public void Open()
        {
            HudWindows.NotifyOpened(this); // 다른 창(연구 ↔ 주민)은 닫힘
            _open = true;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _view.SetActive(false); // 이사 고르기 초기화
            _view.SetActive(true);
            _tween.Play();
            _fx?.Replay();
            AudioService.TryPlay(l => l.UiOpen);
        }

        /// <summary>다른 창이 열리면 소리 없이 닫힘 (여는 소리만 나게).</summary>
        private void HandleWindowOpened(object window)
        {
            if (!ReferenceEquals(window, this))
                Close(sound: false);
        }

        public void Close() => Close(sound: true);

        private void Close(bool sound)
        {
            if (!_open)
                return;
            _open = false;
            _view.Cancel();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _tween.Hide();
            if (sound)
                AudioService.TryPlay(l => l.UiClose);
        }

        // ---------------- 구성 ----------------

        private static string LauncherText()
            => $"주민 ({KeyBindings.Label(GameAction.Roster)})"; // " (키)"는 테크 버튼 배지로

        private void RefreshLauncher()
        {
            if (_launcherLabel != null)
                _launcherLabel.SetText(LauncherText());
        }

        private void BuildLauncher()
        {
            var button = _ui.TechButton((RectTransform)transform, LauncherText(), 19f, Toggle);
            HoloUi.Place((RectTransform)button.transform, new Vector2(202f, -24f), new Vector2(140f, 44f)); // 연구 버튼 오른쪽
            _launcherLabel = button.GetComponentInChildren<TMP_Text>();
        }

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("RosterWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-150f, 40f); // 16:9 기준 오른쪽 자원 패널과 겹치지 않게
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // 내용 (패드 주민 탭과 같은 구성, 배율만 크게) — 높이를 알아야 창 크기가 정해지므로 먼저 만든다
            var content = HoloUi.Rect("Content", _window);
            content.localScale = new Vector3(ContentScale, ContentScale, 1f);
            _view = new RosterView(_ui, content, ContentWidth, _roster, top: 0f, rowsPerPage: Mathf.Max(1, _pageRows));
            const float head = 96f, foot = 30f, side = 33f;
            _window.sizeDelta = new Vector2(ContentWidth * ContentScale + side * 2f, head + _view.Height * ContentScale + foot);
            HoloUi.Place(content, new Vector2(side, -head), new Vector2(ContentWidth, _view.Height));
            _fx = _ui.Window(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.96f), "ROSTER");
            content.SetAsLastSibling(); // 창 테두리 · 장식보다 위

            var title = _ui.Label(_window, "주민 명단", 32f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -30f), new Vector2(500f, 46f));
            HoloUi.Glow(title, 0.5f);

            // 닫기는 아래 쪽 넘김 줄 오른쪽 끝
            var close = _ui.Button(_window, "닫기 (ESC)", 19f, Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-40f, 26f);
            crt.sizeDelta = new Vector2(190f, 48f);

            _tween = new UiTween(_window, _group, new Vector2(0f, -24f), 0.2f, 0.15f, glitch: true);
        }
    }
}
