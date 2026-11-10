using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 3단계 내부 KWS 물 만들기 (사용자가 받은 실사 물 패키지 Assets/KriptoFX — git 제외).
    /// 패키지 없는 PC에서도 컴파일되게 KWS 타입을 이름으로 찾고 값은 SerializedObject로 넣음 (없으면 아무것도 안 만들고 null → Blender 수면이 그대로 물).
    /// KWS '내 메시' 규칙: 기준점 = 수면 (어긋나면 물이 떠 보임), 정점 색 흰색 = 수면 (물결 · 반사) · 검정 = 옆 · 바닥 (부피 — 물빛 무늬가 바닥에 닿음).
    /// </summary>
    internal static class KwsWaterBuilder
    {
        private const string MeshFolder = "Assets/_Project/Art/Models/Interior/Generated";
        private const string SettingsFolder = "Assets/_Project/Data/Interior/Water";

        public static System.Type WaterType => System.Type.GetType("KWS.WaterSystem, KWS");
        private static System.Type SettingsType => System.Type.GetType("KWS.WaterSystemScriptableData, KWS");
        private static System.Type InteractType => System.Type.GetType("KWS.KWS_InteractWithWater, KWS");

        /// <summary>
        /// Blender 수면 물체 (윗면 = 수면 높이 level)에서 부피 메시를 만들어 꺼 둔 "KWS_<id>" 물체로 (켜기 = InteriorKwsWater).
        /// dynamicWaves = 폭포 물결 (KWS_InteractWithWater)을 받을 물.
        /// </summary>
        public static GameObject Build(Transform root, MeshFilter surface, float level, float depth, string id, bool dynamicWaves)
        {
            var waterType = WaterType;
            var settingsType = SettingsType;
            if (waterType == null || settingsType == null || surface == null)
                return null;
            var mesh = VolumeMesh(root, surface, level, depth, "SM_KWS_" + id);
            if (mesh == null)
                return null;
            var settings = LoadOrCreateSettings(settingsType, id);
            var so = new SerializedObject(settings);
            void F(string p, float v) => so.FindProperty(p).floatValue = v;
            void B(string p, bool v) => so.FindProperty(p).boolValue = v;
            void C(string p, Color v) => so.FindProperty(p).colorValue = v;
            void I(string p, int v) => so.FindProperty(p).intValue = v;
            I("WaterMeshType", 3);   // CustomMesh
            so.FindProperty("CustomMesh").objectReferenceValue = mesh;
            // 우리 정원 물 (2026-11-11 Unity 시험 값): 짙은 청록 탁도 · 맑음 1 m, 잔잔한 바람 물결, 화면 반사 + 물 전용 우주 하늘 (InteriorKwsWater)
            F("Transparent", 1f);
            C("TurbidityColor", new Color(0.06f, 0.3f, 0.32f));
            C("DyeColor", new Color(0.85f, 1f, 0.96f, 1f));
            B("OverrideSkyColor", false);
            B("UsePlanarReflection", false);
            B("UseScreenSpaceReflection", true);
            F("GlobalWindSpeed", 0.6f);
            F("GlobalWindTurbulence", 0.15f);
            I("GlobalFftWavesQuality", 64);   // Medium
            I("GlobalFftWavesCascades", 2);
            B("UseVolumetricLight", false);
            B("UseUnderwaterEffect", false);
            B("UseRefractionDispersion", false);
            B("UseIntersectionFoam", true);
            B("UseCausticEffect", true);
            F("CausticStrength", 2f);
            F("GlobalCausticDepth", 1f);
            B("UseDynamicWaves", dynamicWaves);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);

            var go = new GameObject("KWS_" + id);
            go.SetActive(false);   // KWS = [ExecuteAlways]: 템플릿을 만들 때 씬에 숨은 관리 물체가 생기지 않게 꺼 둔 채 저장
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, level, 0f);
            var water = go.AddComponent(waterType);
            var wso = new SerializedObject(water);
            wso.FindProperty("Profile").objectReferenceValue = settings;
            wso.ApplyModifiedPropertiesWithoutUndo();
            return go;
        }

        /// <summary>폭포가 떨어지는 자리에 끊임없는 물결 (KWS 물 물체 아래 — 함께 켜짐).</summary>
        public static void Ripple(GameObject water, Vector3 localPoint, float size, float strength)
        {
            var type = InteractType;
            if (type == null || water == null)
                return;
            var go = new GameObject("FallsRipple");
            go.transform.SetParent(water.transform.parent, false);
            go.transform.localPosition = localPoint;
            go.transform.SetParent(water.transform, true);
            var c = go.AddComponent(type);
            var so = new SerializedObject(c);
            so.FindProperty("MeshType").intValue = 1;   // Sphere
            so.FindProperty("Size").floatValue = size;
            so.FindProperty("AdditionalConstantStrength").floatValue = strength;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Object LoadOrCreateSettings(System.Type type, string id)
        {
            EnsureFolder(SettingsFolder);
            string path = $"{SettingsFolder}/KWS_{id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (existing != null)
                return existing;
            var created = ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        // 수면 (윗면 · 높이 level ± 3 cm)만 골라 흰색, 가장자리를 depth 아래까지 내린 옆면 · 뒤집은 바닥은 검정 — 정점은 기준점 (수면) 기준
        private static Mesh VolumeMesh(Transform root, MeshFilter surface, float level, float depth, string name)
        {
            var src = surface.sharedMesh;
            var v = src.vertices;
            var tris = src.triangles;
            var toRoot = root.worldToLocalMatrix * surface.transform.localToWorldMatrix;
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var top = new List<int>();
            var map = new Dictionary<int, int>();
            var p = new Vector3[3];
            for (int i = 0; i < tris.Length; i += 3)
            {
                for (int k = 0; k < 3; k++)
                    p[k] = toRoot.MultiplyPoint3x4(v[tris[i + k]]);
                var n = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
                if (n.y < 0.9f || Mathf.Abs((p[0].y + p[1].y + p[2].y) / 3f - level) > 0.03f)
                    continue;
                for (int k = 0; k < 3; k++)
                {
                    int id = tris[i + k];
                    if (!map.TryGetValue(id, out int j))
                    {
                        j = verts.Count;
                        map[id] = j;
                        verts.Add(new Vector3(p[k].x, 0f, p[k].z));
                        cols.Add(Color.white);
                    }
                    top.Add(j);
                }
            }
            if (top.Count == 0)
            {
                Debug.LogWarning($"[KwsWaterBuilder] {surface.name}: 높이 {level} 수면이 없음");
                return null;
            }
            int nTop = verts.Count;
            long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            var edges = new Dictionary<long, int>();
            for (int i = 0; i < top.Count; i += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    long key = Key(top[i + e], top[i + (e + 1) % 3]);
                    edges[key] = edges.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }
            var sides = new List<int>();
            for (int i = 0; i < top.Count; i += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = top[i + e], b = top[i + (e + 1) % 3];
                    if (edges[Key(a, b)] != 1)
                        continue;
                    int a1 = verts.Count; verts.Add(verts[a]); cols.Add(Color.black);
                    int b1 = verts.Count; verts.Add(verts[b]); cols.Add(Color.black);
                    int a2 = verts.Count; verts.Add(new Vector3(verts[a].x, -depth, verts[a].z)); cols.Add(Color.black);
                    int b2 = verts.Count; verts.Add(new Vector3(verts[b].x, -depth, verts[b].z)); cols.Add(Color.black);
                    sides.AddRange(new[] { b1, a1, a2, b1, a2, b2 });
                }
            }
            int off = verts.Count;
            for (int i = 0; i < nTop; i++)
            {
                verts.Add(new Vector3(verts[i].x, -depth, verts[i].z));
                cols.Add(Color.black);
            }
            var bottom = new List<int>();
            for (int i = 0; i < top.Count; i += 3)
                bottom.AddRange(new[] { top[i] + off, top[i + 2] + off, top[i + 1] + off });
            var all = new List<int>(top);
            all.AddRange(sides);
            all.AddRange(bottom);

            EnsureFolder(MeshFolder);
            string path = $"{MeshFolder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew)
                mesh = new Mesh();
            mesh.Clear();
            mesh.name = name;
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetTriangles(all, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (isNew)
                AssetDatabase.CreateAsset(mesh, path);
            else
                EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            int cut = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, cut));
            AssetDatabase.CreateFolder(path.Substring(0, cut), path.Substring(cut + 1));
        }
    }
}
