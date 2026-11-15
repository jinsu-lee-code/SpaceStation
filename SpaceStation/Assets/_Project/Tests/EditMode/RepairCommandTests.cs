using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 진화 3단계 수리 사령부 (Blender rc3_builder와 같은 값): 연결점 (외형 연결점 면과 같음 — 뒤 위 옆문 하나 · 아래 해치 아홉 · 위 해치 둘) ·
    /// 걸어서 닿음 (해치 · 문 · 줄기 속 나선 계단 → 나무집 층계참 · 관제실 · 꼭대기 관측대 · 공방 · 바구니 정류장 · 빈터, 연못 · 샘 바닥 못 내려감) · 해치 뚜껑이 묻히지 않음 · 주민 자리 발밑.
    /// </summary>
    public class RepairCommandTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interior/PF_Interior_EvoRepairCommand.prefab";
        private const string TemplatePath = "Assets/_Project/Data/Interior/IT_EvoRepairCommand.asset";
        private const float F = RepairCommand.Floor;

        [Test]
        public void Sockets_MatchExterior()
        {
            // 외형 MD_Evo_RepairCommand 연결점 (면 깊이 ≥ 0인 바깥 면): 바닥 칸 아홉 아래 · 위 칸 (1, 1, 0) 뒤 · 칸 (1, 1, 0) · (1, 1, 1) 위
            var module = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.ModuleData>("Assets/_Project/Data/Modules/Evolved/MD_Evo_RepairCommand.asset");
            Assert.IsNotNull(module);
            var cells = new HashSet<Vector3Int>(module.CellOffsets);
            var expected = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var c in module.CellOffsets)
                foreach (var d in new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) })
                    if (!cells.Contains(c + d) && module.TryGetFaceDepth(c, d, out _) && !module.IsClosedFace(c, d))
                        expected.Add((c, d));
            var actual = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var (cell, off, floorY) in RepairCommand.Hatches)
            {
                Assert.IsTrue(actual.Add((cell, Vector3Int.down)), $"해치 칸 겹침 {cell}");
                Assert.Less(Mathf.Abs(off.x), 3.05f, $"{cell}");
                Assert.Less(Mathf.Abs(off.y), 3.05f, $"{cell}");
            }
            foreach (var (cell, dir) in RepairCommand.Doors)
                Assert.IsTrue(actual.Add((cell, dir)));
            foreach (var (cell, off) in RepairCommand.UpHatches)
            {
                Assert.IsTrue(actual.Add((cell, Vector3Int.up)), $"위 해치 칸 겹침 {cell}");
                Assert.Less(Mathf.Abs(off.x), 3.05f, $"{cell}");
                Assert.Less(Mathf.Abs(off.y), 3.05f, $"{cell}");
            }
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
                col.enabled = col is MeshCollider ? col.name.EndsWith("_Shell") || col.name.EndsWith("_LakeHit") || col.name.EndsWith("_Glass") : false;
            Physics.SyncTransforms();
            ok = true;
            return root;
        }

        private static Vector3 AroundTree(float r, float deg, float y)
        {
            var t = RepairCommand.TreeAxis;
            return new Vector3(t.x + r * Mathf.Cos(deg * Mathf.Deg2Rad), y, t.z + r * Mathf.Sin(deg * Mathf.Deg2Rad));
        }

        [Test]
        public void Walk_ReachesEverything()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                const float step = 0.2f, x0 = -3.9f, z0 = -3.9f, top = 11.9f;
                int nx = Mathf.RoundToInt(23.8f / step) + 1, nz = Mathf.RoundToInt(23.8f / step) + 1;
                var nodes = new List<Vector3>();
                var cols = new List<int>[nx, nz];
                for (int i = 0; i < nx; i++)
                {
                    for (int j = 0; j < nz; j++)
                    {
                        var origin = new Vector3(x0 + i * step, top, z0 + j * step);
                        for (int guard = 0; guard < 24 && Physics.Raycast(origin, Vector3.down, out var hit, origin.y - F + 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore); guard++)
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
                var start = Node(RepairCommand.Spawn);
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
                // 연못 · 샘 바닥까지 못 내려감 (보이지 않는 벽 LakeHit)
                var pond = RepairCommand.Pond;
                var pr = RepairCommand.PondRadii;
                for (int k = 0; k < nodes.Count; k++)
                {
                    if (!seen[k])
                        continue;
                    var n = nodes[k];
                    float e = Mathf.Pow((n.x - pond.x) / pr.x, 2f) + Mathf.Pow((n.z - pond.z) / pr.y, 2f);
                    Assert.IsFalse(e < 1f && n.y < pond.y - 0.1f, $"연못 바닥까지 걸어 내려감 {n}");
                    var sp = RepairCommand.Spring;
                    Assert.IsFalse(new Vector2(n.x - sp.x, n.z - sp.z).magnitude < 0.78f && n.y < sp.y, $"샘에 들어감 {n}");
                }
                var fails = new List<string>();
                void Reach(Vector3 p, string what)
                {
                    int k = Node(p);
                    if (k < 0)
                        fails.Add($"{what}: 설 곳 없음 {p}");
                    else if (!seen[k])
                        fails.Add($"{what}: 걸어서 못 감 {p}");
                }
                foreach (var (cell, off, floorY) in RepairCommand.Hatches)
                {
                    var h = new Vector3(cell.x * 8f + off.x, floorY, cell.z * 8f + off.y);
                    Reach(h, $"해치 {cell}");
                    Assert.IsFalse(Physics.CheckBox(h + Vector3.up * 0.1f, new Vector3(0.7f, 0.04f, 0.7f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                        $"해치 {cell} 뚜껑이 묻힘");
                }
                foreach (var (cell, dir) in RepairCommand.Doors)
                    Reach(new Vector3(cell.x * 8f, F + cell.y * 8f, cell.z * 8f) + (Vector3)dir * (RepairCommand.DoorDepth - 0.5f), $"문 {cell} {dir}");
                foreach (var (cell, off) in RepairCommand.UpHatches)
                {
                    float floorY = RepairCommand.DeckY;
                    Reach(new Vector3(cell.x * 8f + off.x, floorY, cell.z * 8f + off.y), $"위 해치 밑 {cell}");
                    Assert.Less(cell.y * 8f + RepairCommand.UpHatchDepth - (floorY + 1.6f), 3.8f, $"위 해치 {cell}가 눈에서 손 닿는 거리 (해치 4 m)");
                }
                float hill = RepairCommand.HillTop, sky = RepairCommand.SkyFloor, deck = RepairCommand.DeckY;
                Reach(AroundTree(1.25f, RepairCommand.EntryAngle, hill), "줄기 속 (문 안 계단 들머리 앞)");
                Reach(AroundTree(3.4f, RepairCommand.EntryAngle, hill), "줄기 문 앞");
                Reach(AroundTree(1.4f, RepairCommand.TreehouseDoorAngle, sky), "줄기 속 나무집 층계참");
                Reach(AroundTree(1.2f, 295f, deck), "관측대 층계참 (계단 꼭대기)");
                Reach(AroundTree(3.3f, 30f, deck), "꼭대기 관측대 (앞 오른쪽)");
                Reach(AroundTree(3.3f, 220f, deck), "꼭대기 관측대 (뒤 왼쪽)");
                var th = RepairCommand.Treehouse;
                Reach(new Vector3(th.x, sky, th.z + 2.9f), "줄기 다리 (나무집 쪽)");
                Reach(th + new Vector3(1.0f, 0f, 0f), "나무집 관제실 안 (지도 탁자 옆)");
                Reach(th + new Vector3(2.05f * Mathf.Cos(122f * Mathf.Deg2Rad), 0f, 2.05f * Mathf.Sin(122f * Mathf.Deg2Rad)), "나무집 마루 (경종 줄 손잡이 앞)");
                Reach(th + new Vector3(2.05f * Mathf.Cos(150f * Mathf.Deg2Rad), 0f, 2.05f * Mathf.Sin(150f * Mathf.Deg2Rad)), "나무집 마루 (다리 → 바구니 문 사이 고리길)");
                Reach(th + new Vector3(-2.15f, 0f, 0f), "나무집 마루 (바구니 문 앞)");
                // 마루 고리길 (다리 90° → 바구니 문 180°)은 이 쪽 원호로도 지나감 (반대쪽으로 돌아 닿아도 통과하던 걷기 시험이 경종 줄을 못 잡았음, 2026-11-13 플레이)
                for (float a = 95f; a <= 175f; a += 5f)
                {
                    var p = th + new Vector3(2.0f * Mathf.Cos(a * Mathf.Deg2Rad), 0f, 2.0f * Mathf.Sin(a * Mathf.Deg2Rad));
                    if (Physics.CheckCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 1.5f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        fails.Add($"나무집 마루 고리길 {a}°가 막힘 {p}");
                }
                var bk = RepairCommand.Basket;
                Reach(bk, "구조 바구니 땅 정류장");
                foreach (float a in RepairCommand.WorkshopAngles)
                {
                    Reach(AroundTree(5.05f, a, hill), $"공방 {a}° 문 앞");
                    Reach(AroundTree(3.9f, a, hill), $"공방 {a}° 안 (작업대 앞)");
                }
                Reach(new Vector3(8f, F, 15.8f), "그루터기 뒤 (기억 등불 보는 자리)");
                Reach(new Vector3(5.51f, F + 0.16f, 14.89f), "돌봄 오두막 마루");
                Reach(new Vector3(8f, F, 17.7f), "연못가 벤치 앞");
                Reach(new Vector3(12.0f, F, 13.0f), "연못 앞 (물길 아치 다리 아래쪽)");
                Reach(new Vector3(-2.6f, F, 2.0f), "언덕 왼쪽 낮은 땅");
                Reach(new Vector3(17.5f, F, 2.0f), "언덕 오른쪽 낮은 땅");
                // 주민 자리 모두 걸어서 닿음 (앉는 자리 = 의자 앞 · 옆 바닥)
                var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>(TemplatePath);
                Assert.IsNotNull(template);
                foreach (var spot in template.ResidentSpots)
                {
                    bool sit = spot.Pose == SpaceStation.Data.ResidentPose.Sit;
                    bool okSpot = false;
                    foreach (var d in new[] { Vector3.zero, Vector3.forward * 0.5f, Vector3.back * 0.5f, Vector3.left * 0.5f, Vector3.right * 0.5f })
                    {
                        var sp = spot.Position + d;
                        if (sit)
                            sp.y -= 0.46f;
                        int k = Node(sp);
                        if (k >= 0 && seen[k] && Mathf.Abs(nodes[k].y - sp.y) < 0.6f)
                        {
                            okSpot = true;
                            break;
                        }
                    }
                    if (!okSpot)
                        fails.Add($"주민 자리 ({(sit ? "앉음" : "섬")}) {spot.Position}: 걸어서 못 감");
                }
                Assert.IsEmpty(fails, string.Join(" / ", fails));
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
            var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>(TemplatePath);
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

        /// <summary>움직이는 조각 (체험 · 미니게임이 씀)이 따로 있고 원점이 값과 같음: 경종 · 구조 바구니 · 도르래 줄 · 가지 등불 · 기억 등불.</summary>
        [Test]
        public void Movers_AtTheirSpots()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            const string N = "INT_EvoRepairCommand_";
            void At(string name, Vector3 p)
            {
                var t = prefab.transform.Find(N + name);
                Assert.IsNotNull(t, $"{name} 없음");
                Assert.Less(Vector3.Distance(t.localPosition, p), 0.01f, $"{name} 원점 {t.localPosition} ≠ {p}");
            }
            At("Bell", RepairCommand.Bell);
            At("BellRope", RepairCommand.BellHandle);
            Assert.IsNull(prefab.transform.Find(N + "BellRope").GetComponent<Collider>(), "종 줄은 충돌 없음 (마루 고리길을 막았음)");
            At("Basket", RepairCommand.Basket);
            At("BasketRope", RepairCommand.Pulley);
            At("PullRope", RepairCommand.Pulley);
            Assert.IsNull(prefab.transform.Find(N + "PullRope").GetComponent<Collider>(), "당기는 줄은 충돌 없음 (바구니 안을 지나 탄 사람이 걸렸음)");
            for (int k = 0; k < RepairCommand.Lanterns.Length; k++)
                At("Lantern" + k, RepairCommand.Lanterns[k]);
            Assert.IsNull(prefab.transform.Find(N + "Lantern" + RepairCommand.Lanterns.Length), "등불이 값보다 많음");
            for (int k = 0; k < RepairCommand.MemoryLamps.Length; k++)
                At("MemLamp" + k, RepairCommand.MemoryLamps[k]);
            Assert.AreEqual(RepairCommand.MemoryLamps.Length, RepairCommand.MemoryShapes.Length);
        }

        private static List<RescueBasketRide.Arrival> Run(RescueBasketRide ride, float seconds, bool holdPull = false)
        {
            var arrivals = new List<RescueBasketRide.Arrival>();
            for (float t = 0f; t < seconds; t += 0.02f)
            {
                if (holdPull && !ride.Pulling)
                    ride.Pull();
                var a = ride.Step(0.02f);
                if (a != RescueBasketRide.Arrival.None)
                    arrivals.Add(a);
            }
            return arrivals;
        }

        /// <summary>구조 바구니: 한 번 당김 = 0.62 m 오르고 멈춤쇠가 붙잡음 · 당기는 중 누르면 이어서 · 꼭대기 (나무집 마루)를 넘지 않고 한 번 닿음 · S 꾹 = 쥔 동안만 내려감 · 빈 바구니 부르기.</summary>
        [Test]
        public void RescueBasketRide_PullHoldLowerCall()
        {
            var ride = new RescueBasketRide(RepairCommand.BasketTop);
            Assert.AreEqual(RepairCommand.SkyFloor, RepairCommand.Basket.y + ride.Top, 1e-4f, "꼭대기 = 나무집 마루");
            int strokes = Mathf.CeilToInt(ride.Top / RescueBasketRide.StrokeRise - 1e-3f);
            Assert.AreEqual(strokes, ride.StrokesLeft);
            Assert.That(strokes, Is.InRange(9, 16), "줄 당기는 횟수가 너무 적거나 많음");
            Assert.IsTrue(ride.Pull());
            Run(ride, 1f);
            Assert.AreEqual(RescueBasketRide.StrokeRise, ride.Height, 1e-3f);
            Run(ride, 3f);
            Assert.AreEqual(RescueBasketRide.StrokeRise, ride.Height, 1e-3f, "손을 떼면 멈춤쇠가 그 높이에 붙잡음");
            ride.Pull();
            ride.Step(0.1f);
            Assert.IsFalse(ride.Pull(), "당기는 중에는 다음 한 번을 이어 둠");
            Run(ride, 1.5f);
            Assert.AreEqual(3f * RescueBasketRide.StrokeRise, ride.Height, 1e-3f, "이어 둔 한 번까지 당김");
            var arrivals = Run(ride, 20f, holdPull: true);
            Assert.IsTrue(ride.AtTop);
            Assert.AreEqual(ride.Top, ride.Height, 1e-4f);
            Assert.AreEqual(1, arrivals.Count);
            Assert.AreEqual(RescueBasketRide.Arrival.Top, arrivals[0]);
            Assert.AreEqual(strokes, ride.Strokes, "꼭대기까지 당긴 횟수");
            Assert.IsFalse(ride.Pull(), "꼭대기에서는 더 못 당김");
            ride.Lower(true);
            Run(ride, 1f);
            float h = ride.Height;
            Assert.That(ride.Top - h, Is.InRange(0.6f, RescueBasketRide.LowerSpeed + 0.01f), "1초 내려간 거리");
            ride.Lower(false);
            Run(ride, 2f);
            Assert.AreEqual(h, ride.Height, 1e-4f, "떼면 멈춤");
            ride.Lower(true);
            arrivals = Run(ride, 10f);
            Assert.IsTrue(ride.AtBottom);
            Assert.AreEqual(new[] { RescueBasketRide.Arrival.Bottom }, arrivals.ToArray());
            ride.Lower(false);
            ride.Call(true);
            arrivals = Run(ride, ride.Top / RescueBasketRide.CallSpeed + 0.2f);
            Assert.IsTrue(ride.AtTop, "빈 바구니가 마루로 올라옴");
            Assert.AreEqual(new[] { RescueBasketRide.Arrival.Top }, arrivals.ToArray());
            ride.Call(false);
            Run(ride, ride.Top / RescueBasketRide.CallSpeed + 0.2f);
            Assert.IsTrue(ride.AtBottom, "빈 바구니가 정류장으로 내려옴");
            ride.Park(3f);
            Assert.IsFalse(ride.Moving);
            Assert.AreEqual(3f, ride.Height, 1e-5f);
        }

        /// <summary>등불 쇼: 종 → 떠오름 → 돌기 (등불 열셋이 떼가 지나는 차례로 한 번씩 켜짐) → 오름 → 흩어짐 → 돌아감 → 쉼, 도는 중엔 다시 못 울림, 다시 울리면 켜진 등불이 종 동안 스러졌다가 다시 켜짐.</summary>
        [Test]
        public void FireflyLanternShow_Sequence()
        {
            var angles = new float[RepairCommand.Lanterns.Length];
            for (int k = 0; k < angles.Length; k++)
                angles[k] = RepairCommand.AngleAroundTree(RepairCommand.Lanterns[k]);
            var show = new FireflyLanternShow(angles);
            Assert.Less(show.CircleTime, 360f / FireflyLanternShow.OrbitSpeed + 4f, "한 바퀴 남짓");
            Assert.That(show.TotalTime, Is.InRange(25f, 45f), "쇼 길이");
            for (int round = 0; round < 2; round++)
            {
                Assert.IsTrue(show.Start());
                Assert.IsFalse(show.Start(), "도는 중에 다시 울림");
                var phases = new List<FireflyLanternShow.Phase> { show.Current };
                var order = new List<int>();
                var lit = new List<int>();
                float ringGlow0 = round == 1 ? show.Glow(0) : 0f;
                float t = 0f;
                while (show.Running && t < 120f)
                {
                    lit.Clear();
                    show.Step(0.02f, lit);
                    t += 0.02f;
                    order.AddRange(lit);
                    foreach (int k in lit)
                    {
                        Assert.AreEqual(FireflyLanternShow.Phase.Circle, show.Current, "등불은 도는 동안 켜짐");
                        Assert.AreEqual(1f, show.Visit(k), 1e-3f, "드론이 등불 옆에 닿을 때 켜짐");
                    }
                    if (show.Current != phases[phases.Count - 1])
                        phases.Add(show.Current);
                    if (round == 1 && show.Current == FireflyLanternShow.Phase.Ring && show.PhaseTime > 1f)
                        Assert.Less(show.Glow(0), ringGlow0, "다시 울리면 켜진 등불이 스러짐");
                }
                Assert.AreEqual(new[] { FireflyLanternShow.Phase.Ring, FireflyLanternShow.Phase.Rise, FireflyLanternShow.Phase.Circle,
                    FireflyLanternShow.Phase.Finale, FireflyLanternShow.Phase.Return, FireflyLanternShow.Phase.Idle }, phases.ToArray());
                Assert.AreEqual(show.TotalTime, t, 0.2f);
                Assert.AreEqual(angles.Length, order.Count, "등불마다 한 번씩");
                Assert.AreEqual(angles.Length, new HashSet<int>(order).Count);
                Assert.AreEqual(angles.Length, show.LitCount);
                float prev = -1f;
                foreach (int k in order)
                {
                    float d = Mathf.Repeat(angles[k] - FireflyLanternShow.StartAngle, 360f);
                    Assert.GreaterOrEqual(d, prev, "떼가 지나는 차례 (돌기 시작 각에서 각이 커지는 쪽)");
                    prev = d;
                }
                for (int k = 0; k < angles.Length; k++)
                    Assert.AreEqual(1f, show.Glow(k), "쇼가 끝나도 등불은 켜진 채");
            }
            show.Reset();
            Assert.IsFalse(show.Running);
            Assert.AreEqual(0f, show.Glow(0), "방을 나가면 꺼짐");
        }

        /// <summary>
        /// 드론 길이 몸체 (줄기 · 가지 · 나무집 · 관측대 · 공방)에 닿지 않음 (InteriorFireflyShow 토막 길): 떼가 도는 고리 (흩어짐 ± 0.3 m · 높이 ± 0.45, 반짝일 때 바깥으로 FinaleOut,
        /// 돌아갈 때 문 위 반지름) · 공방 안 자리 → 문 가운데 줄 → 문 앞에서 곧게 고리 높이 · 등불 옆 자리.
        /// </summary>
        [Test]
        public void FireflyDrones_PathsClear()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var fails = new List<string>();
                void Free(Vector3 p, float r, string what)
                {
                    if (Physics.CheckSphere(p, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        fails.Add($"{what} {p}");
                }
                void Line(Vector3 a, Vector3 b, float r, string what)
                {
                    if (Physics.CheckCapsule(a, b, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        fails.Add($"{what} {a} → {b}");
                }
                for (float a = 0f; a < 360f; a += 4f)
                {
                    foreach (float dr in new[] { -0.3f, 0f, 0.3f, 0.3f + RepairCommand.FinaleOut, RepairCommand.DroneDoorRadius - RepairCommand.SwarmRadius })
                        foreach (float dy in new[] { -0.45f, 0f, 0.45f })
                            Free(RepairCommand.AroundTree(RepairCommand.SwarmRadius + dr, a, RepairCommand.SwarmY + dy), 0.14f, $"떼 고리 {a}°");
                }
                foreach (float w in RepairCommand.WorkshopAngles)
                {
                    foreach (float da in new[] { -4f, 0f, 4f })
                        foreach (float dy in new[] { -0.25f, 0f, 0.25f })
                            Free(RepairCommand.AroundTree(RepairCommand.DroneHomeRadius, w + da, RepairCommand.HillTop + RepairCommand.DroneHomeLift + dy), 0.13f, $"공방 {w}° 안 드론 자리");
                    foreach (float dy in new[] { -0.15f, 0f, 0.15f })
                    {
                        float y = RepairCommand.HillTop + RepairCommand.DroneDoorLift + dy;
                        Line(RepairCommand.AroundTree(RepairCommand.DroneHomeRadius, w, RepairCommand.HillTop + RepairCommand.DroneHomeLift), RepairCommand.AroundTree(RepairCommand.DroneDoorInRadius, w, y), 0.13f, $"공방 {w}° 안 → 문 안");
                        Line(RepairCommand.AroundTree(RepairCommand.DroneDoorInRadius, w, y), RepairCommand.AroundTree(RepairCommand.DroneDoorRadius, w, y), 0.13f, $"공방 {w}° 문 가운데 줄");
                        Line(RepairCommand.AroundTree(RepairCommand.DroneDoorRadius, w, y), RepairCommand.AroundTree(RepairCommand.DroneDoorRadius, w, RepairCommand.SwarmY + dy), 0.13f, $"공방 {w}° 문 앞 → 고리 높이");
                    }
                }
                for (int k = 0; k < RepairCommand.Lanterns.Length; k++)
                    Free(RepairCommand.LanternHover(k), 0.13f, $"등불 {k} 옆 자리");
                Assert.IsEmpty(fails, string.Join(" / ", fails));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 체험 연결: 구조 바구니 (바닥 판 윗면 = 바구니 바닥 · 엮은 벽 열 칸은 마루 쪽 트임 밖 · 마루 끝까지 닿는 바닥) · 마루 바구니 문 벽 · 금빛 막대 ·
        /// 종 줄 손잡이 충돌체 (종 줄 자식) · 등불 열셋 · 쇼 · 바구니가 마루에 서면 걸어 나가는 트임이 마루 문과 마주봄.
        /// </summary>
        [Test]
        public void Experience_Wired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            const string N = "INT_EvoRepairCommand_";
            Assert.IsNotNull(prefab.GetComponent<InteriorRescueBasket>(), "구조 바구니 체험 없음");
            Assert.IsNotNull(prefab.GetComponent<InteriorFireflyShow>(), "경종 등불 쇼 없음");
            var basket = prefab.transform.Find(N + "Basket");
            var floor = basket.Find("BasketFloor").GetComponent<BoxCollider>();
            Assert.AreEqual(RepairCommand.BasketFloorTop, floor.center.y + floor.size.y * 0.5f, 1e-4f, "바닥 판 윗면");
            Assert.GreaterOrEqual(RepairCommand.Basket.x + floor.center.x + floor.size.x * 0.5f, RepairCommand.Treehouse.x - RepairCommand.TreehouseDeckRadius - 0.02f, "바닥 판이 마루 끝에 닿지 않음 (틈)");
            int walls = 0;
            foreach (Transform c in basket)
            {
                if (!c.name.StartsWith("BasketWall"))
                    continue;
                walls++;
                float a = Mathf.Abs(Mathf.DeltaAngle(0f, Mathf.Atan2(c.localPosition.z, c.localPosition.x) * Mathf.Rad2Deg));
                Assert.Greater(a, RepairCommand.BasketOpening, $"{c.name}이 마루 쪽 트임을 막음");
            }
            Assert.AreEqual(10, walls);
            Assert.Less(RepairCommand.Basket.x, RepairCommand.Treehouse.x, "바구니는 마루 서쪽 (트임 +x가 마루 쪽)");
            Assert.AreEqual(RepairCommand.Basket.z, RepairCommand.BasketGate.z, 1e-4f, "바구니 트임 ↔ 마루 문");
            var gate = prefab.transform.Find("BasketGate");
            Assert.IsNotNull(gate);
            Assert.IsNotNull(gate.GetComponent<BoxCollider>());
            Assert.IsNotNull(prefab.transform.Find("BasketGateBar/Bar"), "마루 문 금빛 막대 없음");
            var handle = prefab.transform.Find(N + "BellRope/BellHandle");
            Assert.IsNotNull(handle, "종 줄 손잡이 충돌체가 종 줄 자식이 아님");
            Assert.IsNotNull(handle.GetComponent<SphereCollider>());
            var so = new SerializedObject(prefab.GetComponent<InteriorFireflyShow>());
            var lanterns = so.FindProperty("_lanterns");
            Assert.AreEqual(RepairCommand.Lanterns.Length, lanterns.arraySize);
            for (int k = 0; k < lanterns.arraySize; k++)
                Assert.IsNotNull(lanterns.GetArrayElementAtIndex(k).objectReferenceValue, $"등불 {k} 빠짐");
            foreach (var p in new[] { "_bell", "_bellRope", "_handle", "_bodyMaterial", "_glowMaterial", "_sparkMaterial" })
                Assert.IsNotNull(so.FindProperty(p).objectReferenceValue, p);
            var sb = new SerializedObject(prefab.GetComponent<InteriorRescueBasket>());
            foreach (var p in new[] { "_basket", "_rope", "_gate", "_gateBar" })
                Assert.IsNotNull(sb.FindProperty(p).objectReferenceValue, p);
        }
    }
}
