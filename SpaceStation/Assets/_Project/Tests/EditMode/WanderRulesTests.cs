using NUnit.Framework;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>11-16 ③ 동물 주민 방 안 걷기 규칙.</summary>
    public class WanderRulesTests
    {
        [Test]
        public void IdleSeconds_HappyShorter()
        {
            Assert.Less(WanderRules.IdleSeconds(1f, 0.5), WanderRules.IdleSeconds(-1f, 0.5));
            Assert.AreEqual(WanderRules.IdleHappy, WanderRules.IdleSeconds(1f, 0.5), 1e-4f);
        }

        [Test]
        public void ShouldWander_StopsAfterMaxExcursions()
        {
            Assert.IsTrue(WanderRules.ShouldWander(0f, 0, 0.1));
            Assert.IsFalse(WanderRules.ShouldWander(1f, WanderRules.MaxExcursions, 0.0), "나들이를 다 했으면 원래 자리로");
        }

        [Test]
        public void ShouldWander_MoodChangesChance()
        {
            // 같은 난수에서 기분 좋으면 나가고, 나쁘면 머묾
            Assert.IsTrue(WanderRules.ShouldWander(1f, 0, 0.6));
            Assert.IsFalse(WanderRules.ShouldWander(-1f, 0, 0.6));
        }

        [Test]
        public void WalkSpeed_HappyFaster()
        {
            Assert.Greater(WanderRules.WalkSpeed(1f), WanderRules.WalkSpeed(-1f));
            Assert.AreEqual(WanderRules.SpeedSad, WanderRules.WalkSpeed(-5f), 1e-4f);
        }
    }
}
