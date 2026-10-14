using System.Collections.Generic;
using System.IO;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 13-1 장 틀 그레이박스 설정 (SpaceStation/Chapters/...).
    /// 장 3개를 만든다 — 장 N의 해금 · 새 요구 · 이벤트 규모 = 등급 N-1의 값 (그 등급에서 다음 등급으로 크던 구간이 한 장),
    /// 필수 목표 = 다음 등급 도달 (1장 소형 · 2장 중형 · 3장 대형, D-2). 선택 목표 · 절정(운석 연타) · 방향 선택지는 틀 확인용 임시 값
    /// (실제 내용은 13-4 · 13-6 · 13-8에서). 난이도 프리셋의 등급별 간격 배율을 장별 값으로 옮기고, 게임 씬에 연결 + 장 화면을 붙인다.
    /// </summary>
    public static class ChapterSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        private const string Folder = "Assets/_Project/Data/Chapters";
        private const string GradesPath = "Assets/_Project/Data/StationGrades.asset";

        [MenuItem("SpaceStation/Chapters/Setup Greybox")]
        public static void Setup()
        {
            var grades = AssetDatabase.LoadAssetAtPath<StationGradeConfig>(GradesPath);
            if (grades == null || grades.Grades.Count < 4)
            {
                Debug.LogError("[ChapterSetup] 등급 설정을 찾지 못함: " + GradesPath);
                return;
            }
            Directory.CreateDirectory(Folder);

            var ch1 = Chapter(1, grades, "생존의 기본", "소형 정거장에 닿을 때까지 버티며, 첫 무리가 머물 자리를 만든다.",
                optional: (ChapterGoalKind.Population, 8, ResourceType.Metal, 0f, metalReward: 40f, research: 0, repairs: 0),
                waves: 2, hits: 1, warning: 120f,
                ("expand", "넓히기", "더 많은 무리를 받을 준비"), ("guard", "다지기", "지금 있는 이들을 먼저 지킴"));
            var ch2 = Chapter(2, grades, "배치", "중형 정거장으로 커지며, 어디에 무엇을 둘지가 생존을 가른다.",
                optional: (ChapterGoalKind.Stock, 0, ResourceType.Metal, 150f, metalReward: 0f, research: 1, repairs: 0),
                waves: 3, hits: 2, warning: 180f,
                ("reach", "멀리 뻗기", "새 신호 쪽으로 정거장을 넓힘"), ("shield", "안쪽 다지기", "중심부를 겹겹이 감쌈"));
            var ch3 = Chapter(3, grades, "무리 합류", "대형 정거장에 닿으며, 여러 무리가 한 지붕 아래 모인다.",
                optional: (ChapterGoalKind.Population, 50, ResourceType.Metal, 0f, metalReward: 0f, research: 0, repairs: 1),
                waves: 4, hits: 3, warning: 180f,
                ("open", "문을 연다", "오는 무리를 되도록 받음"), ("order", "질서를 세운다", "받기 전에 규칙부터"));

            var configPath = Folder + "/Chapters.asset";
            var config = AssetDatabase.LoadAssetAtPath<ChapterConfig>(configPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ChapterConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }
            var cso = new SerializedObject(config);
            var list = cso.FindProperty("_chapters");
            list.arraySize = 3;
            list.GetArrayElementAtIndex(0).objectReferenceValue = ch1;
            list.GetArrayElementAtIndex(1).objectReferenceValue = ch2;
            list.GetArrayElementAtIndex(2).objectReferenceValue = ch3;
            cso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);

            // 난이도: 장 N 시작 = 등급 N-1의 간격 배율, 등급 목표를 채운 뒤 = 등급 N의 간격 배율
            foreach (var guid in AssetDatabase.FindAssets("t:DifficultyPreset"))
            {
                var preset = AssetDatabase.LoadAssetAtPath<DifficultyPreset>(AssetDatabase.GUIDToAssetPath(guid));
                var pso = new SerializedObject(preset);
                var from = pso.FindProperty("_eventIntervalMultipliers");
                var to = pso.FindProperty("_chapterEventIntervalMultipliers");
                var up = pso.FindProperty("_chapterGradeUpEventIntervalMultipliers");
                to.arraySize = 3;
                up.arraySize = 3;
                for (int i = 0; i < 3; i++)
                {
                    to.GetArrayElementAtIndex(i).floatValue = i < from.arraySize ? from.GetArrayElementAtIndex(i).floatValue : -1f;
                    up.GetArrayElementAtIndex(i).floatValue = i + 1 < from.arraySize ? from.GetArrayElementAtIndex(i + 1).floatValue : -1f;
                }
                pso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(preset);
            }
            AssetDatabase.SaveAssets();

            // 씬: 호스트 연결 + 장 화면
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var host = Object.FindFirstObjectByType<SpaceStation.Simulation.SimulationHost>();
            var hso = new SerializedObject(host);
            hso.FindProperty("_chapters").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ChapterConfig>(configPath);
            hso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(host);

            var station = Object.FindFirstObjectByType<StationController>();
            var clock = Object.FindFirstObjectByType<SimulationClock>();
            var research = Object.FindFirstObjectByType<SpaceStation.UI.ResearchPanel>(FindObjectsInactive.Include);
            var result = Object.FindFirstObjectByType<SpaceStation.UI.ResultScreen>(FindObjectsInactive.Include);
            var parent = research.transform.parent;
            var old = parent.Find("ChapterView");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("ChapterView", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            // 결과 화면(게임 오버)보다 아래, 다른 HUD 창보다 위
            if (result != null && result.transform.parent == parent)
                go.transform.SetSiblingIndex(result.transform.GetSiblingIndex());
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var view = go.AddComponent<SpaceStation.UI.ChapterView>();
            var rso = new SerializedObject(research);
            var vso = new SerializedObject(view);
            vso.FindProperty("_station").objectReferenceValue = station;
            vso.FindProperty("_clock").objectReferenceValue = clock;
            foreach (var f in new[] { "_font", "_fillSprite", "_frameSprite", "_buttonSprite", "_holoArt" })
                vso.FindProperty(f).objectReferenceValue = rso.FindProperty(f).objectReferenceValue;
            // 정거장 패널 = 튜토리얼 화면이 쓰는 자원 패널 (카드를 그 아래에 붙임)
            var tutorial = Object.FindFirstObjectByType<SpaceStation.UI.TutorialView>(FindObjectsInactive.Include);
            if (tutorial != null)
                vso.FindProperty("_stationPanel").objectReferenceValue = new SerializedObject(tutorial).FindProperty("_resourcePanel").objectReferenceValue;
            vso.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ChapterSetup] 장 3개 · 난이도 장별 간격 · 게임 씬 연결 · 장 화면 완료");
        }

        private static ChapterData Chapter(int number, StationGradeConfig grades, string subtitle, string climaxText,
            (ChapterGoalKind kind, int count, ResourceType resource, float amount, float metalReward, int research, int repairs) optional,
            int waves, int hits, float warning, params (string id, string title, string description)[] choices)
        {
            string path = $"{Folder}/CH_{number}.asset";
            var data = AssetDatabase.LoadAssetAtPath<ChapterData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ChapterData>();
                AssetDatabase.CreateAsset(data, path);
            }
            var grade = grades.Grades[number - 1]; // 장 N = 등급 N-1 → N 구간
            var so = new SerializedObject(data);
            so.FindProperty("_displayName").stringValue = $"{number}장";
            so.FindProperty("_subtitle").stringValue = subtitle;
            var unlocks = so.FindProperty("_unlocks");
            unlocks.arraySize = grade.Unlocks.Count;
            for (int i = 0; i < grade.Unlocks.Count; i++)
                unlocks.GetArrayElementAtIndex(i).objectReferenceValue = grade.Unlocks[i];
            // 요구: 등급이 오르는 순간(이 장의 등급 목표)에 그 등급의 요구 — 장 시작 요구는 1장(시작 상태)만, 2장부터는 지난 장 등급 목표에서 이미 생김
            var target = grades.Grades[number]; // 이 장의 등급 목표 (1장 소형 · 2장 중형 · 3장 대형)
            SetNeeds(so.FindProperty("_newNeeds"), number == 1 ? grade.NewNeeds : new List<ResidentNeed>());
            SetNeeds(so.FindProperty("_gradeUpNeeds"), target.NewNeeds);
            so.FindProperty("_rules").intValue = (int)StationRule.None; // 유성우 = 13-5 (2장), 정책 = 13-6 (3장)
            so.FindProperty("_meteorHitsMin").intValue = grade.MeteorHitsMin;
            so.FindProperty("_meteorHitsMax").intValue = grade.MeteorHitsMax;
            so.FindProperty("_eventIntervalMultiplier").floatValue = grade.EventIntervalMultiplier;
            so.FindProperty("_eventIntensityMultiplier").floatValue = grade.EventIntensityMultiplier;
            // 이벤트 규모도 등급이 오르는 순간 그 등급 값으로 (사용자 결정 2026-10-15)
            so.FindProperty("_gradeUpScale").boolValue = true;
            so.FindProperty("_gradeUpMeteorHitsMin").intValue = target.MeteorHitsMin;
            so.FindProperty("_gradeUpMeteorHitsMax").intValue = target.MeteorHitsMax;
            so.FindProperty("_gradeUpEventIntervalMultiplier").floatValue = target.EventIntervalMultiplier;
            so.FindProperty("_gradeUpEventIntensityMultiplier").floatValue = target.EventIntensityMultiplier;

            var goals = so.FindProperty("_goals");
            goals.arraySize = 2;
            var reach = goals.GetArrayElementAtIndex(0);
            Clear(reach);
            reach.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.ReachGrade;
            reach.FindPropertyRelative("_grade").intValue = number;
            var opt = goals.GetArrayElementAtIndex(1);
            Clear(opt);
            opt.FindPropertyRelative("_kind").enumValueIndex = (int)optional.kind;
            opt.FindPropertyRelative("_optional").boolValue = true;
            opt.FindPropertyRelative("_count").intValue = Mathf.Max(1, optional.count);
            opt.FindPropertyRelative("_resource").enumValueIndex = (int)optional.resource;
            opt.FindPropertyRelative("_amount").floatValue = optional.amount;
            var reward = opt.FindPropertyRelative("_rewardResources");
            reward.arraySize = optional.metalReward > 0f ? 1 : 0;
            if (optional.metalReward > 0f)
            {
                reward.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Metal;
                reward.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = optional.metalReward;
            }
            opt.FindPropertyRelative("_rewardResearchPoints").intValue = optional.research;
            opt.FindPropertyRelative("_rewardFreeRepairs").intValue = optional.repairs;

            var climax = so.FindProperty("_climax");
            climax.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterClimaxKind.MeteorBarrage;
            climax.FindPropertyRelative("_title").stringValue = "운석 연타 (임시 절정)";
            climax.FindPropertyRelative("_description").stringValue = climaxText + $" · 운석 {waves}파 × {hits}개";
            climax.FindPropertyRelative("_warningSeconds").floatValue = warning;
            climax.FindPropertyRelative("_waves").intValue = waves;
            climax.FindPropertyRelative("_waveInterval").floatValue = 20f;
            climax.FindPropertyRelative("_hitsPerWave").intValue = hits;
            climax.FindPropertyRelative("_settleSeconds").floatValue = 30f;
            climax.FindPropertyRelative("_cleanMaxDamaged").intValue = Mathf.Max(0, waves * hits / 3);
            climax.FindPropertyRelative("_hurtMaxDestroyed").intValue = 0;

            var list = so.FindProperty("_choices");
            list.arraySize = choices.Length;
            for (int i = 0; i < choices.Length; i++)
            {
                var c = list.GetArrayElementAtIndex(i);
                c.FindPropertyRelative("_id").stringValue = choices[i].id;
                c.FindPropertyRelative("_title").stringValue = choices[i].title;
                c.FindPropertyRelative("_description").stringValue = choices[i].description + " (효과는 13-2 코어 성격)";
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            return data;
        }

        private static void SetNeeds(SerializedProperty list, IReadOnlyList<ResidentNeed> needs)
        {
            list.arraySize = needs.Count;
            for (int i = 0; i < needs.Count; i++)
                list.GetArrayElementAtIndex(i).enumValueIndex = (int)needs[i];
        }

        private static void Clear(SerializedProperty goal)
        {
            goal.FindPropertyRelative("_text").stringValue = "";
            goal.FindPropertyRelative("_optional").boolValue = false;
            goal.FindPropertyRelative("_module").objectReferenceValue = null;
            goal.FindPropertyRelative("_flag").stringValue = "";
            goal.FindPropertyRelative("_rewardResources").arraySize = 0;
            goal.FindPropertyRelative("_rewardResearchPoints").intValue = 0;
            goal.FindPropertyRelative("_rewardFreeRepairs").intValue = 0;
        }
    }
}
