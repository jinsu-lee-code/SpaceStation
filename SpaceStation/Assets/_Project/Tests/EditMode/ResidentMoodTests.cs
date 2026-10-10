using NUnit.Framework;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>11-16 동물 주민 기분 → 평소 동작 · 반응.</summary>
    public class ResidentMoodTests
    {
        [Test]
        public void Of_CombinesPersonalAndStation_Clamped()
        {
            Assert.AreEqual(0f, ResidentMood.Of(0f, ResidentMood.SatisfactionNeutral, false), 1e-4f);
            Assert.AreEqual(1f, ResidentMood.Of(10f, 100f, false), 1e-4f);
            Assert.AreEqual(-1f, ResidentMood.Of(-10f, 0f, true), 1e-4f);
            // 본인 보정만 가득 좋음 → 절반
            Assert.AreEqual(0.5f, ResidentMood.Of(ResidentMood.PersonalFull, ResidentMood.SatisfactionNeutral, false), 1e-4f);
        }

        [Test]
        public void Of_HomelessLowersMood()
        {
            float home = ResidentMood.Of(0f, 70f, false);
            float homeless = ResidentMood.Of(0f, 70f, true);
            Assert.Less(homeless, home);
        }

        [Test]
        public void Style_SadVsHappy()
        {
            var sad = ResidentMood.Style(-1f);
            var happy = ResidentMood.Style(1f);
            Assert.Greater(sad.HeadDown, happy.HeadDown, "기분 나쁨 = 고개 숙임");
            Assert.Greater(sad.EarDroop, happy.EarDroop, "기분 나쁨 = 귀 처짐");
            Assert.Greater(happy.TailWag, sad.TailWag, "기분 좋음 = 꼬리 크게");
            Assert.Greater(happy.Speed, sad.Speed);
            Assert.Greater(happy.Bounce, sad.Bounce);
            var mid = ResidentMood.Style(0f);
            Assert.AreEqual(ResidentMood.Neutral.TailWag, mid.TailWag, 1e-4f);
        }

        [Test]
        public void Reaction_ByMood()
        {
            Assert.AreEqual(ResidentReaction.HopWave, ResidentMood.Reaction(0.8f, 1, 0));
            Assert.AreEqual(ResidentReaction.TurnAway, ResidentMood.Reaction(-0.8f, 1, 0));
            var a = ResidentMood.Reaction(0f, 3, 0);
            var b = ResidentMood.Reaction(0f, 3, 1);
            Assert.AreNotEqual(a, b, "보통 기분은 만날 때마다 손 흔들기 · 바라보기를 번갈아");
            Assert.That(a == ResidentReaction.Wave || a == ResidentReaction.Look);
        }
    }
}
