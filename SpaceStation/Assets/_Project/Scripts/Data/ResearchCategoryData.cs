using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>연구 카테고리 (RESEARCH.md 3번).</summary>
    public enum ResearchCategory
    {
        Maintenance,
        Defense,
        Production,
        Energy,
        Habitation,
        Construction,
    }

    /// <summary>
    /// 연구가 바꾸는 값. 레벨 표의 값은 "그 레벨에서의 최종값"이다 (예: 수리 비용 비율 0.2 = 20%).
    /// 같은 값을 여러 레벨이 정하면 완료한 가장 높은 레벨의 값을 쓴다. 정하지 않으면 기본값(밸런스 에셋).
    /// </summary>
    public enum ResearchStat
    {
        // 유지보수
        RepairCostRate,          // 기본 BalanceConfig.repairCostRate
        RepairDuration,          // 기본 repairDuration (초)
        DecayMultiplier,         // 노후 속도 배율, 기본 1
        DurabilityEfficiencyFloor, // 노후 효율 최저값, 기본 0
        RebuildCostMultiplier,   // 재건축 순비용 배율, 기본 1
        // 방어
        DefenseRadiusBonus,      // 실드·포탑 반경 +칸, 기본 0
        HitImmunityChance,       // 운석 명중 시 파손 면역 확률, 기본 0
        RicochetChance,          // 실드 튕김 명중 확률, 기본 shieldRicochetChance
        TurretInterceptMultiplier, // 포탑 격추 확률 배율, 기본 1
        TurretMaxIntercept,      // 격추 합산 상한, 기본 turretMaxIntercept
        // 생산
        LifeSupportProductionMultiplier, // 산소·물·식량 생산 배율, 기본 1
        AdjacencyBonusBoost,     // 좋은 인접 효과 +%p (0.1 = +10%p), 기본 0
        MiningBonus,             // 채굴 도킹 금속 생산 +N, 기본 0
        // 에너지
        MinPowerEfficiency,      // 전력 최소 효율, 기본 minPowerEfficiency
        SolarMultiplier,         // 태양광 발전 배율, 기본 1
        BatteryMultiplier,       // 배터리 저장량 배율, 기본 1
        // 거주
        SatisfactionCapBonus,    // 만족도 상한 +N, 기본 0
        ServiceRadiusBonus,      // 의료·휴게 반경 +칸, 기본 0
        HousingBonus,            // 거주 모듈 수용 인원 +N, 기본 0
        // 건설·경제
        BuildCostMultiplier,     // 건설 비용 배율, 기본 1
        DemolishRefundRate,      // 철거 환급률, 기본 demolishRefundRate
        StorageBonusAdd,         // 창고 저장 한도 증가량 +N, 기본 0
    }

    [Serializable]
    public struct ResearchModifier
    {
        public ResearchStat Stat;
        public float Value;
    }

    [Serializable]
    public sealed class ResearchLevel
    {
        [Tooltip("연구 시작 시 1회 차감 (환급 없음)")]
        public List<ResourceAmount> StartCost = new List<ResourceAmount>();
        [Tooltip("연구 중 전력 수요 추가")]
        [Min(0f)] public float PowerDemand = 5f;
        [Tooltip("전력 100% 기준 소요 시간(초)")]
        [Min(1f)] public float Duration = 60f;
        [TextArea(1, 3)] public string Description;
        public List<ResearchModifier> Modifiers = new List<ResearchModifier>();
    }

    /// <summary>연구 카테고리 하나 (레벨 1~4). RESEARCH.md 2·3번 표.</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Research Category", fileName = "RC_New")]
    public sealed class ResearchCategoryData : ScriptableObject
    {
        [SerializeField] private ResearchCategory _category;
        [SerializeField] private string _displayName = "연구";
        [Tooltip("HUD 아이콘 이름 (HUD_Icons 스프라이트)")]
        [SerializeField] private string _icon = "event";
        [Tooltip("레벨 1부터 순서대로")]
        [SerializeField] private List<ResearchLevel> _levels = new List<ResearchLevel>();

        public ResearchCategory Category => _category;
        public string DisplayName => _displayName;
        public string Icon => _icon;
        public IReadOnlyList<ResearchLevel> Levels => _levels;
        public int MaxLevel => _levels.Count;

        /// <summary>레벨(1부터)의 정의. 없으면 null.</summary>
        public ResearchLevel GetLevel(int level) => level >= 1 && level <= _levels.Count ? _levels[level - 1] : null;

#if UNITY_EDITOR
        public void EditorSet(ResearchCategory category, string displayName, string icon, List<ResearchLevel> levels)
        {
            _category = category;
            _displayName = displayName;
            _icon = icon;
            _levels = levels;
        }
#endif
    }
}
