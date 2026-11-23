using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 항성 발전망 미니게임 "벌 몰래 꿀 먹기" 규칙 (사용자 제안 — 선생님 몰래 춤추기처럼, 곰 주민이 되어 경비 꿀벌 몰래 꿀을 먹음). Unity 없이 시험하는 순수 규칙.
    /// 경비 꿀벌 한 마리가 등을 돌리고 있다가 (Back) 날갯짓이 빨라지며 "!"가 깜박이고 (Warn) 돌아봄 (Turn → Watch → TurnBack).
    /// 가끔은 돌아보는 척만 함 (Feint — 반쯤 돌다 다시 등). 꿀벌이 곰 쪽을 보는 동안 (돌아본 정도 ≥ <see cref="SeeFrom"/>) 먹고 있으면 들킴 →
    /// 곰 코를 콕 쏘고 (Sting) 목숨 하나가 줄어듦, 셋째에 끝. 먹는 동안 꿀이 <see cref="SpoonSeconds"/>마다 한 숟갈, 꿀단지 (<see cref="PotSpoons"/>숟갈)를 다 비워도 끝.
    /// 많이 먹을수록 등 돌린 시간 · 경고가 짧아지고 속임수가 늘어남.
    /// </summary>
    public sealed class HoneySneak
    {
        public enum Guard { Back, Warn, Turn, Watch, TurnBack, Feint, Sting }
        public enum Event { None, Spoon, Caught, Finished }

        public const int Lives = 3, PotSpoons = 30;
        public const float SpoonSeconds = 0.6f, ReadySeconds = 1.8f;
        public const float TurnSeconds = 0.22f, TurnBackSeconds = 0.45f, FeintSeconds = 0.6f, StingSeconds = 1.4f;
        /// <summary>이만큼 돌아봤으면 곰을 봄 (0 = 등, 1 = 곰을 똑바로).</summary>
        public const float SeeFrom = 0.6f;
        /// <summary>속임수 때 돌아보는 정도 (곰을 못 봄).</summary>
        public const float FeintTurn = 0.35f;

        private System.Random _random = new System.Random(1);
        private float _stateT, _stateLength;
        private float _ready;
        private float _spoonT;

        public bool Running { get; private set; }
        public bool Finished { get; private set; }
        /// <summary>꿀단지를 다 비워서 끝남 (목숨이 남은 채).</summary>
        public bool Emptied => Finished && Spoons >= PotSpoons;
        public int LivesLeft { get; private set; }
        public int Spoons { get; private set; }
        public Guard State { get; private set; }
        /// <summary>지금 상태 진행 0 ~ 1.</summary>
        public float StateProgress => _stateLength > 0f ? Mathf.Clamp01(_stateT / _stateLength) : 1f;
        /// <summary>시작 준비 중 (먹어도 안 셈 · 들키지 않음).</summary>
        public bool Ready => _ready > 0f;
        /// <summary>지금 숟갈 진행 0 ~ 1 (곰 팔이 꿀단지 → 입).</summary>
        public float SpoonProgress => Mathf.Clamp01(_spoonT / SpoonSeconds);
        /// <summary>얼마나 먹었나 0 ~ 1 (빨라짐).</summary>
        public float Difficulty => Mathf.Clamp01((float)Spoons / PotSpoons);

        /// <summary>돌아본 정도 0 (등) ~ 1 (곰을 똑바로).</summary>
        public float Turn
        {
            get
            {
                float p = StateProgress;
                switch (State)
                {
                    case Guard.Turn: return p;
                    case Guard.Watch: return 1f;
                    case Guard.Sting: return 1f;
                    case Guard.TurnBack: return 1f - p;
                    case Guard.Feint: return FeintTurn * Mathf.Sin(p * Mathf.PI);
                    default: return 0f;
                }
            }
        }

        /// <summary>꿀벌이 곰을 보고 있음.</summary>
        public bool Seeing => Turn >= SeeFrom && State != Guard.Sting;

        public void Start(int seed)
        {
            _random = new System.Random(seed);
            Running = true;
            Finished = false;
            LivesLeft = Lives;
            Spoons = 0;
            _spoonT = 0f;
            _ready = ReadySeconds;
            Enter(Guard.Back, BackSeconds() + 0.6f);   // 첫 판은 조금 넉넉히
        }

        public void Stop()
        {
            Running = false;
        }

        private float Rand(float a, float b) => a + (float)_random.NextDouble() * (b - a);

        private float BackSeconds()
        {
            float d = Difficulty;
            return Rand(Mathf.Lerp(2.2f, 1.0f, d), Mathf.Lerp(4.6f, 2.4f, d));
        }

        private float WarnSeconds()
        {
            float d = Difficulty;
            return Rand(Mathf.Lerp(0.9f, 0.45f, d), Mathf.Lerp(1.15f, 0.62f, d));
        }

        private void Enter(Guard state, float seconds)
        {
            State = state;
            _stateT = 0f;
            _stateLength = seconds;
        }

        /// <summary>시간을 보냄. eating = 지금 F를 누르고 먹는 중.</summary>
        public Event Step(float dt, bool eating)
        {
            if (!Running || Finished || dt <= 0f)
                return Event.None;
            if (_ready > 0f)
            {
                _ready -= dt;
                return Event.None;   // 준비 동안 꿀벌은 등을 돌린 채
            }
            var result = Event.None;
            // 먹기 (쏘이는 중엔 못 먹음)
            if (eating && State != Guard.Sting)
            {
                _spoonT += dt;
                if (_spoonT >= SpoonSeconds)
                {
                    _spoonT -= SpoonSeconds;
                    Spoons++;
                    result = Event.Spoon;
                    if (Spoons >= PotSpoons)
                    {
                        Finished = true;
                        return Event.Finished;
                    }
                }
            }
            else if (!eating)
                _spoonT = Mathf.Max(0f, _spoonT - dt * 2f);   // 손을 떼면 팔이 꿀단지로 돌아감
            // 들킴
            if (eating && Seeing)
            {
                LivesLeft--;
                _spoonT = 0f;
                Enter(Guard.Sting, StingSeconds);
                return Event.Caught;
            }
            _stateT += dt;
            if (_stateT < _stateLength)
                return result;
            switch (State)
            {
                case Guard.Back:
                    Enter(Guard.Warn, WarnSeconds());
                    break;
                case Guard.Warn:
                    if (_random.NextDouble() < Mathf.Lerp(0.15f, 0.4f, Difficulty))
                        Enter(Guard.Feint, FeintSeconds);
                    else
                        Enter(Guard.Turn, TurnSeconds);
                    break;
                case Guard.Turn:
                    Enter(Guard.Watch, Rand(1.2f, 2.0f));
                    break;
                case Guard.Watch:
                    Enter(Guard.TurnBack, TurnBackSeconds);
                    break;
                case Guard.Sting:
                    if (LivesLeft <= 0)
                    {
                        Finished = true;
                        return Event.Finished;
                    }
                    Enter(Guard.TurnBack, TurnBackSeconds);
                    break;
                case Guard.TurnBack:
                case Guard.Feint:
                    Enter(Guard.Back, BackSeconds());
                    break;
            }
            return result;
        }
    }
}
