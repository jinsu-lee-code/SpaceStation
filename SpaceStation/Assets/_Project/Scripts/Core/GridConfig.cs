using UnityEngine;

namespace SpaceStation.Core
{
    /// <summary>
    /// 격자 규격. 셀 크기는 이 상수 하나로만 관리한다.
    /// 셀 좌표는 셀 중심을 가리킨다 (모듈 피벗 = 원점 셀 중심).
    /// </summary>
    public static class GridConfig
    {
        public const float CellSize = 1f;

        public static Vector3 CellToWorld(Vector3Int cell)
        {
            return new Vector3(cell.x, cell.y, cell.z) * CellSize;
        }

        public static Vector3Int WorldToCell(Vector3 world)
        {
            return new Vector3Int(
                Mathf.RoundToInt(world.x / CellSize),
                Mathf.RoundToInt(world.y / CellSize),
                Mathf.RoundToInt(world.z / CellSize));
        }

        /// <summary>셀 표면의 한 점과 법선으로 "맞은 셀"을 구한다 (표면에서 반 칸 안쪽).</summary>
        public static Vector3Int GetHitCell(Vector3 hitPoint, Vector3 hitNormal)
        {
            Vector3Int n = DominantAxis(hitNormal);
            return WorldToCell(hitPoint - (Vector3)n * (CellSize * 0.5f));
        }

        /// <summary>맞은 면의 바깥쪽 인접 셀 (고스트를 놓을 셀).</summary>
        public static Vector3Int GetAdjacentCell(Vector3 hitPoint, Vector3 hitNormal)
        {
            return GetHitCell(hitPoint, hitNormal) + DominantAxis(hitNormal);
        }

        /// <summary>법선을 가장 가까운 6방향 단위 벡터로 반올림한다.</summary>
        public static Vector3Int DominantAxis(Vector3 v)
        {
            float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
            if (ax >= ay && ax >= az)
                return new Vector3Int(v.x >= 0f ? 1 : -1, 0, 0);
            if (ay >= az)
                return new Vector3Int(0, v.y >= 0f ? 1 : -1, 0);
            return new Vector3Int(0, 0, v.z >= 0f ? 1 : -1);
        }
    }
}
