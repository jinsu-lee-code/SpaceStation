using System.Collections.Generic;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 식물 아틀라스 ③ 화분 자리 식물 고르기 (2026-11-16, <see cref="InteriorPlantSpots.SetAll"/> 끝에서): 화분 자리마다 우리 화분 식물 또는
    /// 아틀라스 식물 (맥문동 꽃 · 대나무 새싹 · 작은 벚나무 덤불) 중 하나 — 자리 (템플릿 이름 · 번호)로 정해지는 섞기.
    /// 화분마다 실제 테 · 둘레를 잼: 템플릿을 잠시 놓고 흙 위 높이마다 16방향 광선 (충돌체 = 화분 테 · 벽 · 가구)으로 빈 반지름을 재서,
    /// 아틀라스 식물 꼭짓점 (가장 가까운 단계)이 그 높이의 빈 반지름 − 1.5 cm 안 (또는 우리 화분 식물이 이미 퍼진 반지름 안)일 때만 그 자리 후보.
    /// 우리 화분 식물은 흙 위 20 cm까지 가늘어 (반지름 0.14) 테를 안 넘지만, 흙 바로 위에서 퍼지는 식물은 화분 테를 뚫음 — 수호초 (흙 위 ± 9 cm에서 반지름 0.6)는
    /// 맞는 화분이 하나도 없어 뺐고, 테가 좁은 화분 (생태 모듈 · 연결 허브 등)은 우리 화분 식물 그대로.
    /// 모델 배율 = 우리 화분 식물 (반지름 0.66 · 키 0.94) 크기 안 (반지름 ≤ 0.62 · 키 ≤ 0.95).
    /// </summary>
    public static class InteriorPlantVariants
    {
        /// <summary>화분 아틀라스 식물: 종류 · 메시 (Art/Plants/Meshes) · 모델 배율 · 섞는 무게.</summary>
        public static readonly (PlantKind Kind, string Mesh, float Scale, int Weight)[] Pot =
        {
            (PlantKind.Liriope, "LiriopeSmall_Mesh", 0.67f, 3),
            (PlantKind.Bamboo, "BambooSeedling_Mat", 0.53f, 2),
            (PlantKind.CherryShrub, "CherryShrub_02_Mesh", 0.72f, 2),
        };

        /// <summary>우리 화분 식물 (SM_PlantPot)의 무게 — 아틀라스 식물과 섞어 둠.</summary>
        public const int OursWeight = 3;

        private const string OursPath = "Assets/_Project/Art/Models/Interior/Plants/SM_PlantPot.fbx";
        private static readonly float[] Heights = { 0.02f, 0.06f, 0.1f, 0.15f, 0.2f, 0.3f, 0.45f, 0.6f, 0.8f, 1.0f };
        private const float Margin = 0.015f;

        public static bool IsPotKind(PlantKind k) => k == PlantKind.Pot || System.Array.Exists(Pot, p => p.Kind == k);

        public static GameObject Model(string mesh) => AssetDatabase.LoadAssetAtPath<GameObject>(PlantAtlas.MeshFolder + mesh + ".fbx");

        /// <summary>모델 꼭짓점 (가장 가까운 단계 · 모델 루트 기준).</summary>
        public static List<Vector3> Vertices(GameObject model)
        {
            var list = new List<Vector3>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || (mf.name.Contains("_LOD") && !mf.name.EndsWith("_LOD0")))
                    continue;
                using (var data = Mesh.AcquireReadOnlyMeshData(mf.sharedMesh))
                {
                    var v = new Unity.Collections.NativeArray<Vector3>(mf.sharedMesh.vertexCount, Unity.Collections.Allocator.Temp);
                    data[0].GetVertices(v);
                    foreach (var p in v)
                        list.Add(model.transform.InverseTransformPoint(mf.transform.TransformPoint(p)));
                    v.Dispose();
                }
            }
            return list;
        }

        /// <summary>흙 위 높이마다 빈 반지름 (16방향 광선이 처음 닿는 거리 중 가장 짧은 것, 1.2 m까지) — 물리 씬에 놓인 템플릿에서.</summary>
        public static float[] Clearance(Vector3 soil)
        {
            var c = new float[Heights.Length];
            for (int i = 0; i < Heights.Length; i++)
            {
                float min = 1.2f;
                for (int a = 0; a < 16; a++)
                {
                    float ang = a * Mathf.PI / 8f;
                    var d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    if (Physics.Raycast(soil + Vector3.up * Heights[i], d, out var hit, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        min = Mathf.Min(min, hit.distance);
                }
                c[i] = min;
            }
            return c;
        }

        private static float Band(float[] clear, float y)
        {
            float best = 9f;
            bool any = false;
            for (int i = 0; i < Heights.Length; i++)
            {
                if (Heights[i] >= y - 0.05f && Heights[i] <= y + 0.05f)
                {
                    best = Mathf.Min(best, clear[i]);
                    any = true;
                }
            }
            if (any)
                return best;
            if (y < Heights[0])
                return clear[0];
            return y > Heights[Heights.Length - 1] ? clear[Heights.Length - 1] : best;
        }

        private static float OursRadius(List<Vector3> ours, float y)
        {
            float r = 0f;
            foreach (var p in ours)
            {
                if (Mathf.Abs(p.y - y) < 0.05f)
                    r = Mathf.Max(r, new Vector2(p.x, p.z).magnitude);
            }
            return r;
        }

        /// <summary>
        /// 아틀라스 식물 꼭짓점 (모델 배율 × 자리 배율)이 화분 빈 반지름 안인가: 높이마다 반지름 ≤ max(빈 반지름 − 1.5 cm, 우리 화분 식물 반지름 × 자리 배율).
        /// </summary>
        public static bool Fits(List<Vector3> verts, float scale, float spotScale, float[] clear, List<Vector3> ours, out string why)
        {
            float k = scale * spotScale;
            foreach (var p in verts)
            {
                float y = p.y * k, r = new Vector2(p.x, p.z).magnitude * k;
                if (y > 1.05f)
                    continue;
                float room = Band(clear, y) - Margin;
                if (r <= room)
                    continue;
                if (r <= OursRadius(ours, y / spotScale) * spotScale)
                    continue;
                why = $"높이 {y:F2} 반지름 {r:F2} > 빈 {room:F2}";
                return false;
            }
            why = null;
            return true;
        }

        private static uint Hash(string s, int i)
        {
            uint h = 2166136261u;
            foreach (char ch in s)
                h = (h ^ ch) * 16777619u;
            h = (h ^ (uint)i) * 16777619u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            return h ^ (h >> 15);
        }

        /// <summary>모든 템플릿의 화분 자리를 다시 고름 (우리 화분 식물 · 맞는 아틀라스 식물 중 무게로).</summary>
        public static void Assign()
        {
            var ours = Vertices(AssetDatabase.LoadAssetAtPath<GameObject>(OursPath));
            var verts = new List<Vector3>[Pot.Length];
            for (int v = 0; v < Pot.Length; v++)
                verts[v] = Vertices(Model(Pot[v].Mesh));
            var count = new Dictionary<PlantKind, int>();
            foreach (var guid in AssetDatabase.FindAssets("t:InteriorTemplate"))
            {
                var t = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                bool pots = false;
                foreach (var s in t.PlantSpots)
                    pots |= IsPotKind(s.Kind);
                if (t.Prefab == null || !pots)
                    continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(t.Prefab);
                go.transform.position = new Vector3(0f, -500f, 0f);
                Physics.SyncTransforms();
                var spots = new List<PlantSpot>();
                for (int i = 0; i < t.PlantSpots.Count; i++)
                {
                    var s = t.PlantSpots[i];
                    if (!IsPotKind(s.Kind))
                    {
                        spots.Add(s);
                        continue;
                    }
                    var clear = Clearance(go.transform.TransformPoint(s.Position));
                    var kinds = new List<(PlantKind, int)> { (PlantKind.Pot, OursWeight) };
                    for (int v = 0; v < Pot.Length; v++)
                    {
                        if (Fits(verts[v], Pot[v].Scale, s.Scale, clear, ours, out _))
                            kinds.Add((Pot[v].Kind, Pot[v].Weight));
                    }
                    int total = 0;
                    foreach (var (_, w) in kinds)
                        total += w;
                    int pick = (int)(Hash(t.name, i) % (uint)total);
                    var kind = PlantKind.Pot;
                    foreach (var (k, w) in kinds)
                    {
                        if (pick < w)
                        {
                            kind = k;
                            break;
                        }
                        pick -= w;
                    }
                    count[kind] = count.TryGetValue(kind, out int n) ? n + 1 : 1;
                    spots.Add(new PlantSpot(s.Position, kind, s.Scale, s.Yaw));
                }
                Object.DestroyImmediate(go);
                t.EditorSetPlants(spots);
                EditorUtility.SetDirty(t);
            }
            var msg = new System.Text.StringBuilder("[InteriorPlantVariants] 화분 식물:");
            foreach (var kv in count)
                msg.Append($" {kv.Key} {kv.Value}");
            Debug.Log(msg.ToString());
        }
    }
}
