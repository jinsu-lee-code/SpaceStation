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

        // ---------------- 체험 "정자 꽃 그네" ----------------

        /// <summary>그네를 타며 끝 (박자 ≥ 0.9)에서만 구르면 60초 안에 종 여섯 개, 바닥 근처에서 막 누르면 첫 종 (28°)도 못 닿음, 가장 높이 ≤ MaxAngle.</summary>
        [Test]
        public void Swing_RhythmRingsAllBells()
        {
            var good = new FlowerSwing(StellarGrid.SwingLength, StellarGrid.BellAngles);
            good.Begin();
            Assert.AreEqual(FlowerSwing.Kick.Start, good.Pump());
            float t = 0f;
            const float dt = 1f / 60f;
            int perfect = 0;
            while (t < 60f && !good.AllRung)
            {
                good.Step(dt, null);
                t += dt;
                if (!good.Kicked && good.Timing >= 0.9f && good.Pump() == FlowerSwing.Kick.Perfect)
                    perfect++;
                Assert.LessOrEqual(good.Amplitude, FlowerSwing.MaxAngle + 0.5f);
            }
            Assert.IsTrue(good.AllRung, $"박자 맞춰 60초에 종 {good.RungCount} / 6 (폭 {good.Amplitude:F0}°)");
            Assert.Greater(perfect, 5);
            Debug.Log($"[StellarGridTests] 그네: 박자 딱 {perfect}번 · 종 여섯 {t:F1}초");

            var bad = new FlowerSwing(StellarGrid.SwingLength, StellarGrid.BellAngles);
            bad.Begin();
            bad.Pump();
            for (t = 0f; t < 60f; t += dt)
            {
                bad.Step(dt, null);
                if (bad.Timing < 0.3f)
                    bad.Pump();
            }
            Assert.AreEqual(0, bad.RungCount, $"막 누르기만 해도 종이 울림 (폭 {bad.Highest:F0}°)");
        }

        /// <summary>멈춘 그네에서 F = 출발 (12°), 한 흔들림에 두 번 구르면 "벌써 굴렀어요", S 꾹 (발로 끌기) 8초 안에 멈춤.</summary>
        [Test]
        public void Swing_StartsAndBrakes()
        {
            var s = new FlowerSwing(StellarGrid.SwingLength, StellarGrid.BellAngles);
            s.Begin();
            Assert.IsTrue(s.AtRest);
            Assert.AreEqual(FlowerSwing.Kick.Start, s.Pump());
            Assert.AreEqual(FlowerSwing.StartAngle, s.Amplitude, 0.3f);
            Assert.AreEqual(FlowerSwing.Kick.Again, s.Pump());
            for (int i = 0; i < 600; i++)
                s.Step(1f / 60f, null);
            Assert.Greater(s.Amplitude, 8f, "10초 만에 멈춤 (공기 저항이 너무 셈)");
            s.Braking = true;
            float t = 0f;
            while (!s.AtRest && t < 8f)
            {
                s.Step(1f / 60f, null);
                t += 1f / 60f;
            }
            Assert.IsTrue(s.AtRest, $"발로 끌어도 8초에 안 멈춤 ({s.Amplitude:F1}°)");
        }

        /// <summary>
        /// 그네가 움직이는 전 범위 (± MaxAngle, 2°마다 — 체크리스트 23): 자리 상자 · 탄 몸 (자리 → 눈 + 머리 위 0.15, 반지름 0.3)이 몸체에 안 닿고 잎 · 꽃송이에서 떨어짐,
        /// + 각 = 자리가 +x (앞, 앞 종 Bell0_k 쪽). 종 각에서 자리가 그 종 아래 옆 (z 1 m 안 · 꽃송이 끝보다 아래).
        /// </summary>
        [Test]
        public void Swing_ArcClear()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var swing = root.GetComponentInChildren<InteriorFlowerSwing>(true);
                Assert.IsNotNull(swing, "그네 체험 없음");
                var pivot = root.transform.Find("SwingPivot");
                Assert.IsNotNull(pivot);
                Assert.IsTrue(pivot.Find("INT_EvoStellarGrid_Swing") != null, "그네 조각이 피벗 아래에 없음");
                var leaves = new List<Vector3>();
                foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!f.name.EndsWith("_Leaves") || f.sharedMesh == null)
                        continue;
                    var m = f.transform.localToWorldMatrix;
                    foreach (var v in f.sharedMesh.vertices)
                    {
                        var w = m.MultiplyPoint3x4(v);
                        if (w.x > 6f && w.x < 18f && w.z > 5f && w.z < 9.5f)
                            leaves.Add(w);
                    }
                }
                float L = StellarGrid.SwingLength;
                for (float a = -FlowerSwing.MaxAngle; a <= FlowerSwing.MaxAngle + 0.01f; a += 2f)
                {
                    pivot.localRotation = Quaternion.AngleAxis(a, Vector3.forward);
                    Physics.SyncTransforms();
                    var seat = pivot.TransformPoint(new Vector3(0f, -L + 0.035f, 0f));
                    var head = pivot.TransformPoint(new Vector3(0f, -L + 0.035f + 0.85f + 0.15f, 0f));
                    var foot = pivot.TransformPoint(new Vector3(0.25f, -L - 0.45f, 0f));   // 늘어뜨린 발 (자리 앞 아래)
                    Assert.IsFalse(Physics.CheckCapsule(seat + (head - seat).normalized * 0.3f, head - (head - seat).normalized * 0.3f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                        $"{a}°: 탄 몸이 몸체에 닿음 {seat}");
                    Assert.IsFalse(Physics.CheckSphere(foot, 0.12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"{a}°: 발이 몸체에 닿음 {foot}");
                    foreach (var w in leaves)
                    {
                        var d = DistanceToSegment(w, seat, head);
                        Assert.Greater(d, 0.28f, $"{a}°: 탄 몸이 잎 · 꽃에 닿음 {w} ({d:F2} m)");
                    }
                    if (Mathf.Abs(a - 60f) < 0.01f)
                        Assert.Greater(seat.x, StellarGrid.SwingPivot.x + 3f, "+ 각인데 자리가 앞 (+x)으로 안 감");
                }
                pivot.localRotation = Quaternion.identity;
                for (int k = 0; k < 6; k++)
                {
                    var bell = root.transform.Find($"INT_EvoStellarGrid_Bell{(k < 3 ? 0 : 1)}_{k % 3}");
                    Assert.IsNotNull(bell, $"종 {k}");
                    float a = (k < 3 ? 1f : -1f) * StellarGrid.BellAngles[k % 3];
                    pivot.localRotation = Quaternion.AngleAxis(a, Vector3.forward);
                    var seat = pivot.TransformPoint(new Vector3(0f, -L + 0.035f, 0f));
                    var b = bell.GetComponent<Renderer>().bounds;
                    Assert.Less(Mathf.Abs(b.center.x - seat.x), 0.35f, $"종 {k}: 그네가 {a}°일 때 자리 x {seat.x:F2} ↔ 종 {b.center.x:F2}");
                    Assert.Less(Mathf.Abs(b.center.z - seat.z), 1.0f, $"종 {k}: 자리 옆이 아님");
                    Assert.Greater(b.min.y, seat.y + 0.15f, $"종 {k}: 자리 높이보다 아래 (몸을 뚫음)");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }

        // ---------------- 미니게임 "벌 몰래 꿀 먹기" ----------------

        /// <summary>
        /// 규칙: 안 먹으면 안 들킴 · 꿀벌이 등을 돌렸을 때 (Back)만 먹으면 안 들키고 꿀단지를 다 비움 (속임수에 속아 손을 떼도) ·
        /// 계속 먹으면 세 번 쏘이고 끝 (목숨 0) · 속임수 (반쯤 돌다 맒)가 나오고 그동안은 곰을 못 봄 · 많이 먹을수록 등 돌린 시간이 짧아짐.
        /// </summary>
        [Test]
        public void HoneySneak_Rules()
        {
            const float dt = 1f / 60f;
            var idle = new HoneySneak();
            idle.Start(3);
            for (float t = 0f; t < 60f; t += dt)
                Assert.AreNotEqual(HoneySneak.Event.Caught, idle.Step(dt, false));
            Assert.AreEqual(HoneySneak.Lives, idle.LivesLeft);
            Assert.AreEqual(0, idle.Spoons);

            for (int seed = 1; seed <= 5; seed++)
            {
                var careful = new HoneySneak();
                careful.Start(seed);
                int feints = 0;
                var last = careful.State;
                float time = 0f;
                while (!careful.Finished && time < 600f)
                {
                    bool eat = !careful.Ready && careful.State == HoneySneak.Guard.Back;
                    var ev = careful.Step(dt, eat);
                    Assert.AreNotEqual(HoneySneak.Event.Caught, ev, $"시드 {seed}: 등 돌렸을 때만 먹었는데 들킴 ({careful.State})");
                    if (careful.State == HoneySneak.Guard.Feint)
                        Assert.IsFalse(careful.Seeing, "속임수인데 곰을 봄");
                    if (careful.State != last && careful.State == HoneySneak.Guard.Feint)
                        feints++;
                    last = careful.State;
                    time += dt;
                }
                Assert.IsTrue(careful.Emptied, $"시드 {seed}: 조심히 먹어 600초에 꿀단지를 못 비움 ({careful.Spoons}숟갈)");
                Assert.Greater(feints, 0, $"시드 {seed}: 속임수가 한 번도 없음");
            }

            var greedy = new HoneySneak();
            greedy.Start(9);
            int caught = 0;
            for (float t = 0f; t < 120f && !greedy.Finished; t += dt)
                if (greedy.Step(dt, true) == HoneySneak.Event.Caught)
                    caught++;
            Assert.IsTrue(greedy.Finished, "계속 먹어도 안 끝남");
            Assert.IsFalse(greedy.Emptied, "계속 먹었는데 꿀단지를 비움 (안 들킴)");
            Assert.AreEqual(HoneySneak.Lives, caught);
            Assert.AreEqual(0, greedy.LivesLeft);
        }

        /// <summary>옆 카메라에서 곰 머리 · 꿀단지 · 경비 꿀벌까지 몸체 · 잎이 가리지 않고, 꿀단지가 그루터기 위에 (밑면 = 윗면 + 2 mm).</summary>
        [Test]
        public void HoneySneak_StageVisible()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                Assert.IsNotNull(root.GetComponentInChildren<InteriorHoneySneak>(true), "미니게임 없음");
                var pot = root.transform.Find("INT_EvoStellarGrid_HoneyPot");
                Assert.IsNotNull(pot, "꿀단지 (따로 물체) 없음");
                var pb = pot.GetComponent<Renderer>().bounds;
                Assert.AreEqual(StellarGrid.StumpTop.y + 0.002f, pb.min.y, 0.01f, "꿀단지 밑면");
                Assert.Less(new Vector2(pb.center.x - StellarGrid.StumpTop.x, pb.center.z - StellarGrid.StumpTop.z).magnitude, 0.05f, "꿀단지가 그루터기 가운데가 아님");
                var cam = StellarGrid.SneakCamera;
                Assert.IsFalse(Physics.CheckSphere(cam, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "카메라 자리가 몸체 속");
                var targets = new[]
                {
                    (StellarGrid.StumpTop + Vector3.up * 0.75f, "곰 머리"),
                    (StellarGrid.StumpTop + new Vector3(0.18f, 0.3f, 0f), "무릎 꿀단지"),
                    (StellarGrid.GuardBee, "경비 꿀벌"),
                    (StellarGrid.GuardBee + InteriorHoneySneak.MarkOffset, "경비 꿀벌 머리 위 !"),
                };
                foreach (var (p, what) in targets)
                {
                    var to = p - cam;
                    Assert.IsFalse(Physics.Raycast(cam, to.normalized, out var hit, to.magnitude - 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                        $"카메라 → {what}: 가림 ({hit.collider?.name} {hit.point})");
                    var look = (StellarGrid.SneakLookAt - cam).normalized;
                    Assert.Greater(Vector3.Dot(to.normalized, look), Mathf.Cos(40f * Mathf.Deg2Rad), $"{what}: 화면 밖");
                }
                foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!f.name.EndsWith("_Leaves") || f.sharedMesh == null)
                        continue;
                    var m = f.transform.localToWorldMatrix;
                    foreach (var v in f.sharedMesh.vertices)
                    {
                        var w = m.MultiplyPoint3x4(v);
                        foreach (var (p, what) in targets)
                            Assert.Greater(DistanceToSegment(w, cam, p), 0.05f, $"카메라 → {what}: 잎 · 꽃이 가림 {w}");
                    }
                }
                // 화면 (세로 시야 60°, 16:9 · 4:3) 안: 곰 · 그루터기는 아래 안내 상자 (아래 14 %) 위, 꿀벌 · "!"는 오른쪽 위 정거장 정보 창 (오른쪽 26 % · 위 34 %) 밖
                var camGo = new GameObject("SneakCam");
                try
                {
                    var c = camGo.AddComponent<Camera>();
                    c.fieldOfView = 60f;
                    camGo.transform.SetPositionAndRotation(cam, Quaternion.LookRotation(StellarGrid.SneakLookAt - cam));
                    foreach (float aspect in new[] { 16f / 9f, 4f / 3f })
                    {
                        c.aspect = aspect;
                        foreach (var (p, what, bottom) in new[]
                        {
                            (StellarGrid.StumpTop + Vector3.up * 0.75f, "곰 머리", false), (StellarGrid.StumpTop + Vector3.down * 0.3f, "그루터기", true),
                            (StellarGrid.GuardBee, "경비 꿀벌", false), (StellarGrid.GuardBee + InteriorHoneySneak.MarkOffset + Vector3.up * 0.25f, "꿀벌 머리 위 ! (위 끝)", false),
                        })
                        {
                            var v = c.WorldToViewportPoint(p);
                            Assert.IsTrue(v.z > 0f && v.x > 0.06f && v.x < 0.94f && v.y < 0.94f, $"{what}: 화면 밖 {v} (화면비 {aspect:F2})");
                            Assert.Greater(v.y, bottom ? 0.08f : 0.16f, $"{what}: 아래 안내 상자에 가림 {v}");
                            Assert.IsFalse(v.x > 0.74f && v.y > 0.64f, $"{what}: 오른쪽 위 정거장 정보 창에 가림 {v} (화면비 {aspect:F2})");
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(camGo);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
