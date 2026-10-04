using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>Phase 9 튜토리얼: 단계 진행·이벤트 정지, 첫 밤 직전 시간 정지, 부족분 보급, 대본 운석 수리, 저장 복원, 건너뛰기.</summary>
    public class TutorialTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _block, _battery;
        private MeteorEventData _meteor;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Metal, 100));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 200));
            b.FindProperty("_baseRepairSlots").intValue = 1;
            b.FindProperty("_repairDuration").floatValue = 5f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 600f;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 30f;
            b.FindProperty("_eventIntervalMin").floatValue = 30f;
            b.FindProperty("_eventIntervalMax").floatValue = 30f;
            b.FindProperty("_dayNightPeriod").floatValue = 300f;
            b.FindProperty("_dayLength").floatValue = 180f;
            b.FindProperty("_dayNightTransition").floatValue = 10f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 0f, removable: false);
            _block = Module("MD_TestBlock", 40f);
            _battery = Module("MD_TestBattery", 30f);

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            var grade = list.GetArrayElementAtIndex(0);
            grade.FindPropertyRelative("_meteorHitsMin").intValue = 1;
            grade.FindPropertyRelative("_meteorHitsMax").intValue = 1;
            grade.FindPropertyRelative("_eventIntervalMultiplier").floatValue = 1f;
            g.ApplyModifiedPropertiesWithoutUndo();

            _meteor = Create<MeteorEventData>();
            _meteor.name = "EV_TestMeteor";
            var m = new SerializedObject(_meteor);
            m.FindProperty("_weight").floatValue = 0f; // 무작위로는 안 나옴
            m.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void BuildStep_AdvancesWhenBuilt_AndHoldsRandomEvents()
        {
            var sim = Sim();
            var t = sim.StartTutorial(Data(Step(TutorialGoal.Build, false, _block), Step(TutorialGoal.Build, false, _battery)));
            float before = sim.Events.TimeUntilNext;
            for (int i = 0; i < 100; i++)
                sim.Tick(1f);
            Assert.AreEqual(before, sim.Events.TimeUntilNext, Eps, "진행 중엔 무작위 이벤트 타이머 정지");
            Assert.AreEqual(0, t.StepIndex);
            Assert.AreEqual(_block, t.NextModule(t.Current));

            Assert.IsTrue(sim.Grid.TryPlace(_block, Vector3Int.right, 0, out _));
            sim.Tick(1f);
            Assert.AreEqual(1, t.StepIndex);
            Assert.IsTrue(sim.Grid.TryPlace(_battery, Vector3Int.left, 0, out _));
            sim.Tick(1f);
            Assert.IsFalse(t.Active);
            Assert.IsFalse(sim.Events.Held);
        }

        [Test]
        public void BatteryStep_StopsTimeBeforeFirstNight_UntilBuilt()
        {
            var sim = Sim();
            var t = sim.StartTutorial(Data(Step(TutorialGoal.Build, true, _battery)));
            for (int i = 0; i < 200; i++)
                sim.Tick(1f);
            Assert.IsTrue(t.HoldsTime);
            Assert.AreEqual(165f, sim.ElapsedSeconds, Eps, "해질녘(170초) 5초 전에 멈춤");
            Assert.IsTrue(sim.DayNight.IsDay(sim.ElapsedSeconds));

            Assert.IsTrue(sim.Grid.TryPlace(_battery, Vector3Int.right, 0, out _));
            sim.Tick(1f); // 배터리 확인 → 단계 완료
            sim.Tick(1f);
            Assert.IsFalse(t.HoldsTime);
            Assert.Greater(sim.ElapsedSeconds, 165f, "배터리를 지으면 시간 재개");
        }

        [Test]
        public void ShortCost_IsSuppliedAfterDelay()
        {
            var sim = Sim();
            var data = Data(Step(TutorialGoal.Build, false, _block));
            var t = sim.StartTutorial(data);
            sim.Resources.RemoveStock(ResourceType.Metal, 90f); // 금속 10, 필요 40
            IReadOnlyList<ResourceAmount> supplied = null;
            t.Supplied += a => supplied = new List<ResourceAmount>(a);

            for (int i = 0; i < 7; i++)
                sim.Tick(1f);
            Assert.IsNull(supplied, "대기 시간 전에는 보급 없음");
            sim.Tick(1f);
            Assert.IsNotNull(supplied);
            Assert.GreaterOrEqual(sim.Resources.GetStock(ResourceType.Metal), 40f);
            Assert.IsTrue(sim.CanAfford(_block));
        }

        [Test]
        public void RepairStep_FiresScriptedMeteor_AndFinishesWhenRepaired()
        {
            var sim = Sim();
            Assert.IsTrue(sim.Grid.TryPlace(_block, Vector3Int.right, 0, out var block));
            var t = sim.StartTutorial(Data(Step(TutorialGoal.Repair, false)));
            bool finished = false;
            t.Finished += skipped => finished = !skipped;

            sim.Tick(1f);
            sim.Tick(1f);
            Assert.IsTrue(t.MeteorFired, "대기 2초 뒤 운석");
            Assert.IsTrue(sim.Damage.IsDamaged(block), "외곽 모듈 1개 명중");
            Assert.IsTrue(t.Active);

            Assert.AreEqual(RepairResult.Started, sim.TryRepair(block));
            for (int i = 0; i < 6; i++)
                sim.Tick(1f);
            Assert.IsFalse(sim.Damage.IsDamaged(block));
            Assert.IsTrue(finished);
            Assert.IsFalse(sim.Events.Held);
            Assert.GreaterOrEqual(sim.Events.TimeUntilNext, 120f - 1f, "끝난 뒤 유예");
        }

        [Test]
        public void SaveRoundTrip_ResumesStep()
        {
            var data = Data(Step(TutorialGoal.Camera, false), Step(TutorialGoal.Build, false, _block), Step(TutorialGoal.Build, false, _battery));
            var sim = Sim();
            var t = sim.StartTutorial(data);
            t.ReportCamera(TutorialRunner.CameraUse.All);
            sim.Tick(1f);
            Assert.AreEqual(1, t.StepIndex);

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var state = JsonUtility.FromJson<StationState>(json);
            Assert.IsTrue(state.Tutorial.Active);
            var loaded = Sim();
            loaded.StartTutorial(data);
            StationStateSerializer.Restore(loaded, state);
            Assert.IsTrue(loaded.Tutorial.Active);
            Assert.AreEqual(1, loaded.Tutorial.StepIndex);
            Assert.AreEqual(TutorialRunner.CameraUse.All, loaded.Tutorial.CameraDone);
            Assert.IsTrue(loaded.Events.Held);

            // 튜토리얼 없는 저장은 Active false
            var plain = JsonUtility.FromJson<StationState>(JsonUtility.ToJson(StationStateSerializer.Capture(Sim())));
            Assert.IsFalse(plain.Tutorial.Active);
        }

        [Test]
        public void Skip_EndsAndResumesEvents()
        {
            var sim = Sim();
            var t = sim.StartTutorial(Data(Step(TutorialGoal.Camera, false)));
            bool? skipped = null;
            t.Finished += s => skipped = s;
            t.Skip();
            Assert.IsFalse(t.Active);
            Assert.AreEqual(true, skipped);
            Assert.IsFalse(sim.Events.Held);
            float before = sim.Events.TimeUntilNext;
            sim.Tick(1f);
            Assert.AreEqual(before - 1f, sim.Events.TimeUntilNext, Eps);
        }

        // ---------------- 도우미 ----------------

        private static TutorialStep Step(TutorialGoal goal, bool holdBeforeNight, params ModuleData[] modules) =>
            new TutorialStep(goal.ToString(), "", goal, TutorialHighlight.None, holdBeforeNight, modules);

        private TutorialData Data(params TutorialStep[] steps)
        {
            var d = Create<TutorialData>();
            d.SetSteps(steps);
            var so = new SerializedObject(d);
            so.FindProperty("_nightHoldLead").floatValue = 5f;
            so.FindProperty("_supplyDelay").floatValue = 8f;
            so.FindProperty("_supplyMargin").floatValue = 5f;
            so.FindProperty("_meteorDelay").floatValue = 2f;
            so.FindProperty("_postTutorialGrace").floatValue = 120f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return d;
        }

        private StationSimulation Sim()
        {
            return new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData> { _meteor },
                Random01 = () => 0.5f,
            });
        }

        private ModuleData Module(string name, float metal, bool removable = true)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            Fill(so.FindProperty("_buildCost"), R(ResourceType.Metal, metal));
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
