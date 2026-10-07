using System.Collections.Generic;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-11 템플릿마다 주민 자리를 정해 IT_*.asset에 저장한다 (메뉴 SpaceStation/Interior/Bake Resident Spots).
    /// 템플릿 프리팹을 멀리 임시로 놓고 모든 메시에 MeshCollider를 붙여 실제 모델 모양으로 찾는다:
    /// - 앉기: 바닥 위 0.35~0.65m의 위를 향한 면(침대 · 소파 · 벤치 · 의자 · 상자) + 머리 위 · 다리 앞 공간이 빈 곳. 등받이(벽 · 등판)가 있는 쪽을 등지게
    /// - 작업: 빈 바닥에서 손 높이(1.1m) 0.3~0.75m 앞에 화면 · 장비(Screen · Device 재질)가 있는 곳, 그쪽을 바라봄
    /// - 서기: 빈 바닥 (머리까지 빈 곳), 방 가운데를 바라봄
    /// 문 자리 · 해치 · 들어오는 곳 근처는 비운다. 서로 0.9m 이상 떨어지게 고르고(가장 먼 점 우선), 앉기를 먼저 채운다.
    /// 일터 특성 · 여가 방 표는 아래 <see cref="WorkTraits"/> · <see cref="LeisureRooms"/>.
    /// 템플릿 모양이 바뀌면(Build Templates) 이 메뉴도 다시 실행한다.
    /// </summary>
    public static class ResidentSpotBaker
    {
        private const string DataFolder = "Assets/_Project/Data/Interior/";
        private static readonly Vector3 Far = new Vector3(20000f, 0f, 20000f);
        private const float Step = 0.4f;
        private const float MinSpacing = 0.9f;
        private const float Floor = Interior.InteriorGeometry.FloorOffset;

        /// <summary>낮에 이 방에서 일하는 특성 (템플릿 이름 → 특성).</summary>
        private static readonly Dictionary<string, ResidentTrait[]> WorkTraits = new Dictionary<string, ResidentTrait[]>
        {
            { "MaintenanceBay", new[] { ResidentTrait.Technician, ResidentTrait.Mechanic } },
            { "DamageControl", new[] { ResidentTrait.Technician } },
            { "ArmorBulkhead", new[] { ResidentTrait.Technician } },
            { "Battery", new[] { ResidentTrait.Technician } },
            { "Solar", new[] { ResidentTrait.Technician } },
            { "FuelCell", new[] { ResidentTrait.Technician } },
            { "Turret", new[] { ResidentTrait.Technician } },
            { "Refinery", new[] { ResidentTrait.Mechanic } },
            { "MiningDock", new[] { ResidentTrait.Mechanic } },
            { "CargoTerminal", new[] { ResidentTrait.Mechanic } },
            { "Storage", new[] { ResidentTrait.Mechanic } },
            { "ResearchLab", new[] { ResidentTrait.Scientist } },
            { "FusionReactor", new[] { ResidentTrait.Scientist } },
            { "Shield", new[] { ResidentTrait.Scientist } },
            { "Medical", new[] { ResidentTrait.Scientist } },
            { "Farm", new[] { ResidentTrait.Gardener } },
            { "Oxygen", new[] { ResidentTrait.Gardener } },
            { "WaterRecycler", new[] { ResidentTrait.Gardener } },
        };

        /// <summary>낮에 일터가 없는 주민이 쉬러 오는 방.</summary>
        private static readonly HashSet<string> LeisureRooms = new HashSet<string> { "Recreation", "RotatingRing", "Core" };

        private struct Candidate
        {
            public Vector3 Local;
            public float Yaw;
            public ResidentPose Pose;
        }

        [MenuItem("SpaceStation/Interior/Bake Resident Spots")]
        public static void BakeAll()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[ResidentSpotBaker] 플레이 모드에서는 실행하지 않음");
                return;
            }
            var log = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate", new[] { DataFolder }))
            {
                var template = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (template == null || template.Prefab == null || template.Module == null)
                    continue;
                log.Add(Bake(template));
                EditorUtility.SetDirty(template);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ResidentSpotBaker] " + string.Join(" · ", log));
        }

        private static string Bake(InteriorTemplate template)
        {
            string name = template.name.Replace("IT_", "");
            var module = template.Module;
            var instance = (GameObject)Object.Instantiate(template.Prefab, Far, Quaternion.identity);
            var added = new List<Component>();
            try
            {
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
                        continue;
                    added.Add(filter.gameObject.AddComponent<MeshCollider>());
                    ((MeshCollider)added[added.Count - 1]).sharedMesh = filter.sharedMesh;
                }
                Physics.SyncTransforms();

                var avoid = AvoidPoints(template);
                var sits = new List<Candidate>();
                var works = new List<Candidate>();
                var stands = new List<Candidate>();
                foreach (var cell in module.CellOffsets)
                    Scan(cell, avoid, sits, works, stands);

                WorkTraits.TryGetValue(name, out var traits);
                bool leisure = LeisureRooms.Contains(name);
                int cells = module.CellOffsets.Count;
                int housing = module.HousingCapacity;
                int restCount = housing > 0 || leisure ? Mathf.Min(8, Mathf.Max(housing, 3 * cells)) : 0;
                int workCount = traits != null ? Mathf.Min(4, 2 * cells) : 0;

                var chosen = new List<Vector3>();
                var spots = new List<ResidentSpot>();
                int workFound = 0;
                foreach (var c in Pick(works, workCount, chosen))
                {
                    spots.Add(new ResidentSpot(c.Local, c.Yaw, ResidentPose.Work, work: true));
                    workFound++;
                }
                // 작업대를 못 찾은 만큼은 서서 일하는 자리 (방 가운데를 봄) — 손상 통제실처럼 낮은 상황판만 있는 방
                foreach (var c in Pick(stands, workCount - workFound, chosen))
                    spots.Add(new ResidentSpot(c.Local, c.Yaw, ResidentPose.Stand, work: true));
                int sitCount = 0;
                foreach (var c in Pick(sits, restCount, chosen))
                {
                    spots.Add(new ResidentSpot(c.Local, c.Yaw, ResidentPose.Sit));
                    sitCount++;
                }
                foreach (var c in Pick(stands, restCount - sitCount, chosen))
                    spots.Add(new ResidentSpot(c.Local, c.Yaw, ResidentPose.Stand));

                template.EditorSetResidents(spots, traits != null ? new List<ResidentTrait>(traits) : new List<ResidentTrait>(), leisure);
                int w = 0, s = 0, st = 0;
                foreach (var sp in spots)
                {
                    if (sp.Work) w++;
                    else if (sp.Pose == ResidentPose.Sit) s++;
                    else st++;
                }
                return $"{name} 작업 {w}/{workCount} 앉기 {s} 서기 {st}/{restCount} (후보 {works.Count}/{sits.Count}/{stands.Count})";
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>문 자리 · 해치 · 들어오는 곳 (템플릿 로컬, 바닥 높이 무시).</summary>
        private static List<(Vector3 Point, float Radius)> AvoidPoints(InteriorTemplate template)
        {
            // 처음 값(문 반경 2.0, 들어오는 곳 1.6)은 6.4m 방 바닥을 거의 다 가렸음 → 문 앞 통로 폭만큼
            var list = new List<(Vector3, float)> { (template.Spawn, 1.0f) };
            foreach (var s in template.Sockets)
            {
                var c = (Vector3)s.Cell * Interior.InteriorGeometry.CellSize;
                if (s.Direction.y == 0)
                {
                    var dir = (Vector3)s.Direction;
                    list.Add((c + dir * (s.Depth - 0.4f), 1.1f));
                    list.Add((c + dir * (s.Depth - 1.4f), 0.9f));
                }
                else
                    list.Add((c + s.Offset, 1.0f));
            }
            return list;
        }

        private static bool NearAvoid(Vector3 local, List<(Vector3 Point, float Radius)> avoid)
        {
            foreach (var (p, r) in avoid)
            {
                var d = local - p;
                d.y = 0f;
                if (d.sqrMagnitude < r * r)
                    return true;
            }
            return false;
        }

        private static void Scan(Vector3Int cell, List<(Vector3, float)> avoid, List<Candidate> sits, List<Candidate> works, List<Candidate> stands)
        {
            float size = Interior.InteriorGeometry.CellSize;
            var center = (Vector3)cell * size;
            float floor = center.y + Floor;
            for (float x = -3.4f; x <= 3.401f; x += Step)
            {
                for (float z = -3.4f; z <= 3.401f; z += Step)
                {
                    var local = center + new Vector3(x, 0f, z);
                    var top = Far + new Vector3(local.x, floor + 2.3f, local.z);
                    if (!Physics.Raycast(top, Vector3.down, out var hit, 3.2f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.9f)
                        continue;
                    float h = hit.point.y - (Far.y + floor);
                    var groundLocal = hit.point - Far;
                    if (NearAvoid(groundLocal, avoid))
                        continue;
                    var yawToCenter = YawTo(groundLocal, center);

                    if (Mathf.Abs(h) < 0.25f)
                    {
                        // 빈 바닥: 머리까지 비었는지
                        if (Physics.CheckCapsule(hit.point + Vector3.up * 0.35f, hit.point + Vector3.up * 1.75f, 0.28f, ~0, QueryTriggerInteraction.Ignore))
                            continue;
                        stands.Add(new Candidate { Local = groundLocal, Yaw = OpenYaw(hit.point, yawToCenter), Pose = ResidentPose.Stand });
                        if (TryWork(hit.point, out float workYaw))
                            works.Add(new Candidate { Local = groundLocal, Yaw = workYaw, Pose = ResidentPose.Work });
                    }
                    else if (h > 0.35f && h < 0.65f)
                    {
                        if (TrySit(hit.point, Far.y + floor, out float sitYaw))
                            sits.Add(new Candidate { Local = groundLocal, Yaw = sitYaw, Pose = ResidentPose.Sit });
                    }
                }
            }
        }

        /// <summary>앉을 자리: 위가 비었고, 다리를 내릴 방향이 있는지. 등받이가 있는 반대쪽을 바라봄.</summary>
        private static bool TrySit(Vector3 seat, float floorY, out float yaw)
        {
            yaw = 0f;
            if (Physics.CheckCapsule(seat + Vector3.up * 0.3f, seat + Vector3.up * 1.1f, 0.2f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            float best = -1f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                // 무릎 · 발 자리 (앉은 자세: 무릎 0.44 앞, 발 0.48 앞, 바닥까지)
                var knee = seat + dir * 0.45f + Vector3.up * 0.05f;
                if (Physics.CheckSphere(knee, 0.1f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                var foot = new Vector3(knee.x, floorY + 0.3f, knee.z);
                if (Physics.CheckSphere(foot, 0.12f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                if (!Physics.Raycast(foot, Vector3.down, out var fh, 0.6f) || Mathf.Abs(fh.point.y - floorY) > 0.25f)
                    continue;
                // 등받이 (뒤쪽 0.7 안에 무언가) 점수
                float score = Physics.Raycast(seat + Vector3.up * 0.5f, -dir, 0.7f, ~0, QueryTriggerInteraction.Ignore) ? 1f : 0.5f;
                if (score > best)
                {
                    best = score;
                    yaw = a;
                }
            }
            return best > 0f;
        }

        /// <summary>작업 자리: 손 높이 앞 0.3~0.75m에 화면 · 장비가 있는 방향.</summary>
        private static bool TryWork(Vector3 ground, out float yaw)
        {
            yaw = 0f;
            float bestDist = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                foreach (float height in WorkHeights)
                {
                    if (!Physics.Raycast(ground + Vector3.up * height, dir, out var hit, 0.75f, ~0, QueryTriggerInteraction.Ignore) || hit.distance < 0.3f)
                        continue;
                    if (!IsWorkSurface(MaterialName(hit)))
                        continue;
                    if (hit.distance < bestDist)
                    {
                        bestDist = hit.distance;
                        yaw = a;
                    }
                }
            }
            return bestDist < float.MaxValue;
        }

        /// <summary>손 높이 (작업대 · 화면 · 높은 콘솔).</summary>
        private static readonly float[] WorkHeights = { 0.85f, 1.1f, 1.35f }; // 0.85: 상황판 테이블처럼 낮은 작업대 (0.95만 쓰면 손상 통제실이 0개)

        /// <summary>일하는 대상 재질: 화면 · 장비 · 의료 장비.</summary>
        private static bool IsWorkSurface(string material)
        {
            return material != null && (material.Contains("Screen") || material.Contains("Device") || material.Contains("Clinic"));
        }

        /// <summary>맞은 삼각형의 재질 이름 (MeshCollider만).</summary>
        private static string MaterialName(RaycastHit hit)
        {
            if (!(hit.collider is MeshCollider mc) || mc.sharedMesh == null || hit.triangleIndex < 0)
                return null;
            var mesh = mc.sharedMesh;
            var renderer = mc.GetComponent<Renderer>();
            if (renderer == null)
                return null;
            int index = hit.triangleIndex * 3;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var sub = mesh.GetSubMesh(s);
                if (index >= sub.indexStart && index < sub.indexStart + sub.indexCount)
                {
                    var mats = renderer.sharedMaterials;
                    return s < mats.Length && mats[s] != null ? mats[s].name : null;
                }
            }
            return null;
        }

        /// <summary>
        /// 서기 자리가 바라볼 방향: 눈높이에서 가장 멀리 트인 방향 (같으면 방 가운데 쪽).
        /// 방 가운데를 바라보게 했더니 코어에서는 가운데 유리 탑 벽을 코앞에서 보고 서 있었음.
        /// </summary>
        private static float OpenYaw(Vector3 ground, float towardCenter)
        {
            float bestYaw = towardCenter, best = -1f;
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                float d = Physics.Raycast(ground + Vector3.up * 1.5f, dir, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 6f;
                d += Mathf.Cos(Mathf.DeltaAngle(a, towardCenter) * Mathf.Deg2Rad) * 0.5f; // 가운데 쪽을 조금 더
                if (d > best)
                {
                    best = d;
                    bestYaw = a;
                }
            }
            return bestYaw;
        }

        private static float YawTo(Vector3 from, Vector3 to)
        {
            var d = to - from;
            d.y = 0f;
            return d.sqrMagnitude < 0.01f ? 0f : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>이미 고른 자리에서 가장 먼 후보부터 (간격 <see cref="MinSpacing"/> 이상).</summary>
        private static List<Candidate> Pick(List<Candidate> pool, int count, List<Vector3> chosen)
        {
            var result = new List<Candidate>();
            var left = new List<Candidate>(pool);
            while (result.Count < count && left.Count > 0)
            {
                int bestIndex = -1;
                float bestDist = float.MinValue;
                for (int i = 0; i < left.Count; i++)
                {
                    // 처음 하나는 칸 원점에 가장 가까운 후보, 그 뒤로는 고른 자리에서 가장 먼 후보
                    float d = chosen.Count == 0 ? -left[i].Local.sqrMagnitude : float.MaxValue;
                    foreach (var c in chosen)
                    {
                        var v = left[i].Local - c;
                        v.y = 0f;
                        d = Mathf.Min(d, v.sqrMagnitude);
                    }
                    if (chosen.Count > 0 && d < MinSpacing * MinSpacing)
                        continue;
                    if (d > bestDist)
                    {
                        bestDist = d;
                        bestIndex = i;
                    }
                }
                if (bestIndex < 0)
                    break;
                result.Add(left[bestIndex]);
                chosen.Add(left[bestIndex].Local);
                left.RemoveAt(bestIndex);
            }
            return result;
        }
    }
}
