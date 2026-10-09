using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>11-11d 동물 주민: 종류 · 털색 고르기, 재질 이름 → 슬롯.</summary>
    public class AnimalLookTests
    {
        [Test]
        public void Species_And_Fur_AreDeterministic_InRange_AndSpread()
        {
            var seen = new int[8];
            for (int id = 1; id <= 400; id++)
            {
                int s = AnimalLooks.Species(id, 8);
                Assert.That(s, Is.InRange(0, 7));
                Assert.AreEqual(s, AnimalLooks.Species(id, 8), "같은 주민은 같은 종류");
                Assert.That(AnimalLooks.Fur(id, 5), Is.InRange(0, 4));
                seen[s]++;
            }
            foreach (var n in seen)
                Assert.Greater(n, 20, "8종이 고르게 나옴 (400명 중 종류마다 20명 넘게)");
            Assert.AreEqual(0, AnimalLooks.Species(5, 1));
            Assert.AreEqual(0, AnimalLooks.Fur(5, 0));
        }

        [Test]
        public void SlotOf_ReadsMaterialName()
        {
            Assert.AreEqual("Fur", AnimalModelSet.SlotOf("M_Animal_Fur"));
            Assert.AreEqual("FurLight", AnimalModelSet.SlotOf("M_Animal_FurLight"));
            Assert.AreEqual("Eye", AnimalModelSet.SlotOf("M_Animal_Eye.001"));
            Assert.AreEqual("EyeHi", AnimalModelSet.SlotOf("M_Animal_EyeHi (Instance)"));
            Assert.IsNull(AnimalModelSet.SlotOf("Lit"));
            Assert.IsNull(AnimalModelSet.SlotOf(null));
        }
    }
}
