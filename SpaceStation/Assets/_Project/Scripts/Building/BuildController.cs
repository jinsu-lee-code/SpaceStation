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
    /// 회전(기본 R) / 우클릭·ESC: 선택 취소. 키는 KeyBindings(7-5)에서 바꿀 수 있다
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
        [Header("Dock Lane (8-0)")]
        [Tooltip("배치 중 이미 지은 채굴 도킹의 접근로 표시 색")]
        [SerializeField] private Color _existingLaneColor = new Color(1f, 0.72f, 0.2f, 0.16f);
        [Tooltip("고스트 도킹 접근로: 고스트 색에 곱하는 알파")]
        [SerializeField, Range(0f, 1f)] private float _ghostLaneAlpha = 0.5f;
        [Tooltip("접근로 판 크기 (칸 대비 가로·세로)")]
        [SerializeField, Range(0.3f, 1f)] private float _laneCellScale = 0.7f;
        [Tooltip("접근로 판 두께 (칸 대비) — 얇은 활주로처럼")]
        [SerializeField, Range(0.01f, 1f)] private float _laneThickness = 0.05f;
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
        // 8-0 도킹 접근로 표시 (반투명 홀로그램 칸, 풀링)
        private readonly List<GameObject> _laneCells = new List<GameObject>();
        private readonly List<Renderer> _laneRenderers = new List<Renderer>();
        private readonly List<Vector3Int> _existingLanes = new List<Vector3Int>();
        private readonly List<Vector3Int> _laneScratch = new List<Vector3Int>();
        private bool _existingLanesDirty = true;
        private int _laneUsed;

        /// <summary>선택된 모듈이 바뀔 때 (null = 배치 모드 해제).</summary>
        public event Action<ModuleData> SelectionChanged;
        /// <summary>건설 탭이 바뀔 때.</summary>
        public event Action<ModuleCategory> CategoryChanged;
        /// <summary>배치 중 회전 (R).</summary>
        public event Action Rotated;
        /// <summary>배치 확정 성공.</summary>
        public event Action<ModuleInstance> Placed;
        /// <summary>배치할 수 없는 자리에 클릭함.</summary>
        public event Action<PlacementResult> PlaceRejected;

        public ModuleData Selected => _selected;
        public StationController Station => _station;
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

        private void Start()
        {
            // StationController.Awake에서 그리드가 만들어진 뒤 구독
            if (_station == null || _station.Grid == null)
                return;
            _station.Grid.ModulePlaced += MarkLanesDirty;
            _station.Grid.ModuleRemoved += MarkLanesDirty;
        }

        private void OnDestroy()
        {
            if (_station != null && _station.Grid != null)
            {
                _station.Grid.ModulePlaced -= MarkLanesDirty;
                _station.Grid.ModuleRemoved -= MarkLanesDirty;
            }
        }

        private void MarkLanesDirty(ModuleInstance _) => _existingLanesDirty = true;

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
            if (keyboard == null || mouse == null || InputGate.Blocked)
                return;

            HandleSelectionKeys(keyboard);
            if (_selected == null)
                return;

            if ((keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame) || mouse.rightButton.wasPressedThisFrame)
            {
                Select(null);
                return;
            }

            if (KeyBindings.WasPressed(GameAction.Rotate))
            {
                _rotation = GridDirections.NormalizeRotation(_rotation + 1);
                Rotated?.Invoke();
            }

            if (UiPointer.IsOverUi())
                _hasTarget = false; // HUD 위에서는 고스트 숨김, 클릭 무시
            else
                UpdateTarget(mouse.position.ReadValue());
            UpdateGhost();
            UpdateLanes();

            if (_hasTarget && mouse.leftButton.wasPressedThisFrame)
            {
                if (!_targetValid)
                    PlaceRejected?.Invoke(_targetResult);
                else if (_station.TryPlace(_selected, _targetCell, _rotation, out var placed))
                    Placed?.Invoke(placed);
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
            UpdateLanes(); // 해제하면 접근로 표시도 숨김
            SelectionChanged?.Invoke(_selected);
        }

        /// <summary>건설 메뉴 버튼용: 같은 모듈을 다시 고르면 해제.</summary>
        public void ToggleSelect(ModuleData data)
        {
            Select(_selected == data ? null : data);
        }

        private void HandleSelectionKeys(Keyboard keyboard)
        {
            if (KeyBindings.WasPressed(GameAction.NextCategory))
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
            // 배치는 칸 상자(Visual)로 면을 판정한다 — 모델 모양 Pick 콜라이더(12-0)는 제외
            if (!Physics.Raycast(ray, out var hit, _maxRayDistance, ModulePick.PlacementMask(_moduleMask), QueryTriggerInteraction.Ignore))
                return;

            var hitCell = GridConfig.GetHitCell(hit.point, hit.normal);
            if (!_station.Grid.IsOccupied(hitCell))
                return; // 모듈이 아닌 콜라이더

            _targetCell = GridConfig.GetAdjacentCell(hit.point, hit.normal);
            var outward = _targetCell - hitCell;
            if (_selected.TerminalOnly)
                AutoOrientDock(outward);
            // 8-4: 면 안쪽으로 뻗는 셀이 있는 모듈(가운데 원점인 회전 링 등)은 그만큼 바깥으로 띄워 면에 붙인다
            _targetCell = PlacementRules.AnchorOnFace(_selected, _targetCell, outward, _rotation);
            _targetResult = _station.EvaluatePlacement(_selected, _targetCell, _rotation);
            _targetValid = _targetResult == PlacementResult.Valid;
            _hasTarget = true;
        }

        /// <summary>
        /// 8-0: 도킹은 클릭한 면에서 바깥쪽(normal)으로 입구가 향하도록 회전을 자동으로 맞춘다 (뒷면 = 클릭한 모듈).
        /// 위·아래 면이면 입구가 수평이라 맞출 수 없으므로 수동 회전 유지.
        /// </summary>
        private void AutoOrientDock(Vector3Int outward)
        {
            if (outward.y != 0)
                return;
            for (int r = 0; r < 4; r++)
            {
                if (PlacementRules.DockFrontWorld(_selected, r) == outward)
                {
                    _rotation = r;
                    return;
                }
            }
        }

        /// <summary>8-0: 배치 중 도킹 접근로 표시. 이미 지은 도킹 = 호박색, 고스트 도킹 = 고스트 색.</summary>
        private void UpdateLanes()
        {
            _laneUsed = 0;
            if (_selected != null)
            {
                if (_existingLanesDirty)
                    RebuildExistingLanes();
                foreach (var cell in _existingLanes)
                    AddLane(cell, _existingLaneColor);
                if (_hasTarget && _selected.TerminalOnly)
                {
                    PlacementRules.GetDockLane(_selected, _targetCell, _rotation, _laneScratch);
                    var c = _ghostColor;
                    c.a *= _ghostLaneAlpha;
                    foreach (var cell in _laneScratch)
                        AddLane(cell, c);
                }
            }
            for (int i = _laneUsed; i < _laneCells.Count; i++)
            {
                if (_laneCells[i].activeSelf)
                    _laneCells[i].SetActive(false);
            }
        }

        private void RebuildExistingLanes()
        {
            _existingLanesDirty = false;
            _existingLanes.Clear();
            foreach (var m in _station.Grid.Modules)
            {
                if (m.Data == null || !m.Data.TerminalOnly)
                    continue;
                PlacementRules.GetDockLane(m.Data, m.Origin, m.Rotation, _laneScratch);
                foreach (var cell in _laneScratch)
                {
                    if (!_station.Grid.IsOccupied(cell)) // 예전 규칙 도킹은 막혀 있을 수 있음
                        _existingLanes.Add(cell);
                }
            }
        }

        private void AddLane(Vector3Int cell, Color color)
        {
            if (_laneUsed >= _laneCells.Count)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "DockLane";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(_laneCellScale, _laneThickness, _laneCellScale) * GridConfig.CellSize;
                var r = go.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (_ghostMaterial != null)
                    r.sharedMaterial = _ghostMaterial;
                _laneCells.Add(go);
                _laneRenderers.Add(r);
            }
            var cellGo = _laneCells[_laneUsed];
            cellGo.transform.position = GridConfig.CellToWorld(cell);
            if (!cellGo.activeSelf)
                cellGo.SetActive(true);
            _propertyBlock.SetColor(BaseColorId, color);
            _laneRenderers[_laneUsed].SetPropertyBlock(_propertyBlock);
            _laneUsed++;
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
