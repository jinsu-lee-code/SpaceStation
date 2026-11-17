using System.Collections.Generic;
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
        /// 기둥도 천장 해치도 못 세우는 위 연결점 (사용자 결정 대기, 2026-11-17): 시민 회관 천구 돔 밑 셋 · 대형 토러스 시계탑 · 보안 본부 지휘탑 ·
        /// 심우주 통신기지 셋 (트인 땅은 풀 · 동물, 판석 길은 문 · 해치 · 주민 곁) · 정제 복합단지 여섯 (유리 온실 사이 길이 판보다 좁음).
        /// 모델에 착지 자리를 내거나 정하면 여기서 빼고 템플릿 · 도구를 고침.
        /// </summary>
        private static readonly (string Template, Vector3Int Cell)[] KnownGaps =
        {
            ("IT_EvoCivicHall", new Vector3Int(1, 0, 0)), ("IT_EvoCivicHall", new Vector3Int(2, 0, 0)), ("IT_EvoCivicHall", new Vector3Int(1, 0, 1)),
            ("IT_EvoGrandTorus", new Vector3Int(2, 1, 2)), ("IT_EvoSecurityHQ", new Vector3Int(1, 0, 1)),
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
