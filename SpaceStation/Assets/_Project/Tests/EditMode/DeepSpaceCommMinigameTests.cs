using NUnit.Framework;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>심우주 통신기지 미니게임 판 둘 (Unity 없이): 신호 맞추기 (<see cref="SignalTuneGame"/>) · 별똥별 잡기 (<see cref="StarCatchGame"/>).</summary>
    public class DeepSpaceCommMinigameTests
    {
        /// <summary>신호 맞추기: 처음 다이얼은 목표에서 떨어져 있음 (겹침 30 % 밑), 목표에 맞추면 100점, 시간이 다 되면 저절로 맞춤, 다섯 신호 뒤 끝.</summary>
        [Test]
        public void SignalTune_RoundsAndScore()
        {
            var g = new SignalTuneGame(5);
            g.Begin();
            Assert.AreEqual(SignalTuneGame.Phase.Ready, g.Current);
            for (int round = 0; round < SignalTuneGame.Rounds; round++)
            {
                Assert.Less(g.Match, 0.3f, $"신호 {round}: 처음부터 겹침");
                g.Adjust(5f, 5f);
                Assert.AreEqual(SignalTuneGame.Phase.Ready, g.Current, "준비 중엔 다이얼이 안 돎");
                g.Step(SignalTuneGame.ReadyTime + 0.01f);
                Assert.AreEqual(SignalTuneGame.Phase.Tune, g.Current);
                if (round % 2 == 0)
                {
                    g.Adjust(g.TargetFreq - g.Freq, g.TargetAmp - g.Amp);
                    Assert.AreEqual(1f, g.Match, 1e-4f);
                    Assert.Less(g.Jitter, 0.05f, "맞으면 흔들림이 작음");
                    Assert.IsTrue(g.Lock());
                    Assert.AreEqual(100, g.LastScore);
                }
                else
                {
                    g.Step(SignalTuneGame.RoundTime + 0.01f);   // 손대지 않으면 시간이 다 되어 저절로 맞춤 (낮은 점수)
                    Assert.AreEqual(SignalTuneGame.Phase.Locked, g.Current);
                    Assert.Less(g.LastScore, 30);
                }
                Assert.IsFalse(g.Lock(), "맞춘 뒤엔 다시 못 맞춤");
                g.Step(SignalTuneGame.LockedTime + 0.01f);
            }
            Assert.AreEqual(SignalTuneGame.Phase.Done, g.Current);
            Assert.GreaterOrEqual(g.Total, 300);
            Assert.LessOrEqual(g.Total, 390);
        }

        /// <summary>별똥별 잡기: 3 · 2 · 1 뒤 30초 동안 떨어짐 (20번 넘게 · 세 종류 다), 점수 +3 · +1 · −1 (0 밑으로 안 감), 별만 놓침으로 셈, 30초 뒤 끝.</summary>
        [Test]
        public void StarCatch_SpawnsAndScores()
        {
            var g = new StarCatchGame(7);
            g.Begin();
            Assert.IsNull(g.Step(1f), "세는 중엔 안 떨어짐");
            Assert.AreEqual(StarCatchGame.Phase.Countdown, g.Current);
            g.Step(StarCatchGame.CountdownTime);
            Assert.AreEqual(StarCatchGame.Phase.Play, g.Current);
            Assert.AreEqual(0, g.Catch(StarCatchGame.Kind.Junk), "0점에서 쓰레기 = 그대로 0");
            Assert.AreEqual(3, g.Catch(StarCatchGame.Kind.BigStar));
            Assert.AreEqual(1, g.Catch(StarCatchGame.Kind.SmallStar));
            Assert.AreEqual(-1, g.Catch(StarCatchGame.Kind.Junk));
            Assert.AreEqual(3, g.Score);
            g.Land(StarCatchGame.Kind.Junk);
            g.Land(StarCatchGame.Kind.SmallStar);
            Assert.AreEqual(1, g.Missed, "쓰레기는 놓침이 아님");
            int spawns = 0;
            var kinds = new System.Collections.Generic.HashSet<StarCatchGame.Kind>();
            for (float t = 0f; t < StarCatchGame.RoundTime + 1f; t += 0.05f)
            {
                var k = g.Step(0.05f);
                if (k.HasValue)
                {
                    spawns++;
                    kinds.Add(k.Value);
                }
            }
            Assert.AreEqual(StarCatchGame.Phase.Done, g.Current);
            Assert.Greater(spawns, 20, "30초에 떨어진 수");
            Assert.AreEqual(3, kinds.Count, "큰 별 · 작은 별 · 쓰레기");
            Assert.AreEqual(0, g.Catch(StarCatchGame.Kind.BigStar), "끝난 뒤엔 안 셈");
        }
    }
}
