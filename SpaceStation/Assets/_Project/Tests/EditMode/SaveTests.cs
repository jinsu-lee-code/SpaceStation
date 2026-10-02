using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Save;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>Phase 6 세이브/로드: 상태 캡처 → JSON → 새 시뮬레이션에 복원, 없어진 데이터 건너뛰기, 파일 슬롯·버전.</summary>
    public class SaveTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private ModuleData _core, _lab, _block, _habitat;
        private StationGradeConfig _grades;
        private ResearchCategoryData _catA, _catB;
        private SolarStormEventData _storm;
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 4;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Water, 120), R(ResourceType.Food, 100), R(ResourceType.Metal, 180));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 200));
            b.FindProperty("_baseRepairSlots").intValue = 1;
            b.FindProperty("_repairDuration").floatValue = 30f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 120f;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", removable: false);
            _lab = Module("MD_TestLab");
            var l = new SerializedObject(_lab);
            l.FindProperty("_researchSlots").intValue = 1;
            l.ApplyModifiedPropertiesWithoutUndo();
            _block = Module("MD_TestBlock");
            _habitat = Module("MD_TestHabitat");
            var h = new SerializedObject(_habitat);
            h.FindProperty("_housingCapacity").intValue = 6;
            h.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 2;
            list.GetArrayElementAtIndex(1).FindPropertyRelative("_minPopulation").intValue = 999;
            var unlocks = list.GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks");
            var modules = new[] { _lab, _block, _habitat };
            unlocks.arraySize = modules.Length;
            for (int i = 0; i < modules.Length; i++)
                unlocks.GetArrayElementAtIndex(i).objectReferenceValue = modules[i];
            g.ApplyModifiedPropertiesWithoutUndo();

            _catA = Category("RC_TestA");
            _catB = Category("RC_TestB");
            _storm = Create<SolarStormEventData>();
            _storm.name = "EV_TestStorm";
            var s = new SerializedObject(_storm);
            s.FindProperty("_weight").floatValue = 0f; // 랜덤으로는 안 나옴
            s.FindProperty("_duration").floatValue = 60f;
            s.FindProperty("_powerSupplyMultiplier").floatValue = 0.5f;
            s.ApplyModifiedPropertiesWithoutUndo();

            _dir = Path.Combine(Path.GetTempPath(), "SpaceStationSaveTests");
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
            SaveService.Directory = _dir;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
            SaveService.Directory = null;
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        [Test]
        public void RoundTrip_RestoresSimulationState()
        {
            var sim = Sim();
            Assert.IsTrue(sim.Grid.TryPlace(_block, Vector3Int.left, 0, out var block));
            Assert.IsTrue(sim.Grid.TryPlace(_lab, Vector3Int.right, 0, out var damagedLab));
            Assert.IsTrue(sim.Grid.TryPlace(_habitat, Vector3Int.forward, 0, out var habitat));
            Assert.IsTrue(sim.Grid.TryPlace(_lab, Vector3Int.back, 0, out _));
            sim.Durability.ApplyImpact(block, 30f);
            sim.Research.SetLevel(_catA, 1);
            Assert.AreEqual(ResearchStartResult.Ok, sim.TryStartResearch(_catB));
            sim.Damage.Damage(damagedLab);
            sim.Damage.Damage(habitat);
            Assert.AreEqual(RepairResult.Started, sim.TryRepair(damagedLab));
            Assert.AreEqual(RepairResult.Queued, sim.TryRepair(habitat), "슬롯 1개 → 대기");
            sim.Events.Trigger(_storm);
            sim.Automation.Threshold = 70f;
            sim.Automation.AutoRebuild = false;
            for (int i = 0; i < 7; i++)
                sim.Tick(1f);

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var loaded = Sim();
            int missing = StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));

            Assert.AreEqual(0, missing);
            Assert.AreEqual(sim.Grid.ModuleCount, loaded.Grid.ModuleCount);
            Assert.AreEqual(sim.ElapsedSeconds, loaded.ElapsedSeconds, Eps);
            foreach (ResourceType t in System.Enum.GetValues(typeof(ResourceType)))
                if (ResourceSimulation.IsStock(t))
                    Assert.AreEqual(sim.Resources.GetStock(t), loaded.Resources.GetStock(t), Eps, t.ToString());
            Assert.AreEqual(sim.Resources.Population, loaded.Resources.Population);
            Assert.AreEqual(sim.Population.Satisfaction, loaded.Population.Satisfaction, Eps);
            Assert.AreEqual(sim.Population.OvercrowdedLossTimer, loaded.Population.OvercrowdedLossTimer, Eps);

            Assert.IsTrue(loaded.Grid.TryGetModule(Vector3Int.left, out var lBlock));
            Assert.AreSame(_block, lBlock.Data);
            loaded.Durability.TryGetInfo(lBlock, out var d);
            Assert.AreEqual(70f, d.Current, Eps, "내구도");

            loaded.Grid.TryGetModule(Vector3Int.right, out var lLab);
            loaded.Grid.TryGetModule(Vector3Int.forward, out var lHab);
            Assert.IsTrue(loaded.Damage.TryGetInfo(lLab, out var repairing) && repairing.IsRepairing);
            sim.Damage.TryGetInfo(damagedLab, out var originalRepair);
            Assert.AreEqual(originalRepair.RepairRemaining, repairing.RepairRemaining, Eps, "남은 수리 시간");
            Assert.AreEqual(1, loaded.Damage.GetQueuePosition(lHab), "대기열 유지");
            sim.Damage.TryGetInfo(habitat, out var originalQueued);
            loaded.Damage.TryGetInfo(lHab, out var queued);
            Assert.AreEqual(originalQueued.TimeUntilDestroyed, queued.TimeUntilDestroyed, Eps, "파괴 타이머");

            Assert.AreEqual(1, loaded.Events.ActiveEvents.Count);
            Assert.AreEqual(sim.Events.ActiveEvents[0].Remaining, loaded.Events.ActiveEvents[0].Remaining, Eps);
            Assert.AreEqual(0.5f, loaded.Resources.PowerSupplyMultiplier, Eps, "진행 중 폭풍 효과");
            Assert.AreEqual(sim.Events.TimeUntilNext, loaded.Events.TimeUntilNext, Eps);
            Assert.AreEqual(sim.Session.EventsExperienced, loaded.Session.EventsExperienced);

            Assert.AreEqual(1, loaded.Research.GetLevel(loaded.Research.Categories[0]));
            Assert.AreEqual(1, loaded.Research.Projects.Count);
            Assert.AreEqual(sim.Research.Projects[0].Progress, loaded.Research.Projects[0].Progress, Eps, "연구 진행률");
            Assert.AreEqual(70f, loaded.Automation.Threshold, Eps);
            Assert.IsFalse(loaded.Automation.AutoRebuild);

            // 이어서 진행해도 같은 결과 (난수를 쓰지 않는 구간)
            sim.Tick(1f);
            loaded.Tick(1f);
            Assert.AreEqual(sim.Resources.GetStock(ResourceType.Oxygen), loaded.Resources.GetStock(ResourceType.Oxygen), Eps);
            Assert.AreEqual(sim.Research.Projects[0].Progress, loaded.Research.Projects[0].Progress, Eps);
        }

        [Test]
        public void Restore_SkipsMissingData()
        {
            var sim = Sim();
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out var block);
            sim.Grid.TryPlace(_habitat, Vector3Int.right, 0, out _);
            sim.Damage.Damage(block);
            var state = StationStateSerializer.Capture(sim);
            state.Modules[0].Data = "MD_Removed"; // 업데이트로 없어진 모듈 → 그 모듈의 파손도 건너뜀
            state.ActiveEvents.Add(new EventState { Data = "EV_Removed", Duration = 10f, Remaining = 5f });
            state.ResearchLevels.Add(new NamedValue("RC_Removed", 2f));

            var loaded = Sim();
            int missing = StationStateSerializer.Restore(loaded, state);
            Assert.AreEqual(4, missing, "모듈 1 + 파손 1 + 이벤트 1 + 연구 1");
            Assert.AreEqual(2, loaded.Grid.ModuleCount, "코어 + 거주");
            Assert.AreEqual(0, loaded.Damage.DamagedCount);
        }

        [Test]
        public void SaveService_Slots_Version_MostRecent()
        {
            Assert.AreEqual(SaveSlotStatus.Empty, SaveService.Describe("slot1").Status);
            Assert.IsNull(SaveService.MostRecentSlot());

            var older = new SaveFile { Meta = new SaveMeta { SavedAtTicks = 100, GradeName = "초소형" } };
            var newer = new SaveFile { Meta = new SaveMeta { SavedAtTicks = 200, GradeName = "소형" } };
            Assert.IsTrue(SaveService.Write("slot1", older, null));
            Assert.IsTrue(SaveService.Write(SaveService.AutoSlot, newer, new byte[] { 1, 2, 3 }));
            Assert.AreEqual(SaveService.AutoSlot, SaveService.MostRecentSlot());
            Assert.AreEqual(SaveSlotStatus.Ok, SaveService.TryRead("slot1", out var read));
            Assert.AreEqual("초소형", read.Meta.GradeName);
            Assert.IsNotNull(SaveService.Describe(SaveService.AutoSlot).ThumbnailPath);
            Assert.IsTrue(SaveService.Write("slot1", newer, null), "덮어쓰기");

            File.WriteAllText(SaveService.JsonPath("slot2"), JsonUtility.ToJson(new SaveFile { Version = SaveService.CurrentVersion + 1 }));
            Assert.AreEqual(SaveSlotStatus.Incompatible, SaveService.Describe("slot2").Status, "더 새로운 버전");
            File.WriteAllText(SaveService.JsonPath("slot3"), "{ broken");
            Assert.AreEqual(SaveSlotStatus.Incompatible, SaveService.Describe("slot3").Status, "깨진 파일");

            SaveService.Delete(SaveService.AutoSlot);
            Assert.AreEqual(SaveSlotStatus.Empty, SaveService.Describe(SaveService.AutoSlot).Status);
            Assert.IsFalse(File.Exists(SaveService.ThumbnailPath(SaveService.AutoSlot)));
            Assert.AreEqual("slot1", SaveService.MostRecentSlot());
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim()
        {
            return new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData> { _storm },
                Random01 = () => 0.5f,
                ResearchCategories = new[] { _catA, _catB },
            });
        }

        private ResearchCategoryData Category(string name)
        {
            var category = Create<ResearchCategoryData>();
            category.name = name;
            var levels = new List<ResearchLevel>();
            for (int i = 0; i < 2; i++)
                levels.Add(new ResearchLevel { StartCost = new List<ResourceAmount>(), PowerDemand = 0f, Duration = 60f, Description = "Lv" });
            category.EditorSet(ResearchCategory.Maintenance, name, "repair", levels);
            return category;
        }

        private ModuleData Module(string name, bool removable = true)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private static ResourceAmount R(ResourceType t, float a) => new ResourceAmount(t, a);

        private static void Fill(SerializedProperty list, params ResourceAmount[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Type").enumValueIndex = (int)values[i].Type;
                e.FindPropertyRelative("Amount").floatValue = values[i].Amount;
            }
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
