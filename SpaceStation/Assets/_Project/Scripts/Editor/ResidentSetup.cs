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
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ResidentSetup] 특성 {data.Traits.Count}종, 이름 {data.GivenNames.Count}×{data.Surnames.Count}");
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
                new TraitDefinition(ResidentTrait.Technician, "기술자", "수리 시간 -4%/명 (최대 -20%)", true, 0.04f, 0.20f),
                new TraitDefinition(ResidentTrait.Scientist, "과학자", "연구 속도 +4%/명 (최대 +20%)", true, 0.04f, 0.20f),
                new TraitDefinition(ResidentTrait.Gardener, "원예가", "식량 생산 +3%/명 (최대 +15%)", true, 0.03f, 0.15f),
                new TraitDefinition(ResidentTrait.Mechanic, "정비공", "노후 속도 -3%/명 (최대 -15%)", true, 0.03f, 0.15f),
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
            string[] given =
            {
                "엘레나", "라지브", "민서", "아마라", "카이", "소피아", "다니엘", "유키", "마테오", "하나", "이반", "레일라", "오마르", "지우", "클라라",
                "타오", "니콜라", "아이샤", "루카스", "미라", "케이", "사라", "알렉세이", "나디아", "준호", "프리야", "토마스", "린", "에밀", "자라",
                "하빕", "도윤", "에스더", "파블로", "아키라", "말리아", "빅토르", "세린", "노아", "이네스",
            };
            string[] surnames =
            {
                "박", "오카모토", "카르도소", "응우옌", "코왈스키", "멘사", "이바노바", "가르시아", "첸", "샤르마", "뮐러", "오코너", "김", "다실바",
                "하산", "로시", "탄", "노박", "아데예미", "모로", "피셔", "야마다", "로페스", "베르그", "칸", "레예스", "소렌센", "이", "에르난데스",
                "볼코프", "사토", "마르티네즈", "오웬스", "카푸르", "린드", "아콰", "페트로프", "최", "브라운", "하야시",
            };
            data.EditorSet(traits, 0.35f,
                new[] { Module("MD_FusionReactor") }, new[] { Module("MD_Refinery") }, new[] { Module("MD_Farm") },
                given, surnames);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }
    }
}
