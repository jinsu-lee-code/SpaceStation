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
    /// - 맨 아래 가로 카드 '정비 자동화'(단일 연구): 완료 후 자동 정비·재건축 켜기/끄기, 정비 기준 내구도, 자원 보호선, [지금 일괄 정비]
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
            /// <summary>연구 상태 표시 묶음 (자동화 카드는 완료 후 설정으로 바뀌며 숨김).</summary>
            public GameObject ResearchGroup;
        }

        /// <summary>자동화 카드의 설정 (연구 완료 후 표시).</summary>
        private sealed class AutomationControls
        {
            public GameObject Root;
            public Button Maintain;
            public Button Rebuild;
            public TMP_Text ThresholdLabel;
            public TMP_Text ReserveLabel;
            public TMP_Text Status;
            public Button Batch;
        }

        private HoloUi _ui;
        private StationSimulation _sim;
        private CanvasGroup _group;
        private RectTransform _window;
        private UiTween _tween;
        private TMP_Text _subtitle;
        private TMP_Text _tracker;
        private readonly List<Card> _cards = new List<Card>();
        private Card _automationCard;
        private AutomationControls _auto;
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
            KeyBindings.Changed -= RefreshLauncher;
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

        private TMP_Text _launcherLabel;

        private static string LauncherText()
            => $"{HudTheme.Icon("research")} 연구  <size=70%><color=#AFC4D8>{KeyBindings.Label(GameAction.Research)}</color></size>";

        private void RefreshLauncher()
        {
            if (_launcherLabel != null)
                _launcherLabel.SetText(LauncherText());
        }

        private void BuildLauncher()
        {
            var root = (RectTransform)transform;
            var button = _ui.Button(root, LauncherText(), 19f, Toggle);
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
            _window.sizeDelta = new Vector2(1180f, 930f);
            _window.anchoredPosition = new Vector2(-90f, 40f); // 오른쪽 자원 패널·아래 건설 메뉴를 가리지 않게
            _ui.Panel(_window.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.96f), HudTheme.Accent);
            _group = _window.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var title = _ui.Label(_window, $"{HudTheme.Icon("research")} 연구", 32f, TextAlignmentOptions.TopLeft);
            title.fontStyle = FontStyles.Bold;
            HoloUi.Place(title.rectTransform, new Vector2(40f, -26f), new Vector2(500f, 46f));
            _subtitle = _ui.Label(_window, "", 17f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(_subtitle.rectTransform, new Vector2(42f, -78f), new Vector2(1100f, 28f));

            // 레벨형 카테고리는 2열 카드, 자동화(단일 레벨)는 맨 아래 가로 카드
            const float cardW = 540f, cardH = 184f, gap = 14f;
            int slot = 0;
            float bottom = -110f;
            foreach (var category in _sim.Research.Categories)
            {
                if (category.Category == ResearchCategory.Automation)
                    continue;
                float x = 40f + (slot % 2) * (cardW + gap + 6f);
                float y = -110f - (slot / 2) * (cardH + gap);
                _cards.Add(BuildCard(category, new Vector2(x, y), new Vector2(cardW, cardH), false));
                bottom = y - cardH;
                slot++;
            }
            var automation = _sim.Research.Find(ResearchCategory.Automation);
            if (automation != null)
            {
                _automationCard = BuildCard(automation, new Vector2(40f, bottom - gap), new Vector2(cardW * 2f + gap + 6f, 140f), true);
                _cards.Add(_automationCard);
                _auto = BuildAutomationControls((RectTransform)_automationCard.Frame.transform.parent, cardW * 2f + gap + 6f);
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

        /// <param name="wide">가로 카드 (단일 레벨 자동화): 현재 효과 줄 없이 설명·비용·상태를 한 줄씩.</param>
        private Card BuildCard(ResearchCategoryData category, Vector2 topLeft, Vector2 size, bool wide)
        {
            var card = new Card { Category = category };
            var rt = HoloUi.Rect(category.name, _window);
            HoloUi.Place(rt, topLeft, size);
            _ui.Panel(rt.gameObject, new Color(0.05f, 0.11f, 0.16f, 0.9f), HudTheme.AccentDim);
            card.Frame = rt.Find("Frame").GetComponent<Image>();

            card.Title = _ui.Label(rt, "", 22f, TextAlignmentOptions.TopLeft);
            HoloUi.Place(card.Title.rectTransform, new Vector2(20f, -12f), new Vector2(wide ? 700f : 360f, 32f));
            card.Pips = new Image[category.MaxLevel];
            for (int i = 0; i < card.Pips.Length; i++)
            {
                var pip = HoloUi.Rect("Pip", rt);
                HoloUi.Place(pip, new Vector2(size.x - 20f - (card.Pips.Length - i) * 30f, -20f), new Vector2(24f, 9f));
                card.Pips[i] = pip.gameObject.AddComponent<Image>();
                card.Pips[i].raycastTarget = false;
            }

            // 연구 상태 묶음: 자동화 카드는 연구 완료 후 이 자리에 설정이 나온다
            var group = HoloUi.Rect("Research", rt);
            HoloUi.Stretch(group);
            card.ResearchGroup = group.gameObject;
            float textWidth = size.x - 200f; // 버튼은 오른쪽
            float y = -46f;
            if (!wide)
            {
                card.Current = _ui.Label(group, "", 15f, TextAlignmentOptions.TopLeft, wrap: true);
                HoloUi.Place(card.Current.rectTransform, new Vector2(20f, y), new Vector2(size.x - 40f, 34f));
                y -= 34f;
            }
            card.Next = _ui.Label(group, "", 16f, TextAlignmentOptions.TopLeft, wrap: true);
            HoloUi.Place(card.Next.rectTransform, new Vector2(20f, y), new Vector2(wide ? textWidth : size.x - 40f, wide ? 30f : 42f));
            y -= wide ? 32f : 42f;
            card.Cost = _ui.Label(group, "", 15f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(card.Cost.rectTransform, new Vector2(20f, y), new Vector2(textWidth, 24f));
            y -= 24f;
            card.Status = _ui.Label(group, "", 15f, TextAlignmentOptions.MidlineLeft);
            HoloUi.Place(card.Status.rectTransform, new Vector2(20f, y), new Vector2(textWidth, 24f));
            y -= 26f;
            card.Bar = _ui.Bar(group, new Color(0.1f, 0.2f, 0.28f, 1f), HudTheme.Accent, out card.BarFill);
            HoloUi.Place(card.Bar, new Vector2(20f, y), new Vector2(textWidth, 6f));
            float buttonY = wide ? -60f : -124f;
            card.Start = _ui.Button(group, "연구 시작", 17f, () => StartResearch(category));
            HoloUi.Place((RectTransform)card.Start.transform, new Vector2(size.x - 170f, buttonY), new Vector2(150f, 44f));
            card.Cancel = _ui.Button(group, "취소", 16f, () => CancelResearch(category));
            HoloUi.Place((RectTransform)card.Cancel.transform, new Vector2(size.x - 170f, buttonY), new Vector2(150f, 44f));
            return card;
        }

        /// <summary>자동화 설정: [자동 정비] [자동 재건축] 정비 기준 −/+ 자원 보호선 −/+ / 상태 · [지금 일괄 정비].</summary>
        private AutomationControls BuildAutomationControls(RectTransform card, float width)
        {
            var c = new AutomationControls();
            var root = HoloUi.Rect("Automation", card);
            HoloUi.Stretch(root);
            c.Root = root.gameObject;
            var auto = _sim.Automation;

            c.Maintain = _ui.Button(root, "", 16f, () => { auto.AutoMaintain = !auto.AutoMaintain; Refresh(); });
            HoloUi.Place((RectTransform)c.Maintain.transform, new Vector2(20f, -50f), new Vector2(200f, 40f));
            c.Rebuild = _ui.Button(root, "", 16f, () => { auto.AutoRebuild = !auto.AutoRebuild; Refresh(); });
            HoloUi.Place((RectTransform)c.Rebuild.transform, new Vector2(232f, -50f), new Vector2(220f, 40f));

            c.ThresholdLabel = _ui.Label(root, "", 16f, TextAlignmentOptions.MidlineRight);
            HoloUi.Place(c.ThresholdLabel.rectTransform, new Vector2(462f, -50f), new Vector2(206f, 40f));
            Stepper(root, new Vector2(676f, -50f), () => auto.Threshold -= 5f, () => auto.Threshold += 5f);

            c.ReserveLabel = _ui.Label(root, "", 16f, TextAlignmentOptions.MidlineRight);
            HoloUi.Place(c.ReserveLabel.rectTransform, new Vector2(776f, -50f), new Vector2(206f, 40f));
            Stepper(root, new Vector2(990f, -50f), () => auto.ReserveRatio -= 0.1f, () => auto.ReserveRatio += 0.1f);

            c.Status = _ui.Label(root, "", 15f, TextAlignmentOptions.MidlineLeft, wrap: true);
            HoloUi.Place(c.Status.rectTransform, new Vector2(20f, -96f), new Vector2(width - 300f, 40f));
            c.Batch = _ui.Button(root, "지금 일괄 정비", 17f, RunBatch, clickSound: false);
            HoloUi.Place((RectTransform)c.Batch.transform, new Vector2(width - 260f, -94f), new Vector2(240f, 40f));
            return c;
        }

        private void Stepper(RectTransform parent, Vector2 topLeft, System.Action down, System.Action up)
        {
            var minus = _ui.Button(parent, "-", 20f, () => { down(); Refresh(); });
            HoloUi.Place((RectTransform)minus.transform, topLeft, new Vector2(40f, 40f));
            var plus = _ui.Button(parent, "+", 20f, () => { up(); Refresh(); });
            HoloUi.Place((RectTransform)plus.transform, topLeft + new Vector2(46f, 0f), new Vector2(40f, 40f));
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
                ? $"<color={HudText.Orange}>연구소가 없습니다 · 산업 탭에서 연구소를 지으세요</color>"
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

            string progress = max == 1 ? (level >= 1 ? "완료" : "단일 연구") : $"Lv.{level} / {max}";
            card.Title.SetText($"{HudTheme.Icon(category.Icon)} <b>{category.DisplayName}</b>  <size=80%><color=#AFC4D8>{progress}</color></size>");
            for (int i = 0; i < card.Pips.Length; i++)
                card.Pips[i].color = i < level ? HudTheme.Accent : project != null && i == level ? new Color(0.31f, 0.85f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.12f);

            bool single = max == 1;
            if (card == _automationCard)
            {
                bool unlocked = level >= max;
                card.ResearchGroup.SetActive(!unlocked);
                _auto.Root.SetActive(unlocked);
                if (unlocked)
                {
                    card.Frame.color = new Color(0.45f, 1f, 0.6f, 0.6f);
                    RefreshAutomation();
                    return;
                }
            }

            var currentDef = category.GetLevel(level);
            if (card.Current != null)
                card.Current.SetText(currentDef != null ? $"<color=#AFC4D8>현재</color> {currentDef.Description}" : "<color=#AFC4D8>현재 효과 없음</color>");

            var nextDef = category.GetLevel(level + 1);
            if (nextDef == null)
            {
                card.Next.SetText($"<color={HudTheme.GreenHex}>최고 레벨 달성</color>");
                card.Cost.SetText("");
            }
            else
            {
                card.Next.SetText(single ? nextDef.Description : $"<color={HudTheme.AccentHex}>다음 Lv.{level + 1}</color> {nextDef.Description}");
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
            string reason = Reason(check, category, level + 1);
            card.Status.SetText(reason != null ? $"<color={HudText.Orange}>{reason}</color>" : "");
        }

        private string Reason(ResearchStartResult result, ResearchCategoryData category, int nextLevel)
        {
            var research = _sim.Research;
            switch (result)
            {
                case ResearchStartResult.GradeTooLow:
                    int grade = Mathf.Min(research.RequiredGrade(category, nextLevel), _sim.Progression.GradeCount - 1);
                    return $"{_sim.Progression.GetGrade(grade).DisplayName} 등급 필요";
                case ResearchStartResult.PopulationTooLow:
                    return $"인구 {research.RequiredPopulation(nextLevel)}명 필요 (현재 {_sim.Resources.Population})";
                case ResearchStartResult.NoFreeLab:
                    return _sim.Research.LabSlots == 0 ? "연구소 필요" : "빈 연구소 없음";
                case ResearchStartResult.InsufficientResources:
                    return "자원 부족";
                default:
                    return null;
            }
        }

        private void RefreshAutomation()
        {
            var auto = _sim.Automation;
            SetButtonText(_auto.Maintain, $"자동 정비  {OnOff(auto.AutoMaintain)}");
            SetButtonText(_auto.Rebuild, $"자동 재건축  {OnOff(auto.AutoRebuild)}");
            _auto.ThresholdLabel.SetText($"정비 기준 내구도 <b>{auto.Threshold:0}</b>");
            _auto.ReserveLabel.SetText($"자원 보호선 <b>{auto.ReserveRatio * 100f:0}%</b>");

            var jobs = auto.Plan();
            int rebuilds = 0;
            foreach (var job in jobs)
                if (job.Rebuild)
                    rebuilds++;
            var sb = new System.Text.StringBuilder();
            if (jobs.Count == 0)
                sb.Append("<color=#AFC4D8>기준 미만 모듈 없음</color>");
            else
            {
                sb.Append("대상 ").Append(jobs.Count).Append("개");
                if (rebuilds > 0)
                    sb.Append(" (재건축 ").Append(rebuilds).Append(')');
                sb.Append("  ").Append(HudText.Cost(auto.TotalCost(jobs)));
            }
            if (auto.WaitingForReserve)
                sb.Append($"  <color={HudText.Orange}>· 자원 보호선 아래라 대기 중</color>");
            sb.Append($"\n<color=#AFC4D8>자동 처리: 정비 {auto.AutoMaintainCount}회 · 재건축 {auto.AutoRebuildCount}회 · 파손 모듈은 수리가 우선 · 일괄 정비는 보호선을 무시</color>");
            _auto.Status.SetText(sb.ToString());
            _auto.Batch.interactable = jobs.Count > 0;
        }

        private static string OnOff(bool on) => on ? $"<color={HudTheme.GreenHex}>켜짐</color>" : "<color=#AFC4D8>꺼짐</color>";

        private static void SetButtonText(Button button, string text) => button.GetComponentInChildren<TMP_Text>().SetText(text);

        private void RunBatch()
        {
            var (maintained, rebuilt) = _sim.Automation.RunBatch();
            if (maintained + rebuilt > 0)
                AudioService.TryPlay(l => l.BuildSelect);
            else
                AudioService.TryPlay(l => l.UiError);
            Refresh();
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
