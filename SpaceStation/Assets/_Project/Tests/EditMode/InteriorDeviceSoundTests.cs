using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>내부 장치 소리 규칙 (2026-11-26, SOUND_LIST_NEW 4-1 · 4-2 · 4-3): 금고문 · 신호판 잡음 · 레이더 · 우주복 · 드론 뚫음.</summary>
    public class InteriorDeviceSoundTests
    {
        [Test]
        public void Door_VaultOnlyOnShelterNormalDoor()
        {
            Assert.IsNull(InteriorDeviceSound.DoorFor(DoorKind.Open, 3, false));
            Assert.IsNull(InteriorDeviceSound.DoorFor(DoorKind.Open, 2, true), "트인 문간은 차폐실이어도 소리 없음");
            Assert.AreEqual(DoorSound.Airlock, InteriorDeviceSound.DoorFor(DoorKind.Airlock, 2, true), "진화 방호벽의 에어록은 에어록");
            Assert.AreEqual(DoorSound.Vault, InteriorDeviceSound.DoorFor(DoorKind.Normal, 0, true));
            Assert.AreEqual(DoorSound.Vault, InteriorDeviceSound.DoorFor(DoorKind.Normal, 1, true));
            Assert.AreEqual(DoorSound.Basic, InteriorDeviceSound.DoorFor(DoorKind.Normal, 0, false));
            Assert.AreEqual(DoorSound.Stage1, InteriorDeviceSound.DoorFor(DoorKind.Normal, 1, false));
        }

        [Test]
        public void SignalNoise_FollowsBoardState()
        {
            Assert.AreEqual(0f, InteriorDeviceSound.SignalNoise(false, true, false, false, 0f));
            Assert.AreEqual(0.3f, InteriorDeviceSound.SignalNoise(true, false, false, false, 0f), 1e-5f);
            Assert.AreEqual(1f, InteriorDeviceSound.SignalNoise(true, true, false, false, 0.5f), 1e-5f, "해독 못 함 = 거친 잡음");
            Assert.AreEqual(1f, InteriorDeviceSound.SignalNoise(true, true, false, true, 0f), 1e-5f);
            Assert.AreEqual(0.625f, InteriorDeviceSound.SignalNoise(true, true, false, true, 0.5f), 1e-5f);
            Assert.AreEqual(0.1f, InteriorDeviceSound.SignalNoise(true, true, true, true, 1f), 1e-5f);
        }

        [Test]
        public void Radar_LouderOnAlert()
        {
            Assert.AreEqual(0f, InteriorDeviceSound.RadarLevel(false, true));
            Assert.Less(InteriorDeviceSound.RadarLevel(true, false), InteriorDeviceSound.RadarLevel(true, true));
        }

        [Test]
        public void Suit_ChangesOnlyAfterGap()
        {
            Assert.IsTrue(InteriorDeviceSound.SuitOn(true, true, false, 10f, 0f), "실내 → 바깥 = 헬멧");
            Assert.IsFalse(InteriorDeviceSound.SuitOn(true, true, false, 10.5f, 10f), "막 바뀐 뒤 1.5초 안 = 그대로");
            Assert.IsTrue(InteriorDeviceSound.SuitOn(true, false, true, 11f, 10f), "경계를 오가도 1.5초 안에는 그대로");
            Assert.IsFalse(InteriorDeviceSound.SuitOn(true, false, true, 12f, 10f));
            Assert.IsFalse(InteriorDeviceSound.SuitOn(false, true, true, 10.1f, 10f), "개방형 아닌 방으로 = 바로 벗음");
        }

        [Test]
        public void Breach_OnlyNewTarget()
        {
            var a = new object();
            var b = new object();
            Assert.IsFalse(InteriorDeviceSound.BreachIsNew(a, a), "들어오기 전부터 있던 습격 · 다시 짓기");
            Assert.IsFalse(InteriorDeviceSound.BreachIsNew(a, null));
            Assert.IsTrue(InteriorDeviceSound.BreachIsNew(null, b));
            Assert.IsTrue(InteriorDeviceSound.BreachIsNew(a, b));
        }
    }
}
