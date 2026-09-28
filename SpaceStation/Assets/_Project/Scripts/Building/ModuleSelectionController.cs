using System;
using SpaceStation.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceStation.Building
{
    /// <summary>
    /// 배치 모드가 아닐 때 모듈 선택/철거.
    /// 좌클릭: 선택 (빈 곳 클릭 시 해제) / Delete·X: 철거 / ESC: 선택 해제
    /// </summary>
    public sealed class ModuleSelectionController : MonoBehaviour
    {
        [SerializeField] private StationController _station;
        [SerializeField] private BuildController _build;
        [SerializeField] private Camera _camera;

        [Header("Raycast")]
        [SerializeField] private LayerMask _moduleMask = ~0;
        [SerializeField] private float _maxRayDistance = 500f;

        private ModuleInstance _selected;

        /// <summary>선택 모듈이 바뀔 때 (null = 해제).</summary>
        public event Action<ModuleInstance> SelectionChanged;
        /// <summary>철거를 시도했으나 불가 (코어 등).</summary>
        public event Action<ModuleInstance> RemoveRejected;
        /// <summary>철거 성공.</summary>
        public event Action<ModuleInstance> Removed;

        public ModuleInstance Selected => _selected;

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;
        }

        private void Start()
        {
            // StationController.Awake에서 그리드가 만들어진 뒤 구독
            _station.Grid.ModuleRemoved += HandleModuleRemoved;
        }

        private void OnDestroy()
        {
            if (_station != null && _station.Grid != null)
                _station.Grid.ModuleRemoved -= HandleModuleRemoved;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null)
                return;

            if (_build != null && _build.Selected != null)
            {
                Select(null); // 배치 모드 진입 시 선택 해제
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && !UiPointer.IsOverUi())
                Select(PickModule(mouse.position.ReadValue()));

            if (_selected == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Select(null);
                return;
            }

            if (keyboard.deleteKey.wasPressedThisFrame || keyboard.xKey.wasPressedThisFrame)
                RemoveSelected();
        }

        public void Select(ModuleInstance module)
        {
            if (module == _selected)
                return;

            if (_selected != null && _station.TryGetView(_selected, out var previous))
                previous.SetHighlighted(false);
            _selected = module;
            if (_selected != null && _station.TryGetView(_selected, out var view))
                view.SetHighlighted(true);
            SelectionChanged?.Invoke(_selected);
        }

        public void RemoveSelected()
        {
            if (_selected == null)
                return;
            var target = _selected;
            if (_station.TryRemove(target))
                Removed?.Invoke(target);
            else
                RemoveRejected?.Invoke(target);
        }

        private ModuleInstance PickModule(Vector2 screenPosition)
        {
            var ray = _camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, _maxRayDistance, _moduleMask, QueryTriggerInteraction.Ignore))
                return null;
            _station.Grid.TryGetModule(GridConfig.GetHitCell(hit.point, hit.normal), out var module);
            return module;
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            if (module != _selected)
                return;
            _selected = null; // 뷰는 곧 파괴되므로 강조 해제 불필요
            SelectionChanged?.Invoke(null);
        }
    }
}
