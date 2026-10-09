using System;
using System.Collections.Generic;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>패드 미니맵의 칸 하나 (위에서 본 x · z).</summary>
    public struct PadMapCell
    {
        public Vector2Int Cell;
        public ModuleInstance Module;
        /// <summary>지금 내부(연결된 방)에 들어 있는 방 — 빠른 이동 가능.</summary>
        public bool Reachable;
        /// <summary>오른쪽(+x) · 위쪽(+z) 이웃 칸이 같은 모듈 (지도에서 이어 그림).</summary>
        public bool JoinX, JoinZ;
        /// <summary>위 · 아래 층으로 가는 해치가 있는 칸.</summary>
        public bool HatchUp, HatchDown;
    }

    /// <summary>
    /// 11-12 휴대 패드 미니맵 (순수 로직): 층(y) 하나를 위에서 본 칸 목록. 모듈 색 · 상태(파손 · 비활성)는 그리는 쪽이 정함.
    /// </summary>
    public static class PadMap
    {
        /// <param name="reachable">지금 내부에 있는 방인지 (null이면 모두 아님).</param>
        /// <param name="hatch">칸에서 방향(위 · 아래)으로 해치가 있는지 (null이면 없음).</param>
        public static void Build(StationGrid grid, int floor, Func<ModuleInstance, bool> reachable,
            Func<Vector3Int, Vector3Int, bool> hatch, List<PadMapCell> result)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            result.Clear();
            foreach (var module in grid.Modules)
            {
                bool reach = reachable != null && reachable(module);
                foreach (var cell in module.Cells)
                {
                    if (cell.y != floor)
                        continue;
                    result.Add(new PadMapCell
                    {
                        Cell = new Vector2Int(cell.x, cell.z),
                        Module = module,
                        Reachable = reach,
                        JoinX = grid.TryGetModule(cell + Vector3Int.right, out var mx) && mx == module,
                        JoinZ = grid.TryGetModule(cell + new Vector3Int(0, 0, 1), out var mz) && mz == module,
                        HatchUp = hatch != null && hatch(cell, Vector3Int.up),
                        HatchDown = hatch != null && hatch(cell, Vector3Int.down),
                    });
                }
            }
        }

        /// <summary>모듈이 있는 층 목록 (아래 → 위).</summary>
        public static List<int> Floors(StationGrid grid)
        {
            var set = new SortedSet<int>();
            foreach (var module in grid.Modules)
                foreach (var cell in module.Cells)
                    set.Add(cell.y);
            return new List<int>(set);
        }

        /// <summary>층 목록에서 지금 층의 위(+1) · 아래(−1) 층 (없으면 그대로).</summary>
        public static int Step(IReadOnlyList<int> floors, int current, int direction)
        {
            int best = current;
            foreach (var f in floors)
            {
                if (direction > 0 && f > current && (best == current || f < best))
                    best = f;
                if (direction < 0 && f < current && (best == current || f > best))
                    best = f;
            }
            return best;
        }

        /// <summary>내부 월드 위치 → 칸 단위 실수 좌표 (x · z, 미니맵 위 내 위치). 칸 가운데 = 정수.</summary>
        public static Vector2 ToMap(Vector3 world, Vector3 interiorOrigin, float cellSize)
        {
            var local = (world - interiorOrigin) / cellSize;
            return new Vector2(local.x, local.z);
        }
    }
}
