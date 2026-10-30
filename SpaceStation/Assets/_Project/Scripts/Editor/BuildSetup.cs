using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 배포 준비 (메뉴 SpaceStation/Build/...).
    /// - Apply Player Settings: 제작사 "BinaryCompany", 제품 이름 "Another Earth", 버전, Windows 64비트, 테두리 없는 전체 화면(모니터 해상도), 창 크기 조절 허용,
    ///   백그라운드 실행(일시정지는 게임 설정 "창 비활성 시 자동 일시정지"가 결정)
    /// - Build Windows: Builds/Windows/AnotherEarth.exe (Assets 밖). 배포 빌드 = Development Build 꺼짐 → F5 디버그·진단 로그 비활성
    /// 제작사 이름(companyName)은 저장 폴더 경로(LocalLow/{제작사}/{제품})와 PlayerPrefs 레지스트리 경로를 정하므로 바꾸지 않는다.
    /// 타이쿤판(tycoon 브랜치, SPLIT.md S-7)은 제품 이름 "Another Earth Tycoon"으로 서사판과 세이브를 나눴음 →
    /// 합본(combined, 2026-10-30)은 한 게임이므로 다시 "Another Earth" 하나 (모드는 저장마다 SaveMeta.Mode로 구분).
    /// </summary>
    public static class BuildSetup
    {
        public const string CompanyName = "BinaryCompany";
        public const string ProductName = "Another Earth";
        public const string Version = "0.14.0"; // 합본 검수 빌드 (2026-10-30 — 캠페인 1~4장 + 엔드리스)
        /// <summary>메인 메뉴 오른쪽 아래 버전 표시 (11-19: Phase 11 이후에도 "v0.10 · Phase 10"으로 남아 있었음 → 여기 한 곳에서).</summary>
        public const string VersionLabel = "v0.14 · 합본 (캠페인 + 엔드리스)";
        private const string OutputFolder = "Builds/Windows";
        private const string ExeName = "AnotherEarth.exe";

        [MenuItem("SpaceStation/Build/Apply Player Settings")]
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BuildSetup] 플레이어 설정: {CompanyName} / {ProductName} {Version}, 테두리 없는 전체 화면");
        }

        /// <summary>
        /// 11-19: 메인 메뉴 씬의 버전 글자만 <see cref="VersionLabel"/>로 (씬을 다시 만들면 나중에 입힌 테크 스킨이 풀리므로 글자만 고침).
        /// 열려 있던 씬은 그대로 두고 메뉴 씬을 따로 열어 저장한 뒤 닫는다.
        /// </summary>
        public static void ApplyMenuVersionLabel()
        {
            const string menuScene = "Assets/_Project/Scenes/MainMenu.unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(menuScene, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var text in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    {
                        if (text.name != "Version" || text.text == VersionLabel)
                            continue;
                        text.text = VersionLabel;
                        EditorUtility.SetDirty(text);
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                    }
                }
                if (scene.isDirty)
                    UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (UnityEngine.SceneManagement.SceneManager.sceneCount > 1)
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("SpaceStation/Build/Build Windows")]
        public static void BuildWindowsMenu() => BuildWindows(false);

        /// <returns>요약 (성공 여부·크기·시간·오류 수)</returns>
        public static string BuildWindows(bool development)
        {
            ApplyPlayerSettings();
            ApplyMenuVersionLabel();
            string root = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(root, OutputFolder);
            Directory.CreateDirectory(folder);
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled)
                    scenes.Add(s.path);
            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Path.Combine(folder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s2 = report.summary;
            string summary = $"[BuildSetup] {s2.result} · {s2.totalSize / (1024f * 1024f):0.0} MB · {s2.totalTime.TotalSeconds:0}초 · 오류 {s2.totalErrors} · 경고 {s2.totalWarnings} → {options.locationPathName}";
            if (s2.result == BuildResult.Succeeded)
                Debug.Log(summary);
            else
                Debug.LogError(summary);
            return summary;
        }
    }
}
