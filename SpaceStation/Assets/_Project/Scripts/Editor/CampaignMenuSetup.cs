using System.IO;
using SpaceStation.Data;
using SpaceStation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 13-7d 서사판 = 캠페인 모드 메인 메뉴 (사용자 결정 2026-10-25). 공통 고리(main M-44)에 값만 채운다 (SPLIT.md 6번):
    /// 게임 모드 <c>GM_Campaign</c> (단일 난이도 노멀 · 코어 기동 안내 켜기/끄기 줄 · 로딩 "1장 · 깨어남"),
    /// 메인 메뉴 씬 모드 목록 = [캠페인] · 버전 줄 · 배경에 꺼져 가는 지구 (정거장 오른쪽 뒤).
    /// </summary>
    public static class CampaignMenuSetup
    {
        public const string ModePath = "Assets/_Project/Data/Modes/GM_Campaign.asset";
        public const string VersionLabel = "v0.13 · 캠페인 1~3장";
        private const string MenuScene = "Assets/_Project/Scenes/MainMenu.unity";
        private const string NormalPath = "Assets/_Project/Data/Difficulty/DIFF_Normal.asset";

        [MenuItem("SpaceStation/Campaign/Setup Main Menu")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[CampaignMenuSetup] 플레이 중에는 돌리지 않음");
                return;
            }
            CreateMode();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            string previous = EditorSceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
            // 씬을 열면 쓰지 않는 에셋이 메모리에서 내려가 앞서 잡은 참조가 비어 저장됐음 → 연 뒤 경로로 다시
            var mode = AssetDatabase.LoadAssetAtPath<GameModeData>(ModePath);
            var menu = Object.FindFirstObjectByType<MainMenuController>();
            if (menu == null)
            {
                Debug.LogError("[CampaignMenuSetup] 메인 메뉴 씬에 MainMenuController 없음");
                return;
            }
            var so = new SerializedObject(menu);
            var modes = so.FindProperty("_modes");
            modes.arraySize = 1;
            modes.GetArrayElementAtIndex(0).objectReferenceValue = mode;
            so.FindProperty("_versionLabel").stringValue = VersionLabel;
            so.ApplyModifiedPropertiesWithoutUndo();
            AddEarth();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!string.IsNullOrEmpty(previous) && previous != MenuScene)
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            Debug.Log("[CampaignMenuSetup] 캠페인 모드 · 메인 메뉴 모드 목록 · 버전 줄 · 배경 지구 완료");
        }

        private static GameModeData CreateMode()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ModePath));
            var mode = AssetDatabase.LoadAssetAtPath<GameModeData>(ModePath);
            if (mode == null)
            {
                mode = ScriptableObject.CreateInstance<GameModeData>();
                AssetDatabase.CreateAsset(mode, ModePath);
            }
            string first = "1장 · 깨어남";
            foreach (string guid in AssetDatabase.FindAssets("t:ChapterConfig"))
            {
                var config = AssetDatabase.LoadAssetAtPath<ChapterConfig>(AssetDatabase.GUIDToAssetPath(guid));
                var c = config != null && config.Count > 0 ? config.Chapters[0] : null;
                if (c != null)
                {
                    first = string.IsNullOrEmpty(c.Subtitle) ? c.DisplayName : $"{c.DisplayName} · {c.Subtitle}"; // ChapterSystem.ProgressText와 같은 꼴
                    break;
                }
            }
            mode.EditorSet("campaign", "캠페인", "1~3장 · 난이도 하나 · 무너지면 위기 직전부터 다시",
                AssetDatabase.LoadAssetAtPath<DifficultyPreset>(NormalPath), true, "코어 기동 안내 받기", first, "캠페인");
            EditorUtility.SetDirty(mode);
            AssetDatabase.SaveAssets();
            return mode;
        }

        /// <summary>
        /// 게임 씬과 같은 지구를 화면 오른쪽 위에 고정 (메뉴 카메라가 정거장을 천천히 돌아서 방향으로 두면 화면을 가로질러 흘렀음 ·
        /// 왼쪽은 메뉴 패널 · 가운데 아래는 정거장).
        /// </summary>
        private static void AddEarth()
        {
            foreach (var old in Object.FindObjectsByType<EarthView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(old.gameObject);
            var go = new GameObject("EarthView");
            var earth = go.AddComponent<EarthView>();
            var so = new SerializedObject(earth);
            so.FindProperty("_model").objectReferenceValue = ChapterSetup.EarthModel(out var material);
            so.FindProperty("_material").objectReferenceValue = material;
            so.FindProperty("_screenAnchored").boolValue = true;
            so.FindProperty("_screenPoint").vector2Value = new Vector2(0.86f, 0.78f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
