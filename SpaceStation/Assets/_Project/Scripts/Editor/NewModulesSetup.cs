using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 12-5 새 기본 모듈 12종 그레이박스 (메뉴 SpaceStation/Modules/Setup New Basic, 사용자 결정 2026-10-14, BALANCE 39번).
    /// 방열판(대륙급) · 통신 중계소(지구급)는 12-3c에서 이미 만듦 → 14종 완성.
    /// - 도시급 8: 학교(준비 중) · 구조 프레임 · 연결 허브 · 센서 어레이 · 검역소(준비 중) · 단백질 배양실 · 물 추출기 · 데이터 센터
    /// - 국가급 4: 상업 구역(준비 중) · 행정 관제소(준비 중) · 방사선 차폐실(준비 중) · 여객 도킹 포트
    /// - 준비 중 = 연결되는 작업(12-6 새 요구 · 12-7 역병 · 12-8 방사선 폭풍) 전까지 건설 메뉴에 '준비 중'으로 보이고 지을 수 없음.
    /// 모양은 칸마다 색 상자 + 꼭대기 표식 (고유 모델 · 내부 템플릿은 다음 단계). 여러 번 실행해도 같은 에셋을 갱신.
    /// </summary>
    public static class NewModulesSetup
    {
        private const string ModuleDir = "Assets/_Project/Data/Modules";
        private const string PrefabDir = "Assets/_Project/Prefabs/Modules/NewBasic";
        private const string MaterialDir = "Assets/_Project/Art/Materials/NewBasic";
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";

        private sealed class Def
        {
            public string Id, Name, Color;
            public ModuleCategory Category;
            public int Grade;
            public Vector3Int[] Cells = { Vector3Int.zero };
            public float Metal;
            public (ResourceType, float)[] Production = new (ResourceType, float)[0];
            public (ResourceType, float)[] Consumption = new (ResourceType, float)[0];
            public bool CountsTowardGrade = true;
            public string ComingSoon;
            public PrimitiveType Marker = PrimitiveType.Cylinder;
            public System.Action<SerializedObject> Stats;
        }

        private static Vector3Int V(int x, int y, int z) => new Vector3Int(x, y, z);

        private static readonly Def[] Defs =
        {
            // ---- 도시급 ----
            new Def { Id = "School", Name = "학교", Color = "#FFC86B", Category = ModuleCategory.Life, Grade = 5, Cells = new[] { V(0, 0, 0), V(1, 0, 0) },
                Metal = 120, Consumption = new[] { (ResourceType.Power, 3f) }, ComingSoon = "새 요구 '교육' · 12-6", Marker = PrimitiveType.Cube },
            new Def { Id = "StructureFrame", Name = "구조 프레임", Color = "#9AA4B2", Category = ModuleCategory.Industry, Grade = 5,
                Metal = 10, CountsTowardGrade = false, Marker = PrimitiveType.Cube },
            new Def { Id = "ConnectorHub", Name = "연결 허브", Color = "#7FD8FF", Category = ModuleCategory.Industry, Grade = 5,
                Metal = 40, Consumption = new[] { (ResourceType.Power, 1f) }, CountsTowardGrade = false, Marker = PrimitiveType.Sphere,
                Stats = so => so.FindProperty("_adjacencyRelay").boolValue = true },
            new Def { Id = "SensorArray", Name = "센서 어레이", Color = "#B79CFF", Category = ModuleCategory.Defense, Grade = 5,
                Metal = 90, Consumption = new[] { (ResourceType.Power, 3f) },
                Stats = so => so.FindProperty("_warningBonus").floatValue = 20f },
            new Def { Id = "Quarantine", Name = "검역소", Color = "#8FE39A", Category = ModuleCategory.Life, Grade = 5,
                Metal = 100, Consumption = new[] { (ResourceType.Power, 3f) }, ComingSoon = "역병 대응 · 12-7", Marker = PrimitiveType.Cube },
            new Def { Id = "ProteinVat", Name = "단백질 배양실", Color = "#E8D9B5", Category = ModuleCategory.Life, Grade = 5,
                Metal = 80, Production = new[] { (ResourceType.Food, 2.5f) }, Consumption = new[] { (ResourceType.Power, 3f), (ResourceType.Water, 1f) } },
            new Def { Id = "WaterExtractor", Name = "물 추출기", Color = "#6BB8FF", Category = ModuleCategory.Industry, Grade = 5,
                Metal = 110, Production = new[] { (ResourceType.Water, 3f) }, Consumption = new[] { (ResourceType.Power, 4f) } },
            new Def { Id = "DataCenter", Name = "데이터 센터", Color = "#9E80FF", Category = ModuleCategory.Industry, Grade = 5, Cells = new[] { V(0, 0, 0), V(1, 0, 0) },
                Metal = 160, Consumption = new[] { (ResourceType.Power, 8f) }, Marker = PrimitiveType.Cube,
                Stats = so => so.FindProperty("_researchSpeedBonus").floatValue = 0.1f },
            // ---- 국가급 ----
            new Def { Id = "Commerce", Name = "상업 구역", Color = "#FFA040", Category = ModuleCategory.Life, Grade = 6, Cells = new[] { V(0, 0, 0), V(1, 0, 0) },
                Metal = 180, Consumption = new[] { (ResourceType.Power, 5f) }, ComingSoon = "새 요구 '소비' · 12-6", Marker = PrimitiveType.Cube },
            new Def { Id = "Administration", Name = "행정 관제소", Color = "#C8D2DE", Category = ModuleCategory.Industry, Grade = 6, Cells = new[] { V(0, 0, 0), V(1, 0, 0) },
                Metal = 200, Consumption = new[] { (ResourceType.Power, 5f) }, ComingSoon = "새 요구 '행정' · 12-6", Marker = PrimitiveType.Cube },
            new Def { Id = "RadiationShelter", Name = "방사선 차폐실", Color = "#FFE14A", Category = ModuleCategory.Defense, Grade = 6, Cells = new[] { V(0, 0, 0), V(1, 0, 0) },
                Metal = 220, Consumption = new[] { (ResourceType.Power, 3f) }, ComingSoon = "방사선 폭풍 대피 · 12-8", Marker = PrimitiveType.Cube },
            new Def { Id = "PassengerPort", Name = "여객 도킹 포트", Color = "#7CFF9A", Category = ModuleCategory.Life, Grade = 6, Cells = new[] { V(0, 0, 0), V(1, 0, 0) },
                Metal = 200, Consumption = new[] { (ResourceType.Power, 4f) },
                Stats = so =>
                {
                    so.FindProperty("_immigrationPort").boolValue = true;
                    // 도킹 배치 규칙 (화물 터미널과 같음): 뒷면으로 붙고 앞 접근로 2칸은 비어 있어야
                    so.FindProperty("_terminalOnly").boolValue = true;
                    so.FindProperty("_dockFront").vector3IntValue = V(0, 0, 1);
                    so.FindProperty("_approachLaneLength").intValue = 2;
                } },
        };

        [MenuItem("SpaceStation/Modules/Setup New Basic")]
        public static void Run()
        {
            System.IO.Directory.CreateDirectory(PrefabDir);
            System.IO.Directory.CreateDirectory(MaterialDir);
            AssetDatabase.Refresh();
            var paths = new List<string>();
            foreach (var def in Defs)
            {
                var mat = Material(def);
                var prefab = Prefab(def, mat);
                paths.Add(AssetDatabase.GetAssetPath(Module(def, prefab)));
            }
            AssetDatabase.SaveAssets();

            // 등급 해금
            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>("Assets/_Project/Data/StationGrades.asset");
            var gso = new SerializedObject(grades);
            for (int i = 0; i < Defs.Length; i++)
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(paths[i]);
                var unlocks = gso.FindProperty("_grades").GetArrayElementAtIndex(Defs[i].Grade).FindPropertyRelative("_unlocks");
                bool has = false;
                for (int k = 0; k < unlocks.arraySize; k++)
                    has |= unlocks.GetArrayElementAtIndex(k).objectReferenceValue == data;
                if (!has)
                {
                    unlocks.arraySize++;
                    unlocks.GetArrayElementAtIndex(unlocks.arraySize - 1).objectReferenceValue = data;
                }
            }
            gso.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            // 씬: 건설 목록 (경로로 다시 읽어 넣음)
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var build = Object.FindFirstObjectByType<BuildController>();
            var bso = new SerializedObject(build);
            var list = bso.FindProperty("_buildableModules");
            foreach (var path in paths)
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(path);
                bool has = false;
                for (int k = 0; k < list.arraySize; k++)
                    has |= list.GetArrayElementAtIndex(k).objectReferenceValue == data;
                if (!has)
                {
                    list.arraySize++;
                    list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
                }
            }
            bso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(build);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[NewModulesSetup] 새 기본 모듈 {Defs.Length}종 · 해금 · 건설 목록 연결 완료");
        }

        private static Material Material(Def def)
        {
            string path = $"{MaterialDir}/M_{def.Id}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            ColorUtility.TryParseHtmlString(def.Color, out var color);
            mat.SetColor("_BaseColor", Color.Lerp(new Color(0.78f, 0.8f, 0.84f), color, 0.6f));
            mat.SetFloat("_Metallic", 0.35f);
            mat.SetFloat("_Smoothness", 0.5f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>칸마다 상자(이웃 칸 쪽은 이어 붙임) + 첫 칸 위 표식 (구조 프레임은 가는 뼈대만).</summary>
        private static GameObject Prefab(Def def, Material mat)
        {
            string path = $"{PrefabDir}/PF_{def.Id}.prefab";
            var root = new GameObject("PF_" + def.Id);
            try
            {
                root.AddComponent<ModuleView>();
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                var model = new GameObject("Model");
                model.transform.SetParent(visual.transform, false);
                var pick = new GameObject(ModulePick.ChildName);
                pick.transform.SetParent(root.transform, false);
                if (ModulePick.Layer >= 0)
                    pick.layer = ModulePick.Layer;
                var set = new HashSet<Vector3Int>(def.Cells);
                bool frame = def.Id == "StructureFrame";
                foreach (var c in def.Cells)
                {
                    var center = (Vector3)c;
                    var box = visual.AddComponent<BoxCollider>();
                    box.center = center;
                    box.size = Vector3.one;
                    var pb = pick.AddComponent<BoxCollider>();
                    pb.center = center;
                    pb.size = Vector3.one * 0.92f;
                    if (frame)
                    {
                        // 트러스: 모서리 기둥 12개
                        foreach (var e in Edges())
                            Part(model, PrimitiveType.Cube, center + e.pos, e.scale, mat);
                        continue;
                    }
                    Vector3 min = center - Vector3.one * 0.42f, max = center + Vector3.one * 0.42f;
                    if (set.Contains(c + Vector3Int.right)) max.x = center.x + 0.5f;
                    if (set.Contains(c + Vector3Int.left)) min.x = center.x - 0.5f;
                    if (set.Contains(c + new Vector3Int(0, 0, 1))) max.z = center.z + 0.5f;
                    if (set.Contains(c + new Vector3Int(0, 0, -1))) min.z = center.z - 0.5f;
                    Part(model, PrimitiveType.Cube, (min + max) * 0.5f, max - min, mat);
                }
                if (!frame)
                    Part(model, def.Marker, (Vector3)def.Cells[0] + new Vector3(0f, 0.5f, 0f), new Vector3(0.3f, 0.12f, 0.3f), mat);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static IEnumerable<(Vector3 pos, Vector3 scale)> Edges()
        {
            const float h = 0.42f, t = 0.07f;
            foreach (float a in new[] { -h, h })
                foreach (float b in new[] { -h, h })
                {
                    yield return (new Vector3(0f, a, b), new Vector3(2f * h, t, t));
                    yield return (new Vector3(a, 0f, b), new Vector3(t, 2f * h, t));
                    yield return (new Vector3(a, b, 0f), new Vector3(t, t, 2f * h));
                }
        }

        private static void Part(GameObject model, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(model.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static ModuleData Module(Def def, GameObject prefab)
        {
            string path = $"{ModuleDir}/MD_{def.Id}.asset";
            var data = AssetDatabase.LoadAssetAtPath<ModuleData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ModuleData>();
                AssetDatabase.CreateAsset(data, path);
            }
            var so = new SerializedObject(data);
            so.FindProperty("_displayName").stringValue = def.Name;
            var cat = so.FindProperty("_category");
            cat.enumValueIndex = System.Array.IndexOf(cat.enumNames, def.Category.ToString());
            var cells = so.FindProperty("_cellOffsets");
            cells.arraySize = def.Cells.Length;
            for (int i = 0; i < def.Cells.Length; i++)
                cells.GetArrayElementAtIndex(i).vector3IntValue = def.Cells[i];
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.FindProperty("_removable").boolValue = true;
            Amounts(so.FindProperty("_buildCost"), (ResourceType.Metal, def.Metal));
            Amounts(so.FindProperty("_production"), def.Production);
            Amounts(so.FindProperty("_consumption"), def.Consumption);
            so.FindProperty("_countsTowardGrade").boolValue = def.CountsTowardGrade;
            so.FindProperty("_comingSoon").boolValue = !string.IsNullOrEmpty(def.ComingSoon);
            so.FindProperty("_comingSoonNote").stringValue = def.ComingSoon ?? "";
            def.Stats?.Invoke(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            return data;
        }

        private static void Amounts(SerializedProperty list, params (ResourceType type, float amount)[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Type").enumValueIndex = (int)values[i].type;
                e.FindPropertyRelative("Amount").floatValue = values[i].amount;
            }
        }
    }
}
