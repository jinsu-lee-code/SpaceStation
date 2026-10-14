using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 결과 화면 (GDD 12-3).
    /// 12-1 누적 결과 화면: 결과 등급(대형 · 국가급 · 지구급) 첫 도달 → "N구간 달성 · 등급" + 이번 구간 | 누적 기록 + [계속 플레이] [재시작].
    /// 12-1 ★ 등급(초대형 · 도시급 · 대륙급) 첫 도달 → 화면 위쪽 가운데 홀로그램 배너 (게임은 멈추지 않음, 런타임 생성).
    /// 게임 오버: 요약 + [재시작] [메인 메뉴] (5-9, 씬 전환은 SceneFader).
    /// 결과 화면 표시 중에는 시뮬레이션을 일시정지하고 배속 입력을 잠근다.
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
        private const string Gold = "#FFD36A";
        private enum Mode { Hidden, Victory, GameOver }

        [SerializeField] private ProgressionController _progression;
        [SerializeField] private ResourceController _resources;
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _stats;
        [SerializeField] private Button _primaryButton;
        [SerializeField] private TMP_Text _primaryLabel;
        [SerializeField] private Button _secondaryButton;
        [SerializeField] private TMP_Text _secondaryLabel;

        [Header("12-1 ★ 배너")]
        [SerializeField, Min(0.5f)] private float _bannerSeconds = 4f;
        [Tooltip("화면 위쪽 가운데 기준 (위 끝에서 내려온 거리, 크기) — 이벤트 배너 · 조기 경보 배너 아래")]
        [SerializeField] private float _bannerTop = 220f;
        [SerializeField] private Vector2 _bannerSize = new Vector2(640f, 112f);

        private Mode _mode = Mode.Hidden;
        private UiTween _dimTween;     // 어두운 바탕 (부드러운 페이드)
        private UiTween _panelTween;   // 결과 패널 (페이드 + 홀로그램 지지직, 11-15)

        private UiTween _bannerTween;
        private TMP_Text _bannerTitle;
        private TMP_Text _bannerBody;
        private HoloFx _bannerFx;
        private float _bannerUntil = -1f;

        private void Start()
        {
            _progression.Progression.MilestoneReached += HandleMilestone;
            _progression.Session.GameOver += HandleGameOver;
            _primaryButton.onClick.AddListener(HandlePrimary);
            _secondaryButton.onClick.AddListener(HandleSecondary);
            _dimTween = new UiTween(null, _group, Vector2.zero, 0.3f, 0.25f);
            var panel = transform.Find("Panel") as RectTransform;
            if (panel != null)
            {
                var skin = panel.GetComponent<HoloSkin>();
                if (skin != null)
                    HoloGlitch.Add(panel, skin.Art);
                var panelGroup = panel.GetComponent<CanvasGroup>();
                if (panelGroup == null)
                    panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
                _panelTween = new UiTween(panel, panelGroup, Vector2.zero, 0.3f, 0.2f, glitch: true);
                BuildBanner(skin != null ? skin.Art : null);
            }
            if (_title != null)
                HoloUi.Glow(_title, 0.5f);
            SetVisible(false);
        }

        private void Update()
        {
            _dimTween?.Update();
            _panelTween?.Update();
            _bannerTween?.Update();
            if (_bannerUntil > 0f && Time.unscaledTime >= _bannerUntil)
            {
                _bannerUntil = -1f;
                _bannerTween?.Hide();
            }
        }

        private void OnDestroy()
        {
            if (_progression == null || _progression.Progression == null)
                return;
            _progression.Progression.MilestoneReached -= HandleMilestone;
            _progression.Session.GameOver -= HandleGameOver;
        }

        private void HandleMilestone(int grade, GradeMilestone milestone)
        {
            if (_mode == Mode.GameOver || _progression.Chapters != null)
                return; // 13-1 장 틀: 등급 ★ · 결과 화면은 장 결과(ChapterView)로 대체
            var p = _progression.Progression;
            string name = p.GetGrade(grade).DisplayName;
            if (milestone == GradeMilestone.Star)
            {
                ShowBanner(grade, name);
                return;
            }
            string head = p.IsFinalResult(grade) ? "최종 달성" : $"{p.ResultNumber(grade)}구간 달성";
            HideBannerNow(); // 결과 화면이 덮으면 배너는 남기지 않음
            Show(Mode.Victory, $"<color=#7CFF9A>{head}</color>\n<size=60%>{name} 정거장</size>", "계속 플레이", "재시작",
                BuildSegmentStats());
        }

        private void HandleGameOver()
        {
            string reason = FailureMonitor.Describe(_progression.Session.Reason);
            HideBannerNow();
            Show(Mode.GameOver, $"<color={HudText.Red}>게임 오버</color>\n<size=55%>{reason}</size>", "재시작", "메인 메뉴", BuildStats());
        }

        private void Show(Mode mode, string title, string primary, string secondary, string stats)
        {
            _mode = mode;
            _title.SetText(title);
            _stats.SetText(stats);
            _primaryLabel.SetText(primary);
            _secondaryLabel.SetText(secondary);
            _clock.Clock.SetPaused(true);
            _clock.InputLocked = true;
            SetVisible(true);
        }

        private string BuildStats()
        {
            var session = _progression.Session;
            int total = Mathf.FloorToInt(_progression.PlaySeconds);
            return $"플레이 시간<pos=55%>{total / 60:0}분 {total % 60:00}초\n" +
                   $"최종 등급<pos=55%>{_progression.Progression.Current.DisplayName}\n" +
                   $"최대 인구<pos=55%>{session.MaxPopulation}명\n" +
                   $"현재 인구<pos=55%>{_resources.Simulation.Population}명\n" +
                   $"모듈 수<pos=55%>{_progression.ModuleCount}개\n" +
                   $"겪은 이벤트<pos=55%>{session.EventsExperienced}회\n" +
                   $"파괴된 모듈<pos=55%>{session.ModulesDestroyed}개";
        }

        /// <summary>12-1: 이번 구간(직전 결과 화면 이후) | 누적 두 열.</summary>
        private string BuildSegmentStats()
        {
            var session = _progression.Session;
            var seg = session.LastSegment ?? new SegmentRecord();
            const string a = "<pos=40%>", b = "<pos=70%>";
            string added = seg.ModulesAdded >= 0 ? $"+{seg.ModulesAdded}개" : $"{seg.ModulesAdded}개";
            return $"<color={HudText.Muted}>{a}이번 구간{b}누적</color>\n" +
                   $"걸린 시간{a}{Duration(seg.Seconds)}{b}{Duration(_progression.PlaySeconds)}\n" +
                   $"최대 인구{a}{seg.MaxPopulation}명{b}{session.MaxPopulation}명\n" +
                   $"모듈 수{a}{added}{b}{_progression.ModuleCount}개\n" +
                   $"겪은 이벤트{a}{seg.Events}회{b}{session.EventsExperienced}회\n" +
                   $"파괴된 모듈{a}{seg.Destroyed}개{b}{session.ModulesDestroyed}개";
        }

        private static string Duration(float seconds)
        {
            int total = Mathf.FloorToInt(seconds);
            int h = total / 3600, m = total / 60 % 60, s = total % 60;
            return h > 0 ? $"{h}시간 {m:00}분" : $"{m}분 {s:00}초";
        }

        // ---------------- 12-1 ★ 배너 ----------------

        /// <summary>화면 위쪽 가운데 홀로그램 배너 (결과 화면 바로 뒤 형제 — 결과 화면의 어두운 바탕이 덮음). 클릭은 통과.</summary>
        private void BuildBanner(HoloArt art)
        {
            var parent = transform.parent;
            if (parent == null || _title == null)
                return;
            var rt = HoloUi.Rect("MilestoneBanner", parent);
            rt.SetSiblingIndex(transform.GetSiblingIndex());
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -_bannerTop);
            rt.sizeDelta = _bannerSize;
            var ui = HoloUi.For(art, _title.font, null, null, null);
            _bannerFx = ui.Window(rt.gameObject, new Color(0.03f, 0.07f, 0.11f, 0.92f), "MILESTONE", 0.5f);
            if (rt.GetComponent<Image>() == null)
                rt.gameObject.AddComponent<Image>().color = new Color(0.03f, 0.07f, 0.11f, 0.92f);
            foreach (var g in rt.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;
            if (art != null)
                HoloGlitch.Add(rt, art);

            var star = ui.Label(rt, "★", 64f, TextAlignmentOptions.Center);
            HoloUi.Place(star.rectTransform, new Vector2(18f, -14f), new Vector2(96f, 84f));
            star.color = new Color(1f, 0.83f, 0.42f);
            HoloUi.Glow(star, 0.8f);
            _bannerTitle = ui.Label(rt, "", 34f, TextAlignmentOptions.Left);
            HoloUi.Place(_bannerTitle.rectTransform, new Vector2(118f, -18f), new Vector2(_bannerSize.x - 136f, 46f));
            _bannerTitle.fontStyle = FontStyles.Bold;
            HoloUi.Glow(_bannerTitle, 0.6f);
            _bannerBody = ui.Label(rt, "", 19f, TextAlignmentOptions.Left);
            HoloUi.Place(_bannerBody.rectTransform, new Vector2(120f, -66f), new Vector2(_bannerSize.x - 138f, 30f));
            _bannerBody.color = HoloUi.MutedColor;

            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            _bannerTween = new UiTween(rt, group, new Vector2(0f, 16f), 0.3f, 0.35f, glitch: true);
            _bannerTween.HideNow();
        }

        private void ShowBanner(int grade, string name)
        {
            if (_bannerTween == null)
                return;
            var p = _progression.Progression;
            _bannerTitle.SetText($"<color={Gold}>{name} 정거장 달성</color>");
            if (grade + 1 < p.GradeCount)
            {
                var next = p.GetGrade(grade + 1);
                _bannerBody.SetText($"다음 목표  {next.DisplayName}  ·  인구 {next.MinPopulation}  ·  모듈 {next.MinModules}");
            }
            else
                _bannerBody.SetText("최고 등급");
            _bannerTween.Play();
            _bannerFx?.Replay();
            _bannerUntil = Time.unscaledTime + _bannerSeconds;
        }

        private void HideBannerNow()
        {
            _bannerUntil = -1f;
            _bannerTween?.HideNow();
        }

        private void HandlePrimary()
        {
            if (_mode == Mode.Victory)
                Continue();
            else
                Restart();
        }

        private void HandleSecondary()
        {
            if (_mode == Mode.Victory)
                Restart();
            else
                SceneFader.Load(SceneNames.MainMenu);
        }

        private void Continue()
        {
            _mode = Mode.Hidden;
            SetVisible(false);
            _clock.InputLocked = false;
            _clock.Clock.SetPaused(false);
        }

        private static void Restart() => SceneFader.Reload();

        private void SetVisible(bool visible)
        {
            if (_dimTween == null)
                _group.alpha = visible ? 1f : 0f;
            else if (visible)
            {
                _dimTween.Play();
                _panelTween?.Play();
            }
            else if (_mode == Mode.Hidden && _group.alpha > 0f)
            {
                _dimTween.Hide();
                _panelTween?.Hide();
            }
            else
            {
                _dimTween.HideNow();
                _panelTween?.HideNow();
            }
            _group.blocksRaycasts = visible; // 표시 중에는 뒤쪽 HUD·월드 클릭 차단
            _group.interactable = visible;
        }
    }
}
