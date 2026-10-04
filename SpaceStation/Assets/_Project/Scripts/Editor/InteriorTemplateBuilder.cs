using System.Collections.Generic;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// Phase 11-4~ 모듈별 내부 템플릿 만들기 (메뉴 SpaceStation/Interior/Build Templates):
    /// Blender `BlenderWork/Interior_Kit.blend`(텍스트 블록 `core_builder` 등) → `Art/Models/Interior/SM_Interior_X.fbx`의
    /// INT_X_* 오브젝트를 프리팹(`Prefabs/Interior/PF_Interior_X`)으로 조립하고 템플릿 에셋(`Data/Interior/IT_X`)에 문 자리·스폰을 적는다.
    /// 재질 슬롯 이름 → Hull·HullDark = 외부 공용, Accent = 그 모듈 강조색, Light = 천장 조명, Glass = 창 유리.
    /// "_Shell"·"_Glass" 오브젝트는 MeshCollider(걷는 면·창 막기), 나머지는 보기만.
    /// 다시 실행하면 덮어쓴다. 끝나면 Interior/Setup으로 InteriorMode에 연결.
    /// </summary>
    public static class InteriorTemplateBuilder
    {
        private const string ModelFolder = "Assets/_Project/Art/Models/Interior/";
        private const string PrefabFolder = "Assets/_Project/Prefabs/Interior/";
        private const string DataFolder = "Assets/_Project/Data/Interior/";
        private const string ModuleFolder = "Assets/_Project/Data/Modules/";
        private const string StationMaterials = "Assets/_Project/Art/Materials/Station/";
        private const string InteriorMaterials = "Assets/_Project/Art/Materials/Interior/";
        private const string AccentFolder = "Assets/_Project/Art/Materials/";

        private const float F = -1.3f; // InteriorGeometry.FloorOffset
        private const float Socket = 3.2f;

        [MenuItem("SpaceStation/Interior/Build Templates")]
        public static void BuildAll()
        {
            InteriorSetup.CreateMaterials(); // 유리 재질 등이 먼저 있어야 함
            BuildCore();
            AssetDatabase.SaveAssets();
            InteriorSetup.Setup();
        }

        /// <summary>11-4 코어: 12각 고리 복도(반지름 4.4~7.0) + 바깥 연결 통로 8곳 + 안쪽 창 너머 허브·통신탑. 고리 중심 = 로컬 (4, 4).</summary>
        private static void BuildCore()
        {
            var sockets = new List<InteriorSocket>();
            for (int x = 0; x < 2; x++)
            {
                for (int z = 0; z < 2; z++)
                {
                    var cell = new Vector3Int(x, 0, z);
                    sockets.Add(new InteriorSocket(cell, x == 0 ? Vector3Int.left : Vector3Int.right, Socket));
                    sockets.Add(new InteriorSocket(cell, z == 0 ? new Vector3Int(0, 0, -1) : new Vector3Int(0, 0, 1), Socket));
                    sockets.Add(new InteriorSocket(cell, Vector3Int.down, -F)); // 매달린 모듈: 고리 바닥 해치
                }
            }
            // 고리 복도 남쪽, 탑을 바라보며
            var lights = new List<(Vector3, float, float, Color)>();
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f + Mathf.PI / 6f;
                lights.Add((new Vector3(4f + Mathf.Cos(a) * 5.7f, F + 3.1f, 4f + Mathf.Sin(a) * 5.7f), 6.5f, 3.5f, new Color(1f, 0.96f, 0.9f)));
            }
            // 창 너머 탑을 비추는 조명 (허브 위)
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2f / 3f;
                lights.Add((new Vector3(4f + Mathf.Cos(a) * 3.0f, 3.5f, 4f + Mathf.Sin(a) * 3.0f), 7f, 2.5f, new Color(0.85f, 0.92f, 1f)));
            }
            Build("Core", "MD_Core", sockets, new Vector3(4f, F, 4f - 5.7f), 0f, lights);
        }

        private static void Build(string name, string moduleName, List<InteriorSocket> sockets, Vector3 spawn, float spawnYaw,
            List<(Vector3 Position, float Range, float Intensity, Color Color)> lights)
        {
            string modelPath = ModelFolder + "SM_Interior_" + name + ".fbx";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[InteriorTemplateBuilder] 모델 없음: {modelPath}");
                return;
            }
            var module = AssetDatabase.LoadAssetAtPath<ModuleData>(ModuleFolder + moduleName + ".asset");
            var accent = AssetDatabase.LoadAssetAtPath<Material>(AccentFolder + "M_Accent_" + moduleName.Replace("MD_", "") + ".mat");
            var materials = new Dictionary<string, Material>
            {
                { "Hull", AssetDatabase.LoadAssetAtPath<Material>(StationMaterials + "M_Hull.mat") },
                { "HullDark", AssetDatabase.LoadAssetAtPath<Material>(StationMaterials + "M_HullDark.mat") },
                { "Accent", accent },
                { "Light", AssetDatabase.LoadAssetAtPath<Material>(InteriorMaterials + "M_InteriorLight.mat") },
                { "Glass", AssetDatabase.LoadAssetAtPath<Material>(InteriorMaterials + "M_InteriorGlass.mat") },
            };

            var root = new GameObject("PF_Interior_" + name);
            foreach (Transform part in model.transform)
            {
                var filter = part.GetComponent<MeshFilter>();
                if (filter == null)
                    continue;
                var go = new GameObject(part.name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = part.localPosition;
                go.transform.localRotation = part.localRotation;
                go.transform.localScale = part.localScale;
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var slots = part.GetComponent<MeshRenderer>().sharedMaterials;
                var mats = new Material[slots.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    string slot = slots[i] != null ? slots[i].name : "";
                    if (!materials.TryGetValue(slot, out mats[i]) || mats[i] == null)
                    {
                        Debug.LogWarning($"[InteriorTemplateBuilder] {part.name}: 재질 슬롯 '{slot}' → Hull");
                        mats[i] = materials["Hull"];
                    }
                }
                r.sharedMaterials = mats;
                if (part.name.EndsWith("_Shell") || part.name.EndsWith("_Glass"))
                    go.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            }
            var lightRoot = new GameObject("Lights");
            lightRoot.transform.SetParent(root.transform, false);
            foreach (var (position, range, intensity, color) in lights)
            {
                var go = new GameObject("Light");
                go.transform.SetParent(lightRoot.transform, false);
                go.transform.localPosition = position;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.range = range;
                light.intensity = intensity;
                light.color = color;
            }

            System.IO.Directory.CreateDirectory(PrefabFolder);
            string prefabPath = PrefabFolder + "PF_Interior_" + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);

            string templatePath = DataFolder + "IT_" + name + ".asset";
            var template = AssetDatabase.LoadAssetAtPath<InteriorTemplate>(templatePath);
            if (template == null)
            {
                template = ScriptableObject.CreateInstance<InteriorTemplate>();
                AssetDatabase.CreateAsset(template, templatePath);
            }
            template.EditorSet(module, prefab, sockets, spawn, spawnYaw);
            EditorUtility.SetDirty(template);
            Debug.Log($"[InteriorTemplateBuilder] {name}: 문 자리 {sockets.Count}곳, 조명 {lights.Count}개");
        }
    }
}
