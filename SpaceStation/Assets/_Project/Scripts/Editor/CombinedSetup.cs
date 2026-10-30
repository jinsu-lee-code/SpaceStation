using System.IO;
using SpaceStation.Data;
using SpaceStation.Simulation;
using SpaceStation.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 합본(combined 브랜치, 사용자 2026-10-30 "하나의 합본으로") — 한 게임 두 모드 (SPLIT.md 6번 고리에 값만 채움):
    /// 엔드리스 모드 <c>GM_Endless</c> (이지 · 노멀 · 하드 · 공통 튜토리얼 줄) + 서사판 캠페인 모드 <c>GM_Campaign</c>(CampaignMenuSetup),
    /// 메인 메뉴 모드 목록 = [캠페인, 엔드리스] · 버전 줄, 게임 씬 <see cref="SimulationHost"/> 모드 목록 · 씬 단독 실행 기본 = 엔드리스, 제품 이름 하나.
    /// 순서: 서사판 메뉴 SpaceStation/Chapters/Setup Greybox (게임 씬 캠페인 장치) → 이 메뉴.
    /// </summary>
    public static class CombinedSetup
    {
        public const string EndlessPath = "Assets/_Project/Data/Modes/GM_Endless.asset";
        private const string MenuScene = "Assets/_Project/Scenes/MainMenu.unity";
        private const string GameScene = "Assets/_Project/Scenes/Main.unity";

        /// <summary>배치 모드 입구 (-executeMethod SpaceStation.Editor.CombinedSetup.SetupAll): 게임 씬 캠페인 장치 → 모드 · 메뉴.</summary>
        public static void SetupAll()
        {
            ChapterSetup.Setup();
            Setup();
            AssetDatabase.SaveAssets();
        }

        [MenuItem("SpaceStation/Combined/Setup Modes")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[CombinedSetup] 플레이 중에는 돌리지 않음");
                return;
            }
            CampaignMenuSetup.Setup(); // GM_Campaign · 메인 메뉴 배경 지구 (모드 목록은 아래에서 둘로)
            CreateEndless();
            string previous = EditorSceneManager.GetActiveScene().path;

            // 메인 메뉴: [캠페인, 엔드리스] · 버전 줄 (씬을 연 뒤 경로로 다시 — 연 씬이 앞서 잡은 참조를 비움)
            var menuScene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Single);
            var modes = Modes();
            var menu = Object.FindFirstObjectByType<MainMenuController>();
            if (menu == null)
            {
                Debug.LogError("[CombinedSetup] 메인 메뉴 씬에 MainMenuController 없음");
                return;
            }
            var mso = new SerializedObject(menu);
            SetArray(mso.FindProperty("_modes"), modes);
            mso.FindProperty("_versionLabel").stringValue = BuildSetup.VersionLabel;
            mso.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(menuScene);
            EditorSceneManager.SaveScene(menuScene);

            // 게임 씬: 저장 모드 Id → 모드 · 씬 단독 실행 = 엔드리스
            var gameScene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
            modes = Modes();
            var host = Object.FindFirstObjectByType<SimulationHost>();
            var hso = new SerializedObject(host);
            SetArray(hso.FindProperty("_modes"), modes);
            hso.FindProperty("_defaultMode").objectReferenceValue = modes[1];
            hso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(host);
            EditorSceneManager.MarkSceneDirty(gameScene);
            EditorSceneManager.SaveScene(gameScene);

            BuildSetup.ApplyPlayerSettings(); // 제품 이름 "Another Earth" · 버전
            if (!string.IsNullOrEmpty(previous) && previous != GameScene)
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            Debug.Log("[CombinedSetup] 모드 [캠페인, 엔드리스] · 메인 메뉴 · 게임 씬 모드 목록 · 제품 이름 완료");
        }

        private static GameModeData[] Modes() => new[]
        {
            AssetDatabase.LoadAssetAtPath<GameModeData>(CampaignMenuSetup.ModePath),
            AssetDatabase.LoadAssetAtPath<GameModeData>(EndlessPath),
        };

        private static void CreateEndless()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EndlessPath));
            var mode = AssetDatabase.LoadAssetAtPath<GameModeData>(EndlessPath);
            if (mode == null)
            {
                mode = ScriptableObject.CreateInstance<GameModeData>();
                AssetDatabase.CreateAsset(mode, EndlessPath);
            }
            mode.EditorSet("endless", "엔드리스", "끝없이 키우는 경영 · 등급마다 해금 선택 · 커질수록 위협도 커짐",
                null, true, "튜토리얼 받기", "새 정거장", "");
            EditorUtility.SetDirty(mode);
            AssetDatabase.SaveAssets();
        }

        private static void SetArray(SerializedProperty list, GameModeData[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
