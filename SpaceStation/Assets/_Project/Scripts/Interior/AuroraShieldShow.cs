namespace SpaceStation.Interior
{
    /// <summary>
    /// 궤도 방어망 체험 "오로라 보호막 펼치기" 순서 (사용자 2026-11-13 "레버 → 보호막 쇼 관람"). Unity 없이 시험하는 순수 규칙.
    /// 쉼 → 레버 당김 → 막이 레버 자리에서 퍼져 덮음 → 버팀 (별똥별이 떨어져 막에 부딪힘) → 접힘 → 레버가 돌아옴 → 쉼. 점수 없음 (막은 별똥별 수만 셈).
    /// 별똥별 사이 = <see cref="MeteorGapMin"/> ~ <see cref="MeteorGapMax"/>초 (주어진 난수), 막이 다 펼쳐지기 조금 전부터 · 접히기 전까지만.
    /// </summary>
    public sealed class AuroraShieldShow
    {
        public enum Phase { Idle, Pull, Unfold, Hold, Fold, Return }

        public const float PullTime = 0.6f, UnfoldTime = 5f, HoldTime = 36f, FoldTime = 3f, ReturnTime = 0.8f;
        public const float MeteorGapMin = 1.6f, MeteorGapMax = 3.2f;
        /// <summary>펼침이 이만큼 남았을 때부터 별똥별이 떨어짐 (초).</summary>
        public const float MeteorLead = 1.5f;

        private readonly System.Random _random;
        private float _nextMeteor;

        public Phase Current { get; private set; }
        /// <summary>지금 단계에 든 뒤 지난 시간 (초).</summary>
        public float PhaseTime { get; private set; }
        /// <summary>이번 쇼에서 막은 별똥별 수.</summary>
        public int Blocked { get; private set; }
        public bool Running => Current != Phase.Idle;

        public AuroraShieldShow(int seed = 1113)
        {
            _random = new System.Random(seed);
        }

        /// <summary>레버를 당김 (쉬는 중에만). 당겼으면 true.</summary>
        public bool Start()
        {
            if (Running)
                return false;
            Enter(Phase.Pull);
            Blocked = 0;
            _nextMeteor = UnfoldTime - MeteorLead;
            return true;
        }

        /// <summary>막이 별똥별을 막았음 (닿은 순간).</summary>
        public void CountBlocked() => Blocked++;

        /// <summary>레버 당긴 정도 0 (선 채) ~ 1 (다 당김).</summary>
        public float Lever => Current switch
        {
            Phase.Pull => Ease(PhaseTime / PullTime),
            Phase.Unfold or Phase.Hold or Phase.Fold => 1f,
            Phase.Return => 1f - Ease(PhaseTime / ReturnTime),
            _ => 0f,
        };

        /// <summary>막이 레버 자리에서 퍼진 정도 0 ~ 1 (다 덮음).</summary>
        public float Spread => Current switch
        {
            Phase.Unfold => 1f - (1f - Clamp01(PhaseTime / UnfoldTime)) * (1f - Clamp01(PhaseTime / UnfoldTime)),
            Phase.Hold or Phase.Fold => 1f,
            _ => 0f,
        };

        /// <summary>막 밝기 0 ~ 1 (펼칠 때 켜지고 접힐 때 꺼짐).</summary>
        public float Alpha => Current switch
        {
            Phase.Unfold => Clamp01(PhaseTime / 0.4f),
            Phase.Hold => 1f,
            Phase.Fold => 1f - Ease(PhaseTime / FoldTime),
            _ => 0f,
        };

        /// <summary>한 프레임: 별똥별이 떨어질 차례면 true (한 프레임에 하나).</summary>
        public bool Step(float dt)
        {
            if (!Running)
                return false;
            PhaseTime += dt;
            bool meteor = false;
            if (Current == Phase.Unfold || Current == Phase.Hold)
            {
                float showTime = (Current == Phase.Unfold ? 0f : UnfoldTime) + PhaseTime;
                if (showTime >= _nextMeteor && showTime < UnfoldTime + HoldTime - 1f)
                {
                    meteor = true;
                    _nextMeteor = showTime + MeteorGapMin + (float)_random.NextDouble() * (MeteorGapMax - MeteorGapMin);
                }
            }
            float length = Current switch
            {
                Phase.Pull => PullTime,
                Phase.Unfold => UnfoldTime,
                Phase.Hold => HoldTime,
                Phase.Fold => FoldTime,
                _ => ReturnTime,
            };
            if (PhaseTime >= length)
                Enter(Current == Phase.Return ? Phase.Idle : Current + 1);
            return meteor;
        }

        private void Enter(Phase phase)
        {
            Current = phase;
            PhaseTime = 0f;
        }

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        private static float Ease(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }
}
