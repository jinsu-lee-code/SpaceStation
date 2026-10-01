using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 5-6 연결 통로용 표면 깊이 측정 (메뉴 SpaceStation/Art/Measure Connector Depths, 모듈 조립 후 자동 실행).
    /// 프리팹을 멀리 떨어진 곳에 임시로 놓고 모든 메시에 MeshCollider를 붙인 뒤, 칸 경계 바깥에서 칸 중심 쪽으로
    /// 광선을 쏴서 모델 표면까지의 깊이를 잰다. 가운데 광선이 빗나가면 주변 4개를 시도하고, 모두 빗나가면 -1(연결점 없음).
    /// </summary>
    public static class ConnectorDepthBaker
    {
        private static readonly Vector3 TempOrigin = new Vector3(5000f, 5000f, 5000f);
        private const float RayStart = 0.55f;  // 칸 중심에서 이만큼 바깥에서 시작 (이웃 칸 쪽)
        private const float MinDepth = 0.05f;  // 표면이 중심에 너무 가까우면 이 값으로
        private const float ProbeOffset = 0.07f;

        [MenuItem("SpaceStation/Art/Measure Connector Depths")]
        public static void MeasureAll()
        {
            var log = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data/Modules" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null && data.Prefab != null)
                    log.Add(Measure(data));
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ConnectorDepthBaker]\n" + string.Join("\n", log));
        }

        public static string Measure(ModuleData data)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(data.Prefab);
            var colliders = new List<Collider>();
            try
            {
                instance.transform.SetPositionAndRotation(TempOrigin, Quaternion.identity);
                foreach (var c in instance.GetComponentsInChildren<Collider>(true))
                    c.enabled = false; // 셀 판정용 Visual 상자는 제외
                foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null)
                        continue;
                    var mc = filter.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = filter.sharedMesh;
                    colliders.Add(mc);
                }
                Physics.SyncTransforms();

                // 태양을 따라 회전하는 패널이 있으면 윗면 연결점 없음 (기둥이 패널을 뚫지 않도록)
                bool rotatingTop = instance.GetComponentInChildren<SpaceStation.Building.SunFacingPanel>(true) != null;
                var offsets = new HashSet<Vector3Int>(data.CellOffsets);
                var depths = new List<FaceDepth>();
                var summary = new List<string>();
                foreach (var cell in data.CellOffsets)
                {
                    foreach (var dir in GridDirections.Faces)
                    {
                        if (offsets.Contains(cell + dir))
                            continue;
                        float depth = rotatingTop && dir == Vector3Int.up ? -1f : Probe(colliders, TempOrigin + (Vector3)cell, dir);
                        depths.Add(new FaceDepth(cell, dir, depth));
                        summary.Add($"{cell}{Short(dir)}={(depth < 0 ? "없음" : depth.ToString("0.00"))}");
                    }
                }
                Undo.RecordObject(data, "Measure Connector Depths");
                data.SetFaceDepths(depths);
                EditorUtility.SetDirty(data);
                return $"{data.name}: {string.Join(" ", summary)}";
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static float Probe(List<Collider> colliders, Vector3 center, Vector3Int dir)
        {
            Vector3 d = dir;
            // 면에 수직인 두 축
            Vector3 u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
            Vector3 v = Vector3.Cross(d, u);
            var probes = new[] { Vector3.zero, u * ProbeOffset, -u * ProbeOffset, v * ProbeOffset, -v * ProbeOffset };
            foreach (var p in probes)
            {
                float best = -1f;
                var origin = center + d * RayStart + p;
                foreach (var hit in Physics.RaycastAll(origin, -d, RayStart, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!colliders.Contains(hit.collider))
                        continue;
                    float depth = RayStart - hit.distance;
                    best = Mathf.Max(best, depth);
                }
                if (best >= 0f)
                    return Mathf.Clamp(best, MinDepth, 0.5f);
            }
            return -1f;
        }

        private static string Short(Vector3Int d)
        {
            if (d.x > 0) return "+X";
            if (d.x < 0) return "-X";
            if (d.y > 0) return "+Y";
            if (d.y < 0) return "-Y";
            return d.z > 0 ? "+Z" : "-Z";
        }
    }
}
