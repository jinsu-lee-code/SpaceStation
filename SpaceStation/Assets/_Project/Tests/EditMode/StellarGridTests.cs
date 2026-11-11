using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 진화 3단계 항성 발전망 (Blender sg3_builder와 같은 값): 연결점 (외형 연결점 면과 같음 — 옆 문 둘 · 아래 해치 열넷 · 위 해치) ·
    /// 걸어서 닿음 (해치 · 문 · 정자 안 · 벌집 홀 · 굴 · 전망대 · 꿀 샘 · 라벤더 · 꽃나무 숲, 연못 바닥 못 내려감) · 해치 뚜껑이 묻히지 않음 · 주민 자리 발밑 ·
    /// 꿀벌 길이 몸체 · 잎 · 꽃송이에 안 닿음.
    /// </summary>
    public class StellarGridTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interior/PF_Interior_EvoStellarGrid.prefab";
        private const float F = StellarGrid.Floor;

        [Test]
        public void Sockets_MatchExterior()
        {
            // 외형 MD_Evo_StellarGrid 연결점 (면 깊이 ≥ 0인 바깥 면): 바닥 칸 열넷 아래 · 꿀 언덕 칸 앞뒤 · 칸 (3, 0, 1) 위
            var module = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.ModuleData>("Assets/_Project/Data/Modules/Evolved/MD_Evo_StellarGrid.asset");
            Assert.IsNotNull(module);
            var cells = new HashSet<Vector3Int>(module.CellOffsets);
            var expected = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var c in module.CellOffsets)
                foreach (var d in new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) })
                    if (!cells.Contains(c + d) && module.TryGetFaceDepth(c, d, out _) && !module.IsClosedFace(c, d))
                        expected.Add((c, d));
            var actual = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var (cell, off) in StellarGrid.Hatches)
            {
                Assert.IsTrue(actual.Add((cell, Vector3Int.down)), $"해치 칸 겹침 {cell}");
                Assert.Less(Mathf.Abs(off.x), 3.05f, $"{cell}");
                Assert.Less(Mathf.Abs(off.y), 3.05f, $"{cell}");
            }
            foreach (var (cell, dir) in StellarGrid.Doors)
                Assert.IsTrue(actual.Add((cell, dir)));
            Assert.IsTrue(actual.Add((StellarGrid.UpHatchCell, Vector3Int.up)));
            Assert.IsTrue(expected.SetEquals(actual), $"외형 연결점 {expected.Count}곳 ↔ 내부 {actual.Count}곳");
        }

        private static GameObject Spawn(out bool ok)
        {
            ok = false;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                return null;
            var root = Object.Instantiate(prefab);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
                col.enabled = col is MeshCollider ? col.name.EndsWith("_Shell") || col.name.EndsWith("_LakeHit") : false;
            Physics.SyncTransforms();
            ok = true;
            return root;
        }

        /// <summary>
        /// 0.2 m 격자 기둥마다 아래로 쏜 모든 바닥 (층) 중 몸 (반지름 0.28 · 발 위 0.5 ~ 1.6)이 들어가는 곳을 마디로, 이웃 마디 높이 차 0.36 이하면 이어 걸음.
        /// 스폰 (앞 문 안)에서: 해치 열넷 · 옆 문 둘 · 정자 안 (꿀 샘 둘레 · 벤치 앞 · 그네 앞) · 벌집 홀 · 굴 셋 · 전망대 원판 · 꿀 샘 무대 · 라벤더 · 꽃나무 숲 · 연못가.
        /// </summary>
        [Test]
        public void Walk_ReachesEverything()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                const float step = 0.2f, x0 = -3.9f, z0 = -3.9f, top = 12f;
                int nx = Mathf.RoundToInt(55.8f / step) + 1, nz = Mathf.RoundToInt(15.8f / step) + 1;
                var nodes = new List<Vector3>();
                var cols = new List<int>[nx, nz];
                for (int i = 0; i < nx; i++)
                {
                    for (int j = 0; j < nz; j++)
                    {
                        var origin = new Vector3(x0 + i * step, top, z0 + j * step);
                        for (int guard = 0; guard < 16 && Physics.Raycast(origin, Vector3.down, out var hit, origin.y - F + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore); guard++)
                        {
                            origin = hit.point + Vector3.down * 0.01f;
                            if (hit.normal.y < 0.7f)
                                continue;
                            var p = hit.point;
                            if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                                continue;
                            if (cols[i, j] == null)
                                cols[i, j] = new List<int>();
                            cols[i, j].Add(nodes.Count);
                            nodes.Add(p);
                        }
                    }
                }
                int Node(Vector3 p)
                {
                    int i = Mathf.RoundToInt((p.x - x0) / step), j = Mathf.RoundToInt((p.z - z0) / step);
                    int best = -1;
                    float bd = 0.5f;
                    for (int di = -2; di <= 2; di++)
                        for (int dj = -2; dj <= 2; dj++)
                        {
                            int a = i + di, b = j + dj;
                            if (a < 0 || b < 0 || a >= nx || b >= nz || cols[a, b] == null)
                                continue;
                            foreach (int k in cols[a, b])
                            {
                                float d = Mathf.Abs(nodes[k].y - p.y) + new Vector2(nodes[k].x - p.x, nodes[k].z - p.z).magnitude * 0.1f;
                                if (d < bd)
                                {
                                    bd = d;
                                    best = k;
                                }
                            }
                        }
                    return best;
                }
                var start = Node(new Vector3(38.4f, F, -2.8f));
                Assert.GreaterOrEqual(start, 0, "스폰 자리 없음");
                var seen = new bool[nodes.Count];
                var q = new Queue<int>();
                q.Enqueue(start);
                seen[start] = true;
                while (q.Count > 0)
                {
                    int k = q.Dequeue();
                    var p = nodes[k];
                    int i = Mathf.RoundToInt((p.x - x0) / step), j = Mathf.RoundToInt((p.z - z0) / step);
                    foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int a = i + di, b = j + dj;
                        if (a < 0 || b < 0 || a >= nx || b >= nz || cols[a, b] == null)
                            continue;
                        foreach (int m in cols[a, b])
                        {
                            if (seen[m] || Mathf.Abs(nodes[m].y - p.y) > 0.36f)
                                continue;
                            seen[m] = true;
                            q.Enqueue(m);
                        }
                    }
                }
                for (int k = 0; k < nodes.Count; k++)
                    if (seen[k])
                        Assert.IsFalse(nodes[k].y < F - 0.25f, $"연못 바닥까지 걸어 내려감 {nodes[k]}");
                void Reach(Vector3 p, string what)
                {
                    int k = Node(p);
                    Assert.GreaterOrEqual(k, 0, $"{what}: 설 곳 없음 {p}");
                    Assert.IsTrue(seen[k], $"{what}: 걸어서 못 감 {p}");
                }
                foreach (var (cell, off) in StellarGrid.Hatches)
                {
                    var h = new Vector3(cell.x * 8f + off.x, F, cell.z * 8f + off.y);
                    Reach(h, $"해치 {cell}");
                    Assert.IsFalse(Physics.CheckBox(h + Vector3.up * 0.06f + Vector3.up * 0.04f, new Vector3(0.7f, 0.04f, 0.7f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                        $"해치 {cell} 뚜껑이 묻힘");
                }
                foreach (var (cell, dir) in StellarGrid.Doors)
                    Reach(new Vector3(cell.x * 8f, F, cell.z * 8f) + (Vector3)dir * (StellarGrid.DoorDepth - 0.5f), $"문 {cell} {dir}");
                var pc = StellarGrid.PavilionCenter;
                foreach (float a in new[] { 0f, 90f, 180f, 270f })
                    Reach(pc + new Vector3(1.8f * Mathf.Cos(a * Mathf.Deg2Rad), 0f, 1.8f * Mathf.Sin(a * Mathf.Deg2Rad)), $"정자 꿀 샘 둘레 {a}°");
                foreach (float a in new[] { 180f, 300f, 30f })
                    Reach(pc + new Vector3(2.75f * Mathf.Cos(a * Mathf.Deg2Rad), 0f, 2.75f * Mathf.Sin(a * Mathf.Deg2Rad)), $"정자 벤치 앞 {a}°");
                Reach(new Vector3(12f, F, 5.3f), "그네 앞");
                foreach (float a in StellarGrid.Entrances)
                    Reach(pc + new Vector3(7.4f * Mathf.Cos(a * Mathf.Deg2Rad), 0f, 7.4f * Mathf.Sin(a * Mathf.Deg2Rad)), $"정자 입구 밖 {a}°");
                var hall = StellarGrid.HallCenter;
                Reach(hall + new Vector3(-1.6f, 0f, 0f), "벌집 홀 (기둥 왼쪽)");
                Reach(hall + new Vector3(1.6f, 0f, 0f), "벌집 홀 (기둥 오른쪽)");
                Reach(new Vector3(40f, F, -1.2f), "앞 굴");
                Reach(new Vector3(40f, F, 9.6f), "뒤 굴");
                Reach(new Vector3(37.4f, F, StellarGrid.Portal.z), "벌집 입구 굴");
                Reach(new Vector3(35.6f, F, StellarGrid.Portal.z), "벌집 입구 받침 앞");
                Reach(new Vector3(StellarGrid.Lookout.x - 0.4f, StellarGrid.Lookout.y, StellarGrid.Lookout.z - 0.4f), "전망대 원판");
                Reach(new Vector3(32.6f, F, -0.4f), "꿀 샘 무대 앞");
                Reach(new Vector3(47f, F, 0f), "라벤더 이랑 사이");
                Reach(new Vector3(1.2f, F, 2.6f), "꽃나무 숲 안 (앞)");
                Reach(new Vector3(0.9f, F, 6.3f), "꽃나무 숲 안 (뒤)");
                Reach(new Vector3(26.4f, F, 9.9f), "연못가 (뒤 산책길)");
                Reach(new Vector3(22.5f, F, 2.5f), "징검돌 길");
                Assert.Less(StellarGrid.UpHatchCell.y * 8f + StellarGrid.UpHatchDepth - (StellarGrid.Lookout.y + 1.6f), 2.6f, "위 해치가 전망대 눈에서 손 닿는 거리 (해치 4 m)");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>주민 자리 발밑에 바닥 (± 0.12, 앉는 자리 ± 0.5)이 있고 서는 몸 (반지름 0.24)이 들어감.</summary>
        [Test]
        public void ResidentSpots_StandOnFloor()
        {
            var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>("Assets/_Project/Data/Interior/IT_EvoStellarGrid.asset");
            var root = Spawn(out bool ok);
            if (!ok || template == null)
            {
                if (root != null)
                    Object.DestroyImmediate(root);
                Assert.Ignore("템플릿 없음 (Build Evolved Templates 먼저)");
            }
            try
            {
                Assert.Greater(template.ResidentSpots.Count, 15);
                foreach (var spot in template.ResidentSpots)
                {
                    var p = spot.Position;
                    bool sit = spot.Pose == SpaceStation.Data.ResidentPose.Sit;
                    Assert.IsTrue(Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var hit, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"발밑 바닥 없음 {p}");
                    Assert.Less(Mathf.Abs(hit.point.y - p.y), sit ? 0.5f : 0.12f, $"발밑 높이 {p} → {hit.point.y:F2}");
                    if (!sit)
                        Assert.IsFalse(Physics.CheckCapsule(p + Vector3.up * 0.3f, p + Vector3.up * 1.1f, 0.24f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"주민 자리가 묻힘 {p}");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 꿀벌 길 (옆 흔들림 ± 0.22 · 위아래 ± 0.08 포함)과 맴도는 · 쉬는 꿀벌이 몸체 (충돌체)에 안 닿고, 잎 · 꽃송이 · 꽃 (Leaves 꼭짓점)에서 몸 반지름 넘게 떨어짐.
        /// 하늘 면 (바닥 칸 천장 3.95 · 위 칸 11.95) 안.
        /// </summary>
        [Test]
        public void Bees_FlyClear()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                foreach (var col in root.GetComponentsInChildren<Collider>(true))
                    if (col.name.EndsWith("_LakeHit"))
                        col.enabled = false;   // 연못 · 줄기의 보이지 않는 벽은 사람만 막음 (꿀벌은 연못 위로 낮게 남)
                Physics.SyncTransforms();
                var flight = root.GetComponentInChildren<InteriorBeeFlight>(true);
                Assert.IsNotNull(flight, "꿀벌 비행 없음 (SM_InteriorCritters Critter_Bee)");
                var grid = new Dictionary<Vector3Int, List<Vector3>>();
                foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!f.name.EndsWith("_Leaves") || f.sharedMesh == null)
                        continue;
                    var m = f.transform.localToWorldMatrix;
                    foreach (var v in f.sharedMesh.vertices)
                    {
                        var w = m.MultiplyPoint3x4(v);
                        var key = Vector3Int.FloorToInt(w / 0.5f);
                        if (!grid.TryGetValue(key, out var list))
                            grid[key] = list = new List<Vector3>();
                        list.Add(w);
                    }
                }
                float Nearest(Vector3 p)
                {
                    float best = 9f;
                    var c = Vector3Int.FloorToInt(p / 0.5f);
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dz = -1; dz <= 1; dz++)
                                if (grid.TryGetValue(c + new Vector3Int(dx, dy, dz), out var list))
                                    foreach (var w in list)
                                        best = Mathf.Min(best, (w - p).magnitude);
                    return best;
                }
                void Clear(Vector3 p, float body, string what)
                {
                    Assert.IsFalse(Physics.CheckSphere(p, body, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"{what}: 몸체에 닿음 {p}");
                    float d = Nearest(p);
                    Assert.Greater(d, body, $"{what}: 잎 · 꽃에 닿음 {p} ({d:F2} m)");
                    bool tall = (p.x > 4.05f && p.x < 19.95f) || (p.x > 36.05f && p.x < 43.95f);
                    Assert.Less(p.y + body, tall ? 11.95f : 3.95f, $"{what}: 하늘 면 밖 {p}");
                }
                var path = StellarGrid.BeePath;
                for (int k = 0; k < 600; k++)
                {
                    float u = k / 600f;
                    var p = StellarGrid.PathPoint(path, u);
                    var dir = StellarGrid.PathPoint(path, u + 0.004f) - p;
                    var side = Vector3.Cross(Vector3.up, dir.normalized);
                    foreach (float s in new[] { -0.22f, 0f, 0.22f })
                        foreach (float h in new[] { -0.08f, 0.08f })
                            Clear(p + side * s + Vector3.up * h, 0.22f, $"꿀벌 길 {u:F3}");
                }
                foreach (var h in StellarGrid.HoverBees)
                    Clear(h, 0.3f, "맴도는 꿀벌");
                Clear(StellarGrid.GuardBee, 0.22f * StellarGrid.GuardScale, "경비 꿀벌");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
