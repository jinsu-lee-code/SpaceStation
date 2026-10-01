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
        /// <summary>바닥층이 아닌데 받침이 없음 (위층은 아래, 아래층은 위에 모듈 필요).</summary>
        NeedsSupport,
    }

    /// <summary>
    /// 공간 배치 규칙 (비용 제외). 그리드는 점유 충돌만 보고, 모듈 종류별 규칙은 여기서 판정한다.
    /// 받침 규칙 (5-5): 바닥층(<see cref="GroundLevel"/>, 코어 1층)은 자유. 그보다 위층은 모듈 칸 중 하나라도
    /// 바로 아래에 모듈이 있어야 하고, 아래층(매달기)은 하나라도 바로 위에 모듈이 있어야 한다 → 공중에 뜬 배치 방지.
    /// 다른 모듈을 받치고 있는 모듈은 철거할 수 없다(<see cref="SupportsOthers"/>). 운석 파괴는 막지 않는다.
    /// </summary>
    public static class PlacementRules
    {
        /// <summary>바닥층 높이 (코어 1층).</summary>
        public const int GroundLevel = 0;

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
            if (!IsSupported(grid, cells, null))
                return PlacementResult.NeedsSupport;
            return PlacementResult.Valid;
        }

        /// <summary>
        /// 받침 판정. 바닥층에 걸치면 항상 참. 위층이면 맨 아래 칸들 중 하나라도 바로 아래에, 아래층이면 맨 위 칸들 중
        /// 하나라도 바로 위에 (ignore가 아닌) 모듈이 있어야 한다.
        /// </summary>
        public static bool IsSupported(StationGrid grid, IReadOnlyList<Vector3Int> cells, ModuleInstance ignore)
        {
            int minY = int.MaxValue, maxY = int.MinValue;
            for (int i = 0; i < cells.Count; i++)
            {
                minY = Mathf.Min(minY, cells[i].y);
                maxY = Mathf.Max(maxY, cells[i].y);
            }
            if (minY <= GroundLevel && maxY >= GroundLevel)
                return true;
            bool above = minY > GroundLevel;
            int edge = above ? minY : maxY;
            var dir = above ? Vector3Int.down : Vector3Int.up;
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].y != edge)
                    continue;
                var below = cells[i] + dir;
                if (Contains(cells, below))
                    continue;
                if (!grid.TryGetModule(below, out var other) || other == ignore)
                    continue;
                if (above && other.Data != null && !other.Data.SupportsTop)
                    continue; // 윗면 받침 불가 모듈 (코어)
                return true;
            }
            return false;
        }

        /// <summary>이 모듈이 없어지면 받침을 잃는 모듈이 있는지 (철거 불가 판정).</summary>
        public static bool SupportsOthers(StationGrid grid, ModuleInstance module)
        {
            if (module == null)
                return false;
            foreach (var cell in module.Cells)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    if (!grid.TryGetModule(cell + Vector3Int.up * s, out var other) || other == module)
                        continue;
                    if (!IsSupported(grid, other.Cells, module))
                        return true;
                }
            }
            return false;
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
