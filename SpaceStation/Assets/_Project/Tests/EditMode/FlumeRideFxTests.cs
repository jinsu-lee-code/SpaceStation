using NUnit.Framework;
using SpaceStation.Building;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>화물 환적장 후룸라이드 속도: 오르막은 체인이 일정하게, 큰 낙하는 빨라지고, 평지에서는 다시 기본 속도로.</summary>
    public class FlumeRideFxTests
    {
        [Test]
        public void Speeds_LiftSteadyDropFastThenSettle()
        {
            // 평지 0.4 → 오르막 0.5 (높이 0.5) → 능선 0.3 → 낙하 0.5 → 평지 0.6, 0.02 간격
            var pts = new System.Collections.Generic.List<Vector3>();
            float x = 0f, y = 0f;
            void Run(float length, float rise)
            {
                int n = Mathf.RoundToInt(length / 0.02f);
                for (int i = 0; i < n; i++)
                {
                    x += length / n;
                    y += rise / n;
                    pts.Add(new Vector3(x, y, 0f));
                }
            }
            pts.Add(Vector3.zero);
            Run(0.4f, 0f);
            int liftStart = pts.Count;
            Run(0.5f, 0.5f);
            int liftEnd = pts.Count - 1;
            Run(0.3f, 0f);
            int dropStart = pts.Count;
            Run(0.5f, -0.5f);
            int bottom = pts.Count - 1;
            Run(0.6f, 0f);
            var v = FlumeRideFx.Speeds(pts.ToArray(), 0.3f, 0.2f, 1.6f, 0.18f);
            for (int i = liftStart; i <= liftEnd; i++)
                Assert.AreEqual(0.2f, v[i], 1e-4f, "체인 리프트는 일정");
            for (int i = dropStart + 1; i <= bottom; i++)
                Assert.Greater(v[i], v[i - 1], "낙하하며 빨라짐");
            Assert.Greater(v[bottom], 1.1f, "큰 낙하 바닥은 기본 속도보다 훨씬 빠름");
            Assert.AreEqual(0.3f, v[pts.Count - 1], 0.05f, "평지에서 기본 속도로 돌아옴");
        }
    }
}
