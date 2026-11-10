using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>연구원 내부 "별빛 데이터 미로" (Blender mz_builder와 같은 값): 길잡이 길 · 해치 자리 · 입구와 해치에서 연구실까지 걸어 닿음 · 빛 알갱이 트임 숨김.</summary>
    public class ResearchMazeTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interior/PF_Interior_EvoResearchInstitute.prefab";

        [Test]
        public void Route_MatchesBlender()
        {
            Assert.AreEqual(12, ResearchMaze.Route.Length);
            Assert.AreEqual(new Vector2(4f, -3.1f), ResearchMaze.Route[0]);
            Assert.AreEqual(new Vector2(1.9f, 4f), ResearchMaze.Route[ResearchMaze.Route.Length - 1]);
            Assert.AreEqual(51.72f, ResearchMaze.RouteLength(), 0.05f);
            Assert.AreEqual(-2.56f, ResearchMaze.ArchZ, 1e-4f);
        }

        [Test]
        public void RingPoint_HidesInGaps()
        {
            // 바깥 고리 앞 트임 (s = h, 앞 가운데) · 가운데 고리 뒤 트임 (s = 5h) · 안 고리 서쪽 트임 (s = 7h)
            Assert.IsFalse(ResearchMaze.RingPoint(0, ResearchMaze.Rings[0], 0.2f, out _, out _));
            Assert.IsFalse(ResearchMaze.RingPoint(1, 5f * ResearchMaze.Rings[1], 0.2f, out _, out _));
            Assert.IsFalse(ResearchMaze.RingPoint(2, 7f * ResearchMaze.Rings[2], 0.2f, out _, out _));
            Assert.IsTrue(ResearchMaze.RingPoint(0, 3f * ResearchMaze.Rings[0], 0.2f, out var p, out var d));
            Assert.AreEqual(ResearchMaze.Center.x + ResearchMaze.Rings[0], p.x, 1e-4f);
            Assert.AreEqual(Vector3.forward, d);
        }

        [Test]
        public void Hatches_InsideTheirCells()
        {
            foreach (var (cell, off) in ResearchMaze.Hatches)
            {
                Assert.Less(Mathf.Abs(off.x), 3.3f, $"{cell}");
                Assert.Less(Mathf.Abs(off.y), 3.3f, $"{cell}");
            }
        }

        /// <summary>주민 서는 자리가 미로 길 (바깥 고리 ~ 안 고리 사이, 폭 1.4 ~ 1.5) 안에 없음 — 선 주민이 길을 막음 (사용자 2026-11-10).</summary>
        [Test]
        public void ResidentSpots_StayOutOfCorridors()
        {
            var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>("Assets/_Project/Data/Interior/IT_EvoResearchInstitute.asset");
            if (template == null)
                Assert.Ignore("템플릿 없음 (Build Evolved Templates 먼저)");
            var c = ResearchMaze.Center;
            float outer = ResearchMaze.Rings[0] + ResearchMaze.Wall / 2f, inner = ResearchMaze.Rings[2] - ResearchMaze.Wall / 2f;
            Assert.Greater(template.ResidentSpots.Count, 4);
            foreach (var spot in template.ResidentSpots)
            {
                float d = Mathf.Max(Mathf.Abs(spot.Position.x - c.x), Mathf.Abs(spot.Position.z - c.z));
                Assert.IsTrue(d > outer + 0.3f || d < inner - 0.3f, $"미로 길 안 주민 자리 {spot.Position}");
            }
        }

        /// <summary>프리팹 몸체 · 유리 · 벤치 충돌체로 0.2 m 격자를 걸어 봄 (몸 반지름 0.28): 입구 · 해치 넷에서 연구실 가운데까지 닿고, 해치 뚜껑 · 길잡이 점이 벽에 묻히지 않음.</summary>
        [Test]
        public void Walk_ReachesLab_FromEntranceAndHatches()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            var root = Object.Instantiate(prefab);
            try
            {
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var col in root.GetComponentsInChildren<Collider>(true))
                    col.enabled = col is MeshCollider && (col.name.EndsWith("_Shell") || col.name.EndsWith("_Glass") || col.name.EndsWith("_Soft"));
                Physics.SyncTransforms();
                const float F = ResearchMaze.Floor, step = 0.2f, x0 = -3.8f, z0 = -3.8f;
                int n = Mathf.RoundToInt(15.6f / step) + 1;
                bool Free(float x, float z) => !Physics.CheckCapsule(new Vector3(x, F + 0.33f, z), new Vector3(x, F + 1.5f, z), 0.28f);
                var free = new bool[n, n];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        free[i, j] = Free(x0 + i * step, z0 + j * step);
                (int, int) Cell(float x, float z) => (Mathf.RoundToInt((x - x0) / step), Mathf.RoundToInt((z - z0) / step));
                bool Reach(float sx, float sz)
                {
                    var (si, sj) = Cell(sx, sz);
                    if (!free[si, sj])
                        return false;
                    var seen = new bool[n, n];
                    var q = new Queue<(int, int)>();
                    q.Enqueue((si, sj));
                    seen[si, sj] = true;
                    var c = ResearchMaze.Center;
                    while (q.Count > 0)
                    {
                        var (i, j) = q.Dequeue();
                        float x = x0 + i * step, z = z0 + j * step;
                        if (new Vector2(x - c.x, z - c.z).magnitude < 1.1f)
                            return true;
                        foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int a = i + di, b = j + dj;
                            if (a < 0 || b < 0 || a >= n || b >= n || seen[a, b] || !free[a, b])
                                continue;
                            seen[a, b] = true;
                            q.Enqueue((a, b));
                        }
                    }
                    return false;
                }
                Assert.IsTrue(Reach(4f, -3.4f), "입구에서 연구실까지 막힘");
                foreach (var (cell, off) in ResearchMaze.Hatches)
                {
                    float hx = cell.x * 8f + off.x, hz = cell.z * 8f + off.y;
                    Assert.IsTrue(Reach(hx, hz), $"해치 {cell}에서 연구실까지 막힘");
                    Assert.IsFalse(Physics.CheckBox(new Vector3(hx, F + 0.06f, hz), new Vector3(0.7f, 0.04f, 0.7f)), $"해치 {cell} 뚜껑이 벽에 묻힘");
                }
                // 길잡이 길 (0.25 m마다, 몸 반지름 0.22) — 연구실 문까지
                var route = ResearchMaze.Route;
                for (int k = 1; k < route.Length; k++)
                {
                    float L = Vector2.Distance(route[k - 1], route[k]);
                    for (float t = 0f; t <= L; t += 0.25f)
                    {
                        var p = Vector2.Lerp(route[k - 1], route[k], t / L);
                        Assert.IsFalse(Physics.CheckCapsule(new Vector3(p.x, F + 0.3f, p.y), new Vector3(p.x, F + 1.5f, p.y), 0.22f), $"길잡이 길이 벽에 닿음 구간 {k - 1} ({p.x:F2}, {p.y:F2})");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
