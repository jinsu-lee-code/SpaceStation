using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>우측 상단 일시정지/배속 버튼. 단축키(기본 P, F1~F3, 7-5에서 변경 가능)는 SimulationClock이 처리한다.</summary>
    public sealed class TimeControlPanel : MonoBehaviour
    {
        [SerializeField] private SimulationClock _clock;
        [SerializeField] private Button _pauseButton;
        [SerializeField] private TMP_Text _pauseLabel;
        [Tooltip("SimulationClock의 배속 프리셋 순서와 동일")]
        [SerializeField] private Button[] _speedButtons;

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
            KeyBindings.Changed += Refresh; // 7-5: 버튼의 키 표시
            Refresh();
        }

        private void OnDestroy()
        {
            KeyBindings.Changed -= Refresh;
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
            string pauseKey = KeyBindings.Label(GameAction.Pause);
            _pauseLabel.SetText(paused
                ? $"{HudTheme.Icon("play")} 재개 <size=75%><color={HudText.Muted}>{pauseKey}</color></size>"
                : $"{HudTheme.Icon("pause")} 일시정지 <size=75%><color={HudText.Muted}>{pauseKey}</color></size>");
            SetColor(_pauseButton, paused ? HudTheme.ButtonWarning : HudTheme.ButtonNormal);

            var presets = _clock.SpeedPresets;
            for (int i = 0; i < _speedButtons.Length; i++)
            {
                bool active = !paused && i < presets.Length && Mathf.Approximately(presets[i], _tickClock.Speed);
                SetColor(_speedButtons[i], active ? HudTheme.ButtonSelected : HudTheme.ButtonNormal);
                var label = _speedButtons[i].GetComponentInChildren<TMP_Text>();
                if (label != null && i < presets.Length)
                    label.SetText($"{presets[i]:0}x <size=70%><color={HudText.Muted}>{KeyBindings.Label(SpeedAction(i))}</color></size>");
            }
        }

        private static GameAction SpeedAction(int index)
            => index == 0 ? GameAction.Speed1 : index == 1 ? GameAction.Speed2 : GameAction.Speed3;

        private static void SetColor(Button button, Color color)
        {
            if (button.targetGraphic != null)
                button.targetGraphic.color = color;
        }
    }
}
