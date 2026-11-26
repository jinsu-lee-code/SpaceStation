using System;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 5-6: 모듈 모델의 칸·면 방향별 표면 깊이 (회전 0 기준, 칸 중심에서 면 방향으로).
    /// 옛 값 (Linked 꺼짐) = 음수면 연결점 없음.
    /// ⑪ (2026-11-26): Linked = 연결점 있음 — 이때 Depth는 음수일 수 있음 (위아래 기둥이 칸 가운데보다 낮은 받침 윗면까지 내려감).
    /// Deck = 3 · 4단계 다리 데크 높이 (칸 가운데 −0.12)에서 잰 깊이 (0 = 따로 없음 → Depth).
    /// Land = 1 · 2단계 얇은 받침 위에 옆 통로 끝을 내려앉힐 높이 (칸 가운데 기준 음수, 0 = 내려앉지 않음 — 칸 가운데 높이 그대로),
    ///  LandDepth = 그 높이의 통로 끝 깊이, LandMin = 경사가 가파를 때 받침 위로 밀어 넣을 수 있는 가장 안쪽 깊이.
    /// </summary>
    [Serializable]
    public struct FaceDepth
    {
        public Vector3Int Cell;
        public Vector3Int Direction;
        public float Depth;
        public bool Linked;
        public float Deck;
        public float Land;
        public float LandDepth;
        public float LandMin;

        /// <summary>옛 방식: 음수 = 연결점 없음.</summary>
        public FaceDepth(Vector3Int cell, Vector3Int direction, float depth)
        {
            Cell = cell;
            Direction = direction;
            Depth = depth;
            Linked = false;
            Deck = 0f;
            Land = 0f;
            LandDepth = 0f;
            LandMin = 0f;
        }

        /// <summary>연결점 있음 (깊이는 음수도 됨) + 데크 깊이 (0 = 없음).</summary>
        public static FaceDepth Link(Vector3Int cell, Vector3Int direction, float depth, float deck)
        {
            return new FaceDepth(cell, direction, depth) { Linked = true, Deck = deck };
        }

        /// <summary>연결점 있음 + 데크 깊이 + 받침 위 내려앉기 (land = 끝 높이 음수, landDepth = 그 높이 깊이, landMin = 가장 안쪽).</summary>
        public static FaceDepth Link(Vector3Int cell, Vector3Int direction, float depth, float deck, float land, float landDepth, float landMin)
        {
            return new FaceDepth(cell, direction, depth) { Linked = true, Deck = deck, Land = land, LandDepth = landDepth, LandMin = landMin };
        }

        public bool HasLink => Linked || Depth >= 0f;
        public bool Lands => Land < 0f;
    }
}
