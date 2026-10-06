using NUnit.Framework;
using SpaceStation.Interior;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>11-10 방 상태 → 조명 연출 규칙.</summary>
    public class InteriorMoodTests
    {
        private readonly InteriorMoodTuning _t = new InteriorMoodTuning();

        [Test]
        public void Gauge_LitCount_SegmentLitWhenAnyChargeInIt()
        {
            Assert.AreEqual(0, InteriorGauge.LitCount(0f, 12));
            Assert.AreEqual(1, InteriorGauge.LitCount(0.01f, 12));
            Assert.AreEqual(6, InteriorGauge.LitCount(0.5f, 12));
            Assert.AreEqual(11, InteriorGauge.LitCount(0.9f, 12));
            Assert.AreEqual(11, InteriorGauge.LitCount(11f / 12f, 12));
            Assert.AreEqual(12, InteriorGauge.LitCount(0.95f, 12));
            Assert.AreEqual(12, InteriorGauge.LitCount(1f, 12));
            Assert.AreEqual(12, InteriorGauge.LitCount(1.5f, 12));
            Assert.AreEqual(0, InteriorGauge.LitCount(0.5f, 0));
        }

        [Test]
        public void Normal_FullBrightness_NoEffects()
        {
            var m = InteriorMoodRules.Evaluate(RoomCondition.Normal, _t);
            Assert.AreEqual(RoomLightMode.Normal, m.Mode);
            Assert.AreEqual(1f, m.Brightness, 1e-5f);
            Assert.AreEqual(Color.white, m.Tint);
            Assert.AreEqual(0f, m.FlickerRate);
            Assert.AreEqual(0f, m.StutterRate);
            Assert.AreEqual(0f, m.Jitter);
        }

        [Test]
        public void PowerShortage_DimmerAndFlickersMoreAsEfficiencyDrops()
        {
            var c = RoomCondition.Normal;
            c.Power = 0.8f;
            var mild = InteriorMoodRules.Evaluate(c, _t);
            c.Power = _t.PowerFloor;
            var worst = InteriorMoodRules.Evaluate(c, _t);

            Assert.Less(mild.Brightness, 1f);
            Assert.Greater(mild.FlickerRate, 0f);
            Assert.Less(worst.Brightness, mild.Brightness);
            Assert.Greater(worst.FlickerRate, mild.FlickerRate);
            Assert.AreEqual(_t.PowerMinBrightness, worst.Brightness, 1e-5f);
            Assert.AreEqual(_t.PowerMaxFlickerRate, worst.FlickerRate, 1e-5f);
        }

        [Test]
        public void Wear_WarmerDimmer_StuttersOnlyWhenVeryLow()
        {
            var c = RoomCondition.Normal;
            c.Wear = 0.6f;
            var worn = InteriorMoodRules.Evaluate(c, _t);
            Assert.Less(worn.Brightness, 1f);
            Assert.Less(worn.Tint.b, 1f);   // 누런 쪽
            Assert.AreEqual(0f, worn.StutterRate);

            c.Wear = _t.WearStutterBelow - 0.05f;
            var bad = InteriorMoodRules.Evaluate(c, _t);
            Assert.Less(bad.Brightness, worn.Brightness);
            Assert.Greater(bad.StutterRate, 0f);
            Assert.AreEqual(0f, bad.FlickerRate); // 전력은 정상
        }

        [Test]
        public void Damaged_Alarm_RedAndDim_KeepsPowerFlicker()
        {
            var c = RoomCondition.Normal;
            c.Damaged = true;
            c.Power = 0.5f;
            var m = InteriorMoodRules.Evaluate(c, _t);
            Assert.AreEqual(RoomLightMode.Alarm, m.Mode);
            Assert.AreEqual(_t.AlarmTint, m.Tint);
            Assert.Less(m.Brightness, _t.AlarmBrightness + 1e-5f);
            Assert.Greater(m.FlickerRate, 0f);
        }

        [Test]
        public void Inactive_Emergency_LightsOff_EvenIfDamaged()
        {
            var c = RoomCondition.Normal;
            c.Active = false;
            c.Damaged = true;
            var m = InteriorMoodRules.Evaluate(c, _t);
            Assert.AreEqual(RoomLightMode.Emergency, m.Mode);
            Assert.AreEqual(0f, m.Brightness);
        }

        [Test]
        public void Storm_AddsJitter()
        {
            var c = RoomCondition.Normal;
            c.Storm = true;
            var m = InteriorMoodRules.Evaluate(c, _t);
            Assert.AreEqual(_t.StormJitter, m.Jitter, 1e-5f);
            Assert.AreEqual(1f, m.Brightness, 1e-5f);
        }
    }
}
