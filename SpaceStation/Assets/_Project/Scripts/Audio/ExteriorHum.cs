using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 바깥 정거장 험 3단계 (2026-11-25, SOUND_LIST_NEW 4-4 ext_hum_1 ~ 3 · D-12): 정거장 크기 (모듈 수 · 등급)로 두께 0 ~ 3을 정하고,
    /// 이웃 단계끼리 같은 힘으로 교차하는 무게를 낸다 (amb_station_loop는 늘 깔리고 그 위에 겹침). 순수 계산 — GameAudio가 매 프레임 부름.
    /// </summary>
    public static class ExteriorHum
    {
        public const int Tiers = 3;

        /// <summary>
        /// 두께 (0 ~ 3) = (모듈 수 두께 + 등급 두께) / 2.
        /// 모듈 수 두께: 1개 = 0, tierModules[k]개 = k + 1, 사이는 log로 이음 (8 → 40 → 180처럼 몇 배씩 커지므로).
        /// 등급 두께: 등급 번호가 tierGrades[k] 이상이면 k + 1 (계단).
        /// </summary>
        public static float Thickness(int modules, int gradeIndex, int[] tierModules, int[] tierGrades)
        {
            float byModules = Tiers;
            float prev = 1f;
            for (int k = 0; k < Tiers; k++)
            {
                float need = Mathf.Max(prev + 1f, k < tierModules.Length ? tierModules[k] : prev + 1f);
                if (modules < need)
                {
                    byModules = k + Mathf.Clamp01(Mathf.Log(Mathf.Max(modules, 1) / prev) / Mathf.Log(need / prev));
                    break;
                }
                prev = need;
            }
            int byGrade = 0;
            for (int k = 0; k < Tiers && k < tierGrades.Length; k++)
                if (gradeIndex >= tierGrades[k])
                    byGrade = k + 1;
            return 0.5f * (byModules + byGrade);
        }

        /// <summary>
        /// 단계 tier (0 = 1단계 … 2 = 3단계)의 무게 (0 ~ 1). 두께가 tier + 1이면 1, 이웃 단계 쪽으로 cos · sin으로 줄어
        /// 두 층 무게 제곱 합 = 1 (같은 힘 교차 — 섞이는 동안 크기가 안 꺼짐). 두께 0 ~ 1 = 1단계가 0에서 올라옴, 3 넘으면 3단계 그대로.
        /// </summary>
        public static float TierWeight(float thickness, int tier)
        {
            float d = thickness - (tier + 1);
            if (d >= 0f)
                return tier == Tiers - 1 ? 1f : Mathf.Cos(Mathf.Clamp01(d) * Mathf.PI * 0.5f);
            return Mathf.Sin(Mathf.Clamp01(1f + d) * Mathf.PI * 0.5f);
        }
    }
}
