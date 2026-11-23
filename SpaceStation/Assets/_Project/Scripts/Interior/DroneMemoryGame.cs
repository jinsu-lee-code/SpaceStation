using System.Collections.Generic;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 수리 사령부 미니게임 "드론 순서 기억하기" 판 (TASKS 2026-11-13 "고목 둘레 등불 다섯이 반짝이는 순서를 기억해 똑같이 켜기 — 자리 · 밝기로 구분, 맞힐수록 길어짐 · 최고 기록",
    /// 사용자 2026-11-15 "미니게임으로 넘어가자"). Unity 없이 시험하는 순수 규칙.
    /// 준비 → 보여 주기 (반딧불 드론이 등불을 하나씩 날아가 켬: 날아감 <see cref="FlyTime"/> + 켜짐 <see cref="LightTime"/>) → 따라 하기 (같은 순서로 고름) →
    /// 다 맞히면 잠깐 기뻐한 뒤 한 개를 더한 같은 순서로 다시 (길수록 빨라짐), 하나라도 틀리면 끝. 기록 = 끝까지 따라 한 가장 긴 순서.
    /// </summary>
    public sealed class DroneMemoryGame
    {
        public enum Phase { Idle, Ready, Show, Input, Cleared, Done }
        public enum Result { Ignored, Correct, Cleared, Wrong }

        public const int StartLength = 3;
        public const float ReadyTime = 1.6f, ClearedTime = 1.3f, ShowTail = 0.3f;
        /// <summary>등불 하나로 날아가는 시간 · 켜져 있는 시간 (처음 → 가장 빠를 때) · 순서가 하나 길어질 때마다 줄어드는 정도.</summary>
        public const float FlyStart = 0.7f, FlyMin = 0.38f, LightStart = 0.55f, LightMin = 0.3f, FlyFaster = 0.04f, LightFaster = 0.03f;

        private readonly int _lamps;
        private readonly System.Random _random;
        private readonly List<int> _sequence = new List<int>();

        public DroneMemoryGame(int lamps, int seed = 1115)
        {
            _lamps = lamps;
            _random = new System.Random(seed);
        }

        public Phase Current { get; private set; }
        public float PhaseTime { get; private set; }
        public bool Running => Current != Phase.Idle;
        public IReadOnlyList<int> Sequence => _sequence;
        public int Length => _sequence.Count;
        /// <summary>따라 하기에서 맞힌 개수 (이번 순서).</summary>
        public int Entered { get; private set; }
        /// <summary>끝까지 따라 한 가장 긴 순서 (기록).</summary>
        public int Score { get; private set; }
        /// <summary>틀리게 고른 등불 · 그때 골라야 했던 등불 (끝난 뒤 알려 줌, 없으면 −1).</summary>
        public int WrongLamp { get; private set; } = -1;
        public int ExpectedLamp { get; private set; } = -1;

        public float FlyTime => Faster(FlyStart, FlyMin, FlyFaster);
        public float LightTime => Faster(LightStart, LightMin, LightFaster);
        public float StepTime => FlyTime + LightTime;
        public float ShowTime => Length * StepTime + ShowTail;

        /// <summary>새 판: 처음 순서 (StartLength개) → 준비.</summary>
        public void Begin()
        {
            _sequence.Clear();
            for (int i = 0; i < StartLength; i++)
                Append();
            Score = 0;
            Entered = 0;
            WrongLamp = ExpectedLamp = -1;
            Enter(Phase.Ready);
        }

        public void Stop() => Enter(Phase.Idle);

        /// <summary>보여 주기에서 지금 차례 (0부터, 보여 주기가 아니면 −1).</summary>
        public int ShowIndex
        {
            get
            {
                if (Current != Phase.Show)
                    return -1;
                int i = (int)(PhaseTime / StepTime);
                return i < Length ? i : -1;
            }
        }

        /// <summary>보여 주기에서 드론이 지금 등불로 날아간 정도 0 ~ 1 (켜져 있는 동안 1).</summary>
        public float ShowFly
        {
            get
            {
                int i = ShowIndex;
                if (i < 0)
                    return 1f;
                float t = PhaseTime - i * StepTime;
                return t >= FlyTime ? 1f : t / FlyTime;
            }
        }

        /// <summary>보여 주기에서 지금 켜져 있는 등불 (날아가는 중이면 −1).</summary>
        public int ShowLit
        {
            get
            {
                int i = ShowIndex;
                return i >= 0 && PhaseTime - i * StepTime >= FlyTime ? _sequence[i] : -1;
            }
        }

        /// <summary>따라 하기: 등불을 고름.</summary>
        public Result Pick(int lamp)
        {
            if (Current != Phase.Input || lamp < 0 || lamp >= _lamps)
                return Result.Ignored;
            if (lamp != _sequence[Entered])
            {
                WrongLamp = lamp;
                ExpectedLamp = _sequence[Entered];
                Enter(Phase.Done);
                return Result.Wrong;
            }
            Entered++;
            if (Entered < Length)
                return Result.Correct;
            Score = Length;
            Enter(Phase.Cleared);
            return Result.Cleared;
        }

        public void Step(float dt)
        {
            if (!Running)
                return;
            PhaseTime += dt;
            switch (Current)
            {
                case Phase.Ready when PhaseTime >= ReadyTime:
                    Enter(Phase.Show);
                    break;
                case Phase.Show when PhaseTime >= ShowTime:
                    Entered = 0;
                    Enter(Phase.Input);
                    break;
                case Phase.Cleared when PhaseTime >= ClearedTime:
                    Append();
                    Entered = 0;
                    Enter(Phase.Show);
                    break;
            }
        }

        /// <summary>순서에 등불 하나를 더함 (같은 등불이 셋 잇따르지는 않게).</summary>
        private void Append()
        {
            int n = _sequence.Count;
            int next;
            do
                next = _random.Next(_lamps);
            while (n >= 2 && _lamps > 1 && next == _sequence[n - 1] && next == _sequence[n - 2]);
            _sequence.Add(next);
        }

        private float Faster(float start, float min, float per)
        {
            float t = start - per * (Length - StartLength);
            return t < min ? min : t;
        }

        private void Enter(Phase phase)
        {
            Current = phase;
            PhaseTime = 0f;
        }
    }
}
