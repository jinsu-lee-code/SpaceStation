using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 정거장 하나의 씬 진입점. <see cref="StationGrid"/>와 <see cref="StationConnectivity"/>를 소유하고,
    /// 그리드 이벤트를 받아 모듈 프리팹을 생성/제거한다. 시작 시 코어를 원점에 배치한다.
    /// </summary>
    public sealed class StationController : MonoBehaviour
    {
        [SerializeField] private ModuleData _coreModule;
        [Tooltip("생성된 모듈 오브젝트의 부모. 비우면 이 오브젝트 아래에 둔다.")]
        [SerializeField] private Transform _moduleRoot;

        private readonly Dictionary<ModuleInstance, ModuleView> _views = new Dictionary<ModuleInstance, ModuleView>();
        private StationGrid _grid;
        private StationConnectivity _connectivity;

        public StationGrid Grid => _grid;
        public StationConnectivity Connectivity => _connectivity;
        public ModuleInstance Core { get; private set; }

        private void Awake()
        {
            if (_moduleRoot == null)
                _moduleRoot = transform;

            _grid = new StationGrid();
            _connectivity = new StationConnectivity(_grid, new FaceAdjacencyConnectionRule());
            _grid.ModulePlaced += HandleModulePlaced;
            _grid.ModuleRemoved += HandleModuleRemoved;
            _connectivity.ActiveStateChanged += HandleActiveStateChanged;

            if (_coreModule == null)
            {
                Debug.LogError("StationController: 코어 모듈 데이터가 지정되지 않음", this);
                return;
            }
            _grid.TryPlace(_coreModule, Vector3Int.zero, 0, out var core);
            Core = core;
            _connectivity.Root = core;
            _connectivity.Recalculate();
        }

        private void OnDestroy()
        {
            if (_grid == null)
                return;
            _grid.ModulePlaced -= HandleModulePlaced;
            _grid.ModuleRemoved -= HandleModuleRemoved;
            _connectivity.ActiveStateChanged -= HandleActiveStateChanged;
        }

        /// <summary>건설 비용 창구. 없으면 비용 없이 배치된다 (ResourceController가 Start에서 등록).</summary>
        public IBuildCostHandler CostHandler { get; set; }

        /// <summary>공간 배치 규칙(<see cref="PlacementRules"/>) + 비용 지불 가능 여부.</summary>
        public bool CanPlace(ModuleData data, Vector3Int origin, int rotation)
        {
            return EvaluatePlacement(data, origin, rotation) == PlacementResult.Valid;
        }

        /// <summary>배치 가능 여부와 불가 사유. 공간 규칙을 먼저 보고, 통과하면 비용을 본다.</summary>
        public PlacementResult EvaluatePlacement(ModuleData data, Vector3Int origin, int rotation)
        {
            var result = PlacementRules.Evaluate(_grid, data, origin, rotation);
            if (result == PlacementResult.Valid)
                result = CheckBuildable(data);
            if (result == PlacementResult.Valid && !CanAfford(data))
                return PlacementResult.InsufficientResources;
            return result;
        }

        /// <summary>진행도 제한 창구 (ProgressionController가 Start에서 등록). 없으면 제한 없음.</summary>
        public IPlacementPolicy PlacementPolicy { get; set; }

        /// <summary>위치와 무관한 건설 가능 여부 (해금·최대 설치 수).</summary>
        public PlacementResult CheckBuildable(ModuleData data)
        {
            return PlacementPolicy != null ? PlacementPolicy.CheckBuildable(data, _grid) : PlacementResult.Valid;
        }

        public bool CanAfford(ModuleData data)
        {
            return data != null && (CostHandler == null || CostHandler.CanAfford(data.BuildCost));
        }

        public bool TryPlace(ModuleData data, Vector3Int origin, int rotation, out ModuleInstance module)
        {
            module = null;
            if (!CanPlace(data, origin, rotation))
                return false;
            if (CostHandler != null && !CostHandler.TrySpend(data.BuildCost))
                return false;
            return _grid.TryPlace(data, origin, rotation, out module);
        }

        public bool CanRemove(ModuleInstance module)
        {
            return module != null && module != Core && (module.Data == null || module.Data.Removable);
        }

        /// <summary>철거. 건설 비용의 일부(BalanceConfig 환급률)를 돌려받는다.</summary>
        public bool TryRemove(ModuleInstance module)
        {
            if (!CanRemove(module) || !_grid.Remove(module))
                return false;
            if (CostHandler != null && module.Data != null)
                CostHandler.Refund(module.Data.BuildCost);
            return true;
        }

        /// <summary>파괴 (파손 방치 등). 환급 없음. 코어는 파괴되지 않는다.</summary>
        public bool DestroyModule(ModuleInstance module)
        {
            if (module == null || module == Core)
                return false;
            return _grid.Remove(module);
        }

        public bool TryGetView(ModuleInstance module, out ModuleView view)
        {
            return _views.TryGetValue(module, out view);
        }

        private void HandleModulePlaced(ModuleInstance module)
        {
            var prefab = module.Data != null ? module.Data.Prefab : null;
            if (prefab == null)
            {
                Debug.LogWarning($"{module}: 프리팹이 없어 표시하지 않음", this);
            }
            else
            {
                var go = Instantiate(prefab,
                    GridConfig.CellToWorld(module.Origin),
                    GridDirections.ToQuaternion(module.Rotation),
                    _moduleRoot);
                go.name = module.ToString();
                if (!go.TryGetComponent<ModuleView>(out var view))
                    view = go.AddComponent<ModuleView>();
                view.Initialize(module);
                _views.Add(module, view);
            }

            // 코어 배치 시점에는 Root가 아직 없으므로 Awake에서 따로 계산
            if (_connectivity.Root != null)
                _connectivity.Recalculate();
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            if (_views.TryGetValue(module, out var view))
            {
                _views.Remove(module);
                Destroy(view.gameObject);
            }
            _connectivity.Recalculate();
        }

        private void HandleActiveStateChanged(ModuleInstance module, bool active)
        {
            if (_views.TryGetValue(module, out var view))
                view.SetOperational(active);
        }
    }
}
