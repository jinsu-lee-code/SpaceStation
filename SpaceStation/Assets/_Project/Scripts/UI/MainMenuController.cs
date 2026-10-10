using SpaceStation.Audio;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Save;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 5-9 메인 메뉴: [새 게임] → 난이도 선택(이지/노멀/하드) → 게임 씬, [설정](5-10 SettingsPanel), [종료].
    /// Phase 6: [이어하기](가장 최근 저장, 없으면 숨김) · [불러오기](저장 창). 패널 전환은 UiTween, ESC는 뒤로.
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
        [Header("Save (Phase 6)")]
        [SerializeField] private Button _continueButton;
        [SerializeField] private TMP_Text _continueLabel;
        [SerializeField] private Button _loadButton;
        [SerializeField] private SaveLoadPanel _saveLoadPanel;

        private UiTween _mainTween;
        private UiTween _difficultyTween;
        private bool _choosingDifficulty;

        private void Start()
        {
            InputGate.Blocked = false;
            // 11-15 ③ 패널 전환: 미끄러짐 대신 제자리 페이드 + 홀로그램 켜짐 · 꺼짐 (패널에 HoloSkin이 있으면 흩어진 빛줄기가 모임)
            _mainTween = PanelTween(_mainPanel, 0.35f);
            _difficultyTween = PanelTween(_difficultyPanel, 0.3f);
            var title = transform.Find("Title");
            if (title != null && title.TryGetComponent<TMP_Text>(out var titleText))
            {
                HoloUi.Glow(titleText, 0.6f);
                HoloChroma.Add(titleText, 2.4f); // 큰 제목만 자홍 · 청록 색 번짐
            }
            _newGameButton.onClick.AddListener(ShowDifficulty);
            _quitButton.onClick.AddListener(Quit);
            _backButton.onClick.AddListener(ShowMain);
            if (_settingsButton != null)
            {
                _settingsButton.interactable = _settingsPanel != null;
                if (_settingsPanel != null)
                    _settingsButton.onClick.AddListener(_settingsPanel.Open);
            }

            for (int i = 0; i < _difficultyButtons.Length && i < _difficulties.Length; i++)
            {
                int index = i;
                var d = _difficulties[i];
                _difficultyButtons[i].onClick.AddListener(() => StartGame(index));
                if (i < _difficultyLabels.Length && _difficultyLabels[i] != null)
                {
                    string tag = i == _recommendedIndex ? $"  <size=70%><color={HudTheme.AccentHex}>기본</color></size>" : "";
                    _difficultyLabels[i].SetText($"<b>{d.DisplayName}</b>{tag}\n<size=68%><color=#AFC4D8>{d.Description}</color></size>");
                }
            }
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
                        _continueLabel.SetText($"이어하기  <size=62%><color=#AFC4D8>{meta.GradeName} · 인구 {meta.Population} · {meta.SavedAt:MM-dd HH:mm}</color></size>");
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

        private void StartGame(int index)
        {
            if (SceneFader.Busy)
                return;
            GameStartOptions.Difficulty = _difficulties[index];
            SetInteractable(_difficultyPanel, false);
            SceneFader.Load(SceneNames.Game);
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
