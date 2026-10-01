using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 면 클릭 고스트 배치.
    /// Tab / Shift+Tab: 건설 탭 전환 (4-5) / 숫자키 1~9: 현재 탭 안의 모듈 선택 / 좌클릭: 배치 확정 (선택 유지)
    /// R: 90도 회전 / 우클릭·ESC: 선택 취소
    /// </summary>
    public sealed class BuildController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private StationController _station;
        [SerializeField] private Camera _camera;
        [Tooltip("건설 메뉴 순서. 탭은 ModuleData.Category로 나뉘고, 숫자키는 탭 안의 순서")]
        [SerializeField] private List<ModuleData> _buildableModules = new List<ModuleData>();

        [Header("Ghost")]
        [SerializeField] private Material _ghostMaterial;
        [SerializeField] private Color _validColor = new Color(0.2f, 1f, 0.3f, 0.45f);
        [SerializeField] private Color _invalidColor = new Color(1f, 0.2f, 0.2f, 0.45f);
        [Tooltip("배치 가능/불가 색 전환 속도")]
        [SerializeField, Min(0.1f)] private float _colorBlendSpeed = 14f;
        private Color _ghostColor;
        private Color _appliedGhostColor = new Color(-1f, -1f, -1f, -1f);

        [Header("Raycast")]
        [SerializeField] private LayerMask _moduleMask = ~0;
        [SerializeField] private float _maxRayDistance = 500f;

        private ModuleData _selected;
        private int _rotation;
        private GameObject _ghost;
        private Renderer[] _ghostRenderers;
        private MaterialPropertyBlock _propertyBlock;
        private bool _hasTarget;
        private bool _targetValid;
        private PlacementResult _targetResult;
        private bool? _appliedValid;
        private Vector3Int _targetCell;
        private readonly List<ModuleCategory> _categories = new List<ModuleCategory>();
        private readonly List<ModuleData> _categoryModules = new List<ModuleData>();
        private ModuleCategory _category;
        private StationConnectors _connectors; // 5-6 고스트 통로 미리보기

        /// <summary>선택된 모듈이 바뀔 때 (null = 배치 모드 해제).</summary>
        public event Action<ModuleData> SelectionChanged;
        /// <summary>건설 탭이 바뀔 때.</summary>
        public event Action<ModuleCategory> CategoryChanged;

        public ModuleData Selected => _selected;
        public IReadOnlyList<ModuleData> BuildableModules => _buildableModules;
        /// <summary>모듈이 있는 탭만 (방어 탭은 모듈이 생기면 나타남).</summary>
        public IReadOnlyList<ModuleCategory> Categories => _categories;
        public ModuleCategory Category => _category;
        /// <summary>현재 탭의 모듈 (숫자키 순서).</summary>
        public IReadOnlyList<ModuleData> CategoryModules => _categoryModules;
        /// <summary>고스트를 놓을 대상 면이 있는지 (마우스가 모듈 위에 있고 UI 위가 아님).</summary>
        public bool HasTarget => _hasTarget;
        /// <summary>현재 대상 셀의 배치 판정 결과. HasTarget일 때만 의미 있음.</summary>
        public PlacementResult TargetResult => _targetResult;
        public Vector3Int TargetCell => _targetCell;
        public int Rotation => _rotation;

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            if (_camera == null)
                _camera = Camera.main;
            if (_station != null)
                _connectors = _station.GetComponent<StationConnectors>();
            BuildCategories.GetAvailable(_buildableModules, _categories);
            _category = _categories.Count > 0 ? _categories[0] : default;
            BuildCategories.Filter(_buildableModules, _category, _categoryModules);
        }

        public void SetCategory(ModuleCategory category)
        {
            if (category == _category || !_categories.Contains(category))
                return;
            _category = category;
            BuildCategories.Filter(_buildableModules, _category, _categoryModules);
            CategoryChanged?.Invoke(_category); // 배치 중인 모듈은 유지
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null)
                return;

            HandleSelectionKeys(keyboard);
            if (_selected == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
            {
                Select(null);
                return;
            }

            if (keyboard.rKey.wasPressedThisFrame)
                _rotation = GridDirections.NormalizeRotation(_rotation + 1);

            if (UiPointer.IsOverUi())
                _hasTarget = false; // HUD 위에서는 고스트 숨김, 클릭 무시
            else
                UpdateTarget(mouse.position.ReadValue());
            UpdateGhost();

            if (_hasTarget && _targetValid && mouse.leftButton.wasPressedThisFrame)
            {
                _station.TryPlace(_selected, _targetCell, _rotation, out _);
                // 배치 직후 같은 셀은 점유되므로 다음 프레임에 다시 판정된다.
            }
        }

        public void Select(ModuleData data)
        {
            if (data == _selected)
                return;

            _selected = data;
            _rotation = 0;
            _hasTarget = false;
            RebuildGhost();
            SelectionChanged?.Invoke(_selected);
        }

        /// <summary>건설 메뉴 버튼용: 같은 모듈을 다시 고르면 해제.</summary>
        public void ToggleSelect(ModuleData data)
        {
            Select(_selected == data ? null : data);
        }

        private void HandleSelectionKeys(Keyboard keyboard)
        {
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                SetCategory(BuildCategories.Cycle(_categories, _category, keyboard.shiftKey.isPressed ? -1 : 1));
                return;
            }
            int count = Mathf.Min(_categoryModules.Count, 9);
            for (int i = 0; i < count; i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                {
                    ToggleSelect(_categoryModules[i]);
                    return;
                }
            }
        }

        private void UpdateTarget(Vector2 screenPosition)
        {
            _hasTarget = false;
            var ray = _camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, _maxRayDistance, _moduleMask, QueryTriggerInteraction.Ignore))
                return;

            var hitCell = GridConfig.GetHitCell(hit.point, hit.normal);
            if (!_station.Grid.IsOccupied(hitCell))
                return; // 모듈이 아닌 콜라이더

            _targetCell = GridConfig.GetAdjacentCell(hit.point, hit.normal);
            _targetResult = _station.EvaluatePlacement(_selected, _targetCell, _rotation);
            _targetValid = _targetResult == PlacementResult.Valid;
            _hasTarget = true;
        }

        private void UpdateGhost()
        {
            if (_ghost == null)
                return;

            _ghost.SetActive(_hasTarget);
            if (!_hasTarget)
            {
                if (_connectors != null)
                    _connectors.HidePreview();
                return;
            }

            _ghost.transform.SetPositionAndRotation(
                GridConfig.CellToWorld(_targetCell),
                GridDirections.ToQuaternion(_rotation));

            // 5-4: 초록 ↔ 빨강을 부드럽게 전환 (홀로그램 셰이더의 _BaseColor)
            var target = _targetValid ? _validColor : _invalidColor;
            if (_appliedValid == null)
                _ghostColor = target;
            _appliedValid = _targetValid;
            if (_ghostColor != target)
                _ghostColor = Color.Lerp(_ghostColor, target, 1f - Mathf.Exp(-_colorBlendSpeed * Time.unscaledDeltaTime));
            if ((Vector4)_ghostColor != (Vector4)_appliedGhostColor)
            {
                _appliedGhostColor = _ghostColor;
                _propertyBlock.SetColor(BaseColorId, _ghostColor);
                foreach (var r in _ghostRenderers)
                    r.SetPropertyBlock(_propertyBlock);
            }
            if (_connectors != null)
                _connectors.ShowPreview(_selected, _targetCell, _rotation, _ghostColor);
        }

        private void RebuildGhost()
        {
            if (_ghost != null)
                Destroy(_ghost);
            _ghost = null;
            _ghostRenderers = null;
            _appliedValid = null;
            _appliedGhostColor = new Color(-1f, -1f, -1f, -1f); // 새 렌더러에 색을 다시 적용
            if (_connectors != null)
                _connectors.HidePreview();

            if (_selected == null || _selected.Prefab == null)
                return;

            _ghost = Instantiate(_selected.Prefab, transform);
            _ghost.name = "Ghost_" + _selected.name;
            foreach (var c in _ghost.GetComponentsInChildren<Collider>())
            {
                c.enabled = false; // 같은 프레임 레이캐스트에 걸리지 않도록 즉시 끔
                Destroy(c);
            }

            _ghostRenderers = _ghost.GetComponentsInChildren<Renderer>();
            foreach (var r in _ghostRenderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (_ghostMaterial != null)
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++)
                        mats[i] = _ghostMaterial;
                    r.sharedMaterials = mats;
                }
            }
            _ghost.SetActive(false);
        }
    }
}
