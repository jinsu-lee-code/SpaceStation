using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 칸 묶음을 감싸는 상자 (12-2b). 칸끼리 체비셰프 거리를 비교하기 전에 상자끼리 먼 쌍을 빠르게 거른다.
    /// 상자 거리는 칸 쌍 거리의 하한이라 <see cref="InRange"/>는 <see cref="DefenseSystem.InRange"/>와 결과가 같다.
    /// </summary>
    public readonly struct CellBox
    {
        public readonly Vector3Int Min;
        public readonly Vector3Int Max;

        public CellBox(IReadOnlyList<Vector3Int> cells)
        {
            if (cells == null || cells.Count == 0)
            {
                // 빈 묶음: 어떤 상자와도 범위 밖 (DefenseSystem.InRange도 false)
                Min = new Vector3Int(int.MaxValue / 2, int.MaxValue / 2, int.MaxValue / 2);
                Max = new Vector3Int(int.MinValue / 2, int.MinValue / 2, int.MinValue / 2);
                return;
            }
            var min = cells[0];
            var max = cells[0];
            for (int i = 1; i < cells.Count; i++)
            {
                min = Vector3Int.Min(min, cells[i]);
                max = Vector3Int.Max(max, cells[i]);
            }
            Min = min;
            Max = max;
        }

        /// <summary>두 상자 사이 체비셰프 거리 (겹치면 0) — 두 묶음의 어떤 칸 쌍도 이보다 가깝지 않다.</summary>
        public int Distance(in CellBox other)
        {
            int dx = Mathf.Max(0, Mathf.Max(Min.x - other.Max.x, other.Min.x - Max.x));
            int dy = Mathf.Max(0, Mathf.Max(Min.y - other.Max.y, other.Min.y - Max.y));
            int dz = Mathf.Max(0, Mathf.Max(Min.z - other.Max.z, other.Min.z - Max.z));
            return Mathf.Max(dx, Mathf.Max(dy, dz));
        }

        /// <summary><see cref="DefenseSystem.InRange"/>와 같은 판정. 상자가 반경보다 멀면 칸 비교를 생략한다.</summary>
        public static bool InRange(IReadOnlyList<Vector3Int> a, in CellBox boxA, IReadOnlyList<Vector3Int> b, in CellBox boxB, int radius)
            => boxA.Distance(boxB) <= radius && DefenseSystem.InRange(a, b, radius);
    }
}
