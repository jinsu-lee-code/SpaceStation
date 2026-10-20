using System;
using System.Collections.Generic;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-17 실행 중에 소품 붙일 자리를 광선으로 고르는 공용 함수 (현장 수리 손상 지점 · 돌발 상황 · 보급 상자 · 주민 요청 물건 · 환풍구).
    /// 씨앗 난수로 섞으므로 같은 씨앗이면 다시 만들어도 같은 자리.
    /// 12-5 플레이어가 걸어가 손이 닿는 자리만 (사용자 요청 2026-10-20: 행정 관제소 창구 뒤 칸막이에 배관 누출이 생겼음 → 가기 어려운 곳 전부):
    /// 방마다 바닥을 격자로 훑어 플레이어 몸(반지름 0.3)이 서는 칸 중 들어오는 곳 · 칸 가운데에서 이어지는 칸 = 갈 수 있는 바닥(<see cref="Reach"/>),
    /// 벽 자리는 앞 0.55 ~ 1.05m에 갈 수 있는 바닥이 있고 그 눈높이에서 가리는 것 없이 닿을 때, 바닥 자리는 둘레에 그런 바닥이 있을 때만.
    /// </summary>
    public static class InteriorSpots
    {
        private const float ReachStep = 0.25f;     // 바닥 격자 간격
        private const float ReachSpan = 4f;        // 칸 가운데에서 훑는 범위 (칸 크기 8의 반 → 이웃 칸과 이어짐)
        private const float PlayerRadius = 0.3f;   // FirstPersonController
        private const float PlayerHeight = 1.75f;
        private const float PlayerEye = 1.6f;
        private const float UseDistance = 2.4f;    // InteriorMode 상호작용 거리 2.6보다 조금 안 (가장자리에서 깜빡이지 않게)
        private static readonly float[] StandDistances = { 0.55f, 0.8f, 1.05f };

        /// <summary>한 방의 갈 수 있는 바닥 (방을 지을 때마다 한 번 — 방 부모가 바뀌면 다시).</summary>
        private sealed class Reach
        {
            public int ParentId;
            public Vector3 Origin;
            public readonly Dictionary<Vector2Int, float> Floor = new Dictionary<Vector2Int, float>();   // 몸이 서는 칸 → 바닥 높이
            public readonly HashSet<Vector2Int> Reached = new HashSet<Vector2Int>();
        }

        private static readonly Dictionary<ModuleInstance, Reach> _reach = new Dictionary<ModuleInstance, Reach>();

        /// <summary>확인용: 마지막 <see cref="Walls"/>에서 플레이어가 닿을 수 없어 뺀 벽 자리 수.</summary>
        public static int LastUnreachable { get; private set; }

        /// <summary>12-5 바닥 소품을 두지 않을 곳 (주민이 정해진 길로 오가는 통로 — 행정 관제소 민원인). 방 부모마다 하나.</summary>
        public interface IFloorKeepClear
        {
            /// <summary><paramref name="world"/>에 반지름 <paramref name="radius"/>인 물건을 두면 통로를 막는지.</summary>
            bool Blocks(Vector3 world, float radius);
        }

        private static readonly Dictionary<Transform, IFloorKeepClear> _keepClear = new Dictionary<Transform, IFloorKeepClear>();

        /// <summary>방 부모 아래 통로를 알림 (켜질 때) · 지움 (꺼질 때 zone = null).</summary>
        public static void KeepClear(Transform roomParent, IFloorKeepClear zone)
        {
            if (roomParent == null)
                return;
            if (zone != null)
                _keepClear[roomParent] = zone;
            else
                _keepClear.Remove(roomParent);
        }
        private static readonly Collider[] _bodyHits = new Collider[16];
        private static readonly RaycastHit[] _sightHits = new RaycastHit[16];

        /// <summary>
        /// 벽 자리 후보 = 방 칸마다 가운데 <paramref name="height"/>(바닥에서) 높이에서 12방향 수평 광선이 맞은 벽 — 수직이고 평평하며
        /// (가로 ±0.32 · 세로 ±0.28 네 점이 같은 면), 앞 0.4m가 비어 있고, 문 · 해치 · 유리 · 주민 · 다른 상호작용 물건이 아니고,
        /// 플레이어가 앞에 서서 손이 닿는 곳(<see cref="CanReachWall"/>). 씨앗 순서로 섞음.
        /// </summary>
        public static void Walls(InteriorBuilder builder, ModuleInstance module, System.Random rng, float height, List<(Vector3 Position, Vector3 Normal)> result)
        {
            result.Clear();
            builder.EnsureActive(module); // 12-3c-opt: 아직 안 지었거나 꺼 둔 방은 광선에 안 맞음
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
            var walk = ReachOf(builder, module);
            LastUnreachable = 0;
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
                    if (hit.distance < 0.6f || Mathf.Abs(hit.normal.y) > 0.2f || Excluded(hit.collider) || OnScreen(hit))
                        continue;
                    var n = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                    if (!Flat(hit.point, n) || !Clear(hit.point, n) || InKitPanel(hit.point + n * 0.05f))
                        continue;
                    if (walk != null && !CanReachWall(walk, hit.point, n))
                    {
                        LastUnreachable++;
                        continue;   // 창구 뒤 · 가구 너머처럼 플레이어가 앞에 설 수 없는 벽
                    }
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
        /// <paramref name="ignoreResidents"/>: 지금 서 있거나 걷는 주민 몸은 빈 자리 판단에서 뺌 — 주민 위치에 따라 자리가 바뀌지 않게 (대신 부르는 쪽이 주민 자리를 <paramref name="used"/>로 넘김).
        /// </summary>
        public static bool Floor(InteriorBuilder builder, ModuleInstance module, System.Random rng, Vector3 halfExtents, IReadOnlyList<Vector3> used, float spacing,
            out Vector3 spot, float minRadius = 1f, float maxRadius = 2.6f, bool ignoreResidents = false)
        {
            spot = default;
            builder.EnsureActive(module);
            var walk = ReachOf(builder, module);
            var roomParent = builder.RoomParent(module);
            IFloorKeepClear lanes = null;
            if (roomParent != null)
                _keepClear.TryGetValue(roomParent, out lanes);
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
                if (Blocked(p + Vector3.up * (halfExtents.y + 0.04f), halfExtents, ignoreResidents))
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
                if (walk != null && !CanReachFloor(walk, p, halfExtents))
                    continue;   // 둘레에 플레이어가 설 바닥이 없음 (가구 사이 · 창구 뒤 등)
                if (lanes != null && lanes.Blocks(p, Mathf.Max(halfExtents.x, halfExtents.z)))
                    continue;   // 주민이 오가는 통로 (민원인이 상자를 뚫고 지나가지 않게)
                spot = p + Vector3.up * 0.002f;
                return true;
            }
            return false;
        }

        // ---------------- 12-5 플레이어가 갈 수 있는 바닥 ----------------

        /// <summary>방의 갈 수 있는 바닥 (없으면 만듦). 방을 아직 안 지었으면 null (거르지 않음).</summary>
        private static Reach ReachOf(InteriorBuilder builder, ModuleInstance module)
        {
            var parent = builder.RoomParent(module);
            if (parent == null || module.Cells.Count == 0)
                return null;
            int id = parent.GetInstanceID();
            if (_reach.TryGetValue(module, out var r) && r.ParentId == id)
                return r;
            if (_reach.Count > 256)
                _reach.Clear();   // 지워진 모듈 · 예전 내부 것
            r = new Reach { ParentId = id, Origin = builder.FloorPoint(module.Cells[0]) };
            _reach[module] = r;
            Physics.SyncTransforms();
            // 몸이 서는 칸
            foreach (var cell in module.Cells)
            {
                var floor = builder.FloorPoint(cell);
                for (float x = -ReachSpan; x <= ReachSpan + 0.001f; x += ReachStep)
                {
                    for (float z = -ReachSpan; z <= ReachSpan + 0.001f; z += ReachStep)
                    {
                        var key = KeyOf(r, floor + new Vector3(x, 0f, z));
                        if (r.Floor.ContainsKey(key))
                            continue;
                        var p = PointOf(r, key, floor.y);
                        if (Stand(p, floor.y, out float y))
                            r.Floor[key] = y;
                    }
                }
            }
            // 이어진 칸: 들어오는 곳(템플릿 시작 자리) · 칸 가운데에서 퍼져 나감 (가운데가 가구 속이면 근처 칸부터)
            var queue = new Queue<Vector2Int>();
            void Seed(Vector3 world)
            {
                var k = KeyOf(r, world);
                for (int ring = 0; ring <= 3; ring++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        for (int dz = -ring; dz <= ring; dz++)
                        {
                            var c = new Vector2Int(k.x + dx, k.y + dz);
                            if (r.Floor.ContainsKey(c) && r.Reached.Add(c))
                            {
                                queue.Enqueue(c);
                                return;
                            }
                        }
                    }
                }
            }
            if (builder.TryGetTemplateRoom(module, out var template, out var instance))
                Seed(instance.TransformPoint(template.Spawn));
            foreach (var cell in module.Cells)
                Seed(builder.FloorPoint(cell));
            var steps = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                float y = r.Floor[c];
                foreach (var s in steps)
                {
                    var nb = c + s;
                    if (r.Floor.TryGetValue(nb, out float ny) && Mathf.Abs(ny - y) < 0.25f && r.Reached.Add(nb))
                        queue.Enqueue(nb);
                }
            }
            return r;
        }

        private static Vector2Int KeyOf(Reach r, Vector3 world)
            => new Vector2Int(Mathf.RoundToInt((world.x - r.Origin.x) / ReachStep), Mathf.RoundToInt((world.z - r.Origin.z) / ReachStep));

        private static Vector3 PointOf(Reach r, Vector2Int key, float y)
            => new Vector3(r.Origin.x + key.x * ReachStep, y, r.Origin.z + key.y * ReachStep);

        /// <summary>바닥(방 바닥 ±0.3)이 있고 플레이어 몸이 들어가는지 — 주민 · 상호작용 물건(보급 상자 등)은 움직이므로 빼고 봄.</summary>
        private static bool Stand(Vector3 p, float floorY, out float y)
        {
            y = floorY;
            if (!Physics.Raycast(p + Vector3.up * 1.0f, Vector3.down, out var hit, 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (hit.normal.y < 0.9f || Mathf.Abs(hit.point.y - floorY) > 0.3f)
                return false;
            y = hit.point.y;
            var a = hit.point + Vector3.up * (PlayerRadius + 0.08f);
            var b = hit.point + Vector3.up * (PlayerHeight - PlayerRadius);
            int n = Physics.OverlapCapsuleNonAlloc(a, b, PlayerRadius, _bodyHits, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (!Moving(_bodyHits[i]))
                    return false;
            }
            return true;
        }

        /// <summary>움직이는 것 (주민 · 상호작용 물건 · 플레이어 몸) — 갈 수 있는 바닥 · 시선 판단에서 뺌.</summary>
        private static bool Moving(Collider c)
            => c is CharacterController || c.GetComponentInParent<ResidentFigure>() != null || c.GetComponentInParent<InteriorInteractable>() != null;

        /// <summary>갈 수 있는 바닥 중 <paramref name="world"/>에서 0.3m 안의 칸 (있으면 그 바닥 높이).</summary>
        private static bool NearReached(Reach r, Vector3 world, out float y)
        {
            var k = KeyOf(r, world);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var c = new Vector2Int(k.x + dx, k.y + dz);
                    if (!r.Reached.Contains(c))
                        continue;
                    var p = PointOf(r, c, 0f);
                    if (new Vector2(p.x - world.x, p.z - world.z).sqrMagnitude <= 0.3f * 0.3f)
                    {
                        y = r.Floor[c];
                        return true;
                    }
                }
            }
            y = 0f;
            return false;
        }

        /// <summary>벽 앞 0.55 ~ 1.05m에 갈 수 있는 바닥이 있고, 그 눈높이에서 벽 자리까지 가리는 것 없이 손이 닿는지.</summary>
        private static bool CanReachWall(Reach r, Vector3 point, Vector3 n)
        {
            foreach (float d in StandDistances)
            {
                if (!NearReached(r, point + n * d, out float y))
                    continue;
                var stand = point + n * d;
                if (Sight(new Vector3(stand.x, y + PlayerEye, stand.z), point + n * 0.05f))
                    return true;
            }
            return false;
        }

        /// <summary>바닥 물건 둘레(물건 가장자리에서 0.3 ~ 1.0m)에 갈 수 있는 바닥이 있고, 그 눈높이에서 물건이 보이는지.</summary>
        private static bool CanReachFloor(Reach r, Vector3 p, Vector3 halfExtents)
        {
            float inner = Mathf.Max(halfExtents.x, halfExtents.z) + PlayerRadius;
            float outer = inner + 0.7f;
            var target = p + Vector3.up * Mathf.Max(0.1f, halfExtents.y);
            int span = Mathf.CeilToInt(outer / ReachStep);
            var k = KeyOf(r, p);
            for (int dx = -span; dx <= span; dx++)
            {
                for (int dz = -span; dz <= span; dz++)
                {
                    var c = new Vector2Int(k.x + dx, k.y + dz);
                    if (!r.Reached.Contains(c))
                        continue;
                    var q = PointOf(r, c, r.Floor[c]);
                    float d = new Vector2(q.x - p.x, q.z - p.z).magnitude;
                    if (d < inner || d > outer)
                        continue;
                    if (Sight(q + Vector3.up * PlayerEye, target))
                        return true;
                }
            }
            return false;
        }

        /// <summary>눈에서 목표까지 상호작용 거리 안이고 가리는 것이 없는지 (주민 · 상호작용 물건 · 목표 바로 앞 5cm는 봐 줌).</summary>
        private static bool Sight(Vector3 eye, Vector3 target)
        {
            var to = target - eye;
            float dist = to.magnitude;
            if (dist > UseDistance)
                return false;
            if (dist < 0.05f)
                return true;
            int n = Physics.RaycastNonAlloc(eye, to / dist, _sightHits, dist - 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (!Moving(_sightHits[i].collider))
                    return false;
            }
            return true;
        }

        private static readonly Collider[] _overlaps = new Collider[16];

        private static bool Blocked(Vector3 center, Vector3 halfExtents, bool ignoreResidents)
        {
            if (!ignoreResidents)
                return Physics.CheckBox(center, halfExtents, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            int n = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlaps, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_overlaps[i].GetComponentInParent<ResidentFigure>() == null)
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

        /// <summary>
        /// 12-5 화면 위 (재질 이름에 Screen — 지도 벽 · 번호판 · 큰 화면). 평평하고 넓어 벽 자리로 잡혔음 (행정 관제소 지도 벽에 환풍구 자리, 2026-10-20).
        /// </summary>
        private static bool OnScreen(RaycastHit hit)
        {
            if (!(hit.collider is MeshCollider mc) || mc.sharedMesh == null || hit.triangleIndex < 0)
                return false;
            var renderer = mc.GetComponent<Renderer>();
            if (renderer == null)
                return false;
            var mesh = mc.sharedMesh;
            int index = hit.triangleIndex * 3;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var sub = mesh.GetSubMesh(s);
                if (index < sub.indexStart || index >= sub.indexStart + sub.indexCount)
                    continue;
                var mats = renderer.sharedMaterials;
                var m = s < mats.Length ? mats[s] : null;
                return m != null && m.name.IndexOf("Screen", StringComparison.Ordinal) >= 0;
            }
            return false;
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
