using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>요구 하나의 충족 현황.</summary>
    public sealed class NeedStatus
    {
        public ResidentNeed Need { get; internal set; }
        /// <summary>요구가 있는 주민 수 (= 인구).</summary>
        public float Demand { get; internal set; }
        public float Served { get; internal set; }
        public float Ratio => Demand <= 0f ? 1f : Mathf.Clamp01(Served / Demand);
    }

    /// <summary>
    /// 거주자 요구 (4-9, BALANCE 20번). 순수 C#.
    /// 주민은 활성 거주 모듈(수용 인구 > 0, 코어 포함)에 수용 인구 비율대로 산다고 보고,
    /// 서비스 모듈은 반경(체비셰프) 안 거주 모듈의 주민을 담당 인원 × 가동률까지 맡는다 (앞에서부터 배정).
    /// 만족도 상한 = 100 − Σ 요구별 (1 − 충족 비율) × 상한 감소량.
    /// </summary>
    public sealed class NeedsSystem
    {
        private readonly BalanceConfig _config;
        private readonly Func<ModuleInstance, float> _strength;
        private readonly Func<ModuleInstance, float> _housing;
        private readonly List<NeedStatus> _statuses = new List<NeedStatus>();
        private readonly List<ModuleInstance> _habitats = new List<ModuleInstance>();
        private readonly List<float> _remaining = new List<float>();
        private readonly Dictionary<ModuleInstance, float> _serviceUsed = new Dictionary<ModuleInstance, float>();
        // (거주 모듈, 요구) → 충족 비율
        private readonly Dictionary<ModuleInstance, Dictionary<ResidentNeed, float>> _habitatCoverage = new Dictionary<ModuleInstance, Dictionary<ResidentNeed, float>>();

        /// <param name="strength">서비스 모듈 가동률 (0~1)</param>
        /// <param name="housing">모듈의 실제 수용 인구 (비활성이면 0)</param>
        public NeedsSystem(BalanceConfig config, Func<ModuleInstance, float> strength, Func<ModuleInstance, float> housing)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _strength = strength ?? (_ => 1f);
            _housing = housing ?? (m => m.Data != null ? m.Data.HousingCapacity : 0);
        }

        /// <summary>현재 등급까지 생긴 요구들의 충족 현황.</summary>
        public IReadOnlyList<NeedStatus> Statuses => _statuses;
        public float SatisfactionCap { get; private set; } = PopulationSimulation.MaxSatisfaction;

        /// <summary>서비스 모듈이 지금 맡은 주민 수 (UI).</summary>
        public float GetServed(ModuleInstance service) => _serviceUsed.TryGetValue(service, out var v) ? v : 0f;

        /// <summary>거주 모듈 주민의 요구 충족 비율 (요구가 없거나 거주 모듈이 아니면 −1).</summary>
        public float GetHabitatCoverage(ModuleInstance habitat, ResidentNeed need)
            => _habitatCoverage.TryGetValue(habitat, out var d) && d.TryGetValue(need, out var v) ? v : -1f;

        public float GetStrength(ModuleInstance service) => Mathf.Clamp01(_strength(service));

        public void Evaluate(StationGrid grid, int population, IReadOnlyList<ResidentNeed> activeNeeds)
        {
            _statuses.Clear();
            _serviceUsed.Clear();
            _habitatCoverage.Clear();
            _habitats.Clear();

            float totalHousing = 0f;
            foreach (var m in grid.Modules)
            {
                float h = _housing(m);
                if (h <= 0f)
                    continue;
                _habitats.Add(m);
                totalHousing += h;
            }

            float cap = PopulationSimulation.MaxSatisfaction;
            foreach (var need in activeNeeds)
            {
                if (need == ResidentNeed.None)
                    continue;
                var status = new NeedStatus { Need = need, Demand = population };
                _remaining.Clear();
                foreach (var h in _habitats)
                    _remaining.Add(totalHousing > 0f ? population * _housing(h) / totalHousing : 0f);

                foreach (var s in grid.Modules)
                {
                    var data = s.Data;
                    if (data == null || data.ServiceNeed != need)
                        continue;
                    float capacity = data.ServiceCapacity * GetStrength(s);
                    float used = 0f;
                    for (int i = 0; i < _habitats.Count && capacity - used > 1e-4f; i++)
                    {
                        if (_remaining[i] <= 0f || !DefenseSystem.InRange(s.Cells, _habitats[i].Cells, data.ServiceRadius))
                            continue;
                        float take = Mathf.Min(_remaining[i], capacity - used);
                        _remaining[i] -= take;
                        used += take;
                    }
                    _serviceUsed[s] = _serviceUsed.TryGetValue(s, out var prev) ? prev + used : used;
                    status.Served += used;
                }

                for (int i = 0; i < _habitats.Count; i++)
                {
                    float occupants = totalHousing > 0f ? population * _housing(_habitats[i]) / totalHousing : 0f;
                    if (!_habitatCoverage.TryGetValue(_habitats[i], out var d))
                        _habitatCoverage[_habitats[i]] = d = new Dictionary<ResidentNeed, float>();
                    d[need] = occupants > 0f ? 1f - _remaining[i] / occupants : 1f;
                }

                _statuses.Add(status);
                cap -= (1f - status.Ratio) * _config.NeedSatisfactionCapPenalty;
            }
            SatisfactionCap = Mathf.Clamp(cap, 0f, PopulationSimulation.MaxSatisfaction);
        }

        /// <summary>배치 미리보기·봇: 이 자리에 서비스 모듈을 두면 범위에 들어올 활성 거주 모듈 수와 주민 수 (현재 배분 기준).</summary>
        public int CountHabitatsInRange(StationGrid grid, ModuleData data, IReadOnlyList<Vector3Int> cells, int population, out float residents)
        {
            residents = 0f;
            if (data == null || !data.IsService)
                return 0;
            float totalHousing = 0f;
            foreach (var m in grid.Modules)
                totalHousing += Mathf.Max(0f, _housing(m));
            int count = 0;
            foreach (var m in grid.Modules)
            {
                float h = _housing(m);
                if (h <= 0f || !DefenseSystem.InRange(cells, m.Cells, data.ServiceRadius))
                    continue;
                count++;
                residents += totalHousing > 0f ? population * h / totalHousing : 0f;
            }
            return count;
        }
    }
}
