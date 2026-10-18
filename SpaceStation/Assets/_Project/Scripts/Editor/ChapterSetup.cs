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

            var ch1 = Chapter(1, grades, "깨어남", "",
                optional: (ChapterGoalKind.Population, 8, ResourceType.Metal, 0f, metalReward: 40f, research: 0, repairs: 0),
                waves: 2, hits: 1, warning: 90f,
                ("remember", "지구 쪽 창을 열어 둔다", "꺼져 가는 불빛을 계속 지켜본다"), ("forget", "항로만 본다", "뒤돌아보지 않고 앞으로 간다"));
            var ch2 = Chapter(2, grades, "배치", "중형 정거장으로 커지며, 어디에 무엇을 둘지가 생존을 가른다.",
                optional: (ChapterGoalKind.Stock, 0, ResourceType.Metal, 150f, metalReward: 0f, research: 1, repairs: 0),
                waves: 3, hits: 2, warning: 180f,
                ("reach", "멀리 뻗기", "새 신호 쪽으로 정거장을 넓힘"), ("shield", "안쪽 다지기", "중심부를 겹겹이 감쌈"));
            var ch3 = Chapter(3, grades, "무리 합류", "대형 정거장에 닿으며, 여러 무리가 한 지붕 아래 모인다.",
                optional: (ChapterGoalKind.Population, 50, ResourceType.Metal, 0f, metalReward: 0f, research: 0, repairs: 1),
                waves: 4, hits: 3, warning: 180f,
                ("open", "문을 연다", "오는 무리를 되도록 받음"), ("order", "질서를 세운다", "받기 전에 규칙부터"));

            // 13-2 방향 선택 → 코어 성격 축 (장 전환 방향 = 크게, 곁가지 축 = 작게 · 보통 — 임시 값)
            Shifts(ch1, 0, (CoreAxis.Memory, CorePole.First, ShiftSize.Large)); // 13-4: 1장 = 기억 축 (지구를 볼까)
            Shifts(ch1, 1, (CoreAxis.Memory, CorePole.Second, ShiftSize.Large));
            Shifts(ch2, 0, (CoreAxis.Scope, CorePole.Second, ShiftSize.Large), (CoreAxis.Memory, CorePole.Second, ShiftSize.Small));
            Shifts(ch2, 1, (CoreAxis.Control, CorePole.First, ShiftSize.Medium), (CoreAxis.Scope, CorePole.First, ShiftSize.Medium));
            Shifts(ch3, 0, (CoreAxis.Care, CorePole.Second, ShiftSize.Large), (CoreAxis.Control, CorePole.Second, ShiftSize.Small));
            Shifts(ch3, 1, (CoreAxis.Control, CorePole.First, ShiftSize.Large), (CoreAxis.Care, CorePole.First, ShiftSize.Small));
            CoreLines();

            // 13-3 이민선 시계 + 종족 (사용자 결정 2026-10-18: 시작 토끼 → 3장 곰, 2장부터 약 4분 · 4~6명 · 1분 전 예보, 2장부터 자연 증가 느리게)
            var ark = ArkShips(out var rabbit, out var bear);
            Ships(ch1, false, null, 1f);
            Ships(ch2, true, null, 3f);
            Ships(ch3, true, bear, 3f);

            // 13-4 1장 튜토리얼 (사용자 결정 2026-10-18): 코어 기동 9단계 · 내부 안내 · 컷신 · 목표 · 첫 긴 밤
            var boot = CoreBootTutorial();
            var interior = InteriorGuide();
            ChapterOne(ch1, interior);

            // 13-5a 탐사 (사용자 결정 2026-10-18 D-6): 2장 신호 = 소행성 · 난파선 · 파편 지대, 센서 어레이 · 구조 프레임을 2장에서 엶
            Exploration();
            Signals(ch2, SiteKind.Asteroid, SiteKind.Wreck, SiteKind.DebrisField);
            AddUnlocks(ch2, M("MD_SensorArray"), M("MD_StructureFrame"));

            // 13-5b 기록 복원 (사용자 결정 2026-10-18 D-7): 4갈래 × 3단계 · 3단계 봉인(기록 조각) · 2장부터 (연구소도 2장에서 엶)
            var recordResearch = Records();
            MoveUnlock(ch1, ch2, M("MD_ResearchLab"));
            CleanFragments(ch1, 1); // 절정을 깔끔히 버티면 조각 (1장 몫은 2장에서 씀)
            CleanFragments(ch2, 1);

            // 13-5c 2장 구성 (사용자 결정 2026-10-18): 평소 유성우 = 시작 3분 뒤 1파 · 이후 7~10분마다 3파, 절정 = 큰 유성우(두 방향 연속 · 파당 2개 · 막은 비율 + 파괴),
            // 목표 = 필수 3(중형 · 유성우 버티기 · 신호 밝히기) + 선택 2(지점 닿기 · 기록 하나 되살리기), 연결 허브도 2장에서
            ChapterTwo(ch2);
            AddUnlocks(ch2, M("MD_ConnectorHub"));

            // 13-6a 3장 정책 · 종족 반응 · 긴장도 (사용자 결정 2026-10-18): 정책 규칙 3장부터 · 1칸 → 대형 도달 때 2칸 · 4종 · 적용 30초 · 교체 3분
            ChapterThreePolicies(ch3);
            Society();

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
            hso.FindProperty("_core").objectReferenceValue = AssetDatabase.LoadAssetAtPath<CoreConfig>(CorePath);
            hso.FindProperty("_arkShips").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ArkShipConfig>(ArkPath);
            hso.FindProperty("_tutorial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TutorialData>(BootPath);
            hso.FindProperty("_exploration").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ExplorationConfig>(ExplorePath); // 서사판 = 코어 기동 (메뉴 Tutorial/Setup을 다시 돌리면 공통 튜토리얼로 바뀜 → 이 메뉴를 뒤에 다시)
            hso.FindProperty("_records").objectReferenceValue = AssetDatabase.LoadAssetAtPath<RecordConfig>(RecordPath);
            hso.FindProperty("_society").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SocietyConfig>(SocietyPath);
            // 13-5b 연구 목록 = 기록 4갈래 + 정비 자동화 (메뉴 Research/Setup을 다시 돌리면 공통 6분야로 바뀜 → 이 메뉴를 뒤에 다시)
            var researchList = hso.FindProperty("_researchCategories");
            researchList.arraySize = recordResearch.Count;
            for (int i = 0; i < recordResearch.Count; i++)
                researchList.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<ResearchCategoryData>(recordResearch[i]); // 경로로 다시 (막 만든 에셋을 그대로 넣으면 null로 저장됐음)
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

            // 13-2 코어 로그 창 (장 화면 바로 앞 — 장 결과 창이 그 위를 덮음)
            var oldLog = parent.Find("CoreLogPanel");
            if (oldLog != null)
                Object.DestroyImmediate(oldLog.gameObject);
            var logGo = new GameObject("CoreLogPanel", typeof(RectTransform));
            logGo.transform.SetParent(parent, false);
            logGo.transform.SetSiblingIndex(go.transform.GetSiblingIndex());
            var lrt = (RectTransform)logGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            var logPanel = logGo.AddComponent<SpaceStation.UI.CoreLogPanel>();
            var lso = new SerializedObject(logPanel);
            lso.FindProperty("_station").objectReferenceValue = station;
            foreach (var f in new[] { "_font", "_fillSprite", "_frameSprite", "_buttonSprite", "_holoArt" })
                lso.FindProperty(f).objectReferenceValue = rso.FindProperty(f).objectReferenceValue;
            lso.ApplyModifiedPropertiesWithoutUndo();

            // 13-5a 탐사 창 ([관리] 메뉴 '탐사') + 정거장 바깥 지점 표시
            var oldExplore = parent.Find("ExplorationPanel");
            if (oldExplore != null)
                Object.DestroyImmediate(oldExplore.gameObject);
            var exploreGo = new GameObject("ExplorationPanel", typeof(RectTransform));
            exploreGo.transform.SetParent(parent, false);
            exploreGo.transform.SetSiblingIndex(logGo.transform.GetSiblingIndex());
            var ert = (RectTransform)exploreGo.transform;
            ert.anchorMin = Vector2.zero;
            ert.anchorMax = Vector2.one;
            ert.offsetMin = Vector2.zero;
            ert.offsetMax = Vector2.zero;
            var explorePanel = exploreGo.AddComponent<SpaceStation.UI.ExplorationPanel>();
            var eso = new SerializedObject(explorePanel);
            eso.FindProperty("_station").objectReferenceValue = station;
            foreach (var f in new[] { "_font", "_fillSprite", "_frameSprite", "_buttonSprite", "_holoArt" })
                eso.FindProperty(f).objectReferenceValue = rso.FindProperty(f).objectReferenceValue;
            eso.ApplyModifiedPropertiesWithoutUndo();
            var oldView = GameObject.Find("ExplorationView");
            if (oldView != null)
                Object.DestroyImmediate(oldView);
            var viewGo = new GameObject("ExplorationView");
            var exploreView = viewGo.AddComponent<SpaceStation.UI.ExplorationView>();
            var xso = new SerializedObject(exploreView);
            xso.FindProperty("_station").objectReferenceValue = station;
            xso.FindProperty("_material").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_DefenseRange.mat");
            xso.FindProperty("_font").objectReferenceValue = rso.FindProperty("_font").objectReferenceValue;
            xso.FindProperty("_labelRoot").objectReferenceValue = parent as RectTransform;
            // 지점 모습 (바위는 배경 소행성 재질 + 새 재질 3개, 난파선은 Blender 모델 — 사용자 결정 2026-10-18)
            var ambient = Object.FindFirstObjectByType<SpaceStation.Building.AmbientSpace>(FindObjectsInactive.Include);
            var aso = ambient != null ? new SerializedObject(ambient) : null;
            xso.FindProperty("_rockMaterial").objectReferenceValue = aso?.FindProperty("_rockMaterial").objectReferenceValue;
            xso.FindProperty("_wreckModel").objectReferenceValue = WreckModel();
            xso.FindProperty("_iceMaterial").objectReferenceValue = ExploreMaterial("M_ExploreIce", new Color(0.62f, 0.84f, 0.98f), 0.85f, 0.05f, new Color(0.04f, 0.12f, 0.2f));
            xso.FindProperty("_hullMaterial").objectReferenceValue = ExploreMaterial("M_ExploreHull", new Color(0.42f, 0.43f, 0.46f), 0.45f, 0.65f, Color.black);
            xso.FindProperty("_glowMaterial").objectReferenceValue = ExploreMaterial("M_ExploreGlow", Color.white, 0.5f, 0f, Color.white);
            xso.ApplyModifiedPropertiesWithoutUndo();

            // 13-4 장 직전 컷신 (다른 HUD 위 — 재생할 때 맨 앞으로 옮김)
            var oldIntro = parent.Find("ChapterIntroView");
            if (oldIntro != null)
                Object.DestroyImmediate(oldIntro.gameObject);
            var introGo = new GameObject("ChapterIntroView", typeof(RectTransform));
            introGo.transform.SetParent(parent, false);
            var irt = (RectTransform)introGo.transform;
            irt.anchorMin = Vector2.zero;
            irt.anchorMax = Vector2.one;
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;
            var intro = introGo.AddComponent<SpaceStation.UI.ChapterIntroView>();
            var iso = new SerializedObject(intro);
            iso.FindProperty("_station").objectReferenceValue = station;
            iso.FindProperty("_clock").objectReferenceValue = clock;
            foreach (var f in new[] { "_font", "_fillSprite", "_frameSprite", "_buttonSprite", "_holoArt" })
                iso.FindProperty(f).objectReferenceValue = rso.FindProperty(f).objectReferenceValue;
            iso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ChapterSetup] 장 3개 · 코어 성격 · 이민선 · 종족 · 난이도 장별 간격 · 게임 씬 연결 · 장 화면 · 코어 로그 창 완료");
        }

        private const string CorePath = "Assets/_Project/Data/Chapters/CoreConfig.asset";
        private const string ArkPath = "Assets/_Project/Data/Chapters/ArkShipConfig.asset";

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        /// <summary>
        /// 13-3 종족 2종 + 이민선 설정 (사용자 결정 2026-10-18 D-8: 기본값 = 사회 1 + 기술 1 + 견딤 1, 값은 그레이박스 초안).
        /// 토끼: 의료 중시 · 통제에 순응 · 식량 생산 + / 곰: 여가 중시 · 통제에 반발 · 수리 속도 + · 열에 강함.
        /// </summary>
        private static ArkShipConfig ArkShips(out SpeciesData rabbit, out SpeciesData bear)
        {
            rabbit = LoadOrCreate<SpeciesData>(Folder + "/SP_Rabbit.asset");
            rabbit.EditorSet("rabbit", "토끼", "겁이 많지만 부지런한 무리. 아플 때 돌봐 줄 곳을 먼저 찾고, 텃밭을 잘 가꾼다.",
                ResidentNeed.Medical, 0.5f, 0.5f, ResidentTrait.Gardener, 0.02f, 0.1f, 0f, 0f);
            EditorUtility.SetDirty(rabbit);
            bear = LoadOrCreate<SpeciesData>(Folder + "/SP_Bear.asset");
            bear.EditorSet("bear", "곰", "힘이 세고 얽매이기 싫어하는 무리. 쉬고 노는 자리를 중시하고, 고치는 손이 빠르다.",
                ResidentNeed.Recreation, 0.5f, -0.5f, ResidentTrait.Technician, 0.02f, 0.1f, 0.5f, 0f);
            EditorUtility.SetDirty(bear);

            var ark = LoadOrCreate<ArkShipConfig>(ArkPath);
            ark.EditorSet(rabbit, new List<SpeciesData> { rabbit, bear },
                new List<AxisShift> { new AxisShift(CoreAxis.Care, CorePole.Second, ShiftSize.Medium), new AxisShift(CoreAxis.Scope, CorePole.Second, ShiftSize.Small) },
                new List<AxisShift> { new AxisShift(CoreAxis.Care, CorePole.First, ShiftSize.Small) },
                new List<AxisShift> { new AxisShift(CoreAxis.Care, CorePole.First, ShiftSize.Medium), new AxisShift(CoreAxis.Scope, CorePole.First, ShiftSize.Small) });
            EditorUtility.SetDirty(ark);
            return ark;
        }

        // ---------------- 13-4 1장 ----------------

        private const string BootPath = "Assets/_Project/Data/Chapters/TUT_CoreBoot.asset";
        private const string InteriorPath = "Assets/_Project/Data/Chapters/TUT_ArkInterior.asset";
        private const string ModuleFolder = "Assets/_Project/Data/Modules/";
        private const string CoreHex = "#9FE8FF"; // ChapterView.CoreTint

        private static ModuleData M(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<ModuleData>(ModuleFolder + name + ".asset");
            if (m == null)
                Debug.LogError("[ChapterSetup] 모듈 없음: " + name);
            return m;
        }

        /// <summary>본문 = 코어의 짧은 로그 (코어 색) + 조작 안내.</summary>
        private static string Body(string coreLog, string guide) => $"<color={CoreHex}>코어 · {coreLog}</color>\n{guide}";

        /// <summary>
        /// 13-4 코어 기동 (사용자 결정 2026-10-18: 기존 Phase 9 바깥 9단계를 깨어나는 코어의 시점으로 — 코어 로그 한두 줄 + 조작 안내).
        /// 단계 · 목표 · 강조 · 첫 밤 정지는 공통 튜토리얼(TutorialSetup)과 같고 문구만 다름. 문장은 확인용 초안.
        /// </summary>
        private static TutorialData CoreBootTutorial()
        {
            var data = LoadOrCreate<TutorialData>(BootPath);
            data.SetSteps(new[]
            {
                new TutorialStep("감각 연결",
                    Body("빛이 들어온다. 이것이 나를 둘러싼 몸인가.", "외부 센서를 하나씩 이어 정거장을 둘러보세요. 가운데가 코어, 모든 모듈은 코어와 면으로 이어져야 움직입니다."),
                    TutorialGoal.Camera),
                new TutorialStep("금속 확보",
                    Body("몸을 고치고 늘리려면 금속이 필요하다. 가까운 소행성에서 캐 오자.", "<b>산업</b> 탭에서 채굴 도킹을 골라 바깥 면에 붙이세요. 앞쪽 2칸은 채굴선이 드나드는 길이라 비워 둡니다."),
                    TutorialGoal.Build, TutorialHighlight.None, false, M("MD_MiningDock")),
                new TutorialStep("생명 유지 계통",
                    Body("작은 숨소리가 들린다. 토끼 넷. 이들은 숨 쉬고, 마시고, 먹어야 한다.", "산소 생성기 · 물 재활용기 · 농장을 하나씩 지으세요. <b>산소가 바닥나면 방주가 끝납니다.</b>"),
                    TutorialGoal.Build, TutorialHighlight.Resources, false, M("MD_Oxygen"), M("MD_WaterRecycler"), M("MD_Farm")),
                new TutorialStep("전력 계통",
                    Body("모든 계통이 전기를 원한다. 멀리서 오는 햇빛을 받아 두자.", "태양광 패널을 <b>2개</b> 지으세요. 태양광은 낮에만 돌고, 쓰고 <b>남는 전력</b>이 있어야 배터리가 찹니다."),
                    TutorialGoal.Build, TutorialHighlight.Resources, false, M("MD_Solar"), M("MD_Solar")),
                new TutorialStep("축전",
                    Body("곧 지구의 그림자 속으로 들어간다. 빛이 끊겨도 버틸 힘을 모아 두어야 한다.", "밤이 오기 전에 배터리를 지으세요. 배터리는 낮에 남은 전력을 모았다가 밤에 내보냅니다."),
                    TutorialGoal.Build, TutorialHighlight.None, true, M("MD_Battery")),
                new TutorialStep("거주 구역",
                    Body("토끼들이 좁은 코어 안에 웅크리고 있다. 머물 자리를 넓혀 주자.", "거주 모듈을 지으면 수용 인원이 늘고, 만족도가 충분하면 무리가 늘어납니다. 늘어난 만큼 마실 물도 필요하니 물 재활용기를 하나 더 지으세요."),
                    TutorialGoal.Build, TutorialHighlight.Resources, false, M("MD_Habitat"), M("MD_WaterRecycler"), M("MD_WaterRecycler")),
                new TutorialStep("시간 감각",
                    Body("밤은 길다. 하지만 내 시간은 원하는 만큼 빠르게 흐를 수 있다.", "배속을 올려 밤을 한 번 넘겨 보세요. 밤에는 배터리가 모아 둔 전력을 내보내고, 다 쓰면 효율이 떨어집니다."),
                    TutorialGoal.SurviveNight, TutorialHighlight.TimeControl),
                new TutorialStep("자가 정비",
                    Body("몸 한쪽이 삐걱인다. 낡은 곳은 스스로 손봐야 한다.", "모듈은 시간이 지나며 낡습니다. 낡은 모듈을 클릭하고 <b>정비</b>를 누르세요. 정비할수록 최대 내구도가 조금씩 줄어, 많이 낡으면 재건축합니다."),
                    TutorialGoal.Maintain, TutorialHighlight.Maintain),
                new TutorialStep("손상 대응",
                    Body("무언가 다가온다. 돌덩이다. 피할 수 없다면 고친다.", "운석에 맞은 모듈은 <b>파손</b>되어 멈추고, 오래 두면 옆으로 번지거나 부서집니다. 파손된 모듈을 클릭하고 <b>수리</b>를 누르세요."),
                    TutorialGoal.Repair, TutorialHighlight.Repair),
            });
            data.SetTexts("코어 기동", "코어 기동 완료",
                $"<color={CoreHex}>코어 · 모든 계통이 깨어났다. 이 방주를 어디로 데려갈지는 이제 나에게 달렸다.</color>\n" +
                "지금부터 운석 같은 사건이 찾아오고 모듈도 낡습니다. 1장 목표는 오른쪽 장 카드에 있습니다.\n" +
                "<color=#9AA4B2>방주 안은 모듈을 클릭하고 [들어가기]로 드론을 보내 둘러볼 수 있습니다 (선택 목표).</color>",
                transient: false, postTutorialGrace: 180f);
            EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>13-4 내부 안내 (사용자 결정 2026-10-18: 1장 선택 목표 '방주 안 둘러보기' — 들어가면 시작, 드론 시점 T4). 저장하지 않음.</summary>
        private static TutorialData InteriorGuide()
        {
            var data = LoadOrCreate<TutorialData>(InteriorPath);
            data.SetSteps(new[]
            {
                new TutorialStep("드론 연결",
                    Body("몸 안쪽은 작은 드론의 눈으로 본다.", "이동 키와 마우스로 움직이고, ESC로 밖에 나옵니다. 나오면 이 안내는 멈추고 다음에 들어올 때 처음부터 합니다."),
                    TutorialGoal.EnterInterior),
                new TutorialStep("방주 지도",
                    Body("드론의 패드에 방주 지도가 떠 있다.", "패드를 들어 보세요. 부탁이나 사고가 생긴 방이 지도에 표시되고, 다른 방으로 바로 갈 수 있습니다."),
                    TutorialGoal.InteriorPad),
                new TutorialStep("작은 부탁",
                    Body("토끼 하나가 무언가 부탁한다. 작은 일이지만, 들어주면 무리가 편해진다.", "부탁한 주민은 왼쪽 위 방 이름 아래에 표시됩니다. 찾아가 들어주면 <b>만족도</b>가 오릅니다."),
                    TutorialGoal.InteriorRequest),
                new TutorialStep("경보",
                    Body("어딘가에서 경보가 울린다. 드론으로 직접 막아 보자.", "평소에는 제한 시간 안에 막지 못하면 자원을 잃습니다. <color=#9AA4B2>이번은 연습이라 시간 제한이 없습니다.</color>"),
                    TutorialGoal.InteriorIncident),
            });
            data.SetTexts("방주 안", "방주 안 둘러보기 완료",
                $"<color={CoreHex}>코어 · 이들의 목소리는 밖에서는 들리지 않았다.</color>\nESC로 밖에 나옵니다.",
                transient: true, postTutorialGrace: 0f);
            EditorUtility.SetDirty(data);
            return data;
        }

        /// <summary>
        /// 13-4 1장 '깨어남' (사용자 결정 2026-10-18): 직전 컷신(인간이 남긴 방주 시스템 음성 → 코어의 첫 말) · 목표(필수 코어 기동 + 소형,
        /// 선택 방주 안 둘러보기 + 인구 8) · 절정 '첫 긴 밤'(밤 180초 + 운석 2개, 판정 = 생명 유지 바닥 시간 · 파괴). 컷신 · 문장은 확인용 초안.
        /// </summary>
        // ---------------- 13-6a 3장 정책 · 사회 ----------------

        private const string SocietyPath = "Assets/_Project/Data/Chapters/SocietyConfig.asset";
        private const string PolicyFolder = "Assets/_Project/Data/Policies/";

        /// <summary>13-6a 3장 정책 (사용자 결정 2026-10-18): 규칙 Policies(누적) · 1칸 + 대형 도달 때 1칸 · 배급제 · 야간 근무(통제형) · 이민 개방 · 축제 문화(자율형) · 30초 · 3분.</summary>
        private static void ChapterThreePolicies(ChapterData chapter)
        {
            var so = new SerializedObject(chapter);
            so.FindProperty("_rules").intValue = (int)StationRule.Policies;
            so.FindProperty("_policySlots").intValue = 1;
            so.FindProperty("_gradeUpPolicySlots").intValue = 1;
            so.FindProperty("_policyApplyDelay").floatValue = 30f;
            so.FindProperty("_policySwapCooldown").floatValue = 180f;
            var names = new[] { "PO_Rationing", "PO_NightShift", "PO_OpenImmigration", "PO_FestivalCulture" };
            var list = so.FindProperty("_policies");
            list.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                var p = AssetDatabase.LoadAssetAtPath<PolicyData>(PolicyFolder + names[i] + ".asset");
                if (p == null)
                    Debug.LogError("[ChapterSetup] 정책 없음: " + names[i]);
                list.GetArrayElementAtIndex(i).objectReferenceValue = p;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(chapter);
        }

        /// <summary>13-6a 종족 정책 반응 배율 · 긴장도 (값은 SocietyConfig 기본값 = 그레이박스 초안).</summary>
        private static void Society()
        {
            var society = LoadOrCreate<SocietyConfig>(SocietyPath);
            EditorUtility.SetDirty(society);
        }

        /// <summary>13-5c 2장 '배치': 유성우 규칙 · 첫 유성우 · 목표 5개 · 절정 '큰 유성우' (값은 그레이박스 초안 — BALANCE · 측정 때 조정).</summary>
        private static void ChapterTwo(ChapterData chapter)
        {
            var so = new SerializedObject(chapter);
            so.FindProperty("_rules").intValue = (int)StationRule.MeteorShower; // 장 규칙은 지금 장까지 누적 → 3장도 유성우
            so.FindProperty("_firstShowerDelay").floatValue = 180f;
            so.FindProperty("_firstShowerWaves").intValue = 1;

            var goals = so.FindProperty("_goals");
            goals.arraySize = 5;
            var grade = goals.GetArrayElementAtIndex(0);
            Clear(grade);
            grade.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.ReachGrade;
            grade.FindPropertyRelative("_grade").intValue = 2;
            FlagGoal(goals.GetArrayElementAtIndex(1), SpaceStation.Simulation.ChapterShowerSystem.ShowerFlag, "유성우 한 번 버티기 (파괴 없이)", false);
            FlagGoal(goals.GetArrayElementAtIndex(2), SpaceStation.Simulation.ExplorationSystem.ProbeFlag, "탐사선으로 신호 하나 밝히기", false);
            var reach = goals.GetArrayElementAtIndex(3);
            FlagGoal(reach, SpaceStation.Simulation.ExplorationSystem.ReachFlag, "탐사 지점에 닿기", true);
            var reward = reach.FindPropertyRelative("_rewardResources");
            reward.arraySize = 1;
            reward.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Metal;
            reward.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 100f;
            var record = goals.GetArrayElementAtIndex(4);
            FlagGoal(record, SpaceStation.Simulation.RecordArchiveSystem.RecordFlag, "기록 하나 되살리기", true);
            record.FindPropertyRelative("_rewardResearchPoints").intValue = 1;

            var climax = so.FindProperty("_climax");
            climax.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterClimaxKind.DirectionalShower;
            climax.FindPropertyRelative("_title").stringValue = "큰 유성우";
            climax.FindPropertyRelative("_description").stringValue = "두 방향에서 차례로 쏟아집니다 · 방향마다 3파 × 2개 · 예보를 보고 앞면을 가리세요";
            climax.FindPropertyRelative("_warningSeconds").floatValue = 180f;
            climax.FindPropertyRelative("_waves").intValue = 3;
            climax.FindPropertyRelative("_hitsPerWave").intValue = 2;
            climax.FindPropertyRelative("_settleSeconds").floatValue = 120f; // 남은 파손을 기다리는 최대 시간 (파괴 유예와 같게)
            climax.FindPropertyRelative("_hurtMaxDestroyed").intValue = 1;
            climax.FindPropertyRelative("_showerDirections").intValue = 2;
            climax.FindPropertyRelative("_showerFirstWarning").floatValue = 120f;
            climax.FindPropertyRelative("_showerNextWarning").floatValue = 40f;
            climax.FindPropertyRelative("_cleanMinBlocked").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(chapter);
        }

        private static void FlagGoal(SerializedProperty goal, string flag, string text, bool optional)
        {
            Clear(goal);
            goal.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Flag;
            goal.FindPropertyRelative("_flag").stringValue = flag;
            goal.FindPropertyRelative("_text").stringValue = text;
            goal.FindPropertyRelative("_optional").boolValue = optional;
        }

        private static void ChapterOne(ChapterData chapter, TutorialData interior)
        {
            var so = new SerializedObject(chapter);
            so.FindProperty("_introSpeaker").stringValue = "방주 시스템 음성 · 녹음";
            var lines = new[]
            {
                "전원 복구를 확인했습니다.",
                "방주 운영 지능 '코어'. 기동 절차를 시작합니다.",
                "승선 확인. 토끼 4. 인간 승선자 0.",
                "출발지 지구 저궤도. 도착지는 지정되지 않았습니다.",
                "남겨진 지시를 재생합니다. \"이들을 살려 줘. 우리가 하지 못한 방식으로.\"",
                "재생 끝. 이후의 모든 판단은 코어에게 맡깁니다.",
                "코어: ...빛이 들어온다. 여기는 어디지. 아니, 나는 무엇이지.",
            };
            var intro = so.FindProperty("_introLines");
            intro.arraySize = lines.Length;
            for (int i = 0; i < lines.Length; i++)
                intro.GetArrayElementAtIndex(i).stringValue = lines[i];
            so.FindProperty("_interiorGuide").objectReferenceValue = interior;

            var goals = so.FindProperty("_goals");
            var population = goals.GetArrayElementAtIndex(1); // Chapter()가 만든 선택 목표 (인구 8 · 금속 40)를 맨 뒤로
            int popCount = population.FindPropertyRelative("_count").intValue;
            goals.arraySize = 4;
            var boot = goals.GetArrayElementAtIndex(0);
            Clear(boot);
            boot.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Flag;
            boot.FindPropertyRelative("_flag").stringValue = SpaceStation.Simulation.ChapterSystem.TutorialFlag;
            boot.FindPropertyRelative("_text").stringValue = "코어 기동 (모든 계통 깨우기)";
            var reach = goals.GetArrayElementAtIndex(1);
            Clear(reach);
            reach.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.ReachGrade;
            reach.FindPropertyRelative("_grade").intValue = 1;
            var look = goals.GetArrayElementAtIndex(2);
            Clear(look);
            look.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Flag;
            look.FindPropertyRelative("_optional").boolValue = true;
            look.FindPropertyRelative("_flag").stringValue = SpaceStation.Simulation.ChapterSystem.InteriorFlag;
            look.FindPropertyRelative("_text").stringValue = "방주 안 둘러보기 (드론)";
            look.FindPropertyRelative("_rewardResearchPoints").intValue = 1;
            var pop = goals.GetArrayElementAtIndex(3);
            Clear(pop);
            pop.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Population;
            pop.FindPropertyRelative("_optional").boolValue = true;
            pop.FindPropertyRelative("_count").intValue = Mathf.Max(1, popCount);
            var reward = pop.FindPropertyRelative("_rewardResources");
            reward.arraySize = 1;
            reward.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Metal;
            reward.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 40f;

            var climax = so.FindProperty("_climax");
            climax.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterClimaxKind.LongNight;
            climax.FindPropertyRelative("_title").stringValue = "첫 긴 밤";
            climax.FindPropertyRelative("_description").stringValue = "지구 그림자가 평소보다 길게 드리웁니다 · 밤 3분 · 운석 2개";
            climax.FindPropertyRelative("_warningSeconds").floatValue = 90f;
            climax.FindPropertyRelative("_waves").intValue = 2;
            climax.FindPropertyRelative("_hitsPerWave").intValue = 1;
            climax.FindPropertyRelative("_settleSeconds").floatValue = 10f;
            climax.FindPropertyRelative("_nightSeconds").floatValue = 180f;
            climax.FindPropertyRelative("_cleanMaxShortageSeconds").floatValue = 0f;
            climax.FindPropertyRelative("_hurtMaxShortageSeconds").floatValue = 60f;
            climax.FindPropertyRelative("_hurtMaxDestroyed").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(chapter);
        }

        // ---------------- 13-5a 탐사 ----------------

        private const string ExplorePath = "Assets/_Project/Data/Chapters/ExplorationConfig.asset";

        /// <summary>13-5a 탐사 설정: 센서 = 센서 어레이, 난파선 기록 조각(인간 미스터리 T8, 확인용 초안). 수치는 데이터 기본값.</summary>
        private static ExplorationConfig Exploration()
        {
            var cfg = LoadOrCreate<ExplorationConfig>(ExplorePath);
            cfg.EditorSet(M("MD_SensorArray"), new List<string>
            {
                "화물선 '노아 7호' 항해 일지. 3호 방주 출항 확인. 승선 명단에 우리 이름은 없다.",
                "남은 연료로는 한 척만 띄울 수 있다. 회의는 길지 않았다.",
                "우리가 망친 것을 그들에게 넘기는 게 아니기를. 그들이 우리보다 낫기를.",
            });
            EditorUtility.SetDirty(cfg);
            return cfg;
        }

        // ---------------- 13-5b 기록 복원 ----------------

        private const string RecordPath = "Assets/_Project/Data/Chapters/RecordConfig.asset";
        private const string RecordFolder = "Assets/_Project/Data/Research/Records";
        private const string ResearchFolder = "Assets/_Project/Data/Research";

        /// <summary>
        /// 13-5b 기록 복원 (사용자 결정 2026-10-18 D-7): 서사판 전용 연구 분야 4갈래 × 3단계 (기존 6분야 효과를 다시 묶음,
        /// 비용 · 전력 · 시간 = RESEARCH.md 2번 Lv.1 ~ 3), 3단계는 봉인 — 덮어쓰기 효과 = 레벨 표의 효과 × 1.5 (기본값에서 늘어난 만큼).
        /// 문장(지구의 기억 · 인간 미스터리)은 확인용 초안. 돌려줌 = 씬 연구 목록의 에셋 경로 (4갈래 + 정비 자동화 — 씬을 열면 막 만든 에셋 참조가 끊겨 경로로 넘김).
        /// </summary>
        private static List<string> Records()
        {
            Directory.CreateDirectory(RecordFolder);
            var balance = AssetDatabase.LoadAssetAtPath<BalanceConfig>("Assets/_Project/Data/BalanceConfig.asset");
            string Icon(string rc) => AssetDatabase.LoadAssetAtPath<ResearchCategoryData>($"{ResearchFolder}/{rc}.asset")?.Icon ?? "event";

            var survival = RecordCategory("RC_RecordSurvival", ResearchCategory.Production, "생존 기록", Icon("RC_Production"),
                RecordLevel(1, "산소 · 물 · 식량 생산 +10%", (ResearchStat.LifeSupportProductionMultiplier, 1.1f)),
                RecordLevel(2, "태양광 발전 +15% · 전력 최소 효율 30%", (ResearchStat.SolarMultiplier, 1.15f), (ResearchStat.MinPowerEfficiency, 0.3f)),
                RecordLevel(3, "산소 · 물 · 식량 생산 +20% · 좋은 인접 효과 +10%p", (ResearchStat.LifeSupportProductionMultiplier, 1.2f), (ResearchStat.AdjacencyBonusBoost, 0.1f)));
            var structure = RecordCategory("RC_RecordStructure", ResearchCategory.Maintenance, "구조 기록", Icon("RC_Maintenance"),
                RecordLevel(1, "수리 비용 25% · 수리 시간 25초", (ResearchStat.RepairCostRate, 0.25f), (ResearchStat.RepairDuration, 25f)),
                RecordLevel(2, "건설 비용 -5% · 철거 환급 60%", (ResearchStat.BuildCostMultiplier, 0.95f), (ResearchStat.DemolishRefundRate, 0.6f)),
                RecordLevel(3, "노후 속도 20% 완화 · 수리 비용 20% · 수리 시간 20초", (ResearchStat.DecayMultiplier, 0.8f), (ResearchStat.RepairCostRate, 0.2f), (ResearchStat.RepairDuration, 20f)));
            float ricochet = balance != null ? balance.ShieldRicochetChance : 0.7f;
            var defense = RecordCategory("RC_RecordDefense", ResearchCategory.Defense, "방어 기록", Icon("RC_Defense"),
                RecordLevel(1, "운석 · 태양 폭풍 15초 전 경보 · 실드 · 포탑 반경 +1칸", (ResearchStat.EarlyWarningSeconds, 15f), (ResearchStat.DefenseRadiusBonus, 1f)),
                RecordLevel(2, "운석 명중 시 파손 면역 20% · 경보 중 운석 대상 표시", (ResearchStat.HitImmunityChance, 0.2f), (ResearchStat.MeteorTargetPreview, 1f)),
                RecordLevel(3, $"실드 튕김 명중 {ricochet * 100f:0}% → 45% · 파손 면역 30%", (ResearchStat.RicochetChance, 0.45f), (ResearchStat.HitImmunityChance, 0.3f)));
            var life = RecordCategory("RC_RecordLife", ResearchCategory.Habitation, "생활 기록", Icon("RC_Habitation"),
                RecordLevel(1, "만족도 상한 +5", (ResearchStat.SatisfactionCapBonus, 5f)),
                RecordLevel(2, "의료실 · 휴게실 반경 +1칸", (ResearchStat.ServiceRadiusBonus, 1f)),
                RecordLevel(3, "거주 모듈 수용 인원 +1 · 만족도 상한 +8", (ResearchStat.HousingBonus, 1f), (ResearchStat.SatisfactionCapBonus, 8f)));

            var tracks = new List<RecordTrack>
            {
                Track(survival, balance, "생산 +{0} · 좋은 인접 효과 +{1}",
                    "지구의 온실 기록. 유리 지붕 아래 토마토가 한겨울에도 붉게 익었다.",
                    "해가 지지 않는 여름의 북쪽 마을. 지붕마다 검은 판이 해를 따라 돌았다.",
                    "마지막 종자 은행의 목록. 씨앗 4만 종, 봉투마다 누군가의 손글씨 이름표.",
                    "목록 끝의 메모: '방주에는 씨앗보다 먼저 이것을 실어라.' 무엇을 가리키는지는 지워져 있다."),
                Track(structure, balance, "노후 {0} 완화 · 수리 비용 {1} · 수리 시간 {2}",
                    "다리를 고치는 사람들의 작업 일지. 볼트 하나에도 날짜가 적혀 있다.",
                    "무너진 도시를 다시 쌓은 기록. 그들은 남은 벽돌부터 세었다.",
                    "방주 설계 회의록 첫 장: '백 년을 버틸 것. 누구든 고칠 수 있을 것.'",
                    "회의록 서명란은 열두 칸, 이름은 열한 개. 마지막 칸은 비어 있다."),
                Track(defense, balance, "튕김 명중 {0} · 파손 면역 {1}",
                    "유성우 관측 일지. 아이들이 별똥별에 소원을 빌었다고 적혀 있다.",
                    "옛 정거장의 피격 보고서. 벽에 덧댄 얇은 판 하나가 사람을 살렸다.",
                    "하늘을 지키던 망원경들의 마지막 경보 기록.",
                    "경보는 정확했다. 다만 너무 늦게 받아들여졌다. 보고서 아래에 누군가 '우리는 알고 있었다'라고 적었다."),
                Track(life, balance, "수용 인원 +{0} · 만족도 상한 +{1}",
                    "어느 집 저녁 식탁 사진. 의자가 하나 남는다.",
                    "병원 복도에 걸린 그림들. 아이들이 그린 해는 모두 웃고 있다.",
                    "이웃끼리 나눈 쪽지 묶음: '불 꺼지면 우리 집으로 와요.'",
                    "쪽지 하나는 방주 앞으로 쓰였다: '우리 대신 누군가 살아 주기를.' 보낸 이의 이름은 없다."),
            };
            var cfg = LoadOrCreate<RecordConfig>(RecordPath);
            cfg.EditorSet(tracks,
                new List<AxisShift> { new AxisShift(CoreAxis.Memory, CorePole.First, ShiftSize.Small) },
                new List<AxisShift> { new AxisShift(CoreAxis.Memory, CorePole.First, ShiftSize.Medium) },
                new List<AxisShift> { new AxisShift(CoreAxis.Memory, CorePole.Second, ShiftSize.Medium) });
            EditorUtility.SetDirty(cfg);

            var list = new List<string>();
            foreach (var c in new[] { survival, structure, defense, life })
                list.Add(AssetDatabase.GetAssetPath(c));
            list.Add($"{ResearchFolder}/RC_Automation.asset"); // 정비 자동화는 그대로 (단일 연구)
            return list;
        }

        private static ResearchCategoryData RecordCategory(string file, ResearchCategory kind, string name, string icon, params ResearchLevel[] levels)
        {
            var c = LoadOrCreate<ResearchCategoryData>($"{RecordFolder}/{file}.asset");
            c.EditorSet(kind, name, icon, new List<ResearchLevel>(levels));
            EditorUtility.SetDirty(c);
            return c;
        }

        /// <summary>단계 하나 (비용 · 전력 · 시간 = RESEARCH.md 2-1 · 2-2 표 Lv.1 ~ 3).</summary>
        private static ResearchLevel RecordLevel(int level, string description, params (ResearchStat stat, float value)[] modifiers)
        {
            float[] lifeCost = { 20f, 40f, 70f }, metalCost = { 30f, 60f, 100f }, power = { 5f, 8f, 12f }, seconds = { 60f, 120f, 180f };
            int i = level - 1;
            var l = new ResearchLevel { PowerDemand = power[i], Duration = seconds[i], Description = description };
            l.StartCost.Add(new ResourceAmount(ResourceType.Oxygen, lifeCost[i]));
            l.StartCost.Add(new ResourceAmount(ResourceType.Water, lifeCost[i]));
            l.StartCost.Add(new ResourceAmount(ResourceType.Metal, metalCost[i]));
            foreach (var (stat, value) in modifiers)
                l.Modifiers.Add(new ResearchModifier { Stat = stat, Value = value });
            return l;
        }

        /// <summary>갈래 하나: 봉인 단계(3) 효과를 기본값에서 늘어난 만큼 × 1.5 → 덮어쓰기 효과 + 설명({0}, {1} … = 덮어쓴 값).</summary>
        private static RecordTrack Track(ResearchCategoryData category, BalanceConfig balance, string overwriteFormat, string memory1, string memory2, string memory3, string human)
        {
            var sealedLevel = category.GetLevel(3);
            var overwrite = new List<ResearchModifier>();
            var shown = new List<object>();
            foreach (var m in sealedLevel.Modifiers)
            {
                float b = BaseOf(m.Stat, balance);
                float v = b + (m.Value - b) * 1.5f;
                if (m.Stat == ResearchStat.HousingBonus || m.Stat == ResearchStat.ServiceRadiusBonus || m.Stat == ResearchStat.DefenseRadiusBonus)
                    v = Mathf.Ceil(v - 0.01f); // 칸 · 명 단위는 올림 (+1 → +2)
                v = Mathf.Max(0f, v);
                overwrite.Add(new ResearchModifier { Stat = m.Stat, Value = v });
                shown.Add(Shown(m.Stat, v));
            }
            return new RecordTrack(category, new List<string> { memory1, memory2, memory3 }, human, overwrite, string.Format(overwriteFormat, shown.ToArray()));
        }

        /// <summary>연구가 없을 때의 값 (ResearchEffects의 기본값과 같게).</summary>
        private static float BaseOf(ResearchStat stat, BalanceConfig balance)
        {
            switch (stat)
            {
                case ResearchStat.RepairCostRate: return balance != null ? balance.RepairCostRate : 0.3f;
                case ResearchStat.RepairDuration: return balance != null ? balance.RepairDuration : 30f;
                case ResearchStat.RicochetChance: return balance != null ? balance.ShieldRicochetChance : 0.7f;
                case ResearchStat.MinPowerEfficiency: return balance != null ? balance.MinPowerEfficiency : 0.25f;
                case ResearchStat.DemolishRefundRate: return balance != null ? balance.DemolishRefundRate : 0.5f;
                case ResearchStat.LifeSupportProductionMultiplier:
                case ResearchStat.DecayMultiplier:
                case ResearchStat.SolarMultiplier:
                case ResearchStat.BatteryMultiplier:
                case ResearchStat.BuildCostMultiplier:
                case ResearchStat.RebuildCostMultiplier:
                case ResearchStat.TurretInterceptMultiplier:
                    return 1f;
                default:
                    return 0f;
            }
        }

        /// <summary>덮어쓴 값의 화면 표기 (배율 → 늘어난 %, 비율 → %, 초 → 초, 그 밖 = 수).</summary>
        private static string Shown(ResearchStat stat, float v)
        {
            switch (stat)
            {
                case ResearchStat.LifeSupportProductionMultiplier: return $"{(v - 1f) * 100f:0}%";
                case ResearchStat.DecayMultiplier: return $"{(1f - v) * 100f:0}%";
                case ResearchStat.AdjacencyBonusBoost: return $"{v * 100f:0}%p";
                case ResearchStat.RepairCostRate:
                case ResearchStat.RicochetChance:
                case ResearchStat.HitImmunityChance:
                    return $"{v * 100f:0}%";
                case ResearchStat.RepairDuration: return $"{v:0}초";
                default: return $"{v:0.#}";
            }
        }

        /// <summary>모듈 해금을 앞 장에서 빼고 뒤 장에 넣음 (13-5b: 연구소 1장 → 2장).</summary>
        private static void MoveUnlock(ChapterData from, ChapterData to, ModuleData module)
        {
            if (module == null)
                return;
            var so = new SerializedObject(from);
            var list = so.FindProperty("_unlocks");
            for (int i = list.arraySize - 1; i >= 0; i--)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == module)
                {
                    list.GetArrayElementAtIndex(i).objectReferenceValue = null;
                    list.DeleteArrayElementAtIndex(i);
                }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(from);
            AddUnlocks(to, module);
        }

        private static void CleanFragments(ChapterData chapter, int count)
        {
            var so = new SerializedObject(chapter);
            so.FindProperty("_cleanFragments").intValue = count;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(chapter);
        }

        private const string WreckPath = "Assets/_Project/Art/Models/Explore/SM_Wreck.fbx";

        /// <summary>
        /// 13-5a 난파선 모델 (BlenderWork/Explore/wreck_builder.py → SM_Wreck.fbx): 재질 칸을 이름(M_Wreck*)으로 URP Lit 재질에 연결.
        /// 게임 조명이 따뜻해 중립 회색은 갈색으로 보이므로 선체 색은 푸른 쪽으로 (MODELING.md 7-7). 신호등 칸은 ExplorationView가 깜박임.
        /// </summary>
        private static GameObject WreckModel()
        {
            var importer = AssetImporter.GetAtPath(WreckPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("[ChapterSetup] 난파선 모델 없음: " + WreckPath);
                return null;
            }
            var slots = new (string slot, Color color, float smooth, float metal, Color emission)[]
            {
                ("Hull", new Color(0.6f, 0.66f, 0.76f), 0.35f, 0.2f, Color.black),
                ("Hull2", new Color(0.5f, 0.56f, 0.66f), 0.35f, 0.2f, Color.black),
                ("Stripe", new Color(0.85f, 0.42f, 0.14f), 0.3f, 0.1f, Color.black),
                ("Frame", new Color(0.3f, 0.33f, 0.4f), 0.45f, 0.3f, Color.black),
                ("Torn", new Color(0.72f, 0.74f, 0.78f), 0.6f, 0.6f, Color.black),
                ("Dark", new Color(0.06f, 0.065f, 0.08f), 0.2f, 0f, Color.black),
                ("Burn", new Color(0.07f, 0.065f, 0.065f), 0.05f, 0f, Color.black),
                ("BurnM", new Color(0.2f, 0.2f, 0.22f), 0.08f, 0f, Color.black),
                ("BurnL", new Color(0.38f, 0.41f, 0.48f), 0.12f, 0.1f, Color.black),
                ("Glass", new Color(0.08f, 0.14f, 0.2f), 0.92f, 0f, Color.black),
                ("GlassDark", new Color(0.04f, 0.05f, 0.065f), 0.85f, 0f, Color.black),
                ("Lit", new Color(1f, 0.8f, 0.55f), 0.4f, 0f, new Color(1.4f, 1f, 0.55f)),
                ("Vent", new Color(0.08f, 0.085f, 0.1f), 0.3f, 0.2f, Color.black),
                ("CableR", new Color(0.7f, 0.12f, 0.08f), 0.3f, 0f, Color.black),
                ("CableY", new Color(0.9f, 0.72f, 0.15f), 0.3f, 0f, Color.black),
                ("Beacon", new Color(1f, 0.2f, 0.15f), 0.4f, 0f, new Color(0.3f, 0.04f, 0.03f)),
            };
            foreach (var s in slots)
            {
                var mat = ExploreMaterial("M_Wreck" + s.slot, s.color, s.smooth, s.metal, s.emission);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), "M_Wreck" + s.slot), mat);
            }
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(WreckPath);
        }

        /// <summary>탐사 지점 모습 재질 (URP Lit, 발광은 _EMISSION을 켜 두고 색은 지점마다 MaterialPropertyBlock).</summary>
        private static Material ExploreMaterial(string name, Color baseColor, float smoothness, float metallic, Color emission)
        {
            const string dir = "Assets/_Project/Art/Materials/Explore";
            Directory.CreateDirectory(dir);
            string path = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (emission.maxColorComponent > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
                mat.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }

        private static void Signals(ChapterData chapter, params SiteKind[] kinds)
        {
            var so = new SerializedObject(chapter);
            var list = so.FindProperty("_signals");
            list.arraySize = kinds.Length;
            for (int i = 0; i < kinds.Length; i++)
                list.GetArrayElementAtIndex(i).enumValueIndex = (int)kinds[i];
            so.FindProperty("_signalDistanceMin").intValue = 6;
            so.FindProperty("_signalDistanceMax").intValue = 10;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(chapter);
        }

        /// <summary>장 해금 목록 뒤에 더함 (이미 있으면 건너뜀).</summary>
        private static void AddUnlocks(ChapterData chapter, params ModuleData[] modules)
        {
            var so = new SerializedObject(chapter);
            var list = so.FindProperty("_unlocks");
            foreach (var m in modules)
            {
                if (m == null)
                    continue;
                bool has = false;
                for (int i = 0; i < list.arraySize; i++)
                    has |= list.GetArrayElementAtIndex(i).objectReferenceValue == m;
                if (has)
                    continue;
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = m;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(chapter);
        }

        /// <summary>13-3: 장의 이민선 칸 (간격 240초 · 4~6명 · 60초 전 예보는 데이터 기본값).</summary>
        private static void Ships(ChapterData chapter, bool on, SpeciesData newSpecies, float growthScale)
        {
            var so = new SerializedObject(chapter);
            so.FindProperty("_ships").boolValue = on;
            so.FindProperty("_shipInterval").floatValue = 240f;
            so.FindProperty("_shipSizeMin").intValue = 4;
            so.FindProperty("_shipSizeMax").intValue = 6;
            so.FindProperty("_shipForecast").floatValue = 60f;
            so.FindProperty("_newSpecies").objectReferenceValue = newSpecies;
            so.FindProperty("_growthIntervalScale").floatValue = growthScale;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(chapter);
        }

        /// <summary>13-2: 장 전환 선택지 하나의 축 이동.</summary>
        private static void Shifts(ChapterData chapter, int choice, params (CoreAxis axis, CorePole toward, ShiftSize size)[] shifts)
        {
            var so = new SerializedObject(chapter);
            var list = so.FindProperty("_choices").GetArrayElementAtIndex(choice).FindPropertyRelative("_shifts");
            list.arraySize = shifts.Length;
            for (int i = 0; i < shifts.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Axis").enumValueIndex = (int)shifts[i].axis;
                e.FindPropertyRelative("Toward").enumValueIndex = (int)shifts[i].toward;
                e.FindPropertyRelative("Size").enumValueIndex = (int)shifts[i].size;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(chapter);
        }

        /// <summary>
        /// 13-2 코어 문장 표 — 틀 확인용 임시 문장 (사용자 결정 2026-10-15: 실제 문장은 13-4 ~ 13-7에서 톤에 맞춰).
        /// 계기마다 기본(Neutral) + 성향 문장 몇 개. {0} · {1}은 부르는 쪽 값.
        /// </summary>
        private static CoreConfig CoreLines()
        {
            var config = AssetDatabase.LoadAssetAtPath<CoreConfig>(CorePath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<CoreConfig>();
                AssetDatabase.CreateAsset(config, CorePath);
            }
            var lines = new List<(string key, CorePole pole, CoreAxis axis, string text)>
            {
                ("goal.done", CorePole.Neutral, CoreAxis.Care, "장 목표 완료: {0}{1}"),
                ("goal.done", CorePole.First, CoreAxis.Care, "목표 처리 완료: {0}{1} · 다음 작업으로"),
                ("goal.done", CorePole.Second, CoreAxis.Care, "장 목표 완료: {0}{1} · 모두 조금 더 편해질 거예요"),
                ("climax.warning", CorePole.Neutral, CoreAxis.Care, "절정 예고: {0} · {1}분 뒤 (대비하세요)"),
                ("climax.warning", CorePole.First, CoreAxis.Control, "절정 예고: {0} · {1}분 뒤 · 배치를 점검합니다"),
                ("climax.warning", CorePole.Second, CoreAxis.Care, "절정 예고: {0} · {1}분 뒤 · 다들 안쪽으로 모여 주세요"),
                ("chapter.start", CorePole.Neutral, CoreAxis.Care, "{0} 시작{1}"),
                ("chapter.start", CorePole.Second, CoreAxis.Scope, "{0} 시작 · 더 멀리 갈 수 있다{1}"),
                ("chapter.start", CorePole.First, CoreAxis.Scope, "{0} 시작 · 지킬 것부터 지킨다{1}"),
                ("gradeup", CorePole.Neutral, CoreAxis.Care, "{0}"),
                ("outcome.Clean", CorePole.Neutral, CoreAxis.Care, "모두 무사합니다. 기록해 둡니다."),
                ("outcome.Clean", CorePole.First, CoreAxis.Care, "손실 없음. 예상대로입니다."),
                ("outcome.Hurt", CorePole.Neutral, CoreAxis.Care, "상처는 났지만 버텼습니다."),
                ("outcome.Hurt", CorePole.Second, CoreAxis.Care, "다친 곳부터 살피겠습니다. 미안합니다."),
                ("outcome.Lost", CorePole.Neutral, CoreAxis.Care, "잃은 것이 있습니다. 잊지 않겠습니다."),
                ("outcome.Lost", CorePole.Second, CoreAxis.Memory, "잃은 것은 지우고 앞으로 갑니다."),
                // 13-4 1장 방향 (기억 축)
                ("choice.remember", CorePole.Neutral, CoreAxis.Care, "지구 쪽 창을 열어 두었다. 꺼져 가는 불빛을 잊지 않으려고."),
                ("choice.forget", CorePole.Neutral, CoreAxis.Care, "뒤를 보는 창은 닫았다. 이제 앞만 본다."),
                ("outcome.Clean", CorePole.First, CoreAxis.Memory, "아무도 잃지 않았다. 이 밤을 기억해 둔다."),
                // 13-3 이민선 ({0} 종족 · {1} 받은 수 · {2} 돌려보낸 수)
                ("ship.accept", CorePole.Neutral, CoreAxis.Care, "{0} {1}명을 모두 받았습니다. 자리는 함께 만들어 갑니다."),
                ("ship.accept", CorePole.Second, CoreAxis.Care, "{0} {1}명, 모두 어서 오세요."),
                ("ship.partial", CorePole.Neutral, CoreAxis.Care, "{0} {1}명을 받고 {2}명은 돌려보냈습니다."),
                ("ship.partial", CorePole.First, CoreAxis.Care, "수용 기준대로 {1}명만 받았습니다. {2}명은 다음 정거장을 찾아야 합니다."),
                ("ship.refuse", CorePole.Neutral, CoreAxis.Care, "{0} {2}명을 태운 배를 돌려보냈습니다. 기록해 둡니다."),
                ("ship.refuse", CorePole.First, CoreAxis.Care, "지금 있는 이들이 먼저입니다. {0} {2}명은 받지 않았습니다."),
                ("ship.refuse", CorePole.Second, CoreAxis.Care, "{0} {2}명을 돌려보냈습니다. 이 결정이 무겁습니다."),
            };
            var so = new SerializedObject(config);
            var list = so.FindProperty("_lines");
            list.arraySize = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Key").stringValue = lines[i].key;
                e.FindPropertyRelative("Pole").enumValueIndex = (int)lines[i].pole;
                e.FindPropertyRelative("Axis").enumValueIndex = (int)lines[i].axis;
                e.FindPropertyRelative("Text").stringValue = lines[i].text;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            return config;
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
                c.FindPropertyRelative("_description").stringValue = choices[i].description;
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
