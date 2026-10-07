using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Core
{
    /// <summary>
    /// <see cref="TickClock"/>을 소유하고 매 프레임 실제 경과 시간을 넘긴다.
    /// Time.timeScale은 건드리지 않는다 (일시정지 중에도 카메라·건설은 동작).
    /// 키(<see cref="KeyBindings"/>, 기본값 12-0): Space 일시정지 / Tab 배속 순환 / F1·F2·F3 배속 프리셋
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class SimulationClock : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float _tickInterval = 1f;
        [SerializeField, Min(1)] private int _maxTicksPerFrame = 8;
        [Tooltip("F1부터 순서대로 대응")]
        [SerializeField] private float[] _speedPresets = { 1f, 2f, 4f };
        [SerializeField] private bool _logTicks;

        private TickClock _clock;

        public TickClock Clock => _clock;
        public float[] SpeedPresets => _speedPresets;
        /// <summary>결과 화면 등에서 true: 단축키·배속 버튼으로 일시정지를 풀 수 없다.</summary>
        public bool InputLocked { get; set; }

        private void Awake()
        {
            _clock = new TickClock(_tickInterval, _maxTicksPerFrame);
            _clock.Ticked += HandleTicked;
            // 5-10 기본 배속
            float speed = Settings.GameSettings.SpeedChoices[Settings.GameSettings.DefaultSpeedIndex];
            if (!Mathf.Approximately(speed, _clock.Speed))
                _clock.SetSpeed(speed);
        }

        private void OnDestroy()
        {
            if (_clock != null)
                _clock.Ticked -= HandleTicked;
        }

        private void Update()
        {
            HandleDebugKeys();
            _clock.Advance(Time.unscaledDeltaTime);
        }

        public void SetSpeedPreset(int index)
        {
            if (InputLocked || index < 0 || index >= _speedPresets.Length)
                return;
            _clock.SetSpeed(_speedPresets[index]);
            _clock.SetPaused(false);
        }

        /// <summary>지금 배속에 해당하는 프리셋 번호 (없으면 0).</summary>
        private int CurrentPresetIndex()
        {
            for (int i = 0; i < _speedPresets.Length; i++)
            {
                if (Mathf.Approximately(_speedPresets[i], _clock.Speed))
                    return i;
            }
            return 0;
        }

        private void HandleDebugKeys()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || InputLocked)
                return;

            if (KeyBindings.WasPressed(GameAction.Pause))
                _clock.TogglePause();
            if (KeyBindings.WasPressed(GameAction.SpeedCycle))
                SetSpeedPreset((CurrentPresetIndex() + 1) % _speedPresets.Length); // 12-0: 1x → 2x → 4x → 1x
            if (KeyBindings.WasPressed(GameAction.Speed1))
                SetSpeedPreset(0);
            if (KeyBindings.WasPressed(GameAction.Speed2))
                SetSpeedPreset(1);
            if (KeyBindings.WasPressed(GameAction.Speed3))
                SetSpeedPreset(2);
        }

        private void HandleTicked(long tick)
        {
            if (_logTicks)
                Debug.Log($"Tick {tick} (x{_clock.Speed}, {_clock.SimulatedSeconds:0}s)");
        }
    }
}
