using System.Collections.Generic;
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
    /// Phase 6 연구 창 (RESEARCH.md 5번). T 키 또는 왼쪽 위 [연구] 버튼으로 연다. 게임 시간은 멈추지 않는다.
    /// - 카테고리 6개 카드: 현재 레벨·효과, 다음 레벨 효과·시작 비용·전력·소요 시간, [연구 시작] / 진행률·남은 시간·[취소] / 잠김 사유
    /// - 왼쪽 위 추적기: 진행 중인 연구의 진행률 (창을 닫아도 보임)
    /// 내용은 코드로 만든다 (씬에는 글꼴·스프라이트만 연결된 빈 오브젝트). ESC로 닫으면 다른 ESC 처리는 건너뛴다.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    public sealed class ResearchPanel : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private float _refreshSeconds = 0.25f;

        private sealed class Card
        {
            public ResearchCategoryData Category;
            public Image Frame;
            public TMP_Text Title;
            public TMP_Text Current;
            public TMP_Text Next;
            public TMP_Text Cost;
            public TMP_Text Status;
            public Button Start;
            public Button Cancel;
            public RectTransform Bar;
            public RectTransform BarFill;
            public Image[] Pips;
        }

        private HoloUi _ui;
        private StationSimulation _sim;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;
        private TMP_Text _subtitle;
        private TMP_Text _tracker;
        private readonly List<Card> _cards = new List<Card>();
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
            _ui = new HoloUi(_font, _fillSprite, _frameSprite, _buttonSprite);
            BuildLauncher();
            BuildWindow();
            _sim.Research.Completed += HandleCompleted;
            Refresh();
        }

        private void OnDestroy()
        {
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
                if (keyboard.tKey.wasPressedThisFrame)
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
            _open = true;
            _group.blocksRaycasts = true;
            _group.interactable = true;
            _tween.Play();
            Refresh();
            AudioService.TryPlay(l => l.UiOpen);
        }

        public void Close()
        {
            if (!_open)
                return;
            _open = false;
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _tween.Hide();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void HandleCompleted(ResearchCategoryData category, int level)
        {
            AudioService.TryPlay(l => l.EventPositive, 0.8f);
            Refresh();
        }

        // ---------------- 구성 ----------------

        private void BuildLauncher()
        {
            var root = (RectTransform)transform;
            var button = _ui.Button(root, $"{HudTheme.Icon("research")} 연구  <size=70%><color=#AFC4D8>T</color></size>", 19f, Toggle);
            HoloUi.Place((RectTransform)button.transform, new Vector2(24f, -24f), new Vector2(170f, 44f));
            _tracker = _ui.Label(root, "", 15f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_tracker.rectTransform, new Vector2(28f, -76f), new Vector2(420f, 120f));
        }

        private void BuildWindow()
        {
            var root = (RectTransform)transform;
            _window = HoloUi.Rect("ResearchWindow", root);
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.sizeDelta = new Vector2(1180f, 820f);
            _window.anchoredPosition = new Vector2(-90f, 0f); // 오른쪽 자원 패널을 가리지 않게
            _ui.Panel(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.96f), HudTheme.Accent);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var title = _ui.Label(_window, $"{HudTheme.Icon("research")} 연구", 32f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -26f), new Vector2(500f, 46f));
            _subtitle = _ui.Label(_window, "", 17f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_subtitle.rectTransform, new Vector2(42f, -78f), new Vector2(1100f, 28f));

            const float cardW = 540f, cardH = 196f, gap = 20f;
            var categories = _sim.Research.Categories;
            for (int i = 0; i < categories.Count; i++)
            {
                float x = 40f + (i % 2) * (cardW + gap);
                float y = -118f - (i / 2) * (cardH + gap);
                _cards.Add(BuildCard(categories[i], new Vector2(x, y), new Vector2(cardW, cardH)));
            }

            var warn = _ui.Label(_window, $"<color=#AFC4D8>시작 비용은 취소해도 돌려받지 않습니다 · 전력이 부족하면 연구도 느려집니다 · 연구소가 끊기거나 파손되면 멈춥니다</color>", 15f, TextAlignmentOptions.MidlineLeft);
            warn.rectTransform.anchorMin = warn.rectTransform.anchorMax = warn.rectTransform.pivot = new Vector2(0f, 0f);
            warn.rectTransform.anchoredPosition = new Vector2(42f, 34f);
            warn.rectTransform.sizeDelta = new Vector2(880f, 40f);
            var close = _ui.Button(_window, "닫기  <size=70%><color=#AFC4D8>ESC</color></size>", 19f, Close);
            var crt = (RectTransform)close.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0f);
            crt.anchoredPosition = new Vector2(-40f, 28f);
            crt.sizeDelta = new Vector2(190f, 48f);

            _tween = new UiTween(_window, _group, new Vector2(0f, -24f), 0.2f, 0.15f);
        }

        private Card BuildCard(ResearchCategoryData category, Vector2 topLeft, Vector2 size)
        {
            var card = new Card { Category = category };
            var rt = HoloUi.Rect(category.name, _window);
            HoloUi.Place(rt, topLeft, size);
            _ui.Panel(rt.gameObject, new Color(0.05f, 0.11f, 0.16f, 0.9f), HudTheme.AccentDim);
            card.Frame = rt.Find("Frame").GetComponent<Image>();

            card.Title = _ui.Label(rt, "", 22f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(card.Title.rectTransform, new Vector2(20f, -14f), new Vector2(360f, 32f));
            card.Pips = new Image[category.MaxLevel];
            for (int i = 0; i < card.Pips.Length; i++)
            {
                var pip = HoloUi.Rect("Pip", rt);
                HoloUi.Place(pip, new Vector2(size.x - 20f - (card.Pips.Length - i) * 30f, -22f), new Vector2(24f, 9f));
                card.Pips[i] = pip.gameObject.AddComponent<Image>();
                card.Pips[i].raycastTarget = false;
            }
            card.Current = _ui.Label(rt, "", 15f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(card.Current.rectTransform, new Vector2(20f, -50f), new Vector2(size.x - 40f, 36f));
            card.Next = _ui.Label(rt, "", 16f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(card.Next.rectTransform, new Vector2(20f, -88f), new Vector2(size.x - 40f, 44f));
            // 아래 두 줄(비용 / 상태·진행 막대)은 왼쪽, 버튼은 오른쪽
            float textWidth = size.x - 200f;
            card.Cost = _ui.Label(rt, "", 15f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(card.Cost.rectTransform, new Vector2(20f, -132f), new Vector2(textWidth, 26f));
            card.Status = _ui.Label(rt, "", 15f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(card.Status.rectTransform, new Vector2(20f, -158f), new Vector2(textWidth, 24f));
            card.Bar = _ui.Bar(rt, new Color(0.1f, 0.2f, 0.28f, 1f), HudTheme.Accent, out card.BarFill);
            HoloUi.Place(card.Bar, new Vector2(20f, -182f), new Vector2(textWidth, 6f));
            card.Start = _ui.Button(rt, "연구 시작", 17f, () => StartResearch(category));
            HoloUi.Place((RectTransform)card.Start.transform, new Vector2(size.x - 170f, -136f), new Vector2(150f, 44f));
            card.Cancel = _ui.Button(rt, "취소", 16f, () => CancelResearch(category));
            HoloUi.Place((RectTransform)card.Cancel.transform, new Vector2(size.x - 170f, -136f), new Vector2(150f, 44f));
            return card;
        }

        // ---------------- 명령 ----------------

        private void StartResearch(ResearchCategoryData category)
        {
            var result = _sim.TryStartResearch(category);
            if (result == ResearchStartResult.Ok)
                AudioService.TryPlay(l => l.BuildSelect);
            else
                AudioService.TryPlay(l => l.UiError);
            Refresh();
        }

        private void CancelResearch(ResearchCategoryData category)
        {
            if (_sim.CancelResearch(category))
                AudioService.TryPlay(l => l.UiClose);
            Refresh();
        }

        // ---------------- 표시 ----------------

        private void Refresh()
        {
            var research = _sim.Research;
            int running = Mathf.Min(research.Projects.Count, research.LabSlots);
            string labs = research.LabSlots == 0
                ? $"<color={HudText.Orange}>연구소가 없습니다 — 산업 탭에서 연구소를 지으세요</color>"
                : $"연구소 {running} / {research.LabSlots} 사용 중  <color=#AFC4D8>(연구소마다 동시에 하나씩, 같은 분야는 한 번에 하나)</color>";
            if (_open)
            {
                _subtitle.SetText(labs);
                foreach (var card in _cards)
                    RefreshCard(card);
            }
            RefreshTracker();
        }

        private void RefreshCard(Card card)
        {
            var research = _sim.Research;
            var category = card.Category;
            int level = research.GetLevel(category);
            int max = category.MaxLevel;
            var project = research.GetProject(category);

            card.Title.SetText($"{HudTheme.Icon(category.Icon)} <b>{category.DisplayName}</b>  <size=80%><color=#AFC4D8>Lv.{level} / {max}</color></size>");
            for (int i = 0; i < card.Pips.Length; i++)
                card.Pips[i].color = i < level ? HudTheme.Accent : project != null && i == level ? new Color(0.31f, 0.85f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.12f);

            var currentDef = category.GetLevel(level);
            card.Current.SetText(currentDef != null ? $"<color=#AFC4D8>현재</color> {currentDef.Description}" : "<color=#AFC4D8>현재 효과 없음</color>");

            var nextDef = category.GetLevel(level + 1);
            if (nextDef == null)
            {
                card.Next.SetText($"<color={HudTheme.GreenHex}>최고 레벨 달성</color>");
                card.Cost.SetText("");
            }
            else
            {
                card.Next.SetText($"<color={HudTheme.AccentHex}>다음 Lv.{level + 1}</color> {nextDef.Description}");
                card.Cost.SetText($"{HudText.Cost(nextDef.StartCost)}  <color=#AFC4D8>· 전력 +{nextDef.PowerDemand:0} · {nextDef.Duration:0}초</color>");
            }

            bool researching = project != null;
            card.Bar.gameObject.SetActive(researching);
            card.Cancel.gameObject.SetActive(researching);
            card.Start.gameObject.SetActive(!researching && nextDef != null);
            card.Frame.color = researching ? HudTheme.Accent : level >= max ? new Color(0.45f, 1f, 0.6f, 0.6f) : HudTheme.AccentDim;

            if (researching)
            {
                HoloUi.SetBar(card.BarFill, project.Progress);
                card.Status.SetText(project.Paused
                    ? $"<color={HudText.Orange}>연구 중 {project.Progress * 100f:0}% · 멈춤 (연구소 부족)</color>"
                    : $"연구 중 {project.Progress * 100f:0}% · 남은 {FormatTime(project.RemainingSeconds)}{(project.Rate < 0.999f ? $" <color={HudText.Yellow}>(전력 {project.Rate * 100f:0}%)</color>" : "")}");
                return;
            }
            if (nextDef == null)
            {
                card.Status.SetText("");
                return;
            }
            var check = _sim.CanStartResearch(category);
            card.Start.interactable = check == ResearchStartResult.Ok;
            string reason = Reason(check, level + 1);
            card.Status.SetText(reason != null ? $"<color={HudText.Orange}>{reason}</color>" : "");
        }

        private string Reason(ResearchStartResult result, int nextLevel)
        {
            var caps = _sim.Research.Caps;
            var req = caps != null ? caps.Get(nextLevel) : default;
            switch (result)
            {
                case ResearchStartResult.GradeTooLow:
                    return $"{_sim.Progression.GetGrade(Mathf.Min(req.Grade, _sim.Progression.GradeCount - 1)).DisplayName} 등급 필요";
                case ResearchStartResult.PopulationTooLow:
                    return $"인구 {req.MinPopulation}명 필요 (현재 {_sim.Resources.Population})";
                case ResearchStartResult.NoFreeLab:
                    return _sim.Research.LabSlots == 0 ? "연구소 필요" : "빈 연구소 없음";
                case ResearchStartResult.InsufficientResources:
                    return "자원 부족";
                default:
                    return null;
            }
        }

        private void RefreshTracker()
        {
            var research = _sim.Research;
            if (research.Projects.Count == 0)
            {
                _tracker.SetText(research.LabSlots > 0 ? "<color=#AFC4D8>진행 중인 연구 없음</color>" : "");
                return;
            }
            var sb = new System.Text.StringBuilder();
            foreach (var p in research.Projects)
            {
                if (sb.Length > 0)
                    sb.Append('\n');
                sb.Append(HudTheme.Icon(p.Category.Icon)).Append(' ').Append(p.Category.DisplayName).Append(" Lv.").Append(p.TargetLevel)
                  .Append("  ").Append((p.Progress * 100f).ToString("0")).Append('%');
                sb.Append(p.Paused ? $" <color={HudText.Orange}>멈춤</color>" : $" <color=#AFC4D8>{FormatTime(p.RemainingSeconds)}</color>");
            }
            _tracker.SetText(sb.ToString());
        }

        private static string FormatTime(float seconds)
        {
            if (float.IsInfinity(seconds) || seconds > 99 * 60)
                return "-";
            int s = Mathf.CeilToInt(seconds);
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
