using System.Collections.Generic;
using SpaceStation.Data;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 위아래 이동 장치 자리 (2026-11-19, 사용자 "도구가 고르고 사진 검토"): 템플릿마다 하나 (<see cref="InteriorTemplate.TransitSpot"/>, 모듈 로컬 = 0번 칸 가운데 기준).
    /// 들어오는 곳 (스폰 · 문)에서 걸어 닿는 바닥 중 — 평평하고 (장치 둘레 높이차 6 cm) · 장치 몸 (반지름 1.05, 기본 화물 칸 = 2.04 m 네모) · 옆 안내 기둥 자리가 3.45 m까지 비었고
    /// · 위로 천장까지 물체가 없고 (사용자 "상단에 오브젝트가 존재하면 피하고 (천장 제외)") · 바닥이 방의 보통 바닥과 같고 (사용자 "바닥에 선이나 길처럼 일반적인 바닥과 다른 곳도 피해서")
    /// · 충돌체 없는 장식 · 문 앞 길 · 주민 자리 · 식물 · 빛 다리 판에서 떨어졌고 · 지나갈 폭 (3.1 m)과 앞에 설 자리가 있는 곳.
    /// 그중 들어오는 자리와 같은 층 · 5.5 m 근처 · 뒤가 벽에 가까운 곳 · 들어오는 자리 쪽을 보는 곳을 먼저. 자리가 없으면 장치를 85%로 (붐비는 방, 사용자 결정).
    /// </summary>
    public static class InteriorTransitSpots
    {
        private static readonly Vector3 Away = new Vector3(10000f, -5000f, 10000f);
        private const float Radius = 1.05f, CageHalf = 1.02f, Height = 3.45f, SmallScale = 0.85f;
        private const float LaneRadius = 1.0f, LaneHalf = 0.8f, LaneDepth = 1.8f, LaneGap = 0.05f, ResidentClear = 1.5f, LiftPadClear = 2.2f, MinPass = 3.1f;   // 문 앞 길 = 문 가운데 ±0.8 · 안으로 1.8 m
        private const float OverheadGap = 0.35f, OpenSkyCheck = 15f;   // 천장 (위로 쏜 광선이 닿는 가장 높은 면)보다 0.35 m 넘게 낮은 물체 = 위 물체
        private static readonly Vector3 KioskOffset = new Vector3(1.3f, 0f, -0.7f);   // 장치 로컬 (앞 = −z) — 기본 칸 호출 기둥 (1.25, −0.95)도 여기 안
        private const string KitPath = "Assets/_Project/Data/Interior/InteriorKit.asset";

        [MenuItem("SpaceStation/Interior/Set Transit Spots")]
        public static void SetAll()
        {
            var kit = AssetDatabase.LoadAssetAtPath<InteriorKit>(KitPath);
            var log = new System.Text.StringBuilder();
            int found = 0, small = 0, missing = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate", new[] { "Assets/_Project/Data/Interior" }))
            {
                var t = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (t == null || t.Module == null || t.Prefab == null)
                    continue;
                if (TryFind(t, kit, out var spot, out float yaw, out float scale, out string why))
                {
                    t.EditorSetTransitSpot(true, spot, yaw, scale);
                    found++;
                    if (scale < 1f)
                        small++;
                    log.AppendLine($"{t.name}: {spot.x:0.00}, {spot.y:0.00}, {spot.z:0.00} · {yaw:0}° · ×{scale:0.00} ({why})");
                }
                else
                {
                    t.EditorSetTransitSpot(false, Vector3.zero);
                    missing++;
                    log.AppendLine($"{t.name}: [자리 없음] {why}");
                }
                EditorUtility.SetDirty(t);
            }
            AssetDatabase.SaveAssets();
            string text = $"[InteriorTransitSpots] 장치 자리 {found}곳 (85% {small})" + (missing > 0 ? $" · 못 찾음 {missing}곳" : "") + $"\n{log}";
            if (missing > 0)
                Debug.LogWarning(text);
            else
                Debug.Log(text);
        }

        /// <summary>제 크기로 찾고, 없으면 85%로 (붐비는 방).</summary>
        public static bool TryFind(InteriorTemplate t, InteriorKit kit, out Vector3 local, out float yaw, out float scale, out string why)
        {
            scale = 1f;
            if (TryFind(t, kit, 1f, out local, out yaw, out why))
                return true;
            string full = why;
            scale = SmallScale;
            if (TryFind(t, kit, SmallScale, out local, out yaw, out why))
                return true;
            why = full;
            return false;
        }

        /// <summary>
        /// 템플릿 하나의 장치 자리 (모듈 로컬) · 보는 쪽 (y 각도, 90° 단위 — 들어오는 자리 쪽 먼저, 안내 기둥 · 앞자리가 막히면 옆 · 뒤).
        /// why = 고른 자리의 점수 설명 · 못 찾으면 걸린 까닭별 수.
        /// </summary>
        public static bool TryFind(InteriorTemplate t, InteriorKit kit, float scale, out Vector3 local, out float yaw, out string why)
        {
            local = Vector3.zero;
            yaw = 0f;
            bool cage = kit != null && kit.TransitStage(t.Module) == 0;
            float radius = Radius * scale, height = Height * scale;
            var root = Object.Instantiate(t.Prefab);
            root.transform.SetPositionAndRotation(Away, Quaternion.identity);
            Physics.SyncTransforms();
            var reasons = new Dictionary<string, int>();
            try
            {
                var graph = InteriorWalkGraph.ForTemplate(t, root.transform);
                var decor = new InteriorSkyLiftSpots.DecorGrid(root.transform);
                var floor = CommonFloor(graph);
                var spawn = Away + t.Spawn;
                var candidates = new List<(float Score, Vector3 P, float Yaw, string Why)>();
                for (int k = 0; k < graph.Count; k++)
                {
                    if (!graph.Reached(k))
                        continue;
                    var p = graph[k];
                    var toSpawn = spawn - p;
                    toSpawn.y = 0f;
                    float d = toSpawn.magnitude;
                    if (d < 2.4f || d > 30f)   // 큰 방 (대형 토러스 32 m 고리)은 들어오는 자리에서 멀리까지 — 가까울수록 점수가 좋음
                    {
                        Count(reasons, "거리");
                        continue;
                    }
                    string blocked = Blocked(t, p, LaneRadius * scale);
                    if (blocked != null)
                    {
                        Count(reasons, blocked);
                        continue;
                    }
                    if (!Flat(p, radius - 0.05f))
                    {
                        Count(reasons, "평평");
                        continue;
                    }
                    if (!BodyClear(p, cage, scale))
                    {
                        Count(reasons, "막힘");
                        continue;
                    }
                    if (cage ? decor.AnyBox(p, CageHalf * scale, p.y + 0.06f, p.y + height) : decor.Any(p, radius, p.y + 0.06f, p.y + height))
                    {
                        Count(reasons, "장식");
                        continue;
                    }
                    if (!SameFloor(p, radius, decor, out var under))
                    {
                        Count(reasons, "바닥무늬");
                        continue;
                    }
                    if (!OverheadClear(p, radius, decor))
                    {
                        Count(reasons, "위물체");
                        continue;
                    }
                    float pass = InteriorSkyLiftSpots.PassWidth(p);
                    if (pass < MinPass * Mathf.Lerp(1f, scale, 0.5f))
                    {
                        Count(reasons, "폭");
                        continue;
                    }
                    // 보는 쪽 (사용자 "크기 측정해서 길 막지 않는 위치로 — 막는다면 벽면에"): 뒤가 벽에 붙고 (몸 + 0.6 m 안) · 앞이 트이고 (몸 + 1.6 m) ·
                    // 안내 기둥 · 앞에 설 자리가 비는 쪽 중 들어오는 자리 쪽 (벌점 0) → 옆 (1.2) → 뒤 (3)
                    float facing = InteriorTransit.Facing(toSpawn, Quaternion.identity).eulerAngles.y;
                    float turn = -1f, turnCost = 0f, back = 99f;
                    string turnWhy = "벽없음";
                    foreach (var (offset, cost) in new[] { (0f, 0f), (90f, 1.2f), (-90f, 1.2f), (180f, 3f) })
                    {
                        var rotation = Quaternion.Euler(0f, facing + offset, 0f);
                        var fwd = rotation * Vector3.back;
                        float b = Physics.Raycast(p + Vector3.up * 1.2f, -fwd, out var bh, 8f, ~0, QueryTriggerInteraction.Ignore) ? bh.distance : 8f;
                        if (b > radius + 0.6f)
                            continue;
                        float f = Physics.Raycast(p + Vector3.up * 1.0f, fwd, out var fh, 8f, ~0, QueryTriggerInteraction.Ignore) ? fh.distance : 8f;
                        if (f < radius + 1.6f)
                        {
                            turnWhy = "앞좁음";
                            continue;
                        }
                        if (!SideClear(p, rotation, scale, decor, under))
                        {
                            turnWhy = "안내기둥";
                            continue;
                        }
                        turn = facing + offset;
                        turnCost = cost;
                        back = b;
                        break;
                    }
                    if (turn < -0.5f)
                    {
                        Count(reasons, turnWhy);
                        continue;
                    }
                    // 점수 (작을수록): 들어오는 자리와 같은 층 · 보통 바닥 · 5.5 m 근처 · 넓은 곳 · 들어오는 자리 쪽을 봄
                    float level = Mathf.Abs(p.y - spawn.y) > 0.6f ? 6f : 0f;
                    float score = level + (floor != null && under != floor ? 2f : 0f) + Mathf.Abs(d - 5.5f) * 0.6f + Mathf.Max(0f, back - (radius + 0.3f)) * 0.8f + Mathf.Max(0f, 5f - pass) * 0.3f + turnCost;
                    candidates.Add((score, p, Mathf.Repeat(turn, 360f), $"들어오는 자리 {d:0.0} m · 뒤 벽 {back:0.0} m · 폭 {pass:0.0} m" + (turnCost > 0f ? " · 옆을 봄" : "") + (level > 0f ? " · 다른 층" : "")));
                }
                // 점수 순으로 — 장치 자리 (몸 + 0.3 m)를 막아도 다른 곳이 모두 닿는 첫 자리 (길을 끊지 않음)
                candidates.Sort((a, b) => a.Score.CompareTo(b.Score));
                int tried = 0;
                foreach (var c in candidates)
                {
                    if (++tried > 400)
                        break;
                    if (graph.LostWithout(c.P, radius + 0.3f) > 0)
                    {
                        Count(reasons, "길끊음");
                        continue;
                    }
                    local = c.P - Away;
                    yaw = c.Yaw;
                    why = c.Why;
                    return true;
                }
                var sb = new System.Text.StringBuilder();
                foreach (var kv in reasons)
                    sb.Append($"{kv.Key} {kv.Value} ");
                why = sb.ToString();
                return false;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Count(Dictionary<string, int> why, string reason) => why[reason] = why.TryGetValue(reason, out int n) ? n + 1 : 1;

        private static float Flat2(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>안내 기둥 자리 (바닥이 장치와 같은 높이 · 같은 바닥 · 충돌체 · 장식 없음) · 앞에 설 자리 (장치 앞 — 바라보고 F)가 빔.</summary>
        private static bool SideClear(Vector3 p, Quaternion rotation, float scale, InteriorSkyLiftSpots.DecorGrid decor, Material floor)
        {
            var kiosk = p + rotation * (KioskOffset * scale);
            if (!Physics.Raycast(kiosk + Vector3.up * 0.4f, Vector3.down, out var kh, 0.7f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(kh.point.y - p.y) > 0.12f
                || (floor != null && MaterialAt(kh) != floor)
                || Physics.CheckCapsule(kiosk + Vector3.up * 0.45f, kiosk + Vector3.up * 1.45f, 0.3f, ~0, QueryTriggerInteraction.Ignore)
                || decor.Any(kiosk, 0.32f, p.y + 0.06f, p.y + 1.5f))
                return false;
            var stand = p + rotation * Vector3.back * (Radius * scale + 0.65f);
            return Physics.Raycast(stand + Vector3.up * 0.5f, Vector3.down, out var sh, 1.0f, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(sh.point.y - p.y) < 0.3f
                && !Physics.CheckCapsule(stand + Vector3.up * 0.65f, stand + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>문 앞 길 · 빛 다리 판 · 주민 자리 · 식물 가까이면 그 이유 (아니면 null).</summary>
        private static string Blocked(InteriorTemplate t, Vector3 p, float radius)
        {
            foreach (var s in t.Sockets)
            {
                if (s.Direction.y != 0)
                    continue;
                var c = Away + (Vector3)s.Cell * InteriorGeometry.CellSize;
                var door = c + (Vector3)s.Direction * s.Depth;
                if (Mathf.Abs(c.y - 2f - p.y) < 2.5f && LaneGap2(door, -(Vector3)s.Direction, p) < radius + LaneGap)
                    return "문";
                if (s.DoorLift && Flat2(c + s.Offset, p) < LiftPadClear + radius - 1f)
                    return "빛다리";
            }
            foreach (var r in t.ResidentSpots)
            {
                var q = Away + r.Position;
                if (Flat2(q, p) < ResidentClear + radius - 1f && Mathf.Abs(q.y - p.y) < 1.5f)
                    return "주민";
            }
            foreach (var plant in t.PlantSpots)
            {
                bool tree = plant.Kind == PlantKind.Tree || plant.Kind == PlantKind.Pine || plant.Kind == PlantKind.Blossom;
                var q = Away + plant.Position;
                if (Flat2(q, p) < (tree ? 1.3f * plant.Scale : 0.5f) + radius && p.y < q.y + (tree ? 6f : 1.5f) && p.y > q.y - 3f)
                    return "식물";
            }
            return null;
        }

        /// <summary>문 앞 길 (문 가운데에서 안쪽 n으로 LaneDepth · 옆으로 ±LaneHalf 네모)에서 p까지 평면 거리.</summary>
        private static float LaneGap2(Vector3 door, Vector3 n, Vector3 p)
        {
            var side = Vector3.Cross(Vector3.up, n);
            var r = p - door;
            float t = Vector3.Dot(r, n), s = Mathf.Abs(Vector3.Dot(r, side));
            float dt = Mathf.Max(0f, Mathf.Max(-t, t - LaneDepth)), ds = Mathf.Max(0f, s - LaneHalf);
            return Mathf.Sqrt(dt * dt + ds * ds);
        }

        /// <summary>가운데 + 반지름 절반 · 반지름 8방향 바닥이 모두 있고 높이차 6 cm 안.</summary>
        private static bool Flat(Vector3 p, float radius)
        {
            for (int ring = 0; ring <= 2; ring++)
            {
                int count = ring == 0 ? 1 : 8;
                for (int a = 0; a < count; a++)
                {
                    float ang = a * Mathf.PI * 0.25f;
                    var q = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (radius * ring * 0.5f);
                    if (!Physics.Raycast(q + Vector3.up * 0.5f, Vector3.down, out var hit, 1.0f, ~0, QueryTriggerInteraction.Ignore))
                        return false;
                    if (hit.normal.y < 0.95f || Mathf.Abs(hit.point.y - p.y) > 0.06f)
                        return false;
                }
            }
            return true;
        }

        /// <summary>장치 몸 (바닥 0.2 m 위 ~ 높이)에 충돌체가 없음: 둥근 장치 = 원기둥, 기본 화물 칸 = 네모 (방 벽에 맞춘 90° 단위).</summary>
        private static bool BodyClear(Vector3 p, bool cage, float scale)
        {
            float r = Radius * scale, h = Height * scale;
            if (cage)
                return !Physics.CheckBox(p + Vector3.up * (0.2f + (h - 0.2f) * 0.5f), new Vector3(CageHalf * scale, (h - 0.2f) * 0.5f, CageHalf * scale), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            return !Physics.CheckCapsule(p + Vector3.up * (0.2f + r), p + Vector3.up * (h - r), r, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// 장치 위 천장까지 물체 없음: 가운데 · 반지름 절반 · 반지름 8방향에서 위로 광선 — 닿은 가장 높은 면 = 천장, 그보다 0.35 m 넘게 낮은 면이 하나라도 있으면 위 물체.
        /// 하늘이 트인 방 (반 넘게 안 닿음)은 15 m 안에 닿는 게 하나라도 있으면 위 물체. 충돌체 없는 장식 (등 · 매단 것)도 그 사이에 있으면 위 물체.
        /// </summary>
        private static bool OverheadClear(Vector3 p, float radius, InteriorSkyLiftSpots.DecorGrid decor)
        {
            var hits = new List<RaycastHit>();
            int misses = 0;
            for (int ring = 0; ring <= 2; ring++)
            {
                int count = ring == 0 ? 1 : 8;
                for (int a = 0; a < count; a++)
                {
                    float ang = a * Mathf.PI * 0.25f;
                    var q = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (radius * ring * 0.5f) + Vector3.up * 0.3f;
                    if (Physics.Raycast(q, Vector3.up, out var hit, OpenSkyCheck, ~0, QueryTriggerInteraction.Ignore))
                        hits.Add(hit);
                    else
                        misses++;
                }
            }
            float top;
            if (misses > 8)
            {
                if (hits.Count > 0)
                    return false;
                top = p.y + OpenSkyCheck;
            }
            else
            {
                if (misses > 0)
                    return false;   // 천장 구멍 (기둥 · 승강구 아래) — 비킴
                // ê°ì¥ ëì ë©´ë³´ë¤ 0.35 m ëê² ë®ê³  ë°ë©´ì´ ìëë¥¼ ë³´ë ë©´ = ë§¤ë¨ ë¬¼ì²´ (í ì²ì¥ · ë¥ê·¼ êµ´ ì²ì¥ì ìë©´ì ë¹ì¤ë¬í´ì ìë)
                float ceiling = float.MinValue, lowest = float.MaxValue;
                foreach (var h in hits)
                {
                    ceiling = Mathf.Max(ceiling, h.point.y);
                    lowest = Mathf.Min(lowest, h.point.y);
                }
                foreach (var h in hits)
                {
                    if (h.point.y < ceiling - OverheadGap && h.normal.y < -0.85f)
                        return false;
                }
                top = lowest - 0.15f;
            }
            return !decor.Any(p, radius, p.y + Height, top);
        }

        /// <summary>
        /// 장치 밑 · 둘레 (반지름 + 0.25) 바닥이 한 가지 재질 (선 · 길 · 무늬가 지나가지 않음)이고 바닥에 붙은 조각 (바닥 위 0.5 ~ 6 cm 정점)이 없음.
        /// under = 그 재질 (방의 보통 바닥이 아니면 점수에서 뒤로).
        /// </summary>
        private static bool SameFloor(Vector3 p, float radius, InteriorSkyLiftSpots.DecorGrid decor, out Material under)
        {
            under = null;
            // ë°ë¥ ë©´ ìì ì¹í ì¡°ê° (ì  Â· ê¸¸ Â· ë¬´ë¬ â ì ì  ë°ë¡ ë°ì´ ì¶©ë ë°ë¥ ë©´ì´ ìë). ì¸íë¶íí íë°­ ë°ë¥ ìì²´ì ì ì ì ê·¸ ë©´ ìë¼ ëº
            foreach (var v in decor.Near(p, radius + 0.2f, p.y + 0.005f, p.y + 0.06f))
            {
                if (!Physics.Raycast(v + Vector3.up * 0.05f, Vector3.down, out var vh, 0.12f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(vh.point.y - v.y) > 0.012f)
                    return false;
            }
            if (!Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var center, 1.0f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            under = MaterialAt(center);
            var floor = under;
            if (floor == null)
                return true;
            for (int ring = 0; ring <= 2; ring++)
            {
                int count = ring == 0 ? 1 : 8;
                for (int a = 0; a < count; a++)
                {
                    float ang = a * Mathf.PI * 0.25f + (ring == 2 ? Mathf.PI / 8f : 0f);
                    var q = p + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (ring == 2 ? radius + 0.25f : radius * 0.5f * ring);
                    if (!Physics.Raycast(q + Vector3.up * 0.5f, Vector3.down, out var hit, 1.0f, ~0, QueryTriggerInteraction.Ignore))
                        return false;
                    if (MaterialAt(hit) != floor)
                        return false;
                }
            }
            return true;
        }

        /// <summary>걷는 마디 (닿는 곳, 4개 중 하나)에서 아래로 쏴 가장 많이 닿은 재질 = 방의 보통 바닥.</summary>
        private static Material CommonFloor(InteriorWalkGraph graph)
        {
            var counts = new Dictionary<Material, int>();
            for (int k = 0; k < graph.Count; k += 4)
            {
                if (!graph.Reached(k))
                    continue;
                if (!Physics.Raycast(graph[k] + Vector3.up * 0.3f, Vector3.down, out var hit, 0.8f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                var m = MaterialAt(hit);
                if (m != null)
                    counts[m] = counts.TryGetValue(m, out int n) ? n + 1 : 1;
            }
            Material best = null;
            int bestCount = 0;
            foreach (var kv in counts)
            {
                if (kv.Value > bestCount)
                {
                    best = kv.Key;
                    bestCount = kv.Value;
                }
            }
            return best;
        }

        /// <summary>광선이 닿은 면의 재질 (메시 충돌체 + 같은 오브젝트의 렌더러 — 삼각형이 든 부분 메시). 모르면 null.</summary>
        private static Material MaterialAt(RaycastHit hit)
        {
            if (!(hit.collider is MeshCollider mc) || mc.sharedMesh == null || hit.triangleIndex < 0)
                return null;
            var renderer = mc.GetComponent<MeshRenderer>();
            if (renderer == null)
                return null;
            var mesh = mc.sharedMesh;
            var mats = renderer.sharedMaterials;
            int index = hit.triangleIndex * 3;
            for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
            {
                var sub = mesh.GetSubMesh(s);
                if (index >= sub.indexStart && index < sub.indexStart + sub.indexCount)
                    return mats[s];
            }
            return mats.Length > 0 ? mats[0] : null;
        }
    }
}
