namespace SpaceStation.Interior
{
    /// <summary>
    /// 심우주 통신기지 미니게임 "별똥별 잡기" 판 (사용자 2026-11-17 미니게임 2안). Unity 없이 시험하는 순수 규칙.
    /// 서면 3 · 2 · 1 → <see cref="RoundTime"/>초 동안 별자리 광장 하늘에서 별똥별 · 우주 쓰레기가 천천히 떨어짐 (갈수록 자주 · 빠르게) →
    /// 바라보고 F = 잡음: 큰 별 +3 · 작은 별 +1 · 우주 쓰레기 −1 (점수는 0 밑으로 내려가지 않음), 땅에 닿으면 놓침. 끝나면 점수 · 최고 기록 → 다시 하면 새 판.
    /// 떨어질 것 사이 = <see cref="GapStart"/> → <see cref="GapEnd"/>초 (주어진 난수로 ± 25%), 종류 = 큰 별 20 % · 작은 별 55 % · 쓰레기 25 %.
    /// </summary>
    public sealed class StarCatchGame
    {
        public enum Phase { Idle, Countdown, Play, Done }
        public enum Kind { BigStar, SmallStar, Junk }

        public const float CountdownTime = 3f, RoundTime = 30f;
        public const float GapStart = 1.1f, GapEnd = 0.55f, SpeedStart = 0.95f, SpeedEnd = 1.55f;
        public const float BigChance = 0.2f, JunkChance = 0.25f;

        private readonly System.Random _random;
        private float _nextSpawn;

        public Phase Current { get; private set; }
        public float PhaseTime { get; private set; }
        public int Score { get; private set; }
        public int BigCaught { get; private set; }
        public int SmallCaught { get; private set; }
        public int JunkCaught { get; private set; }
        public int Missed { get; private set; }

        public bool Playing => Current == Phase.Play;
        public float Progress => Current == Phase.Play ? Clamp01(PhaseTime / RoundTime) : Current == Phase.Done ? 1f : 0f;
        public float Remaining => Current == Phase.Play ? RoundTime - PhaseTime : Current == Phase.Done ? 0f : RoundTime;
        /// <summary>지금 떨어지는 빠르기 (m/초).</summary>
        public float Speed => SpeedStart + (SpeedEnd - SpeedStart) * Progress;

        public StarCatchGame(int seed = 1117)
        {
            _random = new System.Random(seed);
        }

        public static int Points(Kind kind) => kind == Kind.BigStar ? 3 : kind == Kind.SmallStar ? 1 : -1;

        public void Begin()
        {
            Current = Phase.Countdown;
            PhaseTime = 0f;
            Score = BigCaught = SmallCaught = JunkCaught = Missed = 0;
            _nextSpawn = 0.3f;
        }

        public void Stop()
        {
            Current = Phase.Idle;
            PhaseTime = 0f;
        }

        /// <summary>잡음 (판 중에만 셈): 점수를 더하고 (0 밑으로는 안 내려감) 더한 값을 돌려줌.</summary>
        public int Catch(Kind kind)
        {
            if (Current != Phase.Play)
                return 0;
            switch (kind)
            {
                case Kind.BigStar: BigCaught++; break;
                case Kind.SmallStar: SmallCaught++; break;
                default: JunkCaught++; break;
            }
            int before = Score;
            Score = System.Math.Max(0, Score + Points(kind));
            return Score - before;
        }

        /// <summary>땅에 닿음 (별만 놓침으로 셈 — 쓰레기는 안 잡는 게 맞음).</summary>
        public void Land(Kind kind)
        {
            if (Current == Phase.Play && kind != Kind.Junk)
                Missed++;
        }

        /// <summary>한 프레임: 떨어질 차례면 종류를 돌려줌 (한 프레임에 하나), 아니면 null.</summary>
        public Kind? Step(float dt)
        {
            PhaseTime += dt;
            if (Current == Phase.Countdown)
            {
                if (PhaseTime >= CountdownTime)
                {
                    Current = Phase.Play;
                    PhaseTime = 0f;
                }
                return null;
            }
            if (Current != Phase.Play)
                return null;
            if (PhaseTime >= RoundTime)
            {
                Current = Phase.Done;
                PhaseTime = 0f;
                return null;
            }
            if (PhaseTime < _nextSpawn)
                return null;
            float gap = GapStart + (GapEnd - GapStart) * Progress;
            _nextSpawn = PhaseTime + gap * (0.75f + 0.5f * (float)_random.NextDouble());
            double roll = _random.NextDouble();
            return roll < BigChance ? Kind.BigStar : roll < BigChance + JunkChance ? Kind.Junk : Kind.SmallStar;
        }

        /// <summary>판이 정한 0 ~ 1 난수 (떨어지는 자리 — 같은 씨앗이면 같은 판).</summary>
        public float Next() => (float)_random.NextDouble();

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
    }
}
