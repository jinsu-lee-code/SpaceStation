namespace SpaceStation.Interior
{
    /// <summary>
    /// 심우주 통신기지 미니게임 "신호 맞추기" 판 (사용자 2026-11-17 미니게임 1안). Unity 없이 시험하는 순수 규칙.
    /// 판 = <see cref="Rounds"/>신호. 신호마다 목표 파형 (빈도 · 세기)이 정해지고, 받은 파형의 빈도 · 세기를 다이얼로 돌려 겹침 →
    /// 맞춤 (또는 <see cref="RoundTime"/>초가 지나면 저절로) = 겹친 정도 (0 ~ 100) 점수. 받은 파형은 덜 맞을수록 크게 흔들림 (<see cref="Jitter"/>).
    /// 다 끝나면 합계 · 최고 기록 → 다시 하면 새 판.
    /// </summary>
    public sealed class SignalTuneGame
    {
        public enum Phase { Idle, Ready, Tune, Locked, Done }

        public const int Rounds = 5;
        public const float ReadyTime = 1.4f, RoundTime = 15f, LockedTime = 1.6f;
        /// <summary>빈도 (화면 너비에 든 물결 수) · 세기 (화면 높이 반에 대한 비) 범위.</summary>
        public const float FreqMin = 1f, FreqMax = 4.5f, AmpMin = 0.25f, AmpMax = 1f;
        /// <summary>겹침이 0이 되는 어긋남 (빈도 · 세기).</summary>
        public const float FreqSpan = 1.4f, AmpSpan = 0.55f;
        /// <summary>목표와 처음 다이얼이 적어도 이만큼 떨어짐 (그냥 맞춤으로 점수 나지 않게).</summary>
        public const float StartGapFreq = 1f, StartGapAmp = 0.3f;

        private readonly System.Random _random;

        public Phase Current { get; private set; }
        public float PhaseTime { get; private set; }
        /// <summary>지금 신호 번호 (0부터).</summary>
        public int Round { get; private set; }
        public float TargetFreq { get; private set; }
        public float TargetAmp { get; private set; }
        public float Freq { get; private set; }
        public float Amp { get; private set; }
        /// <summary>마지막에 맞춘 신호 점수 · 합계.</summary>
        public int LastScore { get; private set; }
        public int Total { get; private set; }

        public bool Playing => Current == Phase.Ready || Current == Phase.Tune || Current == Phase.Locked;
        public float Remaining => Current == Phase.Tune ? RoundTime - PhaseTime : RoundTime;

        /// <summary>겹친 정도 0 ~ 1 (빈도 · 세기 어긋남을 곱함).</summary>
        public float Match => Clamp01(1f - Abs(Freq - TargetFreq) / FreqSpan) * Clamp01(1f - Abs(Amp - TargetAmp) / AmpSpan);
        /// <summary>받은 파형 흔들림 (세기 비) — 덜 맞을수록 큼.</summary>
        public float Jitter => 0.04f + 0.3f * (1f - Match);
        /// <summary>점수 (0 ~ 100) = 겹친 정도.</summary>
        public int ScoreNow => (int)(Match * 100f + 0.5f);

        public SignalTuneGame(int seed = 1117)
        {
            _random = new System.Random(seed);
        }

        /// <summary>새 판: 첫 신호 준비부터.</summary>
        public void Begin()
        {
            Round = 0;
            Total = LastScore = 0;
            NewSignal();
        }

        public void Stop()
        {
            Current = Phase.Idle;
            PhaseTime = 0f;
        }

        /// <summary>다이얼 돌림 (맞추는 중에만).</summary>
        public void Adjust(float freqDelta, float ampDelta)
        {
            if (Current != Phase.Tune)
                return;
            Freq = Clamp(Freq + freqDelta, FreqMin, FreqMax);
            Amp = Clamp(Amp + ampDelta, AmpMin, AmpMax);
        }

        /// <summary>맞춤 (맞추는 중에만): 지금 겹친 정도가 이 신호 점수. 맞췄으면 true.</summary>
        public bool Lock()
        {
            if (Current != Phase.Tune)
                return false;
            LastScore = ScoreNow;
            Total += LastScore;
            Current = Phase.Locked;
            PhaseTime = 0f;
            return true;
        }

        public void Step(float dt)
        {
            PhaseTime += dt;
            switch (Current)
            {
                case Phase.Ready:
                    if (PhaseTime >= ReadyTime)
                    {
                        Current = Phase.Tune;
                        PhaseTime = 0f;
                    }
                    break;
                case Phase.Tune:
                    if (PhaseTime >= RoundTime)
                        Lock();
                    break;
                case Phase.Locked:
                    if (PhaseTime >= LockedTime)
                    {
                        Round++;
                        if (Round >= Rounds)
                        {
                            Current = Phase.Done;
                            PhaseTime = 0f;
                        }
                        else
                            NewSignal();
                    }
                    break;
            }
        }

        private void NewSignal()
        {
            TargetFreq = Lerp(FreqMin + 0.3f, FreqMax - 0.3f, (float)_random.NextDouble());
            TargetAmp = Lerp(AmpMin + 0.1f, AmpMax - 0.05f, (float)_random.NextDouble());
            // 처음 다이얼: 목표에서 빈도 · 세기 모두 떨어진 곳 (범위 안에서 먼 쪽)
            Freq = TargetFreq + (TargetFreq < (FreqMin + FreqMax) * 0.5f ? 1f : -1f) * (StartGapFreq + 0.6f * (float)_random.NextDouble());
            Amp = TargetAmp + (TargetAmp < (AmpMin + AmpMax) * 0.5f ? 1f : -1f) * (StartGapAmp + 0.1f * (float)_random.NextDouble());
            Freq = Clamp(Freq, FreqMin, FreqMax);
            Amp = Clamp(Amp, AmpMin, AmpMax);
            Current = Phase.Ready;
            PhaseTime = 0f;
        }

        /// <summary>파형 높이 (−1 ~ 1 × 세기): x = 화면 왼쪽 0 → 오른쪽 1.</summary>
        public static float Wave(float x, float freq, float amp, float phase) => amp * (float)System.Math.Sin(2.0 * System.Math.PI * (freq * x + phase));

        private static float Abs(float x) => x < 0f ? -x : x;
        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        private static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
