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
        /// <summary>코어 2층 옆 칸인데 바로 아래에 모듈이 없음 (7-7).</summary>
        NeedsSupport,
    }

    /// <summary>
    /// 공간 배치 규칙 (비용 제외). 그리드는 점유 충돌만 보고, 모듈 종류별 규칙은 여기서 판정한다.
    /// 받침 규칙 (7-7, 5-5 규칙을 완화): 어느 방향·층이든 자유. 단 <see cref="ModuleData.UpperSidesNeedSupport"/> 모듈(코어)의
    /// 맨 위층 옆면에 맞닿는 칸은 바로 아래에 모듈이 있어야 한다 (코어 2층은 연결점 없는 탑이라 바로 붙이지 못함).
    /// 그 칸을 받치고 있는 모듈은 철거할 수 없다(<see cref="SupportsOthers"/>). 운석 파괴는 막지 않는다.
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
            if (!IsSupported(grid, cells, null))
                return PlacementResult.NeedsSupport;
            return PlacementResult.Valid;
        }

        /// <summary>
        /// 받침 판정 (7-7). 코어 2층 옆 칸(<see cref="NeedsSupportAt"/>)에 놓인 칸마다 바로 아래에 (ignore가 아닌) 모듈이
        /// 있거나 같은 모듈의 칸이 있어야 한다. 그 밖의 칸은 조건 없음.
        /// </summary>
        public static bool IsSupported(StationGrid grid, IReadOnlyList<Vector3Int> cells, ModuleInstance ignore)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (!NeedsSupportAt(grid, cells[i], ignore))
                    continue;
                var below = cells[i] + Vector3Int.down;
                if (Contains(cells, below))
                    continue;
                if (!grid.TryGetModule(below, out var other) || other == ignore)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 이 칸이 받침이 필요한 칸인지: 수평으로 맞닿은 칸이 <see cref="ModuleData.UpperSidesNeedSupport"/> 모듈의
        /// 맨 위층(여러 층일 때만)에 속하면 참.
        /// </summary>
        public static bool NeedsSupportAt(StationGrid grid, Vector3Int cell, ModuleInstance ignore = null)
        {
            foreach (var dir in GridDirections.Faces)
            {
                if (dir.y != 0)
                    continue;
                if (!grid.TryGetModule(cell + dir, out var other) || other == ignore || other.Data == null || !other.Data.UpperSidesNeedSupport)
                    continue;
                int minY = int.MaxValue, maxY = int.MinValue;
                foreach (var c in other.Cells)
                {
                    minY = Mathf.Min(minY, c.y);
                    maxY = Mathf.Max(maxY, c.y);
                }
                if (maxY > minY && cell.y == maxY)
                    return true;
            }
            return false;
        }

        /// <summary>이 모듈이 없어지면 받침을 잃는 모듈이 있는지 (철거 불가 판정). 바로 위 칸의 모듈만 영향받는다.</summary>
        public static bool SupportsOthers(StationGrid grid, ModuleInstance module)
        {
            if (module == null)
                return false;
            foreach (var cell in module.Cells)
            {
                if (!grid.TryGetModule(cell + Vector3Int.up, out var other) || other == module)
                    continue;
                if (!IsSupported(grid, other.Cells, module))
                    return true;
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
