using System.Text;
using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-1 장 틀 화면 (그레이박스, 사용자 결정 2026-10-15):
    /// - 오른쪽 장 카드 (정거장 패널 아래): 장 이름 · 준비 목표(필수 · 선택 + 보상, 진행 수치) · 단계 줄
    ///   (준비 = 안내 / 예고 = 절정까지 남은 시간 + [바로 맞이하기] / 절정 = 진행 막대 + 상황).
    /// - 장 결과 · 방향 선택 (가운데, 시간 멈춤): 절정 결과 3등급 + 사유 + 걸린 시간, 방향 선택지 버튼 (없으면 [다음 장으로]).
    /// - 캠페인 끝 "이어질 이야기" (마지막 장 전환 뒤): [계속 플레이] [메인 메뉴].
    /// 등급 ★ 배너 · 결과 화면은 이것으로 대체된다 (ResultScreen이 장 틀이면 건너뜀). 내용은 코드로 만든다.
    /// </summary>
    [DefaultExecutionOrder(-240)]
    public sealed class ChapterView : MonoBehaviour
    {
        private const float CardWidth = 430f;
        private const float ModalWidth = 900f;
        private const string Soft = "#C8D2DE";
        private const string Gold = "#FFD36A";
        /// <summary>13-2 코어의 말 색 (로그 창과 같게).</summary>
        public const string CoreTint = "#9FE8FF";

        [SerializeField] private StationController _station;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Sprite _fillSprite;
        [SerializeField] private Sprite _frameSprite;
        [SerializeField] private Sprite _buttonSprite;
        [SerializeField] private HoloArt _holoArt;
        [Tooltip("정거장 패널 (카드를 그 아래 끝에 붙임 — 경고 줄이 생기면 패널이 길어짐). 없으면 카드 위치 고정")]
        [SerializeField] private RectTransform _stationPanel;
        [Tooltip("카드 위치 (오른쪽 위 기준: 오른쪽 끝에서, 위 끝에서) — 정거장 패널이 없을 때")]
        [SerializeField] private Vector2 _cardOffset = new Vector2(-24f, -408f);
        [SerializeField] private float _panelGap = 14f;
        [SerializeField] private float _refreshSeconds = 0.2f;

        private HoloUi _ui;
        private StationSimulation _sim;
        private ChapterSystem _chapters;
        private float _nextRefresh;

        // 카드
        private RectTransform _cardHolder;
        private RectTransform _card;
        private UiTween _cardTween;
        private TMP_Text _cardHeader, _cardTitle, _goals, _phase;
        private RectTransform _barRoot, _barFill;
        private Button _climaxNow;
        // 13-3 이민선 (카드 아래 줄 + 도착 창), 13-6b 처음 만나는 종족은 도킹 연출 뒤 도착 창
        private ArkShipSystem _ships;
        private ArkShipView _arkView;
        private TMP_Text _shipLine;
        // 13-5a 탐사 (카드 줄)
        private ExplorationSystem _explore;
        private TMP_Text _exploreLine;
        private readonly System.Collections.Generic.List<ExploreSite> _sites = new System.Collections.Generic.List<ExploreSite>();
        // 13-5b 기록 복원 (조각 줄 + 봉인 기록 선택 창)
        private RecordArchiveSystem _records;
        // 13-6a 긴장도 (이민선 줄 아래 · 장 결과 창)
        private TensionSystem _tension;
        // 13-4 내부 안내 (방주 안 둘러보기)
        private Interior.InteriorMode _interior;
        private TutorialRunner _guide;

        // 결과 · 선택 / 캠페인 끝
        private RectTransform _modalRoot;
        private CanvasGroup _modalGroup;
        private RectTransform _modal;
        private UiTween _modalTween;
        private HoloFx _modalFx;
        private TMP_Text _modalTitle, _modalBody;
        private RectTransform _buttonRow;
        private bool _modalOpen;

        private readonly StringBuilder _sb = new StringBuilder();

        private void Start()
        {
            _sim = _station != null ? _station.Simulation : null;
            _chapters = _sim?.Chapters;
            if (_chapters == null)
            {
                gameObject.SetActive(false);
                return;
            }
            _ui = HoloUi.For(_holoArt, _font, _fillSprite, _frameSprite, _buttonSprite);
            BuildCard();
            BuildModal();
            _chapters.ClimaxEnded += HandleClimaxEnded;
            _chapters.ChapterStarted += HandleChapterStarted;
            _chapters.CampaignFinished += HandleCampaignFinished;
            _ships = _sim.GetSystem<ArkShipSystem>();
            if (_ships != null)
            {
                _ships.Arrived += HandleShipArrived;
                var ships = _ships;
                ResidentText.KindTag = r => ships.Find(r.Kind)?.DisplayName; // 주민 명단: "토끼 | 기술자·낙천가" (종족 기본 | 개인)
            }
            _explore = _sim.GetSystem<ExplorationSystem>();
            _records = _sim.GetSystem<RecordArchiveSystem>();
            _tension = _sim.GetSystem<TensionSystem>();
            _arkView = FindFirstObjectByType<ArkShipView>(FindObjectsInactive.Include);
            _interior =FindFirstObjectByType<Interior.InteriorMode>(FindObjectsInactive.Include);
            if (_interior != null)
                _interior.InsideChanged += HandleInsideChanged;
            _cardTween.Play(restart: true);
            // 불러온 판이 전환 · 끝 상태면 그 화면부터
            if (_chapters.Phase == ChapterPhase.Transition)
                ShowResult(_chapters.LastRecord);
            else if (_chapters.Phase == ChapterPhase.Finished)
                ShowEnding();
            else if (_ships?.Pending != null)
                ShowShip(_ships.Pending);
            Refresh();
        }

        private void OnDestroy()
        {
            if (_chapters == null)
                return;
            _chapters.ClimaxEnded -= HandleClimaxEnded;
            _chapters.ChapterStarted -= HandleChapterStarted;
            _chapters.CampaignFinished -= HandleCampaignFinished;
            if (_ships != null)
            {
                _ships.Arrived -= HandleShipArrived;
                ResidentText.KindTag = null;
            }
            if (_interior != null)
                _interior.InsideChanged -= HandleInsideChanged;
            if (_guide != null)
                _guide.Finished -= HandleGuideFinished;
        }

        // ---------------- 13-4 내부 안내 ----------------

        /// <summary>
        /// 들어가면 그 장의 내부 안내를 시작 (코어 기동이 끝났고 · 아직 다 안 했고 · 준비 · 예고 중일 때). 나오면 중간이어도 멈추고,
        /// 다음에 들어가면 처음부터. 끝까지 하면 표시 'interior' (1장 선택 목표). 안내는 저장하지 않음 (데이터 _transient).
        /// 들어간 순간 알림(InsideChanged)이 튜토리얼 '들어가기' 보고보다 먼저라, 새 안내의 첫 단계(들어가기)가 바로 채워진다.
        /// </summary>
        private void HandleInsideChanged(bool inside)
        {
            if (!inside)
            {
                if (_guide != null && _guide.Active)
                    _guide.Skip();
                return;
            }
            var data = _chapters.Current.InteriorGuide;
            if (data == null || _chapters.HasFlag(ChapterSystem.InteriorFlag))
                return;
            if (_chapters.Phase != ChapterPhase.Preparation && _chapters.Phase != ChapterPhase.Warning)
                return;
            var running = _sim.Tutorial;
            if (running != null && running.Active)
                return;
            if (_guide != null)
                _guide.Finished -= HandleGuideFinished;
            _guide = _sim.StartTutorial(data);
            _guide.Finished += HandleGuideFinished;
        }

        private void HandleGuideFinished(bool skipped)
        {
            if (!skipped)
                _chapters.SetFlag(ChapterSystem.InteriorFlag);
        }

        private void Update()
        {
            if (_ui == null)
                return;
            _cardTween.Update();
            _modalTween.Update();
            if (_sim.Session.IsGameOver)
            {
                if (_cardTween.Visible)
                    _cardTween.Hide();
                if (_modalOpen)
                    CloseModal(resume: false);
                return;
            }
            FollowStationPanel();
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + _refreshSeconds;
                Refresh();
            }
        }

        private readonly Vector3[] _corners = new Vector3[4];

        /// <summary>정거장 패널 아래 끝에 카드를 붙임 (자원 경고 줄로 패널 높이가 바뀜).</summary>
        private void FollowStationPanel()
        {
            if (_stationPanel == null || !_stationPanel.gameObject.activeInHierarchy)
                return;
            var self = (RectTransform)transform;
            _stationPanel.GetWorldCorners(_corners);
            float bottom = self.InverseTransformPoint(_corners[0]).y;
            float y = Mathf.Round(bottom - self.rect.yMax - _panelGap - _cardOffset.y); // 카드 자리는 트윈이 정하므로 담는 칸을 옮김
            if (Mathf.Abs(_cardHolder.anchoredPosition.y - y) > 0.5f)
                _cardHolder.anchoredPosition = new Vector2(0f, y);
        }

        // ---------------- 만들기 ----------------

        private void BuildCard()
        {
            _cardHolder = HoloUi.Rect("CardHolder", transform);
            HoloUi.Stretch(_cardHolder);
            _card = HoloUi.Rect("ChapterCard", _cardHolder);
            _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(1f, 1f);
            _card.anchoredPosition = _cardOffset;
            _ui.Panel(_card.gameObject, HudTheme.PanelFill, HudTheme.Accent);
            HoloUi.Decorative((RectTransform)_card.Find("Frame"));
            var group = _card.gameObject.AddComponent<CanvasGroup>();
            var layout = _card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14);
            layout.spacing = 6f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = _card.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _card.sizeDelta = new Vector2(CardWidth, 0f);

            _cardHeader = _ui.Label(_card, "", HudTheme.SmallSize, TextAlignmentOptions.Left);
            _cardTitle = _ui.Label(_card, "", HudTheme.TitleSize, TextAlignmentOptions.Left, wrap: true);
            _cardTitle.fontStyle = FontStyles.Bold;
            HoloUi.Glow(_cardTitle, 0.4f);
            _goals = _ui.Label(_card, "", 17f, TextAlignmentOptions.TopLeft, wrap: true);
            _phase = _ui.Label(_card, "", 17f, TextAlignmentOptions.TopLeft, wrap: true);
            _shipLine = _ui.Label(_card, "", 17f, TextAlignmentOptions.TopLeft, wrap: true);
            _shipLine.gameObject.SetActive(false);
            _exploreLine = _ui.Label(_card, "", 17f, TextAlignmentOptions.TopLeft, wrap: true);
            _exploreLine.gameObject.SetActive(false);

            _barRoot = _ui.Bar(_card, new Color(1f, 1f, 1f, 0.12f), HudTheme.Negative, out _barFill);
            _barRoot.gameObject.AddComponent<LayoutElement>().preferredHeight = 8f;

            var row = HoloUi.Rect("Buttons", _card);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
            _climaxNow = _ui.TechButton(row, "바로 맞이하기", HudTheme.SmallSize, HandleClimaxNow);
            var crt = (RectTransform)_climaxNow.transform;
            crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0.5f);
            crt.anchoredPosition = Vector2.zero;
            crt.sizeDelta = new Vector2(190f, 36f);

            HoloGlitch.Add(_card, _holoArt);
            _cardTween = new UiTween(_card, group, new Vector2(30f, 0f), 0.25f, 0.2f, glitch: true);
        }

        private void BuildModal()
        {
            _modalRoot = HoloUi.Rect("ChapterModal", transform);
            HoloUi.Stretch(_modalRoot);
            var dim = _modalRoot.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0.01f, 0.02f, 0.62f);
            _modalGroup = _modalRoot.gameObject.AddComponent<CanvasGroup>();

            _modal = HoloUi.Rect("Window", _modalRoot);
            _modal.anchorMin = _modal.anchorMax = _modal.pivot = new Vector2(0.5f, 0.5f);
            _modal.sizeDelta = new Vector2(ModalWidth, 560f);
            _modalFx = _ui.Window(_modal.gameObject, new Color(0.02f, 0.045f, 0.07f, 0.99f), "CHAPTER");
            var back = HoloUi.Rect("Backing", _modal);
            HoloUi.Stretch(back, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            back.SetAsFirstSibling();
            var bi = back.gameObject.AddComponent<Image>();
            bi.color = new Color(0.015f, 0.03f, 0.05f, 0.97f);
            bi.raycastTarget = false;

            _modalTitle = _ui.Label(_modal, "", 40f, TextAlignmentOptions.Top, wrap: true);
            _modalTitle.fontStyle = FontStyles.Bold;
            HoloUi.Place(_modalTitle.rectTransform, new Vector2(40f, -34f), new Vector2(ModalWidth - 80f, 110f));
            HoloUi.Glow(_modalTitle, 0.5f);
            _modalBody = _ui.Label(_modal, "", 20f, TextAlignmentOptions.Top, wrap: true);
            HoloUi.Place(_modalBody.rectTransform, new Vector2(60f, -150f), new Vector2(ModalWidth - 120f, 170f));

            _buttonRow = HoloUi.Rect("Choices", _modal);
            _buttonRow.anchorMin = new Vector2(0f, 0f);
            _buttonRow.anchorMax = new Vector2(1f, 0f);
            _buttonRow.pivot = new Vector2(0.5f, 0f);
            _buttonRow.anchoredPosition = new Vector2(0f, 36f);
            _buttonRow.sizeDelta = new Vector2(-80f, 200f);
            var h = _buttonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 16f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.LowerCenter;

            HoloGlitch.Add(_modal, _holoArt);
            _modalTween = new UiTween(_modal, _modalGroup, new Vector2(0f, -24f), 0.25f, 0.2f, glitch: true);
            _modalTween.HideNow();
            _modalGroup.blocksRaycasts = false;
            _modalGroup.interactable = false;
        }

        // ---------------- 카드 ----------------

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private static string Number(float v) => Mathf.Abs(v - Mathf.Round(v)) < 0.05f ? $"{Mathf.Round(v):0}" : $"{v:0.#}";

        private void Refresh()
        {
            var ch = _chapters;
            var data = ch.Current;
            _cardHeader.SetText($"<color={Soft}>CHAPTER {ch.Index + 1} / {ch.Count}</color>");
            _cardTitle.SetText(string.IsNullOrEmpty(data.Subtitle) ? data.DisplayName : $"{data.DisplayName}  <size=70%><color={Soft}>{data.Subtitle}</color></size>");

            _sb.Clear();
            var goals = data.Goals;
            for (int i = 0; i < goals.Count; i++)
            {
                var g = goals[i];
                bool done = ch.IsGoalDone(i);
                ch.GoalProgress(i, out float cur, out float req);
                string box = done ? $"<color={HudTheme.GreenHex}>■</color>" : "□";
                string tag = g.Optional ? $"<color={Soft}>(선택)</color> " : "";
                string count = req > 1f && !done ? $"  <color={Soft}>{Number(cur)}/{Number(req)}</color>" : "";
                string text = done ? $"<color={Soft}>{ch.GoalText(i)}</color>" : ch.GoalText(i);
                _sb.Append($"{box} {tag}{text}{count}");
                if (g.Optional && g.HasReward && !done)
                    _sb.Append($"\n    <size=86%><color={Gold}>보상 {ChapterSystem.RewardText(g)}</color></size>");
                if (i < goals.Count - 1)
                    _sb.Append('\n');
            }
            if (goals.Count == 0)
                _sb.Append($"<color={Soft}>목표 없음</color>");
            _goals.SetText(_sb.ToString());

            var climax = data.Climax;
            bool showBar = false, showNow = false;
            switch (ch.Phase)
            {
                case ChapterPhase.Preparation:
                    _phase.SetText($"<color={Soft}>필수 목표를 모두 채우면 절정 예고: <b>{climax.Title}</b></color>");
                    break;
                case ChapterPhase.Warning:
                    _phase.SetText($"{HudTheme.Icon("warning")} <color={HudText.Orange}><b>{climax.Title}</b>까지 {Clock(ch.WarningLeft)}</color>"
                                   + (string.IsNullOrEmpty(climax.Description) ? "" : $"\n<size=86%><color={Soft}>{climax.Description}</color></size>"));
                    showNow = true;
                    break;
                case ChapterPhase.Climax:
                    _phase.SetText($"{HudTheme.Icon("warning")} <color={HudText.Red}><b>{climax.Title}</b></color>  <size=86%>{ch.Climax?.Status}</size>");
                    showBar = true;
                    if (ch.Climax != null)
                        HoloUi.SetBar(_barFill, ch.Climax.Progress);
                    break;
                case ChapterPhase.Transition:
                    _phase.SetText($"<color={Soft}>방주의 방향을 고르세요</color>");
                    break;
                default:
                    _phase.SetText($"<color={Gold}>이야기는 여기까지 · 계속 플레이 중</color>");
                    break;
            }
            if (_barRoot.gameObject.activeSelf != showBar)
                _barRoot.gameObject.SetActive(showBar);
            if (_climaxNow.transform.parent.gameObject.activeSelf != showNow)
                _climaxNow.transform.parent.gameObject.SetActive(showNow);
            RefreshShipLine();
            RefreshExploreLine();
            // 13-5b 봉인 단계를 마친 기록 → 선택 창 (다른 창이 열려 있으면 닫힌 뒤, 불러온 판도 여기서)
            var pending = _records?.Pending;
            if (pending != null && !_modalOpen && _chapters.Phase != ChapterPhase.Transition && _chapters.Phase != ChapterPhase.Finished)
                ShowRecord(pending);
        }

        /// <summary>13-5a 카드 아래 탐사 줄: 센서 · 고를 신호 · 탐사선 비행 · 밝힌 지점까지 거리 · 닿음.</summary>
        private void RefreshExploreLine()
        {
            string text = "";
            if (_explore != null && _explore.ChapterHasSignals)
            {
                _explore.CurrentSites(_sites);
                ExploreSite revealed = null;
                foreach (var s in _sites)
                    if (s.Revealed)
                        revealed = s;
                if (_sites.Count == 0)
                    text = _explore.SensorActive ? "" : $"<color={Soft}>탐사 · 센서 어레이를 지으면 신호가 잡힙니다</color>";
                else if (_explore.ProbeInFlight)
                    text = $"탐사 · 탐사선 도착까지 {Clock(_explore.ProbeTimeLeft)}";
                else if (revealed == null)
                    text = _explore.ProbeUsedThisChapter ? "" : $"{HudTheme.Icon("warning")} <color={Gold}>탐사 · 신호 {_sites.Count}개</color> <size=86%><color={Soft}>[관리] → 탐사에서 하나를 고르세요</color></size>";
                else if (!revealed.Reached)
                    text = $"탐사 · <b>{ExplorationConfig.Name(revealed.Kind)}</b> {MeteorShowerSystem.DirectionName(revealed.Direction)} · 지금 {_explore.Distance(revealed)}칸 <size=86%><color={Soft}>({_explore.Config.ReachRadius}칸 이내 도달 시 탐사 시작)</color></size>";
                else
                    text = $"<color={HudTheme.GreenHex}>탐사 · {ExplorationConfig.Name(revealed.Kind)} 탐사 중</color>";
            }
            // 13-5b 가진 기록 조각 (봉인된 기록을 푸는 데 씀)
            if (_records != null && _records.IsOpen && _records.Fragments > 0)
            {
                if (text.Length > 0)
                    text += "\n";
                text += $"<color={Gold}>기록 조각 {_records.Fragments}</color> <size=86%><color={Soft}>[관리] → {_records.Title}에서 봉인된 기록을 풂</color></size>";
            }
            bool show = text.Length > 0;
            if (_exploreLine.gameObject.activeSelf != show)
                _exploreLine.gameObject.SetActive(show);
            if (show)
                _exploreLine.SetText(text);
        }

        // ---------------- 13-3 이민선 ----------------

        /// <summary>카드 아래 줄: 다음 배 시각 (예보 뒤엔 종족 · 규모), 위기면 늦어짐, 절정 중이면 멈춤.</summary>
        private void RefreshShipLine()
        {
            string text = "";
            if (_ships != null && _ships.ClockOn)
            {
                var next = _ships.NextShip;
                string slow = _ships.Slowed ? $"\n<size=86%><color={HudText.Orange}>정거장이 흔들려 배가 사정을 보며 늦게 옵니다</color></size>" : "";
                bool held = _chapters.Phase == ChapterPhase.Climax || _chapters.Phase == ChapterPhase.Transition;
                string clock = held ? "대기 중" : $"{ArkShipSystem.Clock(_ships.TimeLeft)} 뒤";
                if (_chapters.Phase != ChapterPhase.Climax || _chapters.Current.Climax.Kind != ChapterClimaxKind.GreatMigration) // 13-6c 대이주 중엔 절정 줄이 배를 보여 줌
                    text = (next != null
                        ? $"{HudTheme.Icon("warning")} <color={Gold}>이민선 예보</color>  <b>{_ships.SpeciesName(next.Species)} {next.Size}명</b> · {clock}"
                          + (next.NewSpecies ? $"\n<size=86%><color={Gold}>처음 만나는 종족</color></size>" : "")
                        : $"<color={Soft}>다음 이민선 · {clock}</color>") + "\n";
                text += $"<size=86%><color={Soft}>빈 집 {_ships.FreeHousing} · 무리 {_ships.Composition()}</color></size>" + slow;
            }
            string tension = TensionText(); // 13-6a 긴장도 (값 + 표시만)
            if (tension.Length > 0)
                text += (text.Length > 0 ? "\n" : "") + tension;
            bool show = text.Length > 0;
            if (_shipLine.gameObject.activeSelf != show)
                _shipLine.gameObject.SetActive(show);
            if (show)
                _shipLine.SetText(text);
        }

        /// <summary>13-6a 긴장도 줄: "긴장도 32 · 술렁임 (오름: 과밀 · 정책 반발)". 0이고 오르지 않으면 숨김.</summary>
        private string TensionText()
        {
            if (_tension == null)
                return "";
            string rising = _tension.RisingCauses();
            if (_tension.Value < 0.5f && rising.Length == 0)
                return "";
            string color = _tension.Level switch { 0 => Soft, 1 => Gold, 2 => HudText.Orange, _ => HudText.Red };
            return $"<color={color}>긴장도 {_tension.Value:0} · {_tension.LevelName}</color>"
                   + (rising.Length > 0 ? $" <size=86%><color={Soft}>(오름: {rising})</color></size>" : "");
        }

        private void HandleShipArrived(ArkShip ship)
        {
            if (_arkView != null && _arkView.WantsArrival(ship)) // 13-6b 처음 만나는 종족: 도킹 연출 → 코어 독백 → 도착 창
                _arkView.PlayArrival(ship, () =>
                {
                    if (_ships.Pending == ship)
                        ShowShip(ship);
                });
            else
                ShowShip(ship);
        }

        /// <summary>도착 창 (시간 멈춤): 종족 · 규모 · 종족 기본값 · 빈 집 → 모두 받기 / 빈 집만큼 / 거부.</summary>
        private void ShowShip(ArkShip ship)
        {
            var species = _ships.Find(ship.Species);
            string name = _ships.SpeciesName(ship.Species);
            string convoy = ship.Convoy ? $"  <color={HudText.Red}>· {_chapters.Current.Climax.Title} {ship.ConvoyOrder}/{_chapters.Current.Climax.MigrationShips}</color>" : ""; // 13-6c
            _modalTitle.SetText($"이민선 도착\n<size=70%><color={Gold}>{name} {ship.Size}명</color>{(ship.NewSpecies ? $"  <color={Soft}>· 처음 만나는 종족</color>" : "")}{convoy}</size>");
            _sb.Clear();
            if (species != null)
            {
                if (!string.IsNullOrEmpty(species.Description))
                    _sb.Append($"<color={Soft}>{species.Description}</color>\n");
                _sb.Append($"<size=90%>종족 기본: {species.Summary()}</size>\n");
            }
            int free = _ships.FreeHousing;
            _sb.Append($"\n빈 집 <b>{free}</b>  ·  지금 무리 {_ships.Composition()}");
            if (ship.Size > free)
                _sb.Append($"\n<size=90%><color={HudText.Orange}>모두 받으면 집이 {ship.Size - free}개 모자라 과밀 · 주민이 하나둘 떠날 수 있습니다</color></size>");
            _modalBody.SetText(_sb.ToString());

            ClearButtons();
            AddShipButton(ShipAnswer.Accept, $"<b>모두 받기</b>\n<size=78%><color={Soft}>{ship.Size}명</color></size>", _ships.Config.AcceptShifts);
            AddShipButton(ShipAnswer.Partial, $"<b>빈 집만큼</b>\n<size=78%><color={Soft}>{Mathf.Min(ship.Size, free)}명 받고 {Mathf.Max(0, ship.Size - free)}명 돌려보냄</color></size>", _ships.Config.PartialShifts);
            AddShipButton(ShipAnswer.Refuse, $"<b>거부</b>\n<size=78%><color={Soft}>{ship.Size}명 모두 돌려보냄</color></size>", _ships.Config.RefuseShifts);
            OpenModal();
        }

        private void AddShipButton(ShipAnswer answer, string label, System.Collections.Generic.IReadOnlyList<AxisShift> shifts)
        {
            string lean = LeanText(shifts);
            if (lean.Length > 0)
                label += $"\n<size=70%><color={CoreTint}>코어: {lean}</color></size>";
            AddButton(label, () => AnswerShip(answer), tall: true);
        }

        private void AnswerShip(ShipAnswer answer)
        {
            if (!_ships.Answer(answer))
            {
                AudioService.TryPlay(l => l.UiError);
                return;
            }
            AudioService.TryPlay(l => l.UiClick);
            CloseModal(resume: true);
            Refresh();
        }

        // ---------------- 13-5b 봉인 기록 ----------------

        /// <summary>봉인 단계를 마친 기록 (시간 멈춤): 되살린 기억 · 인간 미스터리 한 줄 → 보존 / 덮어쓰기.</summary>
        private void ShowRecord(RecordTrack track)
        {
            var config = _records.Config;
            int sealedLevel = config.SealedLevel;
            _modalTitle.SetText($"봉인된 기록 복원\n<size=70%><color={Gold}>{track.Category.DisplayName}</color></size>");
            _sb.Clear();
            _sb.Append($"<color={Soft}>{track.Memory(sealedLevel)}</color>\n");
            if (!string.IsNullOrEmpty(track.SealedHuman))
                _sb.Append($"\n<color={CoreTint}>{track.SealedHuman}</color>\n");
            _sb.Append("\n<size=90%>원본을 남기면 기본 효과, 지우고 그 저장 공간을 쓰면 효과가 1.5배가 됩니다. 한 번 고르면 바꿀 수 없습니다.</size>");
            _modalBody.SetText(_sb.ToString());

            ClearButtons();
            var level = track.Category.GetLevel(sealedLevel);
            AddRecordButton(track, true, $"<b>보존</b>\n<size=78%><color={Soft}>{level?.Description} · 기록을 남김</color></size>", config.PreserveShifts);
            AddRecordButton(track, false, $"<b>덮어쓰기</b>\n<size=78%><color={Soft}>{track.OverwriteDescription} · 원본은 지워짐</color></size>", config.OverwriteShifts);
            OpenModal();
        }

        private void AddRecordButton(RecordTrack track, bool preserve, string label, System.Collections.Generic.IReadOnlyList<AxisShift> shifts)
        {
            string lean = LeanText(shifts);
            if (lean.Length > 0)
                label += $"\n<size=70%><color={CoreTint}>코어: {lean}</color></size>";
            AddButton(label, () => AnswerRecord(track, preserve), tall: true);
        }

        private void AnswerRecord(RecordTrack track, bool preserve)
        {
            if (!_records.Choose(track, preserve))
            {
                AudioService.TryPlay(l => l.UiError);
                return;
            }
            AudioService.TryPlay(l => l.UiClick);
            CloseModal(resume: true);
            Refresh();
        }

        private void HandleClimaxNow()
        {
            if (_chapters.StartClimaxNow())
                AudioService.TryPlay(l => l.UiClick);
            Refresh();
        }

        // ---------------- 결과 · 선택 ----------------

        private void HandleClimaxEnded(ChapterRecord record) => ShowResult(record);

        /// <summary>결과 이름 (절정 설정에 이름이 있으면 그것 — 13-6c 대이주 = 맞이함, 사용자 요청 2026-10-19).</summary>
        private static string OutcomeText(ChapterOutcome o, ChapterClimaxSettings climax)
        {
            string Name(string custom, string fallback) => string.IsNullOrEmpty(custom) ? fallback : custom;
            switch (o)
            {
                case ChapterOutcome.Clean: return $"<color={HudTheme.GreenHex}>{Name(climax?.CleanLabel, "깔끔히 버팀")}</color>";
                case ChapterOutcome.Hurt: return $"<color={HudText.Orange}>{Name(climax?.HurtLabel, "피해를 입고 버팀")}</color>";
                default: return $"<color={HudText.Red}>{Name(climax?.LostLabel, "일부를 잃음")}</color>";
            }
        }

        private void ShowResult(ChapterRecord record)
        {
            var data = _chapters.Current;
            int seconds = Mathf.FloorToInt(record != null ? record.Seconds : _chapters.ChapterSeconds);
            _modalTitle.SetText($"{data.DisplayName} 결과\n<size=70%>{(record != null ? OutcomeText(record.Outcome, data.Climax) : "")}</size>");
            _sb.Clear();
            _sb.Append($"<color={Soft}>{data.Climax.Title}</color>");
            if (record != null && !string.IsNullOrEmpty(record.Summary))
                _sb.Append($"  ·  {record.Summary}");
            _sb.Append($"\n<color={Soft}>이 장에 걸린 시간</color> {seconds / 60}분 {seconds % 60:00}초");
            if (_tension != null && (_tension.Value >= 0.5f || _tension.TopChapterCause(out _))) // 13-6a 긴장도 (결과는 6장부터, 지금은 보이기만)
            {
                _sb.Append($"\n<color={Soft}>긴장도</color> {_tension.Value:0} · {_tension.LevelName}");
                if (_tension.TopChapterCause(out var cause))
                    _sb.Append($"  <size=90%><color={Soft}>이 장에서 가장 큰 원인: {TensionSystem.SourceName(cause)}</color></size>");
            }
            if (record != null && !string.IsNullOrEmpty(record.Monologue)) // 13-2 코어 독백 (지금 성향으로 고른 문장)
                _sb.Append($"\n\n<color={CoreTint}><i>코어 · {record.Monologue}</i></color>");
            var choices = data.Choices;
            _sb.Append(choices.Count > 0
                ? $"\n\n<color={Gold}>방주의 방향을 하나 고르세요</color>"
                : _chapters.IsLast ? "" : $"\n\n<color={Soft}>다음 장으로 넘어갑니다</color>");
            _modalBody.SetText(_sb.ToString());

            ClearButtons();
            if (choices.Count > 0)
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    int index = i;
                    var c = choices[i];
                    string label = string.IsNullOrEmpty(c.Description) ? $"<b>{c.Title}</b>" : $"<b>{c.Title}</b>\n<size=78%><color={Soft}>{c.Description}</color></size>";
                    string lean = LeanText(c);
                    if (lean.Length > 0) // 13-2: 코어가 어느 쪽으로 기우는지 말로 (숫자는 숨김)
                        label += $"\n<size=70%><color={CoreTint}>코어: {lean}</color></size>";
                    AddButton(label, () => Choose(index), tall: true);
                }
            }
            else
                AddButton(_chapters.IsLast ? "이야기 마무리" : "다음 장으로", () => Choose(-1), tall: false);
            OpenModal();
        }

        /// <summary>선택지가 코어를 기울이는 쪽 ("보존 쪽 · 연민 쪽"). 크기 · 숫자는 보이지 않음.</summary>
        private static string LeanText(ChapterChoice choice) => LeanText(choice.Shifts);

        private static string LeanText(System.Collections.Generic.IReadOnlyList<AxisShift> shifts)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var s in shifts)
            {
                if (s.Toward == CorePole.Neutral)
                    continue;
                string word = CoreConfig.PoleName(s.Axis, s.Toward) + " 쪽";
                if (!parts.Contains(word))
                    parts.Add(word);
            }
            return string.Join(" · ", parts);
        }

        private void Choose(int index)
        {
            if (!_chapters.Choose(index))
            {
                AudioService.TryPlay(l => l.UiError);
                return;
            }
            if (_chapters.Phase != ChapterPhase.Finished)
                CloseModal(resume: true);
            Refresh();
        }

        private void HandleChapterStarted(int index) => Refresh();

        // ---------------- 캠페인 끝 ----------------

        private void HandleCampaignFinished() => ShowEnding();

        private void ShowEnding()
        {
            _modalTitle.SetText($"<color={Gold}>이어질 이야기</color>\n<size=60%>수직 단면 · {_chapters.Count}장까지</size>");
            _sb.Clear();
            foreach (var r in _chapters.Records)
            {
                var c = _chapters.GetChapter(r.Chapter);
                string choice = "";
                foreach (var option in c.Choices)
                    if (option.Id == r.Choice)
                        choice = $"  ·  방향 <b>{option.Title}</b>";
                _sb.Append($"{c.DisplayName}  ·  {OutcomeText(r.Outcome, c.Climax)}{choice}\n");
            }
            _sb.Append($"\n<color={Soft}>다음 장은 아직 준비 중입니다. 지금 정거장으로 계속 플레이할 수 있습니다.</color>");
            _modalBody.SetText(_sb.ToString());
            ClearButtons();
            AddButton("계속 플레이", () => CloseModal(resume: true), tall: false);
            AddButton("메인 메뉴", () => SceneFader.Load(SceneNames.MainMenu), tall: false);
            OpenModal();
        }

        // ---------------- 창 ----------------

        private void ClearButtons()
        {
            for (int i = _buttonRow.childCount - 1; i >= 0; i--)
            {
                var old = _buttonRow.GetChild(i);
                old.gameObject.SetActive(false); // 같은 프레임에 새 버튼과 함께 배치되지 않게 (Destroy는 프레임 끝)
                old.SetParent(null, false);
                Destroy(old.gameObject);
            }
        }

        private void AddButton(string label, System.Action onClick, bool tall)
        {
            var b = _ui.TechButton(_buttonRow, label, 22f, onClick);
            var text = b.GetComponentInChildren<TMP_Text>();
            text.enableWordWrapping = true;
            text.margin = new Vector4(14f, 6f, 14f, 6f);
            var le = b.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = tall ? 190f : 64f;
            le.flexibleHeight = 0f;
            _buttonRow.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
        }

        private void OpenModal()
        {
            _modalRoot.SetAsLastSibling();
            _modalOpen = true;
            _modalGroup.blocksRaycasts = true;
            _modalGroup.interactable = true;
            _modalTween.Play(restart: true);
            _modalFx?.Replay();
            _clock.Clock.SetPaused(true); // 전환 동안 시간 멈춤 (사용자 결정)
            _clock.InputLocked = true;
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void CloseModal(bool resume)
        {
            _modalOpen = false;
            _modalGroup.blocksRaycasts = false;
            _modalGroup.interactable = false;
            _modalTween.Hide();
            if (!resume)
                return;
            _clock.InputLocked = false;
            _clock.Clock.SetPaused(false);
        }
    }
}
