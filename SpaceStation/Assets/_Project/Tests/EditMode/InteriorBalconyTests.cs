using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEngine;

namespace SpaceStation.Tests
{
    public class InteriorBalconyTests
    {
        private const float S = 8f;
        private const float D = 3.2f;
        private const float F = -1.3f;

        private static List<Vector3Int> Core()
        {
            var cells = new List<Vector3Int>();
            for (int y = 0; y < 2; y++)
            for (int x = 0; x < 2; x++)
            for (int z = 0; z < 2; z++)
                cells.Add(new Vector3Int(x, y, z));
            return cells;
        }

        private static InteriorBalcony.Plan Build(IReadOnlyList<Vector3Int> cells) => InteriorBalcony.Build(cells, S, D, F);

        [Test]
        public void SingleLevel_NoBalcony()
        {
            var plan = Build(new[] { Vector3Int.zero, Vector3Int.right });
            Assert.IsTrue(plan.IsEmpty);
            Assert.IsFalse(plan.HasStair);
        }

        [Test]
        public void Core_DecksAlongOuterWalls_NoOverlap()
        {
            var plan = Build(Core());
            Assert.AreEqual(8, plan.Decks.Count, "위층 4칸 × 바깥 벽 2면");
            // 홀 14.4×14.4 (칸 중심 간격 8 + 벽 깊이 3.2×2) - 트인 곳 11.6×11.6
            float area = plan.Decks.Sum(d => d.Length * InteriorBalcony.DeckWidth);
            Assert.AreEqual(14.4f * 14.4f - 11.6f * 11.6f, area, 0.01f);
            Assert.IsTrue(plan.Decks.All(d => Mathf.Approximately(d.Center.y, S + F)), "위층 바닥 높이");
        }

        [Test]
        public void Core_RailsCloseVoid_ExceptBridgeGap()
        {
            var plan = Build(Core());
            float bridgeRails = 2f * (plan.Bridge.Length - 0.2f);
            float total = plan.Rails.Sum(r => r.Length);
            Assert.AreEqual(11.6f * 4f - InteriorBalcony.BridgeWidth + bridgeRails, total, 0.01f);
        }

        [Test]
        public void Core_StairAtHallCenter_RisesOneLevel()
        {
            var plan = Build(Core());
            Assert.IsTrue(plan.HasStair);
            Assert.AreEqual(4f, plan.StairBase.x, 0.001f);
            Assert.AreEqual(4f, plan.StairBase.z, 0.001f);
            Assert.AreEqual(F, plan.StairBase.y, 0.001f);
            Assert.AreEqual(S, plan.StairRise, 0.001f);
            Assert.AreEqual(40, plan.StairSteps, "0.2m씩 두 바퀴");
            float far = Vector3.Dot(plan.Bridge.Center - plan.StairBase, plan.BridgeDirection) + plan.Bridge.Length * 0.5f;
            Assert.AreEqual(5.8f + 0.1f, far, 0.01f);
        }

        [Test]
        public void NarrowStack_BalconyWithoutStair()
        {
            var plan = Build(new[] { Vector3Int.zero, Vector3Int.up });
            Assert.AreEqual(4, plan.Decks.Count);
            Assert.IsFalse(plan.HasStair);
        }
    }
}
