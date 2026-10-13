using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-0b (StationSimulation에서 분리): 모듈 상태에서 나오는 파생값 — 활성 모듈 · 생산/소비 배율 수집, 가동률 · 효율,
    /// 실제 수용 인구, 수리 슬롯, 회전 링 증가 배율, 거주자 요구 → 만족도 상한.
    /// </summary>
    internal sealed class StationModuleStats
    {
        private readonly StationSimulation _sim;
        private readonly List<ModuleData> _activeModules = new List<ModuleData>();
        private readonly List<float> _productionMultipliers = new List<float>();
        private readonly List<float> _consumptionMultipliers = new List<float>();
        private readonly List<ResidentNeed> _activeNeeds = new List<ResidentNeed>();

        public StationModuleStats(StationSimulation sim) => _sim = sim;

        public IReadOnlyList<ModuleData> ActiveModules => _activeModules;
        public IReadOnlyList<float> ProductionMultipliers => _productionMultipliers;
        public IReadOnlyList<float> ConsumptionMultipliers => _consumptionMultipliers;
        public IReadOnlyList<ResidentNeed> ActiveNeeds => _activeNeeds;

        /// <summary>활성 모듈의 실제 수용 인구 (인접 가감 포함, 비활성 0). 4-9 주민 배분용.</summary>
        public float EffectiveHousing(ModuleInstance module)
        {
            if (module.Data == null || module.Data.HousingCapacity <= 0 || !_sim.Connectivity.IsActive(module))
                return 0f;
            int housing = _sim.Effects.Housing(module.Data); // 거주 연구 +N
            return housing + Math.Max(-housing, _sim.Adjacency.GetHousingBonus(module));
        }

        public void RefreshNeeds()
        {
            var progression = _sim.Progression;
            _activeNeeds.Clear();
            for (int i = 0; i <= progression.GradeIndex; i++)
            {
                foreach (var need in progression.GetGrade(i).NewNeeds)
                {
                    if (need != ResidentNeed.None && !_activeNeeds.Contains(need))
                        _activeNeeds.Add(need);
                }
            }
            _sim.Needs.Evaluate(_sim.Grid, _sim.Resources.Population, _activeNeeds);
            // Phase 10: 주민 특성 보정은 요구·연구를 반영한 상한에 더함 (0~100)
            float mood = _sim.Residents != null ? _sim.Residents.MoodTotal : 0f;
            float policy = _sim.Effects.Policy(PolicyStat.SatisfactionCap); // 12-3c 정책 · 사회 위기
            _sim.Population.SatisfactionCap = Math.Max(0f, Math.Min(PopulationSimulation.MaxSatisfaction, _sim.Needs.SatisfactionCap + mood + policy));
            _sim.Population.GrowthIntervalMultiplier = GrowthIntervalMultiplier() * _sim.Effects.Policy(PolicyStat.GrowthInterval);
        }

        /// <summary>
        /// 8-4 회전 링: 가동 중인(활성·정상) 모듈 중 가장 강한 인구 증가 간격 배율 (중첩 없음).
        /// 배율 = 1 − (1 − 값) × 가동률. 없으면 1.
        /// </summary>
        public float GrowthIntervalMultiplier()
        {
            float best = 1f;
            foreach (var m in _sim.Grid.Modules)
            {
                var data = m.Data;
                if (data == null || data.GrowthIntervalMultiplier >= 1f)
                    continue;
                float s = Strength(m);
                if (s <= 0f)
                    continue;
                float value = 1f - (1f - data.GrowthIntervalMultiplier) * s;
                if (value < best)
                    best = value;
            }
            return best;
        }

        /// <summary>
        /// 방어·서비스 모듈 가동률 (4-8, 4-9): 비활성·파손이면 0, 아니면 전력 효율(전력을 쓰는 경우) × 내구도 효율.
        /// </summary>
        public float Strength(ModuleInstance defender)
        {
            if (defender.Data == null || !_sim.Connectivity.IsActive(defender) || _sim.Damage.IsDamaged(defender) || _sim.IsHalted(defender))
                return 0f; // 12-3: 합성 중 재료도 가동 정지
            float power = 1f;
            foreach (var c in defender.Data.Consumption)
            {
                if (c.Type == ResourceType.Power && c.Amount > 0f)
                    power = _sim.Resources.PowerEfficiency;
            }
            return power * _sim.Durability.GetEfficiency(defender);
        }

        /// <summary>
        /// 7-1: 모듈 자체 효율 (선택 테두리·패널 색). 비활성 0.
        /// 방어·서비스 모듈은 가동률(파손 시 0), 나머지는 자원 틱과 같은 최종 생산 배율
        /// = 전력 효율(전력을 쓰는 경우) × 파손 × 내구도 × 인접 × 생산 연구.
        /// 낮/밤·태양 폭풍처럼 정거장 전체에 걸리는 배율은 모듈 탓이 아니므로 제외한다.
        /// </summary>
        public float Efficiency(ModuleInstance module)
        {
            if (module == null || module.Data == null || !_sim.Connectivity.IsActive(module))
                return 0f;
            if (module.Data.IsDefense || module.Data.IsService)
                return Strength(module);
            if (_sim.IsHalted(module))
                return 0f;
            float power = 1f;
            foreach (var c in module.Data.Consumption)
            {
                if (c.Type == ResourceType.Power && c.Amount > 0f)
                    power = _sim.Resources.PowerEfficiency;
            }
            return power * _sim.Damage.GetProductionMultiplier(module) * _sim.Durability.GetEfficiency(module) * _sim.Incidents.ProductionMultiplier(module)
                   * _sim.Adjacency.GetProductionMultiplier(module) * _sim.Effects.ProductionMultiplier(module.Data);
        }

        public void RefreshCapacities()
        {
            CollectActiveModules();
            _sim.Resources.RefreshCapacities(_activeModules);
        }

        public void CollectActiveModules()
        {
            _activeModules.Clear();
            _productionMultipliers.Clear();
            _consumptionMultipliers.Clear();
            int extraHousing = 0;
            int labSlots = 0;
            foreach (var module in _sim.Grid.Modules)
            {
                if (module.Data == null || !_sim.Connectivity.IsActive(module))
                    continue;
                _activeModules.Add(module.Data);
                bool halted = _sim.IsHalted(module); // 12-3 합성 중: 수리 중처럼 생산 0 (소비 · 수용 · 저장은 유지)
                // 파손 배율 × 내구도 효율 × 인접 효과 × 생산 연구 (BALANCE 1번: 곱셈)
                _productionMultipliers.Add(halted ? 0f : _sim.Damage.GetProductionMultiplier(module) * _sim.Durability.GetEfficiency(module) * _sim.Incidents.ProductionMultiplier(module)
                                           * _sim.Adjacency.GetProductionMultiplier(module) * _sim.Effects.ProductionMultiplier(module.Data));
                if (module.Data.ResearchSlots > 0 && !_sim.Damage.IsDamaged(module) && !halted)
                    labSlots += module.Data.ResearchSlots; // 연결되고 파손되지 않은 연구소만
                _consumptionMultipliers.Add(_sim.Adjacency.GetConsumptionMultiplier(module));
                // 인접 수용 인구 가감 (모듈 자체 수용 인구 아래로는 내려가지 않음)
                extraHousing += Math.Max(-module.Data.HousingCapacity, _sim.Adjacency.GetHousingBonus(module));
            }
            _sim.Resources.ExtraHousing = extraHousing;
            _sim.Research.LabSlots = labSlots;
            RefreshRepairCapacity(); // 연결·파손 상태 반영 (4-6)
        }

        /// <summary>동시 수리 슬롯 = 기본(코어) + 활성이고 파손되지 않은 정비 베이의 슬롯 합 (BALANCE 17번).</summary>
        public int CountRepairSlots()
        {
            int slots = _sim.Balance.BaseRepairSlots;
            foreach (var module in _sim.Grid.Modules)
            {
                if (module.Data == null || module.Data.RepairSlots <= 0)
                    continue;
                if (_sim.Connectivity.IsActive(module) && !_sim.Damage.IsDamaged(module) && !_sim.IsHalted(module))
                    slots += module.Data.RepairSlots;
            }
            return slots;
        }

        public void RefreshRepairCapacity() => _sim.Damage.RepairCapacity = CountRepairSlots();
    }
}
