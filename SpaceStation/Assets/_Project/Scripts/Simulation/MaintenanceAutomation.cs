using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// Phase 6 자동화 연구 (GDD 10번 정비 자동화). 연구를 끝내야 동작한다 (<see cref="Unlocked"/>).
    /// - 대상: 내구도가 기준값 미만이고 운석 파손(수리 대기·수리 중)이 아닌 모듈. 내구도가 낮은 순서.
    /// - 정비해도 최대 내구도가 기준값 미만이면 정비 대신 재건축 (밸런스 봇과 같은 규칙).
    /// - 자동 실행은 자원 보호선을 지킨다: 낸 뒤에도 각 자원이 저장 한도 × 보호 비율 이상 남아야 한다.
    ///   못 내면 그 자리에서 멈추고 다음 검사 때 다시 시도 (가장 급한 모듈부터 자원을 모음).
    /// - 일괄 정비(<see cref="RunBatch"/>)는 보호선을 무시하고 지금 낼 수 있는 만큼 처리한다.
    /// </summary>
    public sealed class MaintenanceAutomation
    {
        public const float DefaultThreshold = 55f;   // 효율 저하 기준(50) 직전
        public const float MinThreshold = 30f;
        public const float MaxThreshold = 90f;
        public const float DefaultReserveRatio = 0.4f;
        public const float MaxReserveRatio = 0.8f;
        private const float CheckInterval = 1f;

        /// <summary>처리할 모듈 하나와 방법.</summary>
        public readonly struct Job
        {
            public readonly ModuleInstance Module;
            public readonly bool Rebuild;

            public Job(ModuleInstance module, bool rebuild)
            {
                Module = module;
                Rebuild = rebuild;
            }
        }

        private readonly StationSimulation _sim;
        private readonly List<DurabilityInfo> _candidates = new List<DurabilityInfo>();
        private readonly List<Job> _jobs = new List<Job>();
        private readonly float[] _required = new float[Enum.GetValues(typeof(ResourceType)).Length];
        private float _threshold = DefaultThreshold;
        private float _reserveRatio = DefaultReserveRatio;
        private float _timer;

        /// <summary>(정비 수, 재건축 수, 일괄 정비 여부). 하나 이상 처리했을 때만.</summary>
        public event Action<int, int, bool> Performed;
        /// <summary>(이전 모듈, 새 모듈). 선택 유지 등에 사용.</summary>
        public event Action<ModuleInstance, ModuleInstance> ModuleRebuilt;

        public MaintenanceAutomation(StationSimulation sim)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
        }

        public bool Unlocked => _sim.Effects.MaintenanceAutomation;
        public bool AutoMaintain { get; set; } = true;
        public bool AutoRebuild { get; set; } = true;

        /// <summary>이 내구도 미만이면 정비/재건축 대상.</summary>
        public float Threshold
        {
            get => _threshold;
            set => _threshold = Math.Max(MinThreshold, Math.Min(MaxThreshold, value));
        }

        /// <summary>자동 실행 후에도 남겨야 할 자원 (저장 한도 대비 비율).</summary>
        public float ReserveRatio
        {
            get => _reserveRatio;
            set => _reserveRatio = Math.Max(0f, Math.Min(MaxReserveRatio, value));
        }

        public int AutoMaintainCount { get; internal set; }
        public int AutoRebuildCount { get; internal set; }
        /// <summary>마지막 자동 검사에서 보호선 때문에 미룬 모듈이 있었는지.</summary>
        public bool WaitingForReserve { get; private set; }

        /// <summary>지금 기준값 미만인 모듈과 처리 방법 (UI 미리보기·일괄 정비).</summary>
        public IReadOnlyList<Job> Plan()
        {
            _jobs.Clear();
            _candidates.Clear();
            foreach (var info in _sim.Durability.Modules)
            {
                if (info.Current < _threshold && !_sim.Damage.IsDamaged(info.Module) && _sim.IsRemovableKind(info.Module))
                    _candidates.Add(info);
            }
            _candidates.Sort((a, b) => a.Current.CompareTo(b.Current));
            foreach (var info in _candidates)
            {
                bool rebuild = _sim.Durability.MaxAfterMaintenance(info) < _threshold;
                if (rebuild && _sim.CanRebuildKind(info.Module))
                    _jobs.Add(new Job(info.Module, true));
                else if (info.Current < info.Max - 1e-4f)
                    _jobs.Add(new Job(info.Module, false)); // 재건축할 수 없으면(등급 잠김 등) 정비라도
            }
            return _jobs;
        }

        public List<ResourceAmount> CostOf(Job job)
            => job.Rebuild ? _sim.GetRebuildCost(job.Module) : _sim.Durability.GetMaintenanceCost(job.Module);

        /// <summary>계획 전체의 비용 합 (자원별 한 줄).</summary>
        public List<ResourceAmount> TotalCost(IReadOnlyList<Job> jobs)
        {
            Array.Clear(_required, 0, _required.Length);
            foreach (var job in jobs)
                foreach (var a in CostOf(job))
                    _required[(int)a.Type] += a.Amount;
            var total = new List<ResourceAmount>();
            for (int i = 0; i < _required.Length; i++)
                if (_required[i] > 1e-4f)
                    total.Add(new ResourceAmount((ResourceType)i, _required[i]));
            return total;
        }

        internal void Tick(float dt)
        {
            if (!Unlocked || (!AutoMaintain && !AutoRebuild))
            {
                WaitingForReserve = false;
                return;
            }
            _timer += dt;
            if (_timer < CheckInterval)
                return;
            _timer = 0f;

            int maintained = 0, rebuilt = 0;
            WaitingForReserve = false;
            foreach (var job in new List<Job>(Plan()))
            {
                if (job.Rebuild ? !AutoRebuild : !AutoMaintain)
                    continue;
                if (!KeepsReserve(CostOf(job)))
                {
                    WaitingForReserve = true;
                    break; // 가장 급한 모듈부터 자원을 모은다
                }
                if (Execute(job))
                {
                    if (job.Rebuild) rebuilt++;
                    else maintained++;
                }
            }
            AutoMaintainCount += maintained;
            AutoRebuildCount += rebuilt;
            if (maintained + rebuilt > 0)
                Performed?.Invoke(maintained, rebuilt, false);
        }

        /// <summary>일괄 정비: 기준값 미만 모듈을 지금 처리 (보호선 무시, 못 내는 모듈은 건너뜀). 반환: (정비, 재건축).</summary>
        public (int maintained, int rebuilt) RunBatch()
        {
            int maintained = 0, rebuilt = 0;
            if (!Unlocked)
                return (0, 0);
            foreach (var job in new List<Job>(Plan()))
            {
                if (Execute(job))
                {
                    if (job.Rebuild) rebuilt++;
                    else maintained++;
                }
            }
            if (maintained + rebuilt > 0)
                Performed?.Invoke(maintained, rebuilt, true);
            return (maintained, rebuilt);
        }

        private bool Execute(Job job)
        {
            if (!job.Rebuild)
                return _sim.TryMaintain(job.Module) == MaintainResult.Done;
            if (_sim.TryRebuild(job.Module, out var rebuilt) != RebuildResult.Done)
                return false;
            ModuleRebuilt?.Invoke(job.Module, rebuilt);
            return true;
        }

        /// <summary>비용을 낸 뒤에도 각 자원이 저장 한도 × 보호 비율 이상 남는지.</summary>
        private bool KeepsReserve(IReadOnlyList<ResourceAmount> cost)
        {
            Array.Clear(_required, 0, _required.Length);
            foreach (var a in cost)
                _required[(int)a.Type] += a.Amount;
            var res = _sim.Resources;
            for (int i = 0; i < _required.Length; i++)
            {
                if (_required[i] <= 0f)
                    continue;
                var type = (ResourceType)i;
                if (!ResourceSimulation.IsStock(type))
                    return false;
                if (res.GetStock(type) - _required[i] < res.GetCapacity(type) * _reserveRatio)
                    return false;
            }
            return true;
        }
    }
}
