using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 방어·서비스 모듈 범위 표시 (4-8, 4-9): 배치 중인 모듈의 고스트 위치, 또는 선택한 모듈 주위에 반투명 상자.
    /// 상자 = 모듈 셀 경계 + 반경 칸 (체비셰프 거리이므로 정육면체).
    /// M-125 의료 · 여가 모듈을 고르면 그 요구를 절반도 못 받는 집을 붉게 표시 (어느 집이 비는지 집을 하나씩 눌러 봐야 알았음).
    /// </summary>
    public sealed class DefenseRangeView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private BuildController _build;
        [SerializeField] private ModuleSelectionController _selection;
        [Tooltip("콜라이더 없는 큐브 (반투명 재질)")]
        [SerializeField] private Renderer _box;
        [SerializeField] private Color _shieldColor = new Color(0.3f, 0.8f, 1f, 0.12f);
        [SerializeField] private Color _turretColor = new Color(1f, 0.6f, 0.2f, 0.12f);
        [SerializeField] private Color _serviceColor = new Color(0.45f, 1f, 0.55f, 0.12f); // 4-9 의료·여가
        [SerializeField] private Color _controlColor = new Color(1f, 0.85f, 0.25f, 0.12f); // 8-3 손상 통제실
        [SerializeField] private Color _decoyColor = new Color(0.85f, 0.85f, 0.9f, 0.12f); // 8-5 장갑 격벽 끌어오기
        [SerializeField] private Color _lackColor = new Color(1f, 0.3f, 0.25f, 0.3f); // M-125 요구를 못 받는 집

        private const float LackCoverage = 0.5f; // 이 비율 미만이면 표시
        private const float LackRefresh = 0.5f;

        private static bool HasRange(ModuleData data) => data != null && (data.IsDefense || data.IsService);

        private MaterialPropertyBlock _block;
        private MaterialPropertyBlock _lackBlock;
        private Color? _appliedColor;
        private readonly List<Renderer> _lackMarks = new List<Renderer>();
        private int _lackShown;
        private float _nextLack;
        private ModuleData _lackFor;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _lackBlock = new MaterialPropertyBlock();
            _lackBlock.SetColor(BaseColorId, _lackColor);
            _box.gameObject.SetActive(false);
        }

        /// <summary>M-125 고른 서비스 모듈이 맡는 요구를 못 받는 집 표시 (서비스 모듈이 아니면 끔).</summary>
        private void RefreshLack(ModuleData service)
        {
            if (service == null)
            {
                _lackFor = null;
                ShowLack(0);
                return;
            }
            if (service == _lackFor && Time.unscaledTime < _nextLack)
                return;
            _lackFor = service;
            _nextLack = Time.unscaledTime + LackRefresh;
            var sim = _build.Station != null ? _build.Station.Simulation : null;
            if (sim == null)
                return;
            int used = 0;
            foreach (var m in sim.Grid.Modules)
            {
                if (!Lacks(sim.Needs, m, service))
                    continue;
                if (used == _lackMarks.Count)
                    _lackMarks.Add(Instantiate(_box, _box.transform.parent));
                PlaceMark(_lackMarks[used++], m);
            }
            ShowLack(used);
        }

        private static bool Lacks(SpaceStation.Simulation.NeedsSystem needs, ModuleInstance module, ModuleData service)
        {
            foreach (var need in service.ServiceNeeds)
            {
                float coverage = needs.GetHabitatCoverage(module, need);
                if (coverage >= 0f && coverage < LackCoverage)
                    return true;
            }
            return false;
        }

        private void PlaceMark(Renderer mark, ModuleInstance module)
        {
            var min = module.Cells[0];
            var max = module.Cells[0];
            foreach (var c in module.Cells)
            {
                min = Vector3Int.Min(min, c);
                max = Vector3Int.Max(max, c);
            }
            var t = mark.transform;
            t.position = (GridConfig.CellToWorld(min) + GridConfig.CellToWorld(max)) * 0.5f;
            t.rotation = Quaternion.identity;
            t.localScale = ((Vector3)(max - min + Vector3Int.one) + Vector3.one * 0.1f) * GridConfig.CellSize;
            mark.SetPropertyBlock(_lackBlock);
        }

        private void ShowLack(int count)
        {
            for (int i = count; i < _lackShown && i < _lackMarks.Count; i++)
                _lackMarks[i].gameObject.SetActive(false);
            for (int i = _lackShown; i < count; i++)
                _lackMarks[i].gameObject.SetActive(true);
            _lackShown = count;
        }

        private void LateUpdate()
        {
            var picked = _build.Selected != null ? _build.Selected : _selection.Selected != null ? _selection.Selected.Data : null;
            RefreshLack(picked != null && picked.IsService ? picked : null);

            ModuleData data = null;
            IReadOnlyList<Vector3Int> cells = null;
            var building = _build.Selected;
            if (building != null)
            {
                if (HasRange(building) && _build.HasTarget)
                {
                    data = building;
                    cells = StationGrid.ResolveCells(building.CellOffsets, _build.TargetCell, _build.Rotation);
                }
            }
            else if (_selection.Selected != null && HasRange(_selection.Selected.Data))
            {
                data = _selection.Selected.Data;
                cells = _selection.Selected.Cells;
            }

            bool show = data != null;
            if (_box.gameObject.activeSelf != show)
                _box.gameObject.SetActive(show);
            if (!show)
                return;

            // Phase 6 연구 반경 반영
            var fx = _build.Station != null && _build.Station.Simulation != null ? _build.Station.Simulation.Effects : null;
            int radius = fx != null
                ? Mathf.Max(fx.DecoyRadius(data), Mathf.Max(fx.ControlRadius(data), Mathf.Max(fx.ShieldRadius(data), Mathf.Max(fx.TurretRadius(data), fx.ServiceRadius(data)))))
                : Mathf.Max(data.DecoyRadius, Mathf.Max(data.ControlRadius, Mathf.Max(data.ShieldRadius, Mathf.Max(data.TurretRadius, data.IsService ? data.ServiceRadius : 0))));
            var min = cells[0];
            var max = cells[0];
            foreach (var c in cells)
            {
                min = Vector3Int.Min(min, c);
                max = Vector3Int.Max(max, c);
            }
            var t = _box.transform;
            t.position = (GridConfig.CellToWorld(min) + GridConfig.CellToWorld(max)) * 0.5f;
            t.rotation = Quaternion.identity;
            t.localScale = (Vector3)(max - min + Vector3Int.one * (1 + 2 * radius)) * GridConfig.CellSize;

            var color = data.IsService ? _serviceColor : data.IsShield ? _shieldColor
                : data.IsDamageControl ? _controlColor : data.IsDecoy ? _decoyColor : _turretColor;
            if (_appliedColor != color)
            {
                _appliedColor = color;
                _block.SetColor(BaseColorId, color);
                _box.SetPropertyBlock(_block);
            }
        }
    }
}
