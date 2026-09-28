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
        private readonly List<bool> _stopped = new List<bool>();

        /// <summary>틱 처리가 끝난 뒤 발생.</summary>
        public event Action Ticked;
        /// <summary>(자원, 고갈 여부). 스톡 자원이 0이 되거나 0에서 벗어날 때 발생.</summary>
        public event Action<ResourceType, bool> DepletionChanged;
        /// <summary>표시할 값(재고·한도·수지·인구)이 바뀔 수 있는 모든 시점에 발생 (UI 갱신용).</summary>
        public event Action Changed;

        public float PowerSupply { get; private set; }
        public float PowerDemand { get; private set; }
        /// <summary>전력 소비 모듈에 적용되는 효율 (최소 효율 ~ 1). 수요가 없으면 1.</summary>
        public float PowerEfficiency { get; private set; } = 1f;
        public int Population { get; private set; }
        public int HousingCapacity { get; private set; }
        /// <summary>입력 자원 부족으로 이번 틱에 정지한 모듈 수.</summary>
        public int StoppedModuleCount { get; private set; }

        public ResourceSimulation(BalanceConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

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

        /// <summary>인구를 0~수용 인구 범위로 변경한다 (2-3 임시 디버그용, 3-1에서 대체).</summary>
        public bool TryAdjustPopulation(int delta)
        {
            int next = Population + delta;
            if (next < 0 || next > HousingCapacity)
                return false;
            Population = next;
            Changed?.Invoke();
            return true;
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

        /// <summary>철거 환급: 건설 비용 × 환급률. 저장 한도를 넘는 분은 버린다.</summary>
        public void RefundBuildCost(IReadOnlyList<ResourceAmount> cost)
        {
            if (cost == null)
                return;
            float rate = _config.DemolishRefundRate;
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
            foreach (var m in activeModules)
            {
                housing += m.HousingCapacity;
                if (m.StorageBonus > 0f)
                {
                    for (int i = 0; i < ResourceCount; i++)
                    {
                        if (IsStock((ResourceType)i))
                            _capacity[i] += m.StorageBonus;
                    }
                }
            }
            HousingCapacity = housing;
            Changed?.Invoke();
        }

        public void Tick(IReadOnlyList<ModuleData> activeModules, float deltaSeconds)
        {
            RefreshCapacities(activeModules);

            // 입력 자원(전력 제외)이 이전 틱 재고 기준 0이면 정지. 정지한 모듈은 전력도 쓰지 않는다.
            _stopped.Clear();
            int stoppedCount = 0;
            float supply = 0f, demand = 0f;
            foreach (var m in activeModules)
            {
                bool stopped = HasDepletedInput(m);
                _stopped.Add(stopped);
                if (stopped)
                {
                    stoppedCount++;
                    continue;
                }
                supply += Sum(m.Production, ResourceType.Power);
                demand += Sum(m.Consumption, ResourceType.Power);
            }
            StoppedModuleCount = stoppedCount;
            PowerSupply = supply;
            PowerDemand = demand;
            PowerEfficiency = demand > 0f
                ? Mathf.Clamp(supply / demand, _config.MinPowerEfficiency, 1f)
                : 1f;

            Array.Clear(_production, 0, ResourceCount);
            Array.Clear(_consumption, 0, ResourceCount);
            for (int k = 0; k < activeModules.Count; k++)
            {
                if (_stopped[k])
                    continue;
                var m = activeModules[k];
                float efficiency = Sum(m.Consumption, ResourceType.Power) > 0f ? PowerEfficiency : 1f;
                foreach (var a in m.Production)
                {
                    if (IsStock(a.Type))
                        _production[(int)a.Type] += a.Amount * efficiency;
                }
                foreach (var a in m.Consumption)
                {
                    if (IsStock(a.Type))
                        _consumption[(int)a.Type] += a.Amount * efficiency;
                }
            }

            foreach (var a in _config.ConsumptionPerResident)
            {
                if (IsStock(a.Type))
                    _consumption[(int)a.Type] += a.Amount * Population;
            }

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

        private bool HasDepletedInput(ModuleData module)
        {
            foreach (var a in module.Consumption)
            {
                if (IsStock(a.Type) && a.Amount > 0f && _stock[(int)a.Type] <= 0f)
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
