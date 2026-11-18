using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 내부 템플릿 모델이 자기 칸 밖으로 나가지 않음 (사용자 2026-11-06 "벽의 일부분이 뚫려서 관통되어 보이는 문제 … 이후에 만드는 모델에서도 같은 문제 안생기게"):
    /// 옆 칸으로 나간 조각은 그 칸에 모듈을 붙이면 옆 방 벽 · 바닥을 뚫고 보임 (방사선 차폐벽 ㄱ자 안쪽 모서리 갑판 0.26m, 시민 회관 라운지 처마 0.1m, 이전 치안 본부 하늘 조각 0.54m).
    /// 옆 방 벽은 칸 경계에서 0.05 안쪽이라 수평은 0.06까지만. 위로는 위 칸 방 바닥 (경계 + 2.5 이상) 아래 빈 곳이라 2m까지 봐줌.
    /// 문 · 해치 키트 조각은 <see cref="InteriorKitSkyPatchTests"/>가 따로 봄.
    /// </summary>
    public class InteriorTemplateBoundsTests
    {
        private const float CellSize = 8f;
        private const float SideTolerance = 0.06f;
        private const float UpTolerance = 2f;

        /// <summary>사용자 결정을 기다리는 모델 (이유와 함께). 결정되면 빼서 검사에 다시 넣음.</summary>
        private static readonly Dictionary<string, string> Pending = new Dictionary<string, string>();

        /// <summary>
        /// 위로 더 봐주는 모델 (m, 이유). 옆 칸 검사는 그대로.
        /// 대형 토러스: 고리가 지나가는 안쪽 대각선 네 칸은 "비워 둘 칸"으로 막음 (사용자 2026-11-06). 하늘 껍질은 시계탑 방에서 내려다보는 마을이
        /// 가려지지 않게 바퀴 전체 위층 끝 (11.95)까지 — 고리 위 칸에 모듈을 지으면 그 방이 하늘 안에 보임 (위층을 막을지 사용자 결정 대기).
        /// </summary>
        private static readonly Dictionary<string, (float up, string reason)> ExtraUp = new Dictionary<string, (float, string)>
        {
            { "IT_EvoGrandTorus", (8f, "하늘이 고리 위층까지 덮음 (시계탑 전망)") },
        };

        [Test]
        public void TemplatesStayInsideTheirCells()
        {
            var problems = new StringBuilder();
            int checkedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate"))
            {
                var template = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (template == null || template.Prefab == null || template.Module == null || Pending.ContainsKey(template.name))
                    continue;
                checkedCount++;
                var cells = template.Module.CellOffsets.Count > 0 ? template.Module.CellOffsets.ToList() : new List<Vector3Int> { Vector3Int.zero };
                cells.AddRange(template.Module.KeepClearCells); // 비워 둘 칸 (대형 토러스 고리 안쪽)은 아무것도 못 지으므로 지나가도 됨
                float upTolerance = ExtraUp.TryGetValue(template.name, out var extra) ? extra.up : UpTolerance;
                var root = template.Prefab.transform;
                foreach (var filter in template.Prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                        continue;
                    var toRoot = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                    float worst = 0f;
                    Vector3 at = Vector3.zero;
                    foreach (var v in filter.sharedMesh.vertices)
                    {
                        var p = toRoot.MultiplyPoint3x4(v);
                        float d = Outside(p, cells, upTolerance);
                        if (d > worst)
                        {
                            worst = d;
                            at = p;
                        }
                    }
                    if (worst > 0f)
                        problems.AppendLine($"{template.name} / {filter.name}: 칸 밖 {worst:F2}m @ ({at.x:F2}, {at.y:F2}, {at.z:F2})");
                }
            }
            Assert.Greater(checkedCount, 0, "검사한 템플릿이 있어야 함");
            Assert.IsEmpty(problems.ToString(), "옆 칸으로 나간 조각 (옆 방을 뚫고 보임):\n" + problems);
        }

        /// <summary>
        /// 하늘 껍질 (2단계 개방형 방의 바깥 화면)은 문 자리마다 뚫려 있음 (2026-11-06 코드 검토): Blender에서 2cm 홈만 남아 열린 문 안을 하늘 그림이 덮었음.
        /// 문 자리 가운데 (바닥 + 1.1) 에서 문 깊이 −0.5 → +1.0 사이에 하늘 삼각형이 없어야 함. 닫힌 자리는 키트 하늘 조각이 따로 메움.
        /// </summary>
        [Test]
        public void SkyShellIsOpenAtEveryDoor()
        {
            var problems = new StringBuilder();
            int checkedDoors = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate"))
            {
                var template = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (template == null || template.Prefab == null)
                    continue;
                MeshFilter sky = null;
                foreach (Transform part in template.Prefab.transform)
                {
                    var r = part.GetComponent<MeshRenderer>();
                    if (part.name.EndsWith("_Sky") && r != null && r.sharedMaterial != null && r.sharedMaterial.name.Contains("Evo2Sky"))
                        sky = part.GetComponent<MeshFilter>();
                }
                if (sky == null || sky.sharedMesh == null)
                    continue;
                var toRoot = template.Prefab.transform.worldToLocalMatrix * sky.transform.localToWorldMatrix;
                var verts = sky.sharedMesh.vertices.Select(v => toRoot.MultiplyPoint3x4(v)).ToArray();
                var tris = sky.sharedMesh.triangles;
                foreach (var socket in template.Sockets)
                {
                    if (socket.Direction.y != 0)
                        continue;
                    checkedDoors++;
                    var dir = (Vector3)socket.Direction;
                    var start = (Vector3)socket.Cell * CellSize + dir * (socket.Depth - 0.5f);
                    start.y = socket.Cell.y * CellSize - 1.3f + 1.1f;
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        if (Hit(start, dir, 1.5f, verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]]))
                        {
                            problems.AppendLine($"{template.name}: 문 자리 칸 {socket.Cell} 방향 {socket.Direction} 깊이 {socket.Depth} 앞을 하늘이 막음");
                            break;
                        }
                    }
                }
            }
            Assert.Greater(checkedDoors, 0, "하늘 껍질 방의 문 자리가 있어야 함");
            Assert.IsEmpty(problems.ToString(), "열린 문이 하늘 그림에 가려짐:\n" + problems);
        }

        /// <summary>
        /// 장식 · 구조물에 걸린 바닥 해치 중 사용자 결정을 기다리는 것 (템플릿, 칸) — 이유와 함께. 자리를 정해 옮기면 뺌.
        /// </summary>
        private static readonly Dictionary<(string, Vector3Int), string> PendingHatches = new Dictionary<(string, Vector3Int), string>();

        /// <summary>
        /// 바닥 해치 뚜껑 가운데 (±0.45 m) 위 2 m에 충돌체가 없고 1.5 m 아래에 충돌체 없는 장식 (동물 · 구슬 등)이 없으며, 뚜껑 둘레 1.3 m 8방향 중 한 곳 이상에 사람이 설 수 있음 —
        /// 융합 발전소 가운데 칸 해치가 칸 중심 = 융합로 받침 밑에 묻혀, 시선이 뚜껑에 닿지 않아 위에서 내려갈 수 없었음 (2026-11-18 플레이).
        /// 머리 위 전구 줄 (창고 단지 1.65 m) · 연못 마루 (바이오스피어 — 한쪽으로만 들어감)는 괜찮음.
        /// </summary>
        [Test]
        public void FloorHatchesAreOpen()
        {
            var away = new Vector3(30000f, -7000f, 30000f);
            var problems = new StringBuilder();
            int hatches = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate"))
            {
                var template = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (template == null || template.Prefab == null || !template.Sockets.Any(s => s.Direction.y < 0))
                    continue;
                var root = Object.Instantiate(template.Prefab);
                root.transform.SetPositionAndRotation(away, Quaternion.identity);
                Physics.SyncTransforms();
                try
                {
                    var decor = new List<Vector3>();
                    foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (f.sharedMesh != null && f.GetComponent<Collider>() == null && !f.name.EndsWith("_Sky"))
                            decor.AddRange(f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v)));
                    }
                    foreach (var s in template.Sockets)
                    {
                        if (s.Direction.y >= 0 || PendingHatches.ContainsKey((template.name, s.Cell)))
                            continue;
                        hatches++;
                        var hatch = away + (Vector3)s.Cell * CellSize + s.Offset + Vector3.down * s.Depth;
                        float half = 0.45f * s.HatchScale;
                        string at = $"{template.name} {s.Cell} 해치 ({s.Offset.x:F2}, {s.Offset.z:F2})";
                        float covered = 0f;
                        for (int i = 0; i < 5; i++)
                        {
                            for (int j = 0; j < 5; j++)
                            {
                                var top = hatch + new Vector3((i / 2f - 1f) * half, 2f, (j / 2f - 1f) * half);
                                if (Physics.Raycast(top, Vector3.down, out var hit, 2.3f, ~0, QueryTriggerInteraction.Ignore))
                                    covered = Mathf.Max(covered, hit.point.y - hatch.y);
                            }
                        }
                        if (covered > 0.12f)
                            problems.AppendLine($"{at}: 뚜껑 위에 물체 (바닥 + {covered:F2} m)");
                        if (decor.Any(v => Mathf.Abs(v.x - hatch.x) < half && Mathf.Abs(v.z - hatch.z) < half && v.y > hatch.y + 0.15f && v.y < hatch.y + 1.5f))
                            problems.AppendLine($"{at}: 뚜껑 위에 장식");
                        int stand = 0;
                        for (int a = 0; a < 8; a++)
                        {
                            var p = hatch + Quaternion.Euler(0f, a * 45f, 0f) * Vector3.forward * 1.3f;
                            if (Physics.Raycast(p + Vector3.up * 0.4f, Vector3.down, out var ground, 0.8f, ~0, QueryTriggerInteraction.Ignore)
                                && !Physics.CheckCapsule(ground.point + Vector3.up * 0.5f, ground.point + Vector3.up * 1.6f, 0.28f, ~0, QueryTriggerInteraction.Ignore))
                                stand++;
                        }
                        if (stand < 1)
                            problems.AppendLine($"{at}: 곁에 설 자리 없음");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Physics.SyncTransforms();
                }
            }
            Assert.Greater(hatches, 300, "바닥 해치가 있어야 함");
            Assert.IsEmpty(problems.ToString(), "바닥 해치를 쓸 수 없음:\n" + problems);
        }

        /// <summary>
        /// 문 안쪽 바닥이 통로 · 다리 바닥 (칸 바닥 −1.3)보다 낮은데 층계참을 아직 못 정한 방 — 이유와 함께. 고치면 뺌.
        /// </summary>
        private static readonly Dictionary<string, string> PendingDoorSills = new Dictionary<string, string>
        {
        };

        /// <summary>
        /// 열린 문 자리 바로 안쪽 (문 깊이 − 0.25 m) 바닥이 통로 · 다리 바닥 (칸 바닥 −1.3)과 0.3 m 안 — 사람이 넘는 턱은 0.3 m까지 (그 안쪽은 층계참 · 계단이어도 됨, 방마다 걷기 시험).
        /// 심우주 통신기지 바닥이 0.78 m 낮아 다리에 오르지 못했고 (걸어 보니 문 앞에서 멈춤), 그 턱 아래로 하늘 껍질이 검게 보였음 (2026-11-18).
        /// </summary>
        [Test]
        public void DoorSillsMatchTubeFloor()
        {
            var away = new Vector3(30000f, -7000f, 30000f);
            var problems = new StringBuilder();
            int doors = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate"))
            {
                var template = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (template == null || template.Prefab == null || PendingDoorSills.ContainsKey(template.name) || !template.Sockets.Any(s => s.Direction.y == 0 && !s.SealedWall))
                    continue;
                var root = Object.Instantiate(template.Prefab);
                root.transform.SetPositionAndRotation(away, Quaternion.identity);
                Physics.SyncTransforms();
                try
                {
                    foreach (var s in template.Sockets)
                    {
                        if (s.Direction.y != 0 || s.SealedWall || s.DoorLift)   // 문 승강판 = 문 앞 바닥 없음 (판까지 내려앉음)
                            continue;
                        doors++;
                        var c = away + (Vector3)s.Cell * CellSize;
                        float sill = c.y - 1.3f;
                        foreach (float back in new[] { 0.25f })
                        {
                            var p = c + (Vector3)s.Direction * (s.Depth - back);
                            if (!Physics.Raycast(new Vector3(p.x, sill + 2f, p.z), Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
                                problems.AppendLine($"{template.name} {s.Cell} {s.Direction}: 문 안 {back} m 바닥 없음");
                            else if (Mathf.Abs(hit.point.y - sill) > 0.3f)
                                problems.AppendLine($"{template.name} {s.Cell} {s.Direction}: 문 안 {back} m 바닥이 문턱과 {hit.point.y - sill:+0.00;-0.00} m");
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Physics.SyncTransforms();
                }
            }
            Assert.Greater(doors, 400, "문 자리가 있어야 함");
            Assert.IsEmpty(problems.ToString(), "문턱이 통로 바닥과 맞지 않음:\n" + problems);
        }

        private static bool Hit(Vector3 origin, Vector3 dir, float length, Vector3 a, Vector3 b, Vector3 c)
        {
            var e1 = b - a;
            var e2 = c - a;
            var h = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, h);
            if (Mathf.Abs(det) < 1e-7f)
                return false;
            float inv = 1f / det;
            var s = origin - a;
            float u = inv * Vector3.Dot(s, h);
            if (u < 0f || u > 1f)
                return false;
            var q = Vector3.Cross(s, e1);
            float v = inv * Vector3.Dot(dir, q);
            if (v < 0f || u + v > 1f)
                return false;
            float t = inv * Vector3.Dot(e2, q);
            return t >= 0f && t <= length;
        }

        /// <summary>칸 상자 (옆 0.06 · 위 upTolerance 여유) 중 가장 가까운 것까지 벗어난 거리. 안이면 0.</summary>
        private static float Outside(Vector3 p, List<Vector3Int> cells, float upTolerance)
        {
            const float Half = CellSize * 0.5f;
            float best = float.MaxValue;
            foreach (var c in cells)
            {
                var center = (Vector3)c * CellSize;
                float dx = Mathf.Max(0f, Mathf.Abs(p.x - center.x) - Half - SideTolerance);
                float dz = Mathf.Max(0f, Mathf.Abs(p.z - center.z) - Half - SideTolerance);
                float up = p.y - center.y - Half;
                float down = center.y - Half - p.y;
                float dy = Mathf.Max(0f, Mathf.Max(up - upTolerance, down - SideTolerance));
                best = Mathf.Min(best, new Vector3(dx, dy, dz).magnitude);
                if (best <= 0f)
                    return 0f;
            }
            return best;
        }
    }
}
