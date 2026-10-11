using System;
using System.Collections.Generic;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-17 실행 중에 소품 붙일 자리를 광선으로 고르는 공용 함수 (현장 수리 손상 지점 · 보급 상자 · 주민 요청 물건 · 환풍구).
    /// 씨앗 난수로 섞으므로 같은 씨앗이면 다시 만들어도 같은 자리.
    /// </summary>
    public static class InteriorSpots
    {
        /// <summary>
        /// 벽 자리 후보 = 방 칸마다 가운데 <paramref name="height"/>(바닥에서) 높이에서 12방향 수평 광선이 맞은 벽 — 수직이고 평평하며
        /// (가로 ±0.32 · 세로 ±0.28 네 점이 같은 면), 앞 0.4m가 비어 있고, 문 · 해치 · 유리 · 주민 · 다른 상호작용 물건이 아닌 곳. 씨앗 순서로 섞음.
        /// </summary>
        public static void Walls(InteriorBuilder builder, ModuleInstance module, System.Random rng, float height, List<(Vector3 Position, Vector3 Normal)> result)
        {
            result.Clear();
            // 문 자리 키트 패널(막힌 벽 KIT_Wall · 문 KIT_WallDoor)은 충돌체 없이 벽면(Solid)보다 방 안쪽으로 튀어나와 있어
            // 그 자리에 붙이면 패널 뒤에 가려짐 (11-17 ③ 환풍구) → 패널 범위 안은 뺌
            _kitBounds.Clear();
            var roomParent = builder.RoomParent(module);
            if (roomParent != null)
            {
                foreach (var r in roomParent.GetComponentsInChildren<Renderer>())
                {
                    if (r.name.StartsWith("KIT_Wall", StringComparison.Ordinal))
                    {
                        var b = r.bounds;
                        b.Expand(0.2f);
                        _kitBounds.Add(b);
                    }
                }
            }
            float reach = InteriorGeometry.CellSize * 0.6f;
            foreach (var cell in module.Cells)
            {
                var origin = builder.FloorPoint(cell) + Vector3.up * height;
                if (Physics.CheckSphere(origin, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                float start = (float)rng.NextDouble() * 30f;
                for (int k = 0; k < 12; k++)
                {
                    float a = (start + k * 30f) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    if (!Physics.Raycast(origin, dir, out var hit, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        continue;
                    if (hit.distance < 0.6f || Mathf.Abs(hit.normal.y) > 0.2f || Excluded(hit.collider))
                        continue;
                    var n = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                    if (!Flat(hit.point, n) || !Clear(hit.point, n) || InKitPanel(hit.point + n * 0.05f))
                        continue;
                    result.Add((hit.point, n));
                }
            }
            for (int i = result.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (result[i], result[j]) = (result[j], result[i]);
            }
        }

        /// <summary>
        /// 바닥 자리: 방 칸 가운데에서 <paramref name="minRadius"/>~<paramref name="maxRadius"/>m 떨어진 곳 — 평평하고(아래 광선이 같은 높이 바닥),
        /// <paramref name="halfExtents"/>(바닥 위 상자 반 크기)만큼 비었고, <paramref name="used"/>와 <paramref name="spacing"/>m 이상.
        /// </summary>
        public static bool Floor(InteriorBuilder builder, ModuleInstance module, System.Random rng, Vector3 halfExtents, IReadOnlyList<Vector3> used, float spacing,
            out Vector3 spot, float minRadius = 1f, float maxRadius = 2.6f)
        {
            spot = default;
            var cells = module.Cells;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var cell = cells[rng.Next(cells.Count)];
                var floor = builder.FloorPoint(cell);
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = minRadius + (float)rng.NextDouble() * (maxRadius - minRadius);
                var p = floor + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 1.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                if (Mathf.Abs(hit.point.y - floor.y) > 0.06f || hit.normal.y < 0.95f)
                    continue;
                p = hit.point;
                if (Physics.CheckBox(p + Vector3.up * (halfExtents.y + 0.04f), halfExtents, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                bool far = true;
                if (used != null)
                {
                    foreach (var u in used)
                    {
                        if ((u - p).sqrMagnitude < spacing * spacing)
                        {
                            far = false;
                            break;
                        }
                    }
                }
                if (!far)
                    continue;
                spot = p + Vector3.up * 0.002f;
                return true;
            }
            return false;
        }

        private static readonly List<Bounds> _kitBounds = new List<Bounds>();

        private static bool InKitPanel(Vector3 p)
        {
            foreach (var b in _kitBounds)
            {
                if (b.Contains(p))
                    return true;
            }
            return false;
        }

        private static bool Excluded(Collider c)
        {
            if (c.GetComponentInParent<InteriorHatch>() != null || c.GetComponentInParent<ResidentFigure>() != null || c.GetComponentInParent<InteriorInteractable>() != null)
                return true;
            string n = c.name;
            return n.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool Flat(Vector3 point, Vector3 n)
        {
            var t = Vector3.Cross(Vector3.up, n).normalized;
            foreach (var o in new[] { t * 0.32f, -t * 0.32f, Vector3.up * 0.28f, Vector3.down * 0.28f })
            {
                var from = point + o + n * 0.25f;
                if (!Physics.Raycast(from, -n, out var h, 0.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return false; // 뚫린 곳 (문 자리 · 창)
                if (Mathf.Abs(h.distance - 0.25f) > 0.03f || Vector3.Dot(h.normal, n) < 0.95f)
                    return false;
            }
            return true;
        }

        private static bool Clear(Vector3 point, Vector3 n)
        {
            return !Physics.CheckBox(point + n * 0.24f, new Vector3(0.36f, 0.34f, 0.2f), Quaternion.LookRotation(n), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }
    }
}
