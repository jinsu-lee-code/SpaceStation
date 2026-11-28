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
    /// <summary>CONNECT_1121 7번 ① 엔드리스 집 환경 — 방사선 한도 = 보통 × (1 + 견딤), 규칙 전에는 0, 핵융합로 옆 집 주민이 힘듦.</summary>
    public class EndlessHomeComfortTests
    {
        private const float Eps = 1e-4f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ResidentConfig _residents;
        private ModuleData _core, _home, _reactor;

        private sealed class Rules : IStationRules
        {
            public StationRule Open = StationRule.Radiation;
            public bool Allows(StationRule rule) => (Open & rule) == rule;
        }

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 100000f;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.ApplyModifiedPropertiesWithoutUndo();
            _core = Module("MD_TestCore", removable: false);
            _home = Module("MD_TestHome", housing: 6);
            _reactor = Module("MD_TestReactor", output: 5f);
            _residents = Create<ResidentConfig>();
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
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
        public void Limit_IsComfortTimesOnePlusTolerance()
        {
            var c = Create<HeatPowerConfig>();
            Assert.AreEqual(4f, EndlessHomeComfort.Limit(4f, c, false, false), Eps, "보통");
            Assert.AreEqual(2f, EndlessHomeComfort.Limit(4f, c, true, false), Eps, "민감 = 절반");
            Assert.AreEqual(6f, EndlessHomeComfort.Limit(4f, c, false, true), Eps, "내성 = 1.5배");
        }

        [Test]
        public void ReactorNeighbor_StressesNormalResidents_OnlyAfterRuleOpens()
        {
            var rules = new Rules { Open = StationRule.None };
            var sim = Sim(rules);
            var comfort = EndlessHomeComfort.AttachTo(sim, Create<HeatPowerConfig>());
            Assert.IsNotNull(comfort);
            sim.Resources.SetPopulation(3);
            var home = Place(sim, _home, 1, 0, 0);
            Place(sim, _reactor, 2, 0, 0);
            sim.Tick(0f);
            var roster = sim.Residents;
            Assert.AreEqual(0, roster.StressedCount, "규칙 전에는 힘든 주민 없음");

            rules.Open = StationRule.Radiation;
            sim.Tick(0f);
            sim.Tick(0f); // 주민 갱신(인구 단계)이 방사선 계산보다 앞이라 다음 틱에 반영
            Assert.AreEqual(5f, sim.Radiation.LevelOf(home), Eps, "핵융합로 바로 옆 = 5");
            Assert.AreEqual(1f, comfort.StressOf(roster.Residents[0], home), Eps, "보통 한도 4를 1 넘음");
            Assert.AreEqual(3, roster.StressedCount);
            Assert.AreEqual(-20f, roster.StressMood, Eps, "모두 힘듦 = 최대 20");
        }

        private StationSimulation Sim(IStationRules rules)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                HazardRandom01 = () => 0.5f,
                Residents = _residents,
                ResidentRandom01 = () => 0.5f,
            });
            sim.SetRules(rules);
            return sim;
        }

        private static ModuleInstance Place(StationSimulation sim, ModuleData data, int x, int y, int z)
        {
            Assert.IsTrue(sim.Grid.TryPlace(data, new Vector3Int(x, y, z), 0, out var m), $"{data.name} @ {x},{y},{z}");
            return m;
        }

        private ModuleData Module(string name, bool removable = true, int housing = 0, float output = 0f)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            so.FindProperty("_housingCapacity").intValue = housing;
            so.FindProperty("_radiationOutput").floatValue = output;
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
