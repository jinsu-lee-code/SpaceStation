using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>발코니 바닥 띠 하나. 폭은 <see cref="InteriorBalcony.DeckWidth"/> 고정, 길이 방향으로만 늘린다.</summary>
    public readonly struct BalconyDeck
    {
        /// <summary>윗면 가운데 (내부 루트 기준 m).</summary>
        public readonly Vector3 Center;
        /// <summary>길이 방향 (단위).</summary>
        public readonly Vector3 Along;
        /// <summary>트인 쪽(홀 가운데) 방향 (단위).</summary>
        public readonly Vector3 Inward;
        public readonly float Length;

        public BalconyDeck(Vector3 center, Vector3 along, Vector3 inward, float length)
        {
            Center = center;
            Along = along;
            Inward = inward;
            Length = length;
        }
    }

    /// <summary>난간 한 줄 (바닥 높이의 시작·끝점).</summary>
    public readonly struct BalconyRail
    {
        public readonly Vector3 Start;
        public readonly Vector3 End;

        public BalconyRail(Vector3 start, Vector3 end)
        {
            Start = start;
            End = end;
        }

        public float Length => Vector3.Distance(Start, End);
    }

    /// <summary>
    /// Phase 11-2b 여러 층 모듈(코어 2×2×2)의 높은 홀: 위층 칸은 바닥 대신 벽을 따라 발코니 띠 + 트인 쪽 난간,
    /// 홀 가운데 나선 계단(맨 위 디딤판 방향 = 다리 방향) + 다리 하나로 발코니에 잇는다 (순수 계산, 좌표는 내부 루트 기준 m).
    /// 계단은 가운데라 벽의 문 배치와 무관하다. 바닥 넓이가 2×2보다 작으면 계단 없이 발코니만.
    /// </summary>
    public static class InteriorBalcony
    {
        public const float DeckWidth = 1.4f;
        public const float StairRadius = 1.4f;
        /// <summary>디딤판 높이·회전 (높이 8m면 40장, 두 바퀴).</summary>
        public const float StepRise = 0.2f;
        public const float StepAngle = 18f;
        public const float BridgeWidth = 1.4f;

        public sealed class Plan
        {
            public readonly List<BalconyDeck> Decks = new List<BalconyDeck>();
            public readonly List<BalconyRail> Rails = new List<BalconyRail>();
            public bool HasStair;
            /// <summary>계단 축 바닥점 (아래층 바닥 윗면).</summary>
            public Vector3 StairBase;
            public float StairRise;
            public int StairSteps => Mathf.RoundToInt(StairRise / StepRise);
            /// <summary>맨 위 디딤판·다리 방향 (수평 단위).</summary>
            public Vector3 BridgeDirection;
            public BalconyDeck Bridge;

            public bool IsEmpty => Decks.Count == 0;
        }

        private static readonly Vector3Int[] Horizontal =
        {
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1), Vector3Int.right, Vector3Int.left,
        };

        /// <param name="cells">한 모듈의 칸 (월드 셀).</param>
        /// <param name="wallDepth">칸 중심에서 벽 바깥면까지 (같은 모듈 쪽 면은 칸 경계).</param>
        /// <param name="floorOffset">칸 중심 기준 바닥 윗면 높이.</param>
        public static Plan Build(IReadOnlyList<Vector3Int> cells, float roomSize, float wallDepth, float floorOffset)
        {
            var plan = new Plan();
            var set = new HashSet<Vector3Int>(cells);
            float half = roomSize * 0.5f;
            int minY = int.MaxValue;
            foreach (var c in cells)
                minY = Mathf.Min(minY, c.y);

            // 1) 위층 칸마다 벽 쪽 발코니 띠. z쪽 벽 띠는 칸 끝까지, x쪽 벽 띠는 z쪽 벽이 있는 끝을 잘라 모서리 겹침을 없앤다
            foreach (var c in cells)
            {
                if (!set.Contains(c + Vector3Int.down))
                    continue;
                var center = new Vector3(c.x, c.y, c.z) * roomSize;
                float floorY = center.y + floorOffset;
                foreach (var d in Horizontal)
                {
                    if (set.Contains(c + d))
                        continue;
                    var dir = (Vector3)d;
                    var along = d.z != 0 ? Vector3.right : new Vector3(0f, 0f, 1f);
                    // 길이 방향 양 끝의 수직 벽 유무
                    bool wallMinus = !set.Contains(c - new Vector3Int((int)along.x, 0, (int)along.z));
                    bool wallPlus = !set.Contains(c + new Vector3Int((int)along.x, 0, (int)along.z));
                    float extMinus = wallMinus ? wallDepth : half;
                    float extPlus = wallPlus ? wallDepth : half;
                    float railFrom = -extMinus + (wallMinus ? DeckWidth : 0f);
                    float railTo = extPlus - (wallPlus ? DeckWidth : 0f);
                    float deckFrom = d.z != 0 ? -extMinus : railFrom;
                    float deckTo = d.z != 0 ? extPlus : railTo;
                    var edge = center + dir * (wallDepth - DeckWidth);
                    edge.y = floorY;
                    var deckCenter = center + dir * (wallDepth - DeckWidth * 0.5f) + along * ((deckFrom + deckTo) * 0.5f);
                    deckCenter.y = floorY;
                    plan.Decks.Add(new BalconyDeck(deckCenter, along, -dir, deckTo - deckFrom));
                    if (railTo > railFrom + 0.01f)
                        plan.Rails.Add(new BalconyRail(edge + along * railFrom, edge + along * railTo));
                }
            }
            if (plan.IsEmpty)
                return plan;

            // 2) 가운데 나선 계단: 아래층 바닥 넓이의 가운데, 트인 공간이 계단 반지름보다 넓을 때만
            var lower = new List<Vector3Int>();
            foreach (var c in cells)
            {
                if (c.y == minY)
                    lower.Add(c);
            }
            Vector3Int lo = lower[0], hi = lower[0];
            foreach (var c in lower)
            {
                lo = Vector3Int.Min(lo, c);
                hi = Vector3Int.Max(hi, c);
            }
            float spanX = (hi.x - lo.x) * roomSize + wallDepth * 2f;
            float spanZ = (hi.z - lo.z) * roomSize + wallDepth * 2f;
            float voidHalf = Mathf.Min(spanX, spanZ) * 0.5f - DeckWidth;
            if (voidHalf < StairRadius + 0.5f)
                return plan;
            var mid = (new Vector3(lo.x, minY, lo.z) + new Vector3(hi.x, minY, hi.z)) * 0.5f * roomSize;
            var stairBase = new Vector3(mid.x, minY * roomSize + floorOffset, mid.z);
            float upperFloor = stairBase.y + roomSize;
            plan.HasStair = true;
            plan.StairBase = stairBase;
            plan.StairRise = roomSize;
            plan.BridgeDirection = new Vector3(0f, 0f, -1f);

            // 3) 다리: 계단 끝에서 -z 쪽 발코니까지, 그 자리 난간은 끊는다
            var b = plan.BridgeDirection;
            float from = StairRadius - 0.1f;
            float to = voidHalf + 0.1f;
            var bridgeCenter = new Vector3(stairBase.x, upperFloor, stairBase.z) + b * ((from + to) * 0.5f);
            plan.Bridge = new BalconyDeck(bridgeCenter, b, Vector3.right, to - from);
            var side = Vector3.Cross(Vector3.up, b).normalized * (BridgeWidth * 0.5f);
            var start = new Vector3(stairBase.x, upperFloor, stairBase.z) + b * StairRadius;
            var end = new Vector3(stairBase.x, upperFloor, stairBase.z) + b * voidHalf;
            CutGap(plan.Rails, end, side.magnitude, b);
            plan.Rails.Add(new BalconyRail(start + side, end + side));
            plan.Rails.Add(new BalconyRail(start - side, end - side));
            return plan;
        }

        /// <summary>다리 끝이 닿는 발코니 난간(다리 방향에 수직인 줄)에서 다리 폭만큼 잘라낸다.</summary>
        private static void CutGap(List<BalconyRail> rails, Vector3 point, float halfWidth, Vector3 normal)
        {
            for (int i = rails.Count - 1; i >= 0; i--)
            {
                var r = rails[i];
                var dir = r.End - r.Start;
                float len = dir.magnitude;
                if (len < 0.001f)
                    continue;
                dir /= len;
                if (Mathf.Abs(Vector3.Dot(dir, normal)) > 0.01f)
                    continue; // 다리와 나란한 줄
                if (Mathf.Abs(Vector3.Dot(point - r.Start, normal)) > 0.01f || Mathf.Abs(point.y - r.Start.y) > 0.01f)
                    continue; // 다른 줄
                float t = Vector3.Dot(point - r.Start, dir);
                if (t < -halfWidth || t > len + halfWidth)
                    continue;
                rails.RemoveAt(i);
                if (t - halfWidth > 0.01f)
                    rails.Add(new BalconyRail(r.Start, r.Start + dir * (t - halfWidth)));
                if (len - (t + halfWidth) > 0.01f)
                    rails.Add(new BalconyRail(r.Start + dir * (t + halfWidth), r.End));
            }
        }
    }
}
