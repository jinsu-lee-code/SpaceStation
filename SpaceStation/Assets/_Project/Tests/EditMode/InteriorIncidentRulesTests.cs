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
