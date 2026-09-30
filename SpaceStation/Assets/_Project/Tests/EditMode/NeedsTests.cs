using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>4-9 거주자 요구 (BALANCE 20번).</summary>
    public class NeedsTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private ModuleData _habitat, _medical, _block;
        private StationGrid _grid;
        private readonly Dictionary<ModuleInstance, float> _strength = new Dictionary<ModuleInstance, float>();
        private static readonly ResidentNeed[] Medical = { ResidentNeed.Medical };

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var so = new SerializedObject(_config);
            so.FindProperty("_needSatisfactionCapPenalty").floatValue = 30f;
            so.FindProperty("_satisfactionAboveCapDecayPerSecond").floatValue = 1f;
            so.FindProperty("_satisfactionRecoveryPerSecond").floatValue = 2f;
            so.ApplyModifiedPropertiesWithoutUndo();

            _habitat = Create<ModuleData>();
            var h = new SerializedObject(_habitat);
            h.FindProperty("_housingCapacity").intValue = 10;
            h.ApplyModifiedPropertiesWithoutUndo();
            _medical = Create<ModuleData>();
            var m = new SerializedObject(_medical);
            m.FindProperty("_serviceNeed").enumValueIndex = (int)ResidentNeed.Medical;
            m.FindProperty("_serviceRadius").intValue = 3;
            m.FindProperty("_serviceCapacity").intValue = 12;
            m.ApplyModifiedPropertiesWithoutUndo();
            _block = Create<ModuleData>();
            _grid = new StationGrid();
            _strength.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        private NeedsSystem System() => new NeedsSystem(_config, s => _strength.TryGetValue(s, out var v) ? v : 1f, null);

        private ModuleInstance Place(ModuleData data, int x, int y = 0)
        {
            Assert.IsTrue(_grid.TryPlace(data, new Vector3Int(x, y, 0), 0, out var mi));
            return mi;
        }

        [Test]
        public void NoNeeds_CapIs100()
        {
            var needs = System();
            Place(_habitat, 0);
            needs.Evaluate(_grid, 10, new ResidentNeed[0]);
            Assert.AreEqual(100f, needs.SatisfactionCap, Eps);
            Assert.AreEqual(0, needs.Statuses.Count);
        }

        [Test]
        public void Unserved_LowersCapByPenalty()
        {
            var needs = System();
            Place(_habitat, 0);
            needs.Evaluate(_grid, 10, Medical);
            Assert.AreEqual(0f, needs.Statuses[0].Ratio, Eps);
            Assert.AreEqual(70f, needs.SatisfactionCap, Eps);
        }

        [Test]
        public void Service_CoversHabitatsInRange_Only()
        {
            var needs = System();
            var near = Place(_habitat, 0);
            var far = Place(_habitat, 10);
            var med = Place(_medical, 3); // near와 거리 3, far와 거리 7
            needs.Evaluate(_grid, 20, Medical); // 주민은 수용 인구 비율대로 10 / 10
            Assert.AreEqual(10f, needs.Statuses[0].Served, Eps);
            Assert.AreEqual(0.5f, needs.Statuses[0].Ratio, Eps);
            Assert.AreEqual(85f, needs.SatisfactionCap, Eps);
            Assert.AreEqual(1f, needs.GetHabitatCoverage(near, ResidentNeed.Medical), Eps);
            Assert.AreEqual(0f, needs.GetHabitatCoverage(far, ResidentNeed.Medical), Eps);
            Assert.AreEqual(10f, needs.GetServed(med), Eps);
        }

        [Test]
        public void Service_LimitedByCapacity_AndStrength()
        {
            var needs = System();
            Place(_habitat, 0);
            Place(_habitat, 1);
            var med = Place(_medical, 2);
            needs.Evaluate(_grid, 20, Medical);
            Assert.AreEqual(12f, needs.Statuses[0].Served, Eps, "담당 최대 12명");
            Assert.AreEqual(100f - 0.4f * 30f, needs.SatisfactionCap, Eps);

            _strength[med] = 0.5f;
            needs.Evaluate(_grid, 20, Medical);
            Assert.AreEqual(6f, needs.Statuses[0].Served, Eps, "가동률 50% → 6명");
            _strength[med] = 0f;
            needs.Evaluate(_grid, 20, Medical);
            Assert.AreEqual(0f, needs.Statuses[0].Served, Eps);
        }

        [Test]
        public void Population_DecaysToCap_AndRecoversOnlyUpToCap()
        {
            var resources = new ResourceSimulation(_config);
            var pop = new PopulationSimulation(_config, resources);
            pop.SetSatisfaction(100f);
            pop.SatisfactionCap = 70f;
            for (int i = 0; i < 10; i++)
                pop.Tick(1f);
            Assert.AreEqual(90f, pop.Satisfaction, Eps, "초당 1씩 상한으로");
            for (int i = 0; i < 30; i++)
                pop.Tick(1f);
            Assert.AreEqual(70f, pop.Satisfaction, Eps, "상한 아래로는 내려가지 않음");

            pop.SetSatisfaction(50f);
            for (int i = 0; i < 20; i++)
                pop.Tick(1f);
            Assert.AreEqual(70f, pop.Satisfaction, Eps, "회복은 상한까지만");
        }

        [Test]
        public void Integration_GradeNeeds_ApplyCapEachTick()
        {
            var core = Create<ModuleData>();
            var c = new SerializedObject(core);
            c.FindProperty("_removable").boolValue = false;
            c.FindProperty("_housingCapacity").intValue = 4;
            c.ApplyModifiedPropertiesWithoutUndo();
            var grades = Create<StationGradeConfig>();
            var g = new SerializedObject(grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            var needs = list.GetArrayElementAtIndex(0).FindPropertyRelative("_newNeeds");
            needs.arraySize = 1;
            needs.GetArrayElementAtIndex(0).enumValueIndex = (int)ResidentNeed.Medical;
            g.ApplyModifiedPropertiesWithoutUndo();

            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config, Grades = grades, CoreModule = core, Events = new GameEventData[0], Random01 = () => 0f,
            });
            sim.Resources.SetPopulation(4);
            sim.Tick(1f);
            CollectionAssert.AreEqual(Medical, sim.ActiveNeeds);
            Assert.AreEqual(70f, sim.Population.SatisfactionCap, Eps, "코어 주민 4명 미충족");

            sim.Grid.TryPlace(_medical, Vector3Int.right, 0, out _);
            sim.Tick(1f);
            Assert.AreEqual(100f, sim.Population.SatisfactionCap, Eps, "코어 옆 의료실 → 충족");
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
