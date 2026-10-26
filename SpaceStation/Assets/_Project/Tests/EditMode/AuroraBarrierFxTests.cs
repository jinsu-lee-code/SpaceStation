using NUnit.Framework;
using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>실드 돔 오로라 장벽: 커튼이 실드 구면에 붙고, 맨 위 · 양옆 끝은 투명, 아래 단 가운데는 보임.</summary>
    public class AuroraBarrierFxTests
    {
        private const int W = AuroraBarrierFx.Columns + 1;
        private readonly Vector3[] _verts = new Vector3[(AuroraBarrierFx.Columns + 1) * (AuroraBarrierFx.Rows + 1)];
        private readonly Color[] _colors = new Color[(AuroraBarrierFx.Columns + 1) * (AuroraBarrierFx.Rows + 1)];

        [TestCase(1f, 0.4f, -1f)]  // 옆에서
        [TestCase(0f, 1f, 0f)]     // 바로 위에서
        [TestCase(-0.3f, 0.2f, 1f)]
        public void Curtain_HugsShieldSphere(float x, float y, float z)
        {
            var center = new Vector3(3f, 1f, -2f);
            const float radius = 8.6f, fold = 0.28f;
            var contact = center + new Vector3(x, y, z).normalized * radius;
            AuroraBarrierFx.Shape(_verts, _colors, center, radius, contact, 6f, 3.3f, fold, 0.7f, 0f);
            foreach (var v in _verts)
                Assert.AreEqual(radius, Vector3.Distance(center, contact + v), fold + 1e-3f);
        }

        [Test]
        public void Curtain_FadesAtTopAndSides()
        {
            var contact = new Vector3(0f, 0f, -8f);
            AuroraBarrierFx.Shape(_verts, _colors, Vector3.zero, 8f, contact, 6f, 3.3f, 0.28f, 0f, 0f);
            for (int i = 0; i < W; i++)
                Assert.AreEqual(0f, _colors[AuroraBarrierFx.Rows * W + i].a, 1e-4f, "맨 위 줄은 투명");
            for (int j = 0; j <= AuroraBarrierFx.Rows; j++)
            {
                Assert.AreEqual(0f, _colors[j * W].a, 1e-4f, "왼쪽 끝은 투명");
                Assert.AreEqual(0f, _colors[j * W + AuroraBarrierFx.Columns].a, 1e-4f, "오른쪽 끝은 투명");
            }
            Assert.Greater(_colors[W / 2].a, 0.3f, "아래 단 가운데는 보임");
        }
    }
}
