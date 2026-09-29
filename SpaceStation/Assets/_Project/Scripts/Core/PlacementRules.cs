using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Core
{
    public enum PlacementResult
    {
        Valid,
        InvalidDefinition,
        Occupied,
        /// <summary>말단 전용 모듈인데 맞닿은 면이 정확히 1개가 아님.</summary>
        TerminalNeedsSingleContact,
        /// <summary>기존 말단 모듈(채굴 도킹)의 면에 붙이려 함.</summary>
        BlockedByTerminal,
        /// <summary>공간 규칙은 통과했으나 건설 비용이 부족함 (StationSimulation이 판정).</summary>
        InsufficientResources,
        /// <summary>현재 등급에서 아직 해금되지 않은 모듈 (StationProgression이 판정).</summary>
        ModuleLocked,
        /// <summary>등급별 최대 설치 수 도달 (채굴 도킹, StationProgression이 판정).</summary>
        LimitReached,
    }

    /// <summary>
    /// 공간 배치 규칙 (비용 제외). 그리드는 점유 충돌만 보고, 모듈 종류별 규칙은 여기서 판정한다.
    /// </summary>
    public static class PlacementRules
    {
        public static bool CanPlace(StationGrid grid, ModuleData data, Vector3Int origin, int rotation)
        {
            return Evaluate(grid, data, origin, rotation) == PlacementResult.Valid;
        }

        public static PlacementResult Evaluate(StationGrid grid, ModuleData data, Vector3Int origin, int rotation)
        {
            if (data == null || data.CellOffsets.Count == 0)
                return PlacementResult.InvalidDefinition;
            if (!grid.CanPlace(data, origin, rotation))
                return PlacementResult.Occupied;

            var cells = StationGrid.ResolveCells(data.CellOffsets, origin, rotation);
            int contacts = 0;
            foreach (var cell in cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    var neighbor = cell + dir;
                    if (Contains(cells, neighbor) || !grid.TryGetModule(neighbor, out var other))
                        continue;
                    if (other.Data != null && other.Data.TerminalOnly)
                        return PlacementResult.BlockedByTerminal;
                    contacts++;
                }
            }

            if (data.TerminalOnly && contacts != 1)
                return PlacementResult.TerminalNeedsSingleContact;
            return PlacementResult.Valid;
        }

        private static bool Contains(IReadOnlyList<Vector3Int> cells, Vector3Int cell)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] == cell)
                    return true;
            }
            return false;
        }
    }
}
