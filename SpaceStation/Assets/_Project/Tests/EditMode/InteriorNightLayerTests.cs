using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>닫힌 방 밤 층 규칙 (2026-11-26, SOUND_LIST_NEW 4-4 ③): 낮엔 그대로 · 밤엔 바탕이 낮아지고 가끔 소리가 드물어짐 · 기계 · 전기 방이 가장 조용 · 생활 · 물만 밤 루프.</summary>
    public class InteriorNightLayerTests
    {
        private static readonly InteriorAmbience[] All =
        {
            InteriorAmbience.Life, InteriorAmbience.Water, InteriorAmbience.Machine,
            InteriorAmbience.Heat, InteriorAmbience.Electric, InteriorAmbience.Hall,
        };

        [Test]
        public void Day_IsUnchanged_NightIsQuieterAndSparser()
        {
            foreach (var a in All)
            {
                var r = InteriorNightLayer.For(a);
                Assert.AreEqual(1f, InteriorNightLayer.Mix(1f, r.Bed, 0f), 1e-6f, a + " 낮 바탕");
                Assert.AreEqual(1f, InteriorNightLayer.Mix(1f, r.SpotRate, 0f), 1e-6f, a + " 낮 가끔 소리");
                Assert.That(r.Bed, Is.GreaterThan(0.2f).And.LessThan(1f), a + " 밤 바탕 (꺼지지는 않음)");
                Assert.That(r.SpotRate, Is.GreaterThan(0.2f).And.LessThan(1f), a + " 밤 가끔 소리는 드물게");
                Assert.AreEqual(r.Bed, InteriorNightLayer.Mix(1f, r.Bed, 1f), 1e-6f);
            }
        }

        [Test]
        public void MachineAndElectric_GetQuietest()
        {
            var machine = InteriorNightLayer.For(InteriorAmbience.Machine);
            var electric = InteriorNightLayer.For(InteriorAmbience.Electric);
            foreach (var a in new[] { InteriorAmbience.Heat, InteriorAmbience.Hall, InteriorAmbience.Water })
            {
                var r = InteriorNightLayer.For(a);
                Assert.Less(machine.Bed, r.Bed, "기계 방 바탕 < " + a);
                Assert.Less(electric.Bed, r.Bed, "전기 방 바탕 < " + a);
                Assert.Less(machine.SpotRate, r.SpotRate, "기계 방 가끔 소리 < " + a);
            }
        }

        [Test]
        public void OnlyLifeAndWater_HaveNightLoops()
        {
            Assert.AreEqual(InteriorNightLayer.LoopKind.Replace, InteriorNightLayer.For(InteriorAmbience.Life).Loop, "생활 = 불 끈 숙소로 바꿈");
            Assert.AreEqual(InteriorNightLayer.LoopKind.Overlay, InteriorNightLayer.For(InteriorAmbience.Water).Loop, "물 · 식물 = 밤 풀벌레 겹침");
            foreach (var a in new[] { InteriorAmbience.Machine, InteriorAmbience.Heat, InteriorAmbience.Electric, InteriorAmbience.Hall })
                Assert.AreEqual(InteriorNightLayer.LoopKind.None, InteriorNightLayer.For(a).Loop, a.ToString());
            Assert.Less(InteriorNightLayer.For(InteriorAmbience.Life).Bed, InteriorNightLayer.For(InteriorAmbience.Water).Bed, "바꾸는 쪽이 낮 바탕을 더 낮춤");
        }

        [Test]
        public void Step_TakesFiveSecondsEachWay()
        {
            float n = 0f;
            for (int i = 0; i < 49; i++)
                n = InteriorNightLayer.Step(n, false, 0.1f);
            Assert.That(n, Is.LessThan(1f).And.GreaterThan(0.95f), "4.9초 = 아직 넘어가는 중");
            n = InteriorNightLayer.Step(n, false, 0.2f);
            Assert.AreEqual(1f, n, 1e-6f);
            n = InteriorNightLayer.Step(n, true, 2.5f);
            Assert.AreEqual(0.5f, n, 1e-6f);
            Assert.AreEqual(0.5f, InteriorNightLayer.Step(n, true, -1f), 1e-6f, "음수 시간 = 그대로");
        }
    }
}
