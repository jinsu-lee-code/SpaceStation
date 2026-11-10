using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 진화 3단계 바이오스피어 (Blender bio3_builder와 같은 값): 연결점 (옆 문 넷 · 아래 해치 아홉 · 위 해치) · 걸어서 닿음 (계단 밭 · 능선 · 선착장 · 모서리 · 승강기 앞) ·
    /// 해치 뚜껑이 묻히지 않음 · 하늘 정원 (승강기 위 문 앞 → 정원 길 · 위 해치 밑) · 주민 자리 발밑.
    /// </summary>
    public class BiosphereTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interior/PF_Interior_EvoBiosphere.prefab";
        private const float F = Biosphere.Floor;

        [Test]
        public void Sockets_MatchExterior()
        {
            // 외형 MD_Evo_Biosphere 연결점: 바닥 칸 아홉 아래 · 1층 옆면 가운데 넷 · 2층 가운데 위
            Assert.AreEqual(9, Biosphere.Hatches.Length);
            var cells = new HashSet<Vector3Int>();
            foreach (var (cell, off) in Biosphere.Hatches)
            {
                Assert.IsTrue(cells.Add(cell), $"해치 칸 겹침 {cell}");
                Assert.AreEqual(0, cell.y);
                Assert.Less(Mathf.Abs(off.x), 3.05f, $"{cell}");
                Assert.Less(Mathf.Abs(off.y), 3.05f, $"{cell}");
            }
            Assert.AreEqual(4, Biosphere.Doors.Length);
            foreach (var (cell, dir) in Biosphere.Doors)
            {
                Assert.AreEqual(0, cell.y);
                Assert.AreEqual(1, Mathf.Abs(dir.x) + Mathf.Abs(dir.z));
                Assert.AreEqual(1, dir.x != 0 ? cell.z : cell.x, $"문 = 옆면 가운데 칸 {cell}");
            }
            Assert.AreEqual(new Vector3Int(1, 1, 1), Biosphere.UpHatchCell);
            Assert.AreEqual(2, Biosphere.LiftStops.Length);
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
                col.enabled = col is MeshCollider ? col.name.EndsWith("_Shell") || col.name.EndsWith("_Glass") || col.name.EndsWith("_LakeHit") : false;
            Physics.SyncTransforms();
            ok = true;
            return root;
        }

        /// <summary>
        /// 0.2 m 격자 기둥마다 아래로 쏜 모든 바닥 (높이가 다른 바닥 = 층) 중 몸 (반지름 0.28 · 발 위 0.5 ~ 1.6)이 들어가는 곳을 마디로,
        /// 이웃 마디 높이 차 0.36 이하면 이어 걸음. 정문 스폰에서: 해치 아홉 · 옆 문 넷 앞 · 능선 넷 · 계단 밭 다섯 단 · 물가 길 · 선착장 · 네 모서리 · 승강기 앞.
        /// 하늘 정원은 따로 (승강기 위 문 앞에서): 정원 길 둘레 · 위 해치 밑 · 아치 다리 셋.
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
                int n = Mathf.RoundToInt(23.8f / step) + 1;
                var nodes = new List<Vector3>();
                var cols = new List<int>[n, n];
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        var origin = new Vector3(x0 + i * step, top, z0 + j * step);
                        for (int guard = 0; guard < 12 && Physics.Raycast(origin, Vector3.down, out var hit, origin.y - F + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore); guard++)
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
                            if (a < 0 || b < 0 || a >= n || b >= n || cols[a, b] == null)
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
                bool[] Flood(Vector3 from, string what)
                {
                    var seen = new bool[nodes.Count];
                    int start = Node(from);
                    Assert.GreaterOrEqual(start, 0, $"{what}: 설 곳 없음 {from}");
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
                            if (a < 0 || b < 0 || a >= n || b >= n || cols[a, b] == null)
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
                    return seen;
                }
                var ground = Flood(new Vector3(8f, F, -3.0f), "정문 스폰");
                // 호수 · 연못 바닥에 못 내려감 (사용자 "중앙 호수 들어가면 나오지도 못함" — LakeHit 벽)
                for (int k = 0; k < nodes.Count; k++)
                {
                    if (!ground[k])
                        continue;
                    var p = nodes[k];
                    Assert.IsFalse(p.y < F - 0.25f, $"물 바닥까지 걸어 내려감 {p}");
                }
                void Reach(bool[] seen, Vector3 p, string what)
                {
                    int k = Node(p);
                    Assert.GreaterOrEqual(k, 0, $"{what}: 설 곳 없음 {p}");
                    Assert.IsTrue(seen[k], $"{what}: 걸어서 못 감 {p}");
                }
                foreach (var (cell, off) in Biosphere.Hatches)
                {
                    var h = new Vector3(cell.x * 8f + off.x, F, cell.z * 8f + off.y);
                    Reach(ground, h, $"해치 {cell}");
                    Assert.IsFalse(Physics.CheckBox(h + Vector3.up * 0.06f, new Vector3(0.7f, 0.04f, 0.7f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"해치 {cell} 뚜껑이 묻힘");
                }
                foreach (var (cell, dir) in Biosphere.Doors)
                    Reach(ground, new Vector3(cell.x * 8f, F, cell.z * 8f) + (Vector3)dir * (Biosphere.DoorDepth - 0.5f), $"문 {cell} {dir}");
                foreach (float a in new[] { 0f, 90f, 180f, 270f, 45f, 225f })
                    Reach(ground, Biosphere.Polar((Biosphere.RidgeInner + Biosphere.RidgeOuter) / 2f, a, Biosphere.RidgeTop), $"능선 {a}°");
                for (int k = 0; k < 5; k++)
                    Reach(ground, Biosphere.Polar(Biosphere.TerraceInner(k) + 0.4f, 270f, Biosphere.TerraceTop(k) + 0.02f), $"계단 밭 {k + 1}단 (앞 계단 참)");
                foreach (float a in new[] { 300f, 120f, 30f })
                    Reach(ground, Biosphere.Polar(3.2f, a, F), $"물가 길 {a}°");
                Reach(ground, Biosphere.Polar(1.8f, Biosphere.PierAngle, F), "선착장 끝");
                foreach (var (r, a) in new[] { (13.8f, 40f), (12.6f, 135f), (12.6f, 225f), (11.7f, 315f) })   // 반딧불이 숲 탁자 · 연못 비킴
                    Reach(ground, Biosphere.Polar(r, a, F), $"모서리 {a}°");
                var liftFront = Biosphere.Polar(Biosphere.LegRadius - Biosphere.LegThickness - 0.5f, Biosphere.LiftAngle, Biosphere.RidgeTop);
                Reach(ground, liftFront, "승강기 앞 (능선)");
                // 하늘 정원: 승강기 위 문 앞 (아치 다리)에서
                var sky = Flood(Biosphere.Polar(Biosphere.LegRadius - Biosphere.LegThickness - 0.6f, Biosphere.LiftAngle, Biosphere.SkyTop), "승강기 위 문 앞");
                foreach (float a in new[] { 0f, 90f, 180f, 270f, 210f })
                    Reach(sky, Biosphere.Polar(2.8f, a, Biosphere.SkyTop), $"하늘 정원 길 {a}°");
                foreach (float a in Biosphere.Arches)
                    Reach(sky, Biosphere.Polar(6.5f, a, Biosphere.SkyTop), $"아치 다리 {a}°");
                var up = Biosphere.Polar(Biosphere.UpHatchOffset.z - 0.25f, 90f, Biosphere.SkyTop);
                Reach(sky, up, "위 해치 밑");
                Assert.Less(Biosphere.UpHatchCell.y * 8f + Biosphere.UpHatchDepth - (Biosphere.SkyTop + 1.6f), 2.6f, "위 해치가 눈에서 손 닿는 거리 (해치 4 m)");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 승강기 칸 안에서 서는 곳마다 문으로 걸어 나감 (사용자 2026-11-11 "엘리베이터 2층 문에서 막혀서 못 나감" — 아치 위 곡선 관이 문 위 머리 높이를 막았는데,
        /// 걷기 검사가 문 바깥에서 시작해 못 잡았음, 체크리스트 29 "문 · 엘리베이터는 안쪽까지 지나가 봄"). 칸 · 칸 유리 충돌체를 켜고 칸을 그 층으로 옮김, 문짝은 열림 (충돌체 끔).
        /// </summary>
        [Test]
        public void Walk_LiftExitsAtEveryStop()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var car = root.transform.Find("INT_EvoBiosphere_LiftCar");
                Assert.IsNotNull(car, "승강기 칸 없음");
                foreach (var col in car.GetComponentsInChildren<MeshCollider>(true))
                    col.enabled = true;
                var basePos = car.localPosition;
                for (int k = 0; k < Biosphere.LiftStops.Length; k++)
                {
                    float stop = Biosphere.LiftStops[k];
                    car.localPosition = basePos + Vector3.up * (stop - Biosphere.LiftStops[0]);
                    Physics.SyncTransforms();
                    var c = Biosphere.LiftCenter;
                    var inside = new Vector3(c.x, stop + 0.085f, c.z);
                    var outside = Biosphere.Polar(Biosphere.LegRadius - Biosphere.LegThickness - 0.7f, Biosphere.LiftAngle, stop);
                    // 칸 가운데 → 문 → 바깥까지 0.05 m 간격으로 몸 (반지름 0.28 · 발 위 0.3 ~ 1.83 = 실제 키)이 지나가는지
                    for (int t = 0; t <= 40; t++)
                    {
                        var p = Vector3.Lerp(inside, outside, t / 40f);
                        // 발밑 = 작은 공 (반지름 0.12)으로 — 칸 바닥 끝 ↔ 문턱 1.5 cm 틈 (몸은 안 빠짐)으로 가는 광선이 빠졌음
                        Assert.IsTrue(Physics.SphereCast(p + Vector3.up * 0.6f, 0.12f, Vector3.down, out var hit, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"{k + 1}층: 발밑 바닥 없음 {p}");
                        var f = hit.point;
                        Assert.Less(Mathf.Abs(f.y - p.y), 0.2f, $"{k + 1}층: 문턱 높이 {f.y - p.y:F2}");
                        Assert.IsFalse(Physics.CheckCapsule(f + Vector3.up * 0.58f, f + Vector3.up * 1.55f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                            $"{k + 1}층: 칸에서 나가는 길이 막힘 {f} (가운데에서 {t * 2.5f:F0}%)");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>주민 자리 발밑에 바닥 (± 0.12)이 있고 몸 (반지름 0.24)이 들어감.</summary>
        [Test]
        public void ResidentSpots_StandOnFloor()
        {
            var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>("Assets/_Project/Data/Interior/IT_EvoBiosphere.asset");
            var root = Spawn(out bool ok);
            if (!ok || template == null)
            {
                if (root != null)
                    Object.DestroyImmediate(root);
                Assert.Ignore("템플릿 없음 (Build Evolved Templates 먼저)");
            }
            try
            {
                Assert.Greater(template.ResidentSpots.Count, 12);
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
    }
}
