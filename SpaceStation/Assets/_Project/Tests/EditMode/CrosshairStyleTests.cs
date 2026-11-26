using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Settings;

namespace SpaceStation.Tests
{
    /// <summary>REVIEW_1115 1-8 조준점 꾸미기: 저장 문자열 왕복 · HEX 해석 · 범위 고정 · 기본 모양 4 · 프로필 목록.</summary>
    public class CrosshairStyleTests
    {
        private static CrosshairStyle Custom() => new CrosshairStyle
        {
            Visible = false,
            Color = 0x12ABEF,
            OutlineOn = false,
            OutlineThickness = 3,
            OutlineOpacity = 35,
            DotOn = true,
            DotSize = 7,
            DotOpacity = 65,
            Inner = new CrosshairStyle.Lines(true, 13, 5, 0, 80),
            Outer = new CrosshairStyle.Lines(true, 20, 10, 40, 5),
        };

        [Test]
        public void Serialize_RoundTrips_DefaultBuiltInsAndCustom()
        {
            var styles = new List<CrosshairStyle> { CrosshairStyle.Default, Custom() };
            for (int i = 0; i < CrosshairStyle.BuiltInCount; i++)
                styles.Add(CrosshairStyle.BuiltIn(i));
            foreach (var s in styles)
            {
                string text = s.Serialize();
                Assert.IsTrue(CrosshairStyle.TryParse(text, out var back), text);
                Assert.AreEqual(s, back, text);
                Assert.AreEqual(text, back.Serialize());
            }
        }

        [Test]
        public void Parse_RejectsBrokenOrOtherVersion_AndFallsBackToDefault()
        {
            foreach (var bad in new[] { null, "", "v2;vis=1", "garbage", ";vis=0" })
            {
                Assert.IsFalse(CrosshairStyle.TryParse(bad, out var s), bad ?? "null");
                Assert.AreEqual(CrosshairStyle.Default, s);
            }
        }

        [Test]
        public void Parse_KeepsGoodFields_DefaultsBrokenOnes()
        {
            // 색은 깨짐 (기본 흰색), 점은 칸 수가 틀림 (기본값), 안쪽 선은 그대로, 모르는 칸은 무시
            Assert.IsTrue(CrosshairStyle.TryParse("v1;col=ZZZZZZ;dot=1,3;in=1,9,3,4,70;what=1", out var s));
            Assert.AreEqual(0xFFFFFF, s.Color);
            Assert.AreEqual(CrosshairStyle.Default.DotSize, s.DotSize);
            Assert.AreEqual(new CrosshairStyle.Lines(true, 9, 3, 4, 70), s.Inner);
            Assert.AreEqual(CrosshairStyle.Default.Outer, s.Outer);
        }

        [Test]
        public void Parse_ClampsOutOfRangeValues()
        {
            Assert.IsTrue(CrosshairStyle.TryParse("v1;ol=1,99,-5;dot=1,0,250;in=1,0,99,-3,101;out=1,500,0,99,50", out var s));
            Assert.AreEqual(CrosshairStyle.OutlineMax, s.OutlineThickness);
            Assert.AreEqual(0, s.OutlineOpacity);
            Assert.AreEqual(CrosshairStyle.DotMin, s.DotSize);
            Assert.AreEqual(CrosshairStyle.OpacityMax, s.DotOpacity);
            Assert.AreEqual(CrosshairStyle.LengthMin, s.Inner.Length);
            Assert.AreEqual(CrosshairStyle.ThicknessMax, s.Inner.Thickness);
            Assert.AreEqual(0, s.Inner.Gap);
            Assert.AreEqual(CrosshairStyle.OpacityMax, s.Inner.Opacity);
            Assert.AreEqual(CrosshairStyle.LengthMax, s.Outer.Length);
            Assert.AreEqual(CrosshairStyle.ThicknessMin, s.Outer.Thickness);
            Assert.AreEqual(CrosshairStyle.OuterGapMax, s.Outer.Gap);
        }

        [Test]
        public void Clamped_KeepsInRangeValues_AndFixesOutOfRange()
        {
            var ok = Custom();
            Assert.AreEqual(ok, ok.Clamped());
            var bad = ok;
            bad.Color = unchecked((int)0xFF123456);
            bad.Inner.Gap = 999;
            bad.Outer.Gap = 999;
            var c = bad.Clamped();
            Assert.AreEqual(0x123456, c.Color);
            Assert.AreEqual(CrosshairStyle.InnerGapMax, c.Inner.Gap);
            Assert.AreEqual(CrosshairStyle.OuterGapMax, c.Outer.Gap);
        }

        [Test]
        public void Hex_ParsesOnlySixDigitRgb()
        {
            Assert.IsTrue(CrosshairStyle.TryParseHex("#FFAA00", out int a));
            Assert.AreEqual(0xFFAA00, a);
            Assert.IsTrue(CrosshairStyle.TryParseHex("ffaa00", out int b));
            Assert.AreEqual(0xFFAA00, b);
            Assert.IsTrue(CrosshairStyle.TryParseHex("  #00ff7f ", out int c));
            Assert.AreEqual(0x00FF7F, c);
            foreach (var bad in new[] { null, "", "#", "#FFF", "#FFAA0", "#FFAA001", "#GGGGGG", "FF AA00", "##FFAA0", "0xFFAA00", "-FFFFF" })
                Assert.IsFalse(CrosshairStyle.TryParseHex(bad, out _), bad ?? "null");
            Assert.AreEqual("#0A0B0C", CrosshairStyle.ToHex(0x0A0B0C));
            Assert.AreEqual("#FFFFFF", CrosshairStyle.ToHex(0xFFFFFF));
            Assert.IsTrue(CrosshairStyle.TryParseHex(CrosshairStyle.ToHex(0x12ABEF), out int d));
            Assert.AreEqual(0x12ABEF, d);
        }

