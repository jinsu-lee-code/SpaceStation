using UnityEngine;

namespace SpaceStation.Core
{
    /// <summary>모듈 효율 구간 (7-1). 선택 테두리와 선택 패널 글자가 같은 구간·색을 쓴다.</summary>
    public enum EfficiencyBand
    {
        /// <summary>90% 이상: 청록 (정상)</summary>
        Normal,
        /// <summary>50~90%: 노랑</summary>
        Warning,
        /// <summary>25~50%: 빨강</summary>
        Danger,
        /// <summary>25% 미만: 빨강 + 깜빡임</summary>
        Critical,
    }

    /// <summary>
    /// 효율 → 구간·색 판정 (표시 전용 기준이라 밸런스 SO가 아닌 여기서 관리).
    /// 경계값은 위 구간에 포함한다 (정확히 90%는 정상, 50%는 노랑, 25%는 빨강).
    /// </summary>
    public static class EfficiencyBands
    {
        public const float NormalMin = 0.9f;
        public const float WarningMin = 0.5f;
        public const float DangerMin = 0.25f;
        /// <summary>Critical 깜빡임 주기 (초).</summary>
        public const float BlinkPeriod = 0.6f;

        // 글자색 (HUD 색과 동일: 청록 #4FD8FF, 노랑 #FFD24A, 빨강 #FF5A5A)
        public const string NormalHex = "#4FD8FF";
        public const string WarningHex = "#FFD24A";
        public const string DangerHex = "#FF5A5A";
        /// <summary>깜빡임의 꺼진 순간 글자색 (어두운 빨강).</summary>
        public const string CriticalDimHex = "#6A2424";

        // 테두리 빛 (HDR, SelectionRim _RimColor)
        public static readonly Color NormalRim = new Color(0.6f, 1.9f, 2.4f, 1f);
        public static readonly Color WarningRim = new Color(2.2f, 1.8f, 0.5f, 1f);
        public static readonly Color DangerRim = new Color(2.4f, 0.45f, 0.35f, 1f);

        // 선택 틴트 (LDR)
        public static readonly Color NormalTint = new Color(0.31f, 0.85f, 1f, 1f);
        public static readonly Color WarningTint = new Color(1f, 0.9f, 0.3f, 1f);
        public static readonly Color DangerTint = new Color(1f, 0.3f, 0.25f, 1f);

        public static EfficiencyBand Classify(float efficiency)
        {
            // 부동소수 오차로 0.9가 0.8999…가 되어 노랑으로 떨어지지 않게 약간 여유
            const float eps = 1e-4f;
            if (efficiency >= NormalMin - eps)
                return EfficiencyBand.Normal;
            if (efficiency >= WarningMin - eps)
                return EfficiencyBand.Warning;
            if (efficiency >= DangerMin - eps)
                return EfficiencyBand.Danger;
            return EfficiencyBand.Critical;
        }

        /// <summary>깜빡임의 켜진 구간인지 (Critical에서만 의미, 주기 앞쪽 60%가 켜짐).</summary>
        public static bool BlinkOn(float time) => Mathf.Repeat(time, BlinkPeriod) < BlinkPeriod * 0.6f;

        public static string Hex(EfficiencyBand band, float time)
        {
            switch (band)
            {
                case EfficiencyBand.Normal: return NormalHex;
                case EfficiencyBand.Warning: return WarningHex;
                case EfficiencyBand.Danger: return DangerHex;
                default: return BlinkOn(time) ? DangerHex : CriticalDimHex;
            }
        }

        /// <summary>테두리 빛 색. Critical은 깜빡임 꺼진 순간 거의 꺼진 빨강.</summary>
        public static Color Rim(EfficiencyBand band, float time)
        {
            switch (band)
            {
                case EfficiencyBand.Normal: return NormalRim;
                case EfficiencyBand.Warning: return WarningRim;
                case EfficiencyBand.Danger: return DangerRim;
                default: return BlinkOn(time) ? DangerRim * 1.25f : DangerRim * 0.12f;
            }
        }

        public static Color Tint(EfficiencyBand band)
        {
            switch (band)
            {
                case EfficiencyBand.Normal: return NormalTint;
                case EfficiencyBand.Warning: return WarningTint;
                default: return DangerTint;
            }
        }
    }
}
