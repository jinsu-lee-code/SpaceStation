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
    /// </summary>
    public static class BuildSetup
    {
        public const string CompanyName = "BinaryCompany";
        public const string ProductName = "Another Earth";
        public const string Version = "0.10.0"; // Phase 10 (2026-10-04)
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

        [MenuItem("SpaceStation/Build/Build Windows")]
        public static void BuildWindowsMenu() => BuildWindows(false);

        /// <returns>요약 (성공 여부·크기·시간·오류 수)</returns>
        public static string BuildWindows(bool development)
        {
            ApplyPlayerSettings();
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
