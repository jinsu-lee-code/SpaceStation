using System.Collections.Generic;
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
    /// 13-5a 탐사 창 (사용자 결정 2026-10-18 D-6): [관리] 메뉴 '탐사' → 이번 장 신호마다 방향 · 거리 · 흐릿한 힌트 + [탐사선 보내기],
    /// 밝힌 뒤엔 종류 · 효과 · 지금 거리(몇 칸 안으로 들어가야 닿는지), 닿은 뒤엔 효과가 이어지는지. 지난 장 지점은 아래 한 줄씩.
    /// 신호가 있는 장부터 [관리] 메뉴에 보임 · 고를 신호가 있으면 '!'. 내용은 코드로 만든다.
    /// </summary>
    [DefaultExecutionOrder(-245)]
    public sealed class ExplorationPanel : MonoBehaviour
    {
        private const float WindowWidth = 940f;
        private const float WindowHeight = 600f;
        private const float RowHeight = 104f;
        private const string Soft = "#C8D2DE";

        [SerializeField] private StationController _station;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;

        private HoloUi _ui;
        private StationSimulation _sim;
        private ExplorationSystem _explore;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;
        private HoloFx _fx;
        private bool _open;
        private float _nextRefresh;
        private TMP_Text _status, _past;
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<ExploreSite> _current = new List<ExploreSite>();
        private readonly StringBuilder _sb = new StringBuilder();

        private sealed class Row
        {
            public RectTransform Root;
            public TMP_Text Text;
            public Button Button;
            public TMP_Text ButtonText;
            public ExploreSite Site;
        }

        public bool IsOpen => _open;

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _explore = _sim?.GetSystem<ExplorationSystem>();
            if (_explore == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            BuildWindow();
            HudLauncher.For(_ui, transform).Add(new HudLauncher.Entry
            {
                Key = "probe", Order = HudLauncher.Order.EditionExtra, Name = "탐사",
                IsOpen = () => _open, Click = Toggle,
                Visible = () => _explore.ChapterHasSignals || _explore.Sites.Count > 0,
                Attention = HasChoice,
            });
            HudWindows.Opened += HandleWindowOpened;
            _explore.Changed += HandleChanged;
        }

        private void OnDestroy()
        {
            HudWindows.Opened -= HandleWindowOpened;
            if (_explore != null)
                _explore.Changed -= HandleChanged;
        }

        /// <summary>신호가 잡혔는데 아직 탐사선을 안 보냄.</summary>
        private bool HasChoice()
        {
            if (_explore.ProbeUsedThisChapter || _explore.ProbeInFlight)
                return false;
            _explore.CurrentSites(_current);
            return _current.Count > 0;
        }

        private void HandleChanged()
        {
            if (_open)
                Refresh();
        }

        private void Update()
        {
            if (_ui == null)
                return;
            _tween.Update();
            if (!_open)
                return;
            var keyboard = Keyboard.current;
            if (keyboard != null && !InputGate.Blocked && keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                InputGate.ConsumeEscape();
                Close();
                return;
            }
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
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

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("ExplorationWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = new Vector2(-120f, 30f);
            _window.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _fx = _ui.Window(_window.gameObject, new Color(0.02f, 0.045f, 0.07f, 0.99f), "SCAN");
            var back = HoloUi.Rect("Backing", _window);
            HoloUi.Stretch(back, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            back.SetAsFirstSibling();
            var backImage = back.gameObject.AddComponent<Image>();
            backImage.color = new Color(0.015f, 0.03f, 0.05f, 0.97f);
            backImage.raycastTarget = false;

            var title = _ui.Label(_window, "탐사", 34f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -26f), new Vector2(400f, 48f));
            HoloUi.Glow(title, 0.5f);
            _status = _ui.Label(_window, "", 17f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(_status.rectTransform, new Vector2(40f, -72f), new Vector2(WindowWidth - 80f, 50f));

            for (int i = 0; i < 4; i++)
            {
                var row = new Row { Root = HoloUi.Rect("Site" + i, _window) };
                HoloUi.Place(row.Root, new Vector2(40f, -128f - i * (RowHeight + 10f)), new Vector2(WindowWidth - 80f, RowHeight));
                _ui.Panel(row.Root.gameObject, new Color(0.03f, 0.06f, 0.09f, 0.98f), HudTheme.AccentDim);
                row.Text = _ui.Label(row.Root, "", 17f, TextAlignmentOptions.MidlineLeft, wrap: true);
                HoloUi.Stretch(row.Text.rectTransform, new Vector2(18f, 8f), new Vector2(-250f, -8f));
                var r = row;
                row.Button = _ui.TechButton(row.Root, "", 17f, () => HandleProbe(r));
                var brt = (RectTransform)row.Button.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 0.5f);
                brt.anchoredPosition = new Vector2(-14f, 0f);
                brt.sizeDelta = new Vector2(220f, 56f);
                row.ButtonText = row.Button.GetComponentInChildren<TMP_Text>();
                row.ButtonText.enableWordWrapping = true;
                _rows.Add(row);
            }
            _past = _ui.Label(_window, "", 15f, TextAlignmentOptions.BottomLeft, wrap: true);
            HoloUi.Stretch(_past.rectTransform, new Vector2(40f, 76f), new Vector2(-40f, -(128f + 3 * (RowHeight + 10f) + 4f)));

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
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private void Refresh()
        {
            var cfg = _explore.Config;
            _explore.CurrentSites(_current);
            _sb.Clear();
            if (!_explore.ChapterHasSignals)
                _sb.Append($"<color={Soft}>이번 장에는 새 신호가 없습니다</color>");
            else if (_current.Count == 0)
                _sb.Append(_explore.SensorActive ? "신호를 찾는 중..." : $"<color={HudText.Orange}>센서 어레이를 지어 가동하면 신호가 잡힙니다</color> <color={Soft}>(방어 탭)</color>");
            else if (_explore.ProbeInFlight)
                _sb.Append($"탐사선 날아가는 중 · 도착까지 <b>{Clock(_explore.ProbeTimeLeft)}</b>");
            else if (!_explore.ProbeUsedThisChapter)
                _sb.Append($"신호 {_current.Count}개 · <b>한 장에 하나</b>만 탐사선으로 밝힐 수 있습니다 <color={Soft}>(금속 {cfg.ProbeMetal:0} · {cfg.ProbeSeconds:0}초)</color>");
            else
                _sb.Append($"밝힌 지점 쪽으로 정거장을 뻗어 <b>{cfg.ReachRadius}칸 이내 도달 시 탐사 시작</b> <color={Soft}>(구조 프레임이 싸고 가벼움)</color>");
            if (_explore.Fragments > 0)
                _sb.Append($"\n<color={Soft}>모은 기록 조각 {_explore.Fragments}</color>");
            _status.SetText(_sb.ToString());

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool show = i < _current.Count;
                if (row.Root.gameObject.activeSelf != show)
                    row.Root.gameObject.SetActive(show);
                if (!show)
                    continue;
                row.Site = _current[i];
                FillRow(row, i);
            }

            _sb.Clear();
            foreach (var s in _explore.Sites)
            {
                if (s.Chapter == (_sim.Chapters != null ? _sim.Chapters.Index : 0) || !s.Reached)
                    continue;
                _sb.Append($"· {ExplorationConfig.Name(s.Kind)} ({_sim.Chapters?.GetChapter(s.Chapter).DisplayName}) · {Ongoing(s)}\n");
            }
            _past.SetText(_sb.Length > 0 ? $"<color={Soft}>지난 지점</color>\n{_sb}" : "");
        }

        private string Ongoing(ExploreSite s)
        {
            switch (s.Kind)
            {
                case SiteKind.Asteroid:
                case SiteKind.IceRock:
                    return s.InReach ? $"<color={HudTheme.GreenHex}>{_explore.Config.Effect(s.Kind)}</color>" : $"<color={HudText.Orange}>연결이 끊김 · 다시 이어야 함</color>";
                case SiteKind.DebrisField:
                    return $"<color={HudText.Orange}>파편까지 {Clock(s.HazardTimer)}</color>";
                default:
                    return "기록을 거뒀음";
            }
        }

        private void FillRow(Row row, int index)
        {
            var s = row.Site;
            var cfg = _explore.Config;
            string where = $"{MeteorShowerSystem.DirectionName(s.Direction)} · 지금 <b>{_explore.Distance(s)}칸</b>";
            if (!s.Revealed)
            {
                bool flying = _explore.ProbeTarget == s;
                row.Text.SetText($"<b>신호 {index + 1}</b>  <color={Soft}>{ExplorationConfig.Hint(s.Kind)}</color>\n<size=90%>{where}</size>");
                string blocked = _explore.ProbeBlocked(s);
                row.Button.gameObject.SetActive(true);
                row.Button.interactable = blocked == null;
                row.ButtonText.SetText(flying ? $"날아가는 중 {Clock(_explore.ProbeTimeLeft)}"
                    : blocked == null ? $"탐사선 보내기\n<size=80%>금속 {cfg.ProbeMetal:0}</size>"
                    : $"<size=80%>{blocked}</size>");
                return;
            }
            row.Button.gameObject.SetActive(false);
            string state = s.Reached ? $"<color={HudTheme.GreenHex}>탐사 중</color> · {Ongoing(s)}"
                : $"{cfg.Effect(s.Kind)} · <color={HudText.Orange}>{cfg.ReachRadius}칸 이내 도달 시 탐사 시작</color>";
            row.Text.SetText($"<b>{ExplorationConfig.Name(s.Kind)}</b>  <size=90%>{where}</size>\n<size=90%>{state}</size>");
        }

        private void HandleProbe(Row row)
        {
            if (row.Site == null || !_explore.LaunchProbe(row.Site))
            {
                AudioService.TryPlay(l => l.UiError);
                return;
            }
            Refresh();
        }
    }
}
