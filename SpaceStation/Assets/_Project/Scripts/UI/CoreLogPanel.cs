using System.Text;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-2 코어 로그 창 (사용자 결정 2026-10-15, D-4): [관리] 메뉴의 [코어 기록] → 코어의 4축 성향을 말로("연민 쪽으로 기울어짐", 숫자는 숨김) +
    /// 코어가 남긴 로그(장 · 시각, 최근 것이 아래) + 무엇 때문에 기울었는지(최근 축 이동 원인).
    /// 키는 따로 두지 않음 (키 설정은 공통 바닥 — 필요하면 main에서). 내용은 코드로 만든다.
    /// </summary>
    [DefaultExecutionOrder(-245)]
    public sealed class CoreLogPanel : MonoBehaviour
    {
        private const float WindowWidth = 980f;
        private const float WindowHeight = 700f;
        private const int ShownEntries = 16;
        private const int ShownCauses = 5;
        private const string Soft = "#C8D2DE";

        [SerializeField] private StationController _station;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;

        private HoloUi _ui;
        private StationSimulation _sim;
        private CorePersonalitySystem _core;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;
        private HoloFx _fx;
        private bool _open;
        private TMP_Text _axes, _log, _causes;
        private readonly StringBuilder _sb = new StringBuilder();

        public bool IsOpen => _open;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _core = _sim?.GetSystem<CorePersonalitySystem>();
            if (_core == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            BuildLauncher();
            BuildWindow();
            HudWindows.Opened += HandleWindowOpened;
            _core.Logged += HandleLogged;
            _core.Shifted += HandleShifted;
        }

        private void OnDestroy()
        {
            HudWindows.Opened -= HandleWindowOpened;
            if (_core == null)
                return;
            _core.Logged -= HandleLogged;
            _core.Shifted -= HandleShifted;
        }

        private void Update()
        {
            if (_ui == null)
                return;
            _tween.Update();
            var keyboard = Keyboard.current;
            if (_open && keyboard != null && !InputGate.Blocked && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();
                Close();
            }
        }

        private void HandleLogged(CoreLogEntry entry)
        {
            if (_open)
                Refresh();
        }

        private void HandleShifted(CoreAxis axis, float delta)
        {
            if (_open)
                Refresh();
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
            HudWindows.NotifyOpened(this);
            transform.SetAsLastSibling();
            _open = true;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _tween.Play();
            _fx?.Replay();
            Refresh();
            AudioService.TryPlay(l => l.UiOpen);
        }

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

        // ---------------- 구성 ----------------

        /// <summary>[관리] 메뉴 맨 끝 판 전용 칸 (타이쿤판 [시스템]과 같은 자리, 사용자 결정 2026-10-18).</summary>
        private void BuildLauncher()
        {
            HudLauncher.For(_ui, transform).Add(new HudLauncher.Entry
            {
                Key = "core", Order = HudLauncher.Order.Edition, Name = "코어 기록",
                IsOpen = () => _open, Click = Toggle,
            });
        }

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("CoreLogWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-120f, 30f);
            _window.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _fx = _ui.Window(_window.gameObject, new Color(0.02f, 0.045f, 0.07f, 0.99f), "CORE");
            var back = HoloUi.Rect("Backing", _window);
            HoloUi.Stretch(back, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            back.SetAsFirstSibling();
            var backImage = back.gameObject.AddComponent<Image>();
            backImage.color = new Color(0.015f, 0.03f, 0.05f, 0.97f);
            backImage.raycastTarget = false;

            var title = _ui.Label(_window, "코어 기록", 34f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -26f), new Vector2(400f, 48f));
            HoloUi.Glow(title, 0.5f);
            var sub = _ui.Label(_window, $"<color={Soft}>방주 \"코어\"의 AI가 남긴 기록 · 성향은 선택에 따라 조금씩 움직입니다</color>", 17f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(sub.rectTransform, new Vector2(40f, -72f), new Vector2(WindowWidth - 80f, 28f));

            // 왼쪽: 성향 4축 + 최근 원인 / 오른쪽: 로그
            var left = HoloUi.Rect("Lean", _window);
            HoloUi.Place(left, new Vector2(40f, -112f), new Vector2(330f, WindowHeight - 190f));
            _ui.Panel(left.gameObject, new Color(0.03f, 0.06f, 0.09f, 0.98f), HudTheme.AccentDim);
            _axes = _ui.Label(left, "", 19f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Stretch(_axes.rectTransform, new Vector2(18f, 200f), new Vector2(-14f, -14f));
            _causes = _ui.Label(left, "", 15f, TextAlignmentOptions.BottomLeft, wrap: true);
            HoloUi.Stretch(_causes.rectTransform, new Vector2(18f, 14f), new Vector2(-14f, -300f));

            var right = HoloUi.Rect("Log", _window);
            HoloUi.Place(right, new Vector2(390f, -112f), new Vector2(WindowWidth - 430f, WindowHeight - 190f));
            _ui.Panel(right.gameObject, new Color(0.03f, 0.06f, 0.09f, 0.98f), HudTheme.AccentDim);
            _log = _ui.Label(right, "", 17f, TextAlignmentOptions.BottomLeft, wrap: true);
            HoloUi.Stretch(_log.rectTransform, new Vector2(18f, 14f), new Vector2(-14f, -14f));
            _log.overflowMode = TextOverflowModes.Masking;

            var close = _ui.Button(_window, "닫기 (ESC)", 20f, Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-40f, 22f);
            crt.sizeDelta = new Vector2(190f, 46f);

            HoloGlitch.Add(_window, _holoArt);
            _tween = new UiTween(_window, _group, new Vector2(0f, -24f), 0.2f, 0.15f, glitch: true);
        }

        // ---------------- 표시 ----------------

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private string ChapterName(int index)
        {
            var ch = _sim.Chapters;
            return ch != null && index >= 0 && index < ch.Count ? ch.GetChapter(index).DisplayName : "";
        }

        private void Refresh()
        {
            _sb.Clear();
            _sb.Append("<b>지금의 성향</b>\n");
            foreach (CoreAxis axis in System.Enum.GetValues(typeof(CoreAxis)))
            {
                var lean = _core.Lean(axis);
                string color = lean == CorePole.Neutral ? Soft : ChapterView.CoreTint;
                _sb.Append($"\n<size=80%><color={Soft}>{CoreConfig.AxisName(axis)}</color></size>\n<color={color}>{_core.Describe(axis)}</color>\n");
            }
            _axes.SetText(_sb.ToString());

            _sb.Clear();
            var shifts = _core.Shifts;
            if (shifts.Count > 0)
            {
                _sb.Append($"<color={Soft}>최근에 기운 이유</color>\n");
                int from = Mathf.Max(0, shifts.Count - ShownCauses);
                for (int i = from; i < shifts.Count; i++)
                {
                    var s = shifts[i];
                    string pole = CoreConfig.PoleName(s.Axis, s.Delta < 0f ? CorePole.First : CorePole.Second);
                    _sb.Append($"<size=90%>· {s.Cause} → {pole} 쪽</size>\n");
                }
            }
            _causes.SetText(_sb.ToString());

            _sb.Clear();
            var entries = _core.Entries;
            if (entries.Count == 0)
                _sb.Append($"<color={Soft}>아직 기록이 없습니다</color>");
            int start = Mathf.Max(0, entries.Count - ShownEntries);
            for (int i = start; i < entries.Count; i++)
            {
                var e = entries[i];
                string ch = ChapterName(e.Chapter);
                _sb.Append($"<size=78%><color={Soft}>{(string.IsNullOrEmpty(ch) ? "" : ch + " · ")}{Clock(e.Time)}</color></size>  <color={ChapterView.CoreTint}>{e.Text}</color>\n");
            }
            _log.SetText(_sb.ToString());
        }
    }
}
