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
    }
}
