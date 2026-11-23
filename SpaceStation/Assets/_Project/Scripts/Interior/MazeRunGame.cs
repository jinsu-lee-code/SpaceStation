using System.Collections.Generic;

namespace SpaceStation.Interior
{
    /// <summary>
    /// AI 중추 미니게임 "뇌 미로 빨리 빠져나가기" 판 (사용자 2026-11-16 "빛 구슬 모으는 부분도 같이 넣어줘" — 막다른 길의 빛 구슬 셋을 모은 뒤 정자로). Unity 없이 시험하는 순수 규칙.
    /// 준비 (세는 동안 <see cref="CountdownTime"/>초) → 달리기 (시간을 잼, 빛 구슬 자리 후보 중 셋 — 앞 · 뒤 반구 하나씩 넘게) → 구슬을 모두 모아 정자에 닿으면 끝 (기록 = 걸린 시간, 짧을수록 좋음).
    /// 구슬이 모자란 채 정자에 닿으면 알려 주기만 함. 시간 한도를 넘거나 그만두면 기록 없이 끝.
    /// </summary>
    public sealed class MazeRunGame
    {
        public enum Phase { Idle, Countdown, Run, Done }
        public enum Result { Ignored, Collected, AllCollected, NeedMore, Finished }
        public const float CountdownTime = 3f;

        private readonly bool[] _front;
        private readonly int _count;
        private readonly float _limit;
        private readonly System.Random _random;
        private readonly List<int> _orbs = new List<int>();
        private readonly List<bool> _taken = new List<bool>();

        /// <param name="front">빛 구슬 자리 후보마다 앞 반구인지 (<see cref="AICore.Orbs"/>).</param>
        public MazeRunGame(bool[] front, int count = AICore.MazeOrbCount, float timeLimit = AICore.MazeTimeLimit, int seed = 1116)
        {
            _front = front;
            _count = System.Math.Min(count, front.Length);
            _limit = timeLimit;
            _random = new System.Random(seed);
        }

        public Phase Current { get; private set; }
        public float PhaseTime { get; private set; }
        public bool Running => Current == Phase.Countdown || Current == Phase.Run;
        /// <summary>이번 판 빛 구슬 자리 (후보 번호).</summary>
        public IReadOnlyList<int> Orbs => _orbs;
        public int Collected { get; private set; }
        public int Needed => _orbs.Count;
        /// <summary>달린 시간 (초) · 이번 판 기록 (끝까지 갔으면 걸린 시간, 아니면 0) · 끝난 까닭 (시간 넘김).</summary>
        public float Elapsed { get; private set; }
        public float Score { get; private set; }
        public bool TimedOut { get; private set; }

        /// <summary>새 판: 빛 구슬 자리를 고르고 (앞 · 뒤 반구 하나씩 넘게) 세기 시작.</summary>
        public void Begin()
        {
            _orbs.Clear();
            _taken.Clear();
            var front = new List<int>();
            var back = new List<int>();
            for (int i = 0; i < _front.Length; i++)
                (_front[i] ? front : back).Add(i);
            if (front.Count > 0 && _orbs.Count < _count)
                _orbs.Add(front[_random.Next(front.Count)]);
            if (back.Count > 0 && _orbs.Count < _count)
                _orbs.Add(back[_random.Next(back.Count)]);
            var rest = new List<int>();
            for (int i = 0; i < _front.Length; i++)
                if (!_orbs.Contains(i))
                    rest.Add(i);
            while (_orbs.Count < _count && rest.Count > 0)
            {
                int k = _random.Next(rest.Count);
                _orbs.Add(rest[k]);
                rest.RemoveAt(k);
            }
            _orbs.Sort();
            for (int i = 0; i < _orbs.Count; i++)
                _taken.Add(false);
            Collected = 0;
            Elapsed = 0f;
            Score = 0f;
            TimedOut = false;
            Enter(Phase.Countdown);
        }

        public void Stop() => Enter(Phase.Idle);

        /// <summary>이번 판 i번째 구슬을 이미 주웠는지.</summary>
        public bool Taken(int i) => i >= 0 && i < _taken.Count && _taken[i];

        /// <summary>달리는 중 구슬 줍기 (이번 판 i번째).</summary>
        public Result Collect(int i)
        {
            if (Current != Phase.Run || i < 0 || i >= _taken.Count || _taken[i])
                return Result.Ignored;
            _taken[i] = true;
            Collected++;
            return Collected >= _orbs.Count ? Result.AllCollected : Result.Collected;
        }

        /// <summary>정자에 닿음: 구슬을 다 모았으면 끝 (기록), 아니면 모자람.</summary>
        public Result Arrive()
        {
            if (Current != Phase.Run)
                return Result.Ignored;
            if (Collected < _orbs.Count)
                return Result.NeedMore;
            Score = Elapsed;
            Enter(Phase.Done);
            return Result.Finished;
        }

        public void Step(float dt)
        {
            if (!Running)
                return;
            PhaseTime += dt;
            if (Current == Phase.Countdown && PhaseTime >= CountdownTime)
            {
                Enter(Phase.Run);
                return;
            }
            if (Current == Phase.Run)
            {
                Elapsed += dt;
                if (Elapsed >= _limit)
                {
                    TimedOut = true;
                    Enter(Phase.Done);
                }
            }
        }

        private void Enter(Phase phase)
        {
            Current = phase;
            PhaseTime = 0f;
        }
    }
}
