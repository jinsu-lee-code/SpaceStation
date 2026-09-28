using System;

namespace SpaceStation.Core
{
    /// <summary>
    /// 고정 간격 시뮬레이션 틱. 실제 경과 시간 × 배속을 누적해 간격마다 <see cref="Ticked"/>를 발생시킨다.
    /// 시뮬레이션은 이 틱에서만 진행한다 (Update에서 직접 계산하지 않음).
    /// </summary>
    public sealed class TickClock
    {
        private float _accumulator;
        private float _speed = 1f;
        private bool _paused;

        /// <summary>틱 번호(1부터). 틱 간격(초)은 <see cref="TickInterval"/>.</summary>
        public event Action<long> Ticked;
        public event Action<float> SpeedChanged;
        public event Action<bool> PausedChanged;

        public float TickInterval { get; }
        /// <summary>한 번의 Advance에서 처리할 최대 틱 수. 넘치는 시간은 버린다 (렉 이후 틱 폭주 방지).</summary>
        public int MaxTicksPerAdvance { get; }
        public long TickCount { get; private set; }
        public float Speed => _speed;
        public bool IsPaused => _paused;
        /// <summary>다음 틱까지 진행률 0~1 (UI 보간용).</summary>
        public float Progress => _accumulator / TickInterval;
        /// <summary>시뮬레이션 경과 시간(초) = TickCount × TickInterval.</summary>
        public float SimulatedSeconds => TickCount * TickInterval;

        public TickClock(float tickInterval = 1f, int maxTicksPerAdvance = 8)
        {
            if (tickInterval <= 0f)
                throw new ArgumentOutOfRangeException(nameof(tickInterval));
            if (maxTicksPerAdvance < 1)
                throw new ArgumentOutOfRangeException(nameof(maxTicksPerAdvance));
            TickInterval = tickInterval;
            MaxTicksPerAdvance = maxTicksPerAdvance;
        }

        public void Advance(float realDeltaTime)
        {
            if (_paused || realDeltaTime <= 0f)
                return;

            _accumulator += realDeltaTime * _speed;
            int processed = 0;
            while (_accumulator >= TickInterval)
            {
                if (processed >= MaxTicksPerAdvance)
                {
                    _accumulator %= TickInterval;
                    break;
                }
                _accumulator -= TickInterval;
                processed++;
                TickCount++;
                Ticked?.Invoke(TickCount);
            }
        }

        public void SetSpeed(float speed)
        {
            if (speed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(speed), "일시정지는 SetPaused로 처리");
            if (speed == _speed)
                return;
            _speed = speed;
            SpeedChanged?.Invoke(_speed);
        }

        public void SetPaused(bool paused)
        {
            if (paused == _paused)
                return;
            _paused = paused;
            PausedChanged?.Invoke(_paused);
        }

        public void TogglePause()
        {
            SetPaused(!_paused);
        }
    }
}
