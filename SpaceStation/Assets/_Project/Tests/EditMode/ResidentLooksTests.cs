using System.Linq;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Interior;

namespace SpaceStation.Tests
{
    /// <summary>11-11b 주민 외형 고르기 (머리 · 옷 · 얼굴 조합).</summary>
    public class ResidentLooksTests
    {
        private static ResidentLookTables Tables()
        {
            var t = new ResidentLookTables
            {
                Eyes = new[] { "round", "sleepy" }, Brows = new[] { "straight" }, Mouths = new[] { "smile" }, Marks = new[] { "none", "freckles" },
                ManFront = new[] { "blunt", "up" }, ManSide = new[] { "short" }, ManBack = new[] { "short", "bob" },
                WomanFront = new[] { "part" }, WomanSide = new[] { "lock" }, WomanBack = new[] { "long", "updo" },
            };
            void G(string n, GarmentSlot s, bool man = true, string with = "") =>
                t.Garments.Add(new ResidentLookTables.Garment { Name = n, Slot = s, Man = man, Woman = true, With = with });
            G("tshirt", GarmentSlot.Top);
            G("pants", GarmentSlot.Bottom);
            G("skirt", GarmentSlot.Bottom, man: false, with: "panties");
            G("panties", GarmentSlot.Under, man: false);
            G("jumpsuit", GarmentSlot.Suit);
            G("sneakers", GarmentSlot.Shoes);
            return t;
        }

        [Test]
        public void SameIdGivesSameLook()
        {
            var a = ResidentLooks.Choose(42, true, Tables(), 0.2f);
            var b = ResidentLooks.Choose(42, true, Tables(), 0.2f);
            Assert.AreEqual(a.Hair, b.Hair);
            CollectionAssert.AreEqual(a.Garments, b.Garments);
            Assert.AreEqual(a.Eye + a.Mark, b.Eye + b.Mark);
        }

        [Test]
        public void HairComesFromGenderPool()
        {
            for (int id = 1; id < 60; id++)
            {
                var w = ResidentLooks.Choose(id, true, Tables(), 0f).Hair.Split('_');
                Assert.AreEqual("part", w[0]);
                Assert.AreEqual("lock", w[1]);
                CollectionAssert.Contains(new[] { "long", "updo" }, w[2]);
                var m = ResidentLooks.Choose(id, false, Tables(), 0f).Hair.Split('_');
                CollectionAssert.Contains(new[] { "blunt", "up" }, m[0]);
                Assert.AreEqual("short", m[1]);
                CollectionAssert.Contains(new[] { "short", "bob" }, m[2]);
            }
        }

        [Test]
        public void MenNeverGetWomanOnlyGarments()
        {
            for (int id = 1; id < 200; id++)
            {
                var look = ResidentLooks.Choose(id, false, Tables(), 0.3f);
                CollectionAssert.DoesNotContain(look.Garments, "skirt");
                CollectionAssert.DoesNotContain(look.Garments, "panties");
            }
        }

        [Test]
        public void SkirtAlwaysComesWithPanties()
        {
            int skirts = 0;
            for (int id = 1; id < 200; id++)
            {
                var look = ResidentLooks.Choose(id, true, Tables(), 0f);
                if (look.Garments.Contains("skirt"))
                {
                    skirts++;
                    CollectionAssert.Contains(look.Garments, "panties");
                }
            }
            Assert.Greater(skirts, 0);
        }

        [Test]
        public void SuitReplacesTopAndBottom()
        {
            var all = Enumerable.Range(1, 200).Select(id => ResidentLooks.Choose(id, false, Tables(), 1f)).ToList();
            foreach (var look in all)
            {
                CollectionAssert.Contains(look.Garments, "jumpsuit");
                CollectionAssert.DoesNotContain(look.Garments, "tshirt");
                CollectionAssert.DoesNotContain(look.Garments, "pants");
                CollectionAssert.Contains(look.Garments, "sneakers");
            }
            var none = Enumerable.Range(1, 200).Select(id => ResidentLooks.Choose(id, false, Tables(), 0f));
            Assert.IsTrue(none.All(l => !l.Garments.Contains("jumpsuit") && l.Garments.Contains("tshirt")));
        }

        [Test]
        public void ResidentsVary()
        {
            var hairs = Enumerable.Range(1, 40).Select(id => ResidentLooks.Choose(id, false, Tables(), 0f).Hair).Distinct().Count();
            var eyes = Enumerable.Range(1, 40).Select(id => ResidentLooks.Choose(id, false, Tables(), 0f).Eye).Distinct().Count();
            Assert.AreEqual(4, hairs);
            Assert.AreEqual(2, eyes);
        }
    }
}
