using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 정거장 하나의 씬 진입점. <see cref="StationGrid"/>를 소유하고,
    /// 그리드 이벤트를 받아 모듈 프리팹을 생성/제거한다. 시작 시 코어를 원점에 배치한다.
    /// </summary>
    public sealed class StationController : MonoBehaviour
    {
        [SerializeField] private ModuleData _coreModule;
        [Tooltip("생성된 모듈 오브젝트의 부모. 비우면 이 오브젝트 아래에 둔다.")]
        [SerializeField] private Transform _moduleRoot;

        private readonly Dictionary<ModuleInstance, GameObject> _views = new Dictionary<ModuleInstance, GameObject>();
        private StationGrid _grid;

        public StationGrid Grid => _grid;
        public ModuleInstance Core { get; private set; }

        private void Awake()
        {
            if (_moduleRoot == null)
                _moduleRoot = transform;

            _grid = new StationGrid();
            _grid.ModulePlaced += HandleModulePlaced;
            _grid.ModuleRemoved += HandleModuleRemoved;

            if (_coreModule == null)
            {
                Debug.LogError("StationController: 코어 모듈 데이터가 지정되지 않음", this);
                return;
            }
            _grid.TryPlace(_coreModule, Vector3Int.zero, 0, out var core);
            Core = core;
        }

        private void OnDestroy()
        {
            if (_grid == null)
                return;
            _grid.ModulePlaced -= HandleModulePlaced;
            _grid.ModuleRemoved -= HandleModuleRemoved;
        }

        public bool CanPlace(ModuleData data, Vector3Int origin, int rotation)
        {
            return _grid.CanPlace(data, origin, rotation);
        }

        public bool TryPlace(ModuleData data, Vector3Int origin, int rotation, out ModuleInstance module)
        {
            return _grid.TryPlace(data, origin, rotation, out module);
        }

        public bool CanRemove(ModuleInstance module)
        {
            return module != null && module != Core && (module.Data == null || module.Data.Removable);
        }

        public bool TryRemove(ModuleInstance module)
        {
            return CanRemove(module) && _grid.Remove(module);
        }

        public bool TryGetView(ModuleInstance module, out GameObject view)
        {
            return _views.TryGetValue(module, out view);
        }

        private void HandleModulePlaced(ModuleInstance module)
        {
            var prefab = module.Data != null ? module.Data.Prefab : null;
            if (prefab == null)
            {
                Debug.LogWarning($"{module}: 프리팹이 없어 표시하지 않음", this);
                return;
            }

            var view = Instantiate(prefab,
                GridConfig.CellToWorld(module.Origin),
                GridDirections.ToQuaternion(module.Rotation),
                _moduleRoot);
            view.name = module.ToString();
            _views.Add(module, view);
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            if (_views.TryGetValue(module, out var view))
            {
                _views.Remove(module);
                Destroy(view);
            }
        }
    }
}
