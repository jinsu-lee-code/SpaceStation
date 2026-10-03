using System;
using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// Phase 6 연구 효과의 현재값. 각 시스템은 밸런스 값 대신 여기서 읽는다 (RESEARCH.md 6번 "조회 지점").
    /// 연구가 하나도 없으면 모든 값이 밸런스 에셋과 같아 기존 결과가 그대로다.
    /// 값은 완료한 레벨 표에서 다시 계산된다 (<see cref="Recalculate"/>).
    /// </summary>
    public sealed class ResearchEffects
    {
        private readonly BalanceConfig _config;
        private readonly Dictionary<ResearchStat, float> _values = new Dictionary<ResearchStat, float>();

        /// <summary>값이 바뀐 뒤 (인접·용량 등 캐시를 다시 계산해야 하는 쪽이 구독).</summary>
        public event Action Changed;

        public ResearchEffects(BalanceConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public float RepairCostRate => Get(ResearchStat.RepairCostRate, _config.RepairCostRate);
        public float RepairDuration => Math.Max(0.1f, Get(ResearchStat.RepairDuration, _config.RepairDuration));
        public float DecayMultiplier => Get(ResearchStat.DecayMultiplier, 1f);
        public float DurabilityEfficiencyFloor => Get(ResearchStat.DurabilityEfficiencyFloor, 0f);
        public float RebuildCostMultiplier => Get(ResearchStat.RebuildCostMultiplier, 1f);
        public int DefenseRadiusBonus => (int)Math.Round(Get(ResearchStat.DefenseRadiusBonus, 0f));
        public float HitImmunityChance => Get(ResearchStat.HitImmunityChance, 0f);
        public float RicochetChance => Get(ResearchStat.RicochetChance, _config.ShieldRicochetChance);
        public float TurretInterceptMultiplier => Get(ResearchStat.TurretInterceptMultiplier, 1f);
        public float TurretMaxIntercept => Get(ResearchStat.TurretMaxIntercept, _config.TurretMaxIntercept);
        /// <summary>운석·태양 폭풍을 몇 초 전에 알리는지 (0 = 경보 없음).</summary>
        public float EarlyWarningSeconds => Math.Max(0f, Get(ResearchStat.EarlyWarningSeconds, 0f));
        /// <summary>경보 중인 운석의 대상 모듈을 미리 정해 보여주는지.</summary>
        public bool MeteorTargetPreview => Get(ResearchStat.MeteorTargetPreview, 0f) > 0.5f;
        public float LifeSupportProductionMultiplier => Get(ResearchStat.LifeSupportProductionMultiplier, 1f);
        public float AdjacencyBonusBoost => Get(ResearchStat.AdjacencyBonusBoost, 0f);
        public float MiningBonus => Get(ResearchStat.MiningBonus, 0f);
        public float MinPowerEfficiency => Get(ResearchStat.MinPowerEfficiency, _config.MinPowerEfficiency);
        public float SolarMultiplier => Get(ResearchStat.SolarMultiplier, 1f);
        public float BatteryMultiplier => Get(ResearchStat.BatteryMultiplier, 1f);
        public float SatisfactionCapBonus => Get(ResearchStat.SatisfactionCapBonus, 0f);
        public int ServiceRadiusBonus => (int)Math.Round(Get(ResearchStat.ServiceRadiusBonus, 0f));
        public int HousingBonus => (int)Math.Round(Get(ResearchStat.HousingBonus, 0f));
        public float BuildCostMultiplier => Get(ResearchStat.BuildCostMultiplier, 1f);
        public float DemolishRefundRate => Get(ResearchStat.DemolishRefundRate, _config.DemolishRefundRate);
        public float StorageBonusAdd => Get(ResearchStat.StorageBonusAdd, 0f);
        /// <summary>자동 정비·재건축·일괄 정비 개방 (자동화 연구).</summary>
        public bool MaintenanceAutomation => Get(ResearchStat.MaintenanceAutomation, 0f) > 0.5f;

        // ---------------- 모듈별 값 (UI·연출도 이걸 쓴다) ----------------

        public int ShieldRadius(ModuleData data) => data != null && data.ShieldRadius > 0 ? data.ShieldRadius + DefenseRadiusBonus : 0;
        public int TurretRadius(ModuleData data) => data != null && data.TurretRadius > 0 ? data.TurretRadius + DefenseRadiusBonus : 0;
        public int DecoyRadius(ModuleData data) => data != null && data.DecoyRadius > 0 ? data.DecoyRadius + DefenseRadiusBonus : 0;
        public int ControlRadius(ModuleData data) => data != null && data.ControlRadius > 0 ? data.ControlRadius + DefenseRadiusBonus : 0;
        public int ServiceRadius(ModuleData data) => data != null && data.IsService ? data.ServiceRadius + ServiceRadiusBonus : 0;
        public int Housing(ModuleData data) => data != null && data.HousingCapacity > 0 ? data.HousingCapacity + HousingBonus : 0;
        public float BatteryCapacity(ModuleData data) => data != null ? data.BatteryCapacity * BatteryMultiplier : 0f;
        public float StorageBonus(ModuleData data) => data != null && data.StorageBonus > 0f ? data.StorageBonus + StorageBonusAdd : 0f;

        /// <summary>건설 비용 (연구 할인 반영).</summary>
        public List<ResourceAmount> BuildCost(ModuleData data)
        {
            var cost = new List<ResourceAmount>();
            if (data == null)
                return cost;
            float m = BuildCostMultiplier;
            foreach (var a in data.BuildCost)
                cost.Add(new ResourceAmount(a.Type, a.Amount * m));
            return cost;
        }

        /// <summary>
        /// 모듈 생산 배율: 산소·물·식량을 만드는 모듈은 생산 연구 배율, 채굴(금속 생산) 모듈은 +N을 배율로 환산.
        /// </summary>
        public float ProductionMultiplier(ModuleData data)
        {
            if (data == null)
                return 1f;
            float metal = 0f;
            bool lifeSupport = false;
            foreach (var p in data.Production)
            {
                if (p.Type == ResourceType.Oxygen || p.Type == ResourceType.Water || p.Type == ResourceType.Food)
                    lifeSupport = true;
                else if (p.Type == ResourceType.Metal)
                    metal += p.Amount;
            }
            float m = lifeSupport ? LifeSupportProductionMultiplier : 1f;
            if (metal > 0f && MiningBonus > 0f)
                m *= (metal + MiningBonus) / metal;
            return m;
        }

        // ---------------- 계산 ----------------

        public float Get(ResearchStat stat, float fallback) => _values.TryGetValue(stat, out var v) ? v : fallback;

        /// <summary>카테고리별 완료 레벨로 다시 계산. 같은 값은 각 카테고리에서 완료한 가장 높은 레벨의 값.</summary>
        public void Recalculate(IReadOnlyList<ResearchCategoryData> categories, Func<ResearchCategoryData, int> levelOf)
        {
            _values.Clear();
            if (categories != null)
            {
                foreach (var category in categories)
                {
                    if (category == null)
                        continue;
                    int level = levelOf(category);
                    for (int l = 1; l <= level; l++)
                    {
                        var def = category.GetLevel(l);
                        if (def == null)
                            continue;
                        foreach (var m in def.Modifiers)
                            _values[m.Stat] = m.Value; // 높은 레벨이 덮어씀
                    }
                }
            }
            Changed?.Invoke();
        }
    }
}
