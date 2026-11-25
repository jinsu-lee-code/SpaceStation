using NUnit.Framework;
using SpaceStation.Audio;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>운석 충돌 소리 갈래 (2026-11-25, SOUND_LIST_NEW 1-1 · D-6 · D-2): 맞은 곳 · 크기 · 균열 덧댐 · 화면 자리 크기.</summary>
    public class MeteorSoundTests
    {
        [Test]
        public void Surface_ByHitArmorAndDamage()
        {
            Assert.AreEqual(MeteorSurface.Shield, MeteorSound.Surface(false, false, false));   // 튕긴 뒤 다른 실드에 막힘
            Assert.AreEqual(MeteorSurface.Hull, MeteorSound.Surface(true, false, true));      // 보통 모듈 파손
            Assert.AreEqual(MeteorSurface.Armor, MeteorSound.Surface(true, true, true));      // 장갑 격벽 파손
            Assert.AreEqual(MeteorSurface.Armor, MeteorSound.Surface(true, false, false));    // 방어 연구로 파손 면함
            Assert.AreEqual(MeteorSurface.Armor, MeteorSound.Surface(true, true, false));
        }

        [Test]
        public void Large_AtMostOncePerGap()
        {
            float last = -99f;
            // 한 사건 열 발이 0.22초 간격 (MeteorFx 몰림 간격) → 0 · 1.1 · 2.2초만 큰 충돌
            int large = 0;
            for (int i = 0; i < 11; i++)
            {
                float now = 10f + i * 0.22f;
                if (MeteorSound.IsLarge(now, last))
                {
                    last = now;
                    large++;
                }
            }
            Assert.AreEqual(3, large);
            Assert.IsTrue(MeteorSound.IsLarge(5f, 4f));
            Assert.IsFalse(MeteorSound.IsLarge(5f, 4.5f));
            Assert.IsTrue(MeteorSound.IsLarge(1f, 300f), "플레이를 다시 켜 시계가 처음부터");
        }

        [Test]
        public void Crack_OnlyOnDamagedArmor()
        {
            Assert.IsTrue(MeteorSound.CrackOnImpact(MeteorSurface.Armor, true));
            Assert.IsFalse(MeteorSound.CrackOnImpact(MeteorSurface.Armor, false));
            Assert.IsFalse(MeteorSound.CrackOnImpact(MeteorSurface.Hull, true), "선체 충돌 소리에 찢김이 이미 있음");
            Assert.IsFalse(MeteorSound.CrackOnImpact(MeteorSurface.Shield, false));
        }

        [Test]
        public void CenterGain_FadesTowardEdge()
        {
            Assert.AreEqual(1f, MeteorSound.CenterGain(new Vector3(0.5f, 0.5f, 10f)), 1e-4f);
            Assert.AreEqual(MeteorSound.EdgeGain, MeteorSound.CenterGain(new Vector3(1f, 0.5f, 10f)), 1e-4f);
            Assert.AreEqual(MeteorSound.EdgeGain, MeteorSound.CenterGain(new Vector3(1.6f, -0.4f, 10f)), 1e-4f);
            Assert.AreEqual(MeteorSound.EdgeGain, MeteorSound.CenterGain(new Vector3(0.5f, 0.5f, -3f)), 1e-4f);
            Assert.AreEqual(0.8f, MeteorSound.CenterGain(new Vector3(0.75f, 0.6f, 10f)), 1e-4f);
        }

        [Test]
        public void ImpactCue_FallsBackToOldCue()
        {
            var lib = ScriptableObject.CreateInstance<SoundLibrary>();
            Assert.AreSame(lib.MeteorImpact, MeteorSound.ImpactCue(lib, MeteorSurface.Hull, true), "클립 없음 → 옛 한 칸");
            lib.MeteorImpactArmorS.Clips = new[] { AudioClip.Create("t", 10, 1, 48000, false) };
            Assert.AreSame(lib.MeteorImpactArmorS, MeteorSound.ImpactCue(lib, MeteorSurface.Armor, false));
            Assert.AreSame(lib.MeteorImpact, MeteorSound.ImpactCue(lib, MeteorSurface.Armor, true));
            Object.DestroyImmediate(lib);
        }
    }
}
