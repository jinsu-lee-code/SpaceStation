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
                // 주민 자리 = 모두 걸어서 닿음 (2026-11-12 궤도 방어망 지적을 앞 모듈에도 — 정자 고리 의자 (0.45 m)가 한 바퀴를 막아 가운데 주민 자리에 못 들어갔음):
                //  땅 = 정문 스폰에서, 하늘 정원 = 승강기 위 문 앞에서
                var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>("Assets/_Project/Data/Interior/IT_EvoBiosphere.asset");
                Assert.IsNotNull(template);
                foreach (var spot in template.ResidentSpots)
                {
                    var seenAt = spot.Position.y > Biosphere.SkyTop - 1f ? sky : ground;
                    bool sit = spot.Pose == SpaceStation.Data.ResidentPose.Sit;
                    bool okSpot = false;
                    foreach (var d in new[] { Vector3.zero, Vector3.forward * 0.5f, Vector3.back * 0.5f, Vector3.left * 0.5f, Vector3.right * 0.5f })
                    {
                        var sp = spot.Position + d;
                        if (sit)
                            sp.y -= 0.45f;
                        int k = Node(sp);
                        if (k >= 0 && seenAt[k] && Mathf.Abs(nodes[k].y - sp.y) < 0.6f)
                        {
                            okSpot = true;
                            break;
                        }
                    }
                    Assert.IsTrue(okSpot, $"주민 자리 {spot.Position}: 걸어서 못 감");
                }
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

        /// <summary>물꼬 맞추기 규칙: 쉬는 동안 호수까지 · 시작 = 모두 닫힘 · 바퀴 = 한 단씩 (5단 다음 닫힘) · 판마다 목마른 밭 1 → 2 → 3곳 (물길마다 하나, 지금 물꼬와 다른 단) · 세 판 뒤 끝.</summary>
        [Test]
        public void SluicePuzzle_RoundsAndTurns()
        {
            var p = new SluicePuzzle();
            for (int s = 0; s < 3; s++)
                Assert.AreEqual(SluicePuzzle.Lake, p.Gate(s));
            Assert.IsFalse(p.Running);
            for (int seed = 0; seed < 40; seed++)
            {
                p.Start(seed);
                Assert.IsTrue(p.Running);
                for (int round = 0; round < SluicePuzzle.Rounds; round++)
                {
                    Assert.AreEqual(round, p.Round);
                    Assert.AreEqual(round + 1, p.Thirsty.Count, $"seed {seed} 판 {round}");
                    var streams = new HashSet<int>();
                    foreach (var t in p.Thirsty)
                    {
                        Assert.IsTrue(streams.Add(t.Stream), "목마른 밭은 물길마다 하나");
                        Assert.That(t.Level, Is.InRange(1, SluicePuzzle.Levels));
                        Assert.AreNotEqual(p.Gate(t.Stream), t.Level, "이미 맞아 있는 판");
                    }
                    Assert.IsFalse(p.Solved);
                    // 바퀴를 맞춤 (한 단씩 돌려서)
                    foreach (var t in p.Thirsty)
                    {
                        int guard = 0;
                        while (p.Gate(t.Stream) != t.Level && guard++ < 10)
                            p.Turn(t.Stream);
                        Assert.IsTrue(p.Waters(t.Stream, t.Level));
                    }
                    Assert.IsTrue(p.Solved);
                    bool end = p.Advance();
                    Assert.AreEqual(round == SluicePuzzle.Rounds - 1, end);
                }
                Assert.IsTrue(p.Finished);
                Assert.IsFalse(p.Running);
            }
            // 바퀴 한 바퀴: 닫힘 → 1 … 5 → 닫힘
            p.Start(3);
            int g = p.Gate(0);
            Assert.AreEqual(0, g);
            for (int k = 1; k <= 6; k++)
                Assert.AreEqual(k % 6, p.Turn(0));
            p.Reset();
            Assert.AreEqual(SluicePuzzle.Lake, p.Gate(1));
        }

        /// <summary>
        /// 체험 연결 (빌더): 수문 바퀴 셋 (피벗 = 바퀴 가운데 · 시선 공) · 문판 · 물길 물 / 폭포 조각 18 + 18 · 밭 15 (피벗 높이 = 단 윗면, 작물 메시 있음) · 물레방아 셋 ·
        /// 물보라 번호가 물보라 배열 안 · 능선 길에서 바퀴에 시선이 닿음 (2.6 m 안, 몸체에 안 가림) · 반딧불이 선반이 고리 산책길에서 보이고 숲 자리가 방 안.
        /// </summary>
        [Test]
        public void Experiences_Wired()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var gates = root.GetComponent<InteriorSluiceGates>();
                Assert.IsNotNull(gates, "InteriorSluiceGates 없음");
                var so = new SerializedObject(gates);
                Transform Ref(string field, int i) => so.FindProperty(field).GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                Object Obj(string field, int i) => so.FindProperty(field).GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.AreEqual(3, so.FindProperty("_wheels").arraySize);
                Assert.AreEqual(18, so.FindProperty("_chans").arraySize);
                Assert.AreEqual(18, so.FindProperty("_spills").arraySize);
                Assert.AreEqual(15, so.FindProperty("_crops").arraySize);
                for (int i = 0; i < 18; i++)
                {
                    Assert.IsNotNull(Obj("_chans", i), $"물길 물 {i / 6}_{i % 6}");
                    Assert.IsNotNull(Obj("_spills", i), $"폭포 {i / 6}_{i % 6}");
                }
                var fx = root.GetComponent<InteriorWaterfallFx>();
                int landings = new SerializedObject(fx).FindProperty("_landings").arraySize;
                for (int s = 0; s < 3; s++)
                {
                    for (int n = 0; n <= 5; n++)
                        foreach (int idx in Biosphere.SpillLandings(s, n))
                            Assert.That(idx, Is.InRange(0, landings - 1));
                    for (int n = 1; n <= 5; n++)
                    {
                        var crop = Ref("_crops", s * 5 + n - 1);
                        Assert.IsNotNull(crop, $"밭 {s}_{n}");
                        Assert.AreEqual(Biosphere.TerraceTop(Biosphere.TerraceOfLevel(n)), crop.localPosition.y, 1e-4f);
                        var mf = crop.GetComponentInChildren<MeshFilter>();
                        Assert.IsTrue(mf != null && mf.sharedMesh != null && mf.sharedMesh.vertexCount > 100, $"밭 작물 메시 {s}_{n}");
                    }
                    var wheel = Ref("_wheels", s);
                    Assert.IsNotNull(wheel);
                    Assert.Less((wheel.localPosition - Biosphere.GateWheelHub(s)).magnitude, 1e-3f);
                    var ball = wheel.GetComponent<SphereCollider>();
                    Assert.IsNotNull(ball, "바퀴 시선 공");
                    Assert.IsNotNull(Ref("_plates", s), "문판");
                    Assert.IsNotNull(so.FindProperty("_waterWheels").GetArrayElementAtIndex(s).objectReferenceValue, "물레방아");
                    // 능선 길 (바깥 가장자리 0.3 m 안)에 선 눈 (발 + 1.6)에서 바퀴로: 2.6 m 안, 몸체에 가리지 않음
                    ball.enabled = true;
                    Physics.SyncTransforms();
                    var eye = Biosphere.Polar(Biosphere.RidgeOuter - 0.35f, Biosphere.Streams[s] + 4f, Biosphere.RidgeTop + 1.6f);
                    var hub = Biosphere.GateWheelHub(s);
                    Assert.Less((hub - eye).magnitude, 2.4f);
                    Assert.IsTrue(Physics.Raycast(eye, (hub - eye).normalized, out var hit, 2.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "바퀴에 시선이 안 닿음");
                    Assert.AreEqual(ball, hit.collider, $"바퀴 앞을 {hit.collider.name}이 가림");
                    ball.enabled = false;
                }
                var catcher = root.GetComponent<InteriorFireflyCatch>();
                Assert.IsNotNull(catcher, "InteriorFireflyCatch 없음");
                var cso = new SerializedObject(catcher);
                var rack = cso.FindProperty("_rack").objectReferenceValue as BoxCollider;
                Assert.IsNotNull(rack, "유리병 선반 충돌체");
                Assert.IsNotNull(cso.FindProperty("_glowMaterial").objectReferenceValue, "반딧불이 빛 재질");
                var zone = cso.FindProperty("_zoneCenter").vector3Value;
                float zr = cso.FindProperty("_zoneRadius").floatValue;
                Assert.Less(Mathf.Max(zone.x, zone.z) + zr, 19.6f, "반딧불이 숲이 하늘 면 밖");
                Assert.Greater(new Vector2(zone.x - Biosphere.Hub.x, zone.z - Biosphere.Hub.z).magnitude - zr, Biosphere.WallOuter, "반딧불이 숲이 옹벽 안");
                // 고리 산책길 모서리 (선반 바깥 1.3 m)에 선 눈에서 선반으로 시선이 닿음
                rack.enabled = true;
                Physics.SyncTransforms();
                float a = Biosphere.FireflyAngle * Mathf.Deg2Rad;
                var u = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var rackAt = rack.transform.position;
                var from = rackAt + u * 1.3f + Vector3.up * (F + 1.6f - rackAt.y);
                Assert.IsTrue(Physics.Raycast(from, (rackAt - from).normalized, out var rh, 2.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "선반에 시선이 안 닿음");
                Assert.AreEqual(rack, rh.collider, $"선반 앞을 {rh.collider.name}이 가림");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 오리 가족 (Biosphere.DuckSpots): 어미 = 헤엄 (고리 크기가 골라져 움직임) · 새끼 = 바로 앞 어미를 따라감, 모두 보는 쪽 = 가족 보는 방향.
        /// 2분 동안 움직여 봐도 물 안 (둑 · 폭포 기둥 · 선착장 밖), 수면 위 물체 (연잎 · 갈대 · 바위) 꼭짓점이 몸 반지름 0.6배 안에 안 들고, 새끼가 어미 뒤 줄에서 안 떨어짐.
        /// </summary>
        [Test]
        public void DuckFamilies_SwimInWater()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var holder = root.transform.Find("Critters_Duck");
                Assert.IsNotNull(holder, "Critters_Duck 없음");
                var fx = holder.GetComponent<SpaceStation.Building.CritterFx>();
                var spots = Biosphere.DuckSpots();
                Assert.AreEqual(spots.Length, fx.Count, "오리 수");
                var so = new SerializedObject(fx);
                var arr = so.FindProperty("_critters");
                float scale = holder.localScale.x;
                var parts = new Transform[spots.Length];
                var radius = new float[spots.Length];
                var leader = new int[spots.Length];
                var famOf = new int[spots.Length];
                for (int k = 0, idx = 0; k < Biosphere.DuckFamilies.Length; k++)
                    for (int j = 0; j <= Biosphere.DuckFamilies[k].Kids; j++)
                        famOf[idx++] = k;
                int mother = -1;
                for (int i = 0; i < spots.Length; i++)
                {
                    var e = arr.GetArrayElementAtIndex(i);
                    parts[i] = e.FindPropertyRelative("Part").objectReferenceValue as Transform;
                    Assert.IsNotNull(parts[i], $"오리 {i} 모델");
                    radius[i] = e.FindPropertyRelative("Radius").floatValue * scale;
                    leader[i] = e.FindPropertyRelative("Leader").intValue;
                    var kind = (SpaceStation.Building.CritterFx.Kind)e.FindPropertyRelative("Kind").enumValueIndex;
                    if (spots[i].Kind == "Duck")
                    {
                        mother = i;
                        Assert.AreEqual(SpaceStation.Building.CritterFx.Kind.Swim, kind, $"어미 {i}");
                        Assert.AreNotEqual(0f, e.FindPropertyRelative("Side").floatValue, $"어미 {i} 헤엄 고리가 다 막혀 제자리");
                    }
                    else
                    {
                        Assert.AreEqual(SpaceStation.Building.CritterFx.Kind.Follow, kind, $"새끼 {i}");
                        Assert.AreEqual(mother, leader[i], $"새끼 {i}가 따라가는 어미");
                    }
                    // 보는 쪽 = 가족 보는 방향 (모델 앞 · y 회전 부호 확인 — 머리 = 위쪽 꼭짓점이 몰린 쪽)
                    float h = Biosphere.DuckFamilies[famOf[i]].Heading * Mathf.Deg2Rad;
                    var heading = e.FindPropertyRelative("Heading").vector3Value;
                    Assert.Greater(Vector3.Dot(heading, new Vector3(Mathf.Cos(h), 0f, Mathf.Sin(h))), 0.9f, $"오리 {i} 보는 쪽이 다름 {heading}");
                }
                // 물 위 물체 꼭짓점 (수면 − 2 cm ~ + 1.5 m, 호수 둘레 3.6 m 안 — 연잎은 수면 − 1 ~ + 0.5 cm, 수면 물체 · 무지개 띠는 빼고, 큰 판 선착장은 아래에서 따로)
                var above = new List<Vector3>();
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.name.StartsWith("Critter_") || mf.name.EndsWith("Rainbow") || mf.name.Contains("_Water"))
                        continue;
                    var m = mf.transform.localToWorldMatrix;
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        var p = m.MultiplyPoint3x4(v);
                        if (new Vector2(p.x - Biosphere.Hub.x, p.z - Biosphere.Hub.z).magnitude > Biosphere.ShoreRadius)
                            continue;
                        foreach (int lake in new[] { 0, 2 })
                            if (p.y > Biosphere.WaterLevels[lake] - 0.02f && p.y < Biosphere.WaterLevels[lake] + 1.5f)
                                above.Add(p);
                    }
                }
                float pa = Biosphere.PierAngle * Mathf.Deg2Rad;
                var rd = new Vector3(Mathf.Cos(pa), 0f, Mathf.Sin(pa));
                var td = new Vector3(-rd.z, 0f, rd.x);
                var pier = Biosphere.Polar(2.45f, Biosphere.PierAngle, 0f);
                var start = new Vector3[spots.Length];
                var moved = new float[spots.Length];
                for (int i = 0; i < spots.Length; i++)
                    start[i] = parts[i].localPosition * scale;
                fx.Play();
                for (int step = 1; step <= 2400; step++)
                {
                    fx.Step(0.05f, step * 0.05f);
                    for (int i = 0; i < spots.Length; i++)
                    {
                        var p = parts[i].localPosition * scale;
                        moved[i] = Mathf.Max(moved[i], new Vector2(p.x - start[i].x, p.z - start[i].z).magnitude);
                        bool sky = Mathf.Abs(p.y - Biosphere.WaterLevels[2]) < 0.1f;
                        Assert.IsTrue(sky || Mathf.Abs(p.y - Biosphere.WaterLevels[0]) < 0.05f, $"오리 {i} 높이 {p.y:0.000}");
                        float d = new Vector2(p.x - Biosphere.Hub.x, p.z - Biosphere.Hub.z).magnitude;
                        float rim = sky ? Biosphere.SkyWater - 0.02f : Biosphere.LakeRadius - 0.12f;
                        Assert.Less(d + radius[i] * 0.6f, rim, $"오리 {i}가 둑에 닿음 ({step * 0.05f:0.0}초, 가운데에서 {d:0.00})");
                        if (!sky)
                        {
                            Assert.Greater(d, Biosphere.FallsKeepOut - 0.1f, $"오리 {i}가 폭포 기둥 밑 ({step * 0.05f:0.0}초)");
                            var q = p - pier;
                            Assert.IsFalse(Mathf.Abs(Vector3.Dot(q, rd)) < 1.15f + radius[i] * 0.6f && Mathf.Abs(Vector3.Dot(q, td)) < 1.2f + radius[i] * 0.6f,
                                $"오리 {i}가 선착장 밑 ({step * 0.05f:0.0}초)");
                        }
                        if (step % 10 == 0)
                        {
                            float r2 = radius[i] * 0.6f * radius[i] * 0.6f;
                            foreach (var v in above)
                                if (v.y > p.y - 0.02f && v.y < p.y + 1.5f && (v.x - p.x) * (v.x - p.x) + (v.z - p.z) * (v.z - p.z) < r2)
                                    Assert.Fail($"오리 {i}가 물 위 물체에 닿음 ({step * 0.05f:0.0}초, {v})");
                        }
                        if (leader[i] >= 0)
                        {
                            var lp = parts[leader[i]].localPosition * scale;
                            Assert.Less(new Vector2(p.x - lp.x, p.z - lp.z).magnitude, Biosphere.DuckLead + Biosphere.DuckSpacing * 3f + 0.3f, $"새끼 {i}가 어미에게서 떨어짐");
                        }
                    }
                }
                for (int i = 0; i < spots.Length; i++)
                    Assert.Greater(moved[i], 0.4f, $"오리 {i}가 거의 안 움직임");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
