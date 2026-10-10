using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.UI
{
    /// <summary>
    /// Phase 6 연구 창 (RESEARCH.md 5번). T 키 또는 왼쪽 위 [연구] 버튼으로 연다. 게임 시간은 멈추지 않는다.
    /// - 창 내용은 패드 연구 탭과 같은 <see cref="ResearchView"/> (11-15 ②: 왼쪽 분야 목록 + 오른쪽 상세, 정비 자동화 설정 포함)를 조금 크게 넣는다
    /// - 왼쪽 위 추적기: 진행 중인 연구의 진행률 (창을 닫아도 보임)
    /// 내용은 코드로 만든다 (씬에는 글꼴·스프라이트·테마 아트만 연결된 빈 오브젝트). ESC로 닫으면 다른 ESC 처리는 건너뛴다.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class ResearchPanel : MonoBehaviour
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
        [SerializeField] private float _refreshSeconds = 0.25f;

        private HoloUi _ui;
        private StationSimulation _sim;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;
        private HoloFx _fx;
        private ResearchView _view;
        private TMP_Text _tracker;
        private bool _open;
        private float _nextRefresh;

        public bool IsOpen => _open;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            if (_sim == null || _sim.Research.Categories.Count == 0)
            {
                gameObject.SetActive(false); // 연구 데이터가 없는 씬
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            BuildLauncher();
            BuildWindow();
            HudWindows.Opened += HandleWindowOpened;
            _sim.Research.Completed += HandleCompleted;
            Refresh();
        }

        private void OnDestroy()
        {
            KeyBindings.Changed -= RefreshLauncher;
            HudWindows.Opened -= HandleWindowOpened;
            if (_sim != null)
                _sim.Research.Completed -= HandleCompleted;
        }

        private void Update()
        {
            if (_ui == null)
                return;
            _tween.Update();
            var keyboard = Keyboard.current;
            if (keyboard != null && !InputGate.Blocked)
            {
                if (KeyBindings.WasPressed(GameAction.Research))
                    Toggle();
                else if (_open && keyboard.escapeKey.wasPressedThisFrame)
                {
                    InputGate.ConsumeEscape();
                    Close();
                }
            }
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + _refreshSeconds;
                Refresh();
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
            _view.SetActive(true);
            _tween.Play();
            _fx?.Replay();
            Refresh();
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
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _tween.Hide();
            if (sound)
                AudioService.TryPlay(l => l.UiClose);
        }

        private void HandleCompleted(ResearchCategoryData category, int level)
        {
            AudioService.TryPlay(l => l.EventPositive, 0.8f);
            Refresh();
        }

        // ---------------- 구성 ----------------

        private TMP_Text _launcherLabel;

        private static string LauncherText()
            => $"{HudTheme.Icon("research")} 연구 ({KeyBindings.Label(GameAction.Research)})"; // " (키)"는 테크 버튼 배지로

        private void RefreshLauncher()
        {
            if (_launcherLabel != null)
                _launcherLabel.SetText(LauncherText());
        }

        private void BuildLauncher()
        {
            var root = (RectTransform)transform;
            var button = _ui.TechButton(root, LauncherText(), 19f, Toggle);
            HoloUi.Place((RectTransform)button.transform, new Vector2(24f, -24f), new Vector2(170f, 44f));
            _launcherLabel = button.GetComponentInChildren<TMP_Text>();
            KeyBindings.Changed += RefreshLauncher; // 7-5: 버튼의 키 표시
            _tracker = _ui.Label(root, "", 15f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_tracker.rectTransform, new Vector2(28f, -76f), new Vector2(420f, 120f));
        }

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("ResearchWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-150f, 40f); // 16:9 기준 오른쪽 자원 패널과 겹치지 않게 // 오른쪽 자원 패널·아래 건설 메뉴를 가리지 않게
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // 내용 (패드 연구 탭과 같은 구성, 배율만 크게) — 높이를 알아야 창 크기가 정해지므로 먼저 만든다
            var content = HoloUi.Rect("Content", _window);
            content.localScale = new Vector3(ContentScale, ContentScale, 1f);
            _view = new ResearchView(_ui, content, ContentWidth, _sim, top: 0f);
            const float head = 96f, foot = 92f, side = 33f;
            var size = new Vector2(ContentWidth * ContentScale + side * 2f, head + _view.Height * ContentScale + foot);
            _window.sizeDelta = size;
            HoloUi.Place(content, new Vector2(side, -head), new Vector2(ContentWidth, _view.Height));
            content.SetAsLastSibling();
            _fx = _ui.Window(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.96f), "RESEARCH");
            content.SetAsLastSibling(); // 창 테두리 · 장식보다 위

            var title = _ui.Label(_window, $"{HudTheme.Icon("research")} 연구", 32f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -30f), new Vector2(500f, 46f));
            HoloUi.Glow(title, 0.5f);

            var warn = _ui.Label(_window, $"<color={HudText.Muted}>시작 비용은 취소해도 돌려받지 않습니다 · 전력이 부족하면 연구도 느려집니다 · 연구소가 끊기거나 파손되면 멈춥니다</color>", 15f, TextAlignmentOptions.MidlineLeft);
            warn.rectTransform.anchorMin = warn.rectTransform.anchorMax = warn.rectTransform.pivot = new Vector2(0f, 0f);
            warn.rectTransform.anchoredPosition = new Vector2(42f, 30f);
            warn.rectTransform.sizeDelta = new Vector2(860f, 40f);
            var close = _ui.Button(_window, "닫기 (ESC)", 19f, Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-40f, 26f);
            crt.sizeDelta = new Vector2(190f, 48f);

            _tween = new UiTween(_window, _group, new Vector2(0f, -24f), 0.2f, 0.15f, glitch: true);
        }

        // ---------------- 표시 ----------------

        private void Refresh()
        {
            if (_open)
                _view.Refresh();
            RefreshTracker();
        }

        private void RefreshTracker()
        {
            var research = _sim.Research;
            if (research.Projects.Count == 0)
            {
                _tracker.SetText(research.LabSlots > 0 ? $"<color={HudText.Muted}>진행 중인 연구 없음</color>" : "");
                return;
            }
            var sb = new System.Text.StringBuilder();
            foreach (var p in research.Projects)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(HudTheme.Icon(p.Category.Icon)).Append(' ').Append(p.Category.DisplayName).Append(" Lv.").Append(p.TargetLevel)
                  .Append("  ").Append((p.Progress * 100f).ToString("0")).Append('%');
                sb.Append(p.Paused ? $" <color={HudText.Orange}>멈춤</color>" : $" <color={HudText.Muted}>{ResearchView.FormatTime(p.RemainingSeconds)}</color>");
            }
            _tracker.SetText(sb.ToString());
        }
    }
}
