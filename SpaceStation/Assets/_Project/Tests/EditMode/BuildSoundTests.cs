using NUnit.Framework;
using SpaceStation.Audio;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>모듈 배치 · 철거 소리 (2026-11-25 사운드 개편 1-1): 크기 3갈래 (D-6) · 비어 있는 칸 대체 · 0.15초 안 여러 번 = 한 번 (D-2).</summary>
    public class BuildSoundTests
    {
        [Test]
        public void SizeOf_CellsAndEvolution()
        {
            Assert.AreEqual(BuildSize.Small, BuildSound.SizeOf(1, false));
            Assert.AreEqual(BuildSize.Small, BuildSound.SizeOf(0, false));
            Assert.AreEqual(BuildSize.Multi, BuildSound.SizeOf(2, false));
            Assert.AreEqual(BuildSize.Multi, BuildSound.SizeOf(8, false));
            // 진화 모듈은 칸 수와 상관없이 진화 (1칸짜리 결과도)
            Assert.AreEqual(BuildSize.Evolved, BuildSound.SizeOf(1, true));
            Assert.AreEqual(BuildSize.Evolved, BuildSound.SizeOf(6, true));
            Assert.AreEqual(BuildSize.Small, BuildSound.SizeOf(null, null));
        }

        [Test]
        public void Pick_FallsBackToSmallerSlot()
        {
            var clip = AudioClip.Create("t", 480, 1, 48000, false);
            var s = new SoundCue { Clips = new[] { clip } };
            var m = new SoundCue { Clips = new[] { clip } };
            var evo = new SoundCue { Clips = new[] { clip } };
            var empty = new SoundCue();
            Assert.AreSame(s, BuildSound.Pick(BuildSize.Small, s, m, evo));
            Assert.AreSame(m, BuildSound.Pick(BuildSize.Multi, s, m, evo));
            Assert.AreSame(evo, BuildSound.Pick(BuildSize.Evolved, s, m, evo));
            Assert.AreSame(m, BuildSound.Pick(BuildSize.Evolved, s, m, empty));      // 진화 칸이 비면 여러 칸
            Assert.AreSame(s, BuildSound.Pick(BuildSize.Evolved, s, empty, empty));  // 둘 다 비면 1칸
            Assert.AreSame(s, BuildSound.Pick(BuildSize.Multi, s, empty, evo));      // 여러 칸은 진화로 올라가지 않음
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void Admit_GroupsBurstWithin015Seconds()
        {
            float last = -999f;
            Assert.IsTrue(BuildSound.Admit(ref last, 10f));
            Assert.IsFalse(BuildSound.Admit(ref last, 10f));        // 같은 프레임 여러 개
            Assert.IsFalse(BuildSound.Admit(ref last, 10.14f));     // 0.15초 안 = 묶음 (첫 소리 기준)
            Assert.IsTrue(BuildSound.Admit(ref last, 10.16f));      // 0.15초 지나면 다시
            Assert.IsFalse(BuildSound.Admit(ref last, 10.2f));
            Assert.IsTrue(BuildSound.Admit(ref last, 0.5f));        // 플레이를 다시 켜 시계가 처음부터면 새로
            Assert.AreEqual(0.5f, last, 1e-6f);
        }
    }
}
