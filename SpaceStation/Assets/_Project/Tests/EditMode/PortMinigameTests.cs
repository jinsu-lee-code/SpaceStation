using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>궤도 항만 미니게임 둘 판 규칙 (사용자 2026-11-16 "미니게임 진행해줘"): 진주 조개 잡기 · 인어 분수 진주 던지기.</summary>
    public class PortMinigameTests
    {
        private const float Dt = 1f / 30f;

        /// <summary>판 흐름: 쉬는 동안 열림 → 3 · 2 · 1 (뚜껑 닫힘, 못 잡음) → 30초 → 끝. 15초 전엔 하나씩, 뒤엔 둘까지만 열림, 열린 시간은 0.9초 넘게.</summary>
        [Test]
        public void PearlClams_RoundRhythm()
        {
            var g = new PearlClamGame(7);
            for (int k = 0; k < PearlClamGame.ClamCount; k++)
                Assert.AreEqual(1f, g.Lid(k), "쉬는 조개는 열려 진주를 보임");
            g.Start();
            Assert.AreEqual(PearlClamGame.Catch.Miss, g.TryCatch(0), "3 · 2 · 1 동안 잡힘");
            float t = 0f;
            int earlyMax = 0, lateMax = 0;
            var openFor = new float[PearlClamGame.ClamCount];
            float shortest = float.MaxValue;
            while (g.Current != PearlClamGame.Phase.Done && t < 60f)
            {
                g.Tick(Dt);
                t += Dt;
                if (g.Current == PearlClamGame.Phase.Countdown && g.PhaseTime > PearlClamGame.LidTime + 0.05f)
                    for (int k = 0; k < PearlClamGame.ClamCount; k++)
                        Assert.AreEqual(0f, g.Lid(k), 1e-4f, "3 · 2 · 1 동안 뚜껑이 안 닫힘");
                if (!g.Playing)
                    continue;
                int open = 0;
                for (int k = 0; k < PearlClamGame.ClamCount; k++)
                {
                    if (g.IsOpen(k))
                    {
                        open++;
                        openFor[k] += Dt;
                    }
                    else if (openFor[k] > 0f)
                    {
                        shortest = Mathf.Min(shortest, openFor[k]);
                        openFor[k] = 0f;
                    }
                }
                if (g.PhaseTime < PearlClamGame.TwoOpenAfter)
                    earlyMax = Mathf.Max(earlyMax, open);
                else
                    lateMax = Mathf.Max(lateMax, open);
            }
            Assert.AreEqual(PearlClamGame.Phase.Done, g.Current);
            Assert.AreEqual(PearlClamGame.CountdownTime + PearlClamGame.RoundTime, t, 0.1f, "판 길이");
            Assert.AreEqual(1, earlyMax, "15초 전엔 하나씩");
            Assert.AreEqual(2, lateMax, "15초 뒤엔 둘까지");
            Assert.GreaterOrEqual(shortest, PearlClamGame.OpenEnd - 0.05f, "열린 시간이 너무 짧음");
            Assert.Greater(g.Opened, 14, "30초에 열린 조개가 적음");
            for (int i = 0; i < 60; i++)
                g.Tick(Dt);
            for (int k = 0; k < PearlClamGame.ClamCount; k++)
                Assert.AreEqual(1f, g.Lid(k), 1e-4f, "끝난 뒤 다시 열림");
        }

        /// <summary>점수: 반 넘게 열린 조개를 잡는 봇 — 진주 +1 · 금빛 +3 · 게 −1 (0 밑으로 안 감), 같은 조개 두 번 · 닫힌 조개 · 막 열린 틈은 Miss.</summary>
        [Test]
        public void PearlClams_CatchScores()
        {
            int pearls = 0, golds = 0, crabs = 0, gaps = 0;
            for (int seed = 1; seed <= 4; seed++)
            {
                var g = new PearlClamGame(seed);
                g.Start();
                int expect = 0;
                while (g.Current != PearlClamGame.Phase.Done)
                {
                    g.Tick(Dt);
                    for (int k = 0; k < PearlClamGame.ClamCount; k++)
                    {
                        if (!g.IsOpen(k))
                        {
                            if (g.Playing && !g.Taken(k))
                                Assert.AreEqual(PearlClamGame.Catch.Miss, g.TryCatch(k), "닫힌 조개가 잡힘");
                            continue;
                        }
                        if (g.Lid(k) < PearlClamGame.CatchLid)
                        {
                            Assert.AreEqual(PearlClamGame.Catch.Miss, g.TryCatch(k), "막 열린 틈이 잡힘");
                            gaps++;
                            continue;
                        }
                        var content = g.ContentOf(k);
                        var got = g.TryCatch(k);
                        switch (content)
                        {
                            case PearlClamGame.Content.Gold:
                                Assert.AreEqual(PearlClamGame.Catch.Gold, got);
                                expect += PearlClamGame.GoldPoints;
                                golds++;
                                break;
                            case PearlClamGame.Content.Crab:
                                Assert.AreEqual(PearlClamGame.Catch.Crab, got);
                                expect = Mathf.Max(0, expect + PearlClamGame.CrabPoints);
                                crabs++;
                                break;
                            default:
                                Assert.AreEqual(PearlClamGame.Catch.Pearl, got);
                                expect += PearlClamGame.PearlPoints;
                                pearls++;
                                break;
                        }
                        Assert.AreEqual(PearlClamGame.Catch.Miss, g.TryCatch(k), "한 번 연 조개를 두 번 잡음");
                        Assert.AreEqual(expect, g.Score);
                        Assert.GreaterOrEqual(g.Score, 0);
                    }
                }
            }
            Assert.Greater(pearls, 40, "진주가 적음");
            Assert.Greater(golds, 0, "금빛 진주가 안 나옴");
            Assert.Greater(crabs, 0, "게가 안 나옴");
            Assert.Greater(gaps, 0);
        }

        /// <summary>
        /// 분수: 떨어질 때의 고리 자리를 노리면 그 고리 점수 (겹치면 높은 쪽), 가운데 (꼬리 둘레 물) = 0점, 테 밖 = 빗나감, 쉬는 틈 안엔 못 던짐,
        /// 일곱 개가 다 떨어지면 끝 · 점수 = 떨어진 자리 점수 합.
        /// </summary>
        [Test]
        public void FountainToss_ScoresWhereItLands()
        {
            var c = new Vector2(9.8f, -0.5f);
            var g = new FountainToss(c, 1.86f);
            Assert.IsFalse(g.Throw(c), "시작 전에 던져짐");
            g.Start();
            var aims = new List<Vector2>();
            int expect = 0;
            void Wait()
            {
                while (!g.CanThrow)
                    g.Tick(Dt);
            }
            for (int i = 0; i < FountainToss.Rings.Length; i++)
            {
                Wait();
                var at = g.RingCenter(i, g.Time + FountainToss.FlightTime);
                var judge = g.Judge(at, g.Time + FountainToss.FlightTime);
                Assert.GreaterOrEqual(judge.Points, FountainToss.Rings[i].Points, $"고리 {i} 가운데를 노렸는데 점수가 낮음");
                expect += judge.Points;
                Assert.IsTrue(g.Throw(at));
                Assert.IsFalse(g.Throw(at), "쉬는 틈 안에 또 던져짐");
            }
            Wait();
            Assert.AreEqual(FountainToss.Water, g.Judge(c + new Vector2(0.2f, 0f), g.Time + FountainToss.FlightTime).Ring, "가운데 물이 고리로 셈");
            Assert.IsTrue(g.Throw(c + new Vector2(0.2f, 0f)));
            Wait();
            Assert.IsTrue(g.Throw(c + new Vector2(2.6f, 0f)));
            while (g.PearlsLeft > 0)
            {
                Wait();
                Assert.IsTrue(g.Throw(c + new Vector2(0f, 0.2f)));
            }
            int landed = 0, outside = 0;
            for (int i = 0; i < 200 && g.Current == FountainToss.Phase.Play; i++)
            {
                g.Tick(Dt);
                while (g.PopLanding(out var l))
                {
                    landed++;
                    if (l.Ring == FountainToss.Outside)
                        outside++;
                }
            }
            while (g.PopLanding(out var l))
            {
                landed++;
                if (l.Ring == FountainToss.Outside)
                    outside++;
            }
            Assert.AreEqual(FountainToss.Phase.Done, g.Current);
            Assert.AreEqual(FountainToss.PearlCount, landed, "떨어진 진주 수");
            Assert.AreEqual(1, outside, "테 밖 하나");
            Assert.AreEqual(expect, g.Score, "점수 = 고리 셋");
            Assert.AreEqual(FountainToss.Rings.Length, g.Hits);
        }
    }
}
