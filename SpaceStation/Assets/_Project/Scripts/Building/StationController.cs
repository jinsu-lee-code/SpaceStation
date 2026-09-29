using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 정거장의 씬 표현. <see cref="StationSimulation"/>의 그리드를 따라 모듈 프리팹을 생성/제거하고,
    /// 연결(활성/비활성)·파손 상태를 <see cref="ModuleView"/>에 반영한다.
    /// 배치·철거 명령은 시뮬레이션으로 그대로 전달한다 (규칙·비용 판정은 시뮬레이션이 한다).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class StationController : MonoBehaviour
    {
        [SerializeField] private SimulationHost _host;
        [Tooltip("생성된 모듈 오브젝트의 부모. 비우면 이 오브젝트 아래에 둔다.")]
        [SerializeField] private Transform _moduleRoot;

        private readonly Dictionary<ModuleInstance, ModuleView> _views = new Dictionary<ModuleInstance, ModuleView>();
        private StationSimulation _sim;

        public StationSimulation Simulation => _sim;
        public StationGrid Grid => _sim.Grid;
        public StationConnectivity Connectivity => _sim.Connectivity;
        public ModuleInstance Core => _sim.Core;

        private void Awake()
        {
            if (_moduleRoot == null)
                _moduleRoot = transform;

            _sim = _host.Simulation; // SimulationHost.Awake(-100) 이후
            foreach (var module in _sim.Grid.Modules)
                CreateView(module); // 코어 등 이미 배치된 모듈

            _sim.Grid.ModulePlaced += CreateView;
            _sim.Grid.ModuleRemoved += HandleModuleRemoved;
            _sim.Connectivity.ActiveStateChanged += HandleActiveStateChanged;
            _sim.Damage.Damaged += HandleDamaged;
            _sim.Damage.RepairStarted += HandleRepairStarted;
            _sim.Damage.Repaired += HandleRepaired;
        }

        private void OnDestroy()
        {
            if (_sim == null)
                return;
            _sim.Grid.ModulePlaced -= CreateView;
            _sim.Grid.ModuleRemoved -= HandleModuleRemoved;
            _sim.Connectivity.ActiveStateChanged -= HandleActiveStateChanged;
            _sim.Damage.Damaged -= HandleDamaged;
            _sim.Damage.RepairStarted -= HandleRepairStarted;
            _sim.Damage.Repaired -= HandleRepaired;
        }

        public bool CanPlace(ModuleData data, Vector3Int origin, int rotation)
            => _sim.EvaluatePlacement(data, origin, rotation) == PlacementResult.Valid;

        public PlacementResult EvaluatePlacement(ModuleData data, Vector3Int origin, int rotation)
            => _sim.EvaluatePlacement(data, origin, rotation);

        public PlacementResult CheckBuildable(ModuleData data) => _sim.CheckBuildable(data);
        public bool CanAfford(ModuleData data) => _sim.CanAfford(data);

        public bool TryPlace(ModuleData data, Vector3Int origin, int rotation, out ModuleInstance module)
            => _sim.TryPlace(data, origin, rotation, out module);

        public bool CanRemove(ModuleInstance module) => _sim.CanRemove(module);
        public bool TryRemove(ModuleInstance module) => _sim.TryRemove(module);
        public bool DestroyModule(ModuleInstance module) => _sim.DestroyModule(module);

        public bool TryGetView(ModuleInstance module, out ModuleView view)
        {
            return _views.TryGetValue(module, out view);
        }

        private void CreateView(ModuleInstance module)
        {
            var prefab = module.Data != null ? module.Data.Prefab : null;
            if (prefab == null)
            {
                Debug.LogWarning($"{module}: 프리팹이 없어 표시하지 않음", this);
                return;
            }

            var go = Instantiate(prefab,
                GridConfig.CellToWorld(module.Origin),
                GridDirections.ToQuaternion(module.Rotation),
                _moduleRoot);
            go.name = module.ToString();
            if (!go.TryGetComponent<ModuleView>(out var view))
                view = go.AddComponent<ModuleView>();
            view.Initialize(module);
            // 연결 재계산은 시뮬레이션이 이미 끝냈으므로 현재 상태를 바로 반영
            view.SetOperational(_sim.Connectivity.IsActive(module));
            _views.Add(module, view);
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            if (_views.TryGetValue(module, out var view))
            {
                _views.Remove(module);
                Destroy(view.gameObject);
            }
        }

        private void HandleActiveStateChanged(ModuleInstance module, bool active)
        {
            if (_views.TryGetValue(module, out var view))
                view.SetOperational(active);
        }

        private void HandleDamaged(DamageInfo info) => SetDamageVisual(info.Module, ModuleDamageVisual.Damaged);
        private void HandleRepairStarted(DamageInfo info) => SetDamageVisual(info.Module, ModuleDamageVisual.Repairing);
        private void HandleRepaired(ModuleInstance module) => SetDamageVisual(module, ModuleDamageVisual.None);

        private void SetDamageVisual(ModuleInstance module, ModuleDamageVisual visual)
        {
            if (_views.TryGetValue(module, out var view))
                view.SetDamageVisual(visual);
        }
    }
}
