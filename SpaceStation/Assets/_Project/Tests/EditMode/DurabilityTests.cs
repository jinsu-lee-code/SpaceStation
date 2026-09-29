using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>BALANCE.md 15번: 노후화·정비·재건축.</summary>
    public class DurabilityTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _block;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var so = new SerializedObject(_config);
            Fill(so.FindProperty("_startingResources"), R(ResourceType.Metal, 150), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200));
            Fill(so.FindProperty("_baseStorageCapacity"), R(ResourceType.Metal, 500), R(ResourceType.Oxygen, 500), R(ResourceType.Water, 500), R(ResourceType.Food, 500));
            so.FindProperty("_demolishRefundRate").floatValue = 0.5f;
            so.FindProperty("_eventGracePeriod").floatValue = 10000f;
            so.FindProperty("_eventIntervalMin").floatValue = 90f;
            so.FindProperty("_eventIntervalMax").floatValue = 150f;
            so.FindProperty("_durabilityDecayPerSecond").floatValue = 0.2f;
            so.FindProperty("_durabilityEfficiencyThreshold").floatValue = 50f;
            so.FindProperty("_maintenanceCostRate").floatValue = 0.4f;
            so.FindProperty("_maintenanceMaxLoss").floatValue = 15f;
            so.FindProperty("_maxDurabilityFloor").floatValue = 25f;
            so.FindProperty("_meteorDurabilityDamage").floatValue = 20f;
            so.ApplyModifiedPropertiesWithoutUndo();

            _core = Create<ModuleData>();
            var c = new SerializedObject(_core);
            c.FindProperty("_removable").boolValue = false;
            Fill(c.FindProperty("_production"), R(ResourceType.Power, 5));
            c.ApplyModifiedPropertiesWithoutUndo();

            _block = Create<ModuleData>();
            var b = new SerializedObject(_block);
            Fill(b.FindProperty("_buildCost"), R(ResourceType.Metal, 40));
            Fill(b.FindProperty("_production"), R(ResourceType.Oxygen, 3));
            b.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            var unlocks = list.GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks");
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

        private StationSimulation Sim() => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config, Grades = _grades, CoreModule = _core, Events = new GameEventData[0], Random01 = () => 0f,
        });

        private static void Ticks(StationSimulation sim, int n)
        {
            for (int i = 0; i < n; i++)
                sim.Tick(1f);
        }

        [Test]
        public void Core_DoesNotAge_ModulesDo()
        {
            var sim = Sim();
            Assert.IsFalse(sim.Durability.TryGetInfo(sim.Core, out _));
            sim.TryPlace(_block, Vector3Int.right, 0, out var m);
            Ticks(sim, 100);
            Assert.IsTrue(sim.Durability.TryGetInfo(m, out var info));
            Assert.AreEqual(80f, info.Current, Eps, "초당 -0.2");
        }

        [Test]
        public void Efficiency_FullAbove50_LinearBelow()
        {
            var d = new DurabilitySystem(_config);
            Assert.AreEqual(1f, d.EfficiencyFor(100f));
            Assert.AreEqual(1f, d.EfficiencyFor(50f));
            Assert.AreEqual(0.5f, d.EfficiencyFor(25f), Eps);
            Assert.AreEqual(0f, d.EfficiencyFor(0f), Eps);
        }

        [Test]
        public void LowDurability_ReducesProduction()
        {
            var sim = Sim();
            sim.TryPlace(_block, Vector3Int.right, 0, out _);
            Ticks(sim, 375); // 100 - 75 = 25 → 효율 50%
            sim.Tick(1f);
            Assert.AreEqual(3f * (24.8f / 50f), sim.Resources.GetProduction(ResourceType.Oxygen), 0.02f);
        }

        [Test]
        public void ZeroDurability_DestroysWithoutRefund()
        {
            var sim = Sim();
            sim.TryPlace(_block, Vector3Int.right, 0, out _);
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Ticks(sim, 500);
            Assert.IsFalse(sim.Grid.IsOccupied(Vector3Int.right));
            Assert.AreEqual(1, sim.Session.ModulesDestroyed);
            Assert.AreEqual(metal, sim.Resources.GetStock(ResourceType.Metal), Eps);
        }

        [Test]
        public void Maintenance_CostProportional_ReducesMax()
        {
            var sim = Sim();
            sim.TryPlace(_block, Vector3Int.right, 0, out var m);
            Ticks(sim, 250); // 내구도 50
            var cost = sim.Durability.GetMaintenanceCost(m);
            Assert.AreEqual(40f * 0.4f * 0.5f, cost[0].Amount, Eps, "건설비 × 40% × 닳은 비율 50%");

            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.AreEqual(MaintainResult.Done, sim.TryMaintain(m));
            sim.Durability.TryGetInfo(m, out var info);
            Assert.AreEqual(85f, info.Max, Eps);
            Assert.AreEqual(85f, info.Current, Eps);
            Assert.AreEqual(metal - 8f, sim.Resources.GetStock(ResourceType.Metal), Eps);
            Assert.AreEqual(MaintainResult.AlreadyAtMax, sim.TryMaintain(m));
        }

        [Test]
        public void Maintenance_MaxFloor25()
        {
            var sim = Sim();
            sim.TryPlace(_block, Vector3Int.right, 0, out var m);
            sim.Durability.TryGetInfo(m, out var info);
            for (int i = 0; i < 10; i++)
            {
                sim.Tick(1f);
                sim.TryMaintain(m);
            }
            Assert.AreEqual(25f, info.Max, Eps);
        }

        [Test]
        public void Refund_ScalesWithDurability()
        {
            var sim = Sim();
            sim.TryPlace(_block, Vector3Int.right, 0, out var m); // 150 - 40 = 110
            Ticks(sim, 250); // 내구도 50 → 환급 40 × 50% × 50% = 10
            Assert.AreEqual(10f, sim.GetRefund(m)[0].Amount, Eps);
            sim.TryRemove(m);
            Assert.AreEqual(120f, sim.Resources.GetStock(ResourceType.Metal), Eps);
        }

        [Test]
        public void Rebuild_PaysNetCost_ResetsDurability()
        {
            var sim = Sim();
            sim.TryPlace(_block, Vector3Int.right, 0, out var m);
            Ticks(sim, 250); // 내구도 50: 재건축 순비용 = 40 − 10 = 30
            Assert.AreEqual(30f, sim.GetRebuildCost(m)[0].Amount, Eps);
            float metal = sim.Resources.GetStock(ResourceType.Metal);

            Assert.AreEqual(RebuildResult.Done, sim.TryRebuild(m, out var rebuilt));
            Assert.AreEqual(metal - 30f, sim.Resources.GetStock(ResourceType.Metal), Eps);
            Assert.AreNotSame(m, rebuilt);
            Assert.AreEqual(Vector3Int.right, rebuilt.Origin);
            sim.Durability.TryGetInfo(rebuilt, out var info);
            Assert.AreEqual(100f, info.Current, Eps);
            Assert.AreEqual(100f, info.Max, Eps);
        }

        [Test]
        public void MeteorImpact_ReducesDurability()
        {
            var sim = Sim();
            sim.TryPlace(_block, Vector3Int.right, 0, out var m);
            sim.Durability.ApplyImpact(m, 20f);
            sim.Durability.TryGetInfo(m, out var info);
            Assert.AreEqual(80f, info.Current, Eps);
            sim.Durability.ApplyImpact(m, 100f);
            Assert.IsFalse(sim.Grid.IsOccupied(Vector3Int.right), "0이 되면 즉시 파괴");
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }

        private static ResourceAmount R(ResourceType t, float a) => new ResourceAmount(t, a);

        private static void Fill(SerializedProperty list, params ResourceAmount[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                list.GetArrayElementAtIndex(i).FindPropertyRelative("Type").enumValueIndex = (int)values[i].Type;
                list.GetArrayElementAtIndex(i).FindPropertyRelative("Amount").floatValue = values[i].Amount;
            }
        }
    }
}
