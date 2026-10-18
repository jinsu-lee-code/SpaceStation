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
            hso.FindProperty("_tutorial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TutorialData>(BootPath); // 서사판 = 코어 기동 (메뉴 Tutorial/Setup을 다시 돌리면 공통 튜토리얼로 바뀜 → 이 메뉴를 뒤에 다시)
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
