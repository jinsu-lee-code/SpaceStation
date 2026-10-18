using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>12-3c 정책 효과 종류. 배율(1 = 그대로)이 기본, <see cref="PolicyStats.IsAdditive"/>는 더하는 값(0 = 그대로).</summary>
    public enum PolicyStat
    {
        /// <summary>모든 모듈 생산 배율.</summary>
        Production,
        /// <summary>금속 생산 모듈 생산 배율 (생산 배율에 곱함).</summary>
        MetalProduction,
        /// <summary>주민 식량 · 물 소비 배율.</summary>
        FoodWaterConsumption,
        /// <summary>주민 소비 배율 (산소 · 물 · 식량).</summary>
        ResidentConsumption,
        /// <summary>만족도 상한 + (더하기).</summary>
        SatisfactionCap,
        /// <summary>노후화 속도 배율.</summary>
        Decay,
        /// <summary>연구 속도 배율.</summary>
        ResearchSpeed,
        /// <summary>모듈 전력 수요 배율.</summary>
        PowerDemand,
        /// <summary>인구 증가 간격 배율 (작을수록 빠름).</summary>
        GrowthInterval,
        /// <summary>수리 속도 배율 (수리 시간 ÷ 값).</summary>
        RepairSpeed,
        /// <summary>파손 → 파괴까지 시간 배율.</summary>
        DestroyTimer,
        /// <summary>사회 위기 경고 시간 배율.</summary>
        CrisisWarning,
        /// <summary>사회 위기가 시작되는 만족도 기준 + (더하기, 음수 = 덜 일어남).</summary>
        CrisisThreshold,
    }

    public static class PolicyStats
    {
        public const int Count = 13;

        public static bool IsAdditive(PolicyStat stat) => stat == PolicyStat.SatisfactionCap || stat == PolicyStat.CrisisThreshold;

        public static float Neutral(PolicyStat stat) => IsAdditive(stat) ? 0f : 1f;

        /// <summary>효과 한 줄 (예: "생산 +15%", "만족도 상한 −10"). 좋은 쪽이면 good = true.</summary>
        public static string Describe(PolicyStat stat, float value, out bool good)
        {
            float pct = (value - 1f) * 100f;
            string Pct() => $"{(pct >= 0f ? "+" : "-")}{Mathf.Abs(pct):0}%";
            switch (stat)
            {
                case PolicyStat.Production: good = value > 1f; return $"생산 {Pct()}";
                case PolicyStat.MetalProduction: good = value > 1f; return $"금속 생산 {Pct()}";
                case PolicyStat.FoodWaterConsumption: good = value < 1f; return $"식량 · 물 소비 {Pct()}";
                case PolicyStat.ResidentConsumption: good = value < 1f; return $"주민 소비 {Pct()}";
                case PolicyStat.SatisfactionCap: good = value > 0f; return $"만족도 상한 {(value >= 0f ? "+" : "-")}{Mathf.Abs(value):0}";
                case PolicyStat.Decay: good = value < 1f; return $"노후화 {Pct()}";
                case PolicyStat.ResearchSpeed: good = value > 1f; return $"연구 속도 {Pct()}";
                case PolicyStat.PowerDemand: good = value < 1f; return $"전력 수요 {Pct()}";
                case PolicyStat.GrowthInterval: good = value < 1f; return $"인구 증가 간격 {Pct()}";
                case PolicyStat.RepairSpeed: good = value > 1f; return $"수리 속도 {Pct()}";
                case PolicyStat.DestroyTimer: good = value > 1f; return $"파괴까지 시간 {Pct()}";
                case PolicyStat.CrisisWarning: good = value > 1f; return $"사회 위기 경고 시간 {Pct()}";
                case PolicyStat.CrisisThreshold: good = value < 0f; return $"사회 위기 기준 만족도 {(value >= 0f ? "+" : "-")}{Mathf.Abs(value):0}";
                default: good = true; return stat.ToString();
            }
        }
    }

    [Serializable]
    public struct PolicyModifier
    {
        public PolicyStat Stat;
        public float Value;
    }

    /// <summary>M-22 정책 성격 (판이 주민 반응 등에 씀, 공통 틀은 쓰지 않음).</summary>
    public enum PolicyStance
    {
        /// <summary>어느 쪽도 아님.</summary>
        Neutral,
        /// <summary>통제형 (배급 · 근무 강제 · 감시 등).</summary>
        Control,
        /// <summary>자율형 (개방 · 여가 등).</summary>
        Autonomy,
    }

    /// <summary>12-3c 국가급 상시 정책 하나 (효과 + 부작용). BALANCE 35번.</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Policy", fileName = "PO_NewPolicy")]
    public sealed class PolicyData : ScriptableObject
    {
        [SerializeField] private string _displayName = "정책";
        [TextArea(2, 4)]
        [SerializeField] private string _description;
        [SerializeField] private List<PolicyModifier> _modifiers = new List<PolicyModifier>();
        [Tooltip("M-22 정책 성격 (판이 주민 반응 등에 씀)")]
        [SerializeField] private PolicyStance _stance;

        public string DisplayName => _displayName;
        public string Description => _description;
        public IReadOnlyList<PolicyModifier> Modifiers => _modifiers;
        public PolicyStance Stance => _stance;

        /// <summary>에디터 설정 메뉴용.</summary>
        public void Set(string displayName, string description, params PolicyModifier[] modifiers)
        {
            _displayName = displayName;
            _description = description;
            _modifiers = new List<PolicyModifier>(modifiers);
        }

        /// <summary>에디터 설정 메뉴 · 시험용.</summary>
        public void SetStance(PolicyStance stance) => _stance = stance;
    }
}
