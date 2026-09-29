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
        public float RepairRemaining { get; internal set; }

        internal DamageInfo(ModuleInstance module, float destroyAfter)
        {
            Module = module;
            TimeUntilDestroyed = destroyAfter;
        }
    }

    /// <summary>
    /// 모듈 파손/수리/파괴 (GDD 12-2, BALANCE.md 10번).
    /// 파손: 생산 배율 감소 + 산소 누출. 수리 시작 시 누출·파괴 타이머 정지, 수리 중 생산 0.
    /// 방치 시간이 다 되면 <see cref="Destroyed"/>를 발생시키고 상태를 지운다 (실제 제거는 소유자가 한다).
    /// </summary>
    public sealed class DamageSystem
    {
        private readonly BalanceConfig _config;
        private readonly Dictionary<ModuleInstance, DamageInfo> _damaged = new Dictionary<ModuleInstance, DamageInfo>();
        private readonly List<DamageInfo> _finished = new List<DamageInfo>();

        public event Action<DamageInfo> Damaged;
        public event Action<DamageInfo> RepairStarted;
        public event Action<ModuleInstance> Repaired;
        /// <summary>방치로 파괴됨. 구독자가 그리드에서 제거해야 한다 (환급 없음).</summary>
        public event Action<ModuleInstance> Destroyed;
        /// <summary>타이머 등 표시값 변경 (UI 갱신용).</summary>
        public event Action Changed;

        public IReadOnlyCollection<DamageInfo> DamagedModules => _damaged.Values;
        public int DamagedCount => _damaged.Count;

        public DamageSystem(BalanceConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

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
                cost.Add(new ResourceAmount(a.Type, a.Amount * _config.RepairCostRate));
            return cost;
        }

        /// <summary>파손시킨다. 이미 파손된 모듈이면 false.</summary>
        public bool Damage(ModuleInstance module)
        {
            if (module == null || _damaged.ContainsKey(module))
                return false;
            var info = new DamageInfo(module, _config.DestroyAfterSeconds);
            _damaged.Add(module, info);
            Damaged?.Invoke(info);
            Changed?.Invoke();
            return true;
        }

        /// <summary>수리 시작 (비용 지불은 호출자가 먼저 한다). 파손이 아니거나 이미 수리 중이면 false.</summary>
        public bool StartRepair(ModuleInstance module)
        {
            if (!TryGetInfo(module, out var info) || info.IsRepairing)
                return false;
            info.IsRepairing = true;
            info.RepairRemaining = _config.RepairDuration;
            RepairStarted?.Invoke(info);
            Changed?.Invoke();
            return true;
        }

        /// <summary>모듈이 다른 이유(철거 등)로 사라졌을 때 상태만 지운다.</summary>
        public void Forget(ModuleInstance module)
        {
            if (module != null && _damaged.Remove(module))
                Changed?.Invoke();
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
                    info.TimeUntilDestroyed -= deltaSeconds;
                    if (info.TimeUntilDestroyed <= 1e-4f)
                        _finished.Add(info);
                }
            }

            foreach (var info in _finished)
            {
                _damaged.Remove(info.Module);
                if (info.IsRepairing)
                    Repaired?.Invoke(info.Module);
                else
                    Destroyed?.Invoke(info.Module);
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
