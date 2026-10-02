using SpaceStation.Audio;
using SpaceStation.Core;
using SpaceStation.Data;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 5-9 메인 메뉴: [새 게임] → 난이도 선택(이지/노멀/하드) → 게임 씬, [설정](5-10 SettingsPanel), [종료].
    /// 이어하기는 세이브가 생기면 추가. 패널 전환은 UiTween, ESC는 뒤로.
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

        private UiTween _mainTween;
        private UiTween _difficultyTween;
        private bool _choosingDifficulty;

        private void Start()
        {
            InputGate.Blocked = false;
            _mainTween = new UiTween((RectTransform)_mainPanel.transform, _mainPanel, new Vector2(-40f, 0f), 0.35f, 0.2f);
            _difficultyTween = new UiTween((RectTransform)_difficultyPanel.transform, _difficultyPanel, new Vector2(40f, 0f), 0.3f, 0.2f);
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
            SetInteractable(_difficultyPanel, false);
            SetInteractable(_mainPanel, true);
            _mainTween.Play();
        }

        private void Update()
        {
            _mainTween.Update();
            _difficultyTween.Update();
            var keyboard = Keyboard.current;
            if (SettingsPanel.EscapeConsumedThisFrame || (_settingsPanel != null && _settingsPanel.IsOpen))
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
