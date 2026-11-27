using NUnit.Framework;
using SpaceStation.Interior;
using SpaceStation.Simulation;

namespace SpaceStation.Tests
{
    /// <summary>11-17 ④ 내부 사고 규칙.</summary>
    public class InteriorIncidentRulesTests
    {
        [Test]
        public void Pick_NoRepeat_BreakerOnlyWhenProducing()
        {
            for (int i = 0; i < 20; i++)
            {
                var k = InteriorIncidentRules.Pick(false, IncidentKind.Leak, i / 20.0);
                Assert.AreNotEqual(IncidentKind.Leak, k, "직전과 같은 종류 안 나옴");
                Assert.AreNotEqual(IncidentKind.Breaker, k, "생산 없는 방은 차단기 없음");
            }
            bool breaker = false;
            for (int i = 0; i < 20; i++)
                breaker |= InteriorIncidentRules.Pick(true, null, i / 20.0) == IncidentKind.Breaker;
            Assert.IsTrue(breaker);
        }

        [Test]
        public void StageCatalog_KindsPerStage_PickStaysInStage()
        {
            // CONNECT A6 단계별 상황 표 (사용자 결정 2026-11-27)
            Assert.AreEqual(4, IncidentCatalog.KindsForStage(0).Count, "기본 = 지금 넷");
            Assert.AreEqual(3, IncidentCatalog.KindsForStage(1).Count);
            Assert.AreEqual(3, IncidentCatalog.KindsForStage(2).Count);
            Assert.AreEqual(3, IncidentCatalog.KindsForStage(3).Count);
            Assert.AreEqual(2, IncidentCatalog.KindsForStage(4).Count);
            Assert.AreEqual(2, IncidentCatalog.StageOf(IncidentKind.RideStop));
            Assert.AreEqual(0, IncidentCatalog.StageOf(IncidentKind.Filter));
            Assert.AreEqual(IncidentLoss.Drain, IncidentCatalog.LossOf(IncidentKind.WaterChannel));
            Assert.AreEqual(IncidentLoss.Mood, IncidentCatalog.LossOf(IncidentKind.Lanterns));
            Assert.AreEqual(IncidentLoss.Air, IncidentCatalog.LossOf(IncidentKind.Filter), "지금 넷은 손실 그대로");

            var stage3 = IncidentCatalog.KindsForStage(3);
            for (int i = 0; i < 20; i++)
            {
                var k = InteriorIncidentRules.Pick(stage3, true, IncidentKind.Lanterns, i / 20.0);
                Assert.IsTrue(k.HasValue);
                Assert.AreEqual(3, IncidentCatalog.StageOf(k.Value), "그 단계 상황만");
                Assert.AreNotEqual(IncidentKind.Lanterns, k.Value, "직전과 같은 종류 안 나옴");
            }
            Assert.IsNull(InteriorIncidentRules.Pick(new IncidentKind[0], true, null, 0.5), "고를 것이 없으면 상황 없음 (트인 방)");
            Assert.AreEqual(IncidentKind.Lanterns, InteriorIncidentRules.Pick(new[] { IncidentKind.Lanterns }, true, IncidentKind.Lanterns, 0.5), "하나뿐이면 직전과 같아도");
        }

        [Test]
        public void Breaker_LitOnly_WrongResets_AllDone()
        {
            var lit = InteriorIncidentRules.TripPattern(new System.Random(3));
            int count = 0;
            foreach (bool l in lit)
                count += l ? 1 : 0;
            Assert.That(count, Is.InRange(2, 3));

            int off = System.Array.IndexOf(lit, false);
            Assert.AreEqual(SwitchPress.Wrong, InteriorIncidentRules.Press(lit, off), "멀쩡한 스위치 = 틀림");
            SwitchPress last = SwitchPress.Wrong;
            for (int i = 0; i < lit.Length; i++)
            {
                if (lit[i])
                    last = InteriorIncidentRules.Press(lit, i);
            }
            Assert.AreEqual(SwitchPress.AllDone, last);
            Assert.AreEqual(SwitchPress.Wrong, InteriorIncidentRules.Press(lit, 0), "다 올린 뒤에는 누를 게 없음");
        }

        [Test]
        public void Notches_StartDiffersFromTarget_TurnWraps_SolvedWhenAllMatch()
        {
            // CONNECT A6 3단계 ① 물꼬 판 · 4단계 ① 신호 다이얼 (사용자 결정 2026-11-27)
            var rng = new System.Random(7);
            for (int round = 0; round < 50; round++)
            {
                var target = InteriorIncidentRules.NotchTargets(rng, InteriorIncidentRules.SluiceWheels, out var start);
                for (int i = 0; i < target.Length; i++)
                {
                    Assert.That(target[i], Is.InRange(1, InteriorIncidentRules.Notches));
                    Assert.That(start[i], Is.InRange(1, InteriorIncidentRules.Notches));
                    Assert.AreNotEqual(target[i], start[i], "처음부터 맞아 있으면 할 일이 없음");
                }
                Assert.IsFalse(InteriorIncidentRules.NotchesSolved(start, target));
                // 바퀴마다 목표까지 돌리면 해결 (5칸 안에)
                var levels = (int[])start.Clone();
                for (int i = 0; i < levels.Length; i++)
                {
                    int turns = 0;
                    while (levels[i] != target[i])
                    {
                        levels[i] = InteriorIncidentRules.TurnNotch(levels[i]);
                        turns++;
                    }
                    Assert.That(turns, Is.InRange(1, InteriorIncidentRules.Notches - 1));
                }
                Assert.IsTrue(InteriorIncidentRules.NotchesSolved(levels, target));
            }
            Assert.AreEqual(1, InteriorIncidentRules.TurnNotch(InteriorIncidentRules.Notches), "마지막 칸 다음은 첫 칸");
        }

        [Test]
        public void LampOrder_NoImmediateRepeat_WrongRestarts()
        {
            // CONNECT A6 3단계 ② 등불 순서 · 4단계 ② 별빛 등불
            var rng = new System.Random(3);
            for (int round = 0; round < 50; round++)
            {
                var order = InteriorIncidentRules.LampOrder(rng);
                Assert.That(order.Length, Is.InRange(InteriorIncidentRules.LampOrderMin, InteriorIncidentRules.LampOrderMax));
                for (int i = 1; i < order.Length; i++)
                    Assert.AreNotEqual(order[i - 1], order[i], "같은 등불이 바로 이어 나오지 않음");
            }
            var o = new[] { 2, 0, 3 };
            int entered = 0;
            Assert.AreEqual(SwitchPress.Fixed, InteriorIncidentRules.PressLamp(o, ref entered, 2));
            Assert.AreEqual(SwitchPress.Wrong, InteriorIncidentRules.PressLamp(o, ref entered, 1));
            Assert.AreEqual(0, entered, "틀리면 처음부터");
            InteriorIncidentRules.PressLamp(o, ref entered, 2);
            InteriorIncidentRules.PressLamp(o, ref entered, 0);
            Assert.AreEqual(SwitchPress.AllDone, InteriorIncidentRules.PressLamp(o, ref entered, 3));
        }
    }
}
