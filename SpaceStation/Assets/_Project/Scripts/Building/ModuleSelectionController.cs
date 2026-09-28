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
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private StationController _station;
        [SerializeField] private BuildController _build;
        [SerializeField] private Camera _camera;
        [SerializeField] private Color _highlightColor = new Color(1f, 0.9f, 0.3f, 1f);

        [Header("Raycast")]
        [SerializeField] private LayerMask _moduleMask = ~0;
        [SerializeField] private float _maxRayDistance = 500f;

        private ModuleInstance _selected;
        private Renderer[] _selectedRenderers;
        private MaterialPropertyBlock _propertyBlock;

        public ModuleInstance Selected => _selected;

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
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

            if (mouse.leftButton.wasPressedThisFrame)
                Select(PickModule(mouse.position.ReadValue()));

            if (_selected == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Select(null);
                return;
            }

            if (keyboard.deleteKey.wasPressedThisFrame || keyboard.xKey.wasPressedThisFrame)
            {
                if (!_station.TryRemove(_selected))
                    Debug.Log($"{_selected}: 철거할 수 없는 모듈"); // 경고 UI는 2-6 HUD에서
            }
        }

        public void Select(ModuleInstance module)
        {
            if (module == _selected)
                return;

            ClearHighlight();
            _selected = module;
            if (_selected != null && _station.TryGetView(_selected, out var view))
            {
                _selectedRenderers = view.GetComponentsInChildren<Renderer>();
                _propertyBlock.SetColor(BaseColorId, _highlightColor);
                foreach (var r in _selectedRenderers)
                    r.SetPropertyBlock(_propertyBlock);
            }
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
            if (module == _selected)
            {
                _selectedRenderers = null; // 뷰는 곧 파괴되므로 MPB 정리 불필요
                _selected = null;
            }
        }

        private void ClearHighlight()
        {
            if (_selectedRenderers == null)
                return;
            foreach (var r in _selectedRenderers)
            {
                if (r != null)
                    r.SetPropertyBlock(null);
            }
            _selectedRenderers = null;
        }
    }
}
