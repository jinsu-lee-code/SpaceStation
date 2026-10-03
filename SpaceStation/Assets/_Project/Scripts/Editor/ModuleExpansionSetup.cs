using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// Phase 8 신규 모듈 일괄 생성 (메뉴 SpaceStation/Modules/Phase 8 Setup). 여러 번 실행해도 결과가 같다.
    /// 모듈마다: MD_{키} 데이터(수치는 아래 표, BALANCE.md 23번) · 강조 재질 M_Accent_{키} · 기본 프리팹 PF_{키}
    /// (Art/Models/Modules/SM_{키}.fbx가 있으면 StationArtBuilder가 모델·재질·통로 깊이 조립) · 등급 해금 · Main 씬 건설 목록 · 건설 메뉴 썸네일.
    /// </summary>
    public static class ModuleExpansionSetup
    {
        private const string DataRoot = "Assets/_Project/Data";
        private const string ModuleDir = DataRoot + "/Modules";
        private const string PrefabDir = "Assets/_Project/Prefabs/Modules";
        private const string MatRoot = "Assets/_Project/Art/Materials";
        private const string ModelRoot = "Assets/_Project/Art/Models/Modules";
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";

        private sealed class Def
        {
            public string Key;
            public string Name;
            public ModuleCategory Category;
            public Vector3Int[] Cells;
            public float Cost;
            public (ResourceType, float)[] Production = new (ResourceType, float)[0];
            public (ResourceType, float)[] Consumption = new (ResourceType, float)[0];
            public int UnlockGrade; // 0 초소형 / 1 소형 / 2 중형 / 3 대형
            public string AccentHex;
            public float SpreadTimeMultiplier = 1f;
            public Vector3Int[] CellsOrDefault => Cells ?? OneCell;
            public int ControlRadius;
            public float ControlSpread = 1f;
            public float ControlDestroy = 1f;
            public int Housing;
            public ResidentNeed ServiceNeed = ResidentNeed.None;
            public int ServiceRadius;
            public int ServiceCapacity;
            public float GrowthInterval = 1f;
            public bool OnDemandPower;
            public float InputReserve;
            public bool Terminal; // 도킹 규칙 (뒷면 연결 + 앞 접근로 2칸)
            public float CargoInterval;
            public float CargoFraction;
        }

        private static Vector3Int[] Square3()
        {
            var cells = new List<Vector3Int> { Vector3Int.zero }; // 원점 = 가운데 칸 (회전 중심)
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    if (x != 0 || z != 0)
                        cells.Add(new Vector3Int(x, 0, z));
            return cells.ToArray();
        }

        private static readonly Vector3Int[] OneCell = { Vector3Int.zero };

        private static readonly Vector3Int[] TwoCells = { Vector3Int.zero, Vector3Int.right };

        private static readonly Def[] Modules =
        {
            // 8-1 핵융합로: 낮/밤 무관 대량 전력, 비쌈, 냉각수, 파손 확산 2배 빠름 (거주 인접 페널티는 7-6)
            new Def
            {
                Key = "FusionReactor", Name = "핵융합로", Category = ModuleCategory.Power, Cells = TwoCells,
                Cost = 250f, Production = new[] { (ResourceType.Power, 40f) }, Consumption = new[] { (ResourceType.Water, 0.5f) },
                UnlockGrade = 2, AccentHex = "#FFB040", SpreadTimeMultiplier = 0.5f,
            },
            // 8-2 제련소: 자체 생산 없음, 맞닿은 채굴 도킹 증폭 (아래 인접 규칙)
            new Def
            {
                Key = "Refinery", Name = "제련소", Category = ModuleCategory.Industry, Cells = TwoCells,
                Cost = 150f, Consumption = new[] { (ResourceType.Power, 5f), (ResourceType.Water, 0.3f) },
                UnlockGrade = 2, AccentHex = "#FF6A2E",
            },
            // 8-3 손상 통제실: 범위 2칸 안 파손 모듈 확산 2배 늦게, 방치 파괴 1.5배 오래 (중첩 없음)
            new Def
            {
                Key = "DamageControl", Name = "손상 통제실", Category = ModuleCategory.Defense,
                Cost = 120f, Consumption = new[] { (ResourceType.Power, 5f) },
                UnlockGrade = 2, AccentHex = "#FFC83A",
                ControlRadius = 2, ControlSpread = 2f, ControlDestroy = 1.5f,
            },
            // 8-4 회전 링: 3×1×3 (가운데 칸 포함), 대량 수용 + 여가 요구 담당(범위 만족도 상한) + 인구 증가 간격 ×0.8 (중첩 없음)
            new Def
            {
                Key = "RotatingRing", Name = "회전 링", Category = ModuleCategory.Life, Cells = Square3(),
                Cost = 300f, Consumption = new[] { (ResourceType.Power, 10f) },
                UnlockGrade = 3, AccentHex = "#7FD0FF",
                Housing = 30, ServiceNeed = ResidentNeed.Recreation, ServiceRadius = 2, ServiceCapacity = 20, GrowthInterval = 0.8f,
            },
            // 8-5 연료전지: 부족할 때만 최대 +8 발전, 가동 비율만큼 물 소비(최대 1.0/s), 물 20% 이하면 정지
            new Def
            {
                Key = "FuelCell", Name = "연료전지", Category = ModuleCategory.Power,
                Cost = 50f, Production = new[] { (ResourceType.Power, 8f) }, Consumption = new[] { (ResourceType.Water, 1f) },
                UnlockGrade = 1, AccentHex = "#3FE0D0", OnDemandPower = true, InputReserve = 0.2f,
            },
            // 8-5 화물 터미널: 도킹 규칙, 터미널마다 180초마다 화물선 → 가장 부족한 저장 자원을 한도의 15%
            new Def
            {
                Key = "CargoTerminal", Name = "화물 터미널", Category = ModuleCategory.Industry, Cells = TwoCells,
                Cost = 120f, Consumption = new[] { (ResourceType.Power, 4f) },
                UnlockGrade = 2, AccentHex = "#7CD957", Terminal = true, CargoInterval = 180f, CargoFraction = 0.15f,
            },
        };

        /// <summary>신규 모듈이 쓰는 인접 규칙 (AdjacencyRules.asset, 대상+이웃+효과가 같으면 갱신).</summary>
        private sealed class AdjDef
        {
            public string Target;
            public string Neighbor;
            public AdjacencyEffect Effect;
            public float Value;
            public int MaxStacks;
            public string Label;
        }

        private static readonly AdjDef[] Rules =
        {
            // 8-2 채굴 도킹 옆 제련소: 금속 생산 +50% (제련소 여러 개여도 1번만)
            new AdjDef { Target = "MiningDock", Neighbor = "Refinery", Effect = AdjacencyEffect.Production, Value = 0.5f, MaxStacks = 1, Label = "제련 가공" },
        };

        [MenuItem("SpaceStation/Modules/Phase 8 Setup")]
        public static void Run()
        {
            var built = new List<string>();
            foreach (var def in Modules)
            {
                var data = BuildData(def);
                Unlock(data, def.UnlockGrade);
                built.Add(data.name);
            }
            foreach (var rule in Rules)
                EnsureRule(rule);
            AssetDatabase.SaveAssets();
            WireScene();
            HudArtBuilder.BuildThumbnails(); // 건설 메뉴 썸네일 (프리팹 렌더)
            AssetDatabase.SaveAssets();
            Debug.Log("[ModuleExpansionSetup] " + string.Join(", ", built));
        }

        private static ModuleData BuildData(Def def)
        {
            string path = $"{ModuleDir}/MD_{def.Key}.asset";
            var data = AssetDatabase.LoadAssetAtPath<ModuleData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ModuleData>();
                AssetDatabase.CreateAsset(data, path);
            }
            EnsureAccent(def);
            bool hasModel = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelRoot}/SM_{def.Key}.fbx") != null;
            var prefab = EnsureBasePrefab(def.Key);

            var so = new SerializedObject(data);
            so.FindProperty("_displayName").stringValue = def.Name;
            SetEnum(so.FindProperty("_category"), def.Category.ToString());
            var cells = so.FindProperty("_cellOffsets");
            var offsets = def.CellsOrDefault;
            cells.arraySize = offsets.Length;
            for (int i = 0; i < offsets.Length; i++)
                cells.GetArrayElementAtIndex(i).vector3IntValue = offsets[i];
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.FindProperty("_removable").boolValue = true;
            so.FindProperty("_terminalOnly").boolValue = def.Terminal;
            so.FindProperty("_dockFront").vector3IntValue = new Vector3Int(0, 0, 1);
            so.FindProperty("_approachLaneLength").intValue = 2;
            so.FindProperty("_cargoInterval").floatValue = def.CargoInterval;
            so.FindProperty("_cargoFraction").floatValue = def.CargoFraction;
            so.FindProperty("_upperSidesNeedSupport").boolValue = false;
            SetAmounts(so.FindProperty("_buildCost"), (ResourceType.Metal, def.Cost));
            SetAmounts(so.FindProperty("_production"), def.Production);
            SetAmounts(so.FindProperty("_consumption"), def.Consumption);
            so.FindProperty("_housingCapacity").intValue = def.Housing;
            so.FindProperty("_growthIntervalMultiplier").floatValue = def.GrowthInterval;
            so.FindProperty("_storageBonus").floatValue = 0f;
            so.FindProperty("_solarPowered").boolValue = false;
            so.FindProperty("_batteryCapacity").floatValue = 0f;
            so.FindProperty("_batteryRate").floatValue = 0f;
            so.FindProperty("_onDemandPower").boolValue = def.OnDemandPower;
            so.FindProperty("_inputReserveRatio").floatValue = def.InputReserve;
            so.FindProperty("_repairSlots").intValue = 0;
            so.FindProperty("_researchSlots").intValue = 0;
            so.FindProperty("_spreadTimeMultiplier").floatValue = def.SpreadTimeMultiplier;
            so.FindProperty("_shieldRadius").intValue = 0;
            so.FindProperty("_shieldReduction").floatValue = 0f;
            so.FindProperty("_turretRadius").intValue = 0;
            so.FindProperty("_turretInterceptChance").floatValue = 0f;
            so.FindProperty("_controlRadius").intValue = def.ControlRadius;
            so.FindProperty("_controlSpreadMultiplier").floatValue = def.ControlSpread;
            so.FindProperty("_controlDestroyMultiplier").floatValue = def.ControlDestroy;
            SetEnum(so.FindProperty("_serviceNeed"), def.ServiceNeed.ToString());
            so.FindProperty("_serviceRadius").intValue = def.ServiceRadius;
            so.FindProperty("_serviceCapacity").intValue = def.ServiceCapacity;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            if (hasModel)
                StationArtBuilder.RebuildModule(def.Key); // 모델·재질·통로 깊이
            else
                Debug.LogWarning($"[ModuleExpansionSetup] {def.Key}: SM_{def.Key}.fbx 없음 — 빈 프리팹 (모델 제작 후 다시 실행)");
            return data;
        }

        private static void EnsureRule(AdjDef def)
        {
            var set = AssetDatabase.LoadAssetAtPath<AdjacencyRuleSet>($"{DataRoot}/AdjacencyRules.asset");
            var target = AssetDatabase.LoadAssetAtPath<ModuleData>($"{ModuleDir}/MD_{def.Target}.asset");
            var neighbor = AssetDatabase.LoadAssetAtPath<ModuleData>($"{ModuleDir}/MD_{def.Neighbor}.asset");
            var so = new SerializedObject(set);
            var list = so.FindProperty("_rules");
            SerializedProperty rule = null;
            for (int i = 0; i < list.arraySize && rule == null; i++)
            {
                var r = list.GetArrayElementAtIndex(i);
                if (r.FindPropertyRelative("_target").objectReferenceValue == target
                    && r.FindPropertyRelative("_neighbor").objectReferenceValue == neighbor
                    && r.FindPropertyRelative("_effect").enumValueIndex == (int)def.Effect)
                    rule = r;
            }
            if (rule == null)
            {
                list.arraySize++;
                rule = list.GetArrayElementAtIndex(list.arraySize - 1);
            }
            rule.FindPropertyRelative("_target").objectReferenceValue = target;
            rule.FindPropertyRelative("_neighbor").objectReferenceValue = neighbor;
            rule.FindPropertyRelative("_excludeSameType").boolValue = false;
            rule.FindPropertyRelative("_effect").enumValueIndex = (int)def.Effect;
            rule.FindPropertyRelative("_valuePerNeighbor").floatValue = def.Value;
            rule.FindPropertyRelative("_freeNeighbors").intValue = 0;
            rule.FindPropertyRelative("_maxStacks").intValue = def.MaxStacks;
            rule.FindPropertyRelative("_label").stringValue = def.Label;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(set);
        }

        /// <summary>강조 재질: 다른 모듈의 강조 재질을 복사해 색만 바꾼다.</summary>
        private static void EnsureAccent(Def def)
        {
            string path = $"{MatRoot}/M_Accent_{def.Key}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) == null)
                AssetDatabase.CopyAsset($"{MatRoot}/M_Accent_Battery.mat", path);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            ColorUtility.TryParseHtmlString(def.AccentHex, out var color);
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
        }

        /// <summary>모델용 빈 프리팹: ModuleView + Visual(셀 판정 콜라이더, 크기는 StationArtBuilder가 맞춤).</summary>
        private static GameObject EnsureBasePrefab(string key)
        {
            string path = $"{PrefabDir}/PF_{key}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;
            var root = new GameObject($"PF_{key}");
            try
            {
                root.AddComponent<ModuleView>();
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<BoxCollider>().size = Vector3.one;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Unlock(ModuleData data, int grade)
        {
            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>($"{DataRoot}/StationGrades.asset");
            var so = new SerializedObject(grades);
            var list = so.FindProperty("_grades");
            // 다른 등급에 있으면 빼고 지정 등급에만
            for (int g = 0; g < list.arraySize; g++)
            {
                var unlocks = list.GetArrayElementAtIndex(g).FindPropertyRelative("_unlocks");
                for (int i = unlocks.arraySize - 1; i >= 0; i--)
                    if (unlocks.GetArrayElementAtIndex(i).objectReferenceValue == data && g != grade)
                        unlocks.DeleteArrayElementAtIndex(i);
            }
            var target = list.GetArrayElementAtIndex(grade).FindPropertyRelative("_unlocks");
            bool found = false;
            for (int i = 0; i < target.arraySize; i++)
                found |= target.GetArrayElementAtIndex(i).objectReferenceValue == data;
            if (!found)
            {
                target.arraySize++;
                target.GetArrayElementAtIndex(target.arraySize - 1).objectReferenceValue = data;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(grades);
        }

        /// <summary>Main 씬 건설 목록에 추가 (같은 분류 모듈 바로 뒤 = 탭 안 숫자키 순서).</summary>
        private static void WireScene()
        {
            var paths = new List<string>();
            foreach (var def in Modules)
                paths.Add($"{ModuleDir}/MD_{def.Key}.asset");
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var build = Object.FindFirstObjectByType<BuildController>();
            var bso = new SerializedObject(build);
            var buildable = bso.FindProperty("_buildableModules");
            for (int i = buildable.arraySize - 1; i >= 0; i--)
                if (buildable.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    buildable.DeleteArrayElementAtIndex(i);
            foreach (var p in paths)
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(p);
                bool found = false;
                int lastSameCategory = -1;
                for (int i = 0; i < buildable.arraySize; i++)
                {
                    var m = buildable.GetArrayElementAtIndex(i).objectReferenceValue as ModuleData;
                    found |= m == data;
                    if (m != null && m != data && m.Category == data.Category)
                        lastSameCategory = i;
                }
                if (found)
                    continue;
                int at = lastSameCategory >= 0 ? lastSameCategory + 1 : buildable.arraySize;
                buildable.InsertArrayElementAtIndex(at);
                buildable.GetArrayElementAtIndex(at).objectReferenceValue = data;
            }
            bso.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void SetEnum(SerializedProperty p, string name)
        {
            int i = System.Array.IndexOf(p.enumNames, name);
            if (i >= 0)
                p.enumValueIndex = i;
        }

        private static void SetAmounts(SerializedProperty list, params (ResourceType type, float amount)[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                SetEnum(e.FindPropertyRelative("Type"), values[i].type.ToString());
                e.FindPropertyRelative("Amount").floatValue = values[i].amount;
            }
        }
    }
}
