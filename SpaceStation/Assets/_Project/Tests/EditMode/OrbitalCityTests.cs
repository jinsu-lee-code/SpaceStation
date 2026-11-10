using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 진화 3단계 궤도 도시 (Blender oc_builder와 같은 값): 해치 · 문 자리 · 열차 정지선 계산, 걸어서 닿음 (계단 · 단까지 높이가 다른 바닥을 잇는 격자) ·
    /// 해치 뚜껑이 묻히지 않음 · 주민 자리 발밑 · 둘레가 빔.
    /// </summary>
    public class OrbitalCityTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Interior/PF_Interior_EvoOrbitalCity.prefab";
        private const float F = OrbitalCity.Floor;

        [Test]
        public void Sockets_MatchExterior()
        {
            Assert.AreEqual(21, OrbitalCity.Hatches.Length);
            var cells = new HashSet<Vector3Int>();
            foreach (var (cell, off) in OrbitalCity.Hatches)
            {
                Assert.IsTrue(cells.Add(cell), $"해치 칸 겹침 {cell}");
                Assert.AreEqual(0, cell.y);
                Assert.IsFalse((cell.x == 0 || cell.x == 4) && (cell.z == 0 || cell.z == 4), $"귀퉁이 칸 해치 {cell}");
                Assert.Less(Mathf.Abs(off.x), 3.05f, $"{cell}");
                Assert.Less(Mathf.Abs(off.y), 3.05f, $"{cell}");
            }
            // 문 자리 = 옥상 칸만 (가운데 기둥 칸은 승강장이 없어 문 없음, 2026-11-11)
            Assert.AreEqual(3, OrbitalCity.TowerDoors.Length);
            foreach (var (cell, dir) in OrbitalCity.TowerDoors)
            {
                Assert.AreEqual(new Vector3Int(2, 5, 2), cell);
                Assert.AreEqual(0, dir.y);
                Assert.IsFalse(dir.z < 0, "맨 위 칸 −z = 외형 연결점 없음");
            }
            // 엘리베이터 = 언덕 꼭대기 광장 → 옥상 직행
            Assert.AreEqual(2, OrbitalCity.LiftStops.Length);
            Assert.AreEqual(F + 8f, OrbitalCity.LiftStops[0], 1e-4f);
            Assert.AreEqual(5f, (OrbitalCity.LiftStops[1] - F) / 8f, 1e-4f, "옥상 칸 바닥");
        }

        [Test]
        public void Water_FallsLandInPools()
        {
            // 수면 = 아래부터 차례로, 폭포 = 턱이 한 수면 · 떨어지는 자리가 바로 아래 수면 (KWS 물 기준점 = 수면 — 어긋나면 물이 떠 보임)
            for (int k = 1; k < OrbitalCity.WaterLevels.Length; k++)
                Assert.Greater(OrbitalCity.WaterLevels[k], OrbitalCity.WaterLevels[k - 1]);
            Assert.AreEqual(F - 0.15f, OrbitalCity.WaterLevels[0], 1e-4f, "땅 높이 수면 = 강");
            int Level(float y)
            {
                for (int k = 0; k < OrbitalCity.WaterLevels.Length; k++)
                {
                    if (Mathf.Abs(OrbitalCity.WaterLevels[k] - y) < 1e-3f)
                        return k;
                }
                return -1;
            }
            foreach (var (lip, landing, width) in OrbitalCity.Falls)
            {
                int a = Level(lip.y), b = Level(landing.y);
                Assert.GreaterOrEqual(a, 0, $"턱 {lip} 수면 아님");
                Assert.AreEqual(a - 1, b, $"폭포 {lip} → {landing} = 바로 아래 수면");
                Assert.Greater(width, 0.3f);
            }
        }

        [Test]
        public void Train_StopLineAndPose()
        {
            Assert.AreEqual(-35.5f, OrbitalCity.FrontAngle, 1e-4f);
            Assert.AreEqual(OrbitalCity.FrontAngle + 4.4338f, OrbitalCity.NoseAngle, 1e-3f);
            Assert.Less(Quaternion.Angle(InteriorOrbitTrain.PoseAt(OrbitalCity.Lap), Quaternion.identity), 0.01f, "한 바퀴 = 제자리");
            // 피벗을 돌리면 앞 차가 각이 늘어나는 쪽으로 감 (운전석 = 앞)
            var front = OrbitalCity.Polar(OrbitalCity.TrackRadius, OrbitalCity.FrontAngle, 0f) - OrbitalCity.Hub;
            var moved = InteriorOrbitTrain.PoseAt(1f) * front;
            float a = Mathf.Atan2(moved.z, moved.x) * Mathf.Rad2Deg;
            Assert.AreEqual(OrbitalCity.FrontAngle + 1f / OrbitalCity.TrackRadius * Mathf.Rad2Deg, a, 1e-3f);
            Assert.AreEqual("딱 맞췄어요!", OrbitalCity.Grade(-0.2f));
            Assert.AreEqual("아주 좋아요", OrbitalCity.Grade(0.8f));
            Assert.AreEqual("좋아요", OrbitalCity.Grade(-2.5f));
            Assert.AreEqual("조금 아쉬워요", OrbitalCity.Grade(6f));
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
                col.enabled = col is MeshCollider ? col.name.EndsWith("_Shell") || col.name.EndsWith("_Glass") || col.name.EndsWith("_Train") : col.name == "PlatformGuard";
            Physics.SyncTransforms();
            ok = true;
            return root;
        }

        /// <summary>
        /// 옥상 전망대 (사용자 2026-11-11 "주변 기둥 때문에 움직일 수 있는 공간이 1/4"): 꼬인 줄기 넷이 대각선 화단과 붙어 승강기 앞 1/4만 다녔음 →
        /// 승강기 앞에서 걸어서 대각선 넷 · 문 다리 셋 · 뒤까지 모두 닿아야 함 (0.1 m 격자, 몸 반지름 0.28 · 발 위 0.5 ~ 1.6).
        /// </summary>
        [Test]
        public void Walk_RoofDeckAllAround()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                float roof = OrbitalCity.LiftStops[OrbitalCity.LiftStops.Length - 1];
                const float step = 0.1f, x0 = 11.5f, z0 = 11.5f;
                int n = Mathf.RoundToInt(9f / step) + 1;
                var node = new int[n, n];
                var pos = new List<Vector3>();
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        node[i, j] = -1;
                        var origin = new Vector3(x0 + i * step, roof + 1.7f, z0 + j * step);
                        if (!Physics.Raycast(origin, Vector3.down, out var hit, 2.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f || hit.point.y < roof - 0.3f)
                            continue;
                        var p = hit.point;
                        if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            continue;
                        node[i, j] = pos.Count;
                        pos.Add(p);
                    }
                }
                int At(Vector3 p)
                {
                    int i0 = Mathf.RoundToInt((p.x - x0) / step), j0 = Mathf.RoundToInt((p.z - z0) / step);
                    for (int r = 0; r <= 3; r++)
                        for (int di = -r; di <= r; di++)
                            for (int dj = -r; dj <= r; dj++)
                            {
                                int a = i0 + di, b = j0 + dj;
                                if (a >= 0 && b >= 0 && a < n && b < n && node[a, b] >= 0)
                                    return node[a, b];
                            }
                    return -1;
                }
                var seen = new bool[pos.Count];
                int start = At(OrbitalCity.Polar(1.6f, 270f, roof));   // 승강기 문 앞
                Assert.GreaterOrEqual(start, 0, "승강기 앞에 설 곳 없음");
                var q = new Queue<int>();
                q.Enqueue(start);
                seen[start] = true;
                while (q.Count > 0)
                {
                    var p = pos[q.Dequeue()];
                    int i = Mathf.RoundToInt((p.x - x0) / step), j = Mathf.RoundToInt((p.z - z0) / step);
                    foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int a = i + di, b = j + dj;
                        if (a < 0 || b < 0 || a >= n || b >= n || node[a, b] < 0 || seen[node[a, b]] || Mathf.Abs(pos[node[a, b]].y - p.y) > 0.36f)
                            continue;
                        seen[node[a, b]] = true;
                        q.Enqueue(node[a, b]);
                    }
                }
                foreach (float a in new[] { 0f, 45f, 90f, 135f, 180f, 225f, 315f })
                {
                    int k = At(OrbitalCity.Polar(a % 90f == 0f ? 3.6f : 3.0f, a, roof));
                    Assert.GreaterOrEqual(k, 0, $"옥상 {a}°: 설 곳 없음");
                    Assert.IsTrue(seen[k], $"옥상 {a}°: 승강기 앞에서 걸어서 못 감");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 고리 강 다리 넷 (사용자 2026-11-11 "다리를 넘어가면 계단과 겹친 방향으로 못 감"): 315° 다리 안쪽 끝이 바닥 단 계단 꼭대기와 겹쳐 한쪽이 막혔음 →
        /// 다리 가운데에서 내려 양옆 (다리 각 ± 12°, 언덕 앞 고리 길 · 계단 아랫 칸)까지 다리 근처 (± 32°) 안에서만 걸어 닿아야 함.
        /// 바닥 · 다리 · 계단 아랫 칸 (F + 0.8 아래)만 마디 (0.1 m 격자, 몸 반지름 0.28 · 발 위 0.5 ~ 1.6).
        /// </summary>
        [Test]
        public void Walk_BridgeLandingsBothWays()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                const float F = -1.3f, step = 0.1f, x0 = 1f, z0 = 1f;
                int n = Mathf.RoundToInt(30f / step) + 1;
                var node = new int[n, n];
                var pos = new List<Vector3>();
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        node[i, j] = -1;
                        float x = x0 + i * step, z = z0 + j * step, r = Mathf.Sqrt((x - 16f) * (x - 16f) + (z - 16f) * (z - 16f));
                        if (r < 9.6f || r > 14.8f)
                            continue;
                        if (!Physics.Raycast(new Vector3(x, F + 3.3f, z), Vector3.down, out var hit, 4.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.7f || hit.point.y > F + 0.8f)
                            continue;
                        var p = hit.point;
                        if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            continue;
                        node[i, j] = pos.Count;
                        pos.Add(p);
                    }
                }
                float Ang(Vector3 p) => Mathf.Repeat(Mathf.Atan2(p.z - 16f, p.x - 16f) * Mathf.Rad2Deg, 360f);
                foreach (float b in new[] { 45f, 135f, 225f, 315f })
                {
                    var c = OrbitalCity.Polar(12.8f, b, F);
                    int ci = Mathf.RoundToInt((c.x - x0) / step), cj = Mathf.RoundToInt((c.z - z0) / step), start = -1;
                    for (int r = 0; r <= 4 && start < 0; r++)
                        for (int di = -r; di <= r && start < 0; di++)
                            for (int dj = -r; dj <= r && start < 0; dj++)
                                if (node[ci + di, cj + dj] >= 0)
                                    start = node[ci + di, cj + dj];
                    Assert.GreaterOrEqual(start, 0, $"다리 {b}°: 다리 위에 설 곳 없음");
                    var seen = new bool[pos.Count];
                    var q = new Queue<int>();
                    q.Enqueue(start);
                    seen[start] = true;
                    while (q.Count > 0)
                    {
                        var p = pos[q.Dequeue()];
                        int i = Mathf.RoundToInt((p.x - x0) / step), j = Mathf.RoundToInt((p.z - z0) / step);
                        foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int a = i + di, k = j + dj;
                            if (a < 0 || k < 0 || a >= n || k >= n || node[a, k] < 0 || seen[node[a, k]])
                                continue;
                            var o = pos[node[a, k]];
                            if (Mathf.Abs(o.y - p.y) > 0.36f || Mathf.Abs(Mathf.DeltaAngle(Ang(o), b)) > 32f)
                                continue;
                            seen[node[a, k]] = true;
                            q.Enqueue(node[a, k]);
                        }
                    }
                    foreach (float s in new[] { -12f, 12f })
                    {
                        bool any = false, reached = false;
                        for (float r = 10.2f; r <= 11.3f; r += 0.05f)
                        {
                            var t = OrbitalCity.Polar(r, b + s, F);
                            int a = Mathf.RoundToInt((t.x - x0) / step), k = Mathf.RoundToInt((t.z - z0) / step);
                            if (node[a, k] < 0)
                                continue;
                            any = true;
                            reached |= seen[node[a, k]];
                        }
                        Assert.IsTrue(any, $"다리 {b}° 옆 {b + s}°: 언덕 앞에 설 곳 없음");
                        Assert.IsTrue(reached, $"다리 {b}°에서 내려 {b + s}° 쪽으로 못 감 (계단 · 다리 기둥에 막힘)");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 0.25 m 격자 기둥마다 아래로 쏜 모든 바닥 (높이가 다른 바닥 = 층) 중 몸 (반지름 0.28 · 발 위 0.5 ~ 1.6 — 계단 한 칸은 넘음)이 들어가는 곳을 마디로,
        /// 이웃 마디 높이 차 0.36 이하면 이어 걸음 (계단 한 칸 0.18). 스폰 (앞 고리 길)에서: 해치 21 · 승강장 · 아래 · 위 단 · 꼭대기 광장 · 뿌리 홀까지 닿음.
        /// </summary>
        [Test]
        public void Walk_ReachesHatchesStationAndHill()
        {
            var root = Spawn(out bool ok);
            if (!ok)
                Assert.Ignore("프리팹 없음 (Build Evolved Templates 먼저)");
            try
            {
                const float step = 0.25f, x0 = -3.8f, z0 = -3.8f, top = 12f;
                int n = Mathf.RoundToInt(39.6f / step) + 1;
                var nodes = new List<Vector3>();
                var cols = new List<int>[n, n];
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        // 한 충돌체 (몸체 _Shell)에 층이 여럿 (단 · 그 밑 언덕 속 길) → 맞은 자리 바로 밑에서 다시 쏨 (속에서 쏘면 뒷면은 안 맞고 다음 바닥이 맞음)
                        var origin = new Vector3(x0 + i * step, top, z0 + j * step);
                        for (int guard = 0; guard < 12 && Physics.Raycast(origin, Vector3.down, out var hit, origin.y - F + 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore); guard++)
                        {
                            origin = hit.point + Vector3.down * 0.01f;
                            if (hit.normal.y < 0.7f)
                                continue;
                            var p = hit.point;
                            if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.6f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))   // 몸 아래 0.22 m = 계단 한 칸 (0.175) · 다리 끝 턱은 넘음
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
                    for (int di = -1; di <= 1; di++)
                        for (int dj = -1; dj <= 1; dj++)
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
                var seen = new bool[nodes.Count];
                int start = Node(new Vector3(16f, F, 1.4f));
                Assert.GreaterOrEqual(start, 0, "스폰 자리에 설 곳 없음");
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
                void Reach(Vector3 p, string what)
                {
                    int k = Node(p);
                    Assert.GreaterOrEqual(k, 0, $"{what}: 설 곳 없음 {p}");
                    Assert.IsTrue(seen[k], $"{what}: 스폰에서 걸어서 못 감 {p}");
                }
                foreach (var (cell, off) in OrbitalCity.Hatches)
                {
                    var h = new Vector3(cell.x * 8f + off.x, F, cell.z * 8f + off.y);
                    Reach(h, $"해치 {cell}");
                    Assert.IsFalse(Physics.CheckBox(h + Vector3.up * 0.06f, new Vector3(0.7f, 0.04f, 0.7f), Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"해치 {cell} 뚜껑이 묻힘");
                }
                Reach(OrbitalCity.Polar(OrbitalCity.TrackRadius + 1.8f, OrbitalCity.FrontAngle, OrbitalCity.PlatformTop), "승강장 운전석 옆");
                Reach(OrbitalCity.Polar(9.3f, 0f, F + 2.8f), "아래 단");
                Reach(OrbitalCity.Polar(7.5f, 45f, F + 5.2f), "위 단");
                Reach(new Vector3(16f, F + 8f, 13.2f), "꼭대기 광장 (엘리베이터 앞)");
                Reach(new Vector3(17.6f, F, 16f), "뿌리 홀");
                Reach(OrbitalCity.Polar(19.55f, 237f, F + 0.18f), "호숫가 정자");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>주민 자리 발밑에 바닥 (± 0.12)이 있고 몸 (반지름 0.24)이 들어감 — 계단 · 벽 · 열차 · 엘리베이터에 묻히지 않음.</summary>
        [Test]
        public void ResidentSpots_StandOnFloor()
        {
            var template = AssetDatabase.LoadAssetAtPath<SpaceStation.Data.InteriorTemplate>("Assets/_Project/Data/Interior/IT_EvoOrbitalCity.asset");
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
    }
}
