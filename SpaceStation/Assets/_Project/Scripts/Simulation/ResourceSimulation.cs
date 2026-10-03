using System;
using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 자원 수급 계산 (BALANCE.md 5번). 틱마다 활성 모듈 목록을 받아 처리한다.
    /// 순서: 저장/수용 한도 갱신 → 전력 공급·수요 → 효율 → 자원별 생산·소비 집계 → 0~한도로 clamp 후 반영.
    /// 전력은 저장하지 않는 플로우, 나머지는 스톡.
    /// </summary>
    public sealed class ResourceSimulation
    {
        public static readonly int ResourceCount = Enum.GetValues(typeof(ResourceType)).Length;

        private readonly BalanceConfig _config;
        private readonly float[] _stock = new float[ResourceCount];
        private readonly float[] _capacity = new float[ResourceCount];
        private readonly float[] _baseCapacity = new float[ResourceCount];
        private readonly float[] _production = new float[ResourceCount];
        private readonly float[] _consumption = new float[ResourceCount];
        private readonly bool[] _depleted = new bool[ResourceCount];
        private readonly float[] _externalDrain = new float[ResourceCount];
        private readonly List<bool> _stopped = new List<bool>();
        private float _powerSupplyMultiplier = 1f;

        /// <summary>틱 처리가 끝난 뒤 발생.</summary>
        public event Action Ticked;
        /// <summary>(자원, 고갈 여부). 스톡 자원이 0이 되거나 0에서 벗어날 때 발생.</summary>
        public event Action<ResourceType, bool> DepletionChanged;
        /// <summary>표시할 값(재고·한도·수지·인구)이 바뀔 수 있는 모든 시점에 발생 (UI 갱신용).</summary>
        public event Action Changed;

        /// <summary>발전량 (배터리 방전 제외, 낮/밤·폭풍·파손 반영).</summary>
        public float PowerSupply { get; private set; }
        public float PowerDemand { get; private set; }
        /// <summary>모듈 외 수용 인구 가감 (인접 효과 합계). RefreshCapacities에서 반영.</summary>
        public int ExtraHousing { get; set; }
        /// <summary>태양광(SolarPowered) 모듈 출력 배율 — 낮/밤 주기. 다음 틱부터 반영.</summary>
        public float SolarMultiplier { get; set; } = 1f;
        public float BatteryCharge { get; private set; }
        public float BatteryCapacity { get; private set; }
        /// <summary>활성 배터리 충·방전 최대 속도 합 (초당).</summary>
        public float BatteryRate { get; private set; }
        /// <summary>마지막 틱의 배터리 흐름 (초당, +충전 / −방전).</summary>
        public float BatteryFlow { get; private set; }
        /// <summary>전력 소비 모듈에 적용되는 효율 (최소 효율 ~ 1). 수요가 없으면 1.</summary>
        public float PowerEfficiency { get; private set; } = 1f;
        public int Population { get; private set; }
        public int HousingCapacity { get; private set; }
        /// <summary>입력 자원 부족으로 이번 틱에 정지한 모듈 수.</summary>
        public int StoppedModuleCount { get; private set; }

        /// <summary>모든 전력 생산에 곱하는 전역 배율 (태양 폭풍). 다음 틱부터 반영.</summary>
        public float PowerSupplyMultiplier
        {
            get => _powerSupplyMultiplier;
            set
            {
                _powerSupplyMultiplier = Mathf.Max(0f, value);
                Changed?.Invoke();
            }
        }

        /// <summary>Phase 6 연구 효과 (최소 전력 효율, 태양광·배터리·창고·거주·환급). StationSimulation이 공유 인스턴스로 바꿔 끼운다.</summary>
        public ResearchEffects Effects { get; set; }

        /// <summary>모듈 외 전력 수요 (진행 중인 연구, Phase 6). 다음 틱부터 반영.</summary>
        public float ExtraPowerDemand { get; set; }

        /// <summary>모듈과 무관한 초당 추가 소비 (파손 모듈 누출 등). 다음 틱부터 반영.</summary>
        public void SetExternalDrain(ResourceType type, float perSecond)
        {
            _externalDrain[(int)type] = Mathf.Max(0f, perSecond);
        }

        public float GetExternalDrain(ResourceType type) => _externalDrain[(int)type];

        /// <summary>재고 추가 (보급 등). 한도 초과분은 버린다. 실제로 더해진 양을 반환.</summary>
        public float AddStock(ResourceType type, float amount)
        {
            if (!IsStock(type) || amount <= 0f)
                return 0f;
            int i = (int)type;
            float before = _stock[i];
            _stock[i] = Mathf.Min(_capacity[i], before + amount);
            UpdateDepletion();
            Changed?.Invoke();
            return _stock[i] - before;
        }

        /// <summary>재고 차감 (누출 등). 0 미만으로 내려가지 않는다. 실제로 빠진 양을 반환.</summary>
        public float RemoveStock(ResourceType type, float amount)
        {
            if (!IsStock(type) || amount <= 0f)
                return 0f;
            int i = (int)type;
            float before = _stock[i];
            _stock[i] = Mathf.Max(0f, before - amount);
            UpdateDepletion();
            Changed?.Invoke();
            return before - _stock[i];
        }

        public ResourceSimulation(BalanceConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Effects = new ResearchEffects(config);

            foreach (var a in config.BaseStorageCapacity)
                _baseCapacity[(int)a.Type] += a.Amount;
            Array.Copy(_baseCapacity, _capacity, ResourceCount);

            foreach (var a in config.StartingResources)
            {
                int i = (int)a.Type;
                if (IsStock(a.Type))
                    _stock[i] = Mathf.Clamp(_stock[i] + a.Amount, 0f, _capacity[i]);
            }
            for (int i = 0; i < ResourceCount; i++)
                _depleted[i] = IsStock((ResourceType)i) && _stock[i] <= 0f;

            Population = config.StartingPopulation;
        }

        public static bool IsStock(ResourceType type)
        {
            return type != ResourceType.Power;
        }

        public float GetStock(ResourceType type) => _stock[(int)type];
        public float GetCapacity(ResourceType type) => _capacity[(int)type];
        /// <summary>마지막 틱의 초당 생산량. 전력은 공급량.</summary>
        public float GetProduction(ResourceType type) => _production[(int)type];
        /// <summary>마지막 틱의 초당 소비량. 전력은 수요량.</summary>
        public float GetConsumption(ResourceType type) => _consumption[(int)type];
        public float GetNetRate(ResourceType type) => _production[(int)type] - _consumption[(int)type];
        public bool IsDepleted(ResourceType type) => _depleted[(int)type];

        /// <summary>
        /// 인구 설정 (0 이상). 증감 규칙은 <see cref="PopulationSimulation"/>이 판단하며,
        /// 수용 인구 초과도 허용한다 (거주 모듈 철거 직후 등).
        /// </summary>
        public void SetPopulation(int population)
        {
            population = Mathf.Max(0, population);
            if (population == Population)
                return;
            Population = population;
            Changed?.Invoke();
        }

        /// <summary>세이브 복원: 배터리 충전량 (현재 용량까지).</summary>
        internal void RestoreBattery(float charge)
        {
            BatteryCharge = Mathf.Clamp(charge, 0f, BatteryCapacity);
        }

        /// <summary>현재 재고를 직접 설정 (디버그/테스트용). 0~한도로 clamp.</summary>
        public void SetStock(ResourceType type, float amount)
        {
            if (!IsStock(type))
                return;
            int i = (int)type;
            _stock[i] = Mathf.Clamp(amount, 0f, _capacity[i]);
            UpdateDepletion();
            Changed?.Invoke();
        }

        /// <summary>비용 전체를 현재 재고로 낼 수 있는지.</summary>
        public bool CanAfford(IReadOnlyList<ResourceAmount> cost)
        {
            if (cost == null)
                return true;
            // 같은 자원이 여러 줄로 나뉘어 있어도 합산해 판정
            for (int i = 0; i < ResourceCount; i++)
            {
                float required = 0f;
                foreach (var a in cost)
                {
                    if ((int)a.Type == i)
                        required += a.Amount;
                }
                if (required > 0f && (!IsStock((ResourceType)i) || _stock[i] < required))
                    return false;
            }
            return true;
        }

        /// <summary>비용을 차감한다. 하나라도 부족하면 아무것도 차감하지 않고 false.</summary>
        public bool TrySpend(IReadOnlyList<ResourceAmount> cost)
        {
            if (!CanAfford(cost))
                return false;
            if (cost == null)
                return true;
            foreach (var a in cost)
            {
                if (IsStock(a.Type))
                    _stock[(int)a.Type] -= a.Amount;
            }
            UpdateDepletion();
            Changed?.Invoke();
            return true;
        }

        /// <summary>철거 환급: 건설 비용 × 환급률 × multiplier(내구도 비율 등). 저장 한도를 넘는 분은 버린다.</summary>
        public void RefundBuildCost(IReadOnlyList<ResourceAmount> cost, float multiplier = 1f)
        {
            if (cost == null)
                return;
            float rate = Effects.DemolishRefundRate * Mathf.Max(0f, multiplier);
            foreach (var a in cost)
            {
                if (!IsStock(a.Type))
                    continue;
                int i = (int)a.Type;
                _stock[i] = Mathf.Clamp(_stock[i] + a.Amount * rate, 0f, _capacity[i]);
            }
            UpdateDepletion();
            Changed?.Invoke();
        }

        /// <summary>저장 한도와 수용 인구만 다시 계산한다 (틱 사이 건설 직후 조회용).</summary>
        public void RefreshCapacities(IReadOnlyList<ModuleData> activeModules)
        {
            Array.Copy(_baseCapacity, _capacity, ResourceCount);
            int housing = 0;
            float batteryCapacity = 0f, batteryRate = 0f;
            foreach (var m in activeModules)
            {
                // Phase 6 연구: 거주 +N, 배터리 배율, 창고 증가량 +N
                housing += Effects.Housing(m);
                batteryCapacity += Effects.BatteryCapacity(m);
                batteryRate += m.BatteryRate;
                float storage = Effects.StorageBonus(m);
                if (storage > 0f)
                {
                    for (int i = 0; i < ResourceCount; i++)
                    {
                        if (IsStock((ResourceType)i))
                            _capacity[i] += storage;
                    }
                }
            }
            HousingCapacity = Math.Max(0, housing + ExtraHousing);
            BatteryCapacity = batteryCapacity;
            BatteryRate = batteryRate;
            if (BatteryCharge > BatteryCapacity)
                BatteryCharge = BatteryCapacity; // 배터리 철거·분리 시 초과분 손실
            Changed?.Invoke();
        }

        public void Tick(IReadOnlyList<ModuleData> activeModules, float deltaSeconds)
        {
            Tick(activeModules, null, deltaSeconds);
        }

        /// <param name="productionMultipliers">
        /// 모듈별 생산 배율 (파손 0.5, 수리 중 0 등). activeModules와 같은 순서·길이. null이면 모두 1.
        /// 전력 공급을 포함한 생산에만 곱하고, 소비는 유지한다.
        /// </param>
        public void Tick(IReadOnlyList<ModuleData> activeModules, IReadOnlyList<float> productionMultipliers, float deltaSeconds)
        {
            Tick(activeModules, productionMultipliers, null, deltaSeconds);
        }

        /// <param name="consumptionMultipliers">모듈별 전력 외 입력 소비 배율 (인접 효과 등). null이면 모두 1.</param>
        public void Tick(IReadOnlyList<ModuleData> activeModules, IReadOnlyList<float> productionMultipliers,
            IReadOnlyList<float> consumptionMultipliers, float deltaSeconds)
        {
            RefreshCapacities(activeModules);

            // 입력 자원(전력 제외)이 이전 틱 재고 기준 0이면 정지. 정지한 모듈은 전력도 쓰지 않는다.
            _stopped.Clear();
            int stoppedCount = 0;
            float supply = 0f, demand = 0f, onDemandMax = 0f;
            for (int k = 0; k < activeModules.Count; k++)
            {
                var m = activeModules[k];
                bool stopped = HasDepletedInput(m);
                _stopped.Add(stopped);
                if (stopped)
                {
                    stoppedCount++;
                    continue;
                }
                demand += Sum(m.Consumption, ResourceType.Power);
                if (m.OnDemandPower) // 8-5 보조 발전: 최대 출력만 모아 두고 부족분이 있을 때만 쓴다
                {
                    onDemandMax += Sum(m.Production, ResourceType.Power) * Multiplier(productionMultipliers, k);
                    continue;
                }
                float solar = m.SolarPowered ? SolarMultiplier * Effects.SolarMultiplier : 1f; // 낮/밤 (4-2) × 연구
                supply += Sum(m.Production, ResourceType.Power) * Multiplier(productionMultipliers, k) * solar;
            }
            demand += Mathf.Max(0f, ExtraPowerDemand); // 진행 중인 연구 (Phase 6)
            supply *= PowerSupplyMultiplier; // 태양 폭풍 등 전역 배율
            onDemandMax *= PowerSupplyMultiplier;
            StoppedModuleCount = stoppedCount;
            PowerDemand = demand;

            // 배터리 (4-2): 남는 전력은 충전, 모자라면 방전. 둘 다 초당 BatteryRate까지.
            float effectiveSupply = supply;
            float net = supply - demand;
            float dt = Mathf.Max(deltaSeconds, 1e-6f);
            if (net >= 0f)
            {
                float charge = Mathf.Min(net, BatteryRate, (BatteryCapacity - BatteryCharge) / dt);
                charge = Mathf.Max(0f, charge);
                BatteryCharge += charge * deltaSeconds;
                BatteryFlow = charge;
            }
            else
            {
                float discharge = Mathf.Min(-net, BatteryRate, BatteryCharge / dt);
                discharge = Mathf.Max(0f, discharge);
                BatteryCharge -= discharge * deltaSeconds;
                effectiveSupply += discharge;
                BatteryFlow = -discharge;
            }
            BatteryCharge = Mathf.Clamp(BatteryCharge, 0f, BatteryCapacity);

            // 8-5 보조 발전: 다른 발전 + 배터리 방전으로도 모자란 만큼만 (최대 출력까지)
            float onDemandUsed = 0f;
            if (onDemandMax > 0f && effectiveSupply < demand)
            {
                onDemandUsed = Mathf.Min(demand - effectiveSupply, onDemandMax);
                effectiveSupply += onDemandUsed;
                supply += onDemandUsed;
            }
            OnDemandCapacity = onDemandMax;
            OnDemandLoad = onDemandMax > 0f ? onDemandUsed / onDemandMax : 0f;
            PowerSupply = supply;

            PowerEfficiency = demand > 0f
                ? Mathf.Clamp(effectiveSupply / demand, Effects.MinPowerEfficiency, 1f)
                : 1f;

            Array.Clear(_production, 0, ResourceCount);
            Array.Clear(_consumption, 0, ResourceCount);
            for (int k = 0; k < activeModules.Count; k++)
            {
                if (_stopped[k])
                    continue;
                var m = activeModules[k];
                float efficiency = Sum(m.Consumption, ResourceType.Power) > 0f ? PowerEfficiency : 1f;
                if (m.OnDemandPower)
                    efficiency *= OnDemandLoad; // 보조 발전: 가동 비율만큼만 입력 소비
                float productionFactor = efficiency * Multiplier(productionMultipliers, k); // BALANCE 1번: 파손 배율과 곱셈
                foreach (var a in m.Production)
                {
                    if (IsStock(a.Type))
                        _production[(int)a.Type] += a.Amount * productionFactor;
                }
                float consumptionFactor = efficiency * Multiplier(consumptionMultipliers, k); // 인접 효과 (4-4)
                foreach (var a in m.Consumption)
                {
                    if (IsStock(a.Type))
                        _consumption[(int)a.Type] += a.Amount * consumptionFactor;
                }
            }

            foreach (var a in _config.ConsumptionPerResident)
            {
                if (IsStock(a.Type))
                    _consumption[(int)a.Type] += a.Amount * Population;
            }

            for (int i = 0; i < ResourceCount; i++)
                _consumption[i] += _externalDrain[i]; // 파손 모듈 산소 누출 등

            _production[(int)ResourceType.Power] = supply;
            _consumption[(int)ResourceType.Power] = demand;

            for (int i = 0; i < ResourceCount; i++)
            {
                if (!IsStock((ResourceType)i))
                    continue;
                float next = _stock[i] + (_production[i] - _consumption[i]) * deltaSeconds;
                _stock[i] = Mathf.Clamp(next, 0f, _capacity[i]); // 한도 초과분은 버림, 0 미만은 0
            }

            UpdateDepletion();
            Ticked?.Invoke();
            Changed?.Invoke();
        }

        private static float Multiplier(IReadOnlyList<float> multipliers, int index)
        {
            return multipliers != null && index < multipliers.Count ? multipliers[index] : 1f;
        }

        private bool HasDepletedInput(ModuleData module)
        {
            foreach (var a in module.Consumption)
            {
                if (!IsStock(a.Type) || a.Amount <= 0f)
                    continue;
                int i = (int)a.Type;
                // 8-5: 입력 보호 비율 (연료전지: 물 20% 이하면 정지해 주민 몫을 남김)
                if (_stock[i] <= 0f || (module.InputReserveRatio > 0f && _stock[i] <= _capacity[i] * module.InputReserveRatio))
                    return true;
            }
            return false;
        }

        /// <summary>8-5 보조 발전(연료전지)이 지금 낼 수 있는 최대 출력 (정지·폭풍 반영).</summary>
        public float OnDemandCapacity { get; private set; }
        /// <summary>8-5 보조 발전 가동 비율 0~1 (부족분 ÷ 최대 출력). 입력 소비도 이 비율.</summary>
        public float OnDemandLoad { get; private set; }

        /// <summary>이 모듈이 입력 보호 비율 때문에 멈춰 있는지 (바닥나서 멈춘 것은 제외, UI용).</summary>
        public bool IsHeldByReserve(ModuleData module)
        {
            if (module == null || module.InputReserveRatio <= 0f)
                return false;
            foreach (var a in module.Consumption)
            {
                if (!IsStock(a.Type) || a.Amount <= 0f)
                    continue;
                int i = (int)a.Type;
                if (_stock[i] > 0f && _stock[i] <= _capacity[i] * module.InputReserveRatio)
                    return true;
            }
            return false;
        }

        private void UpdateDepletion()
        {
            for (int i = 0; i < ResourceCount; i++)
            {
                var type = (ResourceType)i;
                if (!IsStock(type))
                    continue;
                bool depleted = _stock[i] <= 0f;
                if (depleted == _depleted[i])
                    continue;
                _depleted[i] = depleted;
                DepletionChanged?.Invoke(type, depleted);
            }
        }

        private static float Sum(IReadOnlyList<ResourceAmount> list, ResourceType type)
        {
            float total = 0f;
            foreach (var a in list)
            {
                if (a.Type == type)
                    total += a.Amount;
            }
            return total;
        }
    }
}
