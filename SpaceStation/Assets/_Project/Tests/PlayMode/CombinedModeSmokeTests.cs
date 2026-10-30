using System.Collections;
using System.IO;
using NUnit.Framework;
using SpaceStation.Building;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Interior;
using SpaceStation.Save;
using SpaceStation.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SpaceStation.Tests
{
    /// <summary>
    /// 합본(combined) 검수 빌드 실행 확인 (사용자 2026-10-30 "하나의 합본으로"): 메인 메뉴 두 모드 버튼 · 캠페인 새 게임 · 엔드리스 새 게임 ·
    /// 체험용 저장 불러오기 + 1인칭 내부. 판마다 게임 시간 3분을 직접 돌려, 그동안 오류 로그가 하나라도 나오면 실패 (시험 틀 기본 동작).
    /// 저장 폴더는 임시 폴더로 (자동 저장이 실제 저장 · 빌드용 체험 저장을 덮지 않게).
    /// </summary>
    public class CombinedModeSmokeTests
    {
        private const string CampaignPath = "Assets/_Project/Data/Modes/GM_Campaign.asset";
        private const string EndlessPath = "Assets/_Project/Data/Modes/GM_Endless.asset";
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ss_smoke_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            SaveService.Directory = _dir;
            GameStartOptions.PendingLoad = null;
            GameStartOptions.Difficulty = null;
            GameStartOptions.Mode = null;
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.Directory = null;
            GameStartOptions.PendingLoad = null;
            GameStartOptions.Mode = null;
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        [UnityTest]
        public IEnumerator MainMenu_ShowsCampaignAndEndless()
        {
            yield return Load(SceneNames.MainMenu, 3f);
            bool campaign = false, endless = false;
            foreach (var t in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
            {
                if (!t.isActiveAndEnabled)
                    continue;
                campaign |= t.text.Contains("캠페인");
                endless |= t.text.Contains("엔드리스");
            }
            Assert.IsTrue(campaign, "메인 메뉴에 [캠페인]");
            Assert.IsTrue(endless, "메인 메뉴에 [엔드리스]");
        }

        [UnityTest]
        public IEnumerator Campaign_NewGame_AttachesChapterSystems()
        {
            GameStartOptions.Mode = Mode(CampaignPath);
            GameStartOptions.Difficulty = GameStartOptions.Mode.FixedDifficulty;
            yield return Load(SceneNames.Game, 4f);
            var host = Object.FindFirstObjectByType<SimulationHost>();
            Assert.IsTrue(host.IsCampaign);
            Assert.IsNotNull(host.Simulation.Chapters, "장 틀");
            Assert.IsNotNull(host.Simulation.GetSystem<CorePersonalitySystem>(), "코어 성격");
            Assert.IsNull(host.Simulation.GetSystem<UnlockChoiceSystem>(), "엔드리스 해금 선택은 없음");
            yield return Run(host, 180);
        }

        [UnityTest]
        public IEnumerator Endless_NewGame_AttachesTycoonSystems()
        {
            GameStartOptions.Mode = Mode(EndlessPath);
            yield return Load(SceneNames.Game, 4f);
            var host = Object.FindFirstObjectByType<SimulationHost>();
            Assert.IsFalse(host.IsCampaign);
            Assert.IsNull(host.Simulation.Chapters, "장 틀 없음 = 등급 방식");
            Assert.IsNull(host.Simulation.GetSystem<CorePersonalitySystem>(), "캠페인 코어 성격은 없음");
            Assert.IsNotNull(host.Simulation.GetSystem<UnlockChoiceSystem>(), "해금 선택");
            yield return Run(host, 180);
        }

        [UnityTest]
        public IEnumerator DemoSave_Loads_AndInteriorOpens()
        {
            File.Copy(Path.Combine(Application.streamingAssetsPath, "DemoSaves", "slot1.json"), Path.Combine(_dir, "slot1.json"));
            Assert.AreEqual(SaveSlotStatus.Ok, SaveService.TryRead("slot1", out var file));
            GameStartOptions.PendingLoad = file;
            yield return Load(SceneNames.Game, 5f);
            var host = Object.FindFirstObjectByType<SimulationHost>();
            Assert.IsNotNull(host.LoadedSave);
            Assert.IsFalse(host.IsCampaign, "체험용 저장 = 엔드리스");
            Assert.AreEqual(0, host.LoadMissingCount, "불러오지 못한 항목 없음");
            var station = Object.FindFirstObjectByType<StationController>();
            Assert.Greater(station.Grid.Modules.Count, 100);
            yield return Run(host, 60);

            ModuleInstance target = null;
            foreach (var m in station.Grid.Modules)
                if (m.Data != null && m.Data.name == "MD_Evo_EmergencyCenter")
                    target = m;
            Assert.IsNotNull(target, "체험용 저장에 비상 대응 센터");
            var mode = Object.FindFirstObjectByType<InteriorMode>();
            mode.Enter(target);
            float until = Time.realtimeSinceStartup + 8f;
            while (!mode.IsInside && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.IsTrue(mode.IsInside, "1인칭 내부로 들어감");
            yield return new WaitForSecondsRealtime(3f);
        }

        private static GameModeData Mode(string path)
        {
#if UNITY_EDITOR
            var mode = AssetDatabase.LoadAssetAtPath<GameModeData>(path);
            Assert.IsNotNull(mode, path);
            return mode;
#else
            return null;
#endif
        }

        private static IEnumerator Load(string scene, float settle)
        {
            var op = SceneManager.LoadSceneAsync(scene);
            while (!op.isDone)
                yield return null;
            yield return new WaitForSecondsRealtime(settle);
        }

        /// <summary>게임 시간 seconds초를 직접 돌림 (화면이 따라오게 10틱마다 한 프레임).</summary>
        private static IEnumerator Run(SimulationHost host, int seconds)
        {
            for (int i = 0; i < seconds; i++)
            {
                host.Simulation.Tick(1f);
                if (i % 10 == 9)
                    yield return null;
            }
            yield return new WaitForSecondsRealtime(1f);
        }
    }
}
