namespace SpaceStation.Interior
{
    /// <summary>
    /// 궤도 방어망 미니게임 "별똥별 막기" 판 (사용자 2026-11-13 "포대에 앉아 조준 · 발사"). Unity 없이 시험하는 순수 규칙.
    /// 앉음 → 3 · 2 · 1 → 60초 동안 별똥별이 하늘 면에서 바다로 떨어짐 (갈수록 자주 · 빠르게) → 맞힌 수 · 놓친 수 → 다시 쏘면 새 판.
    /// 별똥별 사이 = <see cref="GapStart"/> → <see cref="GapEnd"/>초 (주어진 난수로 ± 25%), 빠르기 = <see cref="SpeedStart"/> → <see cref="SpeedEnd"/> m/초.
    /// </summary>
    public sealed class StarfallGame
    {
        public enum Phase { Idle, Countdown, Play, Done }

        public const float CountdownTime = 3f, RoundTime = 60f;
        public const float GapStart = 1.5f, GapEnd = 0.65f, SpeedStart = 2.6f, SpeedEnd = 4.6f;
        /// <summary>쏘고 다음에 쏠 수 있을 때까지 (초).</summary>
        public const float FireCooldown = 0.22f;

        private readonly System.Random _random;
        private float _nextSpawn;
        private float _cooldown;

        public Phase Current { get; private set; }
        /// <summary>지금 단계에 든 뒤 지난 시간 (초).</summary>
        public float PhaseTime { get; private set; }
        public int Hits { get; private set; }
        public int Misses { get; private set; }
        public int Shots { get; private set; }

        public bool Playing => Current == Phase.Play;
        /// <summary>판이 지난 정도 0 ~ 1.</summary>
        public float Progress => Current == Phase.Play ? Clamp01(PhaseTime / RoundTime) : Current == Phase.Done ? 1f : 0f;
        public float Remaining => Current == Phase.Play ? RoundTime - PhaseTime : Current == Phase.Done ? 0f : RoundTime;
        /// <summary>지금 떨어지는 별똥별 빠르기 (m/초).</summary>
        public float Speed => SpeedStart + (SpeedEnd - SpeedStart) * Progress;

        public StarfallGame(int seed = 1113)
        {
            _random = new System.Random(seed);
        }

        /// <summary>새 판 (앉았을 때 · 끝난 뒤 다시 쏠 때): 3 · 2 · 1부터.</summary>
        public void Begin()
        {
            Current = Phase.Countdown;
            PhaseTime = 0f;
            Hits = Misses = Shots = 0;
            _nextSpawn = 0.4f;
            _cooldown = 0f;
        }

        /// <summary>내림: 쉼으로.</summary>
        public void Stop()
        {
            Current = Phase.Idle;
            PhaseTime = 0f;
        }

        /// <summary>쏠 수 있으면 쏨 (판 중에만 셈). 쐈으면 true.</summary>
        public bool TryFire()
        {
            if (Current != Phase.Play || _cooldown > 0f)
                return false;
            _cooldown = FireCooldown;
            Shots++;
            return true;
        }

        public void Hit() => Hits++;
        public void Miss() => Misses++;

        /// <summary>한 프레임: 별똥별이 떨어질 차례면 true (한 프레임에 하나).</summary>
        public bool Step(float dt)
        {
            if (_cooldown > 0f)
                _cooldown -= dt;
            if (Current == Phase.Idle || Current == Phase.Done)
            {
                PhaseTime += dt;
                return false;
            }
            PhaseTime += dt;
            if (Current == Phase.Countdown)
            {
                if (PhaseTime >= CountdownTime)
                {
                    Current = Phase.Play;
                    PhaseTime = 0f;
                }
                return false;
            }
            if (PhaseTime >= RoundTime)
            {
                Current = Phase.Done;
                PhaseTime = 0f;
                return false;
            }
            if (PhaseTime < _nextSpawn)
                return false;
            float gap = GapStart + (GapEnd - GapStart) * Progress;
            _nextSpawn = PhaseTime + gap * (0.75f + 0.5f * (float)_random.NextDouble());
            return true;
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
