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
    }
}
