using SpaceStation.Audio;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 5-9 게임 중 ESC 메뉴: [재개] [설정](5-10 SettingsPanel) [재시작] [메인 메뉴].
    /// 배치·선택 중의 ESC는 기존처럼 취소이고, 아무것도 없을 때만 메뉴가 열린다 (그래서 다른 처리기보다 먼저 판단).
    /// 열려 있는 동안 시뮬레이션을 멈추고 조작 키를 막는다. 결과 화면이 떠 있으면 열리지 않는다.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private SimulationHost _host;
        [SerializeField] private BuildController _build;
        [SerializeField] private ModuleSelectionController _selection;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private RectTransform _panel;
        [SerializeField] private TMP_Text _info;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _settingsButton;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _mainMenuButton;
        [SerializeField] private SettingsPanel _settingsPanel;
        [Header("Save (Phase 6)")]
        [SerializeField] private Button _saveButton;
        [SerializeField] private Button _loadButton;
        [SerializeField] private SaveLoadPanel _saveLoadPanel;

        private UiTween _tween;
        private UiTween _panelTween;
        private CanvasGroup _panelGroup;
        private bool _subOpen;
        private bool _open;
        private bool _wasPaused;

        public bool IsOpen => _open;

        private void Start()
        {
            // 11-15 어두운 바탕(_group)은 지지직 없이 부드럽게 페이드, 메뉴 패널만 따로 페이드 + 지지직 (바탕까지 깜박이면 신경 쓰였음)
            _tween = new UiTween(null, _group, Vector2.zero, 0.2f, 0.25f);
            // 11-15 설정 · 저장 창이 위에 열리면 메뉴 패널만 바로 숨김 (어두운 바탕은 그대로) — 새 창이 페이드 · 지지직으로 나타나는 동안
            // 뒤의 메뉴가 앞에 있는 것처럼 비쳐 보였음. 창이 닫히면 지지직하며 다시 나타남.
            _panelGroup = _panel.GetComponent<CanvasGroup>();
            if (_panelGroup == null)
                _panelGroup = _panel.gameObject.AddComponent<CanvasGroup>();
            _panelTween = new UiTween(_panel, _panelGroup, Vector2.zero, 0.2f, 0.12f, glitch: true);
            _panelTween.ShowNow();
            _resumeButton.onClick.AddListener(Close);
            _restartButton.onClick.AddListener(() => Leave(true));
            _mainMenuButton.onClick.AddListener(() => Leave(false));
            if (_settingsButton != null)
            {
                _settingsButton.interactable = _settingsPanel != null;
                if (_settingsPanel != null)
                    _settingsButton.onClick.AddListener(_settingsPanel.Open);
            }
            if (_saveLoadPanel != null)
            {
                if (_saveButton != null)
                    _saveButton.onClick.AddListener(_saveLoadPanel.OpenSave);
                if (_loadButton != null)
                    _loadButton.onClick.AddListener(_saveLoadPanel.OpenLoad);
            }
            SetVisible(false);
            InputGate.Blocked = false;
        }

        private void OnDestroy()
        {
            if (_open)
                InputGate.Blocked = false;
        }

        private void Update()
        {
            _tween.Update();
            _panelTween.Update();
            bool sub = (_settingsPanel != null && _settingsPanel.IsOpen) || (_saveLoadPanel != null && _saveLoadPanel.IsOpen);
            if (_open && sub != _subOpen)
            {
                _subOpen = sub;
                if (sub)
                    _panelTween.HideNow();
                else
                    _panelTween.Play();
            }
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || SceneFader.Busy)
                return;
            if (SettingsPanel.EscapeConsumedThisFrame || InputGate.EscapeConsumedThisFrame || (_settingsPanel != null && _settingsPanel.IsOpen)
                || (_saveLoadPanel != null && _saveLoadPanel.IsOpen))
                return; // 설정창·저장 창이 먼저 닫힌다
            if (_open)
                Close();
            else if (!_clock.InputLocked && (_build == null || _build.Selected == null) && (_selection == null || _selection.Selected == null))
                Open();
        }

        public void Open()
        {
            _open = true;
            _wasPaused = _clock.Clock.IsPaused;
            _clock.Clock.SetPaused(true);
            _clock.InputLocked = true;
            InputGate.Blocked = true;
            if (_info != null)
            {
                var d = _host != null ? _host.Difficulty : null;
                _info.SetText($"<color=#AFC4D8>난이도</color> {(d != null ? d.DisplayName : "-")}");
            }
            if (_saveButton != null)
                _saveButton.interactable = _saveLoadPanel != null && Save.SaveManager.Instance != null && Save.SaveManager.Instance.CanSave;
            SetVisible(true);
            _subOpen = false;
            _panelTween.HideNow();
            _panelTween.Play();
            _tween.Play();
            AudioService.TryPlay(l => l.UiOpen);
        }

        public void Close()
        {
            if (!_open)
                return;
            _open = false;
            _clock.InputLocked = false;
            _clock.Clock.SetPaused(_wasPaused);
            InputGate.Blocked = false;
            SetVisible(false);
            _tween.Hide();
            _panelTween.Hide();
            AudioService.TryPlay(l => l.UiClose);
        }

        private void Leave(bool restart)
        {
            SetVisible(false);
            if (restart)
            {
                SceneFader.Reload();
            }
            else
            {
                Save.SaveManager.Instance?.SaveBeforeLeaving(); // 메인 메뉴로 나갈 때 자동 저장
                SceneFader.Load(SceneNames.MainMenu);
            }
        }

        private void SetVisible(bool on)
        {
            _group.blocksRaycasts = on;
            _group.interactable = on;
        }
    }
}
