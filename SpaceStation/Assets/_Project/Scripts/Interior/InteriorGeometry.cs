using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>벽 패널 한 장 (키트 패널 폭 <see cref="InteriorGeometry.PanelWidth"/>을 Width에 맞춰 늘려 씀).</summary>
    public readonly struct WallPanel
    {
        /// <summary>패널 가운데 (벽 바깥면, 패널 높이의 가운데). 내부 루트 기준 m.</summary>
        public readonly Vector3 Center;
        /// <summary>바깥 방향 (방 밖).</summary>
        public readonly Vector3 Normal;
        public readonly float Width;
        public readonly bool Door;

        public WallPanel(Vector3 center, Vector3 normal, float width, bool door)
        {
            Center = center;
            Normal = normal;
            Width = width;
            Door = door;
        }
    }

    /// <summary>바닥·천장 사각형 한 칸.</summary>
    public readonly struct FloorRect
    {
        public readonly Vector3Int Cell;
        /// <summary>가운데 (바닥 윗면 / 천장 아랫면 높이).</summary>
        public readonly Vector3 Center;
        public readonly Vector2 Size; // x, z
        public readonly bool Ceiling;
        public readonly bool Hatch;

        public FloorRect(Vector3Int cell, Vector3 center, Vector2 size, bool ceiling, bool hatch)
        {
            Cell = cell;
            Center = center;
            Size = size;
            Ceiling = ceiling;
            Hatch = hatch;
        }
    }

    /// <summary>모듈 사이 연결 튜브 하나 (A 벽 바깥면 → B 벽 바깥면, 바닥 높이).</summary>
    public readonly struct TubeSpan
    {
        public readonly Vector3 Start;
        public readonly Vector3 Direction;
        public readonly float Length;

        public TubeSpan(Vector3 start, Vector3 direction, float length)
        {
            Start = start;
            Direction = direction;
            Length = length;
        }

        public Vector3 End => Start + Direction * Length;
    }

    /// <summary>
    /// Phase 11-3 내부 규격 (1칸 = 8m)과 템플릿이 없는 모듈의 대체 방(벽 키트) 배치 계산 (순수 로직, 좌표는 내부 루트 기준 m).
    /// 바닥 = 칸 중심 −1.3, 천장 높이 3.6. 대체 방 벽은 칸 중심에서 <see cref="FallbackDepth"/>, 같은 모듈 쪽 면은 칸 경계까지 이어진다.
    /// 문은 벽 줄의 칸 가운데, 모듈 사이는 벽 바깥면끼리 잇는 튜브.
    /// </summary>
    public static class InteriorGeometry
    {
        public const float CellSize = 8f;
        /// <summary>바닥 윗면 (칸 중심 기준).</summary>
        public const float FloorOffset = -1.3f;
        public const float RoomHeight = 3.6f;
        /// <summary>대체 방 벽 바깥면까지 (칸 중심 기준).</summary>
        public const float FallbackDepth = 3.2f;
        public const float PanelWidth = 3.2f;
        public const float PanelHeight = 4f;
        /// <summary>패널 가운데 높이 (바닥 윗면 기준): 키트 패널은 바닥이 패널 가운데에서 −1.8.</summary>
        public const float PanelCenterAboveFloor = 1.8f;
        public const float Thickness = 0.2f;
        public const float DoorWidth = 1.4f;
        public const float DoorHeight = 2.4f;
        public const float HatchSize = 1.4f;
        public const float TubeHalfWidth = 1.25f;
        public const float TubeHeight = 2.8f;

        public static Vector3 CellCenter(Vector3Int cell) => new Vector3(cell.x, cell.y, cell.z) * CellSize;
        public static float FloorY(Vector3Int cell) => cell.y * CellSize + FloorOffset;

        private static readonly Vector3Int[] Horizontal =
        {
            Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        /// <summary>칸에서 그 방향 벽(또는 같은 모듈이면 칸 경계)까지 거리.</summary>
        public static float Extent(ICollection<Vector3Int> cells, Vector3Int cell, Vector3Int dir, float depth)
        {
            return cells.Contains(cell + dir) ? CellSize * 0.5f : depth;
        }

        /// <summary>
        /// 대체 방 벽 패널: 같은 모듈이 아닌 수평 면마다 한 줄(칸 범위, 같은 모듈 쪽은 경계까지).
        /// <paramref name="doors"/>에 든 면(칸, 방향)은 가운데에 문 패널. 바로 위가 같은 모듈인 칸은 위층 바닥까지 한 줄 더 쌓는다.
        /// </summary>
        public static void FallbackWalls(IReadOnlyList<Vector3Int> cells, ICollection<(Vector3Int, Vector3Int)> doors, List<WallPanel> results)
        {
            results.Clear();
            var set = new HashSet<Vector3Int>(cells);
            foreach (var c in cells)
            {
                foreach (var d in Horizontal)
                {
                    if (set.Contains(c + d))
                        continue;
                    var t = d.z != 0 ? Vector3Int.right : new Vector3Int(0, 0, 1);
                    float from = -Extent(set, c, -t, FallbackDepth);
                    float to = Extent(set, c, t, FallbackDepth);
                    var normal = (Vector3)d;
                    var tangent = (Vector3)t;
                    var plane = CellCenter(c) + normal * FallbackDepth;
                    plane.y = FloorY(c) + PanelCenterAboveFloor;
                    int rows = set.Contains(c + Vector3Int.up) ? 2 : 1;
                    for (int row = 0; row < rows; row++)
                    {
                        var rowPlane = plane + Vector3.up * (PanelHeight * row);
                        bool door = row == 0 && doors != null && doors.Contains((c, d));
                        if (door)
                        {
                            Fill(rowPlane, tangent, normal, from, -PanelWidth * 0.5f, results);
                            results.Add(new WallPanel(rowPlane, normal, PanelWidth, true));
                            Fill(rowPlane, tangent, normal, PanelWidth * 0.5f, to, results);
                        }
                        else
                        {
                            Fill(rowPlane, tangent, normal, from, to, results);
                        }
                    }
                }
            }
        }

        /// <summary>구간을 패널 폭에 가까운 같은 폭 패널로 채운다.</summary>
        private static void Fill(Vector3 plane, Vector3 tangent, Vector3 normal, float from, float to, List<WallPanel> results)
        {
            float length = to - from;
            if (length < 0.05f)
                return;
            int n = Mathf.Max(1, Mathf.RoundToInt(length / PanelWidth));
            float w = length / n;
            for (int i = 0; i < n; i++)
                results.Add(new WallPanel(plane + tangent * (from + w * (i + 0.5f)), normal, w, false));
        }

        /// <summary>대체 방 바닥·천장: 아래(위)가 같은 모듈이면 없음. <paramref name="hatches"/>에 든 면은 가운데 해치.</summary>
        public static void FallbackFloors(IReadOnlyList<Vector3Int> cells, ICollection<(Vector3Int, Vector3Int)> hatches, List<FloorRect> results)
        {
            results.Clear();
            var set = new HashSet<Vector3Int>(cells);
            foreach (var c in cells)
            {
                float xMin = -Extent(set, c, Vector3Int.left, FallbackDepth), xMax = Extent(set, c, Vector3Int.right, FallbackDepth);
                float zMin = -Extent(set, c, new Vector3Int(0, 0, -1), FallbackDepth), zMax = Extent(set, c, new Vector3Int(0, 0, 1), FallbackDepth);
                var center = CellCenter(c) + new Vector3((xMin + xMax) * 0.5f, 0f, (zMin + zMax) * 0.5f);
                var size = new Vector2(xMax - xMin, zMax - zMin);
                if (!set.Contains(c + Vector3Int.down))
                {
                    var p = center;
                    p.y = FloorY(c);
                    results.Add(new FloorRect(c, p, size, false, hatches != null && hatches.Contains((c, Vector3Int.down))));
                }
                if (!set.Contains(c + Vector3Int.up))
                {
                    var p = center;
                    p.y = FloorY(c) + RoomHeight;
                    results.Add(new FloorRect(c, p, size, true, hatches != null && hatches.Contains((c, Vector3Int.up))));
                }
            }
        }

        /// <summary>A 칸에서 방향 d로 B 칸까지 튜브 (양쪽 벽 바깥면 깊이).</summary>
        public static TubeSpan Tube(Vector3Int cellA, Vector3Int dir, float depthA, float depthB)
        {
            var normal = (Vector3)dir;
            var start = CellCenter(cellA) + normal * depthA;
            start.y = FloorY(cellA);
            return new TubeSpan(start, normal, CellSize - depthA - depthB);
        }
    }
}
