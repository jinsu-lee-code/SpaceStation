using System.Collections.Generic;
using System.IO;
using SpaceStation.Building;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// Phase 6 연구 데이터·연구소 일괄 생성 (메뉴 SpaceStation/Research/Setup). 여러 번 실행해도 결과가 같다.
    /// - 카테고리 6개 RC_* (RESEARCH.md 2·3번 표, 9번 결정 반영) + 단일 레벨 RC_Automation, 레벨 상한 ResearchLevelCaps (4번 표)
    /// - 연구소 모듈 MD_ResearchLab (금속 50, 전력 2, 연구 슬롯 1, 산업 탭, 초소형부터 해금) + 임시 모델 PF_ResearchLab
    /// - Main 씬: SimulationHost 연구 데이터, 건설 목록에 연구소 추가
    /// </summary>
    public static class ResearchSetup
    {
        private const string DataRoot = "Assets/_Project/Data";
        private const string ResearchDir = DataRoot + "/Research";
        private const string LabData = DataRoot + "/Modules/MD_ResearchLab.asset";
        private const string LabPrefab = "Assets/_Project/Prefabs/Modules/PF_ResearchLab.prefab";
        private const string TempDir = "Assets/_Project/Art/Models/Modules/Temp";
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";

        // 레벨 공통: 시작 비용(산소, 물, 금속), 연구 중 전력 수요, 소요 시간
        private static readonly float[,] StartCost = { { 20, 20, 30 }, { 40, 40, 60 }, { 70, 70, 100 }, { 110, 110, 150 } };
        private static readonly float[] PowerDemand = { 5, 8, 12, 18 };
        private static readonly float[] Duration = { 60, 120, 180, 240 };

        [MenuItem("SpaceStation/Research/Setup")]
        public static void Run()
        {
            Directory.CreateDirectory(ResearchDir);
            var categories = BuildCategories();
            var caps = BuildCaps();
            var lab = BuildLab();
            UnlockLab(lab);
            WireScene(categories, caps, lab);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ResearchSetup] 연구 카테고리 {categories.Count}개, 레벨 상한, 연구소 생성·연결 완료");
        }

        // ---------------- 카테고리 ----------------

        private static List<ResearchCategoryData> BuildCategories()
        {
            var list = new List<ResearchCategoryData>
            {
                Category("RC_Maintenance", ResearchCategory.Maintenance, "유지보수", "repair",
                    L("수리 비용 25%, 수리 시간 25초", M(ResearchStat.RepairCostRate, 0.25f), M(ResearchStat.RepairDuration, 25f)),
                    L("수리 비용 20%, 수리 시간 20초", M(ResearchStat.RepairCostRate, 0.2f), M(ResearchStat.RepairDuration, 20f)),
                    L("수리 비용 15%, 수리 시간 15초, 노후화 속도 -20%", M(ResearchStat.RepairCostRate, 0.15f), M(ResearchStat.RepairDuration, 15f), M(ResearchStat.DecayMultiplier, 0.8f)),
                    L("재건축 비용 -50%, 노후화 속도 -40%, 노후 효율 최저 30%", M(ResearchStat.RebuildCostMultiplier, 0.5f), M(ResearchStat.DecayMultiplier, 0.6f), M(ResearchStat.DurabilityEfficiencyFloor, 0.3f))),
                Category("RC_Defense", ResearchCategory.Defense, "방어", "shield",
                    L("실드·포탑 반경 2칸 → 3칸, 운석·태양 폭풍 15초 전 경보", M(ResearchStat.DefenseRadiusBonus, 1f), M(ResearchStat.EarlyWarningSeconds, 15f)),
                    L("운석 명중 시 파손 면역 20%, 경보 중 운석 대상 모듈 표시", M(ResearchStat.HitImmunityChance, 0.2f), M(ResearchStat.MeteorTargetPreview, 1f)),
                    L("실드가 빗겨낸 운석의 튕김 명중 70% → 45%", M(ResearchStat.RicochetChance, 0.45f)),
                    L("포탑 격추 20% → 26%, 격추 상한 60% → 75%, 파손 면역 40%", M(ResearchStat.TurretInterceptMultiplier, 1.3f), M(ResearchStat.TurretMaxIntercept, 0.75f), M(ResearchStat.HitImmunityChance, 0.4f))),
                Category("RC_Production", ResearchCategory.Production, "생산", "production",
                    L("산소·물·식량 생산 +10%", M(ResearchStat.LifeSupportProductionMultiplier, 1.1f)),
                    L("좋은 인접 효과 +10%p (산소 생성기 옆 농장 등)", M(ResearchStat.AdjacencyBonusBoost, 0.1f)),
                    L("산소·물·식량 생산 +20%", M(ResearchStat.LifeSupportProductionMultiplier, 1.2f)),
                    L("채굴 도킹 금속 +1, 산소·물·식량 생산 +30%", M(ResearchStat.MiningBonus, 1f), M(ResearchStat.LifeSupportProductionMultiplier, 1.3f))),
                Category("RC_Energy", ResearchCategory.Energy, "에너지", "power",
                    L("전력 최소 효율 25% → 30%", M(ResearchStat.MinPowerEfficiency, 0.3f)),
                    L("태양광 발전 +15%", M(ResearchStat.SolarMultiplier, 1.15f)),
                    L("전력 최소 효율 40%, 배터리 저장량 +20%", M(ResearchStat.MinPowerEfficiency, 0.4f), M(ResearchStat.BatteryMultiplier, 1.2f)),
                    L("태양광 발전 +30%, 전력 최소 효율 50%", M(ResearchStat.SolarMultiplier, 1.3f), M(ResearchStat.MinPowerEfficiency, 0.5f))),
                Category("RC_Habitation", ResearchCategory.Habitation, "거주환경", "population",
                    L("만족도 상한 +5", M(ResearchStat.SatisfactionCapBonus, 5f)),
                    L("의료실·휴게실 반경 3칸 → 4칸", M(ResearchStat.ServiceRadiusBonus, 1f)),
                    L("거주 모듈 수용 인원 +6 → +7", M(ResearchStat.HousingBonus, 1f)),
                    L("만족도 상한 +10, 수용 인원 +8", M(ResearchStat.SatisfactionCapBonus, 10f), M(ResearchStat.HousingBonus, 2f))),
                Category("RC_Construction", ResearchCategory.Construction, "건설·경제", "construct",
                    L("건설 비용 -5%", M(ResearchStat.BuildCostMultiplier, 0.95f)),
                    L("철거 환급 50% → 60%", M(ResearchStat.DemolishRefundRate, 0.6f)),
                    L("건설 비용 -10%", M(ResearchStat.BuildCostMultiplier, 0.9f)),
                    L("창고 저장 한도 +150 → +200, 철거 환급 70%", M(ResearchStat.StorageBonusAdd, 50f), M(ResearchStat.DemolishRefundRate, 0.7f))),
            };
            // 자동화: 단일 레벨, 소형 등급부터, 비용은 Lv.2 수준 (2026-10-02 결정)
            var automation = Category("RC_Automation", ResearchCategory.Automation, "정비 자동화", "module",
                L("자동 정비·자동 재건축·일괄 정비 개방 (정비 기준 내구도·자원 보호선 조절)", M(ResearchStat.MaintenanceAutomation, 1f)));
            ApplyLevelCost(automation, 1, 1);
            list.Add(automation);
            return list;
        }

        /// <summary>레벨(1부터)의 비용·전력·시간을 다른 레벨 표 값으로 바꾸고 최소 등급 지정.</summary>
        private static void ApplyLevelCost(ResearchCategoryData asset, int costTier, int minGrade)
        {
            var levels = new List<ResearchLevel>(asset.Levels);
            int t = costTier;
            foreach (var level in levels)
            {
                level.StartCost = new List<ResourceAmount>
                {
                    new ResourceAmount(ResourceType.Oxygen, StartCost[t, 0]),
                    new ResourceAmount(ResourceType.Water, StartCost[t, 1]),
                    new ResourceAmount(ResourceType.Metal, StartCost[t, 2]),
                };
                level.PowerDemand = PowerDemand[t];
                level.Duration = Duration[t];
            }
            asset.EditorSet(asset.Category, asset.DisplayName, asset.Icon, levels, minGrade);
            EditorUtility.SetDirty(asset);
        }

        private static ResearchModifier M(ResearchStat stat, float value) => new ResearchModifier { Stat = stat, Value = value };

        private static (string, ResearchModifier[]) L(string description, params ResearchModifier[] modifiers) => (description, modifiers);

        private static ResearchCategoryData Category(string file, ResearchCategory category, string name, string icon, params (string desc, ResearchModifier[] mods)[] levels)
        {
            string path = $"{ResearchDir}/{file}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ResearchCategoryData>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ResearchCategoryData>();
                AssetDatabase.CreateAsset(asset, path);
            }
            var list = new List<ResearchLevel>();
            for (int i = 0; i < levels.Length; i++)
            {
                list.Add(new ResearchLevel
                {
                    StartCost = new List<ResourceAmount>
                    {
                        new ResourceAmount(ResourceType.Oxygen, StartCost[i, 0]),
                        new ResourceAmount(ResourceType.Water, StartCost[i, 1]),
                        new ResourceAmount(ResourceType.Metal, StartCost[i, 2]),
                    },
                    PowerDemand = PowerDemand[i],
                    Duration = Duration[i],
                    Description = levels[i].desc,
                    Modifiers = new List<ResearchModifier>(levels[i].mods),
                });
            }
            asset.EditorSet(category, name, icon, list);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static ResearchLevelCapConfig BuildCaps()
        {
            string path = $"{ResearchDir}/ResearchLevelCaps.asset";
            var caps = AssetDatabase.LoadAssetAtPath<ResearchLevelCapConfig>(path);
            if (caps == null)
            {
                caps = ScriptableObject.CreateInstance<ResearchLevelCapConfig>();
                AssetDatabase.CreateAsset(caps, path);
            }
            caps.EditorSet(new List<ResearchLevelCapConfig.Requirement>
            {
                new ResearchLevelCapConfig.Requirement { Grade = 0, MinPopulation = 4 },
                new ResearchLevelCapConfig.Requirement { Grade = 1, MinPopulation = 15 },
                new ResearchLevelCapConfig.Requirement { Grade = 2, MinPopulation = 40 },
                new ResearchLevelCapConfig.Requirement { Grade = 3, MinPopulation = 65 }, // 가안, 봇 측정 후 확정
            });
            EditorUtility.SetDirty(caps);
            return caps;
        }

        // ---------------- 연구소 ----------------

        private static ModuleData BuildLab()
        {
            var data = AssetDatabase.LoadAssetAtPath<ModuleData>(LabData);
            if (data == null)
            {
                AssetDatabase.CopyAsset(DataRoot + "/Modules/MD_Medical.asset", LabData); // 1칸 모듈을 바탕으로
                data = AssetDatabase.LoadAssetAtPath<ModuleData>(LabData);
            }
            var prefab = BuildTempPrefab();
            var so = new SerializedObject(data);
            so.FindProperty("_displayName").stringValue = "연구소";
            SetEnum(so.FindProperty("_category"), "Industry");
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.FindProperty("_removable").boolValue = true;
            so.FindProperty("_terminalOnly").boolValue = false;
            so.FindProperty("_supportsTop").boolValue = true;
            SetAmounts(so.FindProperty("_buildCost"), (ResourceType.Metal, 50f));
            SetAmounts(so.FindProperty("_production"));
            SetAmounts(so.FindProperty("_consumption"), (ResourceType.Power, 2f));
            so.FindProperty("_housingCapacity").intValue = 0;
            so.FindProperty("_storageBonus").floatValue = 0f;
            so.FindProperty("_repairSlots").intValue = 0;
            so.FindProperty("_researchSlots").intValue = 1;
            SetEnum(so.FindProperty("_serviceNeed"), "None");
            so.FindProperty("_serviceRadius").intValue = 0;
            so.FindProperty("_serviceCapacity").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            ConnectorDepthBaker.Measure(data); // 통로 길이용 면 깊이
            return data;
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

        /// <summary>임시 모델: 기단 + 모서리 기둥 + 빛나는 띠 + 위쪽 관측 돔 + 안테나 (나중에 사용자 모델로 교체).</summary>
        private static GameObject BuildTempPrefab()
        {
            Directory.CreateDirectory(TempDir);
            var body = MakeMaterial("M_ResearchLab_Body", new Color(0.78f, 0.8f, 0.84f), Color.black, 0.55f);
            var glow = MakeMaterial("M_ResearchLab_Glow", new Color(0.62f, 0.5f, 1f), new Color(0.55f, 0.42f, 1f) * 2.2f, 0.8f);

            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var cyl = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            var sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            var bodyParts = new List<CombineInstance>
            {
                Part(cube, new Vector3(0f, -0.2f, 0f), new Vector3(0.86f, 0.52f, 0.86f)),
                Part(cyl, new Vector3(0f, 0.08f, 0f), new Vector3(0.72f, 0.03f, 0.72f)),
            };
            foreach (var x in new[] { -0.39f, 0.39f })
                foreach (var z in new[] { -0.39f, 0.39f })
                    bodyParts.Add(Part(cube, new Vector3(x, -0.17f, z), new Vector3(0.1f, 0.6f, 0.1f)));
            var glowParts = new List<CombineInstance>
            {
                Part(cube, new Vector3(0f, -0.08f, 0f), new Vector3(0.88f, 0.07f, 0.88f)),
                Part(sphere, new Vector3(0f, 0.12f, 0f), new Vector3(0.52f, 0.42f, 0.52f)),
                Part(cyl, new Vector3(0.18f, 0.36f, 0.18f), new Vector3(0.035f, 0.11f, 0.035f)),
                Part(sphere, new Vector3(0.18f, 0.47f, 0.18f), new Vector3(0.07f, 0.07f, 0.07f)),
            };

            var mesh = new Mesh { name = "SM_ResearchLab_Temp" };
            var bodyMesh = new Mesh();
            bodyMesh.CombineMeshes(bodyParts.ToArray(), true, true);
            var glowMesh = new Mesh();
            glowMesh.CombineMeshes(glowParts.ToArray(), true, true);
            mesh.CombineMeshes(new[] { new CombineInstance { mesh = bodyMesh, transform = Matrix4x4.identity }, new CombineInstance { mesh = glowMesh, transform = Matrix4x4.identity } }, false, false);
            mesh.RecalculateBounds();
            string meshPath = $"{TempDir}/SM_ResearchLab_Temp.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                mesh = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, meshPath);
            }

            var root = new GameObject("PF_ResearchLab");
            try
            {
                root.AddComponent<ModuleView>();
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<BoxCollider>().size = Vector3.one;
                var model = new GameObject("Model");
                model.transform.SetParent(visual.transform, false);
                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                model.AddComponent<MeshRenderer>().sharedMaterials = new[] { body, glow };
                return PrefabUtility.SaveAsPrefabAsset(root, LabPrefab);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static CombineInstance Part(Mesh mesh, Vector3 position, Vector3 scale)
            => new CombineInstance { mesh = mesh, transform = Matrix4x4.TRS(position, Quaternion.identity, scale) };

        private static Material MakeMaterial(string name, Color color, Color emission, float smoothness)
        {
            string path = $"{TempDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic", 0.4f);
            mat.SetFloat("_Smoothness", smoothness);
            if (emission.maxColorComponent > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void UnlockLab(ModuleData lab)
        {
            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>($"{DataRoot}/StationGrades.asset");
            var so = new SerializedObject(grades);
            var unlocks = so.FindProperty("_grades").GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks");
            for (int i = 0; i < unlocks.arraySize; i++)
                if (unlocks.GetArrayElementAtIndex(i).objectReferenceValue == lab)
                    return;
            unlocks.arraySize++;
            unlocks.GetArrayElementAtIndex(unlocks.arraySize - 1).objectReferenceValue = lab;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(grades);
        }

        // ---------------- 씬 ----------------

        private static void WireScene(List<ResearchCategoryData> categories, ResearchLevelCapConfig caps, ModuleData lab)
        {
            AssetDatabase.SaveAssets();
            // 씬을 열면 메모리의 에셋 참조가 풀릴 수 있어 경로로 다시 불러온다
            var paths = new List<string>();
            foreach (var c in categories)
                paths.Add(AssetDatabase.GetAssetPath(c));
            string capsPath = AssetDatabase.GetAssetPath(caps);
            string labPath = AssetDatabase.GetAssetPath(lab);
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            categories = new List<ResearchCategoryData>();
            foreach (var p in paths)
                categories.Add(AssetDatabase.LoadAssetAtPath<ResearchCategoryData>(p));
            caps = AssetDatabase.LoadAssetAtPath<ResearchLevelCapConfig>(capsPath);
            lab = AssetDatabase.LoadAssetAtPath<ModuleData>(labPath);
            var host = Object.FindFirstObjectByType<SimulationHost>();
            var hso = new SerializedObject(host);
            var list = hso.FindProperty("_researchCategories");
            list.arraySize = categories.Count;
            for (int i = 0; i < categories.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = categories[i];
            hso.FindProperty("_researchCaps").objectReferenceValue = caps;
            hso.ApplyModifiedPropertiesWithoutUndo();

            var build = Object.FindFirstObjectByType<BuildController>();
            var bso = new SerializedObject(build);
            var buildable = bso.FindProperty("_buildableModules");
            for (int i = buildable.arraySize - 1; i >= 0; i--)
            {
                if (buildable.GetArrayElementAtIndex(i).objectReferenceValue == null)
                    buildable.DeleteArrayElementAtIndex(i); // 이전 실행에서 남은 빈 칸
            }
            bool found = false;
            for (int i = 0; i < buildable.arraySize; i++)
                found |= buildable.GetArrayElementAtIndex(i).objectReferenceValue == lab;
            if (!found)
            {
                buildable.arraySize++;
                buildable.GetArrayElementAtIndex(buildable.arraySize - 1).objectReferenceValue = lab;
            }
            bso.ApplyModifiedPropertiesWithoutUndo();
            BuildResearchPanel(Object.FindFirstObjectByType<StationController>());
            BuildEarlyWarning(Object.FindFirstObjectByType<StationController>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>HUD에 조기 경보 배너·대상 표시 자리 (방어 연구). 연구 창보다 아래 순서(창이 열리면 가림).</summary>
        private static void BuildEarlyWarning(StationController station)
        {
            var hud = GameObject.Find("HUD");
            if (hud == null)
                return;
            var old = hud.transform.Find("EarlyWarning");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("EarlyWarning", typeof(RectTransform));
            go.transform.SetParent(hud.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var markers = hud.transform.Find("DamageMarkers"); // 월드 표시 바로 위, 다른 HUD·결과 화면 아래
            go.transform.SetSiblingIndex(markers != null ? markers.GetSiblingIndex() + 1 : 0);
            var view = go.AddComponent<SpaceStation.UI.EarlyWarningView>();
            var so = new SerializedObject(view);
            so.FindProperty("_station").objectReferenceValue = station;
            so.FindProperty("_font").objectReferenceValue = hud.GetComponentInChildren<TMPro.TMP_Text>(true).font;
            so.FindProperty("_fillSprite").objectReferenceValue = HudArtBuilder.Fill;
            so.FindProperty("_frameSprite").objectReferenceValue = HudArtBuilder.Frame;
            so.FindProperty("_camera").objectReferenceValue = Camera.main;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>HUD에 연구 창 자리 (내용은 런타임 생성). 일시정지·설정창보다 아래 순서.</summary>
        private static void BuildResearchPanel(StationController station)
        {
            var hud = GameObject.Find("HUD");
            if (hud == null)
                return;
            var old = hud.transform.Find("ResearchPanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("ResearchPanel", typeof(RectTransform));
            go.transform.SetParent(hud.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var result = hud.transform.Find("ResultScreen"); // 결과 화면이 연구 창·추적기를 가리도록 그 아래
            var pause = hud.transform.Find("PauseMenu");
            if (result != null)
                go.transform.SetSiblingIndex(result.GetSiblingIndex());
            else if (pause != null)
                go.transform.SetSiblingIndex(pause.GetSiblingIndex());
            var panel = go.AddComponent<SpaceStation.UI.ResearchPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("_station").objectReferenceValue = station;
            so.FindProperty("_font").objectReferenceValue = hud.GetComponentInChildren<TMPro.TMP_Text>(true).font;
            so.FindProperty("_fillSprite").objectReferenceValue = HudArtBuilder.Fill;
            so.FindProperty("_frameSprite").objectReferenceValue = HudArtBuilder.Frame;
            so.FindProperty("_buttonSprite").objectReferenceValue = HudArtBuilder.Button;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
