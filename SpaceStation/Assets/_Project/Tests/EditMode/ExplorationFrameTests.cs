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
    /// <summary>
    /// CONNECT_1121 M8 공통 탐사 틀 (장 틀 없이 — main에서도 돌아감): 신호를 정하는 쪽 · 방향 · 닿음 판정 고리 · 매장량 칸 · 묶음이 바뀌면 새 신호.
    /// 서사판 장 신호 · 표시 · 기록은 <see cref="ExplorationTests"/>.
    /// </summary>
    public class ExplorationFrameTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _sensor;
        private ExplorationConfig _explore;

        private sealed class Signals : IExploreSignals
        {
            public int Round { get; set; }
            public IReadOnlyList<SiteKind> Kinds { get; set; } = new List<SiteKind> { SiteKind.Asteroid };
            public int DistanceMin => 4;
            public int DistanceMax => 4;
            public IReadOnlyList<Vector3Int> Directions { get; set; } = new List<Vector3Int> { Vector3Int.up };
            public string RoundName(int round) => $"묶음 {round}";
        }

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Water, 50), R(ResourceType.Food, 150), R(ResourceType.Metal, 100));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 1000));
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.ApplyModifiedPropertiesWithoutUndo();
            _core = Module("MD_TestCore", false);
            _sensor = Module("MD_TestSensor", true);
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();
            _explore = Create<ExplorationConfig>();
            _explore.EditorSet(_sensor, new List<string>());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void WithoutChapters_SignalSourceDirectionsReachRule()
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config, Grades = _grades, CoreModule = _core, Events = new List<GameEventData>(), Random01 = () => 0.5f,
            });
            var ex = sim.AddSystem(new ExplorationSystem(_explore, () => 0.5f), TickStepNames.Population);
            Assert.IsTrue(sim.Grid.TryPlace(_sensor, Vector3Int.right, 0, out _));
            Tick(sim, 1);
            Assert.AreEqual(0, ex.Sites.Count, "신호를 정하는 쪽이 없으면 신호 없음");

            var signals = new Signals();
            ex.Signals = signals;
            Tick(sim, 1);
            Assert.AreEqual(1, ex.Sites.Count, "장 틀 없이도 신호를 정하는 쪽이 있으면 지점");
            var site = ex.Sites[0];
            Assert.AreEqual(Vector3Int.up, site.Direction, "방향도 정하는 쪽이 (위)");
            Assert.Greater(site.Cell.y, 0);
            Assert.AreEqual(-1f, site.Reserve, "매장량 기본 = 끝없음");
            Assert.AreEqual("묶음 0", ex.RoundName(site.Chapter));

            site.Revealed = true;
            bool allow = false;
            ex.ReachRule = s => allow;
            Tick(sim, 1);
            Assert.IsFalse(site.Reached, "닿음 판정을 판이 바꿈");
            allow = true;
            Tick(sim, 1);
            Assert.IsTrue(site.Reached && site.InReach);

            signals.Round = 1;
            Tick(sim, 1);
            Assert.AreEqual(2, ex.Sites.Count, "묶음 번호가 바뀌면 새 신호");
            Assert.IsFalse(ex.ProbeUsedThisRound);
        }

        private static void Tick(StationSimulation sim, int seconds)
        {
            for (int i = 0; i < seconds; i++)
                sim.Tick(1f);
        }

        private ModuleData Module(string name, bool removable)
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
