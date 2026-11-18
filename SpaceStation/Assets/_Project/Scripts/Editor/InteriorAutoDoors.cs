using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 모든 면 연결 (2026-11-18, 사용자 "연결점이 모두 이어져서 어떤 방향으로든 이동이 가능하게"): 템플릿을 지을 때
    /// 문 자리가 없는 (또는 닫힌) 둘레 옆면 중 문 안쪽 바닥이 문턱 높이 · 비어 있고 걸어서 닿는 면에 열린 문 자리를 더한다
    /// (문 깊이 = 그 방 문 깊이, 바닥이 칸 끝 조금 안에서 끝나면 바닥 끝 — 문에서 칸 끝까지 튜브 자리가 비어 있어야 함, <see cref="FindDepth"/>).
    /// 문 높이에 바닥이 없는 2층 높이 옆문은 아래 바닥에 빛 기둥 문 승강판 자리를 찾아 연다 (<see cref="TryDoorLift"/>, ②-4).
    /// 하늘 껍질 문 구멍보다 먼저 불러 새 문도 하늘이 비켜 줌 (InteriorTemplateBuilder.Build).
    /// 칸 끝까지 바닥이 없는 면은 모델에 길 · 문간을 낸 뒤 다시 지으면 저절로 열림 (빌더마다 문 목록을 손대지 않음).
    /// 바깥 통로는 Measure Connector Depths가 열린 문 자리를 따라 만듦 (<see cref="ConnectorDepthBaker"/>).
    /// </summary>
    public static class InteriorAutoDoors
    {
        private static readonly Vector3 TempOrigin = new Vector3(0f, 600f, 0f);
        /// <summary>문 안쪽 바닥을 보는 자리 (문 평면에서 안쪽 — 걷기 마디 · 문턱 바로 안) · 문 폭 방향 옆 자리.</summary>
        private const float Inside = 0.6f, Edge = 0.25f, Side = 0.45f, SillTolerance = 0.3f;

        /// <summary>
        /// 짓는 중인 방 (root = 0번 칸 중심이 원점, 충돌체 붙음)에 문 자리 열기 — sockets에 바로 더함. 연 면 수.
        /// </summary>
        public static int OpenOn(GameObject root, ModuleData module, List<InteriorSocket> sockets, Vector3 spawn, List<string> log = null, string name = null)
        {
            if (root == null || module == null)
                return 0;
            var probe = ScriptableObject.CreateInstance<InteriorTemplate>();
            var keep = root.transform.position;
            int opened = 0;
            try
            {
                probe.EditorSet(module, null, new List<InteriorSocket>(sockets), spawn, 0f);
                root.transform.position = TempOrigin;
                Physics.SyncTransforms();
                var graph = InteriorWalkGraph.ForTemplate(probe, root.transform);
                var decor = new InteriorSkyLiftSpots.DecorGrid(root.transform);
                var cells = new HashSet<Vector3Int>(module.CellOffsets);
                float depth = DoorDepth(sockets);
                var pads = new List<Vector3>();   // 승강판 (위 · 문) — 서로 떨어지게
                foreach (var s in sockets)
                {
                    if (s.SkyLift || s.DoorLift)
                        pads.Add(TempOrigin + (Vector3)s.Cell * InteriorGeometry.CellSize + s.Offset);
                }
                foreach (var cell in module.CellOffsets)
                {
                    foreach (var dir in GridDirections.Faces)
                    {
                        if (dir.y != 0 || cells.Contains(cell + dir))
                            continue;
                        if ((module.TerminalOnly && dir == module.DockFront) || module.IsClosedFace(cell, dir))
                            continue;   // 도킹 접근로 쪽 (이웃이 못 붙음 — 물류 · 조선소 하역문) · 닫힌 면
                        int i = sockets.FindIndex(s => s.Cell == cell && s.Direction == dir);
                        if (i >= 0 && !sockets[i].SealedWall)
                            continue;
                        InteriorSocket open;
                        float at = FindDepth(graph, decor, cell, dir, depth);
                        if (at >= 0f)
                        {
                            open = new InteriorSocket(cell, dir, at);
                            log?.Add($"{name ?? module.name} {cell} {dir} 깊이 {at:0.00}");
                        }
                        else if (TryDoorLift(graph, decor, sockets, pads, cell, dir, depth, out var pad))
                        {
                            var center = TempOrigin + (Vector3)cell * InteriorGeometry.CellSize;
                            open = new InteriorSocket(cell, dir, depth, pad - center, doorLift: true);
                            pads.Add(pad);
                            log?.Add($"{name ?? module.name} {cell} {dir} 문 승강판 ({pad.x - center.x:0.00}, {pad.y - center.y:0.00}, {pad.z - center.z:0.00})");
                        }
                        else
                            continue;
                        if (i >= 0)
                            sockets[i] = open;
                        else
                            sockets.Add(open);
                        opened++;
                    }
                }
            }
            finally
            {
                root.transform.position = keep;
                Physics.SyncTransforms();
                Object.DestroyImmediate(probe);
            }
            return opened;
        }

        /// <summary>
        /// 하늘 구멍을 낸 뒤: 새로 연 문 앞을 여전히 하늘 그림이 막으면 (InteriorTemplateBoundsTests.SkyShellIsOpenAtEveryDoor와 같은 광선) 예전 자리로 되돌림.
        /// 되돌린 면 수.
        /// </summary>
        public static int UndoSkyBlocked(GameObject root, List<InteriorSocket> sockets, List<InteriorSocket> before)
        {
            var skies = new List<(Vector3[] verts, int[] tris)>();
            foreach (Transform part in root.transform)
            {
                var r = part.GetComponent<MeshRenderer>();
                var f = part.GetComponent<MeshFilter>();
                if (!part.name.EndsWith("_Sky") || r == null || r.sharedMaterial == null || !r.sharedMaterial.name.Contains("Evo2Sky") || f == null || f.sharedMesh == null)
                    continue;
                var toRoot = root.transform.worldToLocalMatrix * part.localToWorldMatrix;
                var v = f.sharedMesh.vertices;
                for (int i = 0; i < v.Length; i++)
                    v[i] = toRoot.MultiplyPoint3x4(v[i]);
                skies.Add((v, f.sharedMesh.triangles));
            }
            int undone = 0;
            for (int i = sockets.Count - 1; i >= 0; i--)
            {
                var s = sockets[i];
                if (s.Direction.y != 0 || s.SealedWall || before.Exists(b => b.Cell == s.Cell && b.Direction == s.Direction && !b.SealedWall))
                    continue;
                var dir = (Vector3)s.Direction;
                var start = (Vector3)s.Cell * InteriorGeometry.CellSize + dir * (s.Depth - 0.5f);
                start.y = InteriorGeometry.FloorY(s.Cell) + 1.1f;
                bool blocked = false;
                foreach (var (verts, tris) in skies)
                {
                    for (int t = 0; t < tris.Length && !blocked; t += 3)
                        blocked = Hit(start, dir, 1.5f, verts[tris[t]], verts[tris[t + 1]], verts[tris[t + 2]]);
                }
                if (!blocked)
                    continue;
                int j = before.FindIndex(b => b.Cell == s.Cell && b.Direction == s.Direction);
                if (j >= 0)
                    sockets[i] = before[j];
                else
                    sockets.RemoveAt(i);
                undone++;
            }
            return undone;
        }

        private static bool Hit(Vector3 o, Vector3 d, float maxT, Vector3 a, Vector3 b, Vector3 c)
        {
            var e1 = b - a;
            var e2 = c - a;
            var p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-7f)
                return false;
            float inv = 1f / det;
            var s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f)
                return false;
            var q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f)
                return false;
            float t = Vector3.Dot(e2, q) * inv;
            return t > 0f && t < maxT;
        }

        /// <summary>그 방 옆 문 자리 깊이 (가장 흔한 값, 없으면 3.9).</summary>
        private static float DoorDepth(List<InteriorSocket> sockets)
        {
            var count = new Dictionary<float, int>();
            foreach (var s in sockets)
            {
                if (s.Direction.y != 0 || s.SealedWall)
                    continue;
                float d = Mathf.Round(s.Depth * 100f) / 100f;
                count[d] = count.TryGetValue(d, out int n) ? n + 1 : 1;
            }
            float best = 3.9f;
            int bestN = 0;
            foreach (var kv in count)
            {
                if (kv.Value > bestN)
                {
                    best = kv.Key;
                    bestN = kv.Value;
                }
            }
            return best;
        }

        /// <summary>
        /// 문 깊이 찾기: 그 방 문 깊이에서 안쪽으로 (0.05 m씩, <see cref="MinDepth"/>까지) 바닥이 문턱 높이로 이어지고 문에서 칸 끝까지 통로 자리가 빈 첫 깊이.
        /// 둥근 · 둥근 사각 평면이라 바닥이 칸 끝 조금 안에서 끝나는 트인 방 (그리드 허브 · 별빛 전력망 등)은 문을 바닥 끝에 세움 — 실내 통로 튜브는 양쪽 문 깊이를 따라 늘어남.
        /// 없으면 −1.
        /// </summary>
        private static float FindDepth(InteriorWalkGraph graph, InteriorSkyLiftSpots.DecorGrid decor, Vector3Int cell, Vector3Int dir, float depth)
        {
            for (float d = depth; d >= MinDepth - 0.001f; d -= 0.05f)
            {
                if (Clear(graph, cell, dir, d) && FloorAt(cell, dir, d - 0.02f) && Corridor(decor, cell, dir, d))
                    return Mathf.Round(d * 100f) / 100f;
            }
            return -1f;
        }

        /// <summary>문 평면 바로 안 (가운데 · 양옆)에 문턱 높이 바닥 — 갑판 끝보다 바깥에 문을 세우면 문턱 밑이 비었음 (그리드 허브 3.45, 갑판 끝 3.2).</summary>
        private static bool FloorAt(Vector3Int cell, Vector3Int dir, float depth)
        {
            var center = TempOrigin + (Vector3)cell * InteriorGeometry.CellSize;
            float sill = TempOrigin.y + InteriorGeometry.FloorY(cell);
            var side = Vector3.Cross(Vector3.up, dir);
            foreach (float s in new[] { 0f, -Side, Side })
            {
                var e = center + (Vector3)dir * depth + side * s;
                if (!Physics.Raycast(new Vector3(e.x, sill + 1f, e.z), Vector3.down, out var hit, 1f + SillTolerance + 0.01f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(hit.point.y - sill) > SillTolerance || hit.normal.y < 0.7f)
                    return false;
            }
            return true;
        }

        /// <summary>문 승강판: 문에서 판 가운데까지 안쪽 거리 후보 (가까운 것 먼저) · 옆 비낌 · 판에서 문턱까지 최소 높이 · 문턱 위로 비울 높이.</summary>
        private static readonly float[] LiftBack = { 1.1f, 1.4f, 1.8f, 2.2f, 2.6f };
        private static readonly float[] LiftSide = { 0f, -0.5f, 0.5f };
        private const float LiftMinRise = 1.2f, LiftHead = 2.3f;

        /// <summary>
        /// 2층 높이 옆문 (문 높이에 바닥이 없음 — 위 칸이 하늘 · 뚫린 홀) = 빛 기둥 문 승강판 자리 (모든 면 연결 ②-4, 2026-11-18):
        /// 문 · 튜브 자리가 비고 (<see cref="Corridor"/>), 문 안 떠오른 길 (문 평면 ~ 판, 문 폭, 문턱 −0.3 ~ +2.3)에 충돌체 · 장식이 없고,
        /// 문 안쪽 1.1 ~ 2.6 m 아래 걸어서 닿는 바닥 (문턱보다 1.2 m 넘게 낮음)에 판이 놓임 — 판 둘레 평평 · 판에서 문턱 + 2.3까지 기둥 비움 · 판 테가 벽에 안 겹침 ·
        /// 다른 판 · 아래 해치 틀 · 같은 바닥 문에서 떨어짐. pad = 판 윗면 가운데 (짓는 자리 좌표).
        /// </summary>
        private static bool TryDoorLift(InteriorWalkGraph graph, InteriorSkyLiftSpots.DecorGrid decor, List<InteriorSocket> sockets, List<Vector3> pads, Vector3Int cell, Vector3Int dir, float depth, out Vector3 pad)
        {
            pad = default;
            var center = TempOrigin + (Vector3)cell * InteriorGeometry.CellSize;
            float sill = TempOrigin.y + InteriorGeometry.FloorY(cell);
            var side = Vector3.Cross(Vector3.up, dir);
            if (FloorAt(cell, dir, depth - 0.02f) || !Corridor(decor, cell, dir, depth))
                return false;
            foreach (float back in LiftBack)
            {
                // 떠오른 길 (문 평면 ~ 판 가운데 + 판 반지름)
                if (Blocked(center, dir, side, depth - back - InteriorSkyLiftSpots.PadRadius, depth, 0f, 0.8f, sill - 0.3f, sill + LiftHead))
                    break;
                bool decorFree = true;
                for (float s = depth - back; s <= depth + 0.001f && decorFree; s += 0.3f)
                {
                    foreach (float x in new[] { -0.45f, 0f, 0.45f })
                        decorFree &= !decor.Any(center + (Vector3)dir * s + side * x, 0.35f, sill - 0.3f, sill + LiftHead);
                }
                if (!decorFree)
                    break;
                foreach (float lat in LiftSide)
                {
                    var p = center + (Vector3)dir * (depth - back) + side * lat;
                    int best = -1;
                    foreach (int k in graph.Column(p.x, p.z))
                    {
                        if (graph.Reached(k) && graph[k].y < sill - LiftMinRise && (best < 0 || graph[k].y > graph[best].y))
                            best = k;
                    }
                    if (best < 0)
                        continue;
                    var q = graph[best];
                    if (!InteriorSkyLiftSpots.PadSurface(q, out float top, out _))
                        continue;
                    var at = new Vector3(q.x, top, q.z);
                    if (!InteriorSkyLiftSpots.ColumnClear(at, sill + LiftHead, InteriorSkyLiftSpots.ColumnRadii[InteriorSkyLiftSpots.ColumnRadii.Length - 1])
                        || !decor.Clear(at, sill + LiftHead) || InteriorSkyLiftSpots.EdgeGap(at) < 0.1f || NearOthers(sockets, pads, at))
                        continue;
                    pad = at;
                    return true;
                }
            }
            return false;
        }

        /// <summary>다른 판 (반지름 둘 + 1 m) · 같은 바닥 아래 해치 틀 (판 반지름 + 0.25) · 같은 바닥 문 (1.8 m) 가까이.</summary>
        private static bool NearOthers(List<InteriorSocket> sockets, List<Vector3> pads, Vector3 at)
        {
            foreach (var other in pads)
            {
                if (new Vector2(other.x - at.x, other.z - at.z).magnitude < InteriorSkyLiftSpots.PadRadius * 2f + 1f)
                    return true;
            }
            foreach (var s in sockets)
            {
                var c = TempOrigin + (Vector3)s.Cell * InteriorGeometry.CellSize;
                if (s.Direction == Vector3Int.down)
                {
                    var hatch = c + s.Offset + Vector3.down * s.Depth;
                    if (Mathf.Abs(hatch.y - at.y) < 1.5f && InteriorSkyLiftSpots.FrameGap(hatch, s.HatchScale, at) < InteriorSkyLiftSpots.PadRadius + 0.25f)
                        return true;
                }
                else if (s.Direction.y == 0 && !s.SealedWall && !s.DoorLift)
                {
                    var door = c + (Vector3)s.Direction * s.Depth;
                    if (Mathf.Abs(TempOrigin.y + InteriorGeometry.FloorY(s.Cell) - at.y) < 1f && new Vector2(door.x - at.x, door.z - at.z).magnitude < 1.8f)
                        return true;
                }
            }
            return false;
        }

        /// <summary>문 깊이 하한 (칸 가운데에서 m) — 이보다 안쪽 문은 튜브가 방 안으로 깊이 들어옴.</summary>
        private const float MinDepth = 2.4f;

        /// <summary>
        /// 문 벽 (두께 0.2)부터 칸 끝까지 문 구멍 폭 · 그 바깥 튜브 자리 (끝 고리 뒤, 폭 <see cref="InteriorGeometry.TubeHalfWidth"/> × 2)에 문턱 위 0.12 ~ 튜브 높이 충돌체가 없고,
        /// 문 평면 바깥에 충돌체 없는 장식 (난간 · 풀 · 덩굴 등) 정점도 없음 — 문 · 튜브가 모델을 뚫지 않게 (①에서 유리 난간 · 벽 · 물막이를 가로지른 문이 열렸었음).
        /// </summary>
        private static bool Corridor(InteriorSkyLiftSpots.DecorGrid decor, Vector3Int cell, Vector3Int dir, float depth)
        {
            const float Edge = InteriorGeometry.CellSize * 0.5f + 0.05f;
            var center = TempOrigin + (Vector3)cell * InteriorGeometry.CellSize;
            float sill = TempOrigin.y + InteriorGeometry.FloorY(cell);
            // 바닥 (잔디 · 마루가 문턱보다 조금 높을 수 있음) 위부터
            var at = center + (Vector3)dir * (depth - 0.25f);
            float ground = Physics.Raycast(new Vector3(at.x, sill + 1f, at.z), Vector3.down, out var hit, 1.31f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? Mathf.Max(sill, hit.point.y) : sill;
            float bottom = ground + 0.12f, top = sill + InteriorGeometry.TubeHeight;
            const float Opening = InteriorGeometry.DoorWidth * 0.5f;
            var side = Vector3.Cross(Vector3.up, dir);
            if (Blocked(center, dir, side, depth - InteriorGeometry.Thickness, Edge, 0f, Opening, bottom, top))
                return false;
            foreach (float s in new[] { -1f, 1f })
            {
                if (Blocked(center, dir, side, depth + CollarCover, Edge, s * (Opening + InteriorGeometry.TubeHalfWidth) * 0.5f, (InteriorGeometry.TubeHalfWidth - Opening) * 0.5f, bottom, top))
                    return false;
            }
            for (float s = depth + 0.25f; s <= Edge + 0.001f; s += 0.2f)
            {
                foreach (float x in new[] { -0.5f, 0f, 0.5f })
                {
                    if (decor.Any(center + (Vector3)dir * s + side * x, 0.25f, ground + 0.15f, top))
                        return false;
                }
            }
            for (float s = depth + CollarCover + 0.25f; s <= Edge + 0.001f; s += 0.2f)
            {
                foreach (float x in new[] { -1f, 1f })
                {
                    if (decor.Any(center + (Vector3)dir * s + side * x, 0.25f, ground + 0.15f, top))
                        return false;
                }
            }
            return true;
        }

        /// <summary>튜브 끝 고리 두께 — 문 평면 바깥 이만큼은 문 구멍 폭 밖 (튜브 벽 쪽) 조각을 고리가 덮음 (생물권 · AI 중추 난간 끝이 문틀 옆에 닿음).</summary>
        private const float CollarCover = 0.3f;

        /// <summary>문 방향 from ~ to · 옆 가운데 lateral ± halfWidth · 높이 bottom ~ top 상자에 충돌체가 있음.</summary>
        private static bool Blocked(Vector3 center, Vector3Int dir, Vector3 side, float from, float to, float lateral, float halfWidth, float bottom, float top)
        {
            if (to <= from)
                return false;
            var mid = center + (Vector3)dir * ((from + to) * 0.5f) + side * lateral;
            mid.y = (bottom + top) * 0.5f;
            return Physics.CheckBox(mid, new Vector3(halfWidth, (top - bottom) * 0.5f, (to - from) * 0.5f), Quaternion.LookRotation(dir), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>문 안쪽 가운데 · 양옆 세 자리: 문턱 높이 마디가 있고 걸어서 닿음 + 문턱 바로 안 (0.25 m)에도 문턱 높이 바닥.</summary>
        private static bool Clear(InteriorWalkGraph graph, Vector3Int cell, Vector3Int dir, float depth)
        {
            var center = TempOrigin + (Vector3)cell * InteriorGeometry.CellSize;
            float sill = TempOrigin.y + InteriorGeometry.FloorY(cell);
            var side = Vector3.Cross(Vector3.up, dir);
            foreach (float s in new[] { 0f, -Side, Side })
            {
                var p = center + (Vector3)dir * (depth - Inside) + side * s;
                int k = graph.Node(new Vector3(p.x, sill, p.z), 0.3f);
                if (k < 0 || !graph.Reached(k) || Mathf.Abs(graph[k].y - sill) > SillTolerance)
                    return false;
                var e = center + (Vector3)dir * (depth - Edge) + side * s;
                if (!Physics.Raycast(new Vector3(e.x, sill + 1f, e.z), Vector3.down, out var hit, 1f + SillTolerance + 0.01f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    || Mathf.Abs(hit.point.y - sill) > SillTolerance || hit.normal.y < 0.7f)
                    return false;
            }
            return true;
        }
    }
}
