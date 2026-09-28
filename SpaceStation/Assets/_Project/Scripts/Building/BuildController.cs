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
    /// 숫자키 1~9: 모듈 선택 (건설 메뉴 UI 전까지 임시) / 좌클릭: 배치 확정 (선택 유지)
    /// R: 90도 회전 / 우클릭·ESC: 선택 취소
    /// </summary>
    public sealed class BuildController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private StationController _station;
        [SerializeField] private Camera _camera;
        [Tooltip("숫자키 1~9 순서대로 선택")]
        [SerializeField] private List<ModuleData> _buildableModules = new List<ModuleData>();

        [Header("Ghost")]
        [SerializeField] private Material _ghostMaterial;
        [SerializeField] private Color _validColor = new Color(0.2f, 1f, 0.3f, 0.45f);
        [SerializeField] private Color _invalidColor = new Color(1f, 0.2f, 0.2f, 0.45f);

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

        /// <summary>선택된 모듈이 바뀔 때 (null = 배치 모드 해제).</summary>
        public event Action<ModuleData> SelectionChanged;

        public ModuleData Selected => _selected;
        public IReadOnlyList<ModuleData> BuildableModules => _buildableModules;
        /// <summary>고스트를 놓을 대상 면이 있는지 (마우스가 모듈 위에 있고 UI 위가 아님).</summary>
        public bool HasTarget => _hasTarget;
        /// <summary>현재 대상 셀의 배치 판정 결과. HasTarget일 때만 의미 있음.</summary>
        public PlacementResult TargetResult => _targetResult;

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            if (_camera == null)
                _camera = Camera.main;
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
            int count = Mathf.Min(_buildableModules.Count, 9);
            for (int i = 0; i < count; i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                {
                    ToggleSelect(_buildableModules[i]);
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
                return;

            _ghost.transform.SetPositionAndRotation(
                GridConfig.CellToWorld(_targetCell),
                GridDirections.ToQuaternion(_rotation));

            if (_appliedValid != _targetValid)
            {
                _appliedValid = _targetValid;
                _propertyBlock.SetColor(BaseColorId, _targetValid ? _validColor : _invalidColor);
                foreach (var r in _ghostRenderers)
                    r.SetPropertyBlock(_propertyBlock);
            }
        }

        private void RebuildGhost()
        {
            if (_ghost != null)
                Destroy(_ghost);
            _ghost = null;
            _ghostRenderers = null;
            _appliedValid = null;

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
