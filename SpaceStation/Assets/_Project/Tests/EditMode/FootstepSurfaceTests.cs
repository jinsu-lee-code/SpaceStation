using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>⑤ 바닥별 발소리 (2026-11-25): 재질 이름 → 바닥 7 표와 얕은 물 판정.</summary>
    public class FootstepSurfaceTests
    {
        [TestCase("M_InteriorHull", StepFloor.Metal)]
        [TestCase("M_InteriorEvoWhite", StepFloor.Metal)]
        [TestCase("M_InteriorEvo2Deck", StepFloor.Metal)]
        [TestCase("M_InteriorDevice", StepFloor.Metal)]      // "Ice"가 대소문자로 안 걸림
        [TestCase("M_InteriorHullDark", StepFloor.Grate)]
        [TestCase("M_InteriorEvo2ClearGlass", StepFloor.Glass)]
        [TestCase("M_InteriorWindow", StepFloor.Glass)]
        [TestCase("M_InteriorEvo2Ice", StepFloor.Glass)]
        [TestCase("M_InteriorEvo2Lawn", StepFloor.Grass)]
        [TestCase("M_InteriorEvo4Lawn", StepFloor.Grass)]
        [TestCase("M_InteriorEvo3BeachSand", StepFloor.Grass)]
        [TestCase("M_InteriorEvo2Bed", StepFloor.Grass)]
        [TestCase("M_InteriorPlantLight", StepFloor.Grass)]
        [TestCase("M_InteriorEvo3Wood", StepFloor.Wood)]
        [TestCase("M_InteriorEvo3TreeBark", StepFloor.Wood)]
        [TestCase("M_InteriorRug", StepFloor.Wood)]
        [TestCase("M_InteriorEvo3Pearl", StepFloor.Stone)]
        [TestCase("M_InteriorEvo2Path", StepFloor.Stone)]
        [TestCase("M_InteriorEvo2Rock", StepFloor.Stone)]
        [TestCase("M_InteriorEvo2Sand", StepFloor.Stone)]     // 달빛 자갈 마당
        [TestCase("M_InteriorEvo3SeaBed", StepFloor.Stone)]
        [TestCase("M_InteriorEvo3Gold", StepFloor.Metal)]
        [TestCase("M_Paint_Evo_Fortress", StepFloor.Metal)]
        [TestCase(null, StepFloor.Metal)]
        public void Classify_MaterialName(string name, StepFloor expected)
        {
            Assert.AreEqual(expected, FootstepSurface.Classify(name));
        }

        [Test]
        public void InWater_OnlyInsideAreaAndShallowBelowSurface()
        {
            var water = new List<FootstepSurface.WaterLevel> { new FootstepSurface.WaterLevel(new Rect(0f, 0f, 10f, 10f), 1.0f) };
            Assert.IsTrue(FootstepSurface.InWater(new Vector3(5f, 0.8f, 5f), water));     // 20 cm 아래
            Assert.IsFalse(FootstepSurface.InWater(new Vector3(5f, 1.0f, 5f), water));    // 수면과 같음 (물가)
            Assert.IsFalse(FootstepSurface.InWater(new Vector3(5f, 0.4f, 5f), water));    // 60 cm 아래 (깊은 물)
            Assert.IsFalse(FootstepSurface.InWater(new Vector3(12f, 0.8f, 5f), water));   // 물 면 밖
            Assert.IsFalse(FootstepSurface.InWater(new Vector3(5f, 0.8f, 5f), null));
        }

        [Test]
        public void Wadeable_OnlyNaturalGround()
        {
            Assert.IsTrue(FootstepSurface.IsWadeable(StepFloor.Grass));
            Assert.IsTrue(FootstepSurface.IsWadeable(StepFloor.Stone));
            Assert.IsFalse(FootstepSurface.IsWadeable(StepFloor.Metal));
            Assert.IsFalse(FootstepSurface.IsWadeable(StepFloor.Wood));
            Assert.IsFalse(FootstepSurface.IsWadeable(StepFloor.Glass));
        }

        [Test]
        public void StageGain_LouderInHigherStageRooms()
        {
            // 2026-11-26 정한 것 S-6: 기본 · 1단계 그대로 · 2단계 +2 dB · 3단계 이상 +4 dB
            Assert.AreEqual(0f, FootstepSurface.StageGainDb(0));
            Assert.AreEqual(0f, FootstepSurface.StageGainDb(1));
            Assert.AreEqual(2f, FootstepSurface.StageGainDb(2));
            Assert.AreEqual(4f, FootstepSurface.StageGainDb(3));
            Assert.AreEqual(4f, FootstepSurface.StageGainDb(4));
            Assert.AreEqual(1f, FootstepSurface.StageGain(1), 1e-6f);
            Assert.AreEqual(1.585f, FootstepSurface.StageGain(3), 1e-3f);
        }
    }
}
