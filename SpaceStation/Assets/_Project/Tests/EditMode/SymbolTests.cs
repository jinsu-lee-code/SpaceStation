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
    /// CONNECT_1121 7번 ④ 타이쿤판 상징 모듈: 짓기 전 보너스 없음 · 가동 중 전체 생산 +10% · 만족도 상한 +5 · 엔드리스 시작 · 10분마다 단계 ·
    /// 파손되면 보너스만 멈추고 엔드리스는 이어짐 · 세이브 · 공통 고리 없으면 예전 그대로.
    /// </summary>
    public class SymbolTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _farm, _prism;
        private SymbolConfig _symbol;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 100000f;
            b.ApplyModifiedPropertiesWithoutUndo();
            _core = Module("MD_TestCore", removable: false);
            _farm = Module("MD_TestFarm", food: 10f);
            _prism = Module("MD_TestPrism");
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();
            _symbol = Create<SymbolConfig>();
            _symbol.EditorSetSymbol(_prism);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void BeforeBuilt_NoBonus()
        {
            var sim = Sim(out var symbol);
            Place(sim, _farm, 1);
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.IsFalse(symbol.Started);
            Assert.AreEqual(0, symbol.Level);
            Assert.AreEqual(10f, sim.Resources.GetProduction(ResourceType.Food), Eps);
            Assert.AreEqual(1f, symbol.IntervalScale, Eps);
            Assert.AreEqual(0f, symbol.RaidBonus, Eps);
        }

        [Test]
        public void Built_ProductionAndCap_StartsEndless()
        {
            var sim = Sim(out var symbol);
            Place(sim, _farm, 1);
            sim.Tick(1f);
            float cap = sim.Population.SatisfactionCap;
            Place(sim, _prism, -1);
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.IsTrue(symbol.Started, "가동되면 엔드리스 시작");
            Assert.IsTrue(symbol.Active);
            Assert.AreEqual(1, symbol.Level);
            Assert.AreEqual(11f, sim.Resources.GetProduction(ResourceType.Food), Eps, "전체 생산 +10%");
            Assert.AreEqual(_symbol.SatisfactionBonus, sim.SatisfactionCapBonus(), Eps, "만족도 상한 고리 +5");
            Assert.AreEqual(Mathf.Min(PopulationSimulation.MaxSatisfaction, cap + _symbol.SatisfactionBonus), sim.Population.SatisfactionCap, Eps, "만족도 상한 +5");
            Assert.AreEqual(1f - _symbol.IntervalStep, symbol.IntervalScale, Eps, "1단계 방문 간격 −5%");
            Assert.AreEqual(_symbol.RaidStep, symbol.RaidBonus, Eps);
        }

        [Test]
        public void Levels_RiseEveryTenMinutes_Capped()
        {
            var sim = Sim(out var symbol);
            Place(sim, _prism, 1);
            sim.Tick(1f);
            for (int i = 0; i < 600; i++)
                sim.Tick(1f);
            Assert.AreEqual(2, symbol.Level, "10분 뒤 2단계");
            Assert.AreEqual(1f - 2f * _symbol.IntervalStep, symbol.IntervalScale, Eps);
            for (int i = 0; i < 600 * 20; i++)
                sim.Tick(1f);
            Assert.AreEqual(_symbol.MinIntervalScale, symbol.IntervalScale, Eps, "방문 간격 바닥 50%");
            Assert.AreEqual(_symbol.MaxRaidBonus, symbol.RaidBonus, Eps, "습격 더하기 최대 30%");
        }

        [Test]
        public void Damaged_StopsBonus_EndlessContinues()
        {
            var sim = Sim(out var symbol);
            Place(sim, _farm, 1);
            var prism = Place(sim, _prism, -1);
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.IsTrue(symbol.Active);
            sim.Damage.Damage(prism);
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.IsFalse(symbol.Active, "파손 = 보너스 멈춤");
            Assert.IsTrue(symbol.Started, "엔드리스는 이어짐");
            Assert.AreEqual(1f, symbol.ProductionMultiplier, Eps);
            Assert.AreEqual(10f, sim.Resources.GetProduction(ResourceType.Food), Eps);
        }

        [Test]
        public void SaveRestore_KeepsEndless()
        {
            var sim = Sim(out var symbol);
            Place(sim, _prism, 1);
            sim.Tick(1f);
            for (int i = 0; i < 700; i++)
                sim.Tick(1f);
            string state = symbol.Capture(_ => -1);
            Sim(out var copy);
            copy.Restore(state, _ => null);
            Assert.IsTrue(copy.Started);
            Assert.AreEqual(symbol.EndlessSeconds, copy.EndlessSeconds, Eps);
            Assert.AreEqual(symbol.Level, copy.Level);
        }

        [Test]
        public void NoHook_OldBehavior()
        {
            var sim = Sim(out _, attach: false);
            Assert.IsNull(sim.SatisfactionCapBonus, "공통 고리 기본 = 없음");
            Place(sim, _farm, 1);
            sim.Tick(1f);
            Assert.AreEqual(10f, sim.Resources.GetProduction(ResourceType.Food), Eps);
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim(out SymbolSystem symbol, bool attach = true)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                HazardRandom01 = () => 0.99f,
            });
            symbol = attach ? SymbolSystem.AttachTo(sim, _symbol) : null;
            return sim;
        }

        private static ModuleInstance Place(StationSimulation sim, ModuleData data, int x)
        {
            Assert.IsTrue(sim.Grid.TryPlace(data, new Vector3Int(x, 0, 0), 0, out var m), $"{data.name} @ {x}");
            return m;
        }

        private ModuleData Module(string name, bool removable = true, float food = 0f)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            var production = so.FindProperty("_production");
            production.arraySize = food > 0f ? 1 : 0;
            if (food > 0f)
            {
                var e = production.GetArrayElementAtIndex(0);
                e.FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Food;
                e.FindPropertyRelative("Amount").floatValue = food;
            }
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
