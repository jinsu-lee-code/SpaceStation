using System.Collections.Generic;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 12-4 연구 확장 설정 (메뉴 SpaceStation/Research/Setup Expansion, 사용자 결정 2026-10-14, BALANCE 38번). 여러 번 실행해도 결과가 같다.
    /// - 카테고리 6개에 Lv.5~8 (Lv.1~4는 그대로 두고 뒤에 붙임): 기존 효과 줄을 이어서 강화 (가안, 등급 작업 12-7 ~ 12-10에서 조정)
    /// - 레벨 상한 Lv.5~8 = 도시급 180 · 국가급 270 · 대륙급 400 · 지구급 600 (각 등급 도달 인구, 측정 전 가안)
    /// - 레시피 해금 연구 단계별 비용 + 연계 연구 3개(있는 시험 모듈로): 폐쇄 순환(생태 모듈) · 공공 복지(복지 센터) · 심우주 청취(심우주 통신기지)
    /// - 레시피 담당 카테고리 · 자동 해금은 RecipeSetup이 맞춤 (끝에 다시 실행)
    /// </summary>
    public static class ResearchExpansionSetup
    {
        private const string ResearchDir = "Assets/_Project/Data/Research";
        private const string EvolvedDir = "Assets/_Project/Data/Modules/Evolved";
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";

        // Lv.5~8 공통: 시작 비용(산소, 물, 금속), 연구 중 전력 수요, 소요 시간
        private static readonly float[,] UpperCost = { { 160, 160, 220 }, { 220, 220, 300 }, { 300, 300, 400 }, { 400, 400, 520 } };
        private static readonly float[] UpperPower = { 24, 30, 38, 46 };
        private static readonly float[] UpperDuration = { 300, 360, 420, 480 };

        [MenuItem("SpaceStation/Research/Setup Expansion")]
        public static void Run()
        {
            Upper("RC_Maintenance",
                L("수리 비용 12%, 수리 시간 12초", M(ResearchStat.RepairCostRate, 0.12f), M(ResearchStat.RepairDuration, 12f)),
                L("노후화 속도 -50%", M(ResearchStat.DecayMultiplier, 0.5f)),
                L("재건축 비용 -60%, 노후 효율 최저 40%", M(ResearchStat.RebuildCostMultiplier, 0.4f), M(ResearchStat.DurabilityEfficiencyFloor, 0.4f)),
                L("수리 비용 10%, 수리 시간 10초, 노후화 속도 -60%", M(ResearchStat.RepairCostRate, 0.1f), M(ResearchStat.RepairDuration, 10f), M(ResearchStat.DecayMultiplier, 0.4f)));
            Upper("RC_Defense",
                L("운석 명중 시 파손 면역 50%, 운석 · 태양 폭풍 25초 전 경보", M(ResearchStat.HitImmunityChance, 0.5f), M(ResearchStat.EarlyWarningSeconds, 25f)),
                L("튕김 명중 45% → 30%", M(ResearchStat.RicochetChance, 0.3f)),
                L("포탑 격추 ×1.5, 격추 상한 85%", M(ResearchStat.TurretInterceptMultiplier, 1.5f), M(ResearchStat.TurretMaxIntercept, 0.85f)),
                L("실드 · 포탑 반경 +2칸, 파손 면역 60%", M(ResearchStat.DefenseRadiusBonus, 2f), M(ResearchStat.HitImmunityChance, 0.6f)));
            Upper("RC_Production",
                L("산소 · 물 · 식량 생산 +40%", M(ResearchStat.LifeSupportProductionMultiplier, 1.4f)),
                L("좋은 인접 효과 +20%p", M(ResearchStat.AdjacencyBonusBoost, 0.2f)),
                L("채굴 도킹 금속 +2, 산소 · 물 · 식량 생산 +50%", M(ResearchStat.MiningBonus, 2f), M(ResearchStat.LifeSupportProductionMultiplier, 1.5f)),
                L("산소 · 물 · 식량 생산 +60%", M(ResearchStat.LifeSupportProductionMultiplier, 1.6f)));
            Upper("RC_Energy",
                L("전력 최소 효율 55%", M(ResearchStat.MinPowerEfficiency, 0.55f)),
                L("태양광 발전 +45%", M(ResearchStat.SolarMultiplier, 1.45f)),
                L("배터리 저장량 +40%", M(ResearchStat.BatteryMultiplier, 1.4f)),
                L("태양광 발전 +60%, 전력 최소 효율 60%", M(ResearchStat.SolarMultiplier, 1.6f), M(ResearchStat.MinPowerEfficiency, 0.6f)));
            Upper("RC_Habitation",
                L("만족도 상한 +15", M(ResearchStat.SatisfactionCapBonus, 15f)),
                L("의료 · 휴게 반경 +2칸", M(ResearchStat.ServiceRadiusBonus, 2f)),
                L("거주 모듈 수용 인원 +3", M(ResearchStat.HousingBonus, 3f)),
                L("만족도 상한 +20", M(ResearchStat.SatisfactionCapBonus, 20f)));
            Upper("RC_Construction",
                L("건설 비용 -15%", M(ResearchStat.BuildCostMultiplier, 0.85f)),
                L("철거 환급 80%", M(ResearchStat.DemolishRefundRate, 0.8f)),
                L("창고 저장 한도 증가량 +100", M(ResearchStat.StorageBonusAdd, 100f)),
                L("건설 비용 -20%", M(ResearchStat.BuildCostMultiplier, 0.8f)));

            // 레벨 상한 Lv.5~8 (Lv.1~4 유지)
            var caps = AssetDatabase.LoadAssetAtPath<ResearchLevelCapConfig>($"{ResearchDir}/ResearchLevelCaps.asset");
            var reqs = new List<ResearchLevelCapConfig.Requirement>();
            for (int l = 1; l <= 4; l++)
                reqs.Add(caps.Get(l));
            reqs.Add(new ResearchLevelCapConfig.Requirement { Grade = 4, MinPopulation = 180 });
            reqs.Add(new ResearchLevelCapConfig.Requirement { Grade = 5, MinPopulation = 270 });
            reqs.Add(new ResearchLevelCapConfig.Requirement { Grade = 6, MinPopulation = 400 });
            reqs.Add(new ResearchLevelCapConfig.Requirement { Grade = 7, MinPopulation = 600 });
            caps.EditorSet(reqs);
            EditorUtility.SetDirty(caps);

            // 레시피 · 연구소 계통 모듈 (담당 카테고리 · 자동 해금 · 레시피 슬롯)
            RecipeSetup.Setup();

            // 연계 연구 3개 + 설정
            var linked = new List<LinkedResearchData>
            {
                Linked("LR_ClosedLoop", "폐쇄 순환", ResearchCategory.Production, "MD_Evo_EcoModule",
                    "생태 모듈의 순환을 정거장 전체로 넓혀 주민의 산소 · 물 소비 -10%", Cost(80, 80, 200), 14f, 180f,
                    M(ResearchStat.ResidentLifeConsumption, 0.9f)),
                Linked("LR_PublicWelfare", "공공 복지", ResearchCategory.Habitation, "MD_Evo_WelfareCenter",
                    "복지 센터가 운영을 맡아 모든 의료 · 휴게 시설 반경 +1칸", Cost(80, 80, 220), 14f, 180f,
                    M(ResearchStat.ServiceRadiusExtra, 1f)),
                Linked("LR_DeepListening", "심우주 청취", ResearchCategory.Defense, "MD_Evo_DeepSpaceComm",
                    "심우주 통신기지의 안테나 배열을 상시 청취에 돌려 통신 세기 +2", Cost(120, 120, 320), 20f, 240f,
                    M(ResearchStat.RelayPowerBonus, 2f)),
            };
            string configPath = $"{ResearchDir}/ResearchExpansionConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<ResearchExpansionConfig>(configPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ResearchExpansionConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }
            config.EditorSet(new List<RecipeResearchCost>
            {
                Stage(60, 60, 150, 1, 10f, 120f),
                Stage(100, 100, 250, 2, 14f, 180f),
                Stage(150, 150, 380, 3, 18f, 240f),
                Stage(200, 200, 520, 4, 22f, 300f),
            }, linked);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            // 씬: 호스트에 설정 (만든 인스턴스를 그대로 넣으면 저장되지 않았던 적이 있어 경로로 다시 읽음)
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var host = Object.FindFirstObjectByType<SimulationHost>();
            var hso = new SerializedObject(host);
            hso.FindProperty("_researchExpansion").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ResearchExpansionConfig>(configPath);
            hso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ResearchExpansionSetup] Lv.5~8 · 레벨 상한 · 레시피 해금 연구 비용 · 연계 연구 3개 · 씬 연결 완료");
        }

        private static ResearchModifier M(ResearchStat stat, float value) => new ResearchModifier { Stat = stat, Value = value };

        private static (string, ResearchModifier[]) L(string description, params ResearchModifier[] modifiers) => (description, modifiers);

        private static List<ResourceAmount> Cost(float oxygen, float water, float metal) => new List<ResourceAmount>
        {
            new ResourceAmount(ResourceType.Oxygen, oxygen), new ResourceAmount(ResourceType.Water, water), new ResourceAmount(ResourceType.Metal, metal),
        };

        private static RecipeResearchCost Stage(float oxygen, float water, float metal, int points, float power, float duration) => new RecipeResearchCost
        {
            StartCost = Cost(oxygen, water, metal), ResearchPoints = points, PowerDemand = power, Duration = duration,
        };

        /// <summary>카테고리의 Lv.1~4는 그대로, Lv.5~8을 이 값으로 (다시 실행하면 덮어씀).</summary>
        private static void Upper(string file, params (string desc, ResearchModifier[] mods)[] levels)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ResearchCategoryData>($"{ResearchDir}/{file}.asset");
            if (asset == null)
            {
                Debug.LogError($"[ResearchExpansionSetup] {file} 없음 — Research/Setup 먼저");
                return;
            }
            var list = new List<ResearchLevel>();
            for (int i = 0; i < 4 && i < asset.Levels.Count; i++)
                list.Add(asset.Levels[i]);
            for (int i = 0; i < levels.Length; i++)
            {
                list.Add(new ResearchLevel
                {
                    StartCost = Cost(UpperCost[i, 0], UpperCost[i, 1], UpperCost[i, 2]),
                    PowerDemand = UpperPower[i],
                    Duration = UpperDuration[i],
                    Description = levels[i].desc,
                    Modifiers = new List<ResearchModifier>(levels[i].mods),
                });
            }
            asset.EditorSet(asset.Category, asset.DisplayName, asset.Icon, list, asset.MinGrade);
            EditorUtility.SetDirty(asset);
        }

        private static LinkedResearchData Linked(string file, string name, ResearchCategory category, string module, string description,
            List<ResourceAmount> cost, float power, float duration, params ResearchModifier[] modifiers)
        {
            string path = $"{ResearchDir}/{file}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<LinkedResearchData>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<LinkedResearchData>();
                AssetDatabase.CreateAsset(asset, path);
            }
            var required = AssetDatabase.LoadAssetAtPath<ModuleData>($"{EvolvedDir}/{module}.asset");
            if (required == null)
                Debug.LogWarning($"[ResearchExpansionSetup] 필요 모듈 {module} 없음");
            asset.EditorSet(name, category, required, new ResearchLevel
            {
                StartCost = cost,
                PowerDemand = power,
                Duration = duration,
                Description = description,
                Modifiers = new List<ResearchModifier>(modifiers),
            });
            EditorUtility.SetDirty(asset);
            return asset;
        }
    }
}
