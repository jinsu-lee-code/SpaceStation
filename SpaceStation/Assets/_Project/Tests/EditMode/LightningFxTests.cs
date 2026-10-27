using NUnit.Framework;
using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>그리드 허브 번개 모양: 양끝은 정확히 닿고, 꺾임은 번개 길이에 비례한 폭 안에서만, 매번 모양이 다름.</summary>
    public class LightningFxTests
    {
        [Test]
        public void Jag_KeepsEndsAndStaysNearLine()
        {
            var from = new Vector3(1f, 0.79f, 0.5f);
            var to = new Vector3(1.9f, 0.29f, 0.5f);
            float length = Vector3.Distance(from, to);
            var a = new Vector3[17];
            var b = new Vector3[17];
            Random.InitState(7);
            LightningFx.Jag(from, to, 0.22f, a);
            LightningFx.Jag(from, to, 0.22f, b);
            Assert.AreEqual(from, a[0]);
            Assert.AreEqual(to, a[16]);
            var dir = (to - from).normalized;
            float bent = 0f, differ = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                float off = Vector3.ProjectOnPlane(a[i] - from, dir).magnitude;
                Assert.Less(off, length * 0.5f, "번개가 옆으로 너무 멀리 튀지 않음");
                bent = Mathf.Max(bent, off);
                differ = Mathf.Max(differ, Vector3.Distance(a[i], b[i]));
            }
            Assert.Greater(bent, length * 0.02f, "곧은 선이 아니라 꺾임");
            Assert.Greater(differ, 1e-3f, "칠 때마다 모양이 바뀜");
        }
    }
}
