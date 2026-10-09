using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-13 홀로그램 외곽선 (순수 로직): 삼각형 메시에서 각진 모서리(두 면 사이 각이 기준 이상)와 열린 가장자리만 선분으로 뽑는다.
    /// 같은 위치의 정점(UV · 노멀 때문에 갈라진 것)은 하나로 본다. 아주 짧은 선분은 작은 모형에서 잡음이라 버린다.
    /// </summary>
    public static class HoloEdges
    {
        private struct EdgeInfo
        {
            public Vector3 Normal;
            public int Count;
            public bool Sharp;
        }

        /// <param name="sharpAngle">이 각(도) 이상 꺾인 모서리만.</param>
        /// <param name="minLength">이보다 짧은 선분은 버림.</param>
        /// <param name="result">선분 끝점 쌍 (0-1, 2-3, …)이 더해짐.</param>
        public static void Extract(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> triangles, float sharpAngle, float minLength, List<Vector3> result)
        {
            const float weld = 1e-4f;
            var ids = new int[vertices.Count];
            var byPos = new Dictionary<Vector3Int, int>();
            var points = new List<Vector3>();
            for (int i = 0; i < vertices.Count; i++)
            {
                var v = vertices[i];
                var key = new Vector3Int(Mathf.RoundToInt(v.x / weld), Mathf.RoundToInt(v.y / weld), Mathf.RoundToInt(v.z / weld));
                if (!byPos.TryGetValue(key, out int id))
                {
                    id = points.Count;
                    points.Add(v);
                    byPos.Add(key, id);
                }
                ids[i] = id;
            }

            float cosLimit = Mathf.Cos(sharpAngle * Mathf.Deg2Rad);
            var edges = new Dictionary<long, EdgeInfo>();
            var order = new List<long>();
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                int a = ids[triangles[t]], b = ids[triangles[t + 1]], c = ids[triangles[t + 2]];
                if (a == b || b == c || a == c)
                    continue;
                var n = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                if (n.sqrMagnitude < 1e-12f)
                    continue;
                n.Normalize();
                Add(edges, order, a, b, n, cosLimit);
                Add(edges, order, b, c, n, cosLimit);
                Add(edges, order, c, a, n, cosLimit);
            }

            float minSq = minLength * minLength;
            foreach (long key in order)
            {
                var e = edges[key];
                if (e.Count != 1 && !e.Sharp && e.Count <= 2)
                    continue;
                int a = (int)(key >> 32), b = (int)(key & 0xffffffff);
                if ((points[a] - points[b]).sqrMagnitude < minSq)
                    continue;
                result.Add(points[a]);
                result.Add(points[b]);
            }
        }

        private static void Add(Dictionary<long, EdgeInfo> edges, List<long> order, int a, int b, Vector3 normal, float cosLimit)
        {
            if (a > b)
                (a, b) = (b, a);
            long key = ((long)a << 32) | (uint)b;
            if (edges.TryGetValue(key, out var e))
            {
                if (Vector3.Dot(e.Normal, normal) < cosLimit)
                    e.Sharp = true;
                e.Count++;
                edges[key] = e;
            }
            else
            {
                edges.Add(key, new EdgeInfo { Normal = normal, Count = 1 });
                order.Add(key);
            }
        }
    }
}
