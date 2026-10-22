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
    /// 식단(남은 요구, 사용자 결정 2026-10-22)만 서비스 모듈이 아니라 정거장 전체 식량 생산의 종류별 비중 —
    /// 종류마다 (1 / 종류 수)까지만 더함 (채소 · 단백질 반반 = 100%, 한 종류만 = 50%, 80 · 20 = 70%), 위치 상관없이 모든 거주 모듈이 같은 비율.
    /// </summary>
    public sealed class NeedsSystem
    {
        private readonly BalanceConfig _config;
        private readonly Func<ModuleInstance, float> _strength;
        private readonly Func<ModuleInstance, float> _housing;
        private readonly List<NeedStatus> _statuses = new List<NeedStatus>();
        private readonly List<ModuleInstance> _habitats = new List<ModuleInstance>();
        private readonly List<CellBox> _habitatBoxes = new List<CellBox>(); // 12-2b: 먼 거주 모듈은 칸 비교 생략
        private readonly List<float> _remaining = new List<float>();
        private readonly Dictionary<ModuleInstance, float> _serviceUsed = new Dictionary<ModuleInstance, float>();
        // (거주 모듈, 요구) → 충족 비율
        private readonly Dictionary<ModuleInstance, Dictionary<ResidentNeed, float>> _habitatCoverage = new Dictionary<ModuleInstance, Dictionary<ResidentNeed, float>>();
        // 식단: 종류별 식량 생산 (FoodKindExtensions.All 순서) · 합
        private readonly float[] _foodByKind = new float[FoodKindExtensions.All.Length];
        private float _foodTotal;

        /// <param name="strength">서비스 모듈 가동률 (0~1)</param>
        /// <param name="housing">모듈의 실제 수용 인구 (비활성이면 0)</param>
        public NeedsSystem(BalanceConfig config, Func<ModuleInstance, float> strength, Func<ModuleInstance, float> housing)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _strength = strength ?? (_ => 1f);
            _housing = housing ?? (m => m.Data != null ? m.Data.HousingCapacity : 0);
            Effects = new ResearchEffects(config);
        }

        /// <summary>Phase 6 연구 효과 (서비스 반경 +칸, 만족도 상한 +N). StationSimulation이 공유 인스턴스로 바꿔 끼운다.</summary>
        public ResearchEffects Effects { get; set; }

        /// <summary>현재 등급까지 생긴 요구들의 충족 현황.</summary>
        public IReadOnlyList<NeedStatus> Statuses => _statuses;
        public float SatisfactionCap { get; private set; } = PopulationSimulation.MaxSatisfaction;

        /// <summary>13-3 공통 고리: 요구별 감점 배율 (null = 1). 서사판 = 그 요구를 중시하는 종족 비율만큼 크게.</summary>
        public Func<ResidentNeed, float> PenaltyScale { get; set; }

        /// <summary>서비스 모듈이 지금 맡은 주민 수 (UI).</summary>
        public float GetServed(ModuleInstance service) => _serviceUsed.TryGetValue(service, out var v) ? v : 0f;

        /// <summary>거주 모듈 주민의 요구 충족 비율 (요구가 없거나 거주 모듈이 아니면 −1).</summary>
        public float GetHabitatCoverage(ModuleInstance habitat, ResidentNeed need)
            => _habitatCoverage.TryGetValue(habitat, out var d) && d.TryGetValue(need, out var v) ? v : -1f;

        public float GetStrength(ModuleInstance service) => Mathf.Clamp01(_strength(service));

        /// <summary>식단: 이 식품 종류의 식량 생산 비중 (0~1, 식량 생산이 없으면 0). 마지막 계산 기준.</summary>
        public float DietShare(FoodKind kind)
        {
            if (_foodTotal <= 0f)
                return 0f;
            int i = System.Array.IndexOf(FoodKindExtensions.All, kind);
            return i >= 0 ? _foodByKind[i] / _foodTotal : 0f;
        }

        public void Evaluate(StationGrid grid, int population, IReadOnlyList<ResidentNeed> activeNeeds)
        {
            _statuses.Clear();
            _serviceUsed.Clear();
            _habitatCoverage.Clear();
            _habitats.Clear();

            _habitatBoxes.Clear();
            float totalHousing = 0f;
            foreach (var m in grid.Modules)
            {
                float h = _housing(m);
                if (h <= 0f)
                    continue;
                _habitats.Add(m);
                _habitatBoxes.Add(new CellBox(m.Cells));
                totalHousing += h;
            }

            float cap = PopulationSimulation.MaxSatisfaction;
            foreach (var need in activeNeeds)
            {
                if (need == ResidentNeed.None)
                    continue;
                var status = new NeedStatus { Need = need, Demand = population };
                if (need == ResidentNeed.Diet)
                {
                    float diet = EvaluateDiet(grid);
                    status.Served = population * diet;
                    foreach (var h in _habitats)
                    {
                        if (!_habitatCoverage.TryGetValue(h, out var dd))
                            _habitatCoverage[h] = dd = new Dictionary<ResidentNeed, float>();
                        dd[need] = diet;
                    }
                    _statuses.Add(status);
                    cap -= (1f - status.Ratio) * _config.NeedPenaltyFor(need) * (PenaltyScale != null ? PenaltyScale(need) : 1f);
                    continue;
                }
                _remaining.Clear();
                foreach (var h in _habitats)
                    _remaining.Add(totalHousing > 0f ? population * _housing(h) / totalHousing : 0f);

                foreach (var s in grid.Modules)
                {
                    var data = s.Data;
                    if (data == null || !data.Serves(need)) // 12-3: 여러 요구를 맡는 모듈(복지 센터)은 요구마다 담당 인원을 따로
                        continue;
                    float capacity = data.ServiceCapacity * GetStrength(s);
                    float used = 0f;
                    var serviceBox = new CellBox(s.Cells);
                    int radius = Effects.ServiceRadius(data);
                    for (int i = 0; i < _habitats.Count && capacity - used > 1e-4f; i++)
                    {
                        if (_remaining[i] <= 0f || !CellBox.InRange(s.Cells, serviceBox, _habitats[i].Cells, _habitatBoxes[i], radius))
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
                cap -= (1f - status.Ratio) * _config.NeedPenaltyFor(need) * (PenaltyScale != null ? PenaltyScale(need) : 1f); // 13-3 요구별 감소량 · 종류별 배율
            }
            cap += Effects.SatisfactionCapBonus; // 연구: 감점을 받아도 상한이 그만큼 높음 (100은 넘지 않음)
            SatisfactionCap = Mathf.Clamp(cap, 0f, PopulationSimulation.MaxSatisfaction);
        }

        /// <summary>식단 충족 비율: 식품 종류가 붙은 모듈의 식량 생산(× 가동률)을 종류별로 모아 종류마다 (1 / 종류 수)까지만 더함.</summary>
        private float EvaluateDiet(StationGrid grid)
        {
            var kinds = FoodKindExtensions.All;
            for (int k = 0; k < kinds.Length; k++)
                _foodByKind[k] = 0f;
            _foodTotal = 0f;
            foreach (var m in grid.Modules)
            {
                var data = m.Data;
                if (data == null || data.FoodKinds == FoodKind.None)
                    continue;
                float food = 0f;
                foreach (var p in data.Production)
                {
                    if (p.Type == ResourceType.Food)
                        food += p.Amount;
                }
                food *= GetStrength(m);
                if (food <= 0f)
                    continue;
                int n = data.FoodKinds.Count();
                for (int k = 0; k < kinds.Length; k++)
                {
                    if ((data.FoodKinds & kinds[k]) != 0)
                        _foodByKind[k] += food / n;
                }
                _foodTotal += food;
            }
            if (_foodTotal <= 0f)
                return 0f;
            float ratio = 0f, each = 1f / kinds.Length;
            for (int k = 0; k < kinds.Length; k++)
                ratio += Mathf.Min(_foodByKind[k] / _foodTotal, each);
            return Mathf.Clamp01(ratio);
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
                if (h <= 0f || !DefenseSystem.InRange(cells, m.Cells, Effects.ServiceRadius(data)))
                    continue;
                count++;
                residents += totalHousing > 0f ? population * h / totalHousing : 0f;
            }
            return count;
        }
    }
}
