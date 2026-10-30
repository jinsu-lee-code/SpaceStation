using System.IO;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Save;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>합본(combined) 검수 빌드: 두 모드 데이터 · 체험용 저장 (사용자 2026-10-30).</summary>
    public class CombinedBuildTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ss_combined_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_dir, "src"));
            Directory.CreateDirectory(Path.Combine(_dir, "dst"));
        }

        [TearDown]
        public void TearDown()
        {
            SaveService.Directory = null;
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        [Test]
        public void Seed_CopiesOnlyEmptySlots_WithThumbnail()
        {
            string src = Path.Combine(_dir, "src"), dst = Path.Combine(_dir, "dst");
            File.WriteAllText(Path.Combine(src, "slot1.json"), "demo1");
            File.WriteAllText(Path.Combine(src, "slot1.png"), "png1");
            File.WriteAllText(Path.Combine(src, "slot2.json"), "demo2");
            File.WriteAllText(Path.Combine(dst, "slot2.json"), "mine");   // 이미 있는 저장은 그대로

            Assert.AreEqual(1, DemoSaveSeeder.Seed(src, dst));
            Assert.AreEqual("demo1", File.ReadAllText(Path.Combine(dst, "slot1.json")));
            Assert.AreEqual("png1", File.ReadAllText(Path.Combine(dst, "slot1.png")));
            Assert.AreEqual("mine", File.ReadAllText(Path.Combine(dst, "slot2.json")));
            Assert.AreEqual(0, DemoSaveSeeder.Seed(src, dst), "두 번째는 넣을 게 없음");
            Assert.AreEqual(0, DemoSaveSeeder.Seed(Path.Combine(_dir, "none"), dst), "원본 폴더가 없으면 0");
        }

        [Test]
        public void DemoSave_InStreamingAssets_IsReadableEndless()
        {
            SaveService.Directory = Path.Combine(Application.streamingAssetsPath, "DemoSaves");
            Assert.AreEqual(SaveSlotStatus.Ok, SaveService.TryRead("slot1", out var file));
            Assert.AreEqual("endless", file.Meta.Mode);
            Assert.AreEqual(529, file.Meta.Population);
            Assert.IsNotNull(file.Station);
        }

        [Test]
        public void ModeAssets_HaveStableIds()
        {
            var campaign = AssetDatabase.LoadAssetAtPath<GameModeData>("Assets/_Project/Data/Modes/GM_Campaign.asset");
            var endless = AssetDatabase.LoadAssetAtPath<GameModeData>("Assets/_Project/Data/Modes/GM_Endless.asset");
            Assert.IsNotNull(campaign);
            Assert.IsNotNull(endless);
            Assert.AreEqual(Simulation.SimulationHost.CampaignModeId, campaign.Id);
            Assert.AreEqual("endless", endless.Id);
            Assert.IsNotNull(campaign.FixedDifficulty, "캠페인 = 난이도 하나");
            Assert.IsNull(endless.FixedDifficulty, "엔드리스 = 이지 · 노멀 · 하드");
        }
    }
}
