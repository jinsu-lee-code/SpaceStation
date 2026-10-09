using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// Phase 10 거주자 특성 설정 (메뉴 SpaceStation/Residents/Setup):
    /// ResidentConfig 에셋(특성 14종·이름·환경 원인 모듈) 생성·갱신 → 게임 씬 SimulationHost 연결.
    /// 수치는 BALANCE 26번. 다시 실행하면 이 기본값으로 덮어쓴다.
    /// </summary>
    public static class ResidentSetup
    {
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";
        public const string DataPath = "Assets/_Project/Data/Residents/ResidentConfig.asset";
        private const string ModuleFolder = "Assets/_Project/Data/Modules/";

        [MenuItem("SpaceStation/Residents/Setup")]
        public static void Setup()
        {
            CreateData();
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            var data = AssetDatabase.LoadAssetAtPath<ResidentConfig>(DataPath); // 씬을 열면 새 에셋 참조가 풀릴 수 있어 다시 읽음
            var host = Object.FindFirstObjectByType<SimulationHost>();
            var so = new SerializedObject(host);
            so.FindProperty("_residents").objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
            BuildRosterPanel();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ResidentSetup] 특성 {data.Traits.Count}종, 이름 {data.GivenNames.Count}×{data.Surnames.Count}");
        }

        /// <summary>HUD에 명단 창 자리 (내용은 런타임 생성). 연구 창과 같은 층 (결과 화면·일시정지 아래).</summary>
        private static void BuildRosterPanel()
        {
            var hud = GameObject.Find("HUD");
            if (hud == null)
            {
                Debug.LogError("[ResidentSetup] HUD 없음");
                return;
            }
            var old = hud.transform.Find("RosterPanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("RosterPanel", typeof(RectTransform));
            go.transform.SetParent(hud.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var research = hud.transform.Find("ResearchPanel");
            if (research != null)
                go.transform.SetSiblingIndex(research.GetSiblingIndex() + 1);
            var panel = go.AddComponent<SpaceStation.UI.RosterPanel>();
            var so = new SerializedObject(panel);
            so.FindProperty("_station").objectReferenceValue = Object.FindFirstObjectByType<SpaceStation.Building.StationController>();
            so.FindProperty("_font").objectReferenceValue = hud.GetComponentInChildren<TMPro.TMP_Text>(true).font;
            so.FindProperty("_fillSprite").objectReferenceValue = HudArtBuilder.Fill;
            so.FindProperty("_frameSprite").objectReferenceValue = HudArtBuilder.Frame;
            so.FindProperty("_buttonSprite").objectReferenceValue = HudArtBuilder.Button;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ModuleData Module(string name) => AssetDatabase.LoadAssetAtPath<ModuleData>(ModuleFolder + name + ".asset");

        public static ResidentConfig CreateData()
        {
            var data = AssetDatabase.LoadAssetAtPath<ResidentConfig>(DataPath);
            if (data == null)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DataPath));
                data = ScriptableObject.CreateInstance<ResidentConfig>();
                AssetDatabase.CreateAsset(data, DataPath);
            }
            var traits = new[]
            {
                // 능력 (정거장 전체, 1명당 비율·합계 상한)
                // 일터 특성은 직함도 줌 (11-11d: "두부 기관사", 직함 없는 주민은 "대원")
                new TraitDefinition(ResidentTrait.Technician, "기술자", "수리 시간 -4%/명 (최대 -20%)", true, 0.04f, 0.20f).WithTitle("기관사"),
                new TraitDefinition(ResidentTrait.Scientist, "과학자", "연구 속도 +4%/명 (최대 +20%)", true, 0.04f, 0.20f).WithTitle("박사"),
                new TraitDefinition(ResidentTrait.Gardener, "원예가", "식량 생산 +3%/명 (최대 +15%)", true, 0.03f, 0.15f).WithTitle("원예사"),
                new TraitDefinition(ResidentTrait.Mechanic, "정비공", "노후 속도 -3%/명 (최대 -15%)", true, 0.03f, 0.15f).WithTitle("정비사"),
                // 성격 (만족도 상한 보정, 합계 상한)
                new TraitDefinition(ResidentTrait.Optimist, "낙천가", "만족도 상한 +2/명 (최대 +10)", true, 2f, 10f)
                    .WithConflict(ResidentTrait.Complainer),
                new TraitDefinition(ResidentTrait.Complainer, "불평꾼", "만족도 상한 -2/명 (최대 -10) · 먼저 떠남", false, -2f, 10f, 0.8f),
                // 소비 (본인)
                new TraitDefinition(ResidentTrait.BigEater, "대식가", "본인 식량 소비 +50%", false, 0.5f, 0f, 0.8f)
                    .WithConflict(ResidentTrait.LightEater),
                new TraitDefinition(ResidentTrait.LightEater, "소식가", "본인 식량·물 소비 -30%", true, -0.3f, 0f, 0.8f),
                // 환경 (집 주변)
                new TraitDefinition(ResidentTrait.Sociable, "사교적", "여가 시설(휴게실·회전 링) 범위 안 집이면 상한 +2, 밖이면 -1 (최대 ±10)", true, 2f, 10f,
                    1f, -1f),
                new TraitDefinition(ResidentTrait.GravityLover, "중력 애호가", "회전 링 범위 안 집이면 상한 +2 (최대 +10)", true, 2f, 10f, 0.7f),
                new TraitDefinition(ResidentTrait.RadiationSensitive, "방사선 민감", "핵융합로 옆 집이면 상한 -3 (최대 -15) · 먼저 떠남", false, -3f, 15f, 0.7f)
                    .WithConflict(ResidentTrait.RadiationTolerant),
                new TraitDefinition(ResidentTrait.RadiationTolerant, "방사선 내성", "핵융합로 옆 집에 살아도 불만 없음 · 그런 집에 먼저 배정", true, 0f, 0f, 0.6f),
                new TraitDefinition(ResidentTrait.NoiseSensitive, "소음 민감", "제련소 옆 집이면 상한 -3 (최대 -15)", false, -3f, 15f, 0.8f),
                new TraitDefinition(ResidentTrait.NatureLover, "자연 애호가", "수경 농장 옆 집이면 상한 +2 (최대 +10)", true, 2f, 10f, 0.8f),
            };
            // 11-11d 동물 주민 (2026-10-10 사용자 결정): 귀여운 짧은 이름 + 직함 ("두부 기관사"), 성 없음.
            //   예전 국제 승무원 이름(엘레나 박 등)은 사람 모델용 — 세이브에 남은 주민은 그 이름 그대로
            string[] given =
            {
                "보리", "두부", "모카", "호두", "콩이", "마루", "라떼", "쿠키", "솜이", "치즈", "모찌", "감자", "밤이", "율무", "자두",
                "망고", "우유", "버터", "꿀이", "단추", "구름", "별이", "달이", "토리", "몽이", "뭉치", "초코", "레몬", "사과", "땅콩",
                "찹쌀", "호빵", "만두", "떡이", "설기", "팥이", "녹두", "나리", "하루", "봄이", "보송", "말랑", "방울", "꼬마", "루루",
                "코코", "미미", "나나", "두리", "또리", "아리", "다롱", "꼬미", "퐁이", "치치", "모모", "파이", "젤리", "푸딩", "와플",
                "크림", "솔이", "들깨", "참깨", "수수", "귤이", "유자", "매실", "오디", "앵두", "키위", "도토리", "바닐라", "인절미", "누룽지",
            };
            data.EditorSet(traits, 0.35f,
                new[] { Module("MD_FusionReactor") }, new[] { Module("MD_Refinery") }, new[] { Module("MD_Farm") },
                given, new string[0], "대원");
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }
    }
}
