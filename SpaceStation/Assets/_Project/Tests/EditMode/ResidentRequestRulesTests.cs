using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>11-17 ③ 주민 요청 규칙.</summary>
    public class ResidentRequestRulesTests
    {
        [Test]
        public void Pick_OnlyAvailable_AvoidsActiveKinds()
        {
            // 화분 · 벽 자리 없음, 다른 방 하나 있음 → 가져다주기 · 말동무만
            for (int i = 0; i < 20; i++)
            {
                var k = ResidentRequestRules.Pick(false, true, false, new List<RequestKind>(), i / 20.0);
                Assert.IsTrue(k == RequestKind.Fetch || k == RequestKind.Talk);
            }
            // 가져다주기가 이미 걸려 있으면 말동무
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(RequestKind.Talk, ResidentRequestRules.Pick(false, true, false, new List<RequestKind> { RequestKind.Fetch }, i / 20.0));
            // 다 겹치면 할 수 있는 것 중에서 (말동무 중복 허용)
            Assert.AreEqual(RequestKind.Talk, ResidentRequestRules.Pick(false, false, false, new List<RequestKind> { RequestKind.Talk }, 0.5));
        }

        [Test]
        public void Pick_CoversAllKinds_WhenAvailable()
        {
            var seen = new HashSet<RequestKind>();
            for (int i = 0; i < 100; i++)
                seen.Add(ResidentRequestRules.Pick(true, true, true, null, i / 100.0).Value);
            Assert.AreEqual(4, seen.Count);
        }

        [Test]
        public void NextDelay_InRange_AndItemsNotRepeated()
        {
            Assert.AreEqual(120f, ResidentRequestRules.NextDelay(120f, 180f, 0.0), 1e-4f);
            Assert.AreEqual(180f, ResidentRequestRules.NextDelay(120f, 180f, 1.0), 1e-4f);
            Assert.IsFalse(ResidentRequestRules.CanAdd(2, 2));
            Assert.IsTrue(ResidentRequestRules.CanAdd(1, 2));
            for (int i = 0; i < 10; i++)
                Assert.AreNotEqual(FetchItem.Toolbox, ResidentRequestRules.PickItem(new List<FetchItem> { FetchItem.Toolbox }, i / 10.0));
        }

        [Test]
        public void TalkLine_UsesTraitLines()
        {
            string line = ResidentRequestRules.TalkLine(new[] { ResidentTrait.Gardener }, 0.0);
            StringAssert.Contains("상추", line);
            Assert.IsFalse(string.IsNullOrEmpty(ResidentRequestRules.TalkLine(new ResidentTrait[0], 0.3)));
        }
    }
}
