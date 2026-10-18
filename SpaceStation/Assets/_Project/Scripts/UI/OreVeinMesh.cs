using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 13-5a 소행성 광맥: 바위 메시(RockMesh — 면마다 꼭짓점이 따로)의 면을 따라 이어지는 얇은 띠 + 드러난 광석 조각.
    /// 띠 조각은 면 하나 안에 놓이고, 이웃 면으로 넘어가는 점은 두 면이 함께 쓰는 가장자리 위에 있어 표면에 딱 붙는다
    /// (상자를 꽂았더니 바위 밖으로 삐져나오거나 속에 묻혔음 — 사용자 피드백 2026-10-18).
    /// </summary>
    public static class OreVeinMesh
    {
        public struct Style
        {
            public int Veins;
            public int MinLength;
            public int MaxLength;
            /// <summary>띠 반폭 (바위 메시 단위, 반지름 약 0.5)</summary>
            public float Width;
            /// <summary>면마다 곁가지가 날 확률</summary>
            public float Branch;
            /// <summary>광맥 위 광석 조각 수</summary>
            public int Patches;
            /// <summary>면에서 띄우는 거리 (겹치는 다른 띠보다 크게 = 위에 그려짐)</summary>
            public float Lift;
        }

        public static Mesh Create(Mesh rock, System.Random rng, Style style)
        {
            var walker = new Walker(rock, rng, style);
            for (int i = 0; i < style.Veins; i++)
                walker.Vein();
            for (int i = 0; i < style.Patches; i++)
                walker.Patch();
            var mesh = new Mesh { name = "OreVeins" };
            mesh.SetVertices(walker.Verts);
            mesh.SetTriangles(walker.Tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private sealed class Walker
        {
            public readonly List<Vector3> Verts = new List<Vector3>();
            public readonly List<int> Tris = new List<int>();
            private readonly Vector3[] _v;
            private readonly int[] _t;
            private readonly int[] _nbr;     // 면 * 3 + 가장자리 → 이웃 면
            private readonly int[] _nbrEdge; // 이웃 면에서 같은 가장자리 번호
            private readonly System.Random _rng;
            private readonly Style _style;
            private readonly HashSet<int> _visited = new HashSet<int>();
            private readonly List<int> _veinFaces = new List<int>();

            public Walker(Mesh rock, System.Random rng, Style style)
            {
                _v = rock.vertices;
                _t = rock.triangles;
                _rng = rng;
                _style = style;
                int faces = _t.Length / 3;
                _nbr = new int[faces * 3];
                _nbrEdge = new int[faces * 3];
                var open = new Dictionary<(Vector3Int, Vector3Int), int>();
                for (int f = 0; f < faces; f++)
                    for (int e = 0; e < 3; e++)
                    {
                        _nbr[f * 3 + e] = -1;
                        var a = Q(V(f, e));
                        var b = Q(V(f, (e + 1) % 3));
                        var key = Less(a, b) ? (a, b) : (b, a);
                        if (open.TryGetValue(key, out int other))
                        {
                            _nbr[f * 3 + e] = other / 3;
                            _nbrEdge[f * 3 + e] = other % 3;
                            _nbr[other] = f;
                            _nbrEdge[other] = e;
                            open.Remove(key);
                        }
                        else
                            open[key] = f * 3 + e;
                    }
            }

            private static Vector3Int Q(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 20000f), Mathf.RoundToInt(p.y * 20000f), Mathf.RoundToInt(p.z * 20000f));

            private static bool Less(Vector3Int a, Vector3Int b) => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

            private float R() => (float)_rng.NextDouble();

            private Vector3 V(int f, int i) => _v[_t[f * 3 + i]];

            private Vector3 Normal(int f) => Vector3.Cross(V(f, 1) - V(f, 0), V(f, 2) - V(f, 0)).normalized; // RockMesh는 바깥을 향하게 감음

            private Vector3 Centroid(int f) => (V(f, 0) + V(f, 1) + V(f, 2)) / 3f;

            private Vector3 EdgeMid(int f, int e) => (V(f, e) + V(f, (e + 1) % 3)) * 0.5f;

            /// <summary>가장자리 위 넘어가는 점 (양쪽 면이 같이 씀). 끝점 가까이는 피하고, 반폭은 가장자리 길이에 맞춰 줄임.</summary>
            private (Vector3 p, Vector3 dir, float w) Crossing(int f, int e, float w)
            {
                var a = V(f, e);
                var b = V(f, (e + 1) % 3);
                float len = Vector3.Distance(a, b);
                w = Mathf.Min(w, len * 0.3f);
                float m = w / len + 0.08f;
                return (Vector3.Lerp(a, b, Mathf.Lerp(m, 1f - m, R())), (b - a) / len, w);
            }

            public void Vein()
            {
                int faces = _t.Length / 3;
                int f = -1;
                for (int i = 0; i < 12 && f < 0; i++)
                {
                    int c = _rng.Next(faces);
                    if (!_visited.Contains(c))
                        f = c;
                }
                if (f < 0)
                    return;
                int length = _style.MinLength + _rng.Next(Mathf.Max(1, _style.MaxLength - _style.MinLength + 1));
                var start = Vector3.Lerp(Centroid(f), V(f, _rng.Next(3)), R() * 0.3f);
                _visited.Add(f);
                Walk(f, start, Vector3.zero, 0f, -1, length, _style.Width, _style.Branch, -1);
            }

            private void Walk(int f, Vector3 inP, Vector3 inDir, float inW, int inEdge, int length, float width, float branch, int forcedExit)
            {
                var heading = Vector3.zero;
                for (int k = 0; k < length; k++)
                {
                    _veinFaces.Add(f);
                    int exit = k == length - 1 ? -1 : k == 0 && forcedExit >= 0 ? (Open(f, forcedExit) ? forcedExit : -1) : ChooseExit(f, inEdge, inP, heading);
                    if (exit < 0) // 끝: 면 안쪽에서 뾰족하게
                    {
                        var tip = Vector3.Lerp(inP, Centroid(f), 0.45f + R() * 0.35f);
                        if ((tip - inP).sqrMagnitude > 1e-6f)
                            Segment(f, inP, inDir, inW, tip, Vector3.zero, 0f);
                        return;
                    }
                    float taper = Mathf.Lerp(0.45f, 1f, Mathf.Sin(Mathf.PI * (k + 1) / length));
                    var c = Crossing(f, exit, width * taper * (0.8f + R() * 0.4f));
                    Segment(f, inP, inDir, inW, c.p, c.dir, c.w);
                    if (branch > 0f && k > 0 && inEdge >= 0 && R() < branch) // 곁가지: 남은 가장자리 쪽으로 가늘게
                    {
                        int third = 3 - inEdge - exit;
                        if (Open(f, third))
                            Walk(f, (inP + c.p) * 0.5f, Vector3.zero, 0f, -1, 2 + _rng.Next(3), width * 0.55f, 0f, third);
                    }
                    heading = (c.p - inP).normalized;
                    int nf = _nbr[f * 3 + exit];
                    _visited.Add(nf);
                    inEdge = _nbrEdge[f * 3 + exit];
                    f = nf;
                    inP = c.p;
                    inDir = c.dir;
                    inW = c.w;
                }
            }

            private bool Open(int f, int e)
            {
                int n = _nbr[f * 3 + e];
                return n >= 0 && !_visited.Contains(n);
            }

            /// <summary>들어온 가장자리 말고 둘 중 하나 — 대체로 곧게 (한 꼭짓점을 빙글 도는 것을 줄임).</summary>
            private int ChooseExit(int f, int inEdge, Vector3 inP, Vector3 heading)
            {
                int a = -1, b = -1, off = _rng.Next(3);
                for (int i = 0; i < 3; i++)
                {
                    int e = (i + off) % 3;
                    if (e == inEdge || !Open(f, e))
                        continue;
                    if (a < 0) a = e;
                    else b = e;
                }
                if (b < 0)
                    return a;
                if (heading == Vector3.zero)
                    return R() < 0.5f ? a : b;
                float da = Vector3.Dot((EdgeMid(f, a) - inP).normalized, heading);
                float db = Vector3.Dot((EdgeMid(f, b) - inP).normalized, heading);
                int best = da >= db ? a : b;
                return R() < 0.72f ? best : best == a ? b : a;
            }

            /// <summary>면 f 안의 띠 한 조각: 들어온 점(반폭 inW, 가장자리 방향 inDir) → 나가는 점. 반폭 0이면 뾰족한 끝.</summary>
            private void Segment(int f, Vector3 inP, Vector3 inDir, float inW, Vector3 outP, Vector3 outDir, float outW)
            {
                var n = Normal(f);
                if (inW <= 0f)
                {
                    Tri(inP, outP + outDir * outW, outP - outDir * outW, n);
                    return;
                }
                if (outW <= 0f)
                {
                    Tri(inP + inDir * inW, inP - inDir * inW, outP, n);
                    return;
                }
                var along = outP - inP;
                if (Mathf.Sign(Vector3.Dot(Vector3.Cross(along, inDir), n)) != Mathf.Sign(Vector3.Dot(Vector3.Cross(along, outDir), n)))
                    outDir = -outDir; // 꼬인 사각형 방지
                var a1 = inP + inDir * inW;
                var a2 = inP - inDir * inW;
                var b1 = outP + outDir * outW;
                var b2 = outP - outDir * outW;
                Tri(a1, b1, b2, n);
                Tri(a1, b2, a2, n);
            }

            /// <summary>광맥이 지나간 면 위에 드러난 광석 조각 (면 안의 불규칙한 육각형).</summary>
            public void Patch()
            {
                if (_veinFaces.Count == 0)
                    return;
                int f = _veinFaces[_rng.Next(_veinFaces.Count)];
                var n = Normal(f);
                var c = Centroid(f);
                var ring = new Vector3[6];
                for (int j = 0; j < 6; j++)
                {
                    var corner = j % 2 == 0 ? V(f, j / 2) : EdgeMid(f, j / 2);
                    ring[j] = Vector3.Lerp(c, corner, 0.22f + R() * 0.3f);
                }
                for (int j = 0; j < 6; j++)
                    Tri(c, ring[j], ring[(j + 1) % 6], n, _style.Lift * 1.5f);
            }

            private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n, float lift = -1f)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f)
                    (b, c) = (c, b);
                var up = n * (lift < 0f ? _style.Lift : lift);
                int i = Verts.Count;
                Verts.Add(a + up);
                Verts.Add(b + up);
                Verts.Add(c + up);
                Tris.Add(i);
                Tris.Add(i + 1);
                Tris.Add(i + 2);
            }
        }
    }
}
