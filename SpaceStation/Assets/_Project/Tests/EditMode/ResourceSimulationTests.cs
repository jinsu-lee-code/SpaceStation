using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>BALANCE.md 4번 검산 케이스를 그대로 재현한다. 수치는 테스트 전용 데이터.</summary>
    public class ResourceSimulationTests
    {
        private const float Eps = 1e-4f;
        private readonly List<Object> _created = new List<Object>();

        private BalanceConfig _config;
        private ModuleData _core, _solar, _oxygen, _water, _mining, _storage, _habitat;

        [SetUp]
        public void SetUp()
        {
            _config = Config(
                startingPopulation: 4,
                perResident: new[] { R(ResourceType.Oxygen, 0.2f), R(ResourceType.Water, 0.1f), R(ResourceType.Food, 0.1f) },
                starting: new[] { R(ResourceType.Oxygen, 200), R(ResourceType.Water, 150), R(ResourceType.Food, 150), R(ResourceType.Metal, 150) },
                baseCapacity: new[] { R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 200) },
                minEfficiency: 0.25f);

            _core = Module("Core", supply: 5, housing: 4);
            _solar = Module("Solar", supply: 10);
            _oxygen = Module("Oxygen", demand: 4, prod: new[] { R(ResourceType.Oxygen, 3) }, cons: new[] { R(ResourceType.Water, 0.5f) });
            _water = Module("Water", demand: 3, prod: new[] { R(ResourceType.Water, 2) });
            _mining = Module("Mining", demand: 3, prod: new[] { R(ResourceType.Metal, 1) });
            _storage = Module("Storage", storage: 150);
            _habitat = Module("Habitat", demand: 2, housing: 6);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void StartingState_FromConfig()
        {
            var sim = new ResourceSimulation(_config);
            Assert.AreEqual(4, sim.Population);
            Assert.AreEqual(200f, sim.GetStock(ResourceType.Oxygen));
            Assert.AreEqual(150f, sim.GetStock(ResourceType.Metal));
            Assert.AreEqual(200f, sim.GetCapacity(ResourceType.Food));
        }

        [Test]
        public void MinimalStation_NetRatesMatchBalanceDoc()
        {
            var sim = new ResourceSimulation(_config);
            sim.Tick(new[] { _core, _solar, _oxygen, _water, _mining }, 1f);

            Assert.AreEqual(15f, sim.PowerSupply, Eps);
            Assert.AreEqual(10f, sim.PowerDemand, Eps);
            Assert.AreEqual(1f, sim.PowerEfficiency, Eps);
            Assert.AreEqual(2.2f, sim.GetNetRate(ResourceType.Oxygen), Eps);
            Assert.AreEqual(1.1f, sim.GetNetRate(ResourceType.Water), Eps);
            Assert.AreEqual(-0.4f, sim.GetNetRate(ResourceType.Food), Eps);
            Assert.AreEqual(1f, sim.GetNetRate(ResourceType.Metal), Eps);

            Assert.AreEqual(200f, sim.GetStock(ResourceType.Oxygen), Eps, "한도 초과분은 버림");
            Assert.AreEqual(151.1f, sim.GetStock(ResourceType.Water), Eps);
            Assert.AreEqual(149.6f, sim.GetStock(ResourceType.Food), Eps);
            Assert.AreEqual(151f, sim.GetStock(ResourceType.Metal), Eps);
        }

        [Test]
        public void SolarRemoved_HalfEfficiency_MatchesBalanceDoc()
        {
            var sim = new ResourceSimulation(_config);
            sim.Tick(new[] { _core, _oxygen, _water, _mining }, 1f);

            Assert.AreEqual(0.5f, sim.PowerEfficiency, Eps);
            Assert.AreEqual(0.7f, sim.GetNetRate(ResourceType.Oxygen), Eps);
            Assert.AreEqual(0.35f, sim.GetNetRate(ResourceType.Water), Eps);
            Assert.AreEqual(0.5f, sim.GetNetRate(ResourceType.Metal), Eps);
        }

        [Test]
        public void SevereShortage_ClampsToMinimumEfficiency()
        {
            var sim = new ResourceSimulation(_config);
            // 공급 5, 수요 24 → 0.208 → 최소 0.25
            sim.Tick(new[] { _core, _oxygen, _oxygen, _oxygen, _oxygen, _oxygen, _oxygen }, 1f);

            Assert.AreEqual(0.25f, sim.PowerEfficiency, Eps);
            Assert.AreEqual(6 * 3 * 0.25f, sim.GetProduction(ResourceType.Oxygen), Eps);
        }

        [Test]
        public void NonPowerModules_IgnoreEfficiency()
        {
            var sim = new ResourceSimulation(_config);
            var freeProducer = Module("Free", prod: new[] { R(ResourceType.Metal, 2) });
            sim.Tick(new[] { _core, _mining, _mining, freeProducer }, 1f); // 수요 6 > 공급 5

            Assert.Less(sim.PowerEfficiency, 1f);
            Assert.AreEqual(2f + 2 * 1f * sim.PowerEfficiency, sim.GetProduction(ResourceType.Metal), Eps);
        }

        [Test]
        public void DepletedInput_StopsModule_AndRemovesItsPowerDemand()
        {
            var sim = new ResourceSimulation(_config);
            sim.SetStock(ResourceType.Water, 0f);
            sim.Tick(new[] { _core, _solar, _oxygen }, 1f);

            Assert.AreEqual(1, sim.StoppedModuleCount);
            Assert.AreEqual(0f, sim.GetProduction(ResourceType.Oxygen), Eps);
            Assert.AreEqual(0f, sim.PowerDemand, Eps);
            Assert.AreEqual(0f, sim.GetStock(ResourceType.Water), Eps, "0 미만으로 내려가지 않음");
        }

        [Test]
        public void StorageModule_RaisesAllStockCapacities()
        {
            var sim = new ResourceSimulation(_config);
            sim.Tick(new[] { _core, _storage }, 1f);

            Assert.AreEqual(350f, sim.GetCapacity(ResourceType.Oxygen), Eps);
            Assert.AreEqual(350f, sim.GetCapacity(ResourceType.Metal), Eps);
            Assert.AreEqual(0f, sim.GetCapacity(ResourceType.Power), Eps);
        }

        [Test]
        public void Population_LimitedByHousing()
        {
            var sim = new ResourceSimulation(_config);
            sim.RefreshCapacities(new[] { _core });
            Assert.AreEqual(4, sim.HousingCapacity);
            Assert.IsFalse(sim.TryAdjustPopulation(1));

            sim.RefreshCapacities(new[] { _core, _habitat });
            Assert.AreEqual(10, sim.HousingCapacity);
            Assert.IsTrue(sim.TryAdjustPopulation(1));
            Assert.AreEqual(5, sim.Population);
            Assert.IsFalse(sim.TryAdjustPopulation(-6));
        }

        [Test]
        public void Depletion_EventFiresOnTransition()
        {
            var sim = new ResourceSimulation(_config);
            var events = new List<(ResourceType, bool)>();
            sim.DepletionChanged += (t, d) => events.Add((t, d));

            sim.SetStock(ResourceType.Food, 0.3f);
            sim.Tick(new[] { _core }, 1f); // 식량 -0.4
            Assert.IsTrue(sim.IsDepleted(ResourceType.Food));
            CollectionAssert.Contains(events, (ResourceType.Food, true));

            events.Clear();
            sim.Tick(new[] { _core }, 1f);
            CollectionAssert.IsEmpty(events, "상태가 그대로면 이벤트 없음");

            sim.SetStock(ResourceType.Food, 10f);
            CollectionAssert.Contains(events, (ResourceType.Food, false));
        }

        [Test]
        public void Cost_SpendDeductsMetal_AndRejectsWhenShort()
        {
            var sim = new ResourceSimulation(_config); // 금속 150
            var cost = new[] { R(ResourceType.Metal, 60) };

            Assert.IsTrue(sim.CanAfford(cost));
            Assert.IsTrue(sim.TrySpend(cost));
            Assert.IsTrue(sim.TrySpend(cost));
            Assert.AreEqual(30f, sim.GetStock(ResourceType.Metal), Eps);

            Assert.IsFalse(sim.CanAfford(cost));
            Assert.IsFalse(sim.TrySpend(cost));
            Assert.AreEqual(30f, sim.GetStock(ResourceType.Metal), Eps, "실패 시 차감 없음");
        }

        [Test]
        public void Cost_ExactAmountIsAffordable()
        {
            var sim = new ResourceSimulation(_config);
            // BALANCE 4번: 20 + 40 + 40 + 50 = 150
            foreach (var c in new[] { 20f, 40f, 40f, 50f })
                Assert.IsTrue(sim.TrySpend(new[] { R(ResourceType.Metal, c) }));
            Assert.AreEqual(0f, sim.GetStock(ResourceType.Metal), Eps);
        }

        [Test]
        public void Cost_SplitLinesAreSummed_AndAllOrNothing()
        {
            var sim = new ResourceSimulation(_config); // 금속 150, 식량 150
            Assert.IsFalse(sim.CanAfford(new[] { R(ResourceType.Metal, 100), R(ResourceType.Metal, 100) }));
            Assert.IsFalse(sim.TrySpend(new[] { R(ResourceType.Metal, 10), R(ResourceType.Food, 999) }));
            Assert.AreEqual(150f, sim.GetStock(ResourceType.Metal), Eps);
            Assert.IsTrue(sim.CanAfford(null));
            Assert.IsFalse(sim.CanAfford(new[] { R(ResourceType.Power, 1) }), "전력은 비용으로 쓸 수 없음");
        }

        [Test]
        public void Refund_ReturnsHalf_ClampedToCapacity()
        {
            var sim = new ResourceSimulation(_config); // 환급률 0.5, 금속 150 / 한도 200
            sim.TrySpend(new[] { R(ResourceType.Metal, 50) });
            sim.RefundBuildCost(new[] { R(ResourceType.Metal, 50) });
            Assert.AreEqual(125f, sim.GetStock(ResourceType.Metal), Eps);

            sim.SetStock(ResourceType.Metal, 190f);
            sim.RefundBuildCost(new[] { R(ResourceType.Metal, 60) });
            Assert.AreEqual(200f, sim.GetStock(ResourceType.Metal), Eps, "한도 초과분은 버림");
        }

        // ---- helpers ----

        private static ResourceAmount R(ResourceType type, float amount) => new ResourceAmount(type, amount);

        private ModuleData Module(string name, float supply = 0, float demand = 0,
            ResourceAmount[] prod = null, ResourceAmount[] cons = null, int housing = 0, float storage = 0)
        {
            var data = ScriptableObject.CreateInstance<ModuleData>();
            data.name = name;
            _created.Add(data);

            var p = new List<ResourceAmount>(prod ?? new ResourceAmount[0]);
            if (supply > 0) p.Add(R(ResourceType.Power, supply));
            var c = new List<ResourceAmount>(cons ?? new ResourceAmount[0]);
            if (demand > 0) c.Add(R(ResourceType.Power, demand));

            var so = new SerializedObject(data);
            Fill(so.FindProperty("_production"), p);
            Fill(so.FindProperty("_consumption"), c);
            so.FindProperty("_housingCapacity").intValue = housing;
            so.FindProperty("_storageBonus").floatValue = storage;
            so.ApplyModifiedPropertiesWithoutUndo();
            return data;
        }

        private BalanceConfig Config(int startingPopulation, ResourceAmount[] perResident, ResourceAmount[] starting,
            ResourceAmount[] baseCapacity, float minEfficiency)
        {
            var config = ScriptableObject.CreateInstance<BalanceConfig>();
            _created.Add(config);
            var so = new SerializedObject(config);
            so.FindProperty("_startingPopulation").intValue = startingPopulation;
            Fill(so.FindProperty("_consumptionPerResident"), perResident);
            Fill(so.FindProperty("_startingResources"), starting);
            Fill(so.FindProperty("_baseStorageCapacity"), baseCapacity);
            so.FindProperty("_minPowerEfficiency").floatValue = minEfficiency;
            so.FindProperty("_demolishRefundRate").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        private static void Fill(SerializedProperty list, IReadOnlyList<ResourceAmount> values)
        {
            list.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Type").enumValueIndex = (int)values[i].Type;
                e.FindPropertyRelative("Amount").floatValue = values[i].Amount;
            }
        }
    }
}
