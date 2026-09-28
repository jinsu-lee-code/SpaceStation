using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Core
{
    /// <summary>면 인접 6방향과 90도 단위(Y축) 회전 계산.</summary>
    public static class GridDirections
    {
        public static readonly IReadOnlyList<Vector3Int> Faces = new[]
        {
            Vector3Int.right, Vector3Int.left,
            Vector3Int.up, Vector3Int.down,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        /// <summary>회전 단계를 0~3으로 정규화한다.</summary>
        public static int NormalizeRotation(int rotation)
        {
            int r = rotation % 4;
            return r < 0 ? r + 4 : r;
        }

        /// <summary>오프셋을 Y축 기준 시계방향(위에서 볼 때) 90도 × rotation 만큼 회전한다.</summary>
        public static Vector3Int Rotate(Vector3Int offset, int rotation)
        {
            switch (NormalizeRotation(rotation))
            {
                case 1: return new Vector3Int(offset.z, offset.y, -offset.x);
                case 2: return new Vector3Int(-offset.x, offset.y, -offset.z);
                case 3: return new Vector3Int(-offset.z, offset.y, offset.x);
                default: return offset;
            }
        }
    }
}
