using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 방어 모듈 범위 표시 (4-8): 배치 중인 방어 모듈의 고스트 위치, 또는 선택한 방어 모듈 주위에 반투명 상자.
    /// 상자 = 모듈 셀 경계 + 반경 칸 (체비셰프 거리이므로 정육면체).
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

        private MaterialPropertyBlock _block;
        private Color? _appliedColor;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            _box.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            ModuleData data = null;
            IReadOnlyList<Vector3Int> cells = null;
            var building = _build.Selected;
            if (building != null)
            {
                if (building.IsDefense && _build.HasTarget)
                {
                    data = building;
                    cells = StationGrid.ResolveCells(building.CellOffsets, _build.TargetCell, _build.Rotation);
                }
            }
            else if (_selection.Selected != null && _selection.Selected.Data != null && _selection.Selected.Data.IsDefense)
            {
                data = _selection.Selected.Data;
                cells = _selection.Selected.Cells;
            }

            bool show = data != null;
            if (_box.gameObject.activeSelf != show)
                _box.gameObject.SetActive(show);
            if (!show)
                return;

            int radius = Mathf.Max(data.ShieldRadius, data.TurretRadius);
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

            var color = data.IsShield ? _shieldColor : _turretColor;
            if (_appliedColor != color)
            {
                _appliedColor = color;
                _block.SetColor(BaseColorId, color);
                _box.SetPropertyBlock(_block);
            }
        }
    }
}
