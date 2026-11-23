using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 진화 3단계 AI 중추 (Blender ai3_builder와 같은 값): 연결점 (외형 연결점 면과 같음 — 옆문 하나 · 아래 해치 일곱) ·
    /// 걸어서 닿음 (해치 · 문 · 장미 문 · 기록판 · 미로 빛 구슬 자리 다섯 · 정자 · 책 받침 · 시냅스 서는 자리 · 드론 착륙장 · 주민 자리) · 해치 뚜껑이 묻히지 않음 ·
    /// 움직이는 조각 원점 · 체험 둘 (드론 날 길이 몸체 · 하늘 면에 안 닿음, 책이 받침 위로 접힘) · 미니게임 둘 (보는 자리에서 시선이 닿음 · 시냅스가 다 보임).
    /// </summary>
    public class AICoreTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interior/PF_Interior_EvoAICore.prefab";
        private const string TemplatePath = "Assets/_Project/Data/Interior/IT_EvoAICore.asset";
        private const string N = "INT_EvoAICore_";
        private const float F = AICore.Floor;

        [Test]
        public void Sockets_MatchExterior()
        {
            // 외형 MD_Evo_AICore 연결점 (면 깊이 ≥ 0인 바깥 면): 바닥 칸 일곱 아래 · 드론 마당 칸 뒤 옆문 하나
            var module = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.ModuleData>("Assets/_Project/Data/Modules/Evolved/MD_Evo_AICore.asset");
            Assert.IsNotNull(module);
            var cells = new HashSet<Vector3Int>(module.CellOffsets);
            var expected = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var c in module.CellOffsets)
                foreach (var d in new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) })
                    if (!cells.Contains(c + d) && module.TryGetFaceDepth(c, d, out _) && !module.IsClosedFace(c, d))
                        expected.Add((c, d));
            var actual = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var (cell, off, floorY) in AICore.Hatches)
            {
                Assert.IsTrue(actual.Add((cell, Vector3Int.down)), $"해치 칸 겹침 {cell}");
                Assert.Less(Mathf.Abs(off.x), 3.05f, $"{cell}");
                Assert.Less(Mathf.Abs(off.y), 3.05f, $"{cell}");
            }
            foreach (var (cell, dir) in AICore.Doors)
                Assert.IsTrue(actual.Add((cell, dir)));
            var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>("Assets/_Project/Data/Interior/IT_EvoAICore.asset");
            foreach (var s in template.Sockets)
                if (!s.SealedWall)
                    actual.Add((s.Cell, s.Direction));   // 모든 면 연결 — 저절로 연 문 계단 (InteriorAutoDoors, 2026-11-19)
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
                Enable(root, "BasketWall");   // 착륙장에 선 드론 바구니 벽 (걷는 몸만 막음)
                const float step = 0.2f, x0 = -3.9f, z0 = -3.9f, top = 3.9f;
                int nx = Mathf.RoundToInt(31.8f / step) + 1, nz = Mathf.RoundToInt(15.8f / step) + 1;
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
                var start = Node(AICore.Spawn);
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
                foreach (var (cell, off, floorY) in AICore.Hatches)
                {
                    var h = new Vector3(cell.x * 8f + off.x, floorY, cell.z * 8f + off.y);
                    Reach(h, $"해치 {cell}");
                    if (Physics.CheckBox(h + Vector3.up * 0.1f, new Vector3(0.7f, 0.04f, 0.7f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        fails.Add($"해치 {cell} 뚜껑이 묻힘");
                }
                foreach (var (cell, dir) in AICore.Doors)
                    Reach(new Vector3(cell.x * 8f, InteriorGeometry.FloorY(cell), cell.z * 8f) + (Vector3)dir * (AICore.DoorDepth - 0.5f), $"문 {cell} {dir}");   // 문 앞 층계참 = 문턱 (2026-11-18)
                Reach(new Vector3(AICore.Gate.x + 1.0f, AICore.PaveTop, AICore.Gate.z), "장미 아치 문 앞 (미로 출발)");
                Reach(new Vector3(AICore.MazeBoard.x + 0.7f, AICore.PaveTop, AICore.MazeBoard.z), "미로 기록판 앞");
                for (int k = 0; k < AICore.Orbs.Length; k++)
                {
                    var o = AICore.Orbs[k].At;
                    Reach(new Vector3(o.x, AICore.GravelTop, o.z), $"빛 구슬 자리 {k} (막다른 길)");
                }
                Reach(AICore.Pavilion + new Vector3(0f, 0f, -1.4f), "정자 단 위");
                Reach(AICore.SynapseStand, "시냅스 따라 켜기 서는 자리");
                for (int k = 0; k < AICore.LecternAngles.Length; k++)
                    Reach(new Vector3(AICore.Pavilion.x, AICore.PodTop, AICore.Pavilion.z) + AICore.LecternOut(k) * 1.65f, $"책 받침 {k} 앞");
                Reach(AICore.DronePad + AICore.DroneExit, "드론 둥지 탑 옥상 착륙장 (드론 내리는 자리)");
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

        /// <summary>주민 자리 발밑에 바닥 (± 0.12, 앉는 자리 ± 0.5)이 있고 서는 몸 (반지름 0.24)이 들어감, 자리 수 15 넘게.</summary>
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

        /// <summary>움직이는 조각 (체험 · 미니게임이 쓰는 자리)이 따로 있고 원점이 값과 같음: 수정 뇌 · 책 여덟 · 시냅스 여섯 · 기록판 · 드론 · 빛 신경 둘.</summary>
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
            At("Brain", AICore.Brain);
            for (int k = 0; k < AICore.LecternAngles.Length; k++)
            {
                At("BookL" + k, AICore.BookSpine(k));
                At("BookR" + k, AICore.BookSpine(k));
            }
            for (int k = 0; k < AICore.Synapses.Length; k++)
                At("Synapse" + k, AICore.Synapses[k]);
            At("MazeBoard", AICore.MazeBoard);
            At("Drone", AICore.DronePad);
            At("VeinsFront", new Vector3(AICore.Pavilion.x, F, AICore.Pavilion.z));
            At("VeinsBack", new Vector3(AICore.Pavilion.x, F, AICore.Pavilion.z));
        }

        /// <summary>
        /// 정원 드론 (체험 ①): 앉음판 둘 · 바구니 벽 (Ignore Raycast — 걷는 몸만 막음) 연결, 착륙장 내리는 자리에서 앉음판을 보면 시선이 2.6 m 안에서 그 자리에 닿음,
        /// 날 길 전체 (떠오름 · 미로 위 한 바퀴 · 내려앉음)에서 드론 바구니 · 몸 · 날개 테가 몸체 · 유리에 안 닿고 꼭대기가 하늘 면 아래 · 방 안 (날개 테 끝까지).
        /// </summary>
        [Test]
        public void DroneRide_Wired()
        {
            Assert.Greater(AICore.DroneRouteLength, 40f, "날 길이 미로 위 한 바퀴보다 짧음");
            AICore.DroneRoutePose(0f, out var first, out _);
            AICore.DroneRoutePose(AICore.DroneRouteLength, out var last, out _);
            Assert.Less(Vector2.Distance(new Vector2(first.x, first.z), new Vector2(AICore.DronePad.x, AICore.DronePad.z)), 0.01f, "날 길 처음 = 착륙장 위");
            Assert.Less(Vector2.Distance(new Vector2(last.x, last.z), new Vector2(AICore.DronePad.x, AICore.DronePad.z)), 0.01f, "날 길 끝 = 착륙장 위");
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var ride = root.GetComponent<InteriorDroneRide>();
                Assert.IsNotNull(ride, "드론 타기 없음");
                var so = new SerializedObject(ride);
                var drone = (Transform)so.FindProperty("_drone").objectReferenceValue;
                Assert.AreEqual(N + "Drone", drone.name);
                var seats = so.FindProperty("_seats");
                Assert.AreEqual(2, seats.arraySize);
                int walls = 0;
                foreach (Transform c in drone)
                    if (c.name.StartsWith("BasketWall"))
                    {
                        walls++;
                        Assert.AreEqual(2, c.gameObject.layer, $"{c.name}: 시선을 막지 않게 Ignore Raycast");
                    }
                Assert.Greater(walls, 6, "바구니 벽 충돌 상자");
                Enable(root, "DroneSeat");
                var eye = AICore.DronePad + AICore.DroneExit + Vector3.up * 1.6f;
                for (int k = 0; k < 2; k++)
                {
                    var seat = (Transform)seats.GetArrayElementAtIndex(k).objectReferenceValue;
                    Assert.AreEqual(drone, seat.parent, $"자리 {k}가 드론 자식");
                    var target = seat.GetComponent<BoxCollider>().bounds.center;
                    Assert.IsTrue(InteriorMode.LookHit(eye, (target - eye).normalized, 2.6f, out var look), $"착륙장 → 자리 {k}: 시선이 아무것도 못 맞힘");
                    Assert.IsTrue(look.collider.name.StartsWith("DroneSeat"), $"착륙장 → 자리 {k}: 시선이 {look.collider.name}에 먼저 닿음");
                }
                Assert.IsTrue(Physics.Raycast(AICore.DronePad + AICore.DroneExit + Vector3.up * 0.5f, Vector3.down, out var deck, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "내리는 자리 발밑 없음");
                Assert.AreEqual(AICore.DronePad.y, deck.point.y, 0.03f, "내리는 자리 = 착륙장 윗면");
                // 날 길: 바구니 (구 반지름 0.9 — 바구니 밑 + 0.95) · 몸과 날개 테 (구 반지름 1.5 — 밑 + 1.85), 떠오름 · 내려앉음은 착륙장 위 곧게
                var fails = new List<string>();
                bool Hits(Vector3 c, float r)   // 드론 자기 조각 (자리 · 바구니 벽 — 프리팹에서는 착륙장에 서 있음)은 뺌
                {
                    foreach (var h in Physics.OverlapSphere(c, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        if (!h.transform.IsChildOf(drone))
                            return true;
                    return false;
                }
                void Check(Vector3 p, string where)
                {
                    if (Hits(p + Vector3.up * 0.95f, 0.9f))
                        fails.Add($"{where} 바구니가 닿음 {p}");
                    else if (p.y > AICore.DronePad.y + 0.5f && Hits(p + Vector3.up * 1.85f, 1.5f))
                        fails.Add($"{where} 몸 · 날개 테가 닿음 {p}");
                    if (p.y + AICore.DroneTop > AICore.CeilingY - 0.05f)
                        fails.Add($"{where} 꼭대기가 하늘 면에 닿음 {p}");
                    foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                    {
                        var e = p + d * AICore.DroneSpan;
                        bool inRoom = e.x > -3.95f && e.x < 27.95f && e.z > -3.95f && e.z < 11.95f && !(e.x > 19.95f && e.z > 3.95f);
                        if (!inRoom)
                            fails.Add($"{where} 날개 테가 방 밖 {e}");
                    }
                }
                for (float y = AICore.DronePad.y; y <= AICore.DroneTransitY; y += 0.1f)
                    Check(new Vector3(AICore.DronePad.x, y, AICore.DronePad.z), "떠오름");
                for (float s = 0f; s <= AICore.DroneRouteLength; s += 0.25f)
                {
                    AICore.DroneRoutePose(s, out var at, out _);
                    Check(at, $"날 길 s {s:F1}");
                }
                Assert.IsEmpty(fails, string.Join(" / ", fails.GetRange(0, Mathf.Min(12, fails.Count))));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 수정 뇌 빛 쇼 (체험 ②): 책 넷 · 받침 충돌체 · 빛 신경 둘 (PulseGlow 칸) 연결, 받침 앞 (1.65 m)에 서서 받침을 보면 시선이 2.6 m 안에서 그 받침에 닿음,
        /// 오른쪽 책장이 책등 축으로 접히면 받침 판 위로 (판 법선 쪽) 올라가 왼쪽 책장 위에 덮임 (부호가 틀리면 받침 속으로 접힘).
        /// </summary>
        [Test]
        public void BrainShow_Wired()
        {
            for (int k = 0; k < AICore.LecternAngles.Length; k++)
            {
                var axis = AICore.BookSpineAxis(k);
                var nrm = AICore.LecternNormal(k);
                var side = Vector3.Cross(Vector3.up, AICore.LecternOut(k)).normalized;
                float sign = Vector3.Dot(Quaternion.AngleAxis(90f, axis) * side, nrm) > 0f ? 1f : -1f;
                var pivot = AICore.BookSpine(k) + nrm * 0.022f;
                var page = AICore.BookSpine(k) + side * 0.105f;
                var closed = pivot + Quaternion.AngleAxis(172f * sign, axis) * (page - pivot);
                var left = AICore.BookSpine(k) - side * 0.105f;
                Assert.Greater(Vector3.Dot(closed - AICore.BookSpine(k), nrm), 0.01f, $"책 {k}: 덮인 책장이 판 위");
                Assert.Less(Vector3.Distance(closed - nrm * Vector3.Dot(closed - left, nrm), left), 0.03f, $"책 {k}: 덮인 책장이 왼쪽 책장 위");
            }
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var show = root.GetComponent<InteriorBrainShow>();
                Assert.IsNotNull(show, "빛 쇼 없음");
                var so = new SerializedObject(show);
                Assert.AreEqual(N + "Brain", ((Transform)so.FindProperty("_brain").objectReferenceValue).name);
                var books = so.FindProperty("_books");
                var lecterns = so.FindProperty("_lecterns");
                var veins = so.FindProperty("_veins");
                Assert.AreEqual(4, books.arraySize);
                Assert.AreEqual(4, lecterns.arraySize);
                Assert.AreEqual(2, veins.arraySize);
                for (int v = 0; v < 2; v++)
                {
                    var r = (Renderer)veins.GetArrayElementAtIndex(v).objectReferenceValue;
                    bool pulse = false;
                    foreach (var m in r.sharedMaterials)
                        pulse |= m != null && m.HasProperty("_Wave");
                    Assert.IsTrue(pulse, $"{r.name}: 흐르는 빛 (PulseGlow) 재질 없음");
                }
                Assert.IsNotNull(so.FindProperty("_memory").objectReferenceValue, "시냅스 따라 켜기와 서로 막음");
                Enable(root, "LecternUse");
                for (int k = 0; k < 4; k++)
                {
                    Assert.AreEqual(N + "BookR" + k, ((Transform)books.GetArrayElementAtIndex(k).objectReferenceValue).name);
                    var use = (Collider)lecterns.GetArrayElementAtIndex(k).objectReferenceValue;
                    var eye = new Vector3(AICore.Pavilion.x, AICore.PodTop + 1.6f, AICore.Pavilion.z) + AICore.LecternOut(k) * 1.65f;
                    var target = use.bounds.center;
                    Assert.IsTrue(InteriorMode.LookHit(eye, (target - eye).normalized, 2.6f, out var look), $"받침 {k}: 시선이 아무것도 못 맞힘");
                    Assert.AreEqual(use, look.collider, $"받침 {k}: 시선이 {look.collider.name}에 먼저 닿음");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 시냅스 따라 켜기 (미니게임): 서는 자리 눈에서 시냅스 여섯이 다 보임 (가림 광선 — 수정 뇌 · 기둥 · 책 받침) · 이웃 사이 각 ≥ 겨눔 너그러움 × 2 · 왼쪽부터 차례 = 숫자 키 차례 (체크리스트 169),
        /// 정자로 다가와 시냅스 쪽을 보면 시선이 시작 상자에 닿음.
        /// </summary>
        [Test]
        public void SynapseMemory_ViewAndStart()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var memory = root.GetComponent<InteriorSynapseMemory>();
                Assert.IsNotNull(memory, "시냅스 따라 켜기 없음");
                var so = new SerializedObject(memory);
                var synapses = so.FindProperty("_synapses");
                Assert.AreEqual(AICore.Synapses.Length, synapses.arraySize);
                var eye = AICore.SynapseStand + Vector3.up * FirstPersonController.EyeHeight;
                var right = Quaternion.Euler(0f, AICore.SynapseYaw, 0f) * Vector3.right;
                float prevSide = float.NegativeInfinity;
                for (int k = 0; k < AICore.Synapses.Length; k++)
                {
                    var t = (Transform)synapses.GetArrayElementAtIndex(k).objectReferenceValue;
                    Assert.AreEqual(N + "Synapse" + k, t.name);
                    var to = AICore.Synapses[k] - eye;
                    Assert.IsFalse(Physics.Raycast(eye, to.normalized, to.magnitude - AICore.SynapseRadius - 0.02f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"시냅스 {k}가 가려짐");
                    float side = Vector3.Dot(to.normalized, right);
                    Assert.Greater(side, prevSide, $"시냅스 {k}: 왼쪽부터 차례가 아님");
                    prevSide = side;
                    if (k > 0)
                        Assert.GreaterOrEqual(Vector3.Angle(to, AICore.Synapses[k - 1] - eye), AICore.SynapseAimDegrees * 2f, $"시냅스 {k - 1} · {k} 사이 각");
                }
                Enable(root, "SynapseStart");
                var start = (Collider)so.FindProperty("_start").objectReferenceValue;
                var approach = AICore.SynapseStand + Vector3.up * 1.6f;
                Assert.IsTrue(InteriorMode.LookHit(approach, (start.bounds.center - approach).normalized, 2.6f, out var look), "시작 상자: 시선이 아무것도 못 맞힘");
                Assert.AreEqual(start, look.collider, $"시작 상자: 시선이 {look.collider.name}에 먼저 닿음");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 뇌 미로 빨리 빠져나가기 (미니게임): 기록판 충돌체 연결 · 앞마당에서 기록판을 보면 시선이 닿음, 빛 구슬 자리 다섯 = 막다른 길 (둘레 셋이 생울타리) · 앞 · 뒤 반구 둘 다,
        /// 구슬 높이에 생울타리 · 몸체 없음.
        /// </summary>
        [Test]
        public void MazeRun_Wired()
        {
            bool front = false, back = false;
            foreach (var (f, _) in AICore.Orbs)
            {
                front |= f;
                back |= !f;
            }
            Assert.IsTrue(front && back, "빛 구슬 자리가 앞 · 뒤 반구 둘 다");
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var run = root.GetComponent<InteriorMazeRun>();
                Assert.IsNotNull(run, "뇌 미로 없음");
                var so = new SerializedObject(run);
                Assert.AreEqual(N + "MazeBoard", ((Transform)so.FindProperty("_board").objectReferenceValue).name);
                Enable(root, "MazeBoardUse");
                var use = (Collider)so.FindProperty("_boardUse").objectReferenceValue;
                var eye = new Vector3(AICore.MazeBoard.x + 0.9f, AICore.PaveTop + 1.6f, AICore.MazeBoard.z);
                Assert.IsTrue(InteriorMode.LookHit(eye, (use.bounds.center - eye).normalized, 2.6f, out var look), "기록판: 시선이 아무것도 못 맞힘");
                Assert.AreEqual(use, look.collider, $"기록판: 시선이 {look.collider.name}에 먼저 닿음");
                for (int k = 0; k < AICore.Orbs.Length; k++)
                {
                    var o = AICore.Orbs[k].At;
                    Assert.IsFalse(Physics.CheckSphere(o, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"빛 구슬 {k}가 생울타리 · 몸체 속");
                    int walls = 0;
                    foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                        if (Physics.Raycast(o, d, 1.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            walls++;
                    Assert.GreaterOrEqual(walls, 3, $"빛 구슬 {k}: 막다른 길이 아님 (둘레 생울타리 {walls})");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 정원 식물 (식물 아틀라스 ②): 빌더 DECOR 수만큼 Garden 아래에 놓임 · 식물마다 거리별 단순화 (LODGroup) · 재질 M_Foliage 하나 · 충돌체 없음,
        /// 삼각형 = 내부 모델 + 식물 가장 가까운 단계 (LOD0)까지 15만 안 (3단계 예산, 체크리스트 54).
        /// </summary>
        [Test]
        public void Garden_OnFoliageWithinBudget()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            var foliage = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Plants/M_Foliage.mat");
            var garden = prefab.transform.Find("Garden");
            Assert.IsNotNull(garden, "Garden 없음");
            Assert.AreEqual(AICore.Garden.Length, garden.childCount, "식물 수 = 빌더 DECOR 수");
            Assert.Greater(AICore.Garden.Length, 30, "정원 식물이 너무 적음");
            long tris = 0;
            foreach (Transform plant in garden)
            {
                Assert.IsNotNull(plant.GetComponent<LODGroup>(), $"{plant.name}: 거리별 단순화 없음");
                Assert.AreEqual(0, plant.GetComponentsInChildren<Collider>(true).Length, $"{plant.name}: 충돌체 (잎은 지나가야 함)");
                foreach (var r in plant.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Assert.AreEqual(1, r.sharedMaterials.Length, $"{r.name}: 재질 칸이 하나가 아님");
                    Assert.AreEqual(foliage, r.sharedMaterial, $"{plant.name}/{r.name}: 재질이 M_Foliage가 아님");
                    if (r.name.EndsWith("_LOD0"))
                        tris += Tris(r.GetComponent<MeshFilter>().sharedMesh);
                }
            }
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!f.transform.IsChildOf(garden))
                    tris += Tris(f.sharedMesh);
            }
            Assert.LessOrEqual(tris, 150000, "삼각형 예산 (내부 모델 + 식물 LOD0)");
        }

        private static long Tris(Mesh mesh)
        {
            long n = 0;
            if (mesh == null)
                return 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
                n += mesh.GetIndexCount(s) / 3;
            return n;
        }
    }
}
