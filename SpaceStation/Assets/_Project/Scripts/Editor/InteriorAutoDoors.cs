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
    /// 문 자리가 없는 (또는 닫힌) 둘레 옆면 중 문 안쪽 바닥이 문턱 높이 · 비어 있고 걸어서 닿는 면에 열린 문 자리를 더한다.
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
                var cells = new HashSet<Vector3Int>(module.CellOffsets);
                float depth = DoorDepth(sockets);
                foreach (var cell in module.CellOffsets)
                {
                    foreach (var dir in GridDirections.Faces)
                    {
                        if (dir.y != 0 || cells.Contains(cell + dir))
                            continue;
                        int i = sockets.FindIndex(s => s.Cell == cell && s.Direction == dir);
                        if (i >= 0 && !sockets[i].SealedWall)
                            continue;
                        if (!Clear(graph, cell, dir, depth))
                            continue;
                        var open = new InteriorSocket(cell, dir, depth);
                        if (i >= 0)
                            sockets[i] = open;
                        else
                            sockets.Add(open);
                        opened++;
                        log?.Add($"{name ?? module.name} {cell} {dir}");
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
