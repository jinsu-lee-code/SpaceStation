using SpaceStation.Core;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 결과 화면 (GDD 12-3).
    /// 최고 등급 첫 도달: "정거장 완성" 요약 + [계속 플레이] [재시작].
    /// 게임 오버: 요약 + [재시작] [메인 메뉴] (5-9, 씬 전환은 SceneFader).
    /// 표시 중에는 시뮬레이션을 일시정지하고 배속 입력을 잠근다.
    /// </summary>
    public sealed class ResultScreen : MonoBehaviour
    {
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

        private Mode _mode = Mode.Hidden;
        private UiTween _dimTween;     // 어두운 바탕 (부드러운 페이드)
        private UiTween _panelTween;   // 결과 패널 (페이드 + 홀로그램 지지직, 11-15)

        private void Start()
        {
            _progression.Progression.VictoryReached += HandleVictory;
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
            }
            if (_title != null)
                HoloUi.Glow(_title, 0.5f);
            SetVisible(false);
        }

        private void Update()
        {
            _dimTween?.Update();
            _panelTween?.Update();
        }

        private void OnDestroy()
        {
            if (_progression == null || _progression.Progression == null)
                return;
            _progression.Progression.VictoryReached -= HandleVictory;
            _progression.Session.GameOver -= HandleGameOver;
        }

        private void HandleVictory()
        {
            if (_mode == Mode.GameOver)
                return;
            Show(Mode.Victory, "<color=#7CFF9A>정거장 완성!</color>", "계속 플레이", "재시작");
        }

        private void HandleGameOver()
        {
            string reason = FailureMonitor.Describe(_progression.Session.Reason);
            Show(Mode.GameOver, $"<color={HudText.Red}>게임 오버</color>\n<size=55%>{reason}</size>", "재시작", "메인 메뉴");
        }

        private void Show(Mode mode, string title, string primary, string secondary)
        {
            _mode = mode;
            _title.SetText(title);
            _stats.SetText(BuildStats());
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
