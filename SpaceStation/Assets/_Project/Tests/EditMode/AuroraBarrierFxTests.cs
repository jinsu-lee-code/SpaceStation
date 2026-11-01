using NUnit.Framework;
using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 실드 돔 오로라 파문: 기존 실드처럼 구 전체 — 닿은 자리에서 구면을 따라 퍼지고, 앞면 윤곽이 밝고 가운데는 비치며, 뒷면은 감춤.
    /// </summary>
    public class AuroraBarrierFxTests
    {
        private static readonly Vector3 Center = new Vector3(3f, 1f, -2f);
        private const float Radius = 8f;
        private static readonly Vector3 Eye = Center + new Vector3(0f, 0f, -40f); // 카메라: −z 쪽 멀리

        private readonly Vector3[] _unit = AuroraBarrierFx.SphereVertices();
        private Color[] _colors;

        [SetUp]
        public void SetUp() => _colors = new Color[_unit.Length];

        private float AlphaNear(Vector3 dir)
        {
            int best = 0;
            float bestDot = -2f;
            for (int i = 0; i < _unit.Length; i++)
            {
                float d = Vector3.Dot(_unit[i], dir.normalized);
                if (d > bestDot)
                {
                    bestDot = d;
                    best = i;
                }
            }
            return _colors[best].a;
        }

        [Test]
        public void SphereVertices_AreUnit()
        {
            foreach (var v in _unit)
                Assert.AreEqual(1f, v.magnitude, 1e-4f);
        }

        [Test]
        public void Spreads_FromContactAlongSphere()
        {
            var hit = new Vector3(-1f, 0.3f, -0.4f).normalized;
            AuroraBarrierFx.Paint(_colors, _unit, Center, Radius, hit, Eye, 0.3f, 0.35f, 0f);
            Assert.Greater(AlphaNear(new Vector3(-1f, 0f, -1f)), 0.06f, "닿은 자리 근처 앞면은 이미 퍼짐");
            Assert.Less(AlphaNear(new Vector3(1f, 0f, -1f)), 0.02f, "반대쪽 앞면은 아직 안 퍼짐");
        }

        [TestCase(0f)]
        [TestCase(0.8f)]
        public void FullySpread_RimBrightFaceClearBackHidden(float time)
        {
            var hit = new Vector3(0f, 0.2f, -1f).normalized;
            AuroraBarrierFx.Paint(_colors, _unit, Center, Radius, hit, Eye, time, 1f, 0f);
            // 원근 때문에 보이는 윤곽은 카메라 쪽으로 조금 앞 (n · z = 0보다 앞)
            float rim = Mathf.Max(AlphaNear(new Vector3(1f, -0.02f, -0.35f)), AlphaNear(new Vector3(-1f, -0.02f, -0.35f)));
            float face = AlphaNear(new Vector3(0f, -0.25f, -1f));
            Assert.Greater(rim, face, "윤곽이 가운데보다 밝음 (정거장이 비쳐 보이게)");
            Assert.Less(AlphaNear(new Vector3(0f, 0f, 1f)), 0.01f, "뒷면은 감춤");
            foreach (var c in _colors)
                Assert.That(c.a, Is.InRange(0f, 1f));
        }
    }
}
