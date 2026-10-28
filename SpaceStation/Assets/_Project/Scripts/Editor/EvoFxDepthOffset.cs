using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 진화 모듈 FBX 가져오기 후처리 (2026-10-27, 사용자 "1단계 뚜껑 재질 · 중력 거주구 위 · 융합 발전소 바닥 일렁거림"):
    /// Blender가 유리 · 창 · 형광 빛줄 · 반짝임 같은 연출 면을 몸체 면과 같은 평면(0mm)에 겹쳐 내보낸 곳이 진화 모델 거의 전부에 있어,
    /// 카메라가 움직일 때 두 면이 번갈아 그려지며 깜빡였다 (z-fighting — 투명 유리는 깊이를 쓰지 않아 같은 깊이의 불투명 몸체와
    /// 화소마다 경쟁). 48종을 Blender에서 다시 굽는 대신 가져올 때 고친다:
    /// 같은 평면(0.3mm 안)에서 넓이 있게 겹치는 삼각형 쌍을 찾아, 우선순위가 높은 쪽(반짝임 &gt; 투명 &gt; 그 밖 연출 &gt; 몸체 · 부품)을
    /// 그 면이 속한 평평한 덩어리째 법선 방향으로 1.7mm × 우선순위 차이만큼 띄운다 (같은 순위 연출끼리는 재질 이름 순으로 0.9mm).
    /// 등을 맞댄 불투명 면끼리는 서로 가려져 깜빡이지 않으므로 그대로, 양면으로 그리는 투명 연출이 끼면 띄운다.
    /// 처음엔 연출 메시 전체를 일정 거리 띄웠는데, Blender가 그 높이에 둔 다른 면과 새로 겹쳐서 겹친 곳만 띄우는 방식으로 바꿈.
    /// 후처리를 바꾸면 GetVersion을 올려 모델을 다시 가져오게 한다.
    /// </summary>
    public sealed class EvoFxDepthOffset : AssetPostprocessor
    {
        private const string ModelFolder = "Assets/_Project/Art/Models/Modules/";
        private const float Step = 0.0017f;
        private const float SameRankStep = 0.0009f;
        private const float NudgeStep = 0.00055f;
        // 같은 평면으로 볼 거리. 고칠 때 더 넓게(0.5mm · 4번) 했더니 곡면 유리가 계속 밀려 다른 층에 걸려 오히려 늘었음 (융합 플랜트)
        private const float PlaneTolerance = 0.0003f;
        // 한 칸의 (연출 × 이웃) 짝이 이보다 많으면 격자로 후보를 고름
        private const long GridMinPairs = 50000;

        private static readonly string[] TransparentNames = { "Glass", "Aurora", "LightBeam", "GalaxyWater", "SplashMist", "ShieldSail", "ShieldShell" };

        // 8: 구운 정지 연출 지우기 (EvoBakedFxStrip) 추가 · 9: 그리드 허브 번개 줄 규칙
        public override uint GetVersion() => 9;

        private struct Tri
        {
            public int Mesh, Sub, A, B, C, Rank;
            public Vector3 P0, P1, P2, N;  // 모델 기준 위치 · 법선
            public Vector3 Cn;             // 같은 평면 찾기용 방향 통일 법선
            public float D;
            public bool TwoSided;
            public string Material;
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!assetPath.StartsWith(ModelFolder) || !System.IO.Path.GetFileName(assetPath).StartsWith("SM_Evo_"))
                return;
            EvoBakedFxStrip.Strip(root, System.IO.Path.GetFileNameWithoutExtension(assetPath));
            Separate(root);
        }

        /// <summary>연출 메시 (몸체 · 움직이는 부품 본체가 아닌 것).</summary>
        public static bool IsFxObject(string objectName) => objectName.StartsWith("Emblem_Fx_") || objectName.StartsWith("Fx_");

        /// <summary>재질 이름("FX_EvoGlass" 또는 리맵된 "M_EvoGlass")의 순위: 반짝임 3 · 투명 2 · 그 밖 연출 1 · 몸체 0.</summary>
        public static int RankOf(string material, bool fxObject)
        {
            string key = material.StartsWith("FX_") ? material.Substring(3) : material.StartsWith("M_") ? material.Substring(2) : material;
            bool fx = fxObject || material.StartsWith("FX_");
            if (!fx)
                return 0;
            if (key.StartsWith("Twinkle"))
                return 3;
            foreach (var t in TransparentNames)
                if (key.Contains(t))
                    return 2;
            return 1;
        }

        /// <summary>
        /// 같은 평면에 겹친 면을 순위대로 띄운다. 띄운 면이 그 거리에 있던 다른 면(예: 유리 앞 창살)과 새로 겹칠 수 있어
        /// 다시 찾아 거리를 바꿔 되풀이 (최대 3번). 띄운 삼각형 수를 돌려줌 (확인용).
        /// </summary>
        public static int Separate(GameObject root)
        {
            int total = 0;
            for (int round = 0; round < 3; round++)
            {
                float scale = 1f + 0.37f * round;
                int moved = SeparateOnce(root, Step * scale, SameRankStep * scale, true, null, PlaneTolerance);
                total += moved;
                if (moved == 0)
                    break;
            }
            // 큰 간격으로 띄운 면이 Blender의 다른 층 0.1 ~ 0.3mm 옆에 걸린 곳은 작게 한 번 더 (Blender 층 간격은 보통 1mm 이상이라 새로 걸리지 않음)
            for (int round = 0; round < 2; round++)
            {
                float nudge = NudgeStep * (1f + 0.5f * round);
                int moved = SeparateOnce(root, nudge, nudge, true, null, PlaneTolerance);
                total += moved;
                if (moved == 0)
                    break;
            }
            return total;
        }

        /// <summary>남아 있는 (보이는) 같은 평면 겹침 짝 수 — 고치지 않고 세기만 (확인용). log가 있으면 짝 설명을 담음.</summary>
        public static int CountOverlaps(GameObject root, List<string> log = null) => SeparateOnce(root, Step, SameRankStep, false, log, PlaneTolerance);

        private static int SeparateOnce(GameObject root, float step, float sameRankStep, bool apply, List<string> log, float tolerance)
        {
            var filters = new List<MeshFilter>();
            var meshes = new List<Mesh>();
            var tris = new List<Tri>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;
                bool fxObject = IsFxObject(mf.name);
                // 움직이는 부품 아래 Fx_ 자식은 Unity(StationArtBuilder)가 부품 기준 제자리로 되돌리므로 부모 공간에서 잰다
                Transform space = mf.name.StartsWith("Fx_") && mf.transform.parent != null && mf.transform.parent != root.transform ? mf.transform.parent : mf.transform;
                var toModel = root.transform.worldToLocalMatrix * space.localToWorldMatrix;
                var mesh = mf.sharedMesh;
                int meshIndex = meshes.Count;
                filters.Add(mf);
                meshes.Add(mesh);
                var mats = mf.GetComponent<MeshRenderer>() != null ? mf.GetComponent<MeshRenderer>().sharedMaterials : null;
                var v = mesh.vertices;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    string mat = mats != null && s < mats.Length && mats[s] != null ? mats[s].name : "";
                    int rank = RankOf(mat, fxObject);
                    var idx = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < idx.Length; i += 3)
                    {
                        var t = new Tri { Mesh = meshIndex, Sub = s, A = idx[i], B = idx[i + 1], C = idx[i + 2], Rank = rank, TwoSided = rank == 2, Material = mat };
                        t.P0 = toModel.MultiplyPoint3x4(v[t.A]);
                        t.P1 = toModel.MultiplyPoint3x4(v[t.B]);
                        t.P2 = toModel.MultiplyPoint3x4(v[t.C]);
                        var n = Vector3.Cross(t.P1 - t.P0, t.P2 - t.P0);
                        if (n.sqrMagnitude < 1e-12f)
                            continue;
                        t.N = n.normalized;
                        t.Cn = Canonical(t.N);
                        t.D = Vector3.Dot(t.Cn, t.P0);
                        tris.Add(t);
                    }
                }
            }

            // 같은 평면끼리 묶기: 법선 칸(0.05)을 축마다 반 칸 어긋난 8가지로 겹쳐 담아 칸 경계에 걸린 짝도 찾음 (곡면 유리),
            // 거리는 0.5mm 칸 + 위 칸
            var buckets = new Dictionary<long, List<int>>();
            for (int i = 0; i < tris.Count; i++)
            {
                // 아주 작은 삼각형 (변 2 ~ 3mm 아래)은 normalized가 0 → 법선 0 · 거리 0으로 모두 한 칸에 모여, 짝이 될 수 없는데도 서로 비교만
                // 수십억 번 했음 (2026-10-27 3단계 동물 부품 · 잔 장식 뒤 궤도 도시 가져오기 13분). 법선 0 면은 같은 평면 검사(법선 내적 0.999)에서
                // 늘 빠지므로 칸에 넣지 않아도 결과는 같음 (덩어리 묶기에는 그대로 씀)
                if (tris[i].Cn.sqrMagnitude < 0.5f)
                    continue;
                for (int variant = 0; variant < 8; variant++)
                {
                    long key = PlaneKey(tris[i].Cn, tris[i].D, variant);
                    if (!buckets.TryGetValue(key, out var list))
                        buckets[key] = list = new List<int>();
                    list.Add(i);
                }
            }
            var pairs = new List<(int hi, int lo, float gap)>();
            var seen = new HashSet<long>();
            foreach (var kv in buckets)
            {
                var list = kv.Value;
                // 연출 면이 낀 짝만 본다 (몸체끼리는 볼 필요 없음 — 큰 바닥 평면 칸에 몸체 삼각형이 수천 개라 전부 짝지으면 느림)
                var fx = new List<int>();
                foreach (int i in list)
                    if (tris[i].Rank > 0)
                        fx.Add(i);
                buckets.TryGetValue(kv.Key + 1, out var up);
                if (up != null)
                    foreach (int i in up)
                        if (tris[i].Rank > 0)
                            fx.Add(i);
                if (fx.Count == 0)
                    continue;
                var near = up == null ? list : new List<int>(list);
                if (up != null)
                    near.AddRange(up);
                // 짝이 아주 많은 칸은 겹칠 수 있는 (평면 위 테두리 상자가 닿는) 삼각형끼리만 (결과 · 순서는 같음, PlaneGrid 참고)
                var grid = (long)fx.Count * near.Count > GridMinPairs ? PlaneGrid.TryCreate(tris, near) : null;
                var cand = grid != null ? new List<int>() : null;
                for (int x = 0; x < fx.Count; x++)
                {
                    int count = near.Count;
                    if (grid != null)
                    {
                        grid.Query(tris[fx[x]], cand);
                        count = cand.Count;
                    }
                    for (int y = 0; y < count; y++)
                    {
                        int i = fx[x], j = grid != null ? near[cand[y]] : near[y];
                        if (i == j)
                            continue;
                        var a = tris[i];
                        var b = tris[j];
                        if ((a.Mesh == b.Mesh && a.Sub == b.Sub) || Vector3.Dot(a.Cn, b.Cn) < 0.999f || Mathf.Abs(a.D - b.D) > tolerance)
                            continue;
                        if (!seen.Add(Mathf.Min(i, j) * (long)tris.Count + Mathf.Max(i, j)))
                            continue;
                        bool sameFacing = Vector3.Dot(a.N, b.N) > 0f;
                        if (!sameFacing && !a.TwoSided && !b.TwoSided)
                            continue; // 등을 맞댄 불투명 면 — 서로 가려져 안 보임
                        if (!Overlap(a, b))
                            continue;
                        int hi = a.Rank > b.Rank || (a.Rank == b.Rank && string.CompareOrdinal(a.Material, b.Material) > 0) ? i : j;
                        int lo = hi == i ? j : i;
                        pairs.Add((hi, lo, tris[hi].Rank > tris[lo].Rank ? step * (tris[hi].Rank - tris[lo].Rank) : sameRankStep));
                    }
                }
            }
            if (!apply)
            {
                if (log != null)
                    foreach (var (hi, lo, gap) in pairs)
                    {
                        var h = tris[hi];
                        var l = tris[lo];
                        log.Add($"{filters[h.Mesh].name}:{h.Material} ↔ {filters[l.Mesh].name}:{l.Material} · {(Vector3.Dot(h.N, l.N) > 0f ? "같은 쪽" : "등 맞댐")} · 거리 {Mathf.Abs(h.D - l.D) * 1000f:F2}mm · 위치 {(h.P0 + h.P1 + h.P2) / 3f:F3}");
                    }
                return pairs.Count;
            }
            // 아래 층이 띄워지면 위 층도 그만큼 더 (몇 번 되풀이하면 안정)
            var push = new float[tris.Count];
            for (int pass = 0; pass < 4; pass++)
                foreach (var (hi, lo, gap) in pairs)
                    push[hi] = Mathf.Max(push[hi], push[lo] + gap);

            // 평평한 덩어리(꼭짓점을 같이 쓰는 삼각형)째 같은 거리로 → 사각형이 꺾이지 않게
            int moved = 0;
            for (int m = 0; m < meshes.Count; m++)
            {
                var mesh = meshes[m];
                var verts = mesh.vertices;
                var normals = mesh.normals;
                if (normals == null || normals.Length != verts.Length)
                    continue;
                var parent = new int[verts.Length];
                for (int i = 0; i < parent.Length; i++)
                    parent[i] = i;
                var amount = new float[verts.Length];
                bool any = false;
                for (int t = 0; t < tris.Count; t++)
                {
                    if (tris[t].Mesh != m)
                        continue;
                    Union(parent, tris[t].A, tris[t].B);
                    Union(parent, tris[t].A, tris[t].C);
                    if (push[t] > 0f)
                        any = true;
                }
                if (!any)
                    continue;
                var groupPush = new Dictionary<int, float>();
                for (int t = 0; t < tris.Count; t++)
                {
                    if (tris[t].Mesh != m || push[t] <= 0f)
                        continue;
                    int g = Find(parent, tris[t].A);
                    groupPush.TryGetValue(g, out float p);
                    groupPush[g] = Mathf.Max(p, push[t]);
                    moved++;
                }
                for (int i = 0; i < verts.Length; i++)
                {
                    if (groupPush.TryGetValue(Find(parent, i), out float p))
                        verts[i] += normals[i] * p;
                }
                mesh.vertices = verts;
                mesh.RecalculateBounds();
            }
            return moved;
        }

        private static Vector3 Canonical(Vector3 n)
        {
            if (n.y < -1e-4f || (Mathf.Abs(n.y) <= 1e-4f && (n.x < -1e-4f || (Mathf.Abs(n.x) <= 1e-4f && n.z < 0f))))
                return -n;
            return n;
        }

        private static long PlaneKey(Vector3 n, float d, int variant)
        {
            float sx = (variant & 1) * 0.5f, sy = ((variant >> 1) & 1) * 0.5f, sz = ((variant >> 2) & 1) * 0.5f;
            long nx = Mathf.FloorToInt(n.x * 20f + sx), ny = Mathf.FloorToInt(n.y * 20f + sy), nz = Mathf.FloorToInt(n.z * 20f + sz);
            return (((((variant * 64L) + nx + 30) * 64 + (ny + 30)) * 64 + (nz + 30)) * 4000003L) + Mathf.FloorToInt(d * 2000f) + 2000000;
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra != rb)
                parent[ra] = rb;
        }

        /// <summary>
        /// 큰 평면 칸에서 서로 겹칠 수 있는 삼각형만 고르는 평면 격자 (2026-10-28 AI 중추: 빛 신경 줄 윗면 · 아랫면 3,700여 개가
        /// 진주 자갈 바닥과 한 칸에 몰려 전부 짝지어 보느라 가져오기 4분). 칸 안 법선이 모두 같을 때만 씀 (평평한 바닥 · 물 높이 —
        /// 그때 Overlap이 쓰는 평면 좌표가 모두 같아, 넓이 있게 겹치는 두 삼각형은 평면 위 테두리 상자가 반드시 닿음 → 결과가 같음).
        /// 법선이 조금씩 다른 칸 (곡면 유리)은 null → 전부 비교. 후보는 near 순서 그대로 돌려줘 짝 순서도 같다.
        /// </summary>
        private sealed class PlaneGrid
        {
            private const float Cell = 0.05f;
            private const float Pad = 0.002f;
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
            private readonly int[] _mark;
            private int _stamp;
            private readonly Vector3 _u, _w;

            private PlaneGrid(Vector3 n, int count)
            {
                _u = Vector3.Cross(n, Mathf.Abs(n.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
                _w = Vector3.Cross(n, _u);
                _mark = new int[count];
            }

            public static PlaneGrid TryCreate(List<Tri> tris, List<int> near)
            {
                var n = tris[near[0]].Cn;
                foreach (int j in near)
                    if (Vector3.Dot(tris[j].Cn, n) < 0.99999999f)
                        return null;
                var grid = new PlaneGrid(n, near.Count);
                for (int y = 0; y < near.Count; y++)
                {
                    grid.Range(tris[near[y]], out int u0, out int u1, out int w0, out int w1);
                    for (int a = u0; a <= u1; a++)
                        for (int b = w0; b <= w1; b++)
                        {
                            long key = ((long)a << 32) ^ (uint)b;
                            if (!grid._cells.TryGetValue(key, out var list))
                                grid._cells[key] = list = new List<int>();
                            list.Add(y);
                        }
                }
                return grid;
            }

            private void Range(Tri t, out int u0, out int u1, out int w0, out int w1)
            {
                float a0 = Vector3.Dot(t.P0, _u), a1 = Vector3.Dot(t.P1, _u), a2 = Vector3.Dot(t.P2, _u);
                float b0 = Vector3.Dot(t.P0, _w), b1 = Vector3.Dot(t.P1, _w), b2 = Vector3.Dot(t.P2, _w);
                u0 = Mathf.FloorToInt((Mathf.Min(a0, Mathf.Min(a1, a2)) - Pad) / Cell);
                u1 = Mathf.FloorToInt((Mathf.Max(a0, Mathf.Max(a1, a2)) + Pad) / Cell);
                w0 = Mathf.FloorToInt((Mathf.Min(b0, Mathf.Min(b1, b2)) - Pad) / Cell);
                w1 = Mathf.FloorToInt((Mathf.Max(b0, Mathf.Max(b1, b2)) + Pad) / Cell);
            }

            /// <summary>t와 평면 위 테두리 상자가 닿는 near 위치들 (오름차순).</summary>
            public void Query(Tri t, List<int> result)
            {
                result.Clear();
                _stamp++;
                Range(t, out int u0, out int u1, out int w0, out int w1);
                for (int a = u0; a <= u1; a++)
                    for (int b = w0; b <= w1; b++)
                    {
                        if (!_cells.TryGetValue(((long)a << 32) ^ (uint)b, out var list))
                            continue;
                        foreach (int y in list)
                            if (_mark[y] != _stamp)
                            {
                                _mark[y] = _stamp;
                                result.Add(y);
                            }
                    }
                result.Sort();
            }
        }

        /// <summary>같은 평면 두 삼각형이 넓이 있게 겹치는지 (2D 분리축, 1mm 안쪽으로 줄여 모서리만 닿는 것은 뺌).</summary>
        private static bool Overlap(Tri a, Tri b)
        {
            Vector3 u = Vector3.Cross(a.Cn, Mathf.Abs(a.Cn.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 w = Vector3.Cross(a.Cn, u);
            var pa = Shrink(Flat(a.P0, u, w), Flat(a.P1, u, w), Flat(a.P2, u, w));
            var pb = Shrink(Flat(b.P0, u, w), Flat(b.P1, u, w), Flat(b.P2, u, w));
            if (pa == null || pb == null)
                return false;
            return !Separated(pa, pb) && !Separated(pb, pa);
        }

        private static Vector2 Flat(Vector3 p, Vector3 u, Vector3 w) => new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w));

        private static Vector2[] Shrink(Vector2 p0, Vector2 p1, Vector2 p2)
        {
            var c = (p0 + p1 + p2) / 3f;
            var src = new[] { p0, p1, p2 };
            var r = new Vector2[3];
            for (int i = 0; i < 3; i++)
            {
                var d = src[i] - c;
                float m = d.magnitude;
                if (m < 0.002f)
                    return null;
                r[i] = c + d * ((m - 0.001f) / m);
            }
            return r;
        }

        private static bool Separated(Vector2[] a, Vector2[] b)
        {
            for (int i = 0; i < 3; i++)
            {
                var e = a[(i + 1) % 3] - a[i];
                var axis = new Vector2(-e.y, e.x);
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (var p in a)
                {
                    float d = Vector2.Dot(p, axis);
                    amin = Mathf.Min(amin, d);
                    amax = Mathf.Max(amax, d);
                }
                foreach (var p in b)
                {
                    float d = Vector2.Dot(p, axis);
                    bmin = Mathf.Min(bmin, d);
                    bmax = Mathf.Max(bmax, d);
                }
                if (amax <= bmin || bmax <= amin)
                    return true;
            }
            return false;
        }
    }
}
