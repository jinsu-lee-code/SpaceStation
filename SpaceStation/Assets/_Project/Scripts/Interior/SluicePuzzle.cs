using System.Collections.Generic;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 바이오스피어 체험 "물꼬 맞추기" 규칙 (사용자 2026-11-11 — 물길 수문 맞추기 · 물꼬 단 고르기 퍼즐). Unity 없이 시험하는 순수 규칙.
    /// 물길 셋마다 수문 바퀴: 돌릴 때마다 물꼬가 한 단씩 내려감 (닫힘 0 → 1단 (맨 위) → … → 5단 (호수 쪽) → 닫힘).
    /// 물꼬가 n단이면 물은 그 단까지 흘러 그 물길 양옆 밭 (계단 사이 120°)으로 들어가고, 아래 단은 마름.
    /// 판마다 목마른 밭 (물길 · 단)이 뜨고, 바퀴 셋을 맞춰 목마른 밭에 한꺼번에 물이 닿으면 판 성공. 세 판 (1곳 → 2곳 → 3곳 — 물길마다 하나).
    /// </summary>
    public sealed class SluicePuzzle
    {
        public const int StreamCount = 3, Levels = 5, Rounds = 3;
        /// <summary>쉬는 동안 (체험 밖) 물꼬 = 호수까지 다 흐름.</summary>
        public const int Lake = Levels + 1;

        private readonly int[] _gates = new int[StreamCount];
        private readonly List<(int Stream, int Level)> _thirsty = new List<(int, int)>();
        private System.Random _random = new System.Random(1);

        public bool Running { get; private set; }
        public bool Finished { get; private set; }
        /// <summary>지금 판 (0부터).</summary>
        public int Round { get; private set; }
        public IReadOnlyList<(int Stream, int Level)> Thirsty => _thirsty;

        public SluicePuzzle()
        {
            Reset();
        }

        /// <summary>물꼬 (0 = 닫힘, 1 ~ 5 = 단, 6 = 호수까지 — 체험 밖).</summary>
        public int Gate(int stream) => _gates[stream];

        /// <summary>체험 밖: 물꼬를 모두 호수까지 열고 목마른 밭 없음.</summary>
        public void Reset()
        {
            Running = false;
            Finished = false;
            Round = 0;
            _thirsty.Clear();
            for (int i = 0; i < StreamCount; i++)
                _gates[i] = Lake;
        }

        /// <summary>시작: 물꼬를 모두 닫고 첫 판.</summary>
        public void Start(int seed)
        {
            _random = new System.Random(seed);
            Running = true;
            Finished = false;
            Round = 0;
            for (int i = 0; i < StreamCount; i++)
                _gates[i] = 0;
            NewRound();
        }

        /// <summary>바퀴를 한 번 돌림 → 새 물꼬.</summary>
        public int Turn(int stream)
        {
            if (!Running)
                return _gates[stream];
            _gates[stream] = (_gates[stream] + 1) % (Levels + 1);
            return _gates[stream];
        }

        /// <summary>물꼬가 이 밭 (물길 · 단)에 물을 대는지.</summary>
        public bool Waters(int stream, int level) => _gates[stream] == level;

        public bool IsThirsty(int stream, int level)
        {
            foreach (var t in _thirsty)
            {
                if (t.Stream == stream && t.Level == level)
                    return true;
            }
            return false;
        }

        /// <summary>목마른 밭 모두에 지금 물꼬가 맞음 (물이 실제로 닿았는지는 연출이 따로 봄).</summary>
        public bool Solved
        {
            get
            {
                if (!Running || _thirsty.Count == 0)
                    return false;
                foreach (var t in _thirsty)
                {
                    if (_gates[t.Stream] != t.Level)
                        return false;
                }
                return true;
            }
        }

        /// <summary>판을 넘김. 마지막 판이면 끝 (true).</summary>
        public bool Advance()
        {
            if (!Running)
                return Finished;
            Round++;
            if (Round >= Rounds)
            {
                Running = false;
                Finished = true;
                _thirsty.Clear();
                return true;
            }
            NewRound();
            return false;
        }

        /// <summary>판 = 목마른 밭 Round + 1곳 (물길마다 하나, 물길은 섞어서), 단은 지금 물꼬와 다르게 (이미 맞아 있으면 생각할 거리가 없음).</summary>
        private void NewRound()
        {
            _thirsty.Clear();
            var streams = new List<int> { 0, 1, 2 };
            for (int i = streams.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (streams[i], streams[j]) = (streams[j], streams[i]);
            }
            int count = System.Math.Min(Round + 1, StreamCount);
            for (int i = 0; i < count; i++)
            {
                int s = streams[i];
                int level;
                do
                    level = 1 + _random.Next(Levels);
                while (level == _gates[s]);
                _thirsty.Add((s, level));
            }
        }
    }
}
