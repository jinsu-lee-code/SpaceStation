using System.Collections.Generic;
using SpaceStation.Building;
using SpaceStation.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 13-1 타이쿤판 해금 선택 설정 (메뉴 SpaceStation/Tycoon/Setup Unlock Choices, TYCOON.md K2-2 · BALANCE 41번):
    /// - UnlockChoiceConfig: 소형 = 정책(자동) / 중형 = 합성 1단계 · 유성우 방향 예보 / 대형 = 칸 단위 열 / 도시 = 합성 2단계 · 사회 사건 /
    ///   국가 = 정책 슬롯 확장 / 대륙 = 합성 3단계 · 방문 세력 (방사선 차폐는 시스템이 생기면 국가에 추가)
    /// - 대응 도구 모듈에 규칙 조건 (센서 어레이 = 유성우 · 방열판 = 열 · 통신 중계소 = 방문 세력) + 중형 해금 목록으로 (규칙이 열리면 바로 건설)
    /// - 정책 슬롯: 정책이 열리면 1 + 확장 2
    /// - 씬: SimulationHost 연결 + 해금 선택 창
    /// </summary>
    public static class TycoonSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        private const string ConfigPath = "Assets/_Project/Data/Tycoon/UnlockChoiceConfig.asset";
        private const int ToolGrade = 2; // 중형: 가장 이른 선택 등급

        [MenuItem("SpaceStation/Tycoon/Setup Unlock Choices")]
        public static void Setup()
        {
            var config = BuildConfig();
            SetToolModules();
            SetPolicySlots();
            WireScene();
            Debug.Log("[TycoonSetup] 해금 선택: 설정 · 대응 도구 모듈 규칙 · 정책 슬롯 · 씬 연결 완료");
        }

        /// <summary>
        /// 13-2 성장 비례 위협 (BALANCE 42번): 기준점은 지금 등급 표(13 · 24 · 27번)가 그 등급 크기의 정거장에서 내던 값에 맞춤
        /// (노출 면 ≈ 모듈 × 3, 구간 시작 상태 실측 도시급 약 580 · 국가급 약 1,100 · 대륙급 약 1,800). 도시급 뒤로도 넓히면 계속 커짐.
        /// </summary>
        [MenuItem("SpaceStation/Tycoon/Setup Growth Threat")]
        public static void SetupGrowthThreat()
        {
            EnsureFolder();
            var config = AssetDatabase.LoadAssetAtPath<GrowthThreatConfig>(GrowthPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<GrowthThreatConfig>();
                AssetDatabase.CreateAsset(config, GrowthPath);
            }
            config.Set(new List<ExposurePoint>
                {
                    new ExposurePoint(24f, 1f, 1f, 1f),       // 등급 없음 (코어 하나 = 노출 24)
                    new ExposurePoint(50f, 2f, 3f, 0.65f),    // 소형 (코어 + 모듈 7 × 약 3.5)
                    new ExposurePoint(85f, 3f, 6f, 0.38f),    // 중형 (모듈 20)
                    new ExposurePoint(150f, 6f, 9f, 0.25f),   // 대형 (모듈 40)
                    new ExposurePoint(500f, 8f, 11f, 0.21f),  // 도시급 (지금 등급 표의 마지막 값)
                    new ExposurePoint(1200f, 10f, 13f, 0.19f),
                    new ExposurePoint(2400f, 12f, 16f, 0.17f),
                },
                new List<ModulePoint>
                {
                    new ModulePoint(0f, 1f),
                    new ModulePoint(8f, 1.3f),
                    new ModulePoint(20f, 1.6f),
                    new ModulePoint(40f, 2f),
                    new ModulePoint(120f, 2.4f),
                },
                0.8f, 10f);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var host = Object.FindFirstObjectByType<SpaceStation.Simulation.SimulationHost>();
            var hso = new SerializedObject(host);
            hso.FindProperty("_growthThreat").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GrowthThreatConfig>(GrowthPath);
            hso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[TycoonSetup] 성장 비례 위협: 설정 · 씬 연결 완료");
        }

        private const string GrowthPath = "Assets/_Project/Data/Tycoon/GrowthThreatConfig.asset";

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data/Tycoon"))
                AssetDatabase.CreateFolder("Assets/_Project/Data", "Tycoon");
        }

        private static UnlockChoiceConfig BuildConfig()
        {
            EnsureFolder();
            var config = AssetDatabase.LoadAssetAtPath<UnlockChoiceConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<UnlockChoiceConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.SetGrades(new List<UnlockGradeEntry>
            {
                new UnlockGradeEntry(1, StationRule.Policies),
                new UnlockGradeEntry(2, StationRule.None,
                    new UnlockOption("synthesis1", "합성 1단계",
                        "모듈을 규격 모양으로 빈틈없이 채워 더 강한 진화 모듈로 합칩니다.\n열림: 1단계 레시피 · 합성 창",
                        StationRule.Synthesis),
                    new UnlockOption("meteor-shower", "유성우 방향 예보",
                        "위기: 유성우 (한 방향에서 여러 파로 쏟아지는 운석)\n대응: 오는 방향 예보 · 센서 어레이 건설 (예보 시간 +)\n예보된 면을 장갑 격벽으로 미리 덮을 수 있어요.",
                        StationRule.MeteorShower)),
                new UnlockGradeEntry(3, StationRule.None,
                    new UnlockOption("heat", "칸 단위 열",
                        "위기: 발열 모듈(핵융합로 · 제련소 · 연료전지) 주변 과열 → 효율 저하 · 파손\n대응: 방열판 건설 · 열 지도\n열을 다스리면 발열 모듈을 촘촘히 지을 수 있어요.",
                        StationRule.Heat)),
                new UnlockGradeEntry(4, StationRule.None,
                    new UnlockOption("synthesis2", "합성 2단계",
                        "더 큰 진화 모듈을 만드는 2단계 레시피가 열립니다.\n열림: 2단계 레시피",
                        StationRule.SynthesisStage2),
                    new UnlockOption("social-crisis", "사회 사건",
                        "위기: 만족도가 낮게 이어지면 파업 경고 → 파업 중 생산 감소\n대응: 무마 · 강경 대응 · 임금 인상 선택지\n만족도를 잘 지키면 위기 없이 지나가요.",
                        StationRule.SocialCrisis)),
                new UnlockGradeEntry(5, StationRule.None,
                    new UnlockOption("policy-expansion", "정책 슬롯 확장",
                        "정책 슬롯이 늘어납니다.\n상시 정책을 더 많이 동시에 걸 수 있어요.",
                        StationRule.PolicyExpansion)),
                new UnlockGradeEntry(6, StationRule.None,
                    new UnlockOption("synthesis3", "합성 3단계",
                        "가장 큰 진화 모듈을 만드는 3단계 이상 레시피가 열립니다.\n열림: 3단계 이상 레시피 (심우주 통신기지 등)",
                        StationRule.SynthesisStage3),
                    new UnlockOption("contact", "방문 세력",
                        "위기: 무법 선단 습격 (포격 · 침입 드론)\n대응: 통신 중계소 건설 · 교역 · 협상 · 평판\n상인 연합 · 학술 함대와 교역할 수 있어요.",
                        StationRule.Contact)),
            });
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<UnlockChoiceConfig>(ConfigPath);
        }

        private static ModuleData Module(string assetName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{assetName} t:ModuleData"))
            {
                var m = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name == assetName)
                    return m;
            }
            Debug.LogWarning($"[TycoonSetup] 모듈 없음: {assetName}");
            return null;
        }

        private static void SetToolModules()
        {
            var tools = new (string Name, StationRule Rule)[]
            {
                ("MD_SensorArray", StationRule.MeteorShower),
                ("MD_Radiator", StationRule.Heat),
                ("MD_CommRelay", StationRule.Contact),
            };
            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>("Assets/_Project/Data/StationGrades.asset");
            var gso = new SerializedObject(grades);
            var list = gso.FindProperty("_grades");
            foreach (var (name, rule) in tools)
            {
                var data = Module(name);
                if (data == null)
                    continue;
                var so = new SerializedObject(data);
                so.FindProperty("_requiredRule").intValue = (int)rule;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);

                // 해금 목록에서 빼고 중형에 넣음 (규칙이 열리는 순간 지을 수 있게)
                for (int g = 0; g < list.arraySize; g++)
                {
                    var unlocks = list.GetArrayElementAtIndex(g).FindPropertyRelative("_unlocks");
                    for (int i = unlocks.arraySize - 1; i >= 0; i--)
                        if (unlocks.GetArrayElementAtIndex(i).objectReferenceValue == data)
                            unlocks.DeleteArrayElementAtIndex(i);
                }
                var target = list.GetArrayElementAtIndex(ToolGrade).FindPropertyRelative("_unlocks");
                target.arraySize++;
                target.GetArrayElementAtIndex(target.arraySize - 1).objectReferenceValue = data;
            }
            gso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(grades);
            AssetDatabase.SaveAssets();
        }

        private static void SetPolicySlots()
        {
            var policies = AssetDatabase.LoadAssetAtPath<PolicyConfig>("Assets/_Project/Data/Policies/PolicyConfig.asset");
            var so = new SerializedObject(policies);
            var slots = so.FindProperty("_slotsByGrade");
            slots.arraySize = 8;
            for (int i = 0; i < slots.arraySize; i++)
                slots.GetArrayElementAtIndex(i).intValue = i == 0 ? 0 : 1; // 정책 규칙이 열리면 1 (소형 자동)
            so.FindProperty("_expansionSlots").intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(policies);
            AssetDatabase.SaveAssets();
        }

        private static void WireScene()
        {
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var host = Object.FindFirstObjectByType<SpaceStation.Simulation.SimulationHost>();
            var hso = new SerializedObject(host);
            hso.FindProperty("_unlockChoices").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnlockChoiceConfig>(ConfigPath);
            hso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(host);

            var station = Object.FindFirstObjectByType<StationController>();
            var research = Object.FindFirstObjectByType<SpaceStation.UI.ResearchPanel>(FindObjectsInactive.Include);
            var contact = Object.FindFirstObjectByType<SpaceStation.UI.ContactPanel>(FindObjectsInactive.Include);
            var anchor = contact != null ? contact.transform : research.transform;
            var old = anchor.parent.Find("UnlockChoicePanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("UnlockChoicePanel", typeof(RectTransform));
            go.transform.SetParent(anchor.parent, false);
            go.transform.SetSiblingIndex(anchor.GetSiblingIndex() + 1);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var panel = go.AddComponent<SpaceStation.UI.UnlockChoicePanel>();
            var rso = new SerializedObject(research);
            var pso = new SerializedObject(panel);
            pso.FindProperty("_station").objectReferenceValue = station;
            foreach (var f in new[] { "_font", "_fillSprite", "_frameSprite", "_buttonSprite", "_holoArt" })
                pso.FindProperty(f).objectReferenceValue = rso.FindProperty(f).objectReferenceValue;
            pso.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
