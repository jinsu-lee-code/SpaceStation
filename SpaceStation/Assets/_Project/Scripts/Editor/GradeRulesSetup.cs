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