        [Test]
        public void Swatches_EightNamedDistinctColors_WhiteFirst()
        {
            Assert.AreEqual(8, CrosshairStyle.Swatches.Length);
            Assert.AreEqual(8, CrosshairStyle.SwatchNames.Length);
            Assert.AreEqual(0xFFFFFF, CrosshairStyle.Swatches[0]);
            Assert.AreEqual(0x000000, CrosshairStyle.Swatches[7]);
            CollectionAssert.AllItemsAreUnique(CrosshairStyle.Swatches);
            for (int i = 0; i < 8; i++)
                Assert.AreEqual(i, CrosshairStyle.SwatchIndex(CrosshairStyle.Swatches[i]));
            Assert.AreEqual(-1, CrosshairStyle.SwatchIndex(0x123456));
        }

        [Test]
        public void BuiltIns_FourDistinctShapes_DefaultIsWhiteDotWithThinBlackOutline()
        {
            Assert.AreEqual(4, CrosshairStyle.BuiltInCount);
            CollectionAssert.AreEqual(new[] { "점", "십자", "작은 십자 + 점", "넓은 십자" }, CrosshairStyle.BuiltInNames);
            var seen = new HashSet<string>();
            for (int i = 0; i < CrosshairStyle.BuiltInCount; i++)
            {
                var s = CrosshairStyle.BuiltIn(i);
                Assert.AreEqual(s, s.Clamped(), "기본 모양 " + i + " 값이 범위 안");
                Assert.IsTrue(s.Visible);
                Assert.AreEqual(0xFFFFFF, s.Color, "기본 모양은 모두 흰색");
                Assert.IsTrue(s.OutlineOn, "기본 모양은 모두 검은 외곽선");
                Assert.IsTrue(s.DotOn || s.Inner.On || s.Outer.On, "보이는 부품이 있음");
                Assert.IsTrue(seen.Add(s.Serialize()), "기본 모양끼리 달라야 함");
                if (s.Outer.On)
                    Assert.Greater(s.Outer.Gap, s.Inner.Gap + s.Inner.Length, "바깥 선이 안쪽 선과 겹치지 않음");
            }
            var d = CrosshairStyle.Default;
            Assert.AreEqual(CrosshairStyle.BuiltIn(0), d);
            Assert.IsTrue(d.DotOn);
            Assert.IsFalse(d.Inner.On);
            Assert.IsFalse(d.Outer.On);
            Assert.AreEqual(1, d.OutlineThickness, "얇은 외곽선");
            Assert.IsTrue(CrosshairStyle.BuiltIn(2).DotOn && CrosshairStyle.BuiltIn(2).Inner.On, "작은 십자 + 점");
            Assert.IsTrue(CrosshairStyle.BuiltIn(3).Outer.On, "넓은 십자는 바깥 선까지");
        }

        [Test]
        public void SameShape_IgnoresVisibleOnly()
        {
            var a = CrosshairStyle.BuiltIn(1);
            var b = a;
            b.Visible = false;
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a.SameShape(b));
            b.Inner.Gap += 1;
            Assert.IsFalse(a.SameShape(b));
        }

        [Test]
        public void Profiles_NamesEmptySlotsAndMatches()
        {
            var slots = new CrosshairStyle?[] { null, Custom(), CrosshairStyle.BuiltIn(1) };
            Assert.AreEqual(7, CrosshairProfiles.Count);
            Assert.AreEqual("점", CrosshairProfiles.Name(0, slots));
            Assert.AreEqual("넓은 십자", CrosshairProfiles.Name(3, slots));
            Assert.AreEqual("내 프로필 1 (비어 있음)", CrosshairProfiles.Name(4, slots));
            Assert.AreEqual("내 프로필 2", CrosshairProfiles.Name(5, slots));
            Assert.AreEqual("내 프로필 3", CrosshairProfiles.Name(6, slots));
            Assert.IsFalse(CrosshairProfiles.Get(4, slots).HasValue);
            Assert.AreEqual(Custom(), CrosshairProfiles.Get(5, slots));
            Assert.IsFalse(CrosshairProfiles.Get(7, slots).HasValue);
            Assert.IsFalse(CrosshairProfiles.Get(-1, slots).HasValue);
            Assert.IsTrue(CrosshairProfiles.IsUserSlot(4) && !CrosshairProfiles.IsUserSlot(3) && !CrosshairProfiles.IsUserSlot(7));

            var hidden = Custom();
            hidden.Visible = true; // 표시 켬 / 끔은 비교에서 뺌
            Assert.AreEqual(5, CrosshairProfiles.Match(hidden, slots));
            // 같은 모양이 둘 (십자 = 내 프로필 3): 고른 칸을 지키고, 없으면 앞에서부터
            Assert.AreEqual(6, CrosshairProfiles.Match(CrosshairStyle.BuiltIn(1), slots, prefer: 6));
            Assert.AreEqual(1, CrosshairProfiles.Match(CrosshairStyle.BuiltIn(1), slots));
            var odd = CrosshairStyle.Default;
            odd.DotSize = 7;
            Assert.AreEqual(-1, CrosshairProfiles.Match(odd, slots));
        }
    }
}
