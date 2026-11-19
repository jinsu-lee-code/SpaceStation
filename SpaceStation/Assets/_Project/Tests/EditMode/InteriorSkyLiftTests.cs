using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Editor;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 진화 단계별 통로 ③ 빛 기둥 승강판 (하늘이 트인 2단계+ 방의 위 연결점, <see cref="InteriorSkyLiftSpots"/>):
    /// 외형 위 연결점마다 위로 가는 길 하나 (승강판 또는 지붕 밑 천장 해치), 1 · 2단계 · 기본 방은 모든 윗면 · 아랫면 (모든 면 연결 ③) — 아직 자리를 못 정한 곳은 <see cref="KnownUpGaps"/> · <see cref="KnownDownGaps"/>,
    /// 승강판은 그 칸 안 · 판이 땅에 앉음 (높이차 0.15 이하 — 판 치마가 덮음) · 판에서 고리까지 기둥이 빔 (충돌체 · 충돌체 없는 장식 정점) · 들어오는 곳에서 걸어서 닿음 · 고리는 하늘 껍질 밑 (기둥이 막히는 방은 천장 · 유리 돔 밑).
    /// </summary>
    public class InteriorSkyLiftTests
    {
        private const string KitPath = "Assets/_Project/Data/Interior/InteriorKit.asset";
        private static readonly Vector3 Away = new Vector3(20000f, -6000f, 20000f);

        /// <summary>
        /// 아직 위로 가는 길이 없는 윗면. 심우주 통신기지 셋 · 정련 콤비나트 여섯은 모델에 착지 원판을 냄 (2026-11-17 사용자 "모델에 착지 자리 내는 걸로" · 시안 2,
        /// <see cref="InteriorSkyLiftSpots.Targets"/>), 시민 회관 돔 칸 셋 = 의료동 천장 해치, 두 탑 = 0.65배 천장 해치 (사용자 "꼭대기방 천장해치로").
        /// 모든 면 연결 ③ (2026-11-19) 마무리: 대형 토러스 모서리 넷 = 현관 정자, 바큇살 넷 = 굴 가운데 등탑 천장 해치, 화물 환적장 (2,0,1) = 능선 밑 작은 천장 해치 (아래 해치를 옮김),
        /// 아콜로지 꼭대기 넷 = 닫힌 면 (알 꼭대기 바늘 첨탑 — 사용자 "윗면 닫기"). 새로 생기면 여기에 까닭과 함께.
        /// </summary>
        private static readonly (string Template, Vector3Int Cell)[] KnownUpGaps =
        {
        };

        /// <summary>아직 아래로 가는 길이 없는 아랫면. 고치면 여기서 뺌 (아콜로지 1층 넷은 2026-11-19 광장 귀퉁이 해치로 풀림).</summary>
        private static readonly (string Template, Vector3Int Cell)[] KnownDownGaps =
        {
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

        /// <summary>
        /// 문 승강판 (2층 높이 옆문 — 모든 면 연결 ②-4, <see cref="InteriorAutoDoors"/>): 판이 땅 위 · 판에서 문턱 + 2.3까지 기둥이 빔 · 걸어서 닿음 · 판 테가 벽에 안 겹침 ·
        /// 문턱보다 1.2 m 넘게 낮음 · 판 ~ 문 떠오른 길 (문 폭, 문턱 −0.3 ~ +2.3)에 충돌체가 없음 · 문 앞 바닥이 없음 (있으면 승강판 말고 문).
        /// </summary>
        [Test]
        public void DoorLifts_SitOnGround_PathClear_Reachable()
        {
            var problems = new StringBuilder();
            int checkedLifts = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate", new[] { "Assets/_Project/Data/Interior" }))
            {
                var t = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (t == null || t.Prefab == null || !t.Sockets.Any(s => s.DoorLift))
                    continue;
                var root = Object.Instantiate(t.Prefab);
                root.transform.SetPositionAndRotation(Away, Quaternion.identity);
                Physics.SyncTransforms();
                try
                {
                    var graph = InteriorWalkGraph.ForTemplate(t, root.transform);
                    foreach (var s in t.Sockets)
                    {
                        if (!s.DoorLift)
                            continue;
                        checkedLifts++;
                        var center = Away + (Vector3)s.Cell * 8f;
                        float sill = center.y - 1.3f;
                        var pad = center + s.Offset;
                        var d = (Vector3)s.Direction;
                        var door = center + d * s.Depth;
                        door.y = sill;
                        string at = $"{t.name} {s.Cell} {s.Direction} 판 {s.Offset}";
                        if (s.Direction.y != 0)
                            problems.AppendLine($"{at}: 문 승강판이 옆문이 아님");
                        if (!InteriorSkyLiftSpots.PadSurface(pad, out float top, out _) || Mathf.Abs(top - pad.y) > 0.02f)
                            problems.AppendLine($"{at}: 판 밑 땅이 고르지 않음 / 판이 떠 있음");
                        bool bridge = InteriorGeometry.IsBridgeLift(pad.y, sill);   // 빛 다리 (문 앞 턱 너머 물길 — 화물 환적장, 2026-11-19)
                        if (!bridge)
                            problems.AppendLine($"{at}: 2층 이상 빛 기둥 문 승강판 — 쓰지 않음 (사용자 2026-11-19: 2층 이상 옆문은 모델에 그 높이 바닥 · 발코니가 있는 면만)");
                        float head = bridge ? sill + InteriorGeometry.BridgeLiftRise + 1.9f : sill + 2.3f;
                        if (!InteriorSkyLiftSpots.ColumnClear(pad, head, InteriorSkyLiftSpots.ColumnRadii[InteriorSkyLiftSpots.ColumnRadii.Length - 1]))
                            problems.AppendLine($"{at}: 판에서 문턱 위까지 기둥이 막힘");
                        if (!bridge && sill - pad.y < 1.2f)
                            problems.AppendLine($"{at}: 문턱보다 {sill - pad.y:F2} m 아래 — 승강판 말고 계단 · 문간");
                        if (bridge)
                        {
                            // 빛 다리: 문 앞 턱이 있고 건너는 길 (판 ~ 턱, 문턱 + 2.5 위)이 빔
                            if (!Physics.Raycast(door - d * 0.12f + Vector3.up * 0.5f, Vector3.down, 0.8f, ~0, QueryTriggerInteraction.Ignore))
                                problems.AppendLine($"{at}: 빛 다리 문 앞 턱이 없음");
                            var over = door - d * InteriorGeometry.BridgeInset;   // 문틀 앞 오르내리는 자리
                            var col = over + Vector3.up * ((InteriorGeometry.BridgeLowRise + 0.05f + InteriorGeometry.BridgeLiftRise + 1.9f) * 0.5f);
                            if (Physics.CheckBox(col, new Vector3(0.45f, (InteriorGeometry.BridgeLiftRise + 1.85f - InteriorGeometry.BridgeLowRise) * 0.5f, 0.3f), Quaternion.LookRotation(d), ~0, QueryTriggerInteraction.Ignore))
                                problems.AppendLine($"{at}: 빛 다리 문틀 앞 오르내리는 자리가 막힘");
                            // 문틀 앞 → 문 밖 1.1 m 문턱까지 비스듬히 내려서는 길 (몸)
                            for (float run = 0f; run <= InteriorGeometry.BridgeInset - InteriorGeometry.Thickness + 0.001f; run += 0.1f)
                            {
                                float feet = sill + InteriorGeometry.BridgeLowRise + (0.03f - InteriorGeometry.BridgeLowRise) * run / (InteriorGeometry.BridgeInset + 1.1f);
                                var body = over + d * run;
                                body.y = feet + 0.03f + 0.935f;
                                if (Physics.CheckBox(body, new Vector3(0.45f, 0.935f, 0.05f), Quaternion.LookRotation(d), ~0, QueryTriggerInteraction.Ignore))
                                {
                                    problems.AppendLine($"{at}: 빛 다리 문으로 내려서는 길이 막힘 (문틀 앞에서 {run:F1} m)");
                                    break;
                                }
                            }
                            var fl = new Vector3(pad.x, 0f, pad.z);
                            var axis = new Vector3(over.x, 0f, over.z) - fl;
                            var m = fl + axis * 0.5f;
                            m.y = sill + InteriorGeometry.BridgeLiftRise + 0.85f;
                            if (axis.magnitude > 0.5f && Physics.CheckBox(m, new Vector3(0.45f, 1.05f, axis.magnitude * 0.5f), Quaternion.LookRotation(axis.normalized), ~0, QueryTriggerInteraction.Ignore))
                                problems.AppendLine($"{at}: 빛 다리 건너는 길이 막힘");
                            if (axis.magnitude > 12.1f)
                                problems.AppendLine($"{at}: 빛 다리가 {axis.magnitude:F1} m — 판이 문에서 너무 멂");
                            // 다른 판 (위 승강판 · 문 승강판)과는 같은 자리 (판을 같이 씀) 아니면 판 테끼리 0.4 m 넘게
                            foreach (var o in t.Sockets)
                            {
                                if (ReferenceEquals(o, s) || !(o.SkyLift || o.DoorLift))
                                    continue;
                                var op = Away + (Vector3)o.Cell * 8f + o.Offset;
                                float gap = new Vector2(op.x - pad.x, op.z - pad.z).magnitude;
                                if (Mathf.Abs(op.y - pad.y) < 1.5f && gap > 0.05f && gap < InteriorSkyLiftSpots.PadRadius * 2f + 0.4f)
                                    problems.AppendLine($"{at}: 다른 판 {o.Cell} {o.Direction}에 {gap:F2} m — 같은 자리도 아니고 너무 가까움");
                            }
                        }
                        if (InteriorSkyLiftSpots.EdgeGap(pad) < 0f)
                            problems.AppendLine($"{at}: 판 테가 벽 · 물건에 겹침");
                        int k = graph.Node(pad, 0.4f);
                        if (k < 0 || !graph.Reached(k))
                            problems.AppendLine($"{at}: 판에 걸어서 못 감");
                        if (bridge)
                            continue;
                        if (Physics.Raycast(door - d * 0.05f + Vector3.up * 1f, Vector3.down, 1.3f, ~0, QueryTriggerInteraction.Ignore))
                            problems.AppendLine($"{at}: 문 앞에 바닥이 있음 (승강판 말고 문)");
                        var top2 = new Vector3(pad.x, sill, pad.z);
                        float len = Vector3.Dot(door - top2, d);
                        var mid = top2 + d * (len * 0.5f) + Vector3.up * 1.0f;
                        var rot = Quaternion.LookRotation(d);
                        if (len > 0f && Physics.CheckBox(mid, new Vector3(0.6f, 1.2f, len * 0.5f), rot, ~0, QueryTriggerInteraction.Ignore))
                            problems.AppendLine($"{at}: 떠오른 자리 ~ 문 사이가 막힘");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Physics.SyncTransforms();
                }
            }
            Assert.IsEmpty(problems.ToString(), problems.ToString());
            Assert.GreaterOrEqual(checkedLifts, 0);
        }

        /// <summary>
        /// 문 계단 (문 안 바닥이 문턱보다 낮은 옆문 — 모든 면 연결 ②, <see cref="InteriorAutoDoors"/>): 층계참 · 세 단 · 내려선 자리 아래 땅이 고르고 문턱보다 0.5 ~ 1.05 m 낮음 ·
        /// 그 위에 충돌체 · 장식이 없음 (<see cref="InteriorAutoDoors.StepsProblem"/>) · 내려선 자리가 땅 높이 (오프셋)이고 걸어서 닿음 · 문 앞 바닥이 없음 (있으면 계단 말고 문) ·
        /// 고정 주민 자리가 계단 자리에 없음.
        /// </summary>
        [Test]
        public void DoorSteps_GroundFlat_Clear_Reachable()
        {
            var problems = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate", new[] { "Assets/_Project/Data/Interior" }))
            {
                var t = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (t == null || t.Prefab == null || !t.Sockets.Any(s => s.DoorSteps != 0))
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
                        if (s.DoorSteps == 0)
                            continue;
                        var center = Away + (Vector3)s.Cell * 8f;
                        var door = center + (Vector3)s.Direction * s.Depth;
                        door.y = center.y - 1.3f;
                        string at = $"{t.name} {s.Cell} {s.Direction} 계단 {s.DoorSteps}";
                        if (s.Direction.y != 0 || s.DoorLift || s.SealedWall)
                            problems.AppendLine($"{at}: 문 계단이 열린 옆문이 아님");
                        string why = InteriorAutoDoors.StepsProblem(door, s.Direction, s.DoorSteps, decor, out float ground);
                        if (why != null)
                            problems.AppendLine($"{at}: {why}");
                        var foot = center + s.Offset;
                        var expect = InteriorAutoDoors.StepsFoot(door, s.Direction, s.DoorSteps);
                        if (why == null && (Mathf.Abs(foot.y - ground) > 0.01f || new Vector2(foot.x - expect.x, foot.z - expect.z).magnitude > 0.01f))
                            problems.AppendLine($"{at}: 내려선 자리 {s.Offset}가 계단 끝 · 땅과 다름");
                        int k = graph.Node(foot, 0.35f);
                        if (k < 0 || !graph.Reached(k))
                            problems.AppendLine($"{at}: 내려선 자리에 걸어서 못 감");
                        if (Physics.Raycast(door - (Vector3)s.Direction * 0.05f + Vector3.up * 1f, Vector3.down, 1.3f, ~0, QueryTriggerInteraction.Ignore))
                            problems.AppendLine($"{at}: 문 앞에 바닥이 있음 (계단 말고 문)");
                        foreach (var spot in t.ResidentSpots)
                        {
                            foreach (var p in InteriorAutoDoors.StepsFootprint(door, s.Direction, s.DoorSteps))
                            {
                                var w = Away + spot.Position;
                                if (Mathf.Abs(w.y - ground) < 1.5f && new Vector2(w.x - p.x, w.z - p.z).magnitude < 0.5f)
                                {
                                    problems.AppendLine($"{at}: 주민 자리 {spot.Position}가 계단 자리에 있음");
                                    break;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Object.DestroyImmediate(root);
                    Physics.SyncTransforms();
                }
            }
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
