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
        private static readonly Dictionary<string, string> Pending = new Dictionary<string, string>
        {
            // 고리 · 하늘이 안쪽 대각선 네 칸 · 위층을 지나감 — 칸 목록을 늘릴지 내부를 칸 안으로 줄일지 (2026-11-06 보고)
            { "IT_EvoGrandTorus", "고리가 칸 목록 밖 안쪽 대각선 칸을 지나감" },
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
                        float d = Outside(p, cells);
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

        /// <summary>칸 상자 (옆 0.06 · 위 2m 여유) 중 가장 가까운 것까지 벗어난 거리. 안이면 0.</summary>
        private static float Outside(Vector3 p, List<Vector3Int> cells)
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
                float dy = Mathf.Max(0f, Mathf.Max(up - UpTolerance, down - SideTolerance));
                best = Mathf.Min(best, new Vector3(dx, dy, dz).magnitude);
                if (best <= 0f)
                    return 0f;
            }
            return best;
        }
    }
}
