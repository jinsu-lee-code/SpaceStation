using SpaceStation.Building;
using SpaceStation.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 12-3c 등급별 핵심 규칙 그레이박스 설정 · 확인 메뉴 (SpaceStation/Grade Rules/...).
    /// </summary>
    public static class GradeRulesSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";

        [MenuItem("SpaceStation/Grade Rules/Setup Meteor Shower View")]
        public static void SetupMeteorShower()
        {
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var station = Object.FindFirstObjectByType<StationController>();
            var view = Object.FindFirstObjectByType<MeteorShowerZoneView>();
            if (view == null)
            {
                view = new GameObject("MeteorShowerZone").AddComponent<MeteorShowerZoneView>();
                Undo.RegisterCreatedObjectUndo(view.gameObject, "MeteorShowerZone");
            }
            var so = new SerializedObject(view);
            so.FindProperty("_station").objectReferenceValue = station;
            so.FindProperty("_material").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_DefenseRange.mat");
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GradeRulesSetup] 유성우 표시 연결 완료");
        }

        [MenuItem("SpaceStation/Grade Rules/Meteor Shower Now (60s)")]
        public static void ShowerNow() => Trigger(60f);

        [MenuItem("SpaceStation/Grade Rules/Meteor Shower Now (10s)")]
        public static void ShowerSoon() => Trigger(10f);

        [MenuItem("SpaceStation/Grade Rules/Meteor Shower Now (60s)", true)]
        [MenuItem("SpaceStation/Grade Rules/Meteor Shower Now (10s)", true)]
        public static bool CanTrigger() => Application.isPlaying;

        // ---------------- ② 국가급 상시 정책 + 사회 위기 ----------------

        private const string PolicyDir = "Assets/_Project/Data/Policies";

        [MenuItem("SpaceStation/Grade Rules/Setup Policies")]
        public static void SetupPolicies()
        {
            if (!AssetDatabase.IsValidFolder(PolicyDir))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Policies");
            PolicyModifier M(PolicyStat s, float v) => new PolicyModifier { Stat = s, Value = v };
            var policies = new System.Collections.Generic.List<PolicyData>
            {
                Policy("PO_Rationing", "배급제", "식량 · 물을 정량으로 나눠 줍니다",
                    M(PolicyStat.FoodWaterConsumption, 0.8f), M(PolicyStat.SatisfactionCap, -10f)),
                Policy("PO_NightShift", "야간 근무", "설비를 쉬지 않고 돌립니다",
                    M(PolicyStat.Production, 1.15f), M(PolicyStat.Decay, 1.3f), M(PolicyStat.SatisfactionCap, -5f)),
                Policy("PO_ResearchFirst", "연구 우선", "전력을 연구소에 먼저 돌립니다",
                    M(PolicyStat.ResearchSpeed, 1.25f), M(PolicyStat.PowerDemand, 1.1f)),
                Policy("PO_MandatoryUpkeep", "정비 의무", "채굴 인력 일부를 정비에 돌립니다",
                    M(PolicyStat.Decay, 0.7f), M(PolicyStat.MetalProduction, 0.85f)),
                Policy("PO_OpenImmigration", "이민 개방", "이주 심사를 줄입니다",
                    M(PolicyStat.GrowthInterval, 0.7f), M(PolicyStat.ResidentConsumption, 1.1f)),
                Policy("PO_FestivalCulture", "축제 문화", "정기 축제로 여가 시간을 늘립니다",
                    M(PolicyStat.SatisfactionCap, 10f), M(PolicyStat.Production, 0.9f)),
                Policy("PO_Surveillance", "감시 강화", "불만을 일찍 알아채지만 주민은 답답해합니다",
                    M(PolicyStat.CrisisWarning, 1.5f), M(PolicyStat.CrisisThreshold, -10f), M(PolicyStat.SatisfactionCap, -8f)),
                Policy("PO_EmergencyDrill", "비상 대비", "상시 비상 훈련을 합니다",
                    M(PolicyStat.RepairSpeed, 1.25f), M(PolicyStat.DestroyTimer, 1.3f), M(PolicyStat.Production, 0.9f)),
            };
            string configPath = $"{PolicyDir}/PolicyConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<PolicyConfig>(configPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<PolicyConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }
            config.SetPolicies(policies);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var host = Object.FindFirstObjectByType<SpaceStation.Simulation.SimulationHost>();
            var hso = new SerializedObject(host);
            hso.FindProperty("_policies").objectReferenceValue = AssetDatabase.LoadAssetAtPath<PolicyConfig>(configPath);
            hso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(host);

            var station = Object.FindFirstObjectByType<StationController>();
            var research = Object.FindFirstObjectByType<SpaceStation.UI.ResearchPanel>(FindObjectsInactive.Include);
            var recipe = Object.FindFirstObjectByType<SpaceStation.UI.RecipePanel>(FindObjectsInactive.Include);
            var anchor = recipe != null ? recipe.transform : research.transform;
            var old = anchor.parent.Find("PolicyPanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("PolicyPanel", typeof(RectTransform));
            go.transform.SetParent(anchor.parent, false);
            go.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var panel = go.AddComponent<SpaceStation.UI.PolicyPanel>();
            var rso = new SerializedObject(research);
            var so = new SerializedObject(panel);
            so.FindProperty("_station").objectReferenceValue = station;
            foreach (var f in new[] { "_font", "_fillSprite", "_frameSprite", "_buttonSprite", "_holoArt" })
                so.FindProperty(f).objectReferenceValue = rso.FindProperty(f).objectReferenceValue;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GradeRulesSetup] 정책 8개 · 설정 · 정책 창 연결 완료");
        }

        private static PolicyData Policy(string file, string name, string description, params PolicyModifier[] modifiers)
        {
            string path = $"{PolicyDir}/{file}.asset";
            var p = AssetDatabase.LoadAssetAtPath<PolicyData>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<PolicyData>();
                AssetDatabase.CreateAsset(p, path);
            }
            p.Set(name, description, modifiers);
            EditorUtility.SetDirty(p);
            return p;
        }

        // ---------------- ③ 대륙급 열 관리 ----------------

        private const string ModuleDir = "Assets/_Project/Data/Modules";

        [MenuItem("SpaceStation/Grade Rules/Setup Heat")]
        public static void SetupHeat()
        {
            // 발열 모듈 (BALANCE 36번). 사용자 결정 2026-10-13: 처음 가안(4 · 3 · 2, 한도 6)은 대륙급 시작 상태에서 과열 341 · 위험 233 → 절반 + 한도 8
            SetHeat("MD_FusionReactor", 2f);
            SetHeat("MD_Refinery", 1.5f);
            SetHeat("MD_FuelCell", 1f);

            // 그레이박스 방열판 (12-5에서 정식 모델 · 수치)
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Radiator.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, "Assets/_Project/Art/Materials/M_Radiator.mat");
            }
            mat.SetColor("_BaseColor", new Color(0.72f, 0.82f, 0.9f));
            mat.SetFloat("_Metallic", 0.6f);
            mat.SetFloat("_Smoothness", 0.6f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            var prefab = RadiatorPrefab(mat);

            string path = $"{ModuleDir}/MD_Radiator.asset";
            var data = AssetDatabase.LoadAssetAtPath<ModuleData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ModuleData>();
                AssetDatabase.CreateAsset(data, path);
            }
            var so = new SerializedObject(data);
            so.FindProperty("_displayName").stringValue = "방열판";
            var cat = so.FindProperty("_category");
            cat.enumValueIndex = System.Array.IndexOf(cat.enumNames, "Power");
            var cells = so.FindProperty("_cellOffsets");
            cells.arraySize = 1;
            cells.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.FindProperty("_removable").boolValue = true;
            var cost = so.FindProperty("_buildCost");
            cost.arraySize = 1;
            cost.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Metal;
            cost.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 60f;
            so.FindProperty("_heatRemoval").floatValue = 4f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            // 대륙급 해금
            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>("Assets/_Project/Data/StationGrades.asset");
            var gso = new SerializedObject(grades);
            var unlocks = gso.FindProperty("_grades").GetArrayElementAtIndex(7).FindPropertyRelative("_unlocks");
            bool has = false;
            for (int i = 0; i < unlocks.arraySize; i++)
                has |= unlocks.GetArrayElementAtIndex(i).objectReferenceValue == data;
            if (!has)
            {
                unlocks.arraySize++;
                unlocks.GetArrayElementAtIndex(unlocks.arraySize - 1).objectReferenceValue = data;
            }
            gso.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            // 씬: 건설 목록 + 열 지도
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var build = Object.FindFirstObjectByType<BuildController>();
            var bso = new SerializedObject(build);
            var list = bso.FindProperty("_buildableModules");
            has = false;
            for (int i = 0; i < list.arraySize; i++)
                has |= list.GetArrayElementAtIndex(i).objectReferenceValue == data;
            if (!has)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
            }
            bso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(build);

            var view = Object.FindFirstObjectByType<HeatMapView>();
            if (view == null)
                view = new GameObject("HeatMap").AddComponent<HeatMapView>();
            var research = Object.FindFirstObjectByType<SpaceStation.UI.ResearchPanel>(FindObjectsInactive.Include);
            var vso = new SerializedObject(view);
            vso.FindProperty("_station").objectReferenceValue = Object.FindFirstObjectByType<StationController>();
            vso.FindProperty("_material").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_DefenseRange.mat");
            var rso = new SerializedObject(research);
            vso.FindProperty("_camera").objectReferenceValue = Camera.main;
            // 라벨 = 창과 같은 스타일 (사용자 피드백 2026-10-14) — 열 표기 · 파손 표기 둘 다
            foreach (var f in new[] { "_font", "_fillSprite", "_frameSprite", "_holoArt" })
                vso.FindProperty(f).objectReferenceValue = rso.FindProperty(f).objectReferenceValue;
            vso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
            var markers = Object.FindFirstObjectByType<SpaceStation.UI.DamageMarkers>(FindObjectsInactive.Include);
            if (markers != null)
            {
                var mso = new SerializedObject(markers);
                foreach (var f in new[] { "_font", "_fillSprite", "_frameSprite", "_holoArt" })
                    mso.FindProperty(f).objectReferenceValue = rso.FindProperty(f).objectReferenceValue;
                mso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(markers);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[GradeRulesSetup] 열 관리: 발열 3종 · 방열판 · 대륙급 해금 · 열 지도 연결 완료");
        }

        private static void SetHeat(string module, float heat)
        {
            var m = AssetDatabase.LoadAssetAtPath<ModuleData>($"{ModuleDir}/{module}.asset");
            if (m == null)
            {
                Debug.LogWarning($"[GradeRulesSetup] {module} 없음");
                return;
            }
            var so = new SerializedObject(m);
            so.FindProperty("_heatOutput").floatValue = heat;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(m);
        }

        /// <summary>그레이박스 방열판: 칸 상자 + 바깥으로 뻗은 얇은 날개 4장.</summary>
        private static GameObject RadiatorPrefab(Material mat)
        {
            const string path = "Assets/_Project/Prefabs/Modules/PF_Radiator.prefab";
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs/Modules"))
                AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "Modules");
            var root = new GameObject("PF_Radiator");
            try
            {
                root.AddComponent<ModuleView>();
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                var model = new GameObject("Model");
                model.transform.SetParent(visual.transform, false);
                var box = visual.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                var pick = new GameObject(ModulePick.ChildName);
                pick.transform.SetParent(root.transform, false);
                if (ModulePick.Layer >= 0)
                    pick.layer = ModulePick.Layer;
                pick.AddComponent<BoxCollider>().size = Vector3.one * 0.92f;
                void Part(Vector3 pos, Vector3 scale)
                {
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(c.GetComponent<Collider>());
                    c.transform.SetParent(model.transform, false);
                    c.transform.localPosition = pos;
                    c.transform.localScale = scale;
                    c.GetComponent<MeshRenderer>().sharedMaterial = mat;
                }
                Part(Vector3.zero, Vector3.one * 0.5f);
                for (int i = -2; i <= 2; i++)
                    Part(new Vector3(i * 0.18f, 0f, 0f), new Vector3(0.04f, 0.9f, 0.9f)); // 날개
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------------- ④ 지구급 방문 세력 + 평판 ----------------

        private const string ContactDir = "Assets/_Project/Data/Contact";

        [MenuItem("SpaceStation/Grade Rules/Setup Contact")]
        public static void SetupContact()
        {
            if (!AssetDatabase.IsValidFolder(ContactDir))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Contact");
            string configPath = $"{ContactDir}/ContactConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<ContactConfig>(configPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ContactConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }

            // 그레이박스 통신 중계소 (12-5에서 정식 모델 · 수치) — 1칸, 통신 세기 1, 지구급 해금
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_CommRelay.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, "Assets/_Project/Art/Materials/M_CommRelay.mat");
            }
            mat.SetColor("_BaseColor", new Color(0.8f, 0.86f, 0.92f));
            mat.SetFloat("_Metallic", 0.4f);
            mat.SetFloat("_Smoothness", 0.6f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            var prefab = RelayPrefab(mat);

            string path = $"{ModuleDir}/MD_CommRelay.asset";
            var data = AssetDatabase.LoadAssetAtPath<ModuleData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ModuleData>();
                AssetDatabase.CreateAsset(data, path);
            }
            var so = new SerializedObject(data);
            so.FindProperty("_displayName").stringValue = "통신 중계소";
            var cat = so.FindProperty("_category");
            cat.enumValueIndex = System.Array.IndexOf(cat.enumNames, "Defense");
            var cells = so.FindProperty("_cellOffsets");
            cells.arraySize = 1;
            cells.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.FindProperty("_removable").boolValue = true;
            var cost = so.FindProperty("_buildCost");
            cost.arraySize = 1;
            cost.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Metal;
            cost.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 80f;
            var use = so.FindProperty("_consumption");
            use.arraySize = 1;
            use.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Power;
            use.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 1.5f;
            so.FindProperty("_relayPower").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            // 지구급 해금
            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>("Assets/_Project/Data/StationGrades.asset");
            var gso = new SerializedObject(grades);
            var unlocks = gso.FindProperty("_grades").GetArrayElementAtIndex(8).FindPropertyRelative("_unlocks");
            bool has = false;
            for (int i = 0; i < unlocks.arraySize; i++)
                has |= unlocks.GetArrayElementAtIndex(i).objectReferenceValue == data;
            if (!has)
            {
                unlocks.arraySize++;
                unlocks.GetArrayElementAtIndex(unlocks.arraySize - 1).objectReferenceValue = data;
            }
            gso.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();

            // 씬: 설정 · 건설 목록 · 외부 접촉 창
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var host = Object.FindFirstObjectByType<SpaceStation.Simulation.SimulationHost>();
            var hso = new SerializedObject(host);
            // 만든 인스턴스를 그대로 넣으면 씬에 저장되지 않았음 (정책 설정 때와 같음) → 경로에서 다시 읽어 넣음
            hso.FindProperty("_contact").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ContactConfig>(configPath);
            hso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(host);

            var build = Object.FindFirstObjectByType<BuildController>();
            var bso = new SerializedObject(build);
            var list = bso.FindProperty("_buildableModules");
            has = false;
            for (int i = 0; i < list.arraySize; i++)
                has |= list.GetArrayElementAtIndex(i).objectReferenceValue == data;
            if (!has)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
            }
            bso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(build);

            var station = Object.FindFirstObjectByType<StationController>();
            var research = Object.FindFirstObjectByType<SpaceStation.UI.ResearchPanel>(FindObjectsInactive.Include);
            var policy = Object.FindFirstObjectByType<SpaceStation.UI.PolicyPanel>(FindObjectsInactive.Include);
            var anchor = policy != null ? policy.transform : research.transform;
            var old = anchor.parent.Find("ContactPanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("ContactPanel", typeof(RectTransform));
            go.transform.SetParent(anchor.parent, false);
            go.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var panel = go.AddComponent<SpaceStation.UI.ContactPanel>();
            var rso = new SerializedObject(research);
            var pso = new SerializedObject(panel);
            pso.FindProperty("_station").objectReferenceValue = station;
            foreach (var f in new[] { "_font", "_fillSprite", "_frameSprite", "_buttonSprite", "_holoArt" })
                pso.FindProperty(f).objectReferenceValue = rso.FindProperty(f).objectReferenceValue;
            pso.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // 상위 모듈 '심우주 통신기지' (중계소 ×3) 레시피 — 레시피 설정이 모든 레시피를 다시 만들고 씬에 연결
            RecipeSetup.Setup();
            Debug.Log("[GradeRulesSetup] 외부 접촉: 설정 · 통신 중계소 · 지구급 해금 · 외부 접촉 창 · 심우주 통신기지 레시피 완료");
        }

        /// <summary>그레이박스 통신 중계소: 작은 칸 상자 + 위로 선 기둥 + 기울어진 접시 안테나.</summary>
        private static GameObject RelayPrefab(Material mat)
        {
            const string path = "Assets/_Project/Prefabs/Modules/PF_CommRelay.prefab";
            var root = new GameObject("PF_CommRelay");
            try
            {
                root.AddComponent<ModuleView>();
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                var model = new GameObject("Model");
                model.transform.SetParent(visual.transform, false);
                var box = visual.AddComponent<BoxCollider>();
                box.size = Vector3.one;
                var pick = new GameObject(ModulePick.ChildName);
                pick.transform.SetParent(root.transform, false);
                if (ModulePick.Layer >= 0)
                    pick.layer = ModulePick.Layer;
                pick.AddComponent<BoxCollider>().size = Vector3.one * 0.92f;
                void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler)
                {
                    var c = GameObject.CreatePrimitive(type);
                    Object.DestroyImmediate(c.GetComponent<Collider>());
                    c.transform.SetParent(model.transform, false);
                    c.transform.localPosition = pos;
                    c.transform.localEulerAngles = euler;
                    c.transform.localScale = scale;
                    c.GetComponent<MeshRenderer>().sharedMaterial = mat;
                }
                Part(PrimitiveType.Cube, new Vector3(0f, -0.2f, 0f), new Vector3(0.8f, 0.5f, 0.8f), Vector3.zero);
                Part(PrimitiveType.Cylinder, new Vector3(0f, 0.15f, 0f), new Vector3(0.08f, 0.2f, 0.08f), Vector3.zero); // 기둥
                Part(PrimitiveType.Cylinder, new Vector3(0f, 0.36f, 0.05f), new Vector3(0.7f, 0.03f, 0.7f), new Vector3(35f, 0f, 0f)); // 접시
                Part(PrimitiveType.Cylinder, new Vector3(0f, 0.46f, -0.1f), new Vector3(0.04f, 0.12f, 0.04f), new Vector3(35f, 0f, 0f)); // 수신기
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("SpaceStation/Grade Rules/Contact Visit Now")]
        public static void ContactNow() => WithContact(c => c.BeginVisit());

        [MenuItem("SpaceStation/Grade Rules/Raid Bombard Now")]
        public static void BombardNow() => WithContact(c => c.ForceRaid(SpaceStation.Simulation.ContactSystem.RaidKind.Bombard));

        [MenuItem("SpaceStation/Grade Rules/Raid Boarding Now")]
        public static void BoardingNow() => WithContact(c => c.ForceRaid(SpaceStation.Simulation.ContactSystem.RaidKind.Boarding));

        /// <summary>확인용: 지구급 시작 상태가 아직 없어 대륙급 저장에서 시험할 때 — 가동 중인 모듈 위 빈 칸에 통신 중계소 2개 (비용 없음).</summary>
        [MenuItem("SpaceStation/Grade Rules/Add 2 Comm Relays (test)")]
        public static void AddRelays() => WithContact(c =>
        {
            var station = Object.FindFirstObjectByType<StationController>();
            var sim = station.Simulation;
            var relay = AssetDatabase.LoadAssetAtPath<ModuleData>($"{ModuleDir}/MD_CommRelay.asset");
            int placed = 0;
            foreach (var m in new System.Collections.Generic.List<SpaceStation.Core.ModuleInstance>(sim.Grid.Modules))
            {
                if (placed >= 2)
                    break;
                if (!sim.Connectivity.IsActive(m))
                    continue;
                foreach (var cell in m.Cells)
                {
                    var up = cell + Vector3Int.up;
                    if (!sim.Grid.IsOccupied(up) && sim.Grid.TryPlace(relay, up, 0, out _))
                    {
                        placed++;
                        break;
                    }
                }
            }
            Debug.Log($"[GradeRulesSetup] 통신 중계소 {placed}개 · 통신 세기 {c.RelayPower}");
        });

        [MenuItem("SpaceStation/Grade Rules/Add 2 Comm Relays (test)", true)]
        [MenuItem("SpaceStation/Grade Rules/Contact Visit Now", true)]
        [MenuItem("SpaceStation/Grade Rules/Raid Bombard Now", true)]
        [MenuItem("SpaceStation/Grade Rules/Raid Boarding Now", true)]
        public static bool CanContact() => Application.isPlaying;

        private static void WithContact(System.Action<SpaceStation.Simulation.ContactSystem> action)
        {
            var station = Object.FindFirstObjectByType<StationController>();
            var contact = station != null && station.Simulation != null ? station.Simulation.Contact : null;
            if (contact == null)
            {
                Debug.LogWarning("[GradeRulesSetup] 게임 씬 플레이 중에만 (외부 접촉 설정 필요)");
                return;
            }
            action(contact);
        }

        [MenuItem("SpaceStation/Grade Rules/Strike Warning Now")]
        public static void StrikeNow()
        {
            var station = Object.FindFirstObjectByType<StationController>();
            var crisis = station != null && station.Simulation != null ? station.Simulation.Crisis : null;
            if (crisis == null)
            {
                Debug.LogWarning("[GradeRulesSetup] 게임 씬 플레이 중에만 (정책 설정 필요)");
                return;
            }
            crisis.BeginWarning();
        }

        [MenuItem("SpaceStation/Grade Rules/Strike Warning Now", true)]
        public static bool CanStrike() => Application.isPlaying;

        private static void Trigger(float warning)
        {
            var station = Object.FindFirstObjectByType<StationController>();
            var shower = station != null && station.Simulation != null ? station.Simulation.MeteorShower : null;
            if (shower == null)
            {
                Debug.LogWarning("[GradeRulesSetup] 게임 씬 플레이 중에만");
                return;
            }
            shower.BeginWarning(warning);
        }
    }
}
