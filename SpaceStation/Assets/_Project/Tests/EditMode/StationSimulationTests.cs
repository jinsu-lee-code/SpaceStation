using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>StationSimulation 통합 동작 (배치 판정 순서, 틱 순서, 이벤트 효과, 방치 파괴).</summary>
    public class StationSimulationTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _balance;
        private StationGradeConfig _grades;
        private ModuleData _core, _solar, _storage;
        private MeteorEventData _meteor;
        private SupplyShipEventData _supply;

        [SetUp]
        public void SetUp()
        {
            _core = Module("Core", cost: 0, supply: 5, housing: 4, removable: false);
            _solar = Module("Solar", cost: 20, supply: 10);
            _storage = Module("Storage", cost: 30, storage: 150);

            _balance = Create<BalanceConfig>();
            var b = new SerializedObject(_balance);
            b.FindProperty("_startingPopulation").intValue = 4;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 150), R(ResourceType.Food, 150), R(ResourceType.Metal, 150));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 200));
            Fill(b.FindProperty("_consumptionPerResident"), R(ResourceType.Oxygen, 0.2f), R(ResourceType.Water, 0.1f), R(ResourceType.Food, 0.1f));
            b.FindProperty("_minPowerEfficiency").floatValue = 0.25f;
            b.FindProperty("_demolishRefundRate").floatValue = 0.5f;
            b.FindProperty("_repairCostRate").floatValue = 0.3f;
            b.FindProperty("_startingSatisfaction").floatValue = 70f;
            b.FindProperty("_growthMinSatisfaction").floatValue = 50f;
            b.FindProperty("_growthIntervalAtMinSatisfaction").floatValue = 30f;
            b.FindProperty("_growthIntervalAtMaxSatisfaction").floatValue = 10f;
            b.FindProperty("_oxygenDepletedLossInterval").floatValue = 10f;
            b.FindProperty("_lowSatisfactionThreshold").floatValue = 25f;
            b.FindProperty("_lowSatisfactionLossInterval").floatValue = 15f;
            b.FindProperty("_overcrowdedLossInterval").floatValue = 5f;
            b.FindProperty("_eventGracePeriod").floatValue = 1000f; // 테스트 중 랜덤 이벤트 없음
            b.FindProperty("_eventIntervalMin").floatValue = 90f;
            b.FindProperty("_eventIntervalMax").floatValue = 150f;
            b.FindProperty("_damagedProductionMultiplier").floatValue = 0.5f;
            b.FindProperty("_damagedOxygenLeakPerSecond").floatValue = 0.3f;
            b.FindProperty("_repairDuration").floatValue = 10f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 120f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 2;
            SetGrade(list.GetArrayElementAtIndex(0), "초소형", 0, 0, 1, _solar);
            SetGrade(list.GetArrayElementAtIndex(1), "소형", 10, 8, 2, _storage);
            g.ApplyModifiedPropertiesWithoutUndo();

            _meteor = Create<MeteorEventData>();
            _supply = Create<SupplyShipEventData>();
            var s = new SerializedObject(_supply);
            Fill(s.FindProperty("_rewards"), R(ResourceType.Metal, 60));
            s.ApplyModifiedPropertiesWithoutUndo();
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
            Balance = _balance, Grades = _grades, CoreModule = _core,
            Events = new GameEventData[] { _meteor, _supply }, Random01 = () => 0f,
        });

        [Test]
        public void Start_PlacesActiveCoreAndStartingState()
        {
            var sim = Sim();
            Assert.IsNotNull(sim.Core);
            Assert.IsTrue(sim.Connectivity.IsActive(sim.Core));
            Assert.AreEqual(4, sim.Resources.HousingCapacity);
            Assert.AreEqual(150f, sim.Resources.GetStock(ResourceType.Metal), Eps);
            Assert.IsFalse(sim.CanRemove(sim.Core));
        }

        [Test]
        public void Place_ChargesCost_AndRespectsUnlocks()
        {
            var sim = Sim();
            Assert.IsTrue(sim.TryPlace(_solar, Vector3Int.right, 0, out _));
            Assert.AreEqual(130f, sim.Resources.GetStock(ResourceType.Metal), Eps);
            Assert.AreEqual(PlacementResult.ModuleLocked, sim.EvaluatePlacement(_storage, Vector3Int.left, 0));
            Assert.AreEqual(PlacementResult.Occupied, sim.EvaluatePlacement(_solar, Vector3Int.right, 0), "공간 규칙이 먼저");
        }

        [Test]
        public void Remove_RefundsHalf()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var solar);
            Assert.IsTrue(sim.TryRemove(solar));
            Assert.AreEqual(140f, sim.Resources.GetStock(ResourceType.Metal), Eps);
        }

        [Test]
        public void Tick_AdvancesResourcesAndTime()
        {
            var sim = Sim();
            sim.Tick(1f);
            Assert.AreEqual(1f, sim.ElapsedSeconds, Eps);
            Assert.AreEqual(199.2f, sim.Resources.GetStock(ResourceType.Oxygen), Eps);
        }

        [Test]
        public void Meteor_DamagesExteriorModule_AndLeakAppliesNextTick()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var solar);
            var reports = new List<string>();
            sim.EffectReported += (m, _) => reports.Add(m);

            sim.Events.Trigger(_meteor);
            Assert.IsTrue(sim.Damage.IsDamaged(solar), "코어 면역 → 유일한 후보인 태양광");
            Assert.AreEqual(1, reports.Count);
            Assert.AreEqual(1, sim.Session.EventsExperienced);

            sim.Tick(1f);
            Assert.AreEqual(10f, sim.Resources.PowerSupply, Eps, "5 + 10 × 0.5");
            Assert.AreEqual(0.8f + 0.3f, sim.Resources.GetConsumption(ResourceType.Oxygen), Eps);
        }

        [Test]
        public void UnrepairedModule_DestroyedAndRemovedFromGrid()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var solar);
            sim.Damage.Damage(solar);
            for (int i = 0; i < 120; i++)
                sim.Tick(1f);
            Assert.IsFalse(sim.Grid.IsOccupied(Vector3Int.right));
            Assert.AreEqual(1, sim.Session.ModulesDestroyed);
        }

        [Test]
        public void Repair_ChargesCostAndRestores()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var solar);
            sim.Damage.Damage(solar);
            float before = sim.Resources.GetStock(ResourceType.Metal);
            Assert.AreEqual(RepairResult.Started, sim.TryRepair(solar));
            Assert.AreEqual(before - 6f, sim.Resources.GetStock(ResourceType.Metal), Eps);
            for (int i = 0; i < 10; i++)
                sim.Tick(1f);
            Assert.IsFalse(sim.Damage.IsDamaged(solar));
        }

        [Test]
        public void GradeScaling_MeteorCountAndIntensity()
        {
            // 두 번째 등급(소형): 운석 2~3개, 강도 ×1.3 / 조건을 낮춰 바로 도달
            var g = new SerializedObject(_grades);
            var small = g.FindProperty("_grades").GetArrayElementAtIndex(1);
            small.FindPropertyRelative("_minPopulation").intValue = 0;
            small.FindPropertyRelative("_minModules").intValue = 4;
            small.FindPropertyRelative("_meteorHitsMin").intValue = 2;
            small.FindPropertyRelative("_meteorHitsMax").intValue = 3;
            small.FindPropertyRelative("_eventIntensityMultiplier").floatValue = 1.3f;
            small.FindPropertyRelative("_eventIntervalMultiplier").floatValue = 0.75f;
            g.ApplyModifiedPropertiesWithoutUndo();

            var sim = Sim(); // Random01 = 0 → 최소 개수
            sim.TryPlace(_solar, Vector3Int.right, 0, out _);
            sim.TryPlace(_solar, Vector3Int.left, 0, out _);
            sim.TryPlace(_solar, Vector3Int.up, 0, out _);
            Assert.AreEqual(1, sim.Progression.GradeIndex);
            Assert.AreEqual(1.3f, sim.EventIntensity, Eps);

            sim.Events.Trigger(_meteor);
            Assert.AreEqual(2, sim.Damage.DamagedCount, "소형 최소 2개");

            float metal = sim.Resources.GetStock(ResourceType.Metal); // 150 - 60 = 90
            sim.Events.Trigger(_supply);
            Assert.AreEqual(metal + 60f * 1.3f, sim.Resources.GetStock(ResourceType.Metal), Eps, "보급 ×1.3");
        }

        [Test]
        public void SupplyShip_AddsRewards()
        {
            var sim = Sim();
            sim.Events.Trigger(_supply);
            Assert.AreEqual(200f, sim.Resources.GetStock(ResourceType.Metal), Eps, "150 + 60 → 한도 200");
        }

        // ---- helpers ----

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }

        private ModuleData Module(string name, float cost, float supply = 0, int housing = 0, float storage = 0, bool removable = true)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            if (cost > 0) Fill(so.FindProperty("_buildCost"), R(ResourceType.Metal, cost));
            if (supply > 0) Fill(so.FindProperty("_production"), R(ResourceType.Power, supply));
            so.FindProperty("_housingCapacity").intValue = housing;
            so.FindProperty("_storageBonus").floatValue = storage;
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

        private static void SetGrade(SerializedProperty grade, string name, int pop, int modules, int maxLimited, params ModuleData[] unlocks)
        {
            grade.FindPropertyRelative("_displayName").stringValue = name;
            grade.FindPropertyRelative("_minPopulation").intValue = pop;
            grade.FindPropertyRelative("_minModules").intValue = modules;
            grade.FindPropertyRelative("_maxLimitedModules").intValue = maxLimited;
            var list = grade.FindPropertyRelative("_unlocks");
            list.arraySize = unlocks.Length;
            for (int i = 0; i < unlocks.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = unlocks[i];
        }
    }
}
