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
    /// CONNECT_1121 7번 ③ 타이쿤판 공급 사슬: 공급량이 정거장 소비에 더해짐 · 재고가 있으면 효과 그대로 · 바닥나면 생산 ÷ 소비 비율만큼 (바닥 25%) ·
    /// 생산 · 서비스 가동률에 곱함 · 파손된 모듈은 공급을 받지 않음 · 공통 고리 없으면 예전 그대로.
    /// </summary>
    public class SupplyChainTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _farm, _clinic, _mine;
        private SupplyChainConfig _supply;

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
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 1000), R(ResourceType.Metal, 100), R(ResourceType.Food, 1000), R(ResourceType.Water, 1000));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 10000), R(ResourceType.Metal, 10000), R(ResourceType.Food, 10000), R(ResourceType.Water, 10000));
            Fill(b.FindProperty("_consumptionPerResident"));
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", removable: false);
            _farm = Module("MD_TestFarm", production: new[] { R(ResourceType.Food, 10f) });
            _mine = Module("MD_TestMine", production: new[] { R(ResourceType.Metal, 1f) });
            _clinic = Module("MD_TestClinic");
            var c = new SerializedObject(_clinic);
            c.FindProperty("_serviceNeed").enumValueIndex = (int)ResidentNeed.Medical;
            c.FindProperty("_serviceCapacity").intValue = 50;
            c.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();

            _supply = Create<SupplyChainConfig>();
            _supply.EditorSet(new[]
            {
                (_farm, new[] { R(ResourceType.Metal, 2f) }),
                (_clinic, new[] { R(ResourceType.Metal, 2f) }),
            });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Stocked_FullEffect_DrainAddedToConsumption()
        {
            var sim = Sim(out var chain);
            var farm = Place(sim, _farm, 1);
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual(1f, chain.FactorOf(farm), Eps, "금속 재고가 있으면 효과 그대로");
            Assert.AreEqual(2f, chain.DemandOf(ResourceType.Metal), Eps);
            Assert.AreEqual(2f, sim.Resources.GetConsumption(ResourceType.Metal), Eps, "공급량이 정거장 소비에 더해짐");
            Assert.AreEqual(10f, sim.Resources.GetProduction(ResourceType.Food), Eps);
        }

        [Test]
        public void Depleted_EffectFollowsSupplyRatio_WithFloor()
        {
            var sim = Sim(out var chain);
            var farm = Place(sim, _farm, 1);
            var mine = Place(sim, _mine, -1); // 금속 1/초 생산 — 공급 2/초의 절반
            sim.Resources.SetStock(ResourceType.Metal, 0f);
            for (int i = 0; i < 3; i++)
                sim.Tick(1f);
            Assert.AreEqual(0.5f, chain.FactorOf(farm), 0.02f, "금속 바닥 · 생산 1 ÷ 소비 2 = 효과 50%");
            Assert.AreEqual(5f, sim.Resources.GetProduction(ResourceType.Food), 0.2f, "농장 생산도 절반");
            Assert.AreEqual(1, chain.ShortCount);

            Assert.IsTrue(sim.Grid.Remove(mine)); // 금속 생산 없음
            for (int i = 0; i < 3; i++)
                sim.Tick(1f);
            Assert.AreEqual(_supply.MinEfficiency, chain.FactorOf(farm), Eps, "공급 0이어도 바닥 25% (꺼지지 않음)");
        }

        [Test]
        public void Service_StrengthScaled()
        {
            var sim = Sim(out var chain);
            var clinic = Place(sim, _clinic, 1);
            sim.Resources.SetStock(ResourceType.Metal, 0f);
            for (int i = 0; i < 3; i++)
                sim.Tick(1f);
            Assert.AreEqual(_supply.MinEfficiency, chain.FactorOf(clinic), Eps);
            Assert.AreEqual(_supply.MinEfficiency, sim.Needs.GetStrength(clinic), Eps, "서비스 가동률에 곱함");
        }

        [Test]
        public void Damaged_GetsNoSupply()
        {
            var sim = Sim(out var chain);
            var farm = Place(sim, _farm, 1);
            sim.Damage.Damage(farm);
            sim.Tick(1f);
            Assert.AreEqual(0, chain.SuppliedCount, "파손된 모듈은 공급을 받지 않음");
            Assert.AreEqual(0f, chain.DemandOf(ResourceType.Metal), Eps);
        }

        [Test]
        public void NoHook_OldBehavior()
        {
            var sim = Sim(out _, attach: false);
            Assert.IsNull(sim.ModuleStrengthScale, "공통 고리 기본 = 없음");
            var clinic = Place(sim, _clinic, 1);
            sim.Tick(1f);
            Assert.AreEqual(1f, sim.StrengthScaleOf(clinic), Eps);
            Assert.AreEqual(1f, sim.Needs.GetStrength(clinic), Eps);
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim(out SupplyChainSystem chain, bool attach = true)
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
            chain = attach ? SupplyChainSystem.AttachTo(sim, _supply) : null;
            return sim;
        }

        private static ModuleInstance Place(StationSimulation sim, ModuleData data, int x)
        {
            Assert.IsTrue(sim.Grid.TryPlace(data, new Vector3Int(x, 0, 0), 0, out var m), $"{data.name} @ {x}");
            return m;
        }

        private ModuleData Module(string name, bool removable = true, ResourceAmount[] production = null)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            Fill(so.FindProperty("_production"), production ?? new ResourceAmount[0]);
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
