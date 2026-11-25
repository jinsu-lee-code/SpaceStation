using NUnit.Framework;
using SpaceStation.Audio;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>바깥 정거장 험 3단계 (2026-11-25): 두께 (모듈 수 · 등급)와 이웃 단계 교차 무게.</summary>
    public class ExteriorHumTests
    {
        private static readonly int[] Modules = { 8, 40, 180 };   // 소형 · 대형 · 국가급 모듈 수 조건 (BALANCE 11번)
        private static readonly int[] Grades = { 1, 3, 5 };

        [Test]
        public void Thickness_FollowsModulesAndGrade()
        {
            Assert.AreEqual(0f, ExteriorHum.Thickness(1, 0, Modules, Grades), 1e-4f);
            Assert.AreEqual(1f, ExteriorHum.Thickness(8, 1, Modules, Grades), 1e-4f);
            Assert.AreEqual(2f, ExteriorHum.Thickness(40, 3, Modules, Grades), 1e-4f);
            Assert.AreEqual(3f, ExteriorHum.Thickness(180, 5, Modules, Grades), 1e-4f);
            Assert.AreEqual(3f, ExteriorHum.Thickness(400, 7, Modules, Grades), 1e-4f);
            // 모듈은 많은데 등급이 낮으면 (인구 부족) 그 사이
            Assert.AreEqual(1.5f, ExteriorHum.Thickness(40, 1, Modules, Grades), 1e-4f);
            // 사이는 log로: 8 → 40의 가운데 (√320 ≈ 17.9개)에서 모듈 두께 1.5
            Assert.AreEqual(0.5f * (1.5f + 1f), ExteriorHum.Thickness(Mathf.RoundToInt(Mathf.Sqrt(320f)), 1, Modules, Grades), 0.01f);
        }

        [Test]
        public void TierWeights_CrossfadeWithEqualPower()
        {
            Assert.AreEqual(0f, ExteriorHum.TierWeight(0f, 0), 1e-4f);
            Assert.AreEqual(1f, ExteriorHum.TierWeight(1f, 0), 1e-4f);
            Assert.AreEqual(0f, ExteriorHum.TierWeight(1f, 1), 1e-4f);
            Assert.AreEqual(1f, ExteriorHum.TierWeight(3.5f, 2), 1e-4f);
            for (float t = 1f; t <= 3f; t += 0.125f)
            {
                float sum = 0f;
                for (int k = 0; k < ExteriorHum.Tiers; k++)
                    sum += ExteriorHum.TierWeight(t, k) * ExteriorHum.TierWeight(t, k);
                Assert.AreEqual(1f, sum, 1e-3f, $"두께 {t}");
            }
        }
    }
}
