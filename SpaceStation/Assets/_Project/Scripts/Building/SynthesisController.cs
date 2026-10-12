using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace SpaceStation.Building
{
    /// <summary>
    /// 12-3 합성 위치 지정 (PHASESET A9): 레시피 창에서 고른 레시피의 규격 고스트를 재료 모듈 위에 겹쳐 띄운다.
    /// 마우스가 가리킨 모듈 칸을 포함하는 자리 중 합성 가능한 자리로 자동으로 붙고(지금 회전 우선, 없으면 다른 회전),
    /// 맞는 자리가 없으면 재료가 가장 많이 들어가는 자리를 빨갛게 보여 준다. 좌클릭 = 합성 시작, 회전 키 = 회전, 우클릭/ESC = 취소.
    /// 배치 고스트와 같은 홀로그램 재질 · 초록/빨강 색을 쓴다. 상태 표시줄 문구는 <see cref="Hint"/>.
    /// </summary>
    public sealed class SynthesisController : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private StationController _station;
        [SerializeField] private BuildController _build;
        [SerializeField] private Camera _camera;
        [SerializeField] private Material _ghostMaterial;
        [SerializeField] private Color _validColor = new Color(0.2f, 1f, 0.3f, 0.45f);
        [SerializeField] private Color _invalidColor = new Color(1f, 0.2f, 0.2f, 0.45f);
        [Tooltip("고스트가 재료 모듈을 덮도록 칸보다 조금 크게")]
        [SerializeField, Range(1f, 1.2f)] private float _ghostScale = 1.04f;
        [SerializeField] private LayerMask _moduleMask = ~0;
        [SerializeField] private float _maxRayDistance = 500f;

        private RecipeData _recipe;
        private int _rotation;
        private GameObject _ghost;
        private Renderer[] _ghostRenderers;
        private MaterialPropertyBlock _block;
        private bool _hasTarget;
        private Vector3Int _origin;
        private SynthesisResult _result;
        private readonly List<IngredientStatus> _statuses = new List<IngredientStatus>();
        private Color _appliedColor = new Color(-1f, -1f, -1f, -1f);

        /// <summary>합성 위치 지정 중인 레시피 (null = 꺼짐).</summary>
        public RecipeData Recipe => _recipe;
        public bool Active => _recipe != null;
        public bool HasTarget => _hasTarget;
        public SynthesisResult TargetResult => _result;
        /// <summary>상태 표시줄 안내 (꺼져 있으면 빈 문자열).</summary>
        public string Hint { get; private set; } = string.Empty;

        public event Action<RecipeData> RecipeChanged;
        public event Action<SynthesisJob> Started;
        public event Action<SynthesisResult> Rejected;

        private SynthesisSystem Synthesis => _station != null && _station.Simulation != null ? _station.Simulation.Synthesis : null;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (_camera == null)
                _camera = Camera.main;
        }

        private void Start()
        {
            if (_build != null)
                _build.SelectionChanged += HandleBuildSelected;
        }

        private void OnDestroy()
        {
            if (_build != null)
                _build.SelectionChanged -= HandleBuildSelected;
        }

        /// <summary>건설 모듈을 고르면 합성 위치 지정은 끝.</summary>
        private void HandleBuildSelected(ModuleData data)
        {
            if (data != null)
                Begin(null);
        }

        /// <summary>레시피 위치 지정 시작 (null = 끝).</summary>
        public void Begin(RecipeData recipe)
        {
            if (recipe == _recipe)
                return;
            _recipe = recipe;
            _rotation = 0;
            _hasTarget = false;
            if (recipe != null && _build != null)
                _build.Select(null);
            RebuildGhost();
            UpdateHint();
            RecipeChanged?.Invoke(recipe);
        }

        private void Update()
        {
            if (_recipe == null)
                return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null || InputGate.Blocked || Synthesis == null)
                return;

            if ((keyboard.escapeKey.wasPressedThisFrame && !InputGate.EscapeConsumedThisFrame) || mouse.rightButton.wasPressedThisFrame)
            {
                if (keyboard.escapeKey.wasPressedThisFrame)
                    InputGate.ConsumeEscape();
                Begin(null);
                return;
            }
            if (KeyBindings.WasPressed(GameAction.Rotate))
                _rotation = GridDirections.NormalizeRotation(_rotation + 1);

            if (UiPointer.IsOverUi())
                _hasTarget = false;
            else
                UpdateTarget(mouse.position.ReadValue());
            UpdateGhost();
            UpdateHint();

            if (_hasTarget && mouse.leftButton.wasPressedThisFrame)
            {
                var result = Synthesis.TryStart(_recipe, _origin, _rotation, out var job);
                if (result == SynthesisResult.Valid)
                {
                    Started?.Invoke(job);
                    Begin(null);
                }
                else
                    Rejected?.Invoke(result);
            }
        }

        // ---------------- 자리 찾기 ----------------

        private void UpdateTarget(Vector2 screenPosition)
        {
            _hasTarget = false;
            var ray = _camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out var hit, _maxRayDistance, ModulePick.PlacementMask(_moduleMask), QueryTriggerInteraction.Ignore))
                return;
            var cell = GridConfig.GetHitCell(hit.point, hit.normal);
            if (!_station.Grid.IsOccupied(cell))
                return;

            // 지금 회전에서 가리킨 칸을 포함하는 자리들 → 합성 가능한 자리, 없으면 다른 회전의 합성 가능한 자리, 없으면 지금 회전의 가장 그럴듯한 자리
            if (TryFind(cell, _rotation, out var origin, out bool valid) && valid)
            {
                Set(origin, _rotation);
                return;
            }
            for (int r = 1; r < 4; r++)
            {
                int rot = GridDirections.NormalizeRotation(_rotation + r);
                if (TryFind(cell, rot, out var o, out bool v) && v)
                {
                    _rotation = rot; // 맞는 회전으로 돌려 줌
                    Set(o, rot);
                    return;
                }
            }
            Set(origin, _rotation);
        }

        /// <summary>가리킨 칸이 규격의 각 칸이 되는 원점들 중 가장 좋은 것 (합성 가능 > 재료 칸 많음 > 앞쪽).</summary>
        private bool TryFind(Vector3Int cell, int rotation, out Vector3Int best, out bool valid)
        {
            best = cell;
            valid = false;
            int bestScore = int.MinValue;
            var offsets = StationGrid.ResolveCells(_recipe.Result.CellOffsets, Vector3Int.zero, rotation);
            foreach (var o in offsets)
            {
                var origin = cell - o;
                var result = Synthesis.Evaluate(_recipe, origin, rotation);
                int score = result == SynthesisResult.Valid ? 1000 : MaterialCells(origin, offsets);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = origin;
                    valid = result == SynthesisResult.Valid;
                }
            }
            return bestScore > int.MinValue;
        }

        /// <summary>규격 칸 중 레시피 재료 모듈이 있는 칸 수 (맞는 자리가 없을 때 가장 그럴듯한 자리 고르기).</summary>
        private int MaterialCells(Vector3Int origin, Vector3Int[] offsets)
        {
            int n = 0;
            foreach (var o in offsets)
                if (_station.Grid.TryGetModule(origin + o, out var m) && m.Data != null && _recipe.CountOf(m.Data) > 0)
                    n++;
            return n;
        }

        private void Set(Vector3Int origin, int rotation)
        {
            _origin = origin;
            _result = Synthesis.Evaluate(_recipe, origin, rotation, null, _statuses);
            _hasTarget = true;
        }

        // ---------------- 고스트 ----------------

        private void UpdateGhost()
        {
            if (_ghost == null)
                return;
            _ghost.SetActive(_hasTarget);
            if (!_hasTarget)
                return;
            _ghost.transform.SetPositionAndRotation(GridConfig.CellToWorld(_origin), GridDirections.ToQuaternion(_rotation));
            var color = _result == SynthesisResult.Valid ? _validColor : _invalidColor;
            if ((Vector4)color == (Vector4)_appliedColor)
                return;
            _appliedColor = color;
            _block.SetColor(BaseColorId, color);
            foreach (var r in _ghostRenderers)
                r.SetPropertyBlock(_block);
        }

        private void RebuildGhost()
        {
            if (_ghost != null)
                Destroy(_ghost);
            _ghost = null;
            _ghostRenderers = null;
            _appliedColor = new Color(-1f, -1f, -1f, -1f);
            if (_recipe == null || _recipe.Result == null)
                return;

            // 결과 모듈 모양을 칸 상자로 (모델이 생기기 전에도, 모델이 있어도 규격이 잘 보이게)
            _ghost = new GameObject("SynthesisGhost_" + _recipe.name);
            _ghost.transform.SetParent(transform, false);
            var renderers = new List<Renderer>();
            foreach (var o in _recipe.Result.CellOffsets)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = cube.GetComponent<Collider>();
                col.enabled = false;
                Destroy(col);
                cube.transform.SetParent(_ghost.transform, false);
                cube.transform.localPosition = GridConfig.CellToWorld(o) - GridConfig.CellToWorld(Vector3Int.zero);
                cube.transform.localScale = Vector3.one * (GridConfig.CellSize * _ghostScale);
                var r = cube.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (_ghostMaterial != null)
                    r.sharedMaterial = _ghostMaterial;
                renderers.Add(r);
            }
            _ghostRenderers = renderers.ToArray();
            _ghost.SetActive(false);
        }

        // ---------------- 안내 ----------------

        private void UpdateHint()
        {
            if (_recipe == null)
            {
                Hint = string.Empty;
                return;
            }
            var sb = new StringBuilder();
            sb.Append("<b>").Append(_recipe.DisplayName).Append("</b> 합성  ·  재료 위에 규격을 맞춰 좌클릭  ·  ")
              .Append(KeyBindings.Label(GameAction.Rotate)).Append(": 회전  ·  우클릭/ESC: 취소");
            if (_hasTarget && _result != SynthesisResult.Valid)
                sb.Append("\n<color=#FF6B5E>합성 불가: ").Append(Reason(_result, _statuses)).Append("</color>");
            else if (_hasTarget)
                sb.Append("\n<color=#7CFF9A>합성 가능</color>");
            Hint = sb.ToString();
        }

        /// <summary>판정 사유 (상태 표시줄 · 레시피 창 공용).</summary>
        public static string Reason(SynthesisResult result, IReadOnlyList<IngredientStatus> statuses = null)
        {
            switch (result)
            {
                case SynthesisResult.Valid: return "합성 가능";
                case SynthesisResult.InvalidRecipe: return "레시피 정의 오류";
                case SynthesisResult.Locked: return "아직 열리지 않은 레시피";
                case SynthesisResult.EmptyCell: return "규격 안에 빈 칸이 있음" + Shortfall(statuses);
                case SynthesisResult.Overhang: return "규격 밖으로 튀어나온 모듈이 있음";
                case SynthesisResult.Mixed: return "레시피에 없는 모듈이 섞여 있음" + Extra(statuses);
                case SynthesisResult.Missing: return "재료 구성이 다름" + Shortfall(statuses);
                case SynthesisResult.Busy: return "이미 합성 중인 재료가 있음";
                case SynthesisResult.Damaged: return "파손 · 수리 중인 재료가 있음";
                case SynthesisResult.InsufficientResources: return "자원 부족";
                default: return result.ToString();
            }
        }

        private static string Shortfall(IReadOnlyList<IngredientStatus> statuses)
        {
            if (statuses == null)
                return string.Empty;
            var sb = new StringBuilder();
            foreach (var s in statuses)
            {
                if (s.Needed <= 0 || s.Present == s.Needed || s.Module == null)
                    continue;
                sb.Append(sb.Length == 0 ? " (" : ", ").Append(s.Module.DisplayName).Append(' ').Append(s.Present).Append('/').Append(s.Needed);
            }
            return sb.Length > 0 ? sb.Append(')').ToString() : string.Empty;
        }

        private static string Extra(IReadOnlyList<IngredientStatus> statuses)
        {
            if (statuses == null)
                return string.Empty;
            var sb = new StringBuilder();
            foreach (var s in statuses)
            {
                if (s.Needed > 0 || s.Module == null)
                    continue;
                sb.Append(sb.Length == 0 ? " (" : ", ").Append(s.Module.DisplayName);
            }
            return sb.Length > 0 ? sb.Append(')').ToString() : string.Empty;
        }
    }
}
