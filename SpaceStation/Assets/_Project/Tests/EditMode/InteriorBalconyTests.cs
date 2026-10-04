using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEngine;

namespace SpaceStation.Tests
{
    public class InteriorBalconyTests
    {
        private const float S = 4f;
        private const float T = 0.2f;

        private static List<Vector3Int> Core()
        {
            var cells = new List<Vector3Int>();
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
                cells.Add(new Vector3Int(x, y, z));
            return cells;
        }

        [Test]
        public void SingleLevel_NoBalcony()
        {
            var plan = InteriorBalcony.Build(new[] { Vector3Int.zero, Vector3Int.right }, S, T);
            Assert.IsTrue(plan.IsEmpty);
            Assert.IsFalse(plan.HasStair);
        }

        [Test]
        public void Core_DecksAlongOuterWalls_NoOverlap()
        {
            var plan = InteriorBalcony.Build(Core(), S, T);
            Assert.AreEqual(8, plan.Decks.Count, "위층 4칸 × 바깥 벽 2면");
            // 띠 넓이 합 = 바닥 8×8 - 트인 곳 5.2×5.2 (모서리 겹침 없음)
            float area = plan.Decks.Sum(d => d.Length * InteriorBalcony.DeckWidth);
            Assert.AreEqual(64f - 5.2f * 5.2f, area, 0.01f);
            Assert.IsTrue(plan.Decks.All(d => Mathf.Approximately(d.Center.y, S - S * 0.5f + T)), "위층 바닥 높이");
        }

        [Test]
        public void Core_RailsCloseVoid_ExceptBridgeGap()
        {
            var plan = InteriorBalcony.Build(Core(), S, T);
            // 트인 곳 둘레 5.2×4 - 다리 폭 + 다리 양옆 난간 2줄
            float bridgeRails = 2f * (plan.Bridge.Length - 0.2f);
            float total = plan.Rails.Sum(r => r.Length);
            Assert.AreEqual(5.2f * 4f - InteriorBalcony.BridgeWidth + bridgeRails, total, 0.01f);
        }

        [Test]
        public void Core_StairAtHallCenter_RisesOneLevel()
        {
            var plan = InteriorBalcony.Build(Core(), S, T);
            Assert.IsTrue(plan.HasStair);
            Assert.AreEqual(2f, plan.StairBase.x, 0.001f);
            Assert.AreEqual(2f, plan.StairBase.z, 0.001f);
            Assert.AreEqual(-S * 0.5f + T, plan.StairBase.y, 0.001f);
            Assert.AreEqual(S, plan.StairRise, 0.001f);
            // 다리가 계단 끝에서 발코니 가장자리(가운데에서 2.6m)까지
            float far = Vector3.Dot(plan.Bridge.Center - plan.StairBase, plan.BridgeDirection) + plan.Bridge.Length * 0.5f;
            Assert.AreEqual(2.6f + 0.1f, far, 0.01f);
        }

        [Test]
        public void NarrowStack_BalconyWithoutStair()
        {
            var plan = InteriorBalcony.Build(new[] { Vector3Int.zero, Vector3Int.up }, S, T);
            Assert.AreEqual(4, plan.Decks.Count);
            Assert.IsFalse(plan.HasStair);
        }
    }
}
