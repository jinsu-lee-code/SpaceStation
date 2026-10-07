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

        private ModuleInstance _removedSelection;

        public ModuleInstance Selected => _selected;
        /// <summary>마지막 철거의 실제 환급액 (내구도 반영, 철거 직전에 계산).</summary>
        public System.Collections.Generic.IReadOnlyList<SpaceStation.Data.ResourceAmount> LastRemovedRefund { get; private set; }

        private void Awake()
        {
            if (_camera == null)
                _camera = Camera.main;
        }

        private void Start()
        {
            // StationController.Awake에서 그리드가 만들어진 뒤 구독
            _station.Grid.ModuleRemoved += HandleModuleRemoved;
            _station.Simulation.Automation.ModuleRebuilt += HandleAutoRebuilt;
        }

        private void OnDestroy()
        {
            if (_station != null && _station.Grid != null)
                _station.Grid.ModuleRemoved -= HandleModuleRemoved;
            if (_station != null && _station.Simulation != null)
                _station.Simulation.Automation.ModuleRebuilt -= HandleAutoRebuilt;
        }

        /// <summary>선택한 모듈이 자동 재건축되면 새 모듈을 계속 선택한다.</summary>
        private void HandleAutoRebuilt(ModuleInstance previous, ModuleInstance rebuilt)
        {
            if (previous != null && previous == _removedSelection)
                Select(rebuilt);
            _removedSelection = null;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null || InputGate.Blocked)
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

            if (keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame)
            {
                Select(null);
                return;
            }

            if (KeyBindings.WasPressed(GameAction.Demolish) || KeyBindings.WasPressed(GameAction.DemolishAlt))
                RemoveSelected();
        }

        /// <summary>7-1: 선택 모듈의 효율 구간을 테두리 색에 반영 (값 조회만, 구간이 바뀔 때만 뷰 갱신).</summary>
        private void LateUpdate()
        {
            if (_selected != null && _station.TryGetView(_selected, out var view))
                view.SetEfficiencyBand(EfficiencyBands.Classify(_station.Simulation.GetModuleEfficiency(_selected)));
        }

        public void Select(ModuleInstance module)
        {
            if (module == _selected)
                return;

            if (_selected != null && _station.TryGetView(_selected, out var previous))
                previous.SetHighlighted(false);
            _selected = module;
            if (_selected != null && _station.TryGetView(_selected, out var view))
            {
                view.SetEfficiencyBand(EfficiencyBands.Classify(_station.Simulation.GetModuleEfficiency(_selected)));
                view.SetHighlighted(true);
            }
            SelectionChanged?.Invoke(_selected);
        }

        public void RemoveSelected()
        {
            if (_selected == null)
                return;
            var target = _selected;
            LastRemovedRefund = _station.GetRefund(target);
            if (_station.TryRemove(target))
                Removed?.Invoke(target);
            else
                RemoveRejected?.Invoke(target);
        }

        private ModuleInstance PickModule(Vector2 screenPosition)
        {
            // 12-0 (U-5): 모델 모양(Pick 콜라이더)으로 판정, 맞은 콜라이더의 모듈 뷰에서 모듈을 찾는다
            var ray = _camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, _maxRayDistance, ModulePick.SelectionMask(_moduleMask), QueryTriggerInteraction.Ignore))
                return null;
            var view = hit.collider.GetComponentInParent<ModuleView>();
            if (view != null && view.Module != null)
                return view.Module;
            _station.Grid.TryGetModule(GridConfig.GetHitCell(hit.point, hit.normal), out var module);
            return module;
        }

        private void HandleModuleRemoved(ModuleInstance module)
        {
            if (module != _selected)
                return;
            _removedSelection = module; // 자동 재건축이면 곧 새 모듈로 다시 선택
            _selected = null; // 뷰는 곧 파괴되므로 강조 해제 불필요
            SelectionChanged?.Invoke(null);
        }
    }
}
