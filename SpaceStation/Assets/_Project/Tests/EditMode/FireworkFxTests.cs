using NUnit.Framework;
using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>요새 포대 별자리 불꽃: 별자리 자료가 맞고, 별은 카메라를 향한 면에 위쪽이 월드 위로 놓인다.</summary>
    public class FireworkFxTests
    {
        [Test]
        public void Constellations_AreWellFormed()
        {
            Assert.GreaterOrEqual(FireworkFx.Sky.Length, 3, "번갈아 쓸 별자리 여러 개");
            foreach (var c in FireworkFx.Sky)
            {
                Assert.GreaterOrEqual(c.Stars.Length, 4, c.Name);
                Assert.AreEqual(0, c.Lines.Length % 2, c.Name + " 빛줄은 별 번호 쌍");
                foreach (var i in c.Lines)
                    Assert.That(i, Is.InRange(0, c.Stars.Length - 1), c.Name);
                foreach (var s in c.Stars)
                    Assert.LessOrEqual(Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)), 1f, c.Name + " 별은 −1~1 안");
            }
        }

        [TestCase(0f, 0f, 1f)]
        [TestCase(0.7f, -0.5f, 0.5f)]
        [TestCase(0f, -1f, 0f)] // 바로 위에서 내려다봄
        public void Layout_FacesCameraUpright(float fx, float fy, float fz)
        {
            var forward = new Vector3(fx, fy, fz).normalized;
            var center = new Vector3(4f, 3f, -2f);
            var c = FireworkFx.Sky[2]; // 오리온: 위 · 아래 별이 뚜렷함
            var pos = new Vector3[c.Stars.Length];
            FireworkFx.Layout(c, center, forward, 2f, pos);
            foreach (var p in pos)
                Assert.AreEqual(0f, Vector3.Dot(p - center, forward), 1e-4f, "카메라를 향한 면 위");
            if (Mathf.Abs(fy) < 0.99f)
                Assert.Greater(pos[0].y, pos[5].y, "위쪽 별(베텔게우스)이 아래쪽 별(사이프)보다 위");
            Assert.AreEqual((c.Stars[0] - c.Stars[1]).magnitude, Vector3.Distance(pos[0], pos[1]), 1e-4f, "폭 2 = 별자리 좌표 1당 1");
        }
    }
}
