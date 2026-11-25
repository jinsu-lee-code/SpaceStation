using NUnit.Framework;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>방 장치 소리 · 바깥 일이 안에서 규칙 (2026-11-26, SOUND_LIST_NEW 4-4 · D-8 · D-10 · D-4): 궤도 부품 이름 · 움직임 크기 · 식물 스침 · 유성우 몰아침 · 바깥 줌.</summary>
    public class InteriorObjectSoundTests
    {
        [Test]
        public void OrbitPart_ByTemplatePartName()
        {
            Assert.AreEqual(InteriorObjectSound.OrbitSound.Drone, InteriorObjectSound.ForOrbitPart("INT_EvoDroneDepot_FlyA"));
            Assert.AreEqual(InteriorObjectSound.OrbitSound.Drone, InteriorObjectSound.ForOrbitPart("INT_EvoSecurityHQ_PadDrone2"));
            Assert.AreEqual(InteriorObjectSound.OrbitSound.Drone, InteriorObjectSound.ForOrbitPart("INT_EvoSecurityHQ_Orbit1"), "광장 순찰 드론 고리");
            Assert.AreEqual(InteriorObjectSound.OrbitSound.Drone, InteriorObjectSound.ForOrbitPart("INT_EvoEmergency_DroneB"));
            Assert.AreEqual(InteriorObjectSound.OrbitSound.None, InteriorObjectSound.ForOrbitPart("INT_EvoDroneDepot_SwarmA"), "드론 모빌 = 매달린 장식");
            Assert.AreEqual(InteriorObjectSound.OrbitSound.None, InteriorObjectSound.ForOrbitPart("INT_EvoShipyard_Drone"), "받침대 위 전시 드론");
            Assert.AreEqual(InteriorObjectSound.OrbitSound.Hologram, InteriorObjectSound.ForOrbitPart("INT_EvoShipyard_Holo"));
            Assert.AreEqual(InteriorObjectSound.OrbitSound.Hologram, InteriorObjectSound.ForOrbitPart("INT_EvoFusionPlant_Plasma"));
            Assert.AreEqual(InteriorObjectSound.OrbitSound.Hologram, InteriorObjectSound.ForOrbitPart("INT_EvoAutomation_RingA"));
            Assert.AreEqual(InteriorObjectSound.OrbitSound.None, InteriorObjectSound.ForOrbitPart("INT_EvoGridHub_Flow"), "번개 정원 체험 소리가 따로");
            Assert.AreEqual(InteriorObjectSound.OrbitSound.Roller, InteriorObjectSound.ForOrbitPart("INT_EvoShipyard_Roll0"));
            Assert.AreEqual(InteriorObjectSound.OrbitSound.None, InteriorObjectSound.ForOrbitPart("INT_EvoShipyard_Roll2"), "롤러 셋 = 소리 하나");
            Assert.AreEqual(InteriorObjectSound.OrbitSound.Carousel, InteriorObjectSound.ForOrbitPart("INT_EvoStorage_Carousel0"));
            Assert.AreEqual(InteriorObjectSound.OrbitSound.None, InteriorObjectSound.ForOrbitPart("INT_EvoStorage_Carousel1"));
            Assert.AreEqual(InteriorObjectSound.OrbitSound.None, InteriorObjectSound.ForOrbitPart("INT_EvoBiodome_Fish"));
            Assert.AreEqual(InteriorObjectSound.OrbitSound.None, InteriorObjectSound.ForOrbitPart("INT_EvoShieldDome_Sweep"), "쓸기 빛 = 레이더 핑이 따로");
            Assert.AreEqual(InteriorObjectSound.OrbitSound.None, InteriorObjectSound.ForOrbitPart(null));
        }

        [Test]
        public void MotionLevel_SilentWhenBarelyMoving()
        {
            Assert.AreEqual(0f, InteriorObjectSound.MotionLevel(0f, 0f, 0.6f, 30f));
            Assert.AreEqual(0f, InteriorObjectSound.MotionLevel(0f, 0.6f, 0f, 15f), "해를 따라 아주 천천히 = 무음");
            Assert.AreEqual(0.5f, InteriorObjectSound.MotionLevel(0.3f, 0f, 0.6f, 30f), 1e-5f);
            Assert.AreEqual(1f, InteriorObjectSound.MotionLevel(0.1f, 45f, 0.6f, 30f), 1e-5f, "도는 쪽이 더 빠르면 그쪽");
            Assert.AreEqual(1f, InteriorObjectSound.MotionLevel(3f, 0f, 0.6f, 30f), 1e-5f, "1에서 멈춤");
        }

        [Test]
        public void Brushes_OnlyWalkingPastAndNotTwice()
        {
            Assert.IsTrue(InteriorObjectSound.Brushes(0.5f, 1.4f, 9f));
            Assert.IsFalse(InteriorObjectSound.Brushes(0.5f, 0.1f, 9f), "서 있으면 X");
            Assert.IsFalse(InteriorObjectSound.Brushes(1.5f, 1.4f, 9f), "멀면 X");
            Assert.IsFalse(InteriorObjectSound.Brushes(0.5f, 1.4f, 0.4f), "막 스친 식물은 X");
        }

        [Test]
        public void Barrage_OneRumbleForAShower()
        {
            Assert.IsFalse(InteriorObjectSound.Barrage(2, 99f));
            Assert.IsTrue(InteriorObjectSound.Barrage(3, 99f));
            Assert.IsFalse(InteriorObjectSound.Barrage(6, 1f), "몰아치는 동안 다시 울리지 않음");
        }

        [Test]
        public void ZoomLevel_OnlyWhenCameraIsClose()
        {
            Assert.AreEqual(1f, InteriorObjectSound.ZoomLevel(2.5f), 1e-5f);
            Assert.AreEqual(0.5f, InteriorObjectSound.ZoomLevel(5f), 1e-5f);
            Assert.AreEqual(0f, InteriorObjectSound.ZoomLevel(12f), "기본 줌 (12 m) = 무음");
        }
    }
}
