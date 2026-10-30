using System;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-6b 이민선 길 · 장면 카메라 자리 고르기 (사용자 요청 2026-10-19: 카메라 자리에 모듈이 있으면 다른 각도로).
    /// 칸이 차 있는지만 받아 계산하는 순수 함수 — 배가 지나는 길 · 카메라 자리 · 카메라와 배 사이가 모듈에 막히지 않는 후보를 차례로 고른다.
    /// 화면 연출만 쓰고 시뮬레이션은 바꾸지 않는다 (<see cref="ArkDocking"/>과 같음).
    /// </summary>
    public static class ArkShot
    {
        private const int PathSamples = 60;
        private const int FollowSamples = 40;
        private const float LineStep = 0.25f; // 시야 검사 간격 (칸)

        // ---------------- 배가 지나는 길 ----------------

        /// <summary>접근로: 멀리 옆 위 → 접근로 → 코(도킹 점), t = 0 ~ 1. side = 휘어 들어오는 쪽 (길이 1).</summary>
        public static Vector3 Approach(Vector3 nose, Vector3 outward, Vector3 side, float t)
        {
            var up = Vector3.up;
            return Bezier(nose + outward * 42f + up * 9f + side * 12f, nose + outward * 20f + up * 4f + side * 6f, nose + outward * 6f + up * 0.3f, nose, t);
        }

        /// <summary>떠나는 길의 뒷부분: 물러난 자리(코 + 바깥 2.5칸)에서 돌아 옆 위로 멀어짐, e = 0 ~ 1. side = 빠져나가는 쪽.</summary>
        public static Vector3 Leave(Vector3 nose, Vector3 outward, Vector3 side, float e)
        {
            var up = Vector3.up;
            return Bezier(nose + outward * 2.5f, nose + outward * 6f + side * 2f, nose + outward * 14f + up * 4f + side * 8f, nose + outward * 40f + up * 10f + side * 16f, e);
        }

        /// <summary>길이 모듈 칸을 지나는 횟수 (t = 0 ~ 1을 고르게 나눠 봄).</summary>
        public static int PathHits(Func<Vector3Int, bool> occupied, Func<float, Vector3> path)
        {
            int hits = 0;
            for (int i = 0; i <= PathSamples; i++)
                if (occupied(GridConfig.WorldToCell(path(i / (float)PathSamples))))
                    hits++;
            return hits;
        }

        /// <summary>두 쪽 중 덜 막힌 쪽 (+1 = preferred, −1 = 반대). 같으면 preferred.</summary>
        public static float PickSide(Func<Vector3Int, bool> occupied, Func<Vector3, Func<float, Vector3>> pathFor, Vector3 preferred)
        {
            int a = PathHits(occupied, pathFor(preferred));
            if (a == 0)
                return 1f;
            int b = PathHits(occupied, pathFor(-preferred));
            return b < a ? -1f : 1f;
        }

        // ---------------- 배를 따라가는 카메라 ----------------

        /// <summary>따라가는 카메라 후보 (배 기준 옆 · 높이). 왼쪽 위가 기본, 막히면 오른쪽 → 더 높이 → 바로 뒤 높이.</summary>
        public static readonly (float side, float lift)[] FollowOptions = { (-1f, 0.55f), (1f, 0.55f), (-1f, 1.3f), (1f, 1.3f), (0f, 2.6f) };

        /// <summary>배 뒤 · 옆 · 위 카메라 자리. side −1 = 배의 왼쪽, lift = 배 길이 배수 높이.</summary>
        public static Vector3 FollowCamera(Vector3 shipPos, Vector3 forward, float shipLength, float side, float lift)
        {
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            return shipPos - forward * (shipLength * 1.9f) + right * (side * shipLength * 0.9f) + Vector3.up * (shipLength * lift);
        }

        /// <summary>
        /// 따라가는 동안(t = 0 ~ until) 카메라가 모듈 속에 들어가거나 배가 모듈에 가려지는 순간이 가장 적은 후보 번호.
        /// 막힘이 없는 첫 후보를 바로 고른다.
        /// </summary>
        public static int PickFollow(Func<Vector3Int, bool> occupied, Func<float, Vector3> path, float until, float shipLength)
        {
            int best = 0, bestHits = int.MaxValue;
            for (int o = 0; o < FollowOptions.Length; o++)
            {
                int hits = 0;
                for (int i = 0; i <= FollowSamples; i++)
                {
                    float t = until * i / FollowSamples;
                    var p = path(t);
                    var ahead = path(Mathf.Min(1f, t + 0.01f)) - p;
                    if (ahead.sqrMagnitude < 1e-8f)
                        continue;
                    var cam = FollowCamera(p, ahead.normalized, shipLength, FollowOptions[o].side, FollowOptions[o].lift);
                    if (!Free(occupied, cam) || !Clear(occupied, cam, p, 0f))
                        hits++;
                }
                if (hits == 0)
                    return o;
                if (hits < bestHits)
                {
                    best = o;
                    bestHits = hits;
                }
            }
            return best;
        }

        // ---------------- 포트 옆 고정 시점 ----------------

        /// <summary>
        /// 포트 옆 고정 시점: 포트와 컷 순간의 배가 한 화면에 들어오게 접근로 옆으로 물러난 자리.
        /// 후보 = 가까운 거리부터 · 낮은 높이부터 · 따라가던 카메라와 같은 쪽(sidePreference) 먼저 → 카메라 자리가 비고, 포트 · 컷 순간의 배 · 붙은 배가 모듈에 가려지지 않는 첫 자리.
        /// 모두 막히면 정거장 꼭대기보다 높은 곳에서 내려다봄 (stationTop = 가장 높은 모듈 칸의 높이).
        /// </summary>
        public static Vector3 PickFixed(Func<Vector3Int, bool> occupied, Vector3 nose, Vector3 cutShip, Vector3 dockedShip, float lookWeight, float sidePreference, float stationTop, out bool overhead)
        {
            overhead = false;
            var span = cutShip - nose;
            var mid = Vector3.Lerp(nose, cutShip, 0.4f);
            var across = Vector3.Cross(Vector3.up, span.normalized);
            if (across.sqrMagnitude < 1e-4f)
                across = Vector3.right;
            across.Normalize();
            float d = Mathf.Max(2f, span.magnitude * 0.85f);
            float pref = sidePreference >= 0f ? 1f : -1f;
            var look = Vector3.Lerp(nose, cutShip, lookWeight);
            foreach (var k in new[] { 1f, 1.25f, 1.5f })
                foreach (var h in new[] { 0.35f, 0.8f })
                    foreach (var s in new[] { pref, -pref })
                    {
                        var p = mid + across * (s * d * k) + Vector3.up * (d * k * h);
                        if (Free(occupied, p) && Clear(occupied, p, look, 0f) && Clear(occupied, p, cutShip, 0f) && Clear(occupied, p, dockedShip, 0f) && Clear(occupied, p, nose, 0.6f))
                            return p;
                    }
            overhead = true;
            var above = Vector3.Lerp(nose, dockedShip, 0.5f) + across * (pref * d * 0.3f);
            above.y = Mathf.Max(stationTop + 3f * GridConfig.CellSize, nose.y + d);
            return above;
        }

        /// <summary>독백 동안 다가갈 자리: from → lookAt 사이 push 비율부터 0.1씩 줄여, 가는 길 · 그 자리가 모듈에 막히지 않는 곳.</summary>
        public static Vector3 PushIn(Func<Vector3Int, bool> occupied, Vector3 from, Vector3 lookAt, float push)
        {
            for (float f = push; f > 0.05f; f -= 0.1f)
            {
                var p = Vector3.Lerp(from, lookAt, f);
                if (Free(occupied, p) && Clear(occupied, from, p, 0f))
                    return p;
            }
            return from;
        }

        // ---------------- 공통 ----------------

        /// <summary>카메라 자리 둘레(앞뒤 한 칸씩, 3×3×3)에 모듈이 없음 — 가까운 면에 화면이 파묻히지 않게.</summary>
        public static bool Free(Func<Vector3Int, bool> occupied, Vector3 p)
        {
            var c = GridConfig.WorldToCell(p);
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (occupied(c + new Vector3Int(x, y, z)))
                            return false;
            return true;
        }

        /// <summary>a → b 사이에 모듈 칸이 없음 (b 앞 stopShort 칸은 뺌 — 모듈 면에 붙은 점을 볼 때).</summary>
        public static bool Clear(Func<Vector3Int, bool> occupied, Vector3 a, Vector3 b, float stopShort)
        {
            var dir = b - a;
            float length = dir.magnitude - stopShort * GridConfig.CellSize;
            if (length <= 0f)
                return true;
            dir.Normalize();
            for (float s = 0f; s <= length; s += LineStep * GridConfig.CellSize)
                if (occupied(GridConfig.WorldToCell(a + dir * s)))
                    return false;
            return true;
        }

        public static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }
    }
}
