using SpaceStation.Audio;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Save;
using SpaceStation.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 5-9 메인 메뉴: [새 게임] → 난이도 선택(이지/노멀/하드) → 게임 씬, [설정](5-10 SettingsPanel), [종료].
    /// Phase 6: [이어하기](가장 최근 저장, 없으면 숨김) · [불러오기](저장 창). 패널 전환은 UiTween, ESC는 뒤로.
    /// 13-7d 게임 모드 고리 (사용자 결정 2026-10-25): <see cref="_modes"/>가 있으면 모드마다 버튼 ([새 게임] 자리부터 차례로) →
    /// 그 모드의 시작 창 (고정 난이도면 [시작] 하나 + 설명, 아니면 난이도 셋 · 튜토리얼 켜기/끄기 줄은 모드가 원할 때만). 비면 예전 그대로.
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _mainPanel;
        [SerializeField] private CanvasGroup _difficultyPanel;
        [SerializeField] private Button _newGameButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _quitButton;
        [SerializeField] private Button _backButton;
        [SerializeField] private SettingsPanel _settingsPanel;
        [Tooltip("이지 / 노멀 / 하드 순서")]
        [SerializeField] private DifficultyPreset[] _difficulties = new DifficultyPreset[0];
        [SerializeField] private Button[] _difficultyButtons = new Button[0];
        [SerializeField] private TMP_Text[] _difficultyLabels = new TMP_Text[0];
        [Tooltip("처음 강조할 난이도 (노멀)")]
        [SerializeField] private int _recommendedIndex = 1;
        [Tooltip("11-18 처음 하는 사람에게 권할 난이도 (이지) — 노멀 초반이 빡빡해 첫 판은 이지 권장 (사용자 결정 2026-10-11)")]
        [SerializeField] private int _firstTimeIndex = 0;
        [Header("Save (Phase 6)")]
        [SerializeField] private Button _continueButton;
        [SerializeField] private TMP_Text _continueLabel;
        [SerializeField] private Button _loadButton;
        [SerializeField] private SaveLoadPanel _saveLoadPanel;
        [Header("13-7d 게임 모드 (비우면 [새 게임] → 난이도 선택, 예전 그대로)")]
        [Tooltip("메인 메뉴에 차례로 버튼을 만들 모드 — 서사판 = 캠페인, 타이쿤판 = 엔드리스, 두 판을 합치면 둘 다 (SPLIT.md '두 모드 합칠 때')")]
        [SerializeField] private GameModeData[] _modes = new GameModeData[0];
        [Tooltip("비우지 않으면 오른쪽 아래 버전 글자를 이것으로 (판마다 씬에서 정함)")]
        [SerializeField] private string _versionLabel = "";

        private UiTween _mainTween;
        private UiTween _difficultyTween;
        private bool _choosingDifficulty;
        private GameModeData _mode; // 지금 시작 창의 모드 (모드 목록이 없으면 null)
        private TMP_Text _header;
        private string _headerDefault;
        private TMP_Text _panelTitle; // 창 왼쪽 위 작은 영문 머리말 (DIFFICULTY → 모드 Id)
        private string _panelTitleDefault;
        private string[] _difficultyTexts = new string[0];
        private Button _tutorialButton;
        private TMP_Text _tutorialText;

        private void Start()
        {
            InputGate.Blocked = false;
            // 11-15 ③ 패널 전환: 미끄러짐 대신 제자리 페이드 + 홀로그램 켜짐 · 꺼짐 (패널에 HoloSkin이 있으면 흩어진 빛줄기가 모임)
            _mainTween = PanelTween(_mainPanel, 0.35f);
            _difficultyTween = PanelTween(_difficultyPanel, 0.3f);
            var title = transform.Find("Title");
            if (title != null && title.TryGetComponent<TMP_Text>(out var titleText))
            {
                // 큰 제목: 은은한 발광 + 살짝 색 번짐 (진하면 글자가 뭉개져 네모난 덩어리처럼 보였음)
                HoloUi.Glow(titleText, 0.3f);
                HoloChroma.Add(titleText, 1.2f, 0.55f);
            }
            if (_modes != null && _modes.Length > 0)
                SetupModeButtons();
            else
                _newGameButton.onClick.AddListener(() => ShowStart(null));
            _quitButton.onClick.AddListener(Quit);
            _backButton.onClick.AddListener(ShowMain);
            if (_settingsButton != null)
            {
                _settingsButton.interactable = _settingsPanel != null;
                if (_settingsPanel != null)
                    _settingsButton.onClick.AddListener(_settingsPanel.Open);
            }

            _difficultyTexts = new string[_difficultyButtons.Length];
            for (int i = 0; i < _difficultyButtons.Length && i < _difficulties.Length; i++)
            {
                int index = i;
                var d = _difficulties[i];
                _difficultyButtons[i].onClick.AddListener(() => StartChosen(index));
                string tag = i == _recommendedIndex ? $"  <size=70%><color={HudTheme.AccentHex}>기본</color></size>"
                    : i == _firstTimeIndex ? $"  <size=70%><color={HudTheme.GreenHex}>처음이면 추천</color></size>" : "";
                _difficultyTexts[i] = $"<b>{d.DisplayName}</b>{tag}\n<size=68%><color=#AFC4D8>{d.Description}</color></size>";
                if (i < _difficultyLabels.Length && _difficultyLabels[i] != null)
                    _difficultyLabels[i].SetText(_difficultyTexts[i]);
            }
            var header = _difficultyPanel.transform.Find("Header");
            if (header != null && header.TryGetComponent<TMP_Text>(out _header))
                _headerDefault = _header.text;
            var panelTitle = _difficultyPanel.transform.Find("PanelTitle");
            if (panelTitle != null && panelTitle.TryGetComponent<TMP_Text>(out _panelTitle))
                _panelTitleDefault = _panelTitle.text;
            if (_modes != null && System.Array.Exists(_modes, m => m != null && m.TutorialChoice))
                BuildTutorialRow();
            if (!string.IsNullOrEmpty(_versionLabel) && transform.Find("Version") is Transform version && version.TryGetComponent<TMP_Text>(out var versionText))
                versionText.SetText(_versionLabel);
            SetupSaveButtons();
            SetInteractable(_difficultyPanel, false);
            SetInteractable(_mainPanel, true);
            _mainTween.Play();
        }

        /// <summary>[이어하기] = 가장 최근 저장 (없으면 숨김), [불러오기] = 저장 창.</summary>
        private void SetupSaveButtons()
        {
            string recent = SaveService.MostRecentSlot();
            if (_continueButton != null)
            {
                _continueButton.gameObject.SetActive(recent != null);
                if (recent != null)
                {
                    var meta = SaveService.Describe(recent).Meta;
                    if (_continueLabel != null && meta != null)
                        _continueLabel.SetText($"이어하기  <size=62%><color=#AFC4D8>{meta.Headline} · 인구 {meta.Population} · {meta.SavedAt:MM-dd HH:mm}</color></size>");
                    _continueButton.onClick.AddListener(() =>
                    {
                        if (!SaveManager.LoadAndPlay(recent))
                            AudioService.TryPlay(l => l.UiError);
                        else
                            SetInteractable(_mainPanel, false);
                    });
                }
            }
            if (_loadButton != null)
            {
                _loadButton.interactable = _saveLoadPanel != null;
                if (_saveLoadPanel != null)
                    _loadButton.onClick.AddListener(_saveLoadPanel.OpenLoad);
            }
        }

        private void Update()
        {
            _mainTween.Update();
            _difficultyTween.Update();
            var keyboard = Keyboard.current;
            if (SettingsPanel.EscapeConsumedThisFrame || InputGate.EscapeConsumedThisFrame || (_settingsPanel != null && _settingsPanel.IsOpen)
                || (_saveLoadPanel != null && _saveLoadPanel.IsOpen))
                return;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && _choosingDifficulty)
                ShowMain();
        }

        private void ShowDifficulty()
        {
            _choosingDifficulty = true;
            _mainTween.Hide();
            SetInteractable(_mainPanel, false);
            _difficultyTween.Play();
            SetInteractable(_difficultyPanel, true);
            AudioService.TryPlay(l => l.UiOpen);
        }

        private void ShowMain()
        {
            _choosingDifficulty = false;
            _difficultyTween.Hide();
            SetInteractable(_difficultyPanel, false);
            _mainTween.Play();
            SetInteractable(_mainPanel, true);
            AudioService.TryPlay(l => l.UiClose);
        }

        // ---------------- 13-7d 게임 모드 ----------------

        /// <summary>모드마다 버튼: 첫 모드는 [새 게임] 자리, 나머지는 그 버튼을 복제해 바로 아래에.</summary>
        private void SetupModeButtons()
        {
            var parent = _newGameButton.transform.parent;
            int sibling = _newGameButton.transform.GetSiblingIndex();
            bool first = true;
            foreach (var mode in _modes)
            {
                if (mode == null)
                    continue;
                var button = first ? _newGameButton : CloneModeButton(parent);
                if (!first)
                {
                    button.name = "Mode_" + mode.Id;
                    button.transform.SetSiblingIndex(++sibling);
                    button.onClick.RemoveAllListeners();
                }
                first = false;
                var label = button.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.SetText(mode.DisplayName);
                button.onClick.AddListener(() => ShowStart(mode));
            }
        }

        /// <summary>
        /// [새 게임] 버튼 복제 (모드가 둘 이상일 때 — 합본 [캠페인, 엔드리스]): 꺼진 채로 복제해 실행 중 장식 · 효과(<see cref="HoloSkin"/>가 원본에 만든 것)를 뗀 뒤 켬
        /// → 복제본의 HoloSkin이 새로 꾸밈. 그대로 복제하면 효과의 내부 참조가 빈 채로 복사돼 매 프레임 오류가 났음 (합본 실행 확인 2026-10-30).
        /// </summary>
        private Button CloneModeButton(Transform parent)
        {
            var source = _newGameButton.gameObject;
            bool active = source.activeSelf;
            source.SetActive(false);
            var clone = Instantiate(_newGameButton, parent);
            source.SetActive(active);
            HoloSkin.StripRuntimeDecor(clone.gameObject);
            clone.gameObject.SetActive(active);
            return clone;
        }

        /// <summary>시작 창을 모드에 맞춰 (null = 예전 난이도 선택 그대로).</summary>
        private void ShowStart(GameModeData mode)
        {
            _mode = mode;
            bool fixedDifficulty = mode != null && mode.FixedDifficulty != null;
            if (_header != null)
                _header.SetText(mode != null ? mode.DisplayName : _headerDefault);
            if (_panelTitle != null)
                _panelTitle.SetText(mode != null && !string.IsNullOrEmpty(mode.Id) ? mode.Id.ToUpperInvariant() : _panelTitleDefault);
            for (int i = 0; i < _difficultyButtons.Length; i++)
            {
                _difficultyButtons[i].gameObject.SetActive(!fixedDifficulty || i == 0);
                if (i < _difficultyLabels.Length && _difficultyLabels[i] != null && i < _difficultyTexts.Length)
                    _difficultyLabels[i].SetText(fixedDifficulty ? StartLabel(mode) : _difficultyTexts[i]);
            }
            if (_tutorialButton != null)
            {
                _tutorialButton.gameObject.SetActive(mode != null && mode.TutorialChoice);
                RefreshTutorial();
            }
            ShowDifficulty();
        }

        private static string StartLabel(GameModeData mode) => string.IsNullOrEmpty(mode.Description)
            ? "<b>시작</b>"
            : $"<b>시작</b>\n<size=68%><color=#AFC4D8>{mode.Description}</color></size>";

        /// <summary>시작 창 튜토리얼 켜기/끄기 (설정 '다음 새 게임에서 튜토리얼'과 같은 값) — [뒤로] 버튼 모양을 복제해 난이도 버튼 위에.</summary>
        private void BuildTutorialRow()
        {
            if (_backButton == null || _difficultyButtons.Length == 0)
                return;
            _tutorialButton = Instantiate(_backButton, _backButton.transform.parent);
            _tutorialButton.name = "TutorialChoice";
            _tutorialButton.onClick.RemoveAllListeners();
            _tutorialButton.transform.SetSiblingIndex(_difficultyButtons[0].transform.GetSiblingIndex());
            var width = _difficultyButtons[0].GetComponent<LayoutElement>();
            if (_tutorialButton.TryGetComponent<LayoutElement>(out var le) && width != null)
                le.preferredWidth = width.preferredWidth;
            _tutorialText = _tutorialButton.GetComponentInChildren<TMP_Text>();
            _tutorialButton.onClick.AddListener(() =>
            {
                GameSettings.TutorialPending = !GameSettings.TutorialPending;
                RefreshTutorial();
            });
            _tutorialButton.gameObject.SetActive(false);
        }

        private void RefreshTutorial()
        {
            if (_tutorialText == null || _mode == null)
                return;
            string state = GameSettings.TutorialPending ? $"<color={HudTheme.GreenHex}>켬</color>" : "<color=#AFC4D8>끔</color>";
            _tutorialText.SetText($"{_mode.TutorialLabel}   <b>{state}</b>   <size=70%><color=#AFC4D8>누르면 바뀜</color></size>");
        }

        private void StartChosen(int index)
        {
            var difficulty = _mode != null && _mode.FixedDifficulty != null ? _mode.FixedDifficulty : _difficulties[index];
            StartGame(difficulty);
        }

        private void StartGame(DifficultyPreset d)
        {
            if (SceneFader.Busy)
                return;
            GameStartOptions.Mode = _mode;
            GameStartOptions.Difficulty = d;
            SetInteractable(_difficultyPanel, false);
            string title = _mode != null && !string.IsNullOrEmpty(_mode.LoadingTitle) ? _mode.LoadingTitle : "새 정거장";
            string detail = _mode != null && !string.IsNullOrEmpty(_mode.LoadingDetail) ? _mode.LoadingDetail
                : d != null ? $"난이도 {d.DisplayName}" : null;
            SceneFader.Load(SceneNames.Game, new LoadingCard { Title = title, Detail = detail });
        }

        private static UiTween PanelTween(CanvasGroup panel, float inSeconds)
        {
            var rt = (RectTransform)panel.transform;
            var skin = panel.GetComponent<HoloSkin>();
            if (skin != null)
                HoloGlitch.Add(rt, skin.Art);
            return new UiTween(rt, panel, Vector2.zero, inSeconds, 0.2f, glitch: true);
        }

        private static void SetInteractable(CanvasGroup group, bool on)
        {
            group.interactable = on;
            group.blocksRaycasts = on;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
