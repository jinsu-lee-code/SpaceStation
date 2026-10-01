using System;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>5-6: 모듈 모델의 칸·면 방향별 표면 깊이 (회전 0 기준, 칸 중심에서 면 방향으로). 음수 = 연결점 없음.</summary>
    [Serializable]
    public struct FaceDepth
    {
        public Vector3Int Cell;
        public Vector3Int Direction;
        public float Depth;

        public FaceDepth(Vector3Int cell, Vector3Int direction, float depth)
        {
            Cell = cell;
            Direction = direction;
            Depth = depth;
        }
    }
}
