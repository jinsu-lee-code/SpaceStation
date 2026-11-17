using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Editor;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 진화 단계별 통로 ③ 빛 기둥 승강판 (하늘이 트인 2단계+ 방의 위 연결점, <see cref="InteriorSkyLiftSpots"/>):
    /// 외형 위 연결점마다 위로 가는 길 하나 (승강판 또는 지붕 밑 천장 해치) — 아직 자리를 못 정한 곳은 <see cref="KnownGaps"/>,
    /// 승강판은 그 칸 안 · 판이 땅에 앉음 (높이차 0.15 이하 — 판 치마가 덮음) · 판에서 하늘 고리까지 기둥이 빔 (충돌체 · 충돌체 없는 장식 정점) · 들어오는 곳에서 걸어서 닿음 · 고리는 하늘 껍질 밑.
    /// </summary>
    public class InteriorSkyLiftTests
    {
        private const string KitPath = "Assets/_Project/Data/Interior/InteriorKit.asset";
        private static readonly Vector3 Away = new Vector3(20000f, -6000f, 20000f);

        /// <summary>
        /// 아직 위로 가는 길이 없는 위 연결점 (2026-11-17 사용자 "통신기지랑 정제 복합단지는 모델에 착지 자리 내는 걸로" — 착지 자리 시안 대기):
        /// 심우주 통신기지 셋 (평평한 빈터가 칸마다 1 ~ 2 m — 아기 사슴 · 주민 자리 · 해치 · 문 앞 길) · 정련 콤비나트 여섯 (유리 볼트 격납고 · 돔 산책로 사이 길이 판보다 좁음).
        /// 모델에 착지 자리를 내면 여기서 빼고 도구 (<see cref="InteriorSkyLiftSpots.Targets"/>)를 고침. 시민 회관 돔 칸 셋 = 의료동 천장 해치, 두 탑 = 0.65배 천장 해치 (사용자 "꼭대기방 천장해치로").
        /// </summary>
        private static readonly (string Template, Vector3Int Cell)[] KnownGaps =
        {
            ("IT_EvoDeepSpaceComm", new Vector3Int(0, 0, 0)), ("IT_EvoDeepSpaceComm", new Vector3Int(1, 0, 0)), ("IT_EvoDeepSpaceComm", new Vector3Int(2, 0, 0)),
            ("IT_EvoRefineryCombine", new Vector3Int(0, 0, 0)), ("IT_EvoRefineryCombine", new Vector3Int(1, 0, 0)), ("IT_EvoRefineryCombine", new Vector3Int(2, 0, 0)),
            ("IT_EvoRefineryCombine", new Vector3Int(0, 0, 1)), ("IT_EvoRefineryCombine", new Vector3Int(1, 0, 1)), ("IT_EvoRefineryCombine", new Vector3Int(2, 0, 1)),
        };

        private static List<InteriorTemplate> OpenTemplates()
        {
            var kit = AssetDatabase.LoadAssetAtPath<InteriorKit>(KitPath);
            var list = new List<InteriorTemplate>();
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate", new[] { "Assets/_Project/Data/Interior" }))
            {
                var t = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (InteriorSkyLiftSpots.IsOpenSky(t, kit))
                    list.Add(t);
            }
            return list;
        }

        [Test]
        public void EveryUpFace_HasOneWayUp()
        {
            var problems = new StringBuilder();
            int lifts = 0;
            foreach (var t in OpenTemplates())
            {
                foreach (var cell in InteriorSkyLiftSpots.UpFaces(t.Module))
                {
                    int ways = 0;
                    foreach (var s in t.Sockets)
                    {
                        if (s.Direction == Vector3Int.up && s.Cell == cell)
                        {
                            ways++;
                            if (s.SkyLift)
                                lifts++;
                        }
                    }
                    bool gap = System.Array.IndexOf(KnownGaps, (t.name, cell)) >= 0;
                    if (gap ? ways != 0 : ways != 1)
                        problems.AppendLine($"{t.name} {cell}: 위로 가는 길 {ways}개" + (gap ? " (자리 못 정한 곳인데 생김 — KnownGaps에서 빼기)" : ""));
                }
                foreach (var s in t.Sockets)
                {
                    if (s.SkyLift && s.Direction != Vector3Int.up)
                        problems.AppendLine($"{t.name} {s.Cell}: 승강판이 위 방향이 아님");
                }
            }
            Assert.GreaterOrEqual(lifts, 30, "승강판이 있어야 함 (메뉴 SpaceStation/Interior/Set Sky Lift Spots)");
            Assert.IsEmpty(problems.ToString(), problems.ToString());
        }

        [Test]
        public void SkyLifts_SitOnGround_ColumnClear_Reachable()
        {
            var problems = new StringBuilder();
            int checkedLifts = 0;
            foreach (var t in OpenTemplates())
            {
                bool any = false;
                foreach (var s in t.Sockets)
                    any |= s.SkyLift;
                if (!any)
                    continue;
                var root = Object.Instantiate(t.Prefab);
                root.transform.SetPositionAndRotation(Away, Quaternion.identity);
                Physics.SyncTransforms();
                try
                {
                    var graph = InteriorWalkGraph.ForTemplate(t, root.transform);
                    var decor = new InteriorSkyLiftSpots.DecorGrid(root.transform);
                    foreach (var s in t.Sockets)
                    {
                        if (!s.SkyLift)
                            continue;
                        checkedLifts++;
                        var center = Away + (Vector3)s.Cell * 8f;
                        var pad = center + s.Offset;
                        string at = $"{t.name} {s.Cell} 판 {s.Offset}";
                        if (Mathf.Abs(s.Offset.x) > 3.15f || Mathf.Abs(s.Offset.z) > 3.15f)
                            problems.AppendLine($"{at}: 칸 가장자리에 너무 가까움");
                        if (Mathf.Abs(s.Depth - InteriorSkyLiftSpots.RingDepth) > 0.001f)
                            problems.AppendLine($"{at}: 고리 깊이 {s.Depth}");
                        if (!InteriorSkyLiftSpots.PadSurface(pad, out float top, out float uneven) || Mathf.Abs(top - pad.y) > 0.02f)
                            problems.AppendLine($"{at}: 판 밑 땅이 고르지 않음 / 판이 떠 있음");
                        if (!InteriorSkyLiftSpots.ColumnClear(pad, center.y + s.Depth, InteriorSkyLiftSpots.ColumnRadii[InteriorSkyLiftSpots.ColumnRadii.Length - 1]))
                            problems.AppendLine($"{at}: 판에서 하늘 고리까지 기둥이 막힘");
                        if (!decor.Clear(pad, center.y + s.Depth))
                            problems.AppendLine($"{at}: 판 · 기둥 속에 장식 (동물 · 케이블 · 풀 · 덩굴)");
                        if (center.y + s.Depth - pad.y < InteriorSkyLiftSpots.MinRise)
                            problems.AppendLine($"{at}: 떠오를 높이가 너무 낮음");
                        int k = graph.Node(pad, 0.4f);
                        if (k < 0 || !graph.Reached(k))
                            problems.AppendLine($"{at}: 들어오는 곳에서 걸어서 못 감");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Physics.SyncTransforms();
                }
            }
            Assert.GreaterOrEqual(checkedLifts, 30);
            Assert.IsEmpty(problems.ToString(), problems.ToString());
        }

        /// <summary>
        /// 좁은 탑 방 천장 해치 (0.65배 — 보안 본부 감시탑 전망실 · 대형 토러스 시계탑 방, 사용자 2026-11-17 "꼭대기방 천장해치로"):
        /// 줄인 틀 (1.9 × 배율) 자리 천장이 평평 · 그 자리에 천장 빛 고리 · 등 (충돌체 없는 정점)이 없음 (빌더가 끊음) · 틀이 엘리베이터 샤프트 · 벽에 안 닿음 (위 광선이 같은 천장) ·
        /// 밑 바닥이 걸어서 닿고 천장까지 2.0 ~ 4.0 m.
        /// </summary>
        [Test]
        public void TowerHatches_FitUnderCeiling()
        {
            var problems = new StringBuilder();
            int towers = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate", new[] { "Assets/_Project/Data/Interior" }))
            {
                var t = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (t == null || t.Prefab == null)
                    continue;
                var small = t.Sockets.Where(s => s.Direction == Vector3Int.up && !s.SkyLift && s.HatchScale < 0.99f).ToList();
                if (small.Count == 0)
                    continue;
                var root = Object.Instantiate(t.Prefab);
                root.transform.SetPositionAndRotation(Away, Quaternion.identity);
                Physics.SyncTransforms();
                try
                {
                    var graph = InteriorWalkGraph.ForTemplate(t, root.transform);
                    var decor = new InteriorSkyLiftSpots.DecorGrid(root.transform);
                    foreach (var s in small)
                    {
                        towers++;
                        var hatch = Away + (Vector3)s.Cell * 8f + s.Offset + Vector3.up * s.Depth;
                        float half = 0.95f * s.HatchScale;
                        string at = $"{t.name} {s.Cell} 해치 {s.Offset} ×{s.HatchScale}";
                        if (!Physics.Raycast(hatch + Vector3.down * 0.3f, Vector3.down, out var floor, 5f, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.9f)
                        {
                            problems.AppendLine($"{at}: 밑에 바닥 없음");
                            continue;
                        }
                        float h = hatch.y - floor.point.y;
                        if (h < InteriorSkyLiftSpots.CeilingMin || h > InteriorSkyLiftSpots.CeilingMax)
                            problems.AppendLine($"{at}: 천장 높이 {h:F2}");
                        for (int a = 0; a < 9; a++)
                        {
                            var q = new Vector3(hatch.x + (a % 3 - 1) * (half - 0.02f), floor.point.y + 0.3f, hatch.z + (a / 3 - 1) * (half - 0.02f));
                            if (!Physics.Raycast(q, Vector3.up, out var c, 5f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(c.point.y - hatch.y) > 0.02f || c.normal.y > -0.95f)
                                problems.AppendLine($"{at}: 틀 자리 {a} 천장이 다름 (샤프트 · 벽 · 다른 높이)");
                        }
                        if (decor.AnyBox(hatch, half + 0.03f, hatch.y - 0.3f, hatch.y - 0.012f))
                            problems.AppendLine($"{at}: 틀 자리에 천장 장식 (빛 고리 · 등)");
                        int k = graph.Node(floor.point, 0.5f);
                        if (k < 0 || !graph.Reached(k))
                            problems.AppendLine($"{at}: 밑 바닥에 걸어서 못 감");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Physics.SyncTransforms();
                }
            }
            Assert.GreaterOrEqual(towers, 2, "탑 천장 해치 둘 (보안 본부 · 대형 토러스)");
            Assert.IsEmpty(problems.ToString(), problems.ToString());
        }

        [Test]
        public void KitHasStagePiecesAndShaft()
        {
            var kit = AssetDatabase.LoadAssetAtPath<InteriorKit>(KitPath);
            Assert.IsNotNull(kit);
            Assert.IsNotNull(kit.SkyLiftShaft, "빛 기둥 재질 (메뉴 Rebuild Kit Asset)");
            Assert.AreEqual("SpaceStation/LightShaft", kit.SkyLiftShaft.shader.name);
            foreach (var t in OpenTemplates())
            {
                Assert.IsNotNull(kit.SkyLiftPadFor(t.Module), $"{t.name}: 단계 승강판 조각");
                Assert.IsNotNull(kit.SkyLiftRingFor(t.Module), $"{t.name}: 단계 하늘 고리 조각");
                Assert.GreaterOrEqual(kit.SkyLiftPadFor(t.Module).AccentSlot, 0, $"{t.name}: 판 빛 테 = 강조 슬롯 (모듈 색)");
            }
        }
    }
}
