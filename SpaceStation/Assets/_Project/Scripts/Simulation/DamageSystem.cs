using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>파손된 모듈 하나의 상태.</summary>
    public sealed class DamageInfo
    {
        public ModuleInstance Module { get; }
        /// <summary>수리하지 않으면 파괴되기까지 남은 시간(초). 수리 중에는 멈춘다.</summary>
        public float TimeUntilDestroyed { get; internal set; }
        public bool IsRepairing { get; internal set; }
        /// <summary>수리 비용을 내고 빈 슬롯을 기다리는 중 (4-6). 대기 중에도 누출·파괴 타이머는 계속.</summary>
        public bool IsQueued { get; internal set; }
        public float RepairRemaining { get; internal set; }
        /// <summary>
        /// 이웃으로 번지기까지 남은 시간(초, 4-7). 수리 중·대기 중에는 멈춘다. 한 번 번지면 <see cref="HasSpread"/>.
        /// 확산이 꺼져 있으면(설정 0) 무한대.
        /// </summary>
        public float TimeUntilSpread { get; internal set; }
        public bool HasSpread { get; internal set; }
        /// <summary>아직 번지지 않았고 확산 타이머가 흐르는 중.</summary>
        public bool SpreadPending => !HasSpread && !IsRepairing && !(IsQueued && _queuePausesSpread) && !float.IsPositiveInfinity(TimeUntilSpread);

        private readonly bool _queuePausesSpread;

        internal DamageInfo(ModuleInstance module, float destroyAfter, float spreadAfter, bool queuePausesSpread)
        {
            Module = module;
            _queuePausesSpread = queuePausesSpread;
            TimeUntilDestroyed = destroyAfter;
            TimeUntilSpread = spreadAfter > 0f ? spreadAfter : float.PositiveInfinity;
        }
    }

    /// <summary>
    /// 모듈 파손/수리/파괴 (GDD 12-2, BALANCE.md 10번).
    /// 파손: 생산 배율 감소 + 산소 누출. 수리 시작 시 누출·파괴 타이머 정지, 수리 중 생산 0.
    /// 방치 시간이 다 되면 <see cref="Destroyed"/>를 발생시키고 상태를 지운다 (실제 제거는 소유자가 한다).
    /// 4-6: 동시 수리 수는 <see cref="RepairCapacity"/>까지. 넘치면 대기열(FIFO, 우선 수리로 맨 앞)에 들어간다.
    /// </summary>
    public sealed class DamageSystem
    {
        private readonly BalanceConfig _config;
        private readonly Dictionary<ModuleInstance, DamageInfo> _damaged = new Dictionary<ModuleInstance, DamageInfo>();
        private readonly List<DamageInfo> _queue = new List<DamageInfo>();
        private readonly List<DamageInfo> _finished = new List<DamageInfo>();
        private readonly List<ModuleInstance> _spreading = new List<ModuleInstance>();
        private readonly List<bool> _finishedRepaired = new List<bool>();
        private int _repairCapacity = int.MaxValue;
        private readonly List<float> _weights = new List<float>();
        private readonly List<ModuleInstance> _picked = new List<ModuleInstance>();

        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> RepairStarted;
        public event Action<ModuleInstance> Repaired;
        /// <summary>방치로 파괴됨. 구독자가 그리드에서 제거해야 한다 (환급 없음).</summary>
        public event Action<ModuleInstance> Destroyed;
        /// <summary>확산 시점 도달 (4-7). 구독자가 이웃을 골라 파손시킨다 (그리드를 모르므로).</summary>
        public event Action<ModuleInstance> SpreadDue;
        /// <summary>타이머 등 표시값 변경 (UI 갱신용).</summary>
        public event Action Changed;

        public IReadOnlyCollection<DamageInfo> DamagedModules => _damaged.Values;
        public int DamagedCount => _damaged.Count;
        /// <summary>수리 대기열 (앞이 먼저 시작).</summary>
        public IReadOnlyList<DamageInfo> Queue => _queue;

        /// <summary>
        /// 동시 수리 슬롯 수 (소유자가 코어·정비 베이 수로 갱신, 기본 무제한).
        /// 줄어도 진행 중인 수리는 끝까지 간다. 늘면 대기열에서 바로 시작.
        /// </summary>
        public int RepairCapacity
        {
            get => _repairCapacity;
            set
            {
                value = Math.Max(0, value);
                if (value == _repairCapacity)
                    return;
                _repairCapacity = value;
                if (StartQueued())
                    Changed?.Invoke();
            }
        }

        public int RepairingCount
        {
            get
            {
                int n = 0;
                foreach (var info in _damaged.Values)
                {
                    if (info.IsRepairing)
                        n++;
                }
                return n;
            }
        }

        public bool HasFreeRepairSlot => RepairingCount < _repairCapacity;

        /// <summary>대기 순번 (1부터). 대기 중이 아니면 0.</summary>
        public int GetQueuePosition(ModuleInstance module)
        {
            for (int i = 0; i < _queue.Count; i++)
            {
                if (_queue[i].Module == module)
                    return i + 1;
            }
            return 0;
        }

        public DamageSystem(BalanceConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Effects = new ResearchEffects(config);
        }

        /// <summary>Phase 6 연구 효과 (수리 비용·시간). StationSimulation이 공유 인스턴스로 바꿔 끼운다.</summary>
        public ResearchEffects Effects { get; set; }

        public bool IsDamaged(ModuleInstance module) => module != null && _damaged.ContainsKey(module);

        public bool TryGetInfo(ModuleInstance module, out DamageInfo info)
        {
            info = null;
            return module != null && _damaged.TryGetValue(module, out info);
        }

        /// <summary>1 = 정상, 파손 배율(0.5), 수리 중 0.</summary>
        public float GetProductionMultiplier(ModuleInstance module)
        {
            if (!TryGetInfo(module, out var info))
                return 1f;
            return info.IsRepairing ? 0f : _config.DamagedProductionMultiplier;
        }

        /// <summary>수리 중이 아닌 파손 모듈들의 산소 누출 합 (초당).</summary>
        public float OxygenLeakPerSecond
        {
            get
            {
                int leaking = 0;
                foreach (var info in _damaged.Values)
                {
                    if (!info.IsRepairing)
                        leaking++;
                }
                return leaking * _config.DamagedOxygenLeakPerSecond;
            }
        }

        /// <summary>수리 비용 = 건설 비용 × 수리 비율.</summary>
        public List<ResourceAmount> GetRepairCost(ModuleInstance module)
        {
            var cost = new List<ResourceAmount>();
            if (module?.Data == null)
                return cost;
            foreach (var a in module.Data.BuildCost)
                cost.Add(new ResourceAmount(a.Type, a.Amount * Effects.RepairCostRate));
            return cost;
        }

        /// <summary>파손시킨다. 이미 파손된 모듈이면 false.</summary>
        public bool Damage(ModuleInstance module)
        {
            if (module == null || _damaged.ContainsKey(module))
                return false;
            var info = new DamageInfo(module, _config.DestroyAfterSeconds, _config.SpreadAfterSeconds, _config.QueuePausesSpread);
            _damaged.Add(module, info);
            Damaged?.Invoke(info);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 세이브 복원: 파손 상태를 그대로 만든다 (이벤트 없음). 대기 중이면 호출 순서대로 대기열 끝에 들어간다.
        /// spreadAfter가 음수면 확산 없음(무한대).
        /// </summary>
        internal void Restore(ModuleInstance module, float timeUntilDestroyed, bool repairing, float repairRemaining,
            float spreadAfter, bool hasSpread, bool queued)
        {
            if (module == null || _damaged.ContainsKey(module))
                return;
            var info = new DamageInfo(module, Math.Max(0.01f, timeUntilDestroyed), 0f, _config.QueuePausesSpread)
            {
                IsRepairing = repairing,
                RepairRemaining = repairing ? Math.Max(0.01f, repairRemaining) : 0f,
                TimeUntilSpread = spreadAfter < 0f ? float.PositiveInfinity : spreadAfter,
                HasSpread = hasSpread,
                IsQueued = !repairing && queued,
            };
            _damaged.Add(module, info);
            if (info.IsQueued)
                _queue.Add(info);
        }

        /// <summary>
        /// 수리 요청 (비용 지불은 호출자가 먼저 한다). 빈 슬롯이 있으면 바로 시작, 없으면 대기열 끝에 넣는다.
        /// 파손이 아니거나 이미 수리 중·대기 중이면 false.
        /// </summary>
        public bool StartRepair(ModuleInstance module)
        {
            if (!TryGetInfo(module, out var info) || info.IsRepairing || info.IsQueued)
                return false;
            if (HasFreeRepairSlot)
            {
                Begin(info);
            }
            else
            {
                info.IsQueued = true;
                _queue.Add(info);
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>대기 중인 모듈을 대기열 맨 앞으로 (우선 수리).</summary>
        public bool Prioritize(ModuleInstance module)
        {
            int position = GetQueuePosition(module);
            if (position <= 1)
                return false;
            var info = _queue[position - 1];
            _queue.RemoveAt(position - 1);
            _queue.Insert(0, info);
            Changed?.Invoke();
            return true;
        }

        /// <summary>대기 취소 (환불은 호출자). 대기 중이 아니면 false.</summary>
        public bool CancelQueued(ModuleInstance module)
        {
            int position = GetQueuePosition(module);
            if (position == 0)
                return false;
            _queue[position - 1].IsQueued = false;
            _queue.RemoveAt(position - 1);
            Changed?.Invoke();
            return true;
        }

        /// <summary>모듈이 다른 이유(철거 등)로 사라졌을 때 상태만 지운다.</summary>
        public void Forget(ModuleInstance module)
        {
            if (module == null || !_damaged.TryGetValue(module, out var info))
                return;
            _damaged.Remove(module);
            _queue.Remove(info);
            StartQueued();
            Changed?.Invoke();
        }

        private void Begin(DamageInfo info)
        {
            info.IsQueued = false;
            info.IsRepairing = true;
            info.RepairRemaining = Effects.RepairDuration;
            RepairStarted?.Invoke(info);
        }

        /// <summary>빈 슬롯만큼 대기열 앞에서 시작. 하나라도 시작했으면 true.</summary>
        private bool StartQueued()
        {
            bool started = false;
            int repairing = RepairingCount;
            while (_queue.Count > 0 && repairing < _repairCapacity)
            {
                var info = _queue[0];
                _queue.RemoveAt(0);
                Begin(info);
                repairing++;
                started = true;
            }
            return started;
        }

        public void Tick(float deltaSeconds)
        {
            if (_damaged.Count == 0)
                return;

            _finished.Clear();
            foreach (var info in _damaged.Values)
            {
                if (info.IsRepairing)
                {
                    info.RepairRemaining -= deltaSeconds;
                    if (info.RepairRemaining <= 1e-4f)
                        _finished.Add(info);
                }
                else
                {
                    if (info.SpreadPending) // 대기 중이면 확산만 멈춤 (설정, 누출·파괴는 계속)
                    {
                        info.TimeUntilSpread -= deltaSeconds;
                        if (info.TimeUntilSpread <= 1e-4f)
                        {
                            info.TimeUntilSpread = 0f;
                            info.HasSpread = true;
                            _spreading.Add(info.Module);
                        }
                    }
                    info.TimeUntilDestroyed -= deltaSeconds;
                    if (info.TimeUntilDestroyed <= 1e-4f)
                        _finished.Add(info);
                }
            }

            // 먼저 끝난 모듈을 모두 빼고 결과를 확정한 뒤에 알린다.
            // (알림 도중 정비 베이 복구로 슬롯이 늘면 StartQueued가 도는데, 같은 틱에 파괴될 대기 모듈이
            //  아직 대기열에 남아 있으면 '수리 시작'으로 바뀌어 파괴 대신 수리 완료로 처리되는 문제 방지)
            _finishedRepaired.Clear();
            foreach (var info in _finished)
            {
                _damaged.Remove(info.Module);
                _queue.Remove(info); // 대기 중 파괴: 낸 수리 비용은 돌려받지 못함
                _finishedRepaired.Add(info.IsRepairing);
            }
            for (int i = 0; i < _finished.Count; i++)
            {
                if (_finishedRepaired[i])
                    Repaired?.Invoke(_finished[i].Module);
                else
                    Destroyed?.Invoke(_finished[i].Module);
            }
            StartQueued(); // 끝난 수리 슬롯을 대기열에 넘김

            // 4-7 확산: 순회가 끝난 뒤 알림 (구독자가 Damage()로 새 파손을 추가하므로). 새 파손은 이번 틱에 흐르지 않음
            if (_spreading.Count > 0)
            {
                foreach (var module in _spreading)
                    SpreadDue?.Invoke(module);
                _spreading.Clear();
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// 운석 대상 후보: 코어 제외, 이미 파손된 모듈 제외, 외곽(어느 셀이든 빈 면이 하나 이상) 모듈.
        /// </summary>
        public void FindMeteorCandidates(StationGrid grid, ModuleInstance core, List<ModuleInstance> results)
        {
            results.Clear();
            foreach (var module in grid.Modules)
            {
                if (module == core || _damaged.ContainsKey(module))
                    continue;
                if (IsExterior(grid, module))
                    results.Add(module);
            }
        }

        /// <summary>
        /// 운석 대상 여러 개 선택 (분산 타격, 중복 없음). 후보마다 가중치 = <see cref="GetMeteorWeight"/> (빈 면이 많을수록 잘 맞음).
        /// 후보가 count보다 적으면 후보 전부.
        /// </summary>
        public void PickMeteorTargets(StationGrid grid, ModuleInstance core, int count, Func<float> random01, List<ModuleInstance> results)
        {
            FindMeteorCandidates(grid, core, results);
            if (results.Count <= count)
                return;

            _weights.Clear();
            float total = 0f;
            foreach (var m in results)
            {
                float w = GetMeteorWeight(CountExposedFaces(grid, m));
                _weights.Add(w);
                total += w;
            }

            _picked.Clear();
            for (int n = 0; n < count && results.Count > 0; n++)
            {
                float roll = random01() * total;
                int index = results.Count - 1;
                for (int i = 0; i < results.Count; i++)
                {
                    if (roll < _weights[i])
                    {
                        index = i;
                        break;
                    }
                    roll -= _weights[i];
                }
                _picked.Add(results[index]);
                total -= _weights[index];
                results.RemoveAt(index);
                _weights.RemoveAt(index);
            }
            results.Clear();
            results.AddRange(_picked);
        }

        /// <summary>후보 중 1곳을 노출 가중치로 고른다 (4-8 실드 튕김). 후보가 없거나 가중치 합이 0이면 null.</summary>
        public ModuleInstance PickWeighted(StationGrid grid, IReadOnlyList<ModuleInstance> candidates, Func<float> random01)
        {
            float total = 0f;
            foreach (var m in candidates)
                total += GetMeteorWeight(CountExposedFaces(grid, m));
            if (total <= 0f)
                return null;
            float roll = random01() * total;
            foreach (var m in candidates)
            {
                float w = GetMeteorWeight(CountExposedFaces(grid, m));
                if (roll < w)
                    return m;
                roll -= w;
            }
            return candidates[candidates.Count - 1];
        }

        /// <summary>피격 가중치 = 기울기 × (노출 면 − 1) + 1 (BALANCE 13번). 노출 1면 = 1.</summary>
        public float GetMeteorWeight(int exposedFaces)
        {
            if (exposedFaces <= 0)
                return 0f;
            return _config.MeteorExposureSlope * (exposedFaces - 1) + 1f;
        }

        /// <summary>모듈의 모든 셀에서 빈 셀과 맞닿은 면의 수.</summary>
        public static int CountExposedFaces(StationGrid grid, ModuleInstance module)
        {
            int exposed = 0;
            foreach (var cell in module.Cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    if (!grid.IsOccupied(cell + dir))
                        exposed++;
                }
            }
            return exposed;
        }

        public static bool IsExterior(StationGrid grid, ModuleInstance module)
        {
            foreach (var cell in module.Cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    if (!grid.IsOccupied(cell + dir))
                        return true;
                }
            }
            return false;
        }
    }
}
