using NUnit.Framework;
using SpaceStation.Audio;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>습격 포격 · 실드 · 포탑 단계 소리 (2026-11-26, SOUND_LIST_NEW 2번 B — 작업자 P4): 포격탄 가리기 · 단계 · 클립이 없을 때 아래 단계로.</summary>
    public class DefenseSoundTests
    {
        private static AudioClip Clip() => AudioClip.Create("t", 10, 1, 48000, false);

        [Test]
        public void RaidShell_OnlyBombardLabel()
        {
            Assert.IsTrue(DefenseSound.IsRaidShell(true, "포격"));
            Assert.IsFalse(DefenseSound.IsRaidShell(false, "포격"));       // 평소 유성우
            Assert.IsFalse(DefenseSound.IsRaidShell(true, "큰 유성우"));   // 서사판 특별 유성우 = 운석 소리
            Assert.IsFalse(DefenseSound.IsRaidShell(true, null));
        }

        [Test]
        public void Tier_ClampsRecipeStage()
        {
            Assert.AreEqual(0, DefenseSound.Tier(0));
            Assert.AreEqual(0, DefenseSound.Tier(-1));
            Assert.AreEqual(2, DefenseSound.Tier(2));
            Assert.AreEqual(3, DefenseSound.Tier(4));   // 4단계 이상 = 3단계 소리
        }

        [Test]
        public void ShieldCue_ByTierWithFallback()
        {
            var lib = ScriptableObject.CreateInstance<SoundLibrary>();
            Assert.AreSame(lib.ShieldDeflect, DefenseSound.ShieldCue(lib, 3));   // 단계 칸에 클립이 없으면 기본
            lib.ShieldDeflectHex.Clips = new[] { Clip() };
            lib.ShieldDeflectCrystal.Clips = new[] { Clip() };
            Assert.AreSame(lib.ShieldDeflect, DefenseSound.ShieldCue(lib, 0));
            Assert.AreSame(lib.ShieldDeflectHex, DefenseSound.ShieldCue(lib, 1));
            Assert.AreSame(lib.ShieldDeflectHex, DefenseSound.ShieldCue(lib, 2));     // 오로라 칸이 비면 육각으로
            Assert.AreSame(lib.ShieldDeflectCrystal, DefenseSound.ShieldCue(lib, 3));
            Assert.AreSame(lib.ShieldDeflectCrystal, DefenseSound.ShieldCue(lib, 4));
            Object.DestroyImmediate(lib);
        }

        [Test]
        public void LaserCue_ByTier()
        {
            var lib = ScriptableObject.CreateInstance<SoundLibrary>();
            lib.TurretLaser1.Clips = new[] { Clip() };
            lib.PrismBeam.Clips = new[] { Clip() };
            Assert.AreSame(lib.TurretLaser, DefenseSound.LaserCue(lib, 0));
            Assert.AreSame(lib.TurretLaser1, DefenseSound.LaserCue(lib, 1));
            Assert.AreSame(lib.TurretLaser1, DefenseSound.LaserCue(lib, 2));   // 2단계 요새 포대 · 치안 본부 = 1단계 레이저
            Assert.AreSame(lib.PrismBeam, DefenseSound.LaserCue(lib, 3));
            lib.PrismBeam.Clips = new AudioClip[0];
            Assert.AreSame(lib.TurretLaser1, DefenseSound.LaserCue(lib, 3));
            Object.DestroyImmediate(lib);
        }

        [Test]
        public void RaidCues_FallBackToMeteor()
        {
            var lib = ScriptableObject.CreateInstance<SoundLibrary>();
            Assert.AreSame(lib.MeteorIncoming, DefenseSound.IncomingCue(lib, true));
            lib.RaidIncoming.Clips = new[] { Clip() };
            lib.RaidImpact.Clips = new[] { Clip() };
            Assert.AreSame(lib.RaidIncoming, DefenseSound.IncomingCue(lib, true));
            Assert.AreSame(lib.MeteorIncoming, DefenseSound.IncomingCue(lib, false));
            Assert.AreSame(lib.RaidImpact, DefenseSound.ImpactCue(lib, true, MeteorSurface.Hull, true));
            Assert.AreSame(MeteorSound.ImpactCue(lib, MeteorSurface.Armor, false), DefenseSound.ImpactCue(lib, false, MeteorSurface.Armor, false));
            Object.DestroyImmediate(lib);
        }
    }

    /// <summary>바깥 진화 연출 · 오가는 배 소리 크기 (2026-11-26, SOUND_LIST_NEW 3번 · D-4 · D-12): 가까이 · 화면 가운데만 · 멀수록 먹먹하게.</summary>
    public class ExteriorFxSoundTests
    {
        [Test]
        public void Level_NearCenterFull_FarOrBehindSilent()
        {
            var center = new Vector3(0.5f, 0.5f, 10f);
            Assert.AreEqual(1f, ExteriorFxSound.Level(ExteriorFxSound.Near, center), 1e-4f);
            Assert.AreEqual(0f, ExteriorFxSound.Level(ExteriorFxSound.Far + 1f, center), 1e-4f);
            Assert.AreEqual(0f, ExteriorFxSound.Level(5f, new Vector3(0.5f, 0.5f, -1f)), 1e-4f);   // 카메라 뒤
            float mid = ExteriorFxSound.Level((ExteriorFxSound.Near + ExteriorFxSound.Far) / 2f, center);
            Assert.That(mid, Is.GreaterThan(0.1f).And.LessThan(0.5f));                            // 줌 아웃하면 빨리 작아짐 (제곱)
        }

        [Test]
        public void CenterGain_EdgeThenOut()
        {
            Assert.AreEqual(ExteriorFxSound.Edge, ExteriorFxSound.CenterGain(new Vector3(1f, 0.5f, 5f)), 1e-4f);
            Assert.AreEqual(0f, ExteriorFxSound.CenterGain(new Vector3(0.5f + ExteriorFxSound.EdgeOut / 2f, 0.5f, 5f)), 1e-4f);
            Assert.That(ExteriorFxSound.CenterGain(new Vector3(0.6f, 0.5f, 5f)), Is.GreaterThan(ExteriorFxSound.CenterGain(new Vector3(0.9f, 0.5f, 5f))));
        }

        [Test]
        public void Cutoff_DarkerFarther()
        {
            Assert.AreEqual(ExteriorFxSound.CutoffNear, ExteriorFxSound.Cutoff(3f), 1e-3f);
            Assert.AreEqual(ExteriorFxSound.CutoffFar, ExteriorFxSound.Cutoff(80f), 1e-3f);
            Assert.That(ExteriorFxSound.Cutoff(20f), Is.LessThan(ExteriorFxSound.CutoffNear));
        }

        [Test]
        public void Engine_IdleHumAndFaster()
        {
            Assert.AreEqual(0.3f, ExteriorFxSound.EngineLevel(0f), 1e-4f);   // 서 있어도 옅게
            Assert.AreEqual(1f, ExteriorFxSound.EngineLevel(20f), 1e-4f);
            Assert.That(ExteriorFxSound.EnginePitch(10f, 1f), Is.GreaterThan(ExteriorFxSound.EnginePitch(0f, 1f)));
            Assert.That(ExteriorFxSound.EnginePitch(5f, 1.15f), Is.GreaterThan(ExteriorFxSound.EnginePitch(5f, 0.85f)));   // 작은 배가 높게
        }
    }
}
