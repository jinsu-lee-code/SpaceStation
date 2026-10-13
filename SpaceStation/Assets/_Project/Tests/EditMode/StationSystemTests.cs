using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceStation.Tests
{
    /// <summary>12-0b 시스템 공통 틀: 틱 단계 순서 · 끼워 넣기, 모듈 배치/철거 알림, 세이브 저장 · 복원, 키 중복.</summary>
    public class StationSystemTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _block;

        /// <summary>시험용 시스템: 틱 · 배치 · 철거를 기록하고, 틱 횟수와 마지막 배치 모듈을 저장한다.</summary>
        private sealed class ProbeSystem : StationSystem
        {
            public readonly List<string> Log = new List<string>();
            public int Ticks;
            public ModuleInstance Last;
            public int RestoredTicks = -1;
            public ModuleInstance RestoredModule;

            public override string Key => "probe";

            public override void Tick(float dt)
            {
                Ticks++;
                Log.Add($"tick {Sim.ElapsedSeconds:0}");
            }

            public override void OnModulePlaced(ModuleInstance module)
            {
                Last = module;
                Log.Add("placed " + module.Data.name);
            }

            public override void OnModuleRemoved(ModuleInstance module) => Log.Add("removed " + module.Data.name);

            public override string Capture(Func<ModuleInstance, int> indexOf) => $"{Ticks}|{indexOf(Last)}";

            public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
            {
                var parts = state.Split('|');
                RestoredTicks = int.Parse(parts[0]);
                RestoredModule = moduleAt(int.Parse(parts[1]));
            }
        }

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Metal, 100));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Metal, 200));
            b.FindProperty("_eventGracePeriod").floatValue = 1000f;
            b.FindProperty("_eventIntervalMin").floatValue = 1000f;
            b.FindProperty("_eventIntervalMax").floatValue = 1000f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 0f, removable: false);
            _block = Module("MD_TestBlock", 10f);

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            var grade = list.GetArrayElementAtIndex(0);
            var unlocks = grade.FindPropertyRelative("_unlocks");
            unlocks.arraySize = 1;
            unlocks.GetArrayElementAtIndex(0).objectReferenceValue = _block;
            g.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void TickSteps_KeepOriginalOrder_AndSystemIsInsertedAfterNamedStep()
        {
            var sim = Sim();
            var names = new List<string>();
            foreach (var s in sim.TickSteps)
                names.Add(s.Name);
            CollectionAssert.AreEqual(new[]
            {
                TickStepNames.Damage, TickStepNames.Durability, TickStepNames.Resources, TickStepNames.Incidents, TickStepNames.Research,
                TickStepNames.Cargo, TickStepNames.Automation, TickStepNames.Needs, TickStepNames.Population, TickStepNames.Events,
                MeteorShowerSystem.SystemKey, HeatSystem.SystemKey, // 12-3c 기본으로 붙는 시스템
                TickStepNames.Clock, TickStepNames.Tutorial, TickStepNames.Progression, TickStepNames.Failure,
            }, names);

            var probe = sim.AddSystem(new ProbeSystem(), TickStepNames.Events);
            Assert.AreSame(sim, probe.Sim);
            Assert.AreSame(probe, sim.GetSystem<ProbeSystem>());
            int at = -1;
            for (int i = 0; i < sim.TickSteps.Count; i++)
                if (sim.TickSteps[i].Name == "probe")
                    at = i;
            Assert.AreEqual(TickStepNames.Events, sim.TickSteps[at - 1].Name, "이벤트 단계 바로 뒤");
            Assert.AreEqual(MeteorShowerSystem.SystemKey, sim.TickSteps[at + 1].Name, "같은 단계 뒤에 먼저 붙은 시스템보다 앞");

            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual(2, probe.Ticks);
            CollectionAssert.AreEqual(new[] { "tick 0", "tick 1" }, probe.Log, "시간 단계보다 먼저 돎 (경과 시간이 아직 안 늘어남)");

            Assert.Throws<ArgumentException>(() => sim.AddSystem(new ProbeSystem(), null), "같은 키");
            Assert.Throws<ArgumentException>(() => Sim().AddSystem(new ProbeSystem(), "없는 단계"));
        }

        [Test]
        public void System_GetsModulePlacedAndRemoved_AndSurvivesSaveRestore()
        {
            var sim = Sim();
            var probe = sim.AddSystem(new ProbeSystem(), null);
            Assert.IsTrue(sim.TryPlace(_block, Vector3Int.right, 0, out var block));
            Assert.AreSame(block, probe.Last);
            Assert.IsTrue(sim.TryRemove(block));
            CollectionAssert.AreEqual(new[] { "placed MD_TestBlock", "removed MD_TestBlock" }, probe.Log);

            Assert.IsTrue(sim.TryPlace(_block, Vector3Int.left, 0, out var kept));
            for (int i = 0; i < 3; i++)
                sim.Tick(1f);
            var state = StationStateSerializer.Capture(sim);
            Assert.AreEqual(1, state.Systems.Count);
            var json = JsonUtility.ToJson(state);

            var copy = Sim();
            var restored = copy.AddSystem(new ProbeSystem(), null);
            Assert.AreEqual(0, StationStateSerializer.Restore(copy, JsonUtility.FromJson<StationState>(json)), "빠진 항목 없음");
            Assert.AreEqual(3, restored.RestoredTicks);
            Assert.IsNotNull(restored.RestoredModule);
            Assert.AreEqual(kept.Origin, restored.RestoredModule.Origin, "모듈 순번 → 복원된 모듈");

            // 이 시스템이 없는 판에 불러오면 건너뛴 항목으로 셈
            Assert.AreEqual(1, StationStateSerializer.Restore(Sim(), JsonUtility.FromJson<StationState>(json)));
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim() => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
        });

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
