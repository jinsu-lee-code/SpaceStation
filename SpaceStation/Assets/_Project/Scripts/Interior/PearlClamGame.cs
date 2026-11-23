namespace SpaceStation.Interior
{
    /// <summary>
    /// 궤도 항만 미니게임 "진주 조개 잡기" 판 (사용자 2026-11-16 "미니게임 진행해줘" — 잡기 단 앞 큰 조개 셋이 번갈아 열림). Unity 없이 시험하는 순수 규칙.
    /// 시작 → 뚜껑이 닫히며 3 · 2 · 1 → 30초 동안 닫힌 조개 하나가 (15초 뒤부터는 둘까지) 잠깐 열림 — 열린 조개를 보고 F = 속에 든 것을 잡음:
    /// 진주 +1 · 금빛 진주 +3 (가끔) · 게 −1 (가끔, 꼬집음 — 0 밑으로는 안 내려감). 열린 시간 = <see cref="OpenStart"/> → <see cref="OpenEnd"/>초,
    /// 다음 조개까지 = <see cref="GapStart"/> → <see cref="GapEnd"/>초 (주어진 난수로 ± 25%). 끝나면 조개가 모두 열려 진주를 보여 줌 (쉬는 모습).
    /// </summary>
    public sealed class PearlClamGame
    {
        public enum Phase { Idle, Countdown, Play, Done }
        public enum Content { Pearl, Gold, Crab }
        public enum Catch { Miss, Pearl, Gold, Crab }

        public const int ClamCount = 3;
        public const float CountdownTime = 3f, RoundTime = 30f;
        public const float OpenStart = 1.7f, OpenEnd = 0.9f, GapStart = 0.9f, GapEnd = 0.35f;
        /// <summary>이때부터 조개 둘이 함께 열릴 수 있음 (초).</summary>
        public const float TwoOpenAfter = 15f;
        /// <summary>뚜껑이 다 열리거나 닫히는 시간 (초) · 끝난 뒤 다시 열리는 시간.</summary>
        public const float LidTime = 0.22f, RestOpenTime = 1.2f;
        /// <summary>잡은 뒤 뚜껑이 닫히기 시작하기까지 (진주가 날아오르는 것이 보이게).</summary>
        public const float CaughtHold = 0.3f;
        /// <summary>닫힌 조개가 다시 열릴 수 있기까지 (초).</summary>
        public const float Rest = 0.5f;
        /// <summary>뚜껑이 이만큼 열려야 잡힘 (막 열리기 시작한 틈은 안 됨).</summary>
        public const float CatchLid = 0.5f;
        public const float GoldChance = 0.12f, CrabChance = 0.16f;
        public const int PearlPoints = 1, GoldPoints = 3, CrabPoints = -1;

        private readonly System.Random _random;
        private readonly bool[] _open = new bool[ClamCount];
        private readonly bool[] _taken = new bool[ClamCount];
        private readonly float[] _t = new float[ClamCount];        // 열린 뒤 (잡혔으면 잡힌 뒤) · 닫힌 뒤 지난 시간
        private readonly float[] _window = new float[ClamCount];
        private readonly float[] _lid = new float[ClamCount];
        private readonly Content[] _content = new Content[ClamCount];
        private float _nextOpen;
        private int _last = -1;

        public Phase Current { get; private set; }
        /// <summary>지금 단계에 든 뒤 지난 시간 (초).</summary>
        public float PhaseTime { get; private set; }
        public int Score { get; private set; }
        /// <summary>잡은 진주 (금빛 포함) · 금빛 진주 · 꼬집힌 게 · 열린 조개 수.</summary>
        public int Pearls { get; private set; }
        public int Golds { get; private set; }
        public int Crabs { get; private set; }
        public int Opened { get; private set; }

        public bool Playing => Current == Phase.Play;
        /// <summary>판이 지난 정도 0 ~ 1.</summary>
        public float Progress => Current == Phase.Play ? Clamp01(PhaseTime / RoundTime) : Current == Phase.Done ? 1f : 0f;
        public float Remaining => Current == Phase.Play ? RoundTime - PhaseTime : Current == Phase.Done ? 0f : RoundTime;
        /// <summary>지금 열리는 조개가 열려 있는 시간 (초).</summary>
        public float Window => OpenStart + (OpenEnd - OpenStart) * Progress;

        /// <summary>열려 있고 아직 잡히지 않은 조개 (속이 보임).</summary>
        public bool IsOpen(int k) => _open[k] && !_taken[k];
        /// <summary>이번에 열린 동안 속을 이미 잡음 (진주가 날아가는 중 · 뚜껑이 닫히는 중).</summary>
        public bool Taken(int k) => _taken[k];
        public Content ContentOf(int k) => _content[k];
        /// <summary>뚜껑 열림 0 (닫힘) ~ 1 (다 열림 = 모델 그대로 52°).</summary>
        public float Lid(int k) => _lid[k];
        /// <summary>판 중 속이 보이는 조개 (열렸거나 닫히는 중 — 잡힌 뒤는 빼고). 쉬는 동안은 모두 진주.</summary>
        public bool ShowsContent(int k) => Current != Phase.Countdown && Current != Phase.Play || (_lid[k] > 0.01f && !_taken[k]);

        public PearlClamGame(int seed = 1116)
        {
            _random = new System.Random(seed);
            for (int k = 0; k < ClamCount; k++)
                _lid[k] = 1f;
        }

        /// <summary>새 판: 뚜껑이 닫히며 3 · 2 · 1부터.</summary>
        public void Start()
        {
            Current = Phase.Countdown;
            PhaseTime = 0f;
            Score = Pearls = Golds = Crabs = Opened = 0;
            for (int k = 0; k < ClamCount; k++)
            {
                _open[k] = _taken[k] = false;
                _t[k] = Rest;
                _content[k] = Content.Pearl;
            }
            _nextOpen = 0.4f;
            _last = -1;
        }

        /// <summary>판을 그만둠 (방을 나감 · 다 끝나고 쉼) — 조개는 진주를 보이며 천천히 열림.</summary>
        public void Stop()
        {
            Current = Phase.Idle;
            PhaseTime = 0f;
            for (int k = 0; k < ClamCount; k++)
            {
                _open[k] = _taken[k] = false;
                _content[k] = Content.Pearl;
            }
        }

        /// <summary>판을 일찍 끝냄 (잡기 단을 떠남) — 지금 점수로 끝.</summary>
        public void Finish()
        {
            if (Current == Phase.Countdown || Current == Phase.Play)
                EnterDone();
        }

        public void Tick(float dt)
        {
            PhaseTime += dt;
            switch (Current)
            {
                case Phase.Countdown:
                    if (PhaseTime >= CountdownTime)
                    {
                        Current = Phase.Play;
                        PhaseTime = 0f;
                    }
                    break;
                case Phase.Play:
                    StepClams(dt);
                    if (PhaseTime >= RoundTime)
                        EnterDone();
                    break;
            }
            for (int k = 0; k < ClamCount; k++)
            {
                float target = Current == Phase.Countdown || Current == Phase.Play ? (_open[k] ? 1f : 0f) : 1f;
                float rate = Current == Phase.Done || Current == Phase.Idle ? 1f / RestOpenTime : 1f / LidTime;
                _lid[k] = _lid[k] < target ? Min(target, _lid[k] + dt * rate) : Max(target, _lid[k] - dt * rate);
            }
        }

        private void EnterDone()
        {
            Current = Phase.Done;
            PhaseTime = 0f;
            for (int k = 0; k < ClamCount; k++)
            {
                _open[k] = _taken[k] = false;
                _content[k] = Content.Pearl;
            }
        }

        private void StepClams(float dt)
        {
            for (int k = 0; k < ClamCount; k++)
            {
                _t[k] += dt;
                if (_open[k] && _t[k] >= (_taken[k] ? CaughtHold : _window[k]))
                {
                    _open[k] = false;
                    _t[k] = 0f;
                    _nextOpen = Max(_nextOpen, PhaseTime + Gap());
                }
                else if (!_open[k] && _taken[k] && _lid[k] <= 0f)
                    _taken[k] = false;                                  // 다 닫힘 → 다음에 열리면 새 속
            }
            if (PhaseTime < _nextOpen)
                return;
            int open = 0;
            for (int k = 0; k < ClamCount; k++)
                if (_open[k])
                    open++;
            if (open >= (PhaseTime >= TwoOpenAfter ? 2 : 1))
                return;
            // 닫혀 쉰 조개 중 하나 (바로 앞에 열린 조개는 다른 게 있으면 피함)
            int pick = -1, count = 0;
            for (int pass = 0; pass < 2 && pick < 0; pass++)
            {
                for (int k = 0; k < ClamCount; k++)
                {
                    if (_open[k] || _taken[k] || _lid[k] > 0f || _t[k] < Rest || (pass == 0 && k == _last))
                        continue;
                    count++;
                    if (_random.Next(count) == 0)
                        pick = k;
                }
            }
            if (pick < 0)
                return;
            double roll = _random.NextDouble();
            _content[pick] = roll < GoldChance ? Content.Gold : roll < GoldChance + CrabChance ? Content.Crab : Content.Pearl;
            _open[pick] = true;
            _taken[pick] = false;
            _t[pick] = 0f;
            _window[pick] = Window;
            _last = pick;
            Opened++;
            _nextOpen = PhaseTime + Gap();
        }

        private float Gap()
        {
            float g = GapStart + (GapEnd - GapStart) * Progress;
            return g * (0.75f + 0.5f * (float)_random.NextDouble());
        }

        /// <summary>조개 k 속을 잡음 (F): 열려 있고 반 넘게 벌어져 있어야 함, 아니면 Miss.</summary>
        public Catch TryCatch(int k)
        {
            if (!Playing || k < 0 || k >= ClamCount || !_open[k] || _taken[k] || _lid[k] < CatchLid)
                return Catch.Miss;
            _taken[k] = true;
            _t[k] = 0f;
            switch (_content[k])
            {
                case Content.Gold:
                    Score += GoldPoints;
                    Pearls++;
                    Golds++;
                    return Catch.Gold;
                case Content.Crab:
                    Score = Max(0, Score + CrabPoints);
                    Crabs++;
                    return Catch.Crab;
                default:
                    Score += PearlPoints;
                    Pearls++;
                    return Catch.Pearl;
            }
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        private static float Min(float a, float b) => a < b ? a : b;
        private static float Max(float a, float b) => a > b ? a : b;
        private static int Max(int a, int b) => a > b ? a : b;
    }
}
