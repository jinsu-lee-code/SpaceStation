using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 진화 3단계 궤도 항만 (Blender op3_builder와 같은 값): 연결점 (외형 연결점 면과 같음 — 옆문 넷 · 아래 해치 열넷 · 위 해치 셋) ·
    /// 걸어서 닿음 (해치 · 문 · 섬 궁전 안 · 분수 마당 · 전망 첨탑 · 선착장 · 방파제 소라 문 · 잔교 · 기중기 운전대 · 조개 잡기 단 · 등대 전망대, 석호 · 분수 물속 못 내려감) ·
    /// 해치 뚜껑이 묻히지 않음 · 주민 자리 발밑 · 움직이는 조각 원점 · 분수 물줄기.
    /// </summary>
    public class OrbitalPortTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interior/PF_Interior_EvoOrbitalPort.prefab";
        private const string TemplatePath = "Assets/_Project/Data/Interior/IT_EvoOrbitalPort.asset";
        private const string N = "INT_EvoOrbitalPort_";
        private const float F = OrbitalPort.Floor;

        [Test]
        public void Sockets_MatchExterior()
        {
            // 외형 MD_Evo_OrbitalPort 연결점 (면 깊이 ≥ 0인 바깥 면): 바닥 칸 열넷 아래 · 옆문 넷 · 위 셋
            var module = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.ModuleData>("Assets/_Project/Data/Modules/Evolved/MD_Evo_OrbitalPort.asset");
            Assert.IsNotNull(module);
            var cells = new HashSet<Vector3Int>(module.CellOffsets);
            var expected = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var c in module.CellOffsets)
                foreach (var d in new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1) })
                    if (!cells.Contains(c + d) && module.TryGetFaceDepth(c, d, out _) && !module.IsClosedFace(c, d))
                        expected.Add((c, d));
            var actual = new HashSet<(Vector3Int, Vector3Int)>();
            foreach (var (cell, off, floorY) in OrbitalPort.Hatches)
            {
                Assert.IsTrue(actual.Add((cell, Vector3Int.down)), $"해치 칸 겹침 {cell}");
                Assert.Less(Mathf.Abs(off.x), 3.05f, $"{cell}");
                Assert.Less(Mathf.Abs(off.y), 3.05f, $"{cell}");
            }
            foreach (var (cell, dir) in OrbitalPort.Doors)
                Assert.IsTrue(actual.Add((cell, dir)));
            foreach (var (cell, off) in OrbitalPort.UpHatches)
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
                const float step = 0.2f, x0 = -3.9f, z0 = -3.9f, top = 3.9f;
                int nx = Mathf.RoundToInt(55.8f / step) + 1, nz = Mathf.RoundToInt(15.8f / step) + 1;
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
                var start = Node(OrbitalPort.Spawn);
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
                // 석호 · 분수 물속까지 못 내려감 (보이지 않는 벽 LakeHit · 분수 테)
                var fo = OrbitalPort.Fountain;
                for (int k = 0; k < nodes.Count; k++)
                {
                    if (!seen[k])
                        continue;
                    var n = nodes[k];
                    if (n.y < OrbitalPort.LagoonLevel - 0.12f)
                    {
                        fails.Add($"석호 물속까지 걸어 내려감 {n}");
                        break;
                    }
                    if (new Vector2(n.x - fo.x, n.z - fo.z).magnitude < OrbitalPort.FountainRadius && n.y < fo.y + 0.05f)
                    {
                        fails.Add($"분수에 들어감 {n}");
                        break;
                    }
                }
                void Reach(Vector3 p, string what)
                {
                    int k = Node(p);
                    if (k < 0)
                        fails.Add($"{what}: 설 곳 없음 {p}");
                    else if (!seen[k])
                        fails.Add($"{what}: 걸어서 못 감 {p}");
                }
                foreach (var (cell, off, floorY) in OrbitalPort.Hatches)
                {
                    var h = new Vector3(cell.x * 8f + off.x, floorY, cell.z * 8f + off.y);
                    Reach(h, $"해치 {cell}");
                    if (Physics.CheckBox(h + Vector3.up * 0.1f, new Vector3(0.7f, 0.04f, 0.7f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        fails.Add($"해치 {cell} 뚜껑이 묻힘");
                }
                foreach (var (cell, dir) in OrbitalPort.Doors)
                    Reach(new Vector3(cell.x * 8f, F + cell.y * 8f, cell.z * 8f) + (Vector3)dir * (OrbitalPort.DoorDepth - 0.5f), $"문 {cell} {dir}");
                for (int k = 0; k < OrbitalPort.UpHatches.Length; k++)
                {
                    var (cell, off) = OrbitalPort.UpHatches[k];
                    float floorY = OrbitalPort.UpHatchFloor(k);
                    Reach(new Vector3(cell.x * 8f + off.x, floorY, cell.z * 8f + off.y), $"위 해치 밑 {cell}");
                    Assert.Less(cell.y * 8f + OrbitalPort.UpHatchDepth - (floorY + 1.6f), 3.8f, $"위 해치 {cell}가 눈에서 손 닿는 거리 (해치 4 m)");
                }
                var pal = OrbitalPort.Palace;
                Reach(pal + new Vector3(-0.9f, 0f, 0f), "궁전 안 (조개 왕좌 앞)");
                Reach(pal + new Vector3(OrbitalPort.PalaceRadius + 1.6f, F - pal.y, 0f), "궁전 큰 계단 아래 (궁전 길)");
                Reach(new Vector3(fo.x + 2.4f, OrbitalPort.PaveTop, fo.z), "분수 마당 (돌판)");
                var stop = OrbitalPort.Stop;
                Reach(stop + new Vector3(0f, 0f, 0.2f), "선착장 마루 끝");
                Reach(new Vector3(24f, F, 10.15f), "방파제 소라 문");
                Reach(new Vector3(44f, OrbitalPort.PierY, 10.0f), "진주 잔교");
                Reach(new Vector3(50.6f, OrbitalPort.PierY, 5.2f), "잔교 끝 (오른쪽 문 앞)");
                var cs = OrbitalPort.ClamStage;
                Reach(cs + new Vector3(0f, 0f, 0.3f), "진주 조개 잡기 단");
                var cd = OrbitalPort.CraneDeck;
                Reach(new Vector3((cd.x + cd.y) * 0.5f, OrbitalPort.CraneDeckY, cd.z + 0.8f), "기중기 운전대 마루");
                Reach(new Vector3(37f, F, -2.0f), "앵무조개 앞 모래톱");
                // 주민 자리 모두 걸어서 닿음 (앉는 자리 = 의자 앞 · 옆 바닥) — 전망대 원판 두 자리도 여기서 (나선 계단으로 오름)
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

        /// <summary>주민 자리 발밑에 바닥 (± 0.12, 앉는 자리 ± 0.5)이 있고 서는 몸 (반지름 0.24)이 들어감, 자리 수 = 바닥 7 × 2칸 25자리.</summary>
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
                Assert.AreEqual(25, template.ResidentSpots.Count);
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

        /// <summary>움직이는 조각 (체험 · 미니게임이 쓸 자리)이 따로 있고 원점이 값과 같음: 회전목마 (천천히 돎) · 배 넷 · 조개 뚜껑 · 진주.</summary>
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
            At("Carousel", OrbitalPort.Carousel);
            Assert.IsNotNull(prefab.transform.Find(N + "Carousel").GetComponent<SpaceStation.Building.ModuleSpinner>(), "회전목마가 돌지 않음");
            for (int k = 0; k < OrbitalPort.Boats.Length; k++)
                At("Boat" + k, OrbitalPort.Boats[k]);
            Assert.IsNull(prefab.transform.Find(N + "Boat" + OrbitalPort.Boats.Length), "배가 값보다 많음");
            for (int k = 0; k < OrbitalPort.Clams.Length; k++)
            {
                At("ClamLid" + k, OrbitalPort.ClamHinges[k]);
                At("ClamPearl" + k, OrbitalPort.ClamPearls[k]);
            }
        }

        /// <summary>큰 진주 조개가 물에 잠기지 않음 (사용자 2026-11-16 "조개가 물속에 파묻혀 있음"): 진주 밑 · 뚜껑 밑이 수면 위, 진주 꼭대기가 입술 (경첩 높이) 위.</summary>
        [Test]
        public void Clams_AboveWater()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                for (int k = 0; k < OrbitalPort.Clams.Length; k++)
                {
                    var pearl = root.transform.Find(N + "ClamPearl" + k).GetComponent<Renderer>().bounds;   // 월드 경계 (FBX 가져오기 회전 포함)
                    var lid = root.transform.Find(N + "ClamLid" + k).GetComponent<Renderer>().bounds;
                    Assert.Greater(pearl.min.y, OrbitalPort.LagoonLevel + 0.02f, $"조개 {k} 진주가 물에 잠김");
                    Assert.Greater(pearl.max.y, OrbitalPort.ClamY + 0.05f, $"조개 {k} 진주가 입술 위로 안 보임");
                    Assert.Greater(lid.min.y, OrbitalPort.LagoonLevel + 0.2f, $"조개 {k} 뚜껑이 물에 닿음");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>해변 = 마른 모래 · 젖은 모래 (물가 띠) 재질, 석호 = 모래 턱이 비치는 맑은 물 (사용자 2026-11-16 "해안선이 해안처럼 느껴지지 않음").</summary>
        [Test]
        public void Beach_SandAndClearWater()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            var names = new HashSet<string>();
            foreach (var m in prefab.transform.Find(N + "Shell").GetComponent<MeshRenderer>().sharedMaterials)
                names.Add(m != null ? m.name : "");
            Assert.IsTrue(names.Contains("M_InteriorEvo3BeachSand"), "모래 재질 없음");
            var sand = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/Interior/M_InteriorEvo3BeachSand.mat");
            Assert.IsNotNull(sand.GetTexture("_DetailNormal"), "모래 결 텍스처 없음");
            Assert.Greater(sand.GetColor("_WetColor").a, 0.5f, "물가 젖은 띠 꺼짐");
            Assert.AreEqual(OrbitalPort.LagoonLevel + 0.05f, sand.GetFloat("_WetLevel"), 0.02f, "젖은 띠 높이 = 수면 + 5 cm");
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/_Project/Data/Interior/Water/KWS_OrbitalPort0.asset");
            if (settings != null)
                Assert.GreaterOrEqual(new SerializedObject(settings).FindProperty("Transparent").floatValue, 2f, "석호가 탁해 모래 턱이 안 비침");
        }

        /// <summary>분수 물줄기 = 꼬리 끝에서 솟아 분수 물 안 (테 안쪽)에 떨어짐, 공중에 박힌 물방울 메시 없음.</summary>
        [Test]
        public void Fountain_Wired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            var fx = prefab.GetComponent<InteriorFountainFx>();
            Assert.IsNotNull(fx, "분수 물줄기 없음");
            var so = new SerializedObject(fx);
            var tip = so.FindProperty("_tip").vector3Value;
            float reach = so.FindProperty("_reach").floatValue;
            Assert.Less(Vector3.Distance(tip, OrbitalPort.FountainTip), 0.01f);
            Assert.AreEqual(OrbitalPort.Fountain.y, so.FindProperty("_waterY").floatValue, 0.001f);
            var fo = OrbitalPort.Fountain;
            Assert.Less(new Vector2(tip.x - fo.x, tip.z - fo.z).magnitude + reach + 0.3f, OrbitalPort.FountainRadius, "물줄기가 분수 테 밖에 떨어짐");
            Assert.Greater(tip.y, fo.y + 1.5f);
            Assert.IsNotNull(so.FindProperty("_material").objectReferenceValue);
        }

        /// <summary>
        /// 바다 동물 (사용자 2026-11-16 "이제 동물 배치 진행해줘"): 자리 · 앞쪽 · 움직임 = OrbitalPort 값, 물범 · 게 몸 밑 닿는 점이 모래 1.5 cm 안 (원점 한 점만 맞추면
        /// 둥근 배 · 꼬리가 떴음 — 체크리스트 148), 게 옆걸음 끝도 같은 모래 높이, 거북 헤엄 고리 = 막히지 않고 · 배 길에서 1.15 m · 발밑 물 깊이 0.25 m 넘게.
        /// </summary>
        [Test]
        public void Critters_OnTheirSpots()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                foreach (var col in root.GetComponentsInChildren<Collider>())
                    if (col.name.EndsWith("_LakeHit"))
                        col.enabled = false;                        // 발밑 · 물 깊이 = 모래 · 석호 바닥 (물 막이 상자 말고)
                Physics.SyncTransforms();
                var groups = new (string Holder, (string Kind, Vector3 At, float Facing)[] Spots, SpaceStation.Building.CritterFx.Kind Kind, bool Land)[]
                {
                    ("Critters_Seal", OrbitalPort.Seals, SpaceStation.Building.CritterFx.Kind.Bask, true),
                    ("Critters_Crab", OrbitalPort.Crabs, SpaceStation.Building.CritterFx.Kind.Scuttle, true),
                    ("Critters_Turtle", OrbitalPort.Turtles, SpaceStation.Building.CritterFx.Kind.Drift, false),
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
                        if (land)
                        {
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
                                Assert.IsTrue(hit.collider.name.EndsWith("_Shell"), $"{holderName} {i} 발밑이 모래가 아님 ({hit.collider.name})");
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
                            Assert.Greater(buried, -deepest, $"{holderName} {i} 모래 속에 너무 깊이 묻힘 {buried:F3}");
                        }
                        if (kind == SpaceStation.Building.CritterFx.Kind.Scuttle)
                        {
                            Assert.AreEqual(2f, e.FindPropertyRelative("Side").floatValue, $"{holderName} {i} 양쪽 옆걸음");
                            Assert.Less(Mathf.Abs(Vector3.Dot(heading.normalized, face)), 0.2f, $"{holderName} {i} 옆걸음 축이 물을 보는 쪽과 직각이어야 함 {heading}");
                            float reach = SpaceStation.Building.CritterFx.ScuttleReach * scale;
                            foreach (float s in new[] { -1f, 1f })
                            {
                                var p = at + heading.normalized * (reach * s);
                                Assert.IsTrue(Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var hit, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                                Assert.Less(Mathf.Abs(hit.point.y - at.y - 0.005f), 0.015f, $"{holderName} {i} 옆걸음 끝 모래 높이 {hit.point.y:F3}");
                            }
                            // 옆걸음 띠 (물가 따라 ± 0.55 · 앞뒤 ± 0.2)에 해변 조개 · 불가사리 장식 없음 (다리가 불가사리 위에 걸쳐 보였음)
                            var across = new Vector3(heading.z, 0f, -heading.x).normalized;
                            for (float a = -0.55f; a <= 0.551f; a += 0.05f)
                                for (float b = -0.2f; b <= 0.201f; b += 0.05f)
                                {
                                    var p = at + heading.normalized * a + across * b;
                                    Assert.IsTrue(Physics.Raycast(new Vector3(p.x, at.y + 0.5f, p.z), Vector3.down, out var hit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                                    Assert.Less(Mathf.Abs(hit.point.y - OrbitalPort.Floor), 0.004f, $"{holderName} {i} 옆걸음 띠에 장식 · 물가 턱 {p} {hit.point.y:F3}");
                                }
                        }
                        if (kind == SpaceStation.Building.CritterFx.Kind.Drift)
                        {
                            Assert.AreEqual(OrbitalPort.LagoonLevel, at.y, 0.001f, $"{holderName} {i} 원점 = 수면");
                            float side = e.FindPropertyRelative("Side").floatValue;
                            Assert.AreNotEqual(0f, side, $"{holderName} {i} 헤엄 고리가 다 막힘");
                            float radius = e.FindPropertyRelative("Radius").floatValue;
                            SpaceStation.Building.CritterFx.SwimSize(true, radius, 0f, Mathf.Abs(side), out float ra, out float rb);
                            for (int k = 0; k < 48; k++)
                            {
                                var p = holder.TransformPoint(part.localPosition + SpaceStation.Building.CritterFx.SwimOffset(heading, Mathf.Sign(side), ra, rb, 2f * Mathf.PI * k / 48f));
                                Assert.Greater(OrbitalPort.CourseDistance(new Vector2(p.x, p.z)), OrbitalPort.TurtleCourseClear, $"{holderName} {i} 헤엄 고리가 배 길에 걸림 {p}");
                                Assert.IsTrue(Physics.Raycast(new Vector3(p.x, OrbitalPort.LagoonLevel + 0.01f, p.z), Vector3.down, out var bed, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                                Assert.Greater(OrbitalPort.LagoonLevel - bed.point.y, 0.25f, $"{holderName} {i} 헤엄 고리 밑 물이 얕음 {p} ({bed.collider.name})");
                            }
                        }
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
