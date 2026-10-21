using System;
using System.Collections.Generic;
using SpaceStation.Core;
using Unity.Collections;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-17 실행 중에 소품 붙일 자리를 광선으로 고르는 공용 함수 (현장 수리 손상 지점 · 돌발 상황 · 보급 상자 · 주민 요청 물건 · 환풍구).
    /// 씨앗 난수로 섞으므로 같은 씨앗이면 다시 만들어도 같은 자리.
    /// 12-5 플레이어가 걸어가 손이 닿는 자리만 (사용자 요청 2026-10-20: 행정 관제소 창구 뒤 칸막이에 배관 누출이 생겼음 → 가기 어려운 곳 전부):
    /// 방마다 바닥을 격자로 훑어 플레이어 몸(반지름 0.3)이 서는 칸 중 들어오는 곳 · 칸 가운데에서 이어지는 칸 = 갈 수 있는 바닥(<see cref="Reach"/>),
    /// 벽 자리는 앞 0.55 ~ 1.05m에 갈 수 있는 바닥이 있고 그 눈높이에서 가리는 것 없이 닿을 때, 바닥 자리는 둘레에 그런 바닥이 있을 때만.
    /// 격자는 층(칸 y)마다 따로 — 아콜로지 · 거주 타워 · 코어처럼 여러 층인 모듈.
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
        private const int EdgeSteps = 5;           // 가장자리 칸 = 이 칸 수(1.25m) 안에 몸이 못 서는 칸이 있는 방향
        private const float EdgeRay = 1.15f;       // 가장자리 칸에서 벽까지 (StandDistances 끝 + 여유)
        private const float SpotBucket = 0.35f;    // 같은 자리로 보는 간격 (가장자리 광선이 같은 벽을 여러 번 맞힘)
        private static readonly Vector3Int[] Steps8 =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 1), new Vector3Int(0, 0, 1), new Vector3Int(-1, 0, 1),
            new Vector3Int(-1, 0, 0), new Vector3Int(-1, 0, -1), new Vector3Int(0, 0, -1), new Vector3Int(1, 0, -1),
        };

        /// <summary>한 방의 갈 수 있는 바닥 (방을 지을 때마다 한 번 — 방 부모가 바뀌면 다시). 칸 = (x, 층, z).</summary>
        private sealed class Reach
        {
            public int ParentId;
            public Vector3 Origin;
            public readonly Dictionary<int, float> Levels = new Dictionary<int, float>();                // 층 → 방 바닥 높이
            public readonly Dictionary<Vector3Int, float> Floor = new Dictionary<Vector3Int, float>();   // 몸이 서는 칸 → 바닥 높이
            public readonly HashSet<Vector3Int> Reached = new HashSet<Vector3Int>();
            public readonly List<(Vector3Int Key, int Dirs)> Edge = new List<(Vector3Int, int)>();      // 벽 · 가구 가까운 칸(0.5m 간격) · 막힌 방향(8방향 비트)
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
        /// 벽 자리 후보 = <paramref name="height"/>(바닥에서) 높이의 수평 광선이 맞은 벽 — 수직이고 평평하며
        /// (가로 ±0.32 · 세로 ±0.28 네 점이 같은 면, 위아래 끝 ±0.34까지 받침), 앞 0.4m가 비어 있고, 문 · 해치 · 유리 · 주민 · 다른 상호작용 물건이 아니고,
        /// 플레이어가 앞에 서서 손이 닿는 곳(<see cref="CanReachWall"/>). 씨앗 순서로 섞음.
        /// 광선: 방 칸마다 가운데에서 12방향 + 12-5 갈 수 있는 바닥 가장자리 칸에서 막힌 쪽으로 짧게
        /// (가운데 광선은 대칭이라 거의 문 옆에만 닿아 48종 중 40종이 0 ~ 2자리였음, 2026-10-20).
        /// </summary>
        public static void Walls(InteriorBuilder builder, ModuleInstance module, System.Random rng, float height, List<(Vector3 Position, Vector3 Normal)> result)
        {
            result.Clear();
            builder.EnsureActive(module); // 12-3c-opt: 아직 안 지었거나 꺼 둔 방은 광선에 안 맞음
            CollectPanels(builder.RoomParent(module));
            var walk = ReachOf(builder, module);
            LastUnreachable = 0;
            _taken.Clear();
            _tried.Clear();
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
                    TryWall(walk, cell.y, origin, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), reach, 0.6f, result);
                }
            }
            if (walk != null)
            {
                foreach (var (key, dirs) in walk.Edge)
                {
                    var origin = PointOf(walk, key, walk.Levels[key.y] + height);
                    for (int k = 0; k < 8; k++)
                    {
                        if ((dirs & (1 << k)) == 0)
                            continue;
                        var s = Steps8[k];
                        TryWall(walk, key.y, origin, new Vector3(s.x, 0f, s.z).normalized, EdgeRay, 0.2f, result);
                    }
                }
            }
            for (int i = result.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (result[i], result[j]) = (result[j], result[i]);
            }
        }

        private static readonly HashSet<Vector3Int> _taken = new HashSet<Vector3Int>();   // 받은 자리 (SpotBucket 칸)
        private static readonly HashSet<Vector3Int> _tried = new HashSet<Vector3Int>();   // 이미 잰 점 (0.1m 칸 — 떨어진 점 옆의 붙일 수 있는 점은 다시 봄)

        /// <summary>광선 하나가 맞은 곳을 벽 자리로 쓸 수 있으면 넣음 (같은 자리는 한 번).</summary>
        private static void TryWall(Reach walk, int level, Vector3 origin, Vector3 dir, float reach, float minDistance, List<(Vector3 Position, Vector3 Normal)> result)
        {
            if (!Physics.Raycast(origin, dir, out var hit, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;
            if (hit.distance < minDistance || Mathf.Abs(hit.normal.y) > 0.2f || Excluded(hit.collider))
                return;
            var n = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            int sector = (Mathf.RoundToInt(Mathf.Atan2(n.z, n.x) / (Mathf.PI * 0.25f)) & 7) + level * 8;
            var taken = new Vector3Int(Mathf.RoundToInt(hit.point.x / SpotBucket), sector, Mathf.RoundToInt(hit.point.z / SpotBucket));
            if (_taken.Contains(taken) || !_tried.Add(new Vector3Int(Mathf.RoundToInt(hit.point.x * 10f), sector, Mathf.RoundToInt(hit.point.z * 10f))))
                return;
            if (!Accept(walk, level, hit, n, out var point))
                return;
            _taken.Add(taken);
            result.Add((point, n));
        }

        private static bool Accept(Reach walk, int level, RaycastHit hit, Vector3 n, out Vector3 point)
        {
            point = hit.point;
            if (OnScreen(hit) || !Flat(hit.point, n) || !OnPanel(ref point, n) || !Clear(point, n))
                return false;
            if (walk != null && !CanReachWall(walk, level, point, n))
            {
                LastUnreachable++;
                return false;   // 창구 뒤 · 가구 너머처럼 플레이어가 앞에 설 수 없는 벽
            }
            return true;
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
                if (walk != null && !CanReachFloor(walk, cell.y, p, halfExtents))
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
            CollectPanels(parent);
            // 몸이 서는 칸 (층마다). 문 패널 안은 뺌 — 문이 열려 있으면 이어진 통로까지 퍼져 통로 벽이 자리로 잡혔음 (행정 관제소)
            foreach (var cell in module.Cells)
            {
                var floor = builder.FloorPoint(cell);
                r.Levels[cell.y] = floor.y;
                for (float x = -ReachSpan; x <= ReachSpan + 0.001f; x += ReachStep)
                {
                    for (float z = -ReachSpan; z <= ReachSpan + 0.001f; z += ReachStep)
                    {
                        var key = KeyOf(r, floor + new Vector3(x, 0f, z), cell.y);
                        if (r.Floor.ContainsKey(key))
                            continue;
                        var p = PointOf(r, key, floor.y);
                        if (!InDoorPanel(p + Vector3.up, 0f) && Stand(p, floor.y, out float y))
                            r.Floor[key] = y;
                    }
                }
            }
            // 이어진 칸: 들어오는 곳(템플릿 시작 자리) · 칸 가운데에서 퍼져 나감 (가운데가 가구 속이면 근처 칸부터)
            var queue = new Queue<Vector3Int>();
            void Seed(Vector3 world, int level)
            {
                var k = KeyOf(r, world, level);
                for (int ring = 0; ring <= 3; ring++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        for (int dz = -ring; dz <= ring; dz++)
                        {
                            var c = new Vector3Int(k.x + dx, level, k.z + dz);
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
            {
                var spawn = instance.TransformPoint(template.Spawn);
                int level = module.Cells[0].y;
                foreach (var cell in module.Cells)
                {
                    if (Mathf.Abs(r.Levels[cell.y] - spawn.y) < Mathf.Abs(r.Levels[level] - spawn.y))
                        level = cell.y;
                }
                Seed(spawn, level);
            }
            foreach (var cell in module.Cells)
                Seed(builder.FloorPoint(cell), cell.y);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                float y = r.Floor[c];
                for (int k = 0; k < 8; k += 2)
                {
                    var nb = c + Steps8[k];
                    if (r.Floor.TryGetValue(nb, out float ny) && Mathf.Abs(ny - y) < 0.25f && r.Reached.Add(nb))
                        queue.Enqueue(nb);
                }
            }
            // 가장자리 칸: 0.5m 간격으로, 몸이 못 서는 칸(벽 · 가구)이 가까운 방향만 — 벽 자리 광선을 쏠 곳
            foreach (var c in r.Reached)
            {
                if ((c.x & 1) != 0 || (c.z & 1) != 0)
                    continue;
                int dirs = 0;
                for (int k = 0; k < 8; k++)
                {
                    for (int d = 1; d <= EdgeSteps; d++)
                    {
                        if (!r.Floor.ContainsKey(c + Steps8[k] * d))
                        {
                            dirs |= 1 << k;
                            break;
                        }
                    }
                }
                if (dirs != 0)
                    r.Edge.Add((c, dirs));
            }
            return r;
        }

        private static Vector3Int KeyOf(Reach r, Vector3 world, int level)
            => new Vector3Int(Mathf.RoundToInt((world.x - r.Origin.x) / ReachStep), level, Mathf.RoundToInt((world.z - r.Origin.z) / ReachStep));

        private static Vector3 PointOf(Reach r, Vector3Int key, float y)
            => new Vector3(r.Origin.x + key.x * ReachStep, y, r.Origin.z + key.z * ReachStep);

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
        private static bool NearReached(Reach r, int level, Vector3 world, out float y)
        {
            var k = KeyOf(r, world, level);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var c = new Vector3Int(k.x + dx, level, k.z + dz);
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
        private static bool CanReachWall(Reach r, int level, Vector3 point, Vector3 n)
        {
            foreach (float d in StandDistances)
            {
                if (!NearReached(r, level, point + n * d, out float y))
                    continue;
                var stand = point + n * d;
                if (Sight(new Vector3(stand.x, y + PlayerEye, stand.z), point + n * 0.05f))
                    return true;
            }
            return false;
        }

        /// <summary>바닥 물건 둘레(물건 가장자리에서 0.3 ~ 1.0m)에 갈 수 있는 바닥이 있고, 그 눈높이에서 물건이 보이는지.</summary>
        private static bool CanReachFloor(Reach r, int level, Vector3 p, Vector3 halfExtents)
        {
            float inner = Mathf.Max(halfExtents.x, halfExtents.z) + PlayerRadius;
            float outer = inner + 0.7f;
            var target = p + Vector3.up * Mathf.Max(0.1f, halfExtents.y);
            int span = Mathf.CeilToInt(outer / ReachStep);
            var k = KeyOf(r, p, level);
            for (int dx = -span; dx <= span; dx++)
            {
                for (int dz = -span; dz <= span; dz++)
                {
                    var c = new Vector3Int(k.x + dx, level, k.z + dz);
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

        // ---------------- 문 자리 키트 패널 ----------------
        // 키트 패널(막힌 벽 KIT_Wall · 문 KIT_WallDoor)은 충돌체 없이 벽면(Solid)보다 방 안쪽으로 튀어나와 있어
        // 광선이 맞은 벽면에 그대로 붙이면 패널 뒤에 가려짐 (11-17 ③ 환풍구 — 그땐 패널 범위를 다 뺐음).
        // 12-5 막힌 벽 패널은 그 앞면으로 옮겨 붙임 (일반 방 벽 대부분이 이 패널이라 자리가 0이었음) — 패널은 뒤판(벽면 5cm 앞) 위에
        // 판자(+5cm) · 가장자리 세로 테(+10cm) · 위 테가 붙은 모양이라 소품 자리마다 모델 칸 지도로 잼.
        // 문 패널은 문틀 · 테가 더 튀어나와 소품 폭(0.72)만큼 평평한 곳이 없어 계속 뺌.

        private const float ShapeCell = 0.025f;        // 패널 모양 칸 (모델 단위)
        private const float PropHalfWidth = 0.36f;     // 벽 소품 반 폭 · 반 높이 (Clear 상자와 같음)
        private const float PropHalfHeight = 0.34f;

        /// <summary>
        /// 막힌 벽 패널 모델의 앞면 모양 (메시마다 한 번) — 모델 x · y 칸마다 그쪽으로 가장 튀어나온 z, +z · -z 양쪽 다
        /// (키트는 보이는 면이 -z지만 어느 쪽이 방 안인지는 쓸 때 맞은 면 방향으로 고름).
        /// 면 방향은 삼각형 감김으로 봄 (꼭짓점 노멀은 모서리에서 섞여 있어 정면 삼각형이 하나도 안 잡혔음).
        /// </summary>
        private sealed class PanelShape
        {
            public float MinX, MinY;
            public int Cols, Rows;
            public float[] Plus, Minus;   // NaN = 면 없음
        }

        private struct Panel
        {
            public Bounds Box;
            public Vector3 Origin, Forward, Right, Scale;   // Forward = 모델 +z (키트는 방 바깥 = 벽 쪽)
            public PanelShape Shape;
        }

        private static readonly List<Panel> _panels = new List<Panel>();
        private static readonly List<Bounds> _doorPanels = new List<Bounds>();   // 바닥 격자가 점마다 보므로 따로
        private static readonly Dictionary<Mesh, PanelShape> _shapes = new Dictionary<Mesh, PanelShape>();
        private static readonly List<float> _footprint = new List<float>();

        private static void CollectPanels(Transform roomParent)
        {
            _panels.Clear();
            _doorPanels.Clear();
            if (roomParent == null)
                return;
            foreach (var r in roomParent.GetComponentsInChildren<Renderer>())
            {
                if (!r.name.StartsWith("KIT_Wall", StringComparison.Ordinal))
                    continue;
                var mesh = r.TryGetComponent<MeshFilter>(out var mf) ? mf.sharedMesh : null;
                // 읽을 수 없는 메시(가져오기 Read/Write 꺼짐)는 게임 중 꼭짓점을 못 읽음 — 예외 대신 문 패널처럼 뺌 (SM_InteriorKit.fbx는 Read/Write 켬)
                if (mesh == null || !mesh.isReadable || r.name.StartsWith("KIT_WallDoor", StringComparison.Ordinal))
                {
                    _doorPanels.Add(r.bounds);   // 모양을 모르는 패널도 문 패널처럼 뺌
                    continue;
                }
                var t = r.transform;
                _panels.Add(new Panel { Box = r.bounds, Origin = t.position, Forward = t.forward, Right = t.right, Scale = t.lossyScale, Shape = ShapeOf(mesh) });
            }
        }

        private static PanelShape ShapeOf(Mesh mesh)
        {
            if (_shapes.TryGetValue(mesh, out var shape))
                return shape;
            var bounds = mesh.bounds;
            shape = new PanelShape
            {
                MinX = bounds.min.x,
                MinY = bounds.min.y,
                Cols = Mathf.CeilToInt(bounds.size.x / ShapeCell) + 1,
                Rows = Mathf.CeilToInt(bounds.size.y / ShapeCell) + 1,
            };
            shape.Plus = new float[shape.Cols * shape.Rows];
            shape.Minus = new float[shape.Cols * shape.Rows];
            for (int i = 0; i < shape.Plus.Length; i++)
            {
                shape.Plus[i] = float.NaN;
                shape.Minus[i] = float.NaN;
            }
            using (var all = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var data = all[0];
                using (var verts = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                {
                    data.GetVertices(verts);
                    for (int s = 0; s < data.subMeshCount; s++)
                    {
                        using (var idx = new NativeArray<int>(data.GetSubMesh(s).indexCount, Allocator.Temp))
                        {
                            data.GetIndices(idx, s);
                            for (int i = 0; i + 2 < idx.Length; i += 3)
                            {
                                Vector3 a = verts[idx[i]], b = verts[idx[i + 1]], c = verts[idx[i + 2]];
                                var g = Vector3.Cross(b - a, c - a);   // Unity 감김(시계 방향) → 바깥을 보는 면 방향
                                if (Mathf.Abs(g.z) < 0.5f * g.magnitude)
                                    continue;   // 옆면 · 넓이 0
                                bool plus = g.z > 0f;
                                var proud = plus ? shape.Plus : shape.Minus;
                                float z = plus ? Mathf.Max(a.z, Mathf.Max(b.z, c.z)) : Mathf.Min(a.z, Mathf.Min(b.z, c.z));
                                int x0 = ShapeIndex(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - shape.MinX, shape.Cols);
                                int x1 = ShapeIndex(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - shape.MinX, shape.Cols);
                                int y0 = ShapeIndex(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - shape.MinY, shape.Rows);
                                int y1 = ShapeIndex(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - shape.MinY, shape.Rows);
                                for (int y = y0; y <= y1; y++)
                                {
                                    for (int x = x0; x <= x1; x++)
                                    {
                                        int k = y * shape.Cols + x;
                                        if (float.IsNaN(proud[k]) || (plus ? z > proud[k] : z < proud[k]))
                                            proud[k] = z;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            _shapes[mesh] = shape;
            return shape;
        }

        private static int ShapeIndex(float offset, int count) => Mathf.Clamp(Mathf.FloorToInt(offset / ShapeCell), 0, count - 1);

        /// <summary>
        /// 패널 위 자리 맞추기: 문 패널에 걸치면 false. 막힌 벽 패널이면 소품 자리(가로 ±0.36 · 세로 ±0.34)에서 가장 튀어나온 면으로
        /// <paramref name="point"/>를 옮김 — 그 면이 자리의 반 이상이어야 (가장자리 테 · 위 테에만 닿아 나머지가 뜨지 않게),
        /// 패널과 맨 벽 · 구석 옆벽 패널에 걸쳐도 안 됨.
        /// </summary>
        private static bool OnPanel(ref Vector3 point, Vector3 n)
        {
            if (InDoorPanel(point + n * 0.05f, 0.2f))
                return false;
            var t = Vector3.Cross(Vector3.up, n).normalized;
            foreach (var o in new[] { t * PropHalfWidth, -t * PropHalfWidth, Vector3.up * PropHalfHeight, Vector3.down * PropHalfHeight })
            {
                if (InDoorPanel(point + o + n * 0.05f, 0.04f))
                    return false;
            }
            var extents = new Vector3(Mathf.Abs(t.x), 0f, Mathf.Abs(t.z)) * PropHalfWidth + Vector3.up * PropHalfHeight
                + new Vector3(Mathf.Abs(n.x), 0f, Mathf.Abs(n.z)) * 0.2f;
            var foot = new Bounds(point, extents * 2f);
            float plane = Vector3.Dot(point, n);
            float covered = 0f;
            _footprint.Clear();
            foreach (var panel in _panels)
            {
                if (!panel.Box.Intersects(foot))
                    continue;
                float facing = Vector3.Dot(panel.Forward, n);
                if (Mathf.Abs(facing) < 0.98f)
                    return false;   // 구석 옆벽 패널
                var shape = panel.Shape;
                var proud = facing > 0f ? shape.Plus : shape.Minus;
                float sign = facing > 0f ? 1f : -1f;
                float at = Vector3.Dot(panel.Origin, panel.Forward);
                // 소품 자리 → 모델 칸 (키트는 LookRotation(벽 방향, 위)라 모델 y = 위)
                float cx = Vector3.Dot(point - panel.Origin, panel.Right) / panel.Scale.x - shape.MinX;
                float cy = (point.y - panel.Origin.y) / panel.Scale.y - shape.MinY;
                float hx = PropHalfWidth / panel.Scale.x, hy = PropHalfHeight / panel.Scale.y;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((cx - hx) / ShapeCell)), x1 = Mathf.Min(shape.Cols - 1, Mathf.FloorToInt((cx + hx) / ShapeCell));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((cy - hy) / ShapeCell)), y1 = Mathf.Min(shape.Rows - 1, Mathf.FloorToInt((cy + hy) / ShapeCell));
                if (x0 > x1 || y0 > y1)
                    continue;
                covered += (x1 - x0 + 1) * ShapeCell * panel.Scale.x;   // 패널은 바닥부터 천장까지라 가로만 셈
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float z = proud[y * shape.Cols + x];
                        _footprint.Add(float.IsNaN(z) ? 0f : sign * (at + z * panel.Scale.z) - plane);   // 뚫린 칸 = 뒤 벽면
                    }
                }
            }
            if (_footprint.Count == 0)
                return true;   // 패널 없는 벽
            if (covered < PropHalfWidth * 2f - 0.03f)
                return false;   // 패널과 맨 벽에 걸침
            float front = float.MinValue;
            foreach (float d in _footprint)
                front = Mathf.Max(front, d);
            if (front > 0.15f)
                return false;
            int near = 0;
            foreach (float d in _footprint)
            {
                if (d >= front - 0.01f)
                    near++;
            }
            if (near * 2 < _footprint.Count)
                return false;
            point += n * Mathf.Max(0f, front);   // 앞면이 맞은 면보다 안쪽이면(템플릿 벽이 패널을 덮음) 그대로
            return true;
        }

        private static bool InDoorPanel(Vector3 p, float margin)
        {
            foreach (var box in _doorPanels)
            {
                var b = box;
                b.Expand(margin);
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

        private static readonly Vector3[] _flatProbes = new Vector3[6];

        /// <summary>
        /// 가로 ±0.32 · 세로 ±0.28이 같은 면(3cm 안) + 12-5 위아래 끝 ±0.34에도 받치는 면(6cm 안 — 패널 줄눈 정도는 봐 줌), 어느 점도 화면이 아님.
        /// 가장자리 광선이 가구 앞면도 맞히는데, 낮은 가구 윗모서리에 소품이 반쯤 걸치지 않게.
        /// </summary>
        private static bool Flat(Vector3 point, Vector3 n)
        {
            var t = Vector3.Cross(Vector3.up, n).normalized;
            _flatProbes[0] = t * 0.32f;
            _flatProbes[1] = -t * 0.32f;
            _flatProbes[2] = Vector3.up * 0.28f;
            _flatProbes[3] = Vector3.down * 0.28f;
            _flatProbes[4] = Vector3.up * 0.34f;
            _flatProbes[5] = Vector3.down * 0.34f;
            for (int i = 0; i < _flatProbes.Length; i++)
            {
                var from = point + _flatProbes[i] + n * 0.25f;
                if (!Physics.Raycast(from, -n, out var h, 0.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return false; // 뚫린 곳 (문 자리 · 창)
                float tolerance = i < 4 ? 0.03f : 0.06f;
                if (Mathf.Abs(h.distance - 0.25f) > tolerance || Vector3.Dot(h.normal, n) < 0.95f || OnScreen(h))
                    return false;   // 화면: 소품 끝이 지도 벽 화면 아래쪽에 걸쳤음
            }
            return true;
        }

        /// <summary>앞 0.04 ~ 0.44m가 비었는지 — 지나가는 주민 · 플레이어 몸은 봐 줌 (같은 씨앗이면 같은 자리 · 현장 수리 지점 순서가 바뀌지 않게).</summary>
        private static bool Clear(Vector3 point, Vector3 n)
        {
            int count = Physics.OverlapBoxNonAlloc(point + n * 0.24f, new Vector3(PropHalfWidth, PropHalfHeight, 0.2f), _overlaps, Quaternion.LookRotation(n), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = _overlaps[i];
                if (!(c is CharacterController) && c.GetComponentInParent<ResidentFigure>() == null)
                    return false;
            }
            return true;
        }
    }
}
