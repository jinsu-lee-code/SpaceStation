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
    }
}
