using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>5-7 각진 저폴리 바위 메시 (운석·배경 소행성). 정이십면체를 한 번 나눈 뒤 반지름을 흔들고 면 단위로 평평하게 칠한다.</summary>
    public static class RockMesh
    {
        public static Mesh Create(int seed, float roughness = 0.28f, int subdivisions = 1)
        {
            var rng = new System.Random(seed);
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            var tris = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int i = 0; i < verts.Count; i++)
                verts[i] = verts[i].normalized;
            for (int s = 0; s < subdivisions; s++)
                Subdivide(verts, tris);

            // 반지름 흔들기 + 약간 찌그러뜨리기
            var squash = new Vector3(1f + (float)rng.NextDouble() * 0.3f, 0.75f + (float)rng.NextDouble() * 0.25f, 0.85f + (float)rng.NextDouble() * 0.3f);
            for (int i = 0; i < verts.Count; i++)
            {
                float r = 1f - roughness * 0.5f + (float)rng.NextDouble() * roughness;
                verts[i] = Vector3.Scale(verts[i] * r, squash) * 0.5f;
            }

            // 면마다 정점을 나눠 평평한 음영
            var flatVerts = new Vector3[tris.Count];
            var flatTris = new int[tris.Count];
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                // 바깥을 향하도록 (Unity 앞면 = 시계 방향)
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f)
                    (b, c) = (c, b);
                flatVerts[i] = a;
                flatVerts[i + 1] = b;
                flatVerts[i + 2] = c;
                flatTris[i] = i;
                flatTris[i + 1] = i + 1;
                flatTris[i + 2] = i + 2;
            }
            var mesh = new Mesh { name = "Rock" + seed };
            mesh.vertices = flatVerts;
            mesh.triangles = flatTris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void Subdivide(List<Vector3> verts, List<int> tris)
        {
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int idx))
                    return idx;
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                cache[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            var result = new List<int>(tris.Count * 4);
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                result.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris.Clear();
            tris.AddRange(result);
        }
    }
}
