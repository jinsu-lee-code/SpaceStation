using NUnit.Framework;
using SpaceStation.UI;

namespace SpaceStation.Tests
{
    /// <summary>휴대 패드의 자원 아이콘 키우기 (<see cref="HudText.BigIcons"/>, 2026-10-20).</summary>
    public class HudTextTests
    {
        [Test]
        public void BigIcons_WrapsEverySpriteOnly()
        {
            Assert.AreEqual("<size=150%><sprite name=metal></size> 20  <size=150%><sprite name=water></size> 5",
                HudText.BigIcons("<sprite name=metal> 20  <sprite name=water> 5"));
            Assert.AreEqual("<color=#FF5A5A><size=140%><sprite name=metal></size> 30</color>",
                HudText.BigIcons("<color=#FF5A5A><sprite name=metal> 30</color>", 140));
            Assert.AreEqual("무료", HudText.BigIcons("무료"));
            Assert.AreEqual("", HudText.BigIcons(""));
            Assert.IsNull(HudText.BigIcons(null));
            Assert.AreEqual("깨진 <sprite", HudText.BigIcons("깨진 <sprite")); // 닫는 > 없음 = 그대로
        }

        [Test]
        public void Amount_ShortensFromHundredThousand()
        {
            Assert.AreEqual("0", HudText.Amount(0f));
            Assert.AreEqual("350", HudText.Amount(349.6f));
            Assert.AreEqual("99999", HudText.Amount(99999f));
            Assert.AreEqual("100k", HudText.Amount(100000f));
            Assert.AreEqual("123k", HudText.Amount(123999f), "올림하면 실제보다 많아 보임");
            Assert.AreEqual("1234k", HudText.Amount(1234567f));
        }

        [Test]
        public void PlacementReason_ZoneReasonOnlyForOutsideZone()
        {
            var zone = new Zone { Reason = "파편 띠" };
            Assert.AreEqual("파편 띠", HudText.PlacementReason(Core.PlacementResult.OutsideZone, zone), "M-53 칸을 정하는 쪽 사유");
            Assert.AreEqual(HudText.PlacementReason(Core.PlacementResult.Occupied), HudText.PlacementReason(Core.PlacementResult.Occupied, zone));
            zone.Reason = "";
            Assert.AreEqual(HudText.PlacementReason(Core.PlacementResult.OutsideZone), HudText.PlacementReason(Core.PlacementResult.OutsideZone, zone), "사유가 비면 기본");
            Assert.AreEqual(HudText.PlacementReason(Core.PlacementResult.OutsideZone), HudText.PlacementReason(Core.PlacementResult.OutsideZone, null));
        }

        private sealed class Zone : Core.IBuildZone
        {
            public bool Allows(UnityEngine.Vector3Int cell) => false;
            public string Reason { get; set; }
        }
    }
}
