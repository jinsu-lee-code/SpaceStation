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
        /// <summary>도킹(8-0)의 뒷면 칸에 붙을 모듈이 없음 (뒷면으로만 정거장에 연결).</summary>
        DockNeedsBackContact,
        /// <summary>도킹의 앞쪽 접근로 칸이 이미 막혀 있음.</summary>
        DockLaneBlocked,
        /// <summary>기존 도킹의 접근로 칸에 지으려 함.</summary>
        BlockedByDockLane,
        /// <summary>공간 규칙은 통과했으나 건설 비용이 부족함 (StationSimulation이 판정).</summary>
        InsufficientResources,
        /// <summary>현재 등급에서 아직 해금되지 않은 모듈 (StationProgression이 판정).</summary>
        ModuleLocked,
        /// <summary>등급별 최대 설치 수 도달 (채굴 도킹, StationProgression이 판정).</summary>
        LimitReached,
        /// <summary>코어 2층 옆 칸인데 바로 아래에 모듈이 없음 (7-7).</summary>
        NeedsSupport,
        /// <summary>코어 윗면 위인데 코어가 아닌 모듈과 옆면이 닿지 않음 (7-7, 연결 통로가 하나도 안 생김).</summary>
        CoreTopNeedsSideContact,
    }

    /// <summary>
    /// 공간 배치 규칙 (비용 제외). 그리드는 점유 충돌만 보고, 모듈 종류별 규칙은 여기서 판정한다.
    /// 받침 규칙 (7-7, 5-5 규칙을 완화): 어느 방향·층이든 자유. 단 <see cref="ModuleData.UpperSidesNeedSupport"/> 모듈(코어)은
    /// - 맨 위층 옆면에 맞닿는 칸: 바로 아래에 모듈이 있어야 한다 (코어 2층은 연결점 없는 탑이라 바로 붙이지 못함)
    /// - 윗면 위에 놓는 모듈: 코어가 아닌 모듈과 옆면이 하나 이상 닿아야 한다 (코어 윗면도 연결점이 없어 통로가 하나는 필요)
    /// 그 조건을 채워 주는 모듈은 철거할 수 없다(<see cref="SupportsOthers"/>). 운석 파괴는 막지 않는다.
    /// 도킹 규칙 (8-0, A안 "앞쪽 접근로"): <see cref="ModuleData.TerminalOnly"/> 모듈은 뒷면 칸에 모듈이 있어야 하고,
    /// 앞쪽 접근로(<see cref="ModuleData.ApproachLaneLength"/>칸)는 비어 있어야 하며 이후에도 그 칸에는 아무것도 못 짓는다.
    /// 옆·위·아래는 자유 (도킹끼리 나란히 가능). 예전 규칙으로 지은 도킹은 그대로 인정한다 (배치할 때만 검사).
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
            // 8-0: 기존 도킹의 앞쪽 접근로에는 아무것도 못 지음
            foreach (var cell in cells)
            {
                if (IsInDockLane(grid, cell))
                    return PlacementResult.BlockedByDockLane;
            }
            if (data.TerminalOnly)
            {
                // 도킹: 뒷면 칸에 모듈이 있어야 하고(연결), 앞쪽 접근로는 비어 있어야 한다. 옆·위·아래는 자유
                var front = DockFrontWorld(data, rotation);
                if (!grid.IsOccupied(origin - front))
                    return PlacementResult.DockNeedsBackContact;
                for (int k = 1; k <= data.ApproachLaneLength; k++)
                {
                    if (grid.IsOccupied(origin + front * k))
                        return PlacementResult.DockLaneBlocked;
                }
            }
            return CheckSupport(grid, cells, null);
        }

        /// <summary>
        /// 8-4: 모듈 면을 가리켜 지을 때의 원점. 회전한 셀 중 클릭한 면 쪽(outward 반대)으로 가장 튀어나온 층이
        /// 바로 그 면에 붙도록 원점을 바깥으로 민다 (원점이 가운데인 회전 링을 옆면에 붙일 때 기존 모듈과 겹치지 않게).
        /// 원점 층이 이미 가장 안쪽인 모듈(1칸·도킹, 바깥쪽으로 뻗는 2칸, 위·아래 면의 평평한 모듈)은 그대로.
        /// </summary>
        public static Vector3Int AnchorOnFace(ModuleData data, Vector3Int faceCell, Vector3Int outward, int rotation)
        {
            if (data == null || data.CellOffsets.Count <= 1 || outward == Vector3Int.zero)
                return faceCell;
            int min = 0;
            foreach (var c in StationGrid.ResolveCells(data.CellOffsets, Vector3Int.zero, rotation))
            {
                int d = c.x * outward.x + c.y * outward.y + c.z * outward.z;
                if (d < min)
                    min = d;
            }
            return faceCell - outward * min;
        }

        /// <summary>도킹 입구(앞) 방향 (월드 격자, 회전 반영).</summary>
        public static Vector3Int DockFrontWorld(ModuleData data, int rotation)
            => GridDirections.Rotate(data.DockFront, rotation);

        /// <summary>도킹 접근로 칸 목록 (원점 앞으로 1~길이 칸).</summary>
        public static void GetDockLane(ModuleData data, Vector3Int origin, int rotation, List<Vector3Int> results)
        {
            results.Clear();
            if (data == null || !data.TerminalOnly)
                return;
            var front = DockFrontWorld(data, rotation);
            for (int k = 1; k <= data.ApproachLaneLength; k++)
                results.Add(origin + front * k);
        }

        private const int MaxLaneScan = 8;

        /// <summary>이 칸이 이미 지어진 도킹의 접근로인지.</summary>
        public static bool IsInDockLane(StationGrid grid, Vector3Int cell)
        {
            foreach (var dir in GridDirections.Faces)
            {
                for (int k = 1; k <= MaxLaneScan; k++)
                {
                    if (!grid.TryGetModule(cell - dir * k, out var other))
                        continue;
                    if (other.Data != null && other.Data.TerminalOnly && other.Origin == cell - dir * k
                        && k <= other.Data.ApproachLaneLength && DockFrontWorld(other.Data, other.Rotation) == dir)
                        return true;
                    break; // 접근로 사이에 다른 모듈이 끼면 그 너머 도킹은 이 칸과 무관
                }
            }
            return false;
        }

        /// <summary>받침 조건 둘 (코어 2층 옆 = 아래 모듈, 코어 윗면 위 = 옆 모듈). ignore = 없다고 치는 모듈 (철거 판정).</summary>
        public static PlacementResult CheckSupport(StationGrid grid, IReadOnlyList<Vector3Int> cells, ModuleInstance ignore)
        {
            if (!IsSupported(grid, cells, ignore))
                return PlacementResult.NeedsSupport;
            if (IsOnCoreTop(grid, cells, ignore) && !HasSideContact(grid, cells, ignore))
                return PlacementResult.CoreTopNeedsSideContact;
            return PlacementResult.Valid;
        }

        /// <summary>칸 중 하나라도 <see cref="ModuleData.UpperSidesNeedSupport"/> 모듈의 윗면 바로 위에 있는지.</summary>
        public static bool IsOnCoreTop(StationGrid grid, IReadOnlyList<Vector3Int> cells, ModuleInstance ignore = null)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var below = cells[i] + Vector3Int.down;
                if (Contains(cells, below))
                    continue;
                if (grid.TryGetModule(below, out var other) && other != ignore && other.Data != null && other.Data.UpperSidesNeedSupport)
                    return true;
            }
            return false;
        }

        /// <summary>코어가 아닌(그리고 ignore가 아닌) 다른 모듈과 옆면(수평)이 하나라도 닿는지.</summary>
        private static bool HasSideContact(StationGrid grid, IReadOnlyList<Vector3Int> cells, ModuleInstance ignore)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    if (dir.y != 0)
                        continue;
                    var n = cells[i] + dir;
                    if (Contains(cells, n) || !grid.TryGetModule(n, out var other) || other == ignore)
                        continue;
                    if (other.Data != null && !other.Data.UpperSidesNeedSupport)
                        return true;
                }
            }
            return false;
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

        /// <summary>
        /// 이 모듈이 없어지면 받침 조건을 잃는 모듈이 있는지 (철거 불가 판정).
        /// 바로 위 칸(코어 2층 옆 받침)과 수평 이웃(코어 윗면 위 모듈의 옆 연결)만 영향받는다.
        /// </summary>
        public static bool SupportsOthers(StationGrid grid, ModuleInstance module)
        {
            if (module == null)
                return false;
            foreach (var cell in module.Cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    if (dir == Vector3Int.down)
                        continue;
                    if (!grid.TryGetModule(cell + dir, out var other) || other == module)
                        continue;
                    // 지금은 조건을 채우는데 이 모듈이 없으면 못 채우는 경우만 (예전 규칙 세이브의 기존 배치는 막지 않음)
                    if (CheckSupport(grid, other.Cells, module) != PlacementResult.Valid
                        && CheckSupport(grid, other.Cells, null) == PlacementResult.Valid)
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
