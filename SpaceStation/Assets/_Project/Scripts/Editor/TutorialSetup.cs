using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using SpaceStation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// Phase 9 튜토리얼 설정 (메뉴 SpaceStation/Tutorial/Setup):
    /// TutorialData 에셋(8단계) 생성·갱신 → SimulationHost 연결 → HUD에 TutorialView 자리 (내용은 런타임 생성).
    /// 단계 문구는 에셋에서 고칠 수 있다 (다시 실행하면 이 기본값으로 덮어씀).
    /// </summary>
    public static class TutorialSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        private const string DataPath = "Assets/_Project/Data/Tutorial/TutorialData.asset";
        private const string ModuleFolder = "Assets/_Project/Data/Modules/";

        [MenuItem("SpaceStation/Tutorial/Setup")]
        public static void Setup()
        {
            var data = CreateData();
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            data = AssetDatabase.LoadAssetAtPath<TutorialData>(DataPath); // 씬을 열면 새로 만든 에셋 참조가 풀릴 수 있어 다시 읽음
            var host = Object.FindFirstObjectByType<SimulationHost>();
            var hso = new SerializedObject(host);
            hso.FindProperty("_tutorial").objectReferenceValue = data;
            hso.ApplyModifiedPropertiesWithoutUndo();
            BuildView();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TutorialSetup] 단계 {data.Steps.Count}개, HUD 연결 완료");
        }

        private static ModuleData Module(string name)
        {
            var m = AssetDatabase.LoadAssetAtPath<ModuleData>(ModuleFolder + name + ".asset");
            if (m == null)
                Debug.LogError($"[TutorialSetup] 모듈 없음: {name}");
            return m;
        }

        private static TutorialData CreateData()
        {
            var data = AssetDatabase.LoadAssetAtPath<TutorialData>(DataPath);
            if (data == null)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DataPath));
                data = ScriptableObject.CreateInstance<TutorialData>();
                AssetDatabase.CreateAsset(data, DataPath);
            }
            data.SetSteps(new[]
            {
                new TutorialStep("정거장 둘러보기",
                    "지휘관님, 환영합니다. 가운데 모듈이 정거장의 심장인 <b>코어</b>입니다.\n" +
                    "모든 모듈은 코어와 면으로 이어져 있어야 작동합니다. 먼저 카메라로 정거장을 둘러보세요.",
                    TutorialGoal.Camera),
                new TutorialStep("금속 확보: 채굴 도킹",
                    "금속은 모든 건설과 수리의 재료입니다. <b>산업</b> 탭에서 채굴 도킹을 골라 정거장 바깥 면에 붙이세요.\n" +
                    "도킹은 뒤쪽이 모듈에 닿아야 하고, 앞쪽 2칸은 채굴선 접근로라 비워 둬야 합니다 (바깥을 향하도록 자동 회전).",
                    TutorialGoal.Build, TutorialHighlight.None, false, Module("MD_MiningDock")),
                // 산소는 시작부터 줄어들어(4명 기준 약 4분) 생명 유지를 일찍 둔다
                new TutorialStep("생명 유지: 산소·물·식량",
                    "거주자는 산소·물·식량을 계속 소비합니다. 깜빡이는 자원 표시에서 줄어드는 양을 확인하세요.\n" +
                    "산소 생성기·물 재활용기·농장을 하나씩 지어 소비를 메우세요. <b>산소가 바닥나면 게임 오버</b>입니다.",
                    TutorialGoal.Build, TutorialHighlight.Resources, false,
                    Module("MD_Oxygen"), Module("MD_WaterRecycler"), Module("MD_Farm")),
                new TutorialStep("전력: 태양광 패널",
                    "채굴 도킹과 생명 유지 모듈은 전력을 씁니다. 전력이 모자라면 모든 모듈의 효율이 함께 떨어집니다.\n" +
                    "<b>전력</b> 탭에서 태양광 패널을 지으세요. 태양광은 낮에만 발전합니다.",
                    TutorialGoal.Build, TutorialHighlight.Resources, false, Module("MD_Solar")),
                new TutorialStep("첫 밤 대비: 배터리",
                    "곧 정거장이 지구 그림자에 들어가 <b>밤</b>이 됩니다. 밤에는 태양광이 멈춥니다.\n" +
                    "배터리는 낮에 남는 전력을 모아 두었다가 밤에 내보냅니다. 밤이 오기 전에 배터리를 지으세요.",
                    TutorialGoal.Build, TutorialHighlight.None, true, Module("MD_Battery")),
                new TutorialStep("인구 늘리기: 거주 모듈",
                    "정거장 등급은 <b>거주자 수와 모듈 수</b>로 오릅니다.\n" +
                    "거주 모듈을 지어 수용 인원을 늘리면, 만족도가 충분할 때 거주자가 천천히 늘어납니다.",
                    TutorialGoal.Build, TutorialHighlight.Resources, false, Module("MD_Habitat")),
                new TutorialStep("시간 빨리 감기",
                    "깜빡이는 시간 조절 버튼으로 배속을 올리거나 일시정지할 수 있습니다.\n" +
                    "배속을 올려 밤을 한 번 넘겨 보세요. 밤 동안 배터리가 전력을 대신 공급합니다.",
                    TutorialGoal.SurviveNight, TutorialHighlight.TimeControl),
                new TutorialStep("운석 대응: 수리",
                    "운석이 다가옵니다! 맞은 모듈은 <b>파손</b>되어 멈추고, 오래 두면 옆으로 번지거나 파괴됩니다.\n" +
                    "파손된 모듈(빨간 표시)을 클릭하고 <b>수리</b>를 누르세요. 수리에는 금속이 듭니다.",
                    TutorialGoal.Repair, TutorialHighlight.Repair),
            });
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }

        /// <summary>HUD에 튜토리얼 카드 자리. 조기 경보 위, 연구 창·일시정지·결과 화면 아래.</summary>
        private static void BuildView()
        {
            var hud = GameObject.Find("HUD");
            if (hud == null)
            {
                Debug.LogError("[TutorialSetup] HUD 없음");
                return;
            }
            var old = hud.transform.Find("Tutorial");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("Tutorial", typeof(RectTransform));
            go.transform.SetParent(hud.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var research = hud.transform.Find("ResearchPanel");
            if (research != null)
                go.transform.SetSiblingIndex(research.GetSiblingIndex());

            var view = go.AddComponent<TutorialView>();
            var so = new SerializedObject(view);
            so.FindProperty("_station").objectReferenceValue = Object.FindFirstObjectByType<StationController>();
            so.FindProperty("_build").objectReferenceValue = Object.FindFirstObjectByType<BuildController>();
            so.FindProperty("_buildMenu").objectReferenceValue = Object.FindFirstObjectByType<BuildMenu>();
            so.FindProperty("_selectionPanel").objectReferenceValue = Object.FindFirstObjectByType<SelectionActionsPanel>();
            so.FindProperty("_clock").objectReferenceValue = Object.FindFirstObjectByType<SimulationClock>();
            so.FindProperty("_camera").objectReferenceValue = Object.FindFirstObjectByType<OrbitCameraController>();
            var resources = Object.FindFirstObjectByType<ResourcePanel>();
            so.FindProperty("_resourcePanel").objectReferenceValue = resources != null ? resources.transform as RectTransform : null;
            var time = Object.FindFirstObjectByType<TimeControlPanel>();
            so.FindProperty("_timePanel").objectReferenceValue = time != null ? time.transform as RectTransform : null;
            so.FindProperty("_font").objectReferenceValue = hud.GetComponentInChildren<TMPro.TMP_Text>(true).font;
            so.FindProperty("_fillSprite").objectReferenceValue = HudArtBuilder.Fill;
            so.FindProperty("_frameSprite").objectReferenceValue = HudArtBuilder.Frame;
            so.FindProperty("_buttonSprite").objectReferenceValue = HudArtBuilder.Button;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
