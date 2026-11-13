using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 진화 3단계 궤도 방어망 (Blender od3_builder와 같은 값): 연결점 (외형 연결점 면과 같음 — 옆 문 여덟 · 아래 해치 열 · 위 해치 셋) ·
    /// 걸어서 닿음 (문 · 해치 · 곶 윗면 · 등대 발코니 · 등실 안 · 하늘 다리 · 동굴 · 잔교 · 포대 부표 · 방파제 끝 · 해안, 바다 바닥 못 내려감) · 해치 뚜껑이 묻히지 않음 · 주민 자리 발밑.
    /// </summary>
    public class OrbitalDefenseTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interior/PF_Interior_EvoOrbitalDefense.prefab";
        private const float F = OrbitalDefense.Floor;

        [Test]
        public void Sockets_MatchExterior()
        {
            // 외형 MD_Evo_OrbitalDefense 연결점 (면 깊이 ≥ 0인 바깥 면): 바닥 칸 열 아래 · 곶 둘레 옆 여섯 · 곶 뒤 위 칸 옆 둘 · 칸 (2, 0, 1) · (0, 1, 1) · (1, 1, 1) 위
            var module = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.ModuleData>("Assets/_Project/Data/Modules/Evolved/MD_Evo_OrbitalDefense.asset");
            Assert.IsNotNull(module);
            var cells = new HashSet<Vector3Int>(module.CellOffsets);
            var expected = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var c in module.CellOffsets)
                foreach (var d in new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) })
                    if (!cells.Contains(c + d) && module.TryGetFaceDepth(c, d, out _) && !module.IsClosedFace(c, d))
                        expected.Add((c, d));
            var actual = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var (cell, off, floorY) in OrbitalDefense.Hatches)
            {
                Assert.IsTrue(actual.Add((cell, Vector3Int.down)), $"해치 칸 겹침 {cell}");
                Assert.Less(Mathf.Abs(off.x), 3.05f, $"{cell}");
                Assert.Less(Mathf.Abs(off.y), 3.05f, $"{cell}");
            }
            foreach (var (cell, dir) in OrbitalDefense.Doors)
                Assert.IsTrue(actual.Add((cell, dir)));
            foreach (var (cell, off) in OrbitalDefense.UpHatches)
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

        [Test]
        public void Walk_ReachesEverything()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                const float step = 0.2f, x0 = -3.9f, z0 = -3.9f, top = 12f;
                int nx = Mathf.RoundToInt(39.8f / step) + 1, nz = Mathf.RoundToInt(15.8f / step) + 1;
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
                var start = Node(OrbitalDefense.Spawn);
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
                        Assert.IsFalse(nodes[k].y < OrbitalDefense.SeaLevel - 0.15f, $"바다 · 웅덩이 바닥까지 걸어 내려감 {nodes[k]}");
                var fails = new List<string>();
                void Reach(Vector3 p, string what)
                {
                    int k = Node(p);
                    if (k < 0)
                        fails.Add($"{what}: 설 곳 없음 {p}");
                    else if (!seen[k])
                        fails.Add($"{what}: 걸어서 못 감 {p}");
                }
                foreach (var (cell, off, floorY) in OrbitalDefense.Hatches)
                {
                    var h = new Vector3(cell.x * 8f + off.x, floorY, cell.z * 8f + off.y);
                    Reach(h, $"해치 {cell}");
                    Assert.IsFalse(Physics.CheckBox(h + Vector3.up * 0.1f, new Vector3(0.7f, 0.04f, 0.7f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                        $"해치 {cell} 뚜껑이 묻힘");
                }
                foreach (var (cell, dir) in OrbitalDefense.Doors)
                    Reach(new Vector3(cell.x * 8f, F + cell.y * 8f, cell.z * 8f) + (Vector3)dir * (OrbitalDefense.DoorDepth - 0.5f), $"문 {cell} {dir}");
                foreach (var (cell, off) in OrbitalDefense.UpHatches)
                {
                    float floorY = cell.y == 1 ? OrbitalDefense.SkywalkY : OrbitalDefense.MoleTop;
                    Reach(new Vector3(cell.x * 8f + off.x, floorY, cell.z * 8f + off.y), $"위 해치 밑 {cell}");
                    Assert.Less(cell.y * 8f + OrbitalDefense.UpHatchDepth - (floorY + 1.6f), 3.8f, $"위 해치 {cell}가 눈에서 손 닿는 거리 (해치 4 m)");
                }
                var lh = OrbitalDefense.Lighthouse;
                float top_ = OrbitalDefense.CapeTop;
                // 절벽 계단 = 곶을 감고 올라가는 곡선 계단 (2026-11-13 다시 — od3_builder CLIFFSTEPS 단 가운데)
                Reach(new Vector3(-1.179f, -0.963f, 0.337f), "절벽 계단 2단");
                Reach(new Vector3(0.104f, 0.049f, -0.915f), "절벽 계단 8단");
                Reach(new Vector3(1.694f, 1.061f, -1.723f), "절벽 계단 14단");
                Reach(new Vector3(3.474f, 2.073f, -1.865f), "절벽 계단 20단");
                Reach(new Vector3(5.218f, 3.085f, -1.491f), "절벽 계단 26단");
                Reach(new Vector3(5.981f, 3.591f, -1.023f), "절벽 계단 29단");
                Reach(new Vector3(5.54f, top_, 0.37f), "곶 윗면 (계단 꼭대기 → 등대 길)");
                Reach(new Vector3(-2.6f, F, -0.4f), "절벽 계단 들머리 (왼쪽 해변)");
                Reach(new Vector3(6.65f, top_, 3.6f), "곶 끝 망원경 앞");
                Reach(new Vector3(1.55f, top_, 2.3f), "관리인 오두막 문 앞");
                Reach(new Vector3(lh.x + 2.0f, top_, lh.z - 2.4f), "등대 둘레 (나선 계단 첫 단 앞)");
                Reach(new Vector3(lh.x, OrbitalDefense.GalleryY, lh.z - 1.85f), "꼭대기 발코니 (앞)");
                Reach(new Vector3(lh.x + 1.85f, OrbitalDefense.GalleryY, lh.z), "꼭대기 발코니 (바다 쪽)");
                Reach(new Vector3(lh.x + 0.92f * Mathf.Cos(7f * Mathf.Deg2Rad), OrbitalDefense.GalleryY, lh.z + 0.92f * Mathf.Sin(7f * Mathf.Deg2Rad)), "등실 안 렌즈 손잡이 앞");
                Reach(new Vector3(lh.x, OrbitalDefense.SkywalkY, 10.45f), "하늘 다리 가운데 (등대 뒤)");
                var cave = OrbitalDefense.CavePath;
                Reach(new Vector3(cave[1].x, F, cave[1].y), "큰 바다 동굴 입구");
                Reach(new Vector3(cave[4].x, F, cave[4].y), "큰 바다 동굴 안쪽");
                Reach(new Vector3(-2.7f, F, 4.0f), "곶 왼쪽 자갈 해안");
                Reach(new Vector3(2.0f, F, 11.0f), "곶 뒤 자갈 해안");
                Reach(new Vector3(-0.6f, F, -2.8f), "절벽 계단 아래 해변");
                Reach(new Vector3(OrbitalDefense.PierX, F + 0.09f, OrbitalDefense.PierEndZ - 0.4f), "잔교 끝");
                var t = OrbitalDefense.Turret;
                Reach(t + new Vector3(-1.1f, 0f, -0.5f), "포대 부표 (건널 판 쪽)");
                Reach(new Vector3(16f, OrbitalDefense.MoleTop, 10.7f), "방파제 (뒤)");
                Reach(new Vector3(28f, OrbitalDefense.MoleTop, 10.3f), "방파제 (굽이)");
                Reach(OrbitalDefense.MoleEnd + new Vector3(0f, 0f, -0.8f), "방파제 끝 등불대 참");
                foreach (float x in OrbitalDefense.Loungers)
                    Reach(new Vector3(x, F, OrbitalDefense.LoungerZ + 0.9f), $"선베드 앞 {x}");
                Reach(new Vector3(32.2f, F, -2.0f), "조수 웅덩이 앞 해변");
                // 주민 자리 모두 걸어서 닿음 (앉는 자리 = 의자 앞 · 옆 바닥) — 곶 윗면 뒤 벤치가 오두막 · 샘에 막혀 부탁을 끝낼 수 없었음 (2026-11-12 사용자 지적)
                var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>("Assets/_Project/Data/Interior/IT_EvoOrbitalDefense.asset");
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
            var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>("Assets/_Project/Data/Interior/IT_EvoOrbitalDefense.asset");
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

        // ---------------- 체험 "등대 빛 돌리기" ----------------

        /// <summary>
        /// 잡고 D로 돌리다 빛 안에 든 곳에서 멈추면 40초 안에 모든 곳이 답함 · 멈추지 않고 가장 빠르게 쓸기만 하면 다 답하지 않음 (머물러야 답함) ·
        /// 잡지 않고 천천히 도는 동안은 답만 하고 세지 않음 · 한꺼번에 셋 넘게 답하지 않음.
        /// </summary>
        [Test]
        public void Lighthouse_SweepAnswersAll()
        {
            var yaws = new List<float>();
            var widths = new List<float>();
            foreach (var (_, at) in OrbitalDefense.BeamTargets)
            {
                yaws.Add(OrbitalDefense.BeamTargetYaw(at));
                widths.Add(OrbitalDefense.BeamTargetWidth(at));
            }
            var idle = new LighthouseBeam(OrbitalDefense.LensHandleYaw, yaws, widths);
            var answered = new List<int>();
            for (int i = 0; i < 60 * 50; i++)
                idle.Step(1f / 60f, answered);
            Assert.Greater(answered.Count, 0, "천천히 돌 때 답하는 곳이 없음");
            Assert.AreEqual(0, idle.FoundCount, "잡지 않았는데 셈");

            var sweep = new LighthouseBeam(OrbitalDefense.LensHandleYaw, yaws, widths);
            sweep.Begin();
            sweep.Steer = 1f;
            for (int i = 0; i < 60 * 30; i++)
                sweep.Step(1f / 60f, null);
            Assert.Less(sweep.FoundCount, sweep.TargetCount, "가장 빠르게 쓸기만 해도 다 답함 (머물러야 답해야 함)");

            var b = new LighthouseBeam(OrbitalDefense.LensHandleYaw, yaws, widths);
            b.Begin();
            float t = 0f;
            int most = 0;
            var now = new List<int>();
            while (!b.AllFound && t < 40f)
            {
                float near = 999f;                   // 가장 가까운 아직 안 답한 곳 (앞 · 뒤 줄기) — 사람처럼 가까우면 그쪽으로 조금씩, 아니면 오른쪽으로
                for (int i = 0; i < b.TargetCount; i++)
                {
                    if (b.Found(i))
                        continue;
                    foreach (float off in new[] { 0f, 180f })
                    {
                        float d = Mathf.DeltaAngle(b.Yaw + off, yaws[i]);
                        if (Mathf.Abs(d) < Mathf.Abs(near))
                            near = d;
                    }
                }
                b.Steer = Mathf.Abs(near) < 20f ? Mathf.Clamp(near / 6f, -1f, 1f) : 1f;
                now.Clear();
                b.Step(1f / 60f, now);
                most = Mathf.Max(most, now.Count);
                t += 1f / 60f;
            }
            Assert.IsTrue(b.AllFound, $"40초에 {b.FoundCount} / {b.TargetCount}");
            Assert.Less(most, 3, "한 번에 셋 넘게 답함 (곳 방향이 겹침)");
            Debug.Log($"[OrbitalDefenseTests] 등대 빛: 멈춰 비추며 {b.TargetCount}곳 {t:F1}초 · 쓸기만 {sweep.FoundCount} / {sweep.TargetCount}");
        }

        /// <summary>
        /// 빛줄기는 렌즈 (수정)에서 나와 등실 유리 아래 턱 · 회랑 바닥 위로 지나감 — 45°였을 때 회랑 바닥 밑에서 나왔음 (사용자 2026-11-13 "회랑 바닥면에서 빛이 나옴").
        /// 어느 방향이든 빛줄기 아래 끝 (BeamRadius, 가장 짧은 빛줄기 = 가장 빨리 퍼짐)이 턱 · 회랑 끝보다 위.
        /// </summary>
        [Test]
        public void Lighthouse_BeamLeavesOverGallery()
        {
            var c = OrbitalDefense.LensCenter;
            float p = OrbitalDefense.BeamPitch * Mathf.Deg2Rad;
            for (int yaw = 0; yaw < 360; yaw += 3)
            {
                float reach = OrbitalDefense.BeamReach(yaw);
                foreach (var (r, floor, what) in new[] { (OrbitalDefense.LampRadius, OrbitalDefense.LampSillTop, "등실 유리 아래 턱"), (OrbitalDefense.GalleryEdge, OrbitalDefense.GalleryY, "회랑 끝") })
                {
                    float s = r / Mathf.Cos(p);
                    float low = c.y - r * Mathf.Tan(p) - OrbitalDefense.BeamRadius(s, reach) / Mathf.Cos(p);
                    Assert.Greater(low, floor + 0.03f, $"yaw {yaw}: 빛줄기 아래 끝 {low:F2} m가 {what} ({floor:F2}) 밑으로 지나감");
                }
            }
        }

        /// <summary>
        /// 답하는 곳은 손잡이를 잡은 자리 (등실, 눈 = 바닥 + 1.6 m)에서 회랑 바닥 끝에 가리지 않음 — 폭포 · 오두막 등은 회랑 밑이라 잡은 채 볼 수 없었음.
        /// 그 곳을 비출 때 선 자리 (손잡이 왼쪽 RiderSide · RiderRadius)에서 곳까지 내려다보는 각 < 회랑 끝까지 내려다보는 각 − 2°.
        /// </summary>
        [Test]
        public void Lighthouse_TargetsSeenFromLamp()
        {
            var c = OrbitalDefense.LensCenter;
            foreach (var (name, at) in OrbitalDefense.BeamTargets)
            {
                float yaw = OrbitalDefense.BeamTargetYaw(at);
                var feet = new Vector2(c.x, c.z) + Rot(yaw + InteriorLighthouse.RiderSide) * InteriorLighthouse.RiderRadius;
                float eye = OrbitalDefense.GalleryY + FirstPersonController.EyeHeight;
                var to = new Vector2(at.x, at.z) - feet;
                float down = Mathf.Atan2(eye - at.y, to.magnitude) * Mathf.Rad2Deg;
                // 선 자리에서 곳 쪽으로 회랑 끝 원 (반지름 GalleryEdge)까지
                var d = to.normalized;
                var f = feet - new Vector2(c.x, c.z);
                float b = Vector2.Dot(f, d), q = f.sqrMagnitude - OrbitalDefense.GalleryEdge * OrbitalDefense.GalleryEdge;
                float edge = -b + Mathf.Sqrt(b * b - q);
                float deck = Mathf.Atan2(eye - OrbitalDefense.GalleryY, edge) * Mathf.Rad2Deg;
                Assert.Less(down, deck - 2f, $"{name}: 내려다보는 각 {down:F1}° — 회랑 끝 ({deck:F1}°)에 가림");
            }
        }

        private static Vector2 Rot(float yaw) => new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));

        /// <summary>
        /// 빛줄기 끝 (BeamReach): 어느 방향이든 끝 고리 (지름 BeamEndDiameter)가 하늘 면 (벽 · 아래 칸 천장 턱) 안 — 뒤쪽 벽을 비출 때 하늘 면에 잘린 끝이 보였음.
        /// 넓은 바다 쪽은 다 뻗음 (BeamLength), 벽 쪽은 짧아짐.
        /// </summary>
        [Test]
        public void Lighthouse_BeamStopsBeforeSky()
        {
            var c = OrbitalDefense.LensCenter;
            float shortest = OrbitalDefense.BeamLength, longest = 0f;
            for (int yaw = 0; yaw < 360; yaw++)
            {
                float reach = OrbitalDefense.BeamReach(yaw);
                Assert.LessOrEqual(reach, OrbitalDefense.BeamLength + 0.001f, $"yaw {yaw}: 가장 긴 길이보다 김");
                shortest = Mathf.Min(shortest, reach);
                longest = Mathf.Max(longest, reach);
                var dir = Quaternion.Euler(OrbitalDefense.BeamPitch, yaw, 0f) * Vector3.forward;
                var end = c + dir * reach;
                var x2 = Vector3.Cross(dir, Vector3.up).normalized;
                var y2 = Vector3.Cross(dir, x2);
                for (int a = 0; a < 16; a++)
                {
                    float ang = a * Mathf.PI / 8f;
                    var q = end + (x2 * Mathf.Cos(ang) + y2 * Mathf.Sin(ang)) * (OrbitalDefense.BeamEndDiameter / 2f);
                    bool inside = q.x > OrbitalDefense.RoomMinX && q.x < OrbitalDefense.RoomMaxX && q.z > OrbitalDefense.RoomMinZ && q.z < OrbitalDefense.RoomMaxZ
                                  && (q.x < OrbitalDefense.LowRoomFromX || q.y < OrbitalDefense.LowCeilingY);
                    Assert.IsTrue(inside, $"yaw {yaw}: 빛줄기 끝 고리 {q}가 하늘 면 밖");
                }
            }
            Assert.Less(shortest, OrbitalDefense.BeamLength - 2f, "벽 쪽에서도 줄지 않음");
            Assert.AreEqual(OrbitalDefense.BeamLength, longest, 0.001f, "어느 쪽으로도 다 뻗지 못함");
        }

        /// <summary>프리팹: 렌즈에 손잡이 구슬 (충돌체, 등실 바닥에서 손 닿는 높이) · 빛줄기 둘이 붙어 같이 돎 (스포트라이트 없음), 답하는 곳 여섯.</summary>
        [Test]
        public void Lighthouse_Wired()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var lh = root.GetComponentInChildren<InteriorLighthouse>(true);
                Assert.IsNotNull(lh, "등대 빛 체험 없음");
                var lens = root.transform.Find("INT_EvoOrbitalDefense_Lens");
                Assert.IsNotNull(lens);
                var knob = lens.Find("LensHandle");
                Assert.IsNotNull(knob, "손잡이 구슬이 렌즈 자식이 아님");
                Assert.IsNotNull(knob.GetComponent<SphereCollider>());
                float h = knob.position.y - OrbitalDefense.GalleryY;
                Assert.That(h, Is.InRange(0.4f, 1.2f), $"손잡이 높이 {h:F2} m (등실 바닥에서)");
                var beams = lens.Find("LensBeams");
                Assert.IsNotNull(beams, "빛줄기가 렌즈 자식이 아님");
                Assert.IsNotNull(beams.GetComponent<SpaceStation.Building.LightBeamFx>());
                Assert.AreEqual(0, beams.GetComponentsInChildren<Light>(true).Length, "스포트라이트는 렌즈 앞 회랑 바닥을 태움");
                Assert.AreEqual(6, OrbitalDefense.BeamTargets.Length);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------------- 미니게임 "별똥별 막기" ----------------

        /// <summary>판 순서: 앉음 → 3초 세기 → 60초 (별똥별이 갈수록 자주 · 빠르게) → 끝, 쏘기는 판 중에만 · 0.22초마다, 다시 Begin = 새 판.</summary>
        [Test]
        public void StarfallGame_Round()
        {
            var g = new StarfallGame(5);
            Assert.IsFalse(g.Step(0.1f), "쉬는 중 별똥별");
            g.Begin();
            Assert.IsFalse(g.TryFire(), "세는 중에 쏨");
            int spawned = 0, early = 0, late = 0, fired = 0;
            float t = 0f, firstSpeed = -1f, lastSpeed = 0f;
            while (g.Current != StarfallGame.Phase.Done && t < 70f)
            {
                if (g.Step(0.02f))
                {
                    spawned++;
                    if (g.Progress < 0.25f) early++;
                    if (g.Progress > 0.75f) late++;
                    if (firstSpeed < 0f) firstSpeed = g.Speed;
                    lastSpeed = g.Speed;
                }
                if (g.TryFire())
                    fired++;
                t += 0.02f;
            }
            Assert.AreEqual(StarfallGame.CountdownTime + StarfallGame.RoundTime, t, 0.1f, "판 길이");
            Assert.That(spawned, Is.InRange(45, 110), "별똥별 수");
            Assert.Greater(late, early, "갈수록 자주 떨어지지 않음");
            Assert.Greater(lastSpeed, firstSpeed + 1f, "갈수록 빨라지지 않음");
            Assert.That(fired, Is.InRange(Mathf.FloorToInt(StarfallGame.RoundTime / (StarfallGame.FireCooldown + 0.02f)) - 5, Mathf.CeilToInt(StarfallGame.RoundTime / StarfallGame.FireCooldown) + 5), "쏘는 간격");
            Assert.IsFalse(g.TryFire(), "끝난 뒤 쏨 (다시 = Begin)");
            g.Hit();
            g.Begin();
            Assert.AreEqual(0, g.Hits, "새 판에 맞힌 수가 남음");
        }

        /// <summary>
        /// 별똥별 길: 출발 = 바다 쪽 하늘 면 바로 밑 · 방 안 · 포수 눈에서 대각선 위 (25° 넘게 — 사용자 "너무 아래쪽에서 운석이 시작해"), 떨어질 자리 = 방 안 수면,
        /// 고른 길은 출발이 앉은 처음 화면 안쪽 · 길 앞 60%가 화면 좌우 안 (사용자 "시야 밖으로 떨어져 놓치기 쉬움"), 포수 몸을 지나지 않음,
        /// 한 번에 고를 확률이 충분해 실행 중 16번 다시 고르기 안에 거의 늘 나옴. 포수 눈은 포대 테 난간 손잡이보다 높음.
        /// </summary>
        [Test]
        public void Starfall_Paths()
        {
            var random = new System.Random(11);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            var eye = OrbitalDefense.GunEyePoint;
            Assert.Greater(eye.y - (OrbitalDefense.Floor + 0.12f + 1.05f), 0.25f, "포수 눈이 포대 테 난간 손잡이 (받침 윗면 + 1.05)에 걸려 화면 가운데를 가로막음");
            Assert.LessOrEqual(OrbitalDefense.GunPitch, -25f, "처음 시선이 대각선 위가 아님");
            int ok = 0;
            for (int i = 0; i < 300; i++)
            {
                var from = OrbitalDefense.StarfallStart(R(-OrbitalDefense.StarfallStartYaw, OrbitalDefense.StarfallStartYaw), R(OrbitalDefense.StarfallStartElevMin, OrbitalDefense.StarfallStartElevMax));
                var to = OrbitalDefense.StarfallLanding(R(-OrbitalDefense.StarfallLandYaw, OrbitalDefense.StarfallLandYaw), R(OrbitalDefense.StarfallLandNear, OrbitalDefense.StarfallLandFar));
                Assert.AreEqual(OrbitalDefense.LowCeilingY - 0.1f, from.y, 1e-4f, "출발이 하늘 면 바로 밑이 아님");
                Assert.AreEqual(OrbitalDefense.SeaLevel, to.y, 1e-4f);
                Assert.That(to.x, Is.InRange(OrbitalDefense.LowRoomFromX + 1.9f, OrbitalDefense.RoomMaxX - 1.9f));
                Assert.That(to.z, Is.InRange(OrbitalDefense.RoomMinZ + 1.9f, OrbitalDefense.RoomMaxZ - 1.9f));
                if ((from - to).magnitude < 3f || !OrbitalDefense.StarfallPathVisible(from, to))
                    continue;   // 실행 중에는 다시 고름 (16번까지)
                ok++;
                Assert.IsTrue(OrbitalDefense.InsideLowRoom(from), "방 밖에서 출발");
                for (int k = 0; k <= 10; k++)
                {
                    var p = Vector3.Lerp(from, to, k / 10f);
                    var d = p - eye;
                    float yaw = Mathf.DeltaAngle(OrbitalDefense.GunSeatYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                    float el = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                    if (k == 0)
                    {
                        Assert.Greater(el, 25f, $"출발이 낮음 ({el:F0}° — 대각선 위가 아님)");
                        Assert.Less(Mathf.Abs(el + OrbitalDefense.GunPitch), 28f, $"출발이 처음 화면 위아래 밖 ({el:F0}°)");
                    }
                    if (k <= 6)
                    {
                        Assert.Less(Mathf.Abs(yaw), 34f, $"길 앞쪽이 처음 화면 옆 밖 ({yaw:F0}°)");
                        Assert.Less(el + OrbitalDefense.GunPitch, 28f, $"길 앞쪽이 처음 화면 위 밖 ({el:F0}°)");
                    }
                    Assert.Greater(d.magnitude, 1f, "별똥별이 포수 몸을 지나감");
                }
            }
            float rate = ok / 300f;
            Assert.Greater(1f - Mathf.Pow(1f - rate, 16f), 0.995f, $"한 번에 고른 별똥별 {ok} / 300 — 16번 안에 못 고를 때가 잦음");
        }

        /// <summary>조준: 시선에 가까운 머리 중 가장 가까운 것을 고름, 뒤 · 멀리 빗나간 것은 안 고름.</summary>
        [Test]
        public void Starfall_PickStar()
        {
            var heads = new List<Vector3> { new Vector3(0.3f, 0f, 10f), new Vector3(0f, 0.2f, 5f), new Vector3(0f, 0f, -4f), new Vector3(3f, 0f, 6f) };
            Assert.AreEqual(1, InteriorStarfall.PickStar(Vector3.zero, Vector3.forward, heads, InteriorStarfall.AimRadius, InteriorStarfall.AimPerMeter), "가까운 것");
            Assert.AreEqual(-1, InteriorStarfall.PickStar(Vector3.zero, Vector3.forward, new List<Vector3> { new Vector3(0f, 0f, -4f) }, InteriorStarfall.AimRadius, InteriorStarfall.AimPerMeter), "뒤에 있는 것");
            Assert.AreEqual(-1, InteriorStarfall.PickStar(Vector3.zero, Vector3.forward, new List<Vector3> { new Vector3(3f, 0f, 6f) }, InteriorStarfall.AimRadius, InteriorStarfall.AimPerMeter), "멀리 빗나간 것");
        }

        /// <summary>프리팹: 포수 의자 충돌체 · 프리즘 광선 (굵기 2.5) · 포대 수정 무리 (원점 = 받침 윗면).</summary>
        [Test]
        public void Starfall_Wired()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                Assert.IsNotNull(root.GetComponentInChildren<InteriorStarfall>(true), "별똥별 막기 없음");
                var seat = root.transform.Find("GunSeatUse");
                Assert.IsNotNull(seat, "포수 의자 충돌체 없음");
                Assert.IsNotNull(seat.GetComponent<BoxCollider>());
                Assert.AreEqual(0f, new Vector2(seat.localPosition.x - OrbitalDefense.GunSeat.x, seat.localPosition.z - OrbitalDefense.GunSeat.z).magnitude, 0.1f);
                var beam = root.transform.Find("StarfallBeam");
                Assert.IsNotNull(beam);
                var so = new SerializedObject(beam.GetComponent<SpaceStation.Building.PrismBeamFx>());
                Assert.AreEqual(2.5f, so.FindProperty("_width").floatValue, 1e-4f);
                Assert.IsNotNull(so.FindProperty("_lineMaterial").objectReferenceValue);
                var turret = root.transform.Find("INT_EvoOrbitalDefense_Turret");
                Assert.IsNotNull(turret);
                Assert.AreEqual(OrbitalDefense.Turret.x, turret.localPosition.x, 0.02f);
                Assert.Less(OrbitalDefense.TurretMuzzle.y - turret.localPosition.y, 2.2f, "포 끝이 수정 무리보다 높음");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------------- 체험 "오로라 보호막 펼치기" ----------------

        /// <summary>
        /// 보호막 (바다 위 납작한 반구)이 방 안에 들어감: 꼭대기는 바다 쪽 아래 칸 천장보다 1 m 넘게 낮음 (별똥별이 하늘 면에서 떨어질 틈) · 발은 벽 안,
        /// 레버 · 레버 앞에 선 사람 · 선베드에 누운 눈은 막 밖, 포대 수정 · 먼 부표 · 매인 배는 막 안 (덮어 지킴).
        /// </summary>
        [Test]
        public void AuroraShield_FitsRoom()
        {
            var c = OrbitalDefense.ShieldCenter;
            var r = OrbitalDefense.ShieldRadii;
            Assert.Less(c.y + r.y, OrbitalDefense.LowCeilingY - 1f, "보호막 꼭대기가 천장에 붙음");
            Assert.Greater(c.x - r.x, OrbitalDefense.RoomMinX);
            Assert.Less(c.x + r.x, OrbitalDefense.RoomMaxX);
            Assert.Greater(c.z - r.z, OrbitalDefense.RoomMinZ + 1f, "남쪽 발이 벽에 붙음 (선베드 자리)");
            Assert.Less(c.z + r.z, OrbitalDefense.RoomMaxZ - 1f, "북쪽 발이 벽에 붙음 (별똥별 틈)");
            Assert.IsFalse(OrbitalDefense.InsideShield(OrbitalDefense.AuroraLever), "레버가 막 안");
            Assert.IsFalse(OrbitalDefense.InsideShield(OrbitalDefense.AuroraLever + new Vector3(0.6f, 0.6f, -0.6f)), "레버 앞에 선 사람 머리가 막 안");
            foreach (float x in OrbitalDefense.Loungers)
            {
                var eye = new Vector3(x, OrbitalDefense.LoungerSeatY + OrbitalDefense.LoungerEye, OrbitalDefense.LoungerZ - 0.3f);
                Assert.IsFalse(OrbitalDefense.InsideShield(eye), $"선베드 {x} 눈이 막 안");
            }
            for (int i = 0; i < 3; i++)
                Assert.IsTrue(OrbitalDefense.InsideShield(OrbitalDefense.BeamTargets[i].At), $"{OrbitalDefense.BeamTargets[i].Name}가 막 밖");
            Assert.AreEqual(1f, Vector3.Dot(OrbitalDefense.ShieldNormal(OrbitalDefense.ShieldPoint(Vector3.up)), Vector3.up), 1e-4f, "꼭대기 법선");
        }

        /// <summary>별똥별 길: 하늘 면 (천장 · 벽) 안에서 출발해 막 밖을 지나 닿는 자리에 떨어짐, 대부분 1.2 m 넘게 보임 (짧으면 다시 고름).</summary>
        [Test]
        public void AuroraShield_MeteorPaths()
        {
            var random = new System.Random(7);
            float R(float a, float b) => a + (float)random.NextDouble() * (b - a);
            int tried = 0, long_ = 0;
            while (tried < 300)
            {
                float lat = R(25f, 68f) * Mathf.Deg2Rad, lon = R(0f, 2f * Mathf.PI);
                var unit = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                if (unit.z > 0.35f)
                    continue;
                tried++;
                var to = OrbitalDefense.ShieldPoint(unit);
                var from = OrbitalDefense.MeteorStart(to, new Vector3(R(-0.5f, 0.5f), 1f, R(0.2f, 0.8f)), 4.5f);
                Assert.LessOrEqual(from.y, OrbitalDefense.LowCeilingY - 0.09f, "천장 밖에서 출발");
                Assert.LessOrEqual(from.z, OrbitalDefense.RoomMaxZ - 0.09f, "북쪽 벽 밖에서 출발");
                Assert.That(from.x, Is.InRange(OrbitalDefense.LowRoomFromX + 0.09f, OrbitalDefense.RoomMaxX - 0.09f), "옆 벽 밖에서 출발");
                if ((from - to).magnitude < 1.2f)
                    continue;
                long_++;
                for (int k = 1; k < 10; k++)
                    Assert.IsFalse(OrbitalDefense.InsideShield(Vector3.Lerp(from, to, k / 10f)), "별똥별이 막 안을 지나감");
            }
            Assert.Greater(long_, tried * 0.7f, $"보이는 길이 별똥별 {long_} / {tried}");
        }

        /// <summary>쇼 순서: 당김 → 펼침 → 버팀 (별똥별 1.6 ~ 3.2초마다) → 접힘 → 레버 돌아옴 → 쉼, 도는 중에는 다시 당겨지지 않음.</summary>
        [Test]
        public void AuroraShieldShow_Sequence()
        {
            var show = new AuroraShieldShow(3);
            Assert.IsFalse(show.Step(0.1f), "쉬는 중 별똥별");
            Assert.IsTrue(show.Start());
            Assert.IsFalse(show.Start(), "도는 중 다시 당겨짐");
            int meteors = 0;
            float t = 0f, total = AuroraShieldShow.PullTime + AuroraShieldShow.UnfoldTime + AuroraShieldShow.HoldTime + AuroraShieldShow.FoldTime + AuroraShieldShow.ReturnTime;
            bool leverDown = false, covered = false;
            while (show.Running && t < total + 1f)
            {
                if (show.Step(0.02f))
                    meteors++;
                t += 0.02f;
                leverDown |= show.Lever > 0.999f;
                covered |= show.Spread > 0.999f && show.Alpha > 0.999f;
            }
            Assert.IsFalse(show.Running, "쇼가 끝나지 않음");
            Assert.AreEqual(total, t, 0.1f);
            Assert.IsTrue(leverDown && covered, "레버 · 막이 다 펼쳐지지 않음");
            Assert.AreEqual(0f, show.Lever);
            Assert.AreEqual(0f, show.Alpha);
            Assert.That(meteors, Is.InRange(Mathf.FloorToInt(AuroraShieldShow.HoldTime / AuroraShieldShow.MeteorGapMax), Mathf.CeilToInt((AuroraShieldShow.HoldTime + AuroraShieldShow.MeteorLead) / AuroraShieldShow.MeteorGapMin) + 1));
            Assert.IsTrue(show.Start(), "끝난 뒤 다시 당겨지지 않음");
        }

        /// <summary>프리팹: 레버에 손잡이 충돌체 (레버 자식 — 같이 기울어짐), 보호막 · 수정 결정 막 (실제 시간) · 선베드 눕기.</summary>
        [Test]
        public void AuroraShield_Wired()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                Assert.IsNotNull(root.GetComponentInChildren<InteriorAuroraShield>(true), "보호막 체험 없음");
                var lever = root.transform.Find("INT_EvoOrbitalDefense_AuroraLever");
                Assert.IsNotNull(lever);
                Assert.AreEqual(OrbitalDefense.AuroraLever.x, lever.localPosition.x, 0.02f, "레버 원점 = 굴대");
                var grip = lever.Find("LeverGrip");
                Assert.IsNotNull(grip, "손잡이 충돌체가 레버 자식이 아님");
                Assert.IsNotNull(grip.GetComponent<BoxCollider>());
                var crystal = root.GetComponentInChildren<SpaceStation.Building.CrystalBarrierFx>(true);
                Assert.IsNotNull(crystal, "수정 결정 막 없음");
                var so = new SerializedObject(crystal);
                Assert.IsTrue(so.FindProperty("_unscaled").boolValue, "수정 결정 막이 게임 시간 (멈추면 멈춤)");
                Assert.IsNotNull(so.FindProperty("_material").objectReferenceValue);
                Assert.GreaterOrEqual(root.GetComponents<InteriorBenchSeats>().Length, 1, "선베드 눕기 없음");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 선베드 셋 = 누운 자리 (앉은 채 눕고 옆으로 일어섬 — 앞으로 일어서면 선베드 앞 길을 막았음, 사용자 2026-11-13):
        /// 누운 자리 수 = 선베드 수, 선베드 양옆 0.7 m에 서는 몸 (반지름 0.24)이 들어가는 빈 모래 (탁자 · 파라솔은 등받이 사이).
        /// </summary>
        [Test]
        public void Loungers_LieAndStandUpSideways()
        {
            var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>("Assets/_Project/Data/Interior/IT_EvoOrbitalDefense.asset");
            var root = Spawn(out bool ok);
            if (!ok || template == null)
            {
                if (root != null)
                    Object.DestroyImmediate(root);
                Assert.Ignore("템플릿 없음 (Build Evolved Templates 먼저)");
            }
            try
            {
                int lie = 0;
                foreach (var spot in template.ResidentSpots)
                {
                    if (!spot.Lie)
                        continue;
                    lie++;
                    Assert.AreEqual(SpaceStation.Data.ResidentPose.Sit, spot.Pose, $"누운 자리는 앉는 자리 {spot.Position}");
                    // 누운 자리 = 방석 윗면 (의자 높이 0.45로 두었더니 누운 몸이 8.5 cm 떠 있었음, 사용자 2026-11-13)
                    Assert.IsTrue(Physics.Raycast(spot.Position + Vector3.up * 0.3f, Vector3.down, out var seat, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"선베드 방석 없음 {spot.Position}");
                    Assert.Less(Mathf.Abs(seat.point.y - spot.Position.y), 0.02f, $"누운 자리 높이 ≠ 방석 윗면 {spot.Position} → {seat.point.y:F3}");
                    int side = 0;
                    foreach (float e in new[] { -0.7f, 0.7f })
                    {
                        var p = new Vector3(spot.Position.x + e, F, spot.Position.z);
                        if (!Physics.CheckCapsule(p + Vector3.up * 0.3f, p + Vector3.up * 1.1f, 0.24f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            side++;
                    }
                    Assert.Greater(side, 0, $"선베드 {spot.Position} 양옆이 막혀 옆으로 못 일어남");
                }
                Assert.AreEqual(OrbitalDefense.Loungers.Length, lie, "누운 자리 수 = 선베드 수");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------------- 바다 동물 ----------------

        /// <summary>
        /// 바다 동물 묶음마다 수 · 움직임 · 앞쪽: 땅 · 바위 동물은 몸 밑 닿는 점이 면에서 1.5 cm 안 (97 %, 가장 큰 틈 3 cm 미만 · 너무 깊이 묻히지 않음), 모델 앞쪽 = 바라보는 쪽, 게는 물가를 따라 (x) 양쪽으로 옆걸음 ·
        /// 그 길 모래 높이 차 1.5 cm 안, 돌고래는 뛰는 길 (± 1.3 m, 수면 ~ + 1 m)에 막는 것이 없고 밑이 1.5 m 넘게 깊음, 거북은 막히지 않는 헤엄 고리를 고름.
        /// </summary>
        [Test]
        public void Critters_OnTheirSpots()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("템플릿 없음 (Build Evolved Templates 먼저)");
            try
            {
                var groups = new (string Holder, (string Kind, Vector3 At, float Facing)[] Spots, SpaceStation.Building.CritterFx.Kind Kind, bool Land)[]
                {
                    ("Critters_Seal", OrbitalDefense.Seals, SpaceStation.Building.CritterFx.Kind.Bask, true),
                    ("Critters_Crab", OrbitalDefense.Crabs, SpaceStation.Building.CritterFx.Kind.Scuttle, true),
                    ("Critters_Dolphin", OrbitalDefense.Dolphins, SpaceStation.Building.CritterFx.Kind.Leap, false),
                    ("Critters_Turtle", OrbitalDefense.Turtles, SpaceStation.Building.CritterFx.Kind.Drift, false),
                    ("Critters_Puffin", OrbitalDefense.Puffins, SpaceStation.Building.CritterFx.Kind.Perch, true),
                    ("Critters_LedgePuffin", OrbitalDefense.LedgePuffins, SpaceStation.Building.CritterFx.Kind.Bask, true),
                };
                foreach (var (holderName, spots, kind, land) in groups)
                {
                    var holder = root.transform.Find(holderName);
                    Assert.IsNotNull(holder, holderName);
                    var fx = holder.GetComponent<SpaceStation.Building.CritterFx>();
                    Assert.IsNotNull(fx, holderName);
                    var arr = new SerializedObject(fx).FindProperty("_critters");
                    Assert.AreEqual(spots.Length, arr.arraySize, $"{holderName} 수");
                    float scale = holder.localScale.x;
                    for (int i = 0; i < spots.Length; i++)
                    {
                        var (name, at, facing) = spots[i];
                        var e = arr.GetArrayElementAtIndex(i);
                        var part = (Transform)e.FindPropertyRelative("Part").objectReferenceValue;
                        Assert.AreEqual("Critter_" + name, part.name);
                        Assert.AreEqual((int)kind, e.FindPropertyRelative("Kind").enumValueIndex, $"{holderName} {i} 움직임");
                        Assert.Less((holder.TransformPoint(part.localPosition) - root.transform.TransformPoint(at)).magnitude, 0.001f, $"{holderName} {i} 자리");
                        var face = new Vector3(Mathf.Cos(facing * Mathf.Deg2Rad), 0f, Mathf.Sin(facing * Mathf.Deg2Rad));
                        var front = part.localRotation * Vector3.left;      // 모델 앞 = Blender +x = Unity −x
                        Assert.Greater(Vector3.Dot(new Vector3(front.x, 0f, front.z).normalized, face), 0.995f, $"{holderName} {i} 앞쪽");
                        var heading = e.FindPropertyRelative("Heading").vector3Value;
                        if (kind == SpaceStation.Building.CritterFx.Kind.Scuttle)
                            Assert.Greater(Mathf.Abs(heading.x), 0.95f, $"{holderName} {i} 옆걸음 축이 물가 (x)를 따라야 함 {heading}");
                        else if (kind != SpaceStation.Building.CritterFx.Kind.Perch && holderName != "Critters_LedgePuffin")
                            Assert.Greater(Vector3.Dot(heading, face), 0.9f, $"{holderName} {i} 머리 쪽 (움직임 앞) {heading}");
                        if (land)
                        {
                            // 몸 밑 닿는 점 (아래를 보는 면 — 물범 = 원점 위 3.5 cm 안 배 · 지느러미 · 엉덩이, 새 · 게 = 1 cm 안 발바닥 · 다리 끝)이 모두 면 가까이:
                            //  원점 한 점만 맞추면 둥근 바위 위 물범 배 · 꼬리가 최대 11 cm 떠 있었음 (사용자 2026-11-13)
                            bool seal = name.StartsWith("Seal");
                            float band = seal ? 0.035f : 0.01f, deepest = name == "Seal" ? 0.07f : seal ? 0.04f : 0.015f;
                            var mesh = part.GetComponent<MeshFilter>().sharedMesh;
                            var vs = mesh.vertices;
                            var ns = mesh.normals;
                            int contact = 0, floating = 0;
                            float worst = 0f, buried = 0f;
                            for (int v = 0; v < vs.Length; v++)
                            {
                                var w = part.TransformPoint(vs[v]);
                                if (w.y - part.position.y > band || part.TransformDirection(ns[v]).y > -0.5f)
                                    continue;
                                Assert.IsTrue(Physics.Raycast(w + Vector3.up * 0.3f, Vector3.down, out var hit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"{holderName} {i} 발밑 없음 {w}");
                                float gap = w.y - hit.point.y;
                                contact++;
                                if (gap > 0.015f)
                                    floating++;
                                worst = Mathf.Max(worst, gap);
                                buried = Mathf.Min(buried, gap);
                            }
                            Assert.Greater(contact, 8, $"{holderName} {i} 닿는 점");
                            Assert.LessOrEqual(floating, contact * 0.03f, $"{holderName} {i} 몸 밑이 1.5 cm 넘게 뜬 점 {floating}/{contact} (가장 큰 틈 {worst:F3})");
                            Assert.Less(worst, 0.03f, $"{holderName} {i} 가장 큰 틈 {worst:F3}");
                            Assert.Greater(buried, -deepest, $"{holderName} {i} 바위 · 모래 속에 너무 깊이 묻힘 {buried:F3}");
                        }
                        if (kind == SpaceStation.Building.CritterFx.Kind.Scuttle)
                        {
                            Assert.AreEqual(2f, e.FindPropertyRelative("Side").floatValue, $"{holderName} {i} 양쪽 옆걸음");
                            float reach = SpaceStation.Building.CritterFx.ScuttleReach * scale;
                            foreach (float s in new[] { -1f, 1f })
                            {
                                var p = at + heading.normalized * (reach * s);
                                Assert.IsTrue(Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var hit, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                                Assert.Less(Mathf.Abs(hit.point.y - at.y - 0.005f), 0.015f, $"{holderName} {i} 옆걸음 끝 모래 높이 {hit.point.y:F3}");
                            }
                        }
                        if (kind == SpaceStation.Building.CritterFx.Kind.Leap)
                        {
                            var a = at - face * 1.3f;
                            var b = at + face * 1.3f;
                            foreach (var c in Physics.OverlapCapsule(new Vector3(a.x, OrbitalDefense.SeaLevel + 0.35f, a.z), new Vector3(b.x, OrbitalDefense.SeaLevel + 0.35f, b.z), 0.35f,
                                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                                Assert.IsTrue(c.name.EndsWith("_LakeHit"), $"{holderName} {i} 뛰는 길이 막힘 ({c.name})");   // 바다에 못 들어가는 보이지 않는 벽만 빼고
                            foreach (var p in new[] { a, at, b })
                            {
                                Assert.IsTrue(Physics.Raycast(new Vector3(p.x, OrbitalDefense.SeaLevel, p.z), Vector3.down, out var bed, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                                Assert.Greater(OrbitalDefense.SeaLevel - bed.point.y, 1.5f, $"{holderName} {i} 바다 깊이 {p}");
                            }
                        }
                        if (kind == SpaceStation.Building.CritterFx.Kind.Drift)
                            Assert.AreNotEqual(0f, e.FindPropertyRelative("Side").floatValue, $"{holderName} {i} 헤엄 고리가 다 막힘");
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
