using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>우측 상단 일시정지/배속 버튼. 단축키(P, F1~F3)는 SimulationClock이 계속 처리한다.</summary>
    public sealed class TimeControlPanel : MonoBehaviour
    {
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private TMP_Text _pauseLabel;
        [Tooltip("SimulationClock의 배속 프리셋 순서와 동일")]
        [SerializeField] private Button[] _speedButtons;
        [SerializeField] private Color _normalColor = new Color(0.18f, 0.2f, 0.24f, 0.9f);
        [SerializeField] private Color _activeColor = new Color(0.25f, 0.55f, 0.9f, 1f);
        [SerializeField] private Color _pausedColor = new Color(0.85f, 0.45f, 0.15f, 1f);

        private TickClock _tickClock;

        private void Start()
        {
            _tickClock = _clock.Clock;
            _pauseButton.onClick.AddListener(() =>
            {
                if (!_clock.InputLocked)
                    _tickClock.TogglePause();
            });
            for (int i = 0; i < _speedButtons.Length; i++)
            {
                int index = i;
                _speedButtons[i].onClick.AddListener(() => _clock.SetSpeedPreset(index));
            }
            _tickClock.SpeedChanged += HandleSpeedChanged;
            _tickClock.PausedChanged += HandlePausedChanged;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_tickClock == null)
                return;
            _tickClock.SpeedChanged -= HandleSpeedChanged;
            _tickClock.PausedChanged -= HandlePausedChanged;
        }

        private void HandleSpeedChanged(float _) => Refresh();
        private void HandlePausedChanged(bool _) => Refresh();

        private void Refresh()
        {
            bool paused = _tickClock.IsPaused;
            _pauseLabel.SetText(paused ? "재개 (P)" : "일시정지 (P)");
            SetColor(_pauseButton, paused ? _pausedColor : _normalColor);

            var presets = _clock.SpeedPresets;
            for (int i = 0; i < _speedButtons.Length; i++)
            {
                bool active = !paused && i < presets.Length && Mathf.Approximately(presets[i], _tickClock.Speed);
                SetColor(_speedButtons[i], active ? _activeColor : _normalColor);
            }
        }

        private static void SetColor(Button button, Color color)
        {
            if (button.targetGraphic != null)
                button.targetGraphic.color = color;
        }
    }
}
