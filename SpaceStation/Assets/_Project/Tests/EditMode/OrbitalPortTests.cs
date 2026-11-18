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
            var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>("Assets/_Project/Data/Interior/IT_EvoOrbitalPort.asset");
            foreach (var s in template.Sockets)
                if (!s.SealedWall)
                    actual.Add((s.Cell, s.Direction));   // 모든 면 연결 (2026-11-18) — 비어 있는 둘레 면에 저절로 연 문 (InteriorAutoDoors)
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
            {
                var boat = prefab.transform.Find($"Boat{k}_Pivot/{N}Boat{k}");   // 뱃놀이 피벗 아래 (InteriorScallopRide가 피벗을 배 길로 돌림)
                Assert.IsNotNull(boat, $"Boat{k} 없음");
                var p = prefab.transform.InverseTransformPoint(boat.position);
                Assert.Less(Vector3.Distance(p, OrbitalPort.Boats[k]), 0.01f, $"Boat{k} 원점 {p} ≠ {OrbitalPort.Boats[k]}");
            }
            Assert.IsNull(prefab.transform.Find($"Boat{OrbitalPort.Boats.Length}_Pivot"), "배가 값보다 많음");
            for (int k = 0; k < OrbitalPort.Clams.Length; k++)
            {
                At("ClamLid" + k, OrbitalPort.ClamHinges[k]);
                At("ClamPearl" + k, OrbitalPort.ClamPearls[k]);
            }
        }

        /// <summary>
        /// 해마 (critter_builder 귀여운 해마 — 사용자 2026-11-17 "해마 유니티로 옮겨줘"): 배마다 한 쌍이 배 피벗 아래 (함께 돎 · 말린 꼬리만 물에),
        /// 회전목마 넷이 회전목마 조각 아래 (금빛 기둥이 몸 속을 지남), 궁전 금빛 상 둘이 진주 받침 위 (꼬리 밑 = 받침 윗면 ± 1.5 cm).
        /// </summary>
        [Test]
        public void Seahorses_Placed()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var fails = new List<string>();
                var r = root.transform;
                for (int k = 0; k < OrbitalPort.Boats.Length; k++)
                {
                    var pivot = r.Find($"Boat{k}_Pivot");
                    foreach (var (model, side) in OrbitalPort.BoatSeahorses)
                    {
                        var t = pivot.Find("Critter_" + model);
                        if (t == null)
                        {
                            fails.Add($"배 {k}: {model} 없음");
                            continue;
                        }
                        OrbitalPort.BoatSeahorse(k, side, out var at, out _);
                        if (Vector3.Distance(t.position, at) > 0.01f)
                            fails.Add($"배 {k} {model}: {t.position} ≠ {at}");
                        var b = t.GetComponent<Renderer>().bounds;
                        if (b.min.y > OrbitalPort.LagoonLevel - 0.05f || b.max.y < OrbitalPort.LagoonLevel + 0.7f)
                            fails.Add($"배 {k} {model}: 높이 {b.min.y:F2} ~ {b.max.y:F2} (꼬리만 물에 · 머리는 수면 위 0.7 m 넘게)");
                    }
                }
                var carousel = r.Find(N + "Carousel");
                foreach (var (model, at, facing) in OrbitalPort.CarouselSeahorses)
                {
                    var t = carousel.Find("Critter_" + model);
                    if (t == null)
                    {
                        fails.Add($"회전목마: {model} 없음");
                        continue;
                    }
                    if (Vector3.Distance(t.position, at) > 0.01f)
                        fails.Add($"회전목마 {model}: {t.position} ≠ {at}");
                    // 기둥 (받침 가운데에서 1.8 m) 높이마다 몸 속 (메시 경계 안)
                    var c = OrbitalPort.Carousel;
                    var pole = new Vector3(at.x, 0f, at.z) - new Vector3(c.x, 0f, c.z);
                    pole = new Vector3(c.x, 0f, c.z) + pole.normalized * 1.8f;
                    var mesh = t.GetComponent<MeshFilter>().sharedMesh;
                    foreach (float h in new[] { 0.3f, 0.6f, 0.9f })
                    {
                        var local = t.InverseTransformPoint(new Vector3(pole.x, at.y + h, pole.z));
                        if (!mesh.bounds.Contains(local))
                            fails.Add($"회전목마 {model}: 기둥이 몸 밖 (꼬리 밑 + {h} m)");
                    }
                }
                foreach (var (model, at, facing) in OrbitalPort.PalaceSeahorses)
                {
                    Transform t = null;
                    foreach (Transform c in r)
                        if (c.name == "Critter_" + model && Vector3.Distance(c.position, at) < 0.01f)
                            t = c;
                    if (t == null)
                    {
                        fails.Add($"궁전 상 {at}: 없음");
                        continue;
                    }
                    if (!Physics.Raycast(at + Vector3.up * 0.05f, Vector3.down, out var hit, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        fails.Add($"궁전 상 {at}: 받침 없음");
                    else if (Mathf.Abs(hit.point.y - at.y) > 0.015f)
                        fails.Add($"궁전 상 {at}: 받침 윗면 {hit.point.y:F3}");
                    if (Mathf.Abs(t.GetComponent<Renderer>().bounds.min.y - at.y) > 0.015f)
                        fails.Add($"궁전 상 {at}: 꼬리 밑 {t.GetComponent<Renderer>().bounds.min.y:F3}");
                }
                Assert.IsEmpty(fails, string.Join(" / ", fails));
            }
            finally
            {
                Object.DestroyImmediate(root);
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
        /// 해마 가리비 배 뱃놀이 (사용자 2026-11-16 "가리비 배 뱃놀이 체험 진행해줘"): 배 길 = Blender op3_builder와 같은 길이 · 배 처음 자리, 배 넷 = 피벗 (+z = 배 길 앞) 아래 ·
        /// 앉음판 자리 둘씩 (상자 충돌체), 배 길 한 바퀴 내내 배 몸 (고물 0.95 ~ 해마 코 2.2 m · 반폭 0.7)이 땅 · 회전목마 · 산호초 · 조개 · 잔교에 닿지 않음,
        /// 선착장 끝에서 정차한 배의 앉음판이 손 닿는 거리, 내리는 자리 = 선착장 마루 위.
        /// </summary>
        [Test]
        public void BoatRide_Wired()
        {
            Assert.AreEqual(44.93f, OrbitalPort.CourseLoop, 0.05f, "배 길 길이 (op3_builder COURSE LEN)");
            for (int k = 0; k < OrbitalPort.Boats.Length; k++)
            {
                var at = OrbitalPort.CoursePoint(OrbitalPort.BoatStartS(k));
                Assert.Less(Vector2.Distance(at, new Vector2(OrbitalPort.Boats[k].x, OrbitalPort.Boats[k].z)), 0.01f, $"배 {k} 처음 자리");
            }
            Assert.Less(OrbitalPort.BoatStartS(0), 0.001f, "배 0 = 선착장 앞");
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var ride = root.GetComponent<InteriorScallopRide>();
                Assert.IsNotNull(ride, "뱃놀이 없음");
                var so = new SerializedObject(ride);
                var boats = so.FindProperty("_boats");
                var seats = so.FindProperty("_seats");
                Assert.AreEqual(OrbitalPort.Boats.Length, boats.arraySize);
                Assert.AreEqual(OrbitalPort.Boats.Length * 2, seats.arraySize);
                for (int b = 0; b < boats.arraySize; b++)
                {
                    var pivot = (Transform)boats.GetArrayElementAtIndex(b).objectReferenceValue;
                    Assert.AreEqual($"Boat{b}_Pivot", pivot.name);
                    Assert.IsNotNull(pivot.Find(N + "Boat" + b), $"배 {b} 메시가 피벗 밑에 없음");
                    OrbitalPort.CoursePose(OrbitalPort.BoatStartS(b), out _, out var dir);
                    Assert.Greater(Vector3.Dot(pivot.forward, new Vector3(dir.x, 0f, dir.y)), 0.999f, $"배 {b} 피벗 앞 = 배 길 앞");
                    for (int k = 0; k < 2; k++)
                    {
                        var seat = (Transform)seats.GetArrayElementAtIndex(b * 2 + k).objectReferenceValue;
                        Assert.AreEqual(pivot, seat.parent, $"배 {b} 자리 {k}");
                        Assert.IsNotNull(seat.GetComponent<BoxCollider>(), $"배 {b} 자리 {k} 충돌체");
                    }
                }
                // 선착장 끝에 서서 정차한 배 자리를 바라보면 시선 광선 (InteriorMode.LookHit — 물 막이 벽은 지나감)이 그 자리에 닿음, 상호작용 거리 2.6 m 안
                //  (첫 물체만 보던 광선은 물가의 보이지 않는 벽에 막혀 "가리비 배 타기"가 뜨지 않았음, 사용자 2026-11-16 "탑승 기능 안 나오는데")
                for (int k = 0; k < 2; k++)   // Spawn이 상자 충돌체를 끔 → 게임처럼 선착장에 선 배 (0)의 자리만 켬 (InteriorScallopRide.UpdateColliders)
                    ((Transform)seats.GetArrayElementAtIndex(k).objectReferenceValue).GetComponent<BoxCollider>().enabled = true;
                Physics.SyncTransforms();
                foreach (float ex in new[] { 23.3f, 24f, 24.7f })
                {
                    var standEye = root.transform.TransformPoint(new Vector3(ex, OrbitalPort.Stop.y + 1.6f, -1.45f));
                    for (int k = 0; k < 2; k++)
                    {
                        var seat = (Transform)seats.GetArrayElementAtIndex(k).objectReferenceValue;
                        var target = seat.GetComponent<BoxCollider>().bounds.center;
                        Assert.IsTrue(InteriorMode.LookHit(standEye, (target - standEye).normalized, 2.6f, out var look), $"선착장 ({ex}) → 자리 {k}: 시선이 아무것도 못 맞힘");
                        Assert.IsTrue(look.collider.name.StartsWith("Seat") && look.collider.transform.parent == seat.parent,
                            $"선착장 ({ex}) → 자리 {k}: 시선이 {look.collider.name}에 먼저 닿음");   // 먼 쪽 자리는 가까운 쪽 자리 상자가 먼저 — 그 배를 타는 것은 같음
                    }
                }
                foreach (var col in root.GetComponentsInChildren<Collider>())
                    if (col.name.EndsWith("_LakeHit"))
                        col.enabled = false;
                Physics.SyncTransforms();
                // 배 길 한 바퀴: 물 위 배 몸 상자 (고물 0.95 ~ 해마 코 2.2 m · 반폭 0.8 · 수면 + 0.15 ~ 1.0 — 땅 = 수면 + 0.1이라 바로 위부터) +
                //  물속 배 밑 (가운데 · 해마 밑이 바닥에서 뜸 — Blender COURSECHK와 같은 기준). 움직이지 않는 몸체만 (배 · 자리 충돌체는 피벗 밑이라 뺌)
                float loop = OrbitalPort.CourseLoop;
                for (float s = 0f; s < loop; s += 0.25f)
                {
                    OrbitalPort.CoursePose(s, out var at, out var dir);
                    var d = new Vector3(dir.x, 0f, dir.y);
                    var c = new Vector3(at.x, OrbitalPort.LagoonLevel, at.y);
                    foreach (var (ahead, need) in new[] { (0f, 0.45f), (1.9f, 0.4f) })
                    {
                        var p = c + d * ahead;
                        Assert.IsTrue(Physics.Raycast(new Vector3(p.x, OrbitalPort.LagoonLevel + 0.01f, p.z), Vector3.down, out var bed, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                        Assert.Greater(OrbitalPort.LagoonLevel - bed.point.y, need, $"배 길 s {s:F2} 배 밑 ({ahead} m 앞)이 얕음 {bed.collider.name}");
                    }
                    // 가리비 그릇 (고물 0.95 ~ 앞 0.95, 반폭 0.82) · 해마 한 쌍 (앞 0.95 ~ 2.2, 반폭 0.5)
                    var hits = new List<Collider>(Physics.OverlapBox(c + Vector3.up * 0.575f, new Vector3(0.82f, 0.425f, 0.95f), Quaternion.LookRotation(d), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                    hits.AddRange(Physics.OverlapBox(c + d * 1.575f + Vector3.up * 0.575f, new Vector3(0.5f, 0.425f, 0.625f), Quaternion.LookRotation(d), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                    foreach (var hit in hits)
                    {
                        bool anyPivot = false;
                        for (int b = 0; b < boats.arraySize; b++)
                            anyPivot |= hit.transform.IsChildOf((Transform)boats.GetArrayElementAtIndex(b).objectReferenceValue);
                        if (anyPivot)
                            continue;
                        if ((s < 0.3f || s > loop - 0.3f) && hit.name.EndsWith("_Shell") && Mathf.Abs(c.x - OrbitalPort.Stop.x) < 1.5f)
                            continue;   // 선착장 끝 마루에 배 옆이 닿아 섬 (정차 자리)
                        Assert.Fail($"배 길 s {s:F2} ({c.x:F2}, {c.z:F2})에서 배 몸이 {hit.name}에 닿음");
                    }
                }
                // 선착장 끝에 선 눈 (마루 끝 0.3 m 안, 눈높이 1.6)에서 정차한 배 앉음판까지 손 닿는 거리 (상호작용 2.6 m)
                var eye = OrbitalPort.Stop + new Vector3(0f, 1.6f, 0.15f);
                for (int k = 0; k < 2; k++)
                {
                    var seat = (Transform)seats.GetArrayElementAtIndex(k).objectReferenceValue;
                    Assert.Less(Vector3.Distance(eye, root.transform.InverseTransformPoint(seat.position)), 2.3f, $"정차한 배 자리 {k}가 선착장 끝에서 멂");
                }
                Assert.IsTrue(Physics.Raycast(OrbitalPort.BoatExit + Vector3.up * 0.5f, Vector3.down, out var deck, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "내리는 자리 발밑 없음");
                Assert.AreEqual(OrbitalPort.BoatExit.y, deck.point.y, 0.02f, "내리는 자리 = 선착장 마루 윗면");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 진주 조개 잡기 (사용자 2026-11-16 "미니게임 진행해줘"): 뚜껑 · 진주 · 시작 상자 · 게 모델 연결, 시작 상자가 잡기 단 마루 (난간 안)를 막지 않음,
        /// 단 세 자리에서 조개 셋을 보면 시선 (InteriorMode.LookHit)이 2.6 m 안에서 시작 상자에 먼저 닿음 (플레이어가 실제로 보는 길 — 체크리스트 196),
        /// 뚜껑을 경첩 축으로 −52° 돌리면 입이 닫힘 (뚜껑이 내려와 진주를 덮고 입술이 경첩 높이에서 만남 · 물에 안 닿음 — 축 부호가 틀리면 물속으로 감).
        /// </summary>
        [Test]
        public void PearlClams_Wired()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var game = root.GetComponent<InteriorPearlClams>();
                Assert.IsNotNull(game, "진주 조개 잡기 없음");
                var so = new SerializedObject(game);
                var start = so.FindProperty("_start").objectReferenceValue as BoxCollider;
                Assert.IsNotNull(start, "시작 상자 없음");
                Assert.IsNotNull(so.FindProperty("_crabMesh").objectReferenceValue, "게 모델 없음");
                Assert.IsNotNull(so.FindProperty("_sparkMaterial").objectReferenceValue, "반짝이 재질 없음");
                var lids = so.FindProperty("_lids");
                var pearls = so.FindProperty("_pearls");
                for (int k = 0; k < OrbitalPort.Clams.Length; k++)
                {
                    Assert.AreEqual(root.transform.Find(N + "ClamLid" + k), lids.GetArrayElementAtIndex(k).objectReferenceValue, $"뚜껑 {k}");
                    Assert.AreEqual(root.transform.Find(N + "ClamPearl" + k), pearls.GetArrayElementAtIndex(k).objectReferenceValue, $"진주 {k}");
                }
                Assert.Greater(OrbitalPort.ClamGameStart.z - OrbitalPort.ClamGameStartSize.z * 0.5f, OrbitalPort.ClamStage.z + 1.0f, "시작 상자가 단 난간 안 마루를 막음");
                start.enabled = true;
                Physics.SyncTransforms();
                foreach (var ex in new[] { -0.9f, 0f, 0.9f })
                {
                    var eye = OrbitalPort.ClamStage + new Vector3(ex, 1.6f, 0.2f);
                    for (int k = 0; k < OrbitalPort.Clams.Length; k++)
                    {
                        var dir = (OrbitalPort.ClamPearls[k] - eye).normalized;
                        bool hit = InteriorMode.LookHit(eye, dir, 2.6f, out var look);
                        Assert.IsTrue(hit && look.collider == start, $"단 ({ex}) → 조개 {k}: 시선이 시작 상자에 안 닿음 ({(hit ? look.collider.name : "없음")})");
                    }
                }
                // 뚜껑 표면을 위에서 쏜 광선으로 잼 (돌린 뒤 Renderer.bounds는 상자를 돌린 큰 상자라 못 씀): 열림 = 입 앞 1.6 m까지 안 덮음,
                //  닫힘 = 입 앞 1.6 m 위가 경첩 높이 근처 (입술이 만남 · 물에 안 닿음) · 진주 위를 덮음
                for (int k = 0; k < OrbitalPort.Clams.Length; k++)
                {
                    var lid = root.transform.Find(N + "ClamLid" + k);
                    var probe = lid.gameObject.AddComponent<MeshCollider>();
                    probe.sharedMesh = lid.GetComponent<MeshFilter>().sharedMesh;
                    Physics.SyncTransforms();
                    float a = OrbitalPort.Clams[k].z * Mathf.Deg2Rad;
                    var mouth = OrbitalPort.ClamHinges[k] + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.6f + Vector3.up * 3f;
                    Assert.IsFalse(probe.Raycast(new Ray(mouth, Vector3.down), out _, 6f), $"조개 {k} 열린 뚜껑이 입 앞까지 덮음 (모델이 열린 채가 아님)");
                    lid.localRotation = Quaternion.AngleAxis(-OrbitalPort.ClamLidOpen, OrbitalPort.ClamHingeAxis(k)) * lid.localRotation;
                    Physics.SyncTransforms();
                    Assert.IsTrue(probe.Raycast(new Ray(mouth, Vector3.down), out var lip, 6f), $"조개 {k} 닫아도 입 앞이 안 덮임 (경첩 축 부호)");
                    Assert.AreEqual(OrbitalPort.ClamY + 0.2f, lip.point.y, 0.25f, $"조개 {k} 닫힌 입술 높이 (경첩 {OrbitalPort.ClamY:0.00})");
                    Assert.Greater(lip.point.y, OrbitalPort.LagoonLevel + 0.2f, $"조개 {k} 닫힌 뚜껑이 물에 닿음");
                    var pearl = OrbitalPort.ClamPearls[k];
                    Assert.IsTrue(probe.Raycast(new Ray(pearl + Vector3.up * 3f, Vector3.down), out var cover, 6f), $"조개 {k} 닫힌 뚜껑이 진주 위에 없음");
                    Assert.Greater(cover.point.y, pearl.y + 0.35f, $"조개 {k} 닫힌 뚜껑이 진주 (꼭대기 {pearl.y + 0.3f:0.00})를 못 덮음");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 인어 분수 진주 던지기 (사용자 2026-11-16 "미니게임 진행해줘"): 시작 상자 · 진주 메시 · 고리 재질 연결, 분수 둘레 여덟 자리 (테 밖 마당)에서 물을 보면 시선이 2.6 m 안에서
        /// 시작 상자에 닿음, 빛 고리 셋이 2분 내내 분수 물 위 (고리 둘레 12점에서 지느러미 밑으로부터 쏜 광선이 꼬리 기둥 · 테에 걸리지 않고 수면 밑 바닥에 닿음 —
        /// 가운데에서 쏘면 꼬리에 닿는지 먼저 봐서 광선이 꼬리를 보는지 확인), 안내판이 꼬리 끝 · 지느러미 위.
        /// </summary>
        [Test]
        public void FountainToss_Wired()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                var game = root.GetComponent<InteriorFountainToss>();
                Assert.IsNotNull(game, "분수 진주 던지기 없음");
                var so = new SerializedObject(game);
                var start = so.FindProperty("_start").objectReferenceValue as BoxCollider;
                Assert.IsNotNull(start, "시작 상자 없음");
                Assert.IsNotNull(so.FindProperty("_pearlMesh").objectReferenceValue, "진주 메시 없음");
                Assert.IsNotNull(so.FindProperty("_pearlMaterial").objectReferenceValue, "진주 재질 없음");
                Assert.IsNotNull(so.FindProperty("_lineMaterial").objectReferenceValue, "고리 재질 없음");
                var fo = OrbitalPort.Fountain;
                start.enabled = true;
                Physics.SyncTransforms();
                for (int q = 0; q < 8; q++)
                {
                    float a = q * Mathf.PI / 4f;
                    var out_ = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var eye = new Vector3(fo.x, OrbitalPort.PaveTop + 1.6f, fo.z) + out_ * 2.55f;
                    var target = new Vector3(fo.x, fo.y, fo.z) + out_ * 1.0f;
                    bool hit = InteriorMode.LookHit(eye, (target - eye).normalized, 2.6f, out var look);
                    Assert.IsTrue(hit && look.collider == start, $"분수 둘레 {q * 45}° → 물: 시선이 시작 상자에 안 닿음 ({(hit ? look.collider.name : "없음")})");
                }
                start.enabled = false;
                Physics.SyncTransforms();
                const float above = 3f;
                Assert.IsTrue(Physics.Raycast(new Vector3(fo.x, fo.y + above, fo.z), Vector3.down, out var tail, above + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && tail.point.y > fo.y + 0.1f, "가운데 광선이 꼬리를 못 봄 (검사가 헛돎)");
                // 고리 둘레는 꼬리지느러미 밑 (수면 위 1.2 m — 지느러미는 수면 위 1.9 m부터 고리 위로 넓게 펼쳐짐)에서 쏨: 꼬리 기둥 · 테만 잼
                const float under = 1.2f;
                var toss = new FountainToss(new Vector2(fo.x, fo.z), OrbitalPort.FountainWaterRadius);
                for (float t = 0f; t < 120f; t += 0.25f)
                {
                    for (int i = 0; i < FountainToss.Rings.Length; i++)
                    {
                        var c = toss.RingCenter(i, t);
                        float rr = FountainToss.Rings[i].Radius + 0.03f;
                        for (int q = 0; q < 12; q++)
                        {
                            float a = q * Mathf.PI / 6f;
                            var p = new Vector3(c.x + rr * Mathf.Cos(a), fo.y + under, c.y + rr * Mathf.Sin(a));
                            bool down = Physics.Raycast(p, Vector3.down, out var bed, under + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                            Assert.IsTrue(down && bed.point.y < fo.y - 0.05f, $"고리 {i} ({t:0.00}초) 둘레 {q * 30}°가 꼬리 · 테에 걸림 ({(down ? bed.point.y.ToString("0.00") : "없음")})");
                        }
                    }
                }
                Assert.Greater(OrbitalPort.TossSign.y, OrbitalPort.FountainTip.y + 0.7f, "안내판이 꼬리 끝 · 지느러미에 걸림");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 바다 동물 (사용자 2026-11-16 "이제 동물 배치 진행해줘"): 자리 · 앞쪽 · 움직임 = OrbitalPort 값, 물범 · 게 몸 밑 닿는 점이 모래 1.5 cm 안 (원점 한 점만 맞추면
        /// 둥근 배 · 꼬리가 떴음 — 체크리스트 148), 게 옆걸음 끝도 같은 모래 높이, 거북 헤엄 고리 = 막히지 않고 · 배 길에서 1.15 m · 발밑 물 깊이 0.25 m 넘게,
        /// 날치 활공 길 = 머리 쪽 · 막히지 않고 · 물 깊이 0.5 m · 배 길에서 1.3 m.
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
                    ("Critters_FlyingFish", OrbitalPort.FlyingFish, SpaceStation.Building.CritterFx.Kind.Glide, false),
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
                        if (kind == SpaceStation.Building.CritterFx.Kind.Glide)
                        {
                            // 날치 활공 길 (CritterFx Glide — 처음 자리 = 한가운데, 앞 · 뒤 반 길이씩): 머리 쪽으로 날고, 길 둘레 0.45 m · 수면 위 0.7 m 안에 물체 없음,
                            //  길 아래 물 깊이 0.5 m (양 끝에서 수면 아래 0.6 m로 들어감), 배 길에서 1.3 m, 방 벽에서 0.6 m
                            Assert.AreEqual(OrbitalPort.FlyingFishScale, scale, 0.001f, $"{holderName} 묶음 배율 = 활공 크기");
                            Assert.Greater(Vector3.Dot(heading.normalized, face), 0.9f, $"{holderName} {i} 머리 쪽 (나는 쪽) {heading}");
                            var a = at - face * (OrbitalPort.FlyingFishGlide * 0.5f);
                            var b = at + face * (OrbitalPort.FlyingFishGlide * 0.5f);
                            foreach (var c in Physics.OverlapCapsule(new Vector3(a.x, OrbitalPort.LagoonLevel + 0.35f, a.z), new Vector3(b.x, OrbitalPort.LagoonLevel + 0.35f, b.z), 0.45f,
                                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                                Assert.Fail($"{holderName} {i} 활공 길이 막힘 ({c.name})");
                            for (int k = 0; k <= 8; k++)
                            {
                                var p = Vector3.Lerp(a, b, k / 8f);
                                Assert.IsTrue(p.x > -3.35f && p.x < 51.35f && p.z > -3.35f && p.z < 11.35f, $"{holderName} {i} 활공 길이 방 벽에 붙음 {p}");
                                Assert.Greater(OrbitalPort.CourseDistance(new Vector2(p.x, p.z)), 1.3f, $"{holderName} {i} 활공 길이 배 길에 걸림 {p}");
                                Assert.IsTrue(Physics.Raycast(new Vector3(p.x, OrbitalPort.LagoonLevel + 0.01f, p.z), Vector3.down, out var bed, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
                                Assert.Greater(OrbitalPort.LagoonLevel - bed.point.y, 0.5f, $"{holderName} {i} 활공 길 아래 물이 얕음 {p}");
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

        /// <summary>
        /// 석호 수련 (식물 아틀라스 ④ 2026-11-16): 정원 식물 수 = <see cref="OrbitalPort.Lilies"/> · 거리별 단순화 · 충돌체 없음 · 재질 M_Foliage 하나,
        /// 잎 꼭짓점 (가장 가까운 단계)이 모두 수면 위 (25 cm 안) · 밑 물 깊이 4 cm 넘게 · 위 1.5 m 안에 모델 없음 · 배 길에서 1.3 m · 거북 헤엄 고리에서 0.5 m ·
        /// 날치 활공 길에서 0.6 m · 조개 · 경첩에서 1.4 m · 방 벽 안쪽 · 등이 닿는 곳 (점광원 합 0.2), 삼각형 예산 (내부 모델 조각 INT_* + 식물 가장 가까운 단계 ≤ 150k).
        /// </summary>
        [Test]
        public void Lilies_OnOpenWaterClearOfRides()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                foreach (var col in root.GetComponentsInChildren<Collider>())
                    if (col.name.EndsWith("_LakeHit"))
                        col.enabled = false;                        // 물 깊이 = 석호 바닥 (물 막이 상자 말고)
                Physics.SyncTransforms();
                var foliage = AssetDatabase.LoadAssetAtPath<Material>(SpaceStation.Editor.PlantAtlas.MaterialPath);
                var garden = root.transform.Find("Garden");
                Assert.IsNotNull(garden, "Garden 없음");
                Assert.AreEqual(OrbitalPort.Lilies.Length, garden.childCount, "수련 수");
                Assert.GreaterOrEqual(OrbitalPort.Lilies.Length, 12, "수련이 너무 적음");

                // 거북 헤엄 고리 (Critters_OnTheirSpots와 같은 계산)
                var loop = new List<Vector3>();
                var holder = root.transform.Find("Critters_Turtle");
                var arr = new SerializedObject(holder.GetComponent<SpaceStation.Building.CritterFx>()).FindProperty("_critters");
                for (int i = 0; i < arr.arraySize; i++)
                {
                    var e = arr.GetArrayElementAtIndex(i);
                    var part = (Transform)e.FindPropertyRelative("Part").objectReferenceValue;
                    float side = e.FindPropertyRelative("Side").floatValue;
                    SpaceStation.Building.CritterFx.SwimSize(true, e.FindPropertyRelative("Radius").floatValue, 0f, Mathf.Abs(side), out float ra, out float rb);
                    var heading = e.FindPropertyRelative("Heading").vector3Value;
                    for (int k = 0; k < 48; k++)
                        loop.Add(holder.TransformPoint(part.localPosition + SpaceStation.Building.CritterFx.SwimOffset(heading, Mathf.Sign(side), ra, rb, 2f * Mathf.PI * k / 48f)));
                }
                Assert.Greater(loop.Count, 0, "거북 고리 없음");

                var lights = new List<Light>();
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                    if (l.type == LightType.Point)
                        lights.Add(l);
                Assert.Greater(lights.Count, 0, "방 등 없음");

                long tris = 0;
                foreach (Transform plant in garden)
                {
                    Assert.IsNotNull(plant.GetComponent<LODGroup>(), $"{plant.name}: 거리별 단순화 없음");
                    float lit = 0f;                                 // 등이 닿는 곳 (어두운 물에서는 수련이 검은 점 무늬)
                    foreach (var l in lights)
                    {
                        float d = Vector3.Distance(l.transform.position, plant.position);
                        if (d < l.range)
                            lit += l.intensity * (1f - d / l.range) * (1f - d / l.range);
                    }
                    Assert.GreaterOrEqual(lit, 0.2f, $"{plant.name} {plant.localPosition}: 등이 닿지 않는 어두운 물");
                    Assert.AreEqual(0, plant.GetComponentsInChildren<Collider>(true).Length, $"{plant.name}: 충돌체");
                    foreach (var r in plant.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        Assert.AreEqual(1, r.sharedMaterials.Length, $"{r.name}: 재질 칸이 하나가 아님");
                        Assert.AreEqual(foliage, r.sharedMaterial, $"{plant.name}/{r.name}: 재질이 M_Foliage가 아님");
                        if (r.name.EndsWith("_LOD0"))
                            tris += Tris(r.GetComponent<MeshFilter>().sharedMesh);
                    }
                    foreach (var v in SpaceStation.Editor.InteriorPlantVariants.Vertices(plant.gameObject))
                    {
                        var p = plant.TransformPoint(v);
                        var xz = new Vector2(p.x, p.z);
                        string at = $"{plant.name} {p}";
                        Assert.IsTrue(p.y > OrbitalPort.LagoonLevel + 0.003f && p.y < OrbitalPort.LagoonLevel + 0.25f, $"{at}: 수면 위가 아님");
                        Assert.IsTrue(p.x > -3.75f && p.x < 51.75f && p.z > -3.75f && p.z < 11.75f, $"{at}: 방 벽에 붙음");
                        Assert.IsTrue(Physics.Raycast(new Vector3(p.x, OrbitalPort.LagoonLevel + 0.01f, p.z), Vector3.down, out var bed, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"{at}: 밑 없음");
                        Assert.Greater(OrbitalPort.LagoonLevel - bed.point.y, 0.04f, $"{at}: 물이 얕음 (모래 위)");
                        Assert.IsFalse(Physics.Raycast(new Vector3(p.x, OrbitalPort.LagoonLevel + 0.02f, p.z), Vector3.up, out var over, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore),
                            $"{at}: 위에 모델 ({over.collider?.name})");
                        Assert.Greater(OrbitalPort.CourseDistance(xz), 1.3f, $"{at}: 배 길에 걸림");
                        foreach (var q in loop)
                            Assert.Greater((new Vector2(q.x, q.z) - xz).magnitude, 0.5f, $"{at}: 거북 헤엄 고리에 걸림");
                        foreach (var (_, fish, facing) in OrbitalPort.FlyingFish)
                        {
                            var face = new Vector2(Mathf.Cos(facing * Mathf.Deg2Rad), Mathf.Sin(facing * Mathf.Deg2Rad));
                            var a = new Vector2(fish.x, fish.z) - face * (OrbitalPort.FlyingFishGlide * 0.5f);
                            var seg = face * OrbitalPort.FlyingFishGlide;
                            float u = Mathf.Clamp01(Vector2.Dot(xz - a, seg) / seg.sqrMagnitude);
                            Assert.Greater((xz - (a + seg * u)).magnitude, 0.6f, $"{at}: 날치 활공 길에 걸림");
                        }
                        for (int k = 0; k < OrbitalPort.Clams.Length; k++)
                        {
                            Assert.Greater((xz - new Vector2(OrbitalPort.Clams[k].x, OrbitalPort.Clams[k].y)).magnitude, 1.4f, $"{at}: 조개 {k}에 붙음");
                            Assert.Greater((xz - new Vector2(OrbitalPort.ClamHinges[k].x, OrbitalPort.ClamHinges[k].z)).magnitude, 1.4f, $"{at}: 조개 경첩 {k}에 붙음");
                        }
                    }
                }
                foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.name.StartsWith(N))                       // 내부 모델 조각만 (동물 = 따로 모델 SM_InteriorCritters — 이 예산 밖)
                        tris += Tris(f.sharedMesh);
                }
                Assert.LessOrEqual(tris, 150000, "삼각형 예산 (내부 모델 조각 + 수련 가장 가까운 단계)");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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
