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
        private static readonly float[] WallHalf = { 0.9f, 1.16f, 1.0f, 1.0f, 1.0f };   // 벽 장치 반폭 (단계별): 기본 = 테 (호출판은 레일 위) · 1 = 옆 홀로그램 판 · 2 ~ 4 = 반원 발판
        private static readonly float[] WallBodyHalf = { 0.72f, 1.16f, 1.0f, 1.0f, 1.0f };   // 벽에서 7 cm 앞부터의 반폭: 기본 = 손잡이 · 발판 (테 · 뒤판은 벽 평평 검사가 봄 — 옆 벽 포트에 걸렸음, 의무실)
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
                if (TryFind(t, kit, out var spot, out float yaw, out float scale, out bool wall, out string why))
                {
                    t.EditorSetTransitSpot(true, spot, yaw, scale, wall);
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
        public static bool TryFind(InteriorTemplate t, InteriorKit kit, out Vector3 local, out float yaw, out float scale, out bool wall, out string why)
        {
            scale = 1f;
            wall = false;
            if (TryFind(t, kit, 1f, out local, out yaw, out why))
                return true;
            string full = why;
            scale = SmallScale;
            if (TryFind(t, kit, SmallScale, out local, out yaw, out why))
                return true;
            scale = 1f;
            wall = true;   // 그래도 없으면 벽 장치 — 기본 방 (1칸)은 문 벽판 옆 구석 벽이 1.6 m라 85% (테 폭 1.53 m)
            if (TryFindWall(t, kit, 1f, out local, out yaw, out string wallWhy))
            {
                why = wallWhy;
                return true;
            }
            scale = SmallScale;
            if (TryFindWall(t, kit, SmallScale, out local, out yaw, out wallWhy))
            {
                why = wallWhy;
                return true;
            }
            why = full + "/ 벽: " + wallWhy;
            scale = 1f;
            wall = false;
            return false;
        }

        /// <summary>
        /// 템플릿 하나의 장치 자리 (모듈 로컬) · 보는 쪽 (y 각도, 90° 단위 — 들어오는 자리 쪽 먼저, 안내 기둥 · 앞자리가 막히면 옆 · 뒤).
        /// why = 고른 자리의 점수 설명 · 못 찾으면 걸린 까닭별 수.
        /// </summary>
        public static bool TryFind(InteriorTemplate t, InteriorKit kit, float scale, out Vector3 local, out float yaw, out string why, Vector3? near = null)
        {
            local = Vector3.zero;
            yaw = 0f;
            bool cage = kit != null && kit.TransitStage(t.Module) == 0;
            float radius = Radius * scale, height = Height * scale;
            var root = Object.Instantiate(t.Prefab);
            root.transform.SetPositionAndRotation(Away, Quaternion.identity);
            Physics.SyncTransforms();
            AddPanels(t, root.transform);
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
                    if (near.HasValue && Flat2(p, Away + near.Value) > 0.2f)   // 손으로 고른 자리 검사 (그 근처 마디만)
                        continue;
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
                    float pass = AxisPass(p);
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

        /// <summary>
        /// 벽 장치 자리 (사용자 2026-11-19 "길을 막는다면 벽면에 배치하거나 하는 방식으로 우회설치"): 걸어 닿는 마디에서 네 방향 0.35 ~ 0.9 m 앞 벽 —
        /// 벽이 장치 폭 (단계별 반폭 <see cref="WallHalf"/>) · 2.3 m 높이로 곧고 (창 · 문 틈 없음) · 발판 자리 (벽에서 1 m · 폭 1.3 m)가 평평한 한 가지 바닥이고 · 장치 몸 (높이 3.25, 단계 장치 3.7)에
        /// 충돌체 · 장식이 없고 · 문 앞 길 · 주민 · 식물 · 위 물체가 없고 · 앞에 설 자리가 있고 · 발판을 막아도 걷는 곳이 끊기지 않는 곳. 피벗 = 벽 면 · 바닥, 보는 쪽 = 방 안.
        /// 치수는 모두 × scale (기본 방 구석 벽 1.6 m = 85%).
        /// </summary>
        public static bool TryFindWall(InteriorTemplate t, InteriorKit kit, float scale, out Vector3 local, out float yaw, out string why, Vector3? near = null)
        {
            local = Vector3.zero;
            yaw = 0f;
            int stage = kit != null ? Mathf.Clamp(kit.TransitStage(t.Module), 0, WallHalf.Length - 1) : 0;
            float height = (stage > 0 ? 3.7f : 3.25f) * scale, half = WallHalf[stage] * scale, bodyHalf = WallBodyHalf[stage] * scale;
            var root = Object.Instantiate(t.Prefab);
            root.transform.SetPositionAndRotation(Away, Quaternion.identity);
            Physics.SyncTransforms();
            AddPanels(t, root.transform);
            var reasons = new Dictionary<string, int>();
            try
            {
                var graph = InteriorWalkGraph.ForTemplate(t, root.transform);
                var decor = new InteriorSkyLiftSpots.DecorGrid(root.transform, true);   // 충돌체 없는 장식 전부 (읽기 꺼진 메시도) — 벽 빛 줄 · 표지
                var spawn = Away + t.Spawn;
                var candidates = new List<(float Score, Vector3 Pivot, Vector3 Stand, float Yaw, string Why)>();
                var dirs = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
                for (int k = 0; k < graph.Count; k++)
                {
                    if (!graph.Reached(k))
                        continue;
                    var p = graph[k];
                    if (near.HasValue && Flat2(p, Away + near.Value) > 0.2f)   // 손으로 고른 자리 검사 (그 근처 마디만)
                        continue;
                    foreach (var d in dirs)
                    {
                        void No(string r) => Count(reasons, near.HasValue ? $"{r}({d.x:0},{d.z:0}){p.x - Away.x:0.00},{p.z - Away.z:0.00}" : r);   // 한 점 검사 = 방향 · 마디까지
                        if (!Physics.Raycast(p + Vector3.up * 1.2f, d, out var wh, 0.9f, ~0, QueryTriggerInteraction.Ignore) || wh.distance < 0.35f || Vector3.Dot(wh.normal, -d) < 0.97f)
                            continue;
                        var pivot = p + d * wh.distance;
                        var side = Vector3.Cross(Vector3.up, d);
                        // 평평한 벽 구간 (옆 ±1.8 m를 5 cm마다 · 높이 5곳 = 장치 꼭대기까지): 장치 폭 안의 벽 면이 3.5 cm 안으로 고르고 (표지판 · 빛 줄이 뒤판을 뚫고 나왔음 — 장치는 가장 튀어나온 면에 붙음)
                        // 문 (미닫이) · 유리 · 화면 · 창이 아닌 곳 (사진 검토 2026-11-20: 대피소 방 안 문 앞을 막았음). 그 폭이 들어가는 가장 가까운 자리로 피벗을 옆으로 옮기고 벽 면 (가장 먼 면)에 붙임.
                        // 기본 방 구석 벽 1.6 m에 85% 장치 (1.53 m)가 맞는 범위는 7 cm라 마디 (0.25 m 간격) 자리 그대로는 늘 벽판 · 옆 벽에 걸렸음
                        const int Reach = 36;
                        var heights = new[] { 0.3f, 0.95f, 1.6f, 2.25f, 2.85f };
                        var near0 = new float[Reach * 2 + 1];
                        var far0 = new float[Reach * 2 + 1];
                        for (int i = 0; i < near0.Length; i++)
                        {
                            near0[i] = float.MaxValue;
                            far0[i] = float.MinValue;
                            foreach (float h in heights)
                            {
                                var from = pivot - d * 0.6f + side * ((i - Reach) * 0.05f) + Vector3.up * (h * scale);
                                if (!Physics.Raycast(from, d, out var hh, 1.0f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(hh.distance - 0.6f) > 0.15f || NotWall(hh))
                                {
                                    near0[i] = float.NaN;   // 창 · 문 틈 (광선이 지나감) · 기둥 · 앞으로 나온 문 벽판 · 문 · 유리 · 화면
                                    break;
                                }
                                near0[i] = Mathf.Min(near0[i], hh.distance);
                                far0[i] = Mathf.Max(far0[i], hh.distance);
                            }
                        }
                        int need = Mathf.FloorToInt((half + 0.01f) / 0.05f), shift = int.MaxValue;
                        float wallAt = 0f;
                        for (int o = 0; o <= Reach - need && shift == int.MaxValue; o++)
                        {
                            foreach (int c in o == 0 ? new[] { 0 } : new[] { -o, o })
                            {
                                float lo = float.MaxValue, hi = float.MinValue;
                                for (int i = c - need; i <= c + need && !float.IsNaN(lo); i++)
                                {
                                    float n0 = near0[i + Reach];
                                    lo = float.IsNaN(n0) ? float.NaN : Mathf.Min(lo, n0);
                                    hi = Mathf.Max(hi, far0[i + Reach]);
                                }
                                if (!float.IsNaN(lo) && hi - lo <= 0.035f)
                                {
                                    shift = c;
                                    wallAt = hi;
                                    break;
                                }
                            }
                        }
                        if (shift == int.MaxValue)
                        {
                            No("벽굽음");
                            continue;
                        }
                        pivot += side * (shift * 0.05f) + d * (wallAt - 0.6f);
                        // 고른 자리만 촘촘히 (옆 10 cm × 높이 8 cm): 가는 가로 빛 줄 · 선반 턱 · 띠는 높이 5곳 사이로 빠졌음 (연구원 사진 2026-11-20).
                        // 벽 면이 3.5 cm 안으로 고르면 장치를 가장 튀어나온 면 (띠 · 테)에 붙임 — 벽 면에 붙이면 3 cm 띠가 뒤판 앞으로 비쳤음 (수경 농장 · 의무실 사진)
                        bool dense = true;
                        float dMin = float.MaxValue, dMax = float.MinValue;
                        for (float lat = -(half - 0.02f); lat <= half - 0.02f + 0.001f && dense; lat += 0.1f)
                        {
                            for (float h = 0.1f; h <= height - 0.05f && dense; h += 0.08f)
                            {
                                dense = Physics.Raycast(pivot - d * 0.3f + side * lat + Vector3.up * h, d, out var dh, 0.6f, ~0, QueryTriggerInteraction.Ignore)
                                    && dh.distance > 0.3f - 0.05f && dh.distance < 0.3f + 0.02f && !NotWall(dh);
                                if (dense)
                                {
                                    dMin = Mathf.Min(dMin, dh.distance);
                                    dMax = Mathf.Max(dMax, dh.distance);
                                }
                            }
                        }
                        if (!dense || dMax - dMin > 0.035f)
                        {
                            No("벽턱");
                            continue;
                        }
                        pivot += d * (dMin - 0.3f - 0.003f);
                        var stand = pivot - d * (InteriorTransit.WallStand * scale);
                        float sd = Flat2(stand, spawn);
                        if (sd < 2.0f || sd > 30f)
                        {
                            No("거리");
                            continue;
                        }
                        string blocked = Blocked(t, stand, 0.75f * scale);
                        if (blocked != null)
                        {
                            Count(reasons, blocked);
                            continue;
                        }
                        // 발판 바닥 (벽에서 0.15 ~ 0.95 · 폭 ±0.65, × 배율): 평평 · 한 가지 바닥 · 얹힌 조각 없음
                        Material under = null;
                        bool flat = true;
                        foreach (float dz in new[] { 0.15f * scale, 0.55f * scale, 0.95f * scale })
                        {
                            foreach (float sx in new[] { -0.65f * scale, 0f, 0.65f * scale })
                            {
                                var q = pivot - d * dz + side * sx;
                                if (!Physics.Raycast(q + Vector3.up * 0.5f, Vector3.down, out var fh, 1.0f, ~0, QueryTriggerInteraction.Ignore) || fh.normal.y < 0.95f || Mathf.Abs(fh.point.y - p.y) > 0.06f)
                                {
                                    flat = false;
                                    continue;
                                }
                                var m = MaterialAt(fh);
                                if (under == null)
                                    under = m;
                                else if (m != under)
                                    flat = false;
                            }
                        }
                        if (!flat || !NoFloorDecals(pivot - d * (0.5f * scale), 0.6f * scale, decor))   // 둘레 고리 재질 검사 (SameFloor)는 벽 뒤까지 쏴서 늘 실패했음 — 재질은 위 9점이 봄
                        {
                            No("바닥");
                            continue;
                        }
                        // 장치 몸 (벽에서 0.07 ~ 1.05 · 폭 ±WallBodyHalf · 0.2 ~ 높이, × 배율) 충돌체 · 장식
                        var rot = Quaternion.LookRotation(d);
                        var bodyCenter = pivot - d * (0.56f * scale) + Vector3.up * (0.2f + (height - 0.2f) * 0.5f);
                        if (Physics.CheckBox(bodyCenter, new Vector3(bodyHalf, (height - 0.2f) * 0.5f, 0.49f * scale), rot, ~0, QueryTriggerInteraction.Ignore))
                        {
                            No("막힘");
                            continue;
                        }
                        // 뒤판 자리 (벽 앞 0 ~ 0.2 m · 장치 폭)의 충돌체 없는 장식 (빛 줄 · 표지 — 광선에 안 잡혀 뒤판 위로 보였음, 연구원 사진 2026-11-20)
                        bool backDecor = false;
                        for (float lat = -half + 0.15f; lat <= half - 0.15f + 0.01f && !backDecor; lat += 0.25f)
                            backDecor = decor.Any(pivot - d * 0.06f + side * lat, 0.16f, p.y + 0.05f, p.y + height);
                        if (backDecor)
                        {
                            No("벽장식");
                            continue;
                        }
                        if (decor.Any(stand, 0.6f * scale, p.y + 0.06f, p.y + height) || decor.Any(stand + side * (0.5f * scale), 0.45f * scale, p.y + 0.06f, p.y + height) || decor.Any(stand - side * (0.5f * scale), 0.45f * scale, p.y + 0.06f, p.y + height))
                        {
                            No("장식");
                            continue;
                        }
                        if (!WallOverheadClear(pivot, d, side, scale, height, decor))
                        {
                            No("위물체");
                            continue;
                        }
                        // 앞에 설 자리 (발판 끝 1.05에서 0.35 m 앞, × 배율 — 1.6 m는 가운데 큰 장비가 있는 방 (융합로)에서 늘 걸렸음)
                        var front = pivot - d * (1.4f * scale);
                        if (!Physics.Raycast(front + Vector3.up * 0.5f, Vector3.down, out var sh, 1.0f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(sh.point.y - p.y) > 0.3f
                            || Physics.CheckCapsule(front + Vector3.up * 0.65f, front + Vector3.up * 1.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                        {
                            No("앞자리");
                            continue;
                        }
                        // 피벗을 옆으로 옮겼으니 서는 자리 둘레 (0.35 m)에 걸어 닿는 마디가 있는지
                        bool reach = false;
                        for (int j = 0; j < graph.Count && !reach; j++)
                            reach = graph.Reached(j) && Flat2(graph[j], stand) < 0.35f && Mathf.Abs(graph[j].y - stand.y) < 0.3f;
                        if (!reach)
                        {
                            No("못닿음");
                            continue;
                        }
                        float level = Mathf.Abs(p.y - spawn.y) > 0.6f ? 6f : 0f;
                        float score = level + Mathf.Abs(sd - 5f) * 0.6f;
                        candidates.Add((score, pivot, stand, rot.eulerAngles.y, $"벽 · 들어오는 자리 {sd:0.0} m" + (level > 0f ? " · 다른 층" : "")));
                    }
                }
                candidates.Sort((a, b) => a.Score.CompareTo(b.Score));
                int tried = 0;
                foreach (var c in candidates)
                {
                    if (++tried > 400)
                        break;
                    if (graph.LostWithout(c.Stand, 0.95f * scale) > 0)
                    {
                        Count(reasons, "길끊음");
                        continue;
                    }
                    local = c.Pivot - Away;
                    yaw = Mathf.Repeat(c.Yaw, 360f);
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

        /// <summary>
        /// 게임에서 문 자리마다 붙는 키트 벽판 (InteriorBuilder.Panel — 폭 3.2 · 두께 0.2 · 문 깊이에서 방 쪽)을 충돌 상자로 세움: 프리팹에는 그 벽이 없어
        /// 기본 방의 "뒤 벽" · "곧은 벽" 검사가 모두 빗나갔음 (2026-11-20). 문이 열릴 수 있으니 가운데 문 구멍 (폭 1.4 · 높이 2.4)은 비워 둠.
        /// </summary>
        private static void AddPanels(InteriorTemplate t, Transform root)
        {
            float pw = InteriorGeometry.PanelWidth, dw = InteriorGeometry.DoorWidth, th = InteriorGeometry.Thickness, h = InteriorGeometry.RoomHeight;
            foreach (var s in t.Sockets)
            {
                if (s.Direction.y != 0)
                    continue;
                var dir = (Vector3)s.Direction;
                var c = Away + (Vector3)s.Cell * InteriorGeometry.CellSize;
                float floor = c.y + InteriorGeometry.FloorOffset;
                var rot = Quaternion.LookRotation(dir, Vector3.up);
                var face = new Vector3(c.x, floor, c.z) + dir * (s.Depth - th * 0.5f);
                float sideW = (pw - dw) * 0.5f;
                foreach (var (offset, size, y) in new[] { (-(dw + sideW) * 0.5f, new Vector3(sideW, h, th), h * 0.5f), ((dw + sideW) * 0.5f, new Vector3(sideW, h, th), h * 0.5f),
                             (0f, new Vector3(dw, h - InteriorGeometry.DoorHeight, th), (h + InteriorGeometry.DoorHeight) * 0.5f) })
                {
                    var go = new GameObject("_Panel");
                    go.transform.SetParent(root, true);
                    go.transform.SetPositionAndRotation(face + rot * Vector3.right * offset + Vector3.up * y, rot);
                    go.AddComponent<BoxCollider>().size = size;
                }
            }
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 허리 높이 (0.9 m)에서 x · z 두 축으로 마주 보는 면 사이 폭 중 좁은 쪽 (한쪽 8 m까지). 8방향으로 재면 구석에서 두 벽을 비스듬히 잇는 짧은 선 (2.8 m)이 폭으로 잡혀
        /// 구석 자리가 모두 빠졌음 (키트 벽판을 세운 뒤 2026-11-20) — 방은 격자에 맞춰져 있고, 길이 끊기는지는 걷기 검사 (LostWithout)가 따로 봄.
        /// </summary>
        private static float AxisPass(Vector3 pad)
        {
            var p = pad + Vector3.up * 0.9f;
            float width = 99f;
            foreach (var d in new[] { Vector3.forward, Vector3.right })
            {
                float d1 = Physics.Raycast(p, d, out var h1, 8f, ~0, QueryTriggerInteraction.Ignore) ? h1.distance : 8f;
                float d2 = Physics.Raycast(p, -d, out var h2, 8f, ~0, QueryTriggerInteraction.Ignore) ? h2.distance : 8f;
                width = Mathf.Min(width, d1 + d2);
            }
            return width;
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
                // 가장 높은 면보다 0.35 m 넘게 낮고 밑면이 아래를 보는 면 = 매단 물체 (휜 천장 · 둥근 굴 천장의 옆면은 비스듬해서 아님)
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
        /// 벽 장치 위 (장치 발자국 — 벽에서 0.1 ~ 0.85 · 옆 ±0.6, × 배율 — 9점에서 위로 광선): 가장 높은 면 = 천장, 그보다 0.35 m 넘게 낮은 아래 보는 면 · 장치 머리보다 낮은 천장 · 충돌체 없는 매단 장식이 없음.
        /// 서는 자리 둘레 고리 (OverheadClear)는 벽 뒤까지 쏴서 늘 실패했음 (2026-11-20). 9점 모두 안 닿으면 하늘이 트인 방.
        /// </summary>
        private static bool WallOverheadClear(Vector3 pivot, Vector3 d, Vector3 side, float scale, float height, InteriorSkyLiftSpots.DecorGrid decor)
        {
            var hits = new List<RaycastHit>();
            foreach (float dz in new[] { 0.1f, 0.45f, 0.85f })
            {
                foreach (float sx in new[] { -0.6f, 0f, 0.6f })
                {
                    var q = pivot - d * (dz * scale) + side * (sx * scale) + Vector3.up * 0.3f;
                    if (Physics.Raycast(q, Vector3.up, out var hit, OpenSkyCheck, ~0, QueryTriggerInteraction.Ignore))
                        hits.Add(hit);
                }
            }
            var middle = pivot - d * (0.5f * scale);
            if (hits.Count == 0)
                return !decor.Any(middle, 0.6f * scale, pivot.y + height, pivot.y + OpenSkyCheck);
            if (hits.Count < 9)
                return false;   // 천장 구멍
            float ceiling = float.MinValue, lowest = float.MaxValue;
            foreach (var h in hits)
            {
                ceiling = Mathf.Max(ceiling, h.point.y);
                lowest = Mathf.Min(lowest, h.point.y);
            }
            if (lowest < pivot.y + height)
                return false;
            foreach (var h in hits)
            {
                if (h.point.y < ceiling - OverheadGap && h.normal.y < -0.85f)
                    return false;
            }
            return !decor.Any(middle, 0.6f * scale, pivot.y + height, lowest - 0.15f);
        }

        /// <summary>
        /// 장치 밑 · 둘레 (반지름 + 0.25) 바닥이 한 가지 재질 (선 · 길 · 무늬가 지나가지 않음)이고 바닥에 붙은 조각 (바닥 위 0.5 ~ 6 cm 정점)이 없음.
        /// under = 그 재질 (방의 보통 바닥이 아니면 점수에서 뒤로).
        /// </summary>
        private static bool SameFloor(Vector3 p, float radius, InteriorSkyLiftSpots.DecorGrid decor, out Material under)
        {
            under = null;
            if (!NoFloorDecals(p, radius + 0.2f, decor))
                return false;
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

        /// <summary>벽 장치를 붙이면 안 되는 면: 미닫이문 (INT_*_Door) · 유리 · 계기판 조각, 창 · 화면 · 유리 · 문 재질.</summary>
        private static bool NotWall(RaycastHit hit)
        {
            var go = hit.collider.gameObject;
            if (go.name.Contains("Glass") || go.name.Contains("Gauge") || go.name.EndsWith("_Door"))   // 미닫이문 컴포넌트는 방 루트에 붙어 있어 (검역소) 조각 이름으로
                return true;
            var m = MaterialAt(hit);
            return m != null && (m.name.Contains("Window") || m.name.Contains("Screen") || m.name.Contains("Glass") || m.name.Contains("Door"));
        }

        /// <summary>바닥 면 위에 얹힌 조각 (선 · 길 · 무늬 — 정점 바로 밑이 충돌 바닥 면이 아님)이 반지름 안에 없음. 울퉁불퉁한 풀밭 바닥 자체의 정점은 그 면 위라 뺌.</summary>
        private static bool NoFloorDecals(Vector3 p, float radius, InteriorSkyLiftSpots.DecorGrid decor)
        {
            foreach (var v in decor.Near(p, radius, p.y + 0.005f, p.y + 0.06f))
            {
                if (!Physics.Raycast(v + Vector3.up * 0.05f, Vector3.down, out var vh, 0.12f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(vh.point.y - v.y) > 0.012f)
                    return false;
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
