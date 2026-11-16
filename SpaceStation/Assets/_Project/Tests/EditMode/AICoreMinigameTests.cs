using NUnit.Framework;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>AI 중추 미니게임 "뇌 미로 빨리 빠져나가기" 판 규칙 (<see cref="MazeRunGame"/> — Unity 없이).</summary>
    public class AICoreMinigameTests
    {
        private static bool[] Front()
        {
            var f = new bool[AICore.Orbs.Length];
            for (int i = 0; i < f.Length; i++)
                f[i] = AICore.Orbs[i].Front;
            return f;
        }

        [Test]
        public void MazeRun_PicksOrbsFromBothHalves()
        {
            var front = Front();
            for (int seed = 0; seed < 40; seed++)
            {
                var g = new MazeRunGame(front, seed: seed);
                g.Begin();
                Assert.AreEqual(AICore.MazeOrbCount, g.Needed, $"seed {seed}");
                bool f = false, b = false;
                for (int i = 0; i < g.Needed; i++)
                {
                    f |= front[g.Orbs[i]];
                    b |= !front[g.Orbs[i]];
                    for (int j = 0; j < i; j++)
                        Assert.AreNotEqual(g.Orbs[j], g.Orbs[i], $"seed {seed}: 같은 자리 둘");
                }
                Assert.IsTrue(f && b, $"seed {seed}: 앞 · 뒤 반구 하나씩 넘게");
            }
        }

        [Test]
        public void MazeRun_CountdownCollectArrive()
        {
            var g = new MazeRunGame(Front());
            g.Begin();
            Assert.AreEqual(MazeRunGame.Phase.Countdown, g.Current);
            Assert.AreEqual(MazeRunGame.Result.Ignored, g.Collect(0), "세는 동안은 못 주움");
            g.Step(MazeRunGame.CountdownTime + 0.01f);
            Assert.AreEqual(MazeRunGame.Phase.Run, g.Current);
            g.Step(10f);
            Assert.AreEqual(10f, g.Elapsed, 1e-4f);
            Assert.AreEqual(MazeRunGame.Result.NeedMore, g.Arrive(), "구슬 없이 정자");
            Assert.AreEqual(MazeRunGame.Result.Collected, g.Collect(0));
            Assert.AreEqual(MazeRunGame.Result.Ignored, g.Collect(0), "같은 구슬 두 번");
            Assert.AreEqual(MazeRunGame.Result.Collected, g.Collect(1));
            Assert.AreEqual(MazeRunGame.Result.NeedMore, g.Arrive());
            Assert.AreEqual(MazeRunGame.Result.AllCollected, g.Collect(2));
            g.Step(5f);
            Assert.AreEqual(MazeRunGame.Result.Finished, g.Arrive());
            Assert.AreEqual(MazeRunGame.Phase.Done, g.Current);
            Assert.AreEqual(15f, g.Score, 1e-4f, "기록 = 달린 시간");
            Assert.IsFalse(g.TimedOut);
            Assert.AreEqual(MazeRunGame.Result.Ignored, g.Arrive(), "끝난 뒤");
        }

        [Test]
        public void MazeRun_TimesOut()
        {
            var g = new MazeRunGame(Front(), timeLimit: 30f);
            g.Begin();
            g.Step(MazeRunGame.CountdownTime + 0.01f);
            for (int i = 0; i < 40; i++)
                g.Step(1f);
            Assert.AreEqual(MazeRunGame.Phase.Done, g.Current);
            Assert.IsTrue(g.TimedOut);
            Assert.AreEqual(0f, g.Score, "시간이 넘으면 기록 없음");
        }
    }
}
