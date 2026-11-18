using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 진화 4단계 심우주 통신기지 (Blender ds4_builder와 같은 값): 연결점 (외형 연결점 면과 같음 — 문 여덟 · 아래 해치 셋, 위 연결점 셋은 비워 둠) ·
    /// 걸어서 닿음 (해치 · 문 · 접시 조작반 · 가운데 관측실 조종대 · 아치 밑 · 광장 앞 · 주민 자리) · 해치 뚜껑이 묻히지 않음 · 움직이는 조각 원점 ·
    /// 체험 둘 · 미니게임 둘 연결 (서는 자리에서 시선이 쓰는 자리에 닿음 · 빛줄 · 고리 · 별똥별이 하늘 면 아래 방 안) · 동물 발이 땅에 닿음.
    /// </summary>
    public class DeepSpaceCommTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interior/PF_Interior_EvoDeepSpaceComm.prefab";
        private const string TemplatePath = "Assets/_Project/Data/Interior/IT_EvoDeepSpaceComm.asset";
        private const string N = "INT_EvoDeepSpaceComm_";
        private const float F = DeepSpaceComm.Floor;
        private const float X0 = -3.95f, X1 = 19.95f, Z0 = -3.95f, Z1 = 3.95f;

        [Test]
        public void Sockets_MatchExterior()
        {
            // 외형 MD_Evo_DeepSpaceComm 연결점 (면 깊이 ≥ 0인 바깥 면): 칸마다 앞 · 뒤 · 아래 + 양 끝 + 위 셋 — 위 = 빛 기둥 승강판 자리 (진화 단계별 통로 ③, 지금은 풀 · 동물 · 판석 길 문 곁이라 자리 못 정함 — InteriorSkyLiftTests.KnownGaps)
            var module = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.ModuleData>("Assets/_Project/Data/Modules/Evolved/MD_Evo_DeepSpaceComm.asset");
            Assert.IsNotNull(module);
            var cells = new HashSet<Vector3Int>(module.CellOffsets);
            var expected = new HashSet<(Vector3Int, Vector3Int)>();
            int ups = 0;
            foreach (var c in module.CellOffsets)
                foreach (var d in new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) })
                    if (!cells.Contains(c + d) && module.TryGetFaceDepth(c, d, out _) && !module.IsClosedFace(c, d))
                    {
                        if (d == Vector3Int.up)
                            ups++;
                        else
                            expected.Add((c, d));
                    }
            Assert.AreEqual(3, ups, "외형 위 연결점 (내부 = 빛 기둥 승강판)");
            var actual = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var (cell, off, floorY) in DeepSpaceComm.Hatches)
            {
                Assert.IsTrue(actual.Add((cell, Vector3Int.down)), $"해치 칸 겹침 {cell}");
                Assert.Less(Mathf.Abs(off.x), 3.05f, $"{cell}");
                Assert.Less(Mathf.Abs(off.y), 3.05f, $"{cell}");
            }
            foreach (var (cell, dir) in DeepSpaceComm.Doors)
                Assert.IsTrue(actual.Add((cell, dir)));
            Assert.IsTrue(expected.SetEquals(actual), $"외형 연결점 {expected.Count}곳 ↔ 내부 {actual.Count}곳");
            var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>(TemplatePath);
            if (template != null)
            {
                int plain = 0, lifts = 0;
                foreach (var s in template.Sockets)
                {
                    if (s.SkyLift)
                        lifts++;
                    else
                        plain++;
                }
                Assert.AreEqual(actual.Count, plain, "템플릿 연결점 수 (승강판 빼고)");
                Assert.LessOrEqual(lifts, ups, "승강판은 외형 위 연결점에만 (자리 못 정한 곳 = InteriorSkyLiftTests.KnownGaps)");
            }
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
                col.enabled = col is MeshCollider && col.name.EndsWith("_Shell");
            Physics.SyncTransforms();
            ok = true;
            return root;
        }

        private static void Enable(GameObject root, params string[] names)
        {
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
                foreach (var n in names)
                    if (col.name.StartsWith(n))
                        col.enabled = true;
            Physics.SyncTransforms();
        }

        [Test]
        public void Walk_ReachesEverything()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                const float step = 0.2f, x0 = -3.9f, z0 = -3.9f, top = 3.9f;
                int nx = Mathf.RoundToInt(23.8f / step) + 1, nz = Mathf.RoundToInt(7.8f / step) + 1;
                var nodes = new List<Vector3>();
                var cols = new List<int>[nx, nz];
                for (int i = 0; i < nx; i++)
                {
                    for (int j = 0; j < nz; j++)
                    {
                        var origin = new Vector3(x0 + i * step, top, z0 + j * step);
                        for (int guard = 0; guard < 24 && Physics.Raycast(origin, Vector3.down, out var hit, origin.y - F + 3f, Physics.AllLayers, QueryTriggerInteraction.Ignore); guard++)
                        {
                            origin = hit.point + Vector3.down * 0.01f;
                            if (hit.normal.y < 0.7f)
                                continue;
                            var p = hit.point;
                            if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.28f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
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
                var start = Node(DeepSpaceComm.Spawn);
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
                var fails = new List<string>();
                void Reach(Vector3 p, string what)
                {
                    int k = Node(p);
                    if (k < 0)
                        fails.Add($"{what}: 설 곳 없음 {p}");
                    else if (!seen[k])
                        fails.Add($"{what}: 걸어서 못 감 {p}");
                }
                foreach (var (cell, off, floorY) in DeepSpaceComm.Hatches)
                {
                    var h = new Vector3(cell.x * 8f + off.x, floorY, cell.z * 8f + off.y);
                    Reach(h, $"해치 {cell}");
                    if (Physics.CheckBox(h + Vector3.up * 0.1f, new Vector3(0.7f, 0.04f, 0.7f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        fails.Add($"해치 {cell} 뚜껑이 묻힘");
                }
                foreach (var (cell, dir) in DeepSpaceComm.Doors)
                    Reach(new Vector3(cell.x * 8f, F, cell.z * 8f) + (Vector3)dir * (DeepSpaceComm.DoorDepth - 0.5f), $"문 {cell} {dir}");
                for (int k = 0; k < DeepSpaceComm.DishStands.Length; k++)
                    Reach(DeepSpaceComm.DishStands[k].At, $"접시 {k} 조작반 앞");
                Reach(DeepSpaceComm.ConsoleStand, "가운데 관측실 조종대 앞 (신호 맞추기)");
                Reach(DeepSpaceComm.WishStand, "별똥별 아치 밑 (소원)");
                Reach(DeepSpaceComm.CatchStand, "별자리 광장 앞 (별똥별 잡기)");
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
                            sp.y -= 0.5f;
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

        /// <summary>주민 자리 발밑에 바닥 (± 0.12, 앉는 자리 ± 0.5)이 있고 서는 몸 (반지름 0.24)이 들어감, 자리 15.</summary>
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
                Assert.GreaterOrEqual(template.ResidentSpots.Count, 12);   // 칸마다 다섯 − 승강판 착지 원판 옆 셋 (2026-11-17 — 주민이 덜 붐비게)
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

        /// <summary>움직이는 조각 (체험 · 미니게임이 쓰는 자리)이 따로 있고 원점이 값과 같음: 접시 받침 · 접시 · 손바퀴 넷 · 천체 모형 · 강 위 별 셋 · 케이블 넷 · 강.</summary>
        [Test]
        public void Movers_AtTheirSpots()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            void At(string name, Vector3 p)
            {
                var t = prefab.transform.Find(N + name);
                Assert.IsNotNull(t, $"{name} 없음");
                Assert.Less(Vector3.Distance(t.localPosition, p), 0.01f, $"{name} 원점 {t.localPosition} ≠ {p}");
            }
            for (int k = 0; k < DeepSpaceComm.Dishes.Length; k++)
            {
                At("DishYaw" + k, DeepSpaceComm.DishYawPivots[k]);
                At("DishTilt" + k, DeepSpaceComm.DishTiltPivots[k]);
                At("DishWheel" + k, DeepSpaceComm.DishWheels[k]);
                At("Cable" + k, Vector3.zero);
            }
            At("Orrery", DeepSpaceComm.Orrery);
            for (int k = 0; k < DeepSpaceComm.FloatingStars.Length; k++)
                At("StarFloat" + k, DeepSpaceComm.FloatingStars[k]);
            At("River", Vector3.zero);
        }

        /// <summary>
        /// 체험 둘 · 미니게임 둘이 붙어 있고, 서는 자리 눈 (1.6 m)에서 쓰는 자리를 보면 시선이 2.6 m 안에서 그 충돌체에 먼저 닿음:
        /// 접시 조작반 넷 → 손바퀴, 아치 앞 → 꼭대기 별, 조종대 앞 → 책상, 광장 앞 → 천체 모형. 강 별빛 번짐 자리가 강 안.
        /// </summary>
        [Test]
        public void Experiences_Wired()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                Assert.IsNotNull(root.GetComponent<InteriorDishSignal>(), "접시 신호 모으기 없음");
                var wish = root.GetComponent<InteriorWishArch>();
                Assert.IsNotNull(wish, "별똥별 아치 소원 없음");
                Assert.IsNotNull(root.GetComponent<InteriorSignalTune>(), "신호 맞추기 없음");
                Assert.IsNotNull(root.GetComponent<InteriorStarCatch>(), "별똥별 잡기 없음");
                var so = new SerializedObject(wish);
                float u = so.FindProperty("_wishU").floatValue, length = so.FindProperty("_riverLength").floatValue;
                Assert.Greater(length, 20f, "강 길이 (UV u)");
                Assert.IsTrue(u > 1f && u < length - 1f, $"별빛 번짐 자리 u {u} / {length}");
                Assert.IsNotNull(so.FindProperty("_river").objectReferenceValue, "강 렌더러");
                Enable(root, "DishWheelUse", "WishArchUse", "SignalTuneUse", "StarCatchUse");
                void Look(Vector3 feet, string target, string what)
                {
                    var eye = feet + Vector3.up * 1.6f;
                    var col = root.transform.Find(target).GetComponent<Collider>();
                    var aim = col.bounds.center;
                    Assert.IsTrue(InteriorMode.LookHit(eye, (aim - eye).normalized, 2.6f, out var hit), $"{what}: 시선이 아무것도 못 맞힘");
                    Assert.AreEqual(target, hit.collider.name, $"{what}: 시선이 {hit.collider.name}에 먼저 닿음");
                }
                for (int k = 0; k < DeepSpaceComm.DishStands.Length; k++)
                    Look(DeepSpaceComm.DishStands[k].At, "DishWheelUse" + k, $"접시 {k} 조작반 앞");
                Look(DeepSpaceComm.WishStand + new Vector3(0f, 0f, -0.6f), "WishArchUse", "아치 앞");
                Look(DeepSpaceComm.ConsoleStand, "SignalTuneUse", "조종대 앞");
                Look(DeepSpaceComm.CatchStand, "StarCatchUse", "광장 앞");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 연출 길이 하늘 면 (3.95) 아래 · 방 안 · 몸체 밖: 신호 별 · 옆 돔 → 가운데 돔 신호 빛줄 (휜 길) · 돔 위 신호 고리 (가장 넓고 높을 때) · 소원 별똥별 ·
        /// 별똥별 잡기 하늘 상자 모서리 · 접시 흐트러진 자세에서 접시 테가 땅 위 1 m 넘게.
        /// </summary>
        [Test]
        public void Shows_StayInTheSky()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var fails = new List<string>();
                void Free(Vector3 p, float r, string what)
                {
                    if (p.y + r > DeepSpaceComm.CeilingY - 0.05f)
                        fails.Add($"{what}: 하늘 면에 닿음 {p}");
                    if (p.x - r < X0 || p.x + r > X1 || p.z - r < Z0 || p.z + r > Z1)
                        fails.Add($"{what}: 방 밖 {p}");
                    if (Physics.CheckSphere(p, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        fails.Add($"{what}: 몸체 속 {p}");
                }
                Free(DeepSpaceComm.SignalStar, 0.2f, "신호 별");
                for (int d = 0; d < DeepSpaceComm.SideDomes.Length; d++)
                {
                    var from = DeepSpaceComm.SideDomeTop(d) + Vector3.up * 0.15f;
                    var to = DeepSpaceComm.CenterDomeTop + Vector3.up * 0.1f;
                    var mid = (from + to) * 0.5f + Vector3.up * 1.7f;
                    for (float u = 0.08f; u <= 0.92f; u += 0.04f)
                        Free(Vector3.Lerp(Vector3.Lerp(from, mid, u), Vector3.Lerp(mid, to, u), u), 0.05f, $"신호 빛줄 {d} ({u:F2})");
                }
                var ring = DeepSpaceComm.CenterDomeTop + Vector3.up * InteriorDishSignal.RingRise;
                for (int a = 0; a < 16; a++)
                    Free(ring + new Vector3(Mathf.Cos(a * Mathf.PI / 8f), 0f, Mathf.Sin(a * Mathf.PI / 8f)) * 2.6f, 0.05f, "신호 고리 (가장 넓을 때)");
                for (float u = 0f; u <= 0.85f; u += 0.05f)
                    Free(Vector3.Lerp(DeepSpaceComm.WishStarFrom, DeepSpaceComm.WishStarTo, u), 0.1f, $"소원 별똥별 ({u:F2})");
                foreach (float x in new[] { DeepSpaceComm.CatchSpawnX.x, DeepSpaceComm.CatchSpawnX.y })
                    foreach (float y in new[] { DeepSpaceComm.CatchSpawnY.x, DeepSpaceComm.CatchSpawnY.y })
                        foreach (float z in new[] { DeepSpaceComm.CatchSpawnZ.x, DeepSpaceComm.CatchSpawnZ.y })
                            Free(new Vector3(x, y, z), InteriorStarCatch.BigSize, "별똥별 잡기 하늘 상자 모서리");
                Assert.IsEmpty(fails, string.Join(" / ", fails));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>동물 (아기 사슴 · 토끼 넷 · 고슴도치 둘 · 발코니 부엉이 둘): 발 넷 (모델 발 자리를 바라보는 쪽으로 돌림)마다 발바닥이 땅 · 턱 면에서 −1.5 ~ +1.2 cm (떠 있거나 깊이 묻히지 않음).</summary>
        [Test]
        public void Critters_OnTheirSpots()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var fails = new List<string>();
                // 모델 발 자리 (앞뒤 반 길이 · 옆 반 폭 · 발바닥 높이 — critter_builder 모델, 머리 = 모델 −x)
                //  (부엉이 = 발가락이 다리 앞 0.04 ~ 0.08 m라 원점 둘레 ±0.04 — 평평한 턱이라 같음)
                var feet = new Dictionary<string, (float Along, float Side, float Sole)>
                {
                    { "Fawn", (0.19f, 0.08f, -0.005f) }, { "Bunny", (0.16f, 0.11f, -0.014f) }, { "Hedgehog", (0.085f, 0.052f, 0f) }, { "Owl", (0.04f, 0.04f, 0f) },
                };
                var all = new List<(string Kind, Vector3 At, float Facing)>();
                all.AddRange(DeepSpaceComm.Fawns);
                all.AddRange(DeepSpaceComm.Bunnies);
                all.AddRange(DeepSpaceComm.Hedgehogs);
                all.AddRange(DeepSpaceComm.Owls);
                foreach (var (kind, at, facing) in all)
                {
                    var (along, side, sole) = feet[kind];
                    float a = facing * Mathf.Deg2Rad;
                    var head = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var lateral = new Vector3(-head.z, 0f, head.x);
                    foreach (float u in new[] { -1f, 1f })
                        foreach (float v in new[] { -1f, 1f })
                        {
                            var p = at + head * (u * along) + lateral * (v * side);
                            if (!Physics.Raycast(p + Vector3.up * 0.4f, Vector3.down, out var hit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            {
                                fails.Add($"{kind} {at}: 발밑 땅 없음");
                                continue;
                            }
                            float gap = at.y + sole - hit.point.y;
                            if (gap > 0.012f || gap < -0.015f)
                                fails.Add($"{kind} {at}: 발 ({u}, {v}) 땅에서 {gap * 100f:F1} cm");
                        }
                }
                Assert.IsEmpty(fails, string.Join(" / ", fails));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 발코니 부엉이: 몸 (턱 위 0.11 ~ 0.3 m 축 · 반지름 0.1, 원점보다 2 cm 뒤)이 위 난간 · 기둥 · 돔 벽 충돌체에 닿지 않음 —
        /// 고개 들기 (Bask 9° 뒤로) · 두리번 (±10°)에도 난간까지 1.5 cm 넘게 남는 자리 (Blender 시험 배치: 1.865 m · 1배는 가슴이 난간에 1.5 cm 묻혔음).
        /// </summary>
        [Test]
        public void Owls_ClearOfRailAndDome()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var fails = new List<string>();
                foreach (var (_, at, facing) in DeepSpaceComm.Owls)
                {
                    float a = facing * Mathf.Deg2Rad;
                    var c = at - new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.02f;
                    var hits = Physics.OverlapCapsule(c + Vector3.up * 0.11f, c + Vector3.up * 0.3f, 0.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    foreach (var h in hits)
                        fails.Add($"부엉이 {at}: 몸이 {h.name}에 닿음");
                }
                Assert.IsEmpty(fails, string.Join(" / ", fails));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
