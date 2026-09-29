using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>BALANCE 14번: 낮/밤 주기와 배터리.</summary>
    public class PowerStorageTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private ModuleData _core, _solar, _battery, _load;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BalanceConfig>();
            _created.Add(_config);
            var so = new SerializedObject(_config);
            so.FindProperty("_minPowerEfficiency").floatValue = 0.25f;
            so.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("Core", supply: 5);
            _solar = Module("Solar", supply: 10, solarPowered: true);
            _battery = Module("Battery", batteryCapacity: 200, batteryRate: 10);
            _load = Module("Load", demand: 12);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ---- 낮/밤 주기 ----

        [Test]
        public void Cycle_StepAndTransition()
        {
            var c = new DayNightCycle(300f, 180f, 10f, 0f);
            Assert.AreEqual(1f, c.SolarMultiplier(0f), Eps);
            Assert.AreEqual(1f, c.SolarMultiplier(170f), Eps);
            Assert.AreEqual(0.5f, c.SolarMultiplier(175f), Eps, "해질녘 중간");
            Assert.AreEqual(0f, c.SolarMultiplier(200f), Eps, "밤");
            Assert.AreEqual(0.5f, c.SolarMultiplier(295f), Eps, "새벽 중간");
            Assert.AreEqual(1f, c.SolarMultiplier(300f), Eps, "다음 주기");
            Assert.IsTrue(c.IsDay(179f));
            Assert.IsFalse(c.IsDay(181f));
            Assert.AreEqual(20f, c.TimeUntilPhaseChange(160f), Eps);
            Assert.AreEqual(100f, c.TimeUntilPhaseChange(200f), Eps);
        }

        [Test]
        public void Cycle_DisabledWhenPeriodZero()
        {
            var c = new DayNightCycle(0f, 0f, 0f, 0f);
            Assert.AreEqual(1f, c.SolarMultiplier(12345f));
            Assert.IsTrue(c.IsDay(5f));
        }

        // ---- 태양광 배율 ----

        [Test]
        public void SolarMultiplier_AffectsOnlySolarPowered()
        {
            var sim = new ResourceSimulation(_config);
            sim.SolarMultiplier = 0f;
            sim.Tick(new[] { _core, _solar }, 1f);
            Assert.AreEqual(5f, sim.PowerSupply, Eps, "밤: 코어만");
        }

        // ---- 배터리 ----

        [Test]
        public void Battery_ChargesWithSurplus_RateLimited()
        {
            var sim = new ResourceSimulation(_config);
            sim.Tick(new[] { _core, _solar, _battery }, 1f); // 잉여 15 → 속도 한도 10
            Assert.AreEqual(10f, sim.BatteryCharge, Eps);
            Assert.AreEqual(10f, sim.BatteryFlow, Eps);
            for (int i = 0; i < 30; i++)
                sim.Tick(new[] { _core, _solar, _battery }, 1f);
            Assert.AreEqual(200f, sim.BatteryCharge, Eps, "용량 한도");
            Assert.AreEqual(0f, sim.BatteryFlow, Eps);
        }

        [Test]
        public void Battery_CoversDeficit_KeepsFullEfficiency()
        {
            var sim = new ResourceSimulation(_config);
            for (int i = 0; i < 20; i++)
                sim.Tick(new[] { _core, _solar, _battery }, 1f); // 200 충전

            sim.SolarMultiplier = 0f; // 밤: 공급 5, 수요 12 → 부족 7
            sim.Tick(new[] { _core, _solar, _battery, _load }, 1f);
            Assert.AreEqual(1f, sim.PowerEfficiency, Eps);
            Assert.AreEqual(-7f, sim.BatteryFlow, Eps);
            Assert.AreEqual(193f, sim.BatteryCharge, Eps);
        }

        [Test]
        public void Battery_DischargeRateLimited()
        {
            var sim = new ResourceSimulation(_config);
            for (int i = 0; i < 20; i++)
                sim.Tick(new[] { _core, _solar, _battery }, 1f);
            sim.SolarMultiplier = 0f;
            // 수요 24, 공급 5 → 부족 19, 방전 한도 10 → 효율 15/24
            sim.Tick(new[] { _core, _solar, _battery, _load, _load }, 1f);
            Assert.AreEqual(-10f, sim.BatteryFlow, Eps);
            Assert.AreEqual(15f / 24f, sim.PowerEfficiency, Eps);
        }

        [Test]
        public void Battery_Empty_EfficiencyDrops()
        {
            var sim = new ResourceSimulation(_config);
            sim.SolarMultiplier = 0f;
            sim.Tick(new[] { _core, _battery, _load }, 1f);
            Assert.AreEqual(0f, sim.BatteryCharge, Eps);
            Assert.AreEqual(5f / 12f, sim.PowerEfficiency, Eps);
        }

        [Test]
        public void Battery_RemovedCapacity_ClampsCharge()
        {
            var sim = new ResourceSimulation(_config);
            for (int i = 0; i < 20; i++)
                sim.Tick(new[] { _core, _solar, _battery }, 1f);
            sim.RefreshCapacities(new[] { _core, _solar });
            Assert.AreEqual(0f, sim.BatteryCharge, Eps);
        }

        private ModuleData Module(string name, float supply = 0, float demand = 0, bool solarPowered = false,
            float batteryCapacity = 0, float batteryRate = 0)
        {
            var m = ScriptableObject.CreateInstance<ModuleData>();
            m.name = name;
            _created.Add(m);
            var so = new SerializedObject(m);
            if (supply > 0) Fill(so.FindProperty("_production"), new ResourceAmount(ResourceType.Power, supply));
            if (demand > 0) Fill(so.FindProperty("_consumption"), new ResourceAmount(ResourceType.Power, demand));
            so.FindProperty("_solarPowered").boolValue = solarPowered;
            so.FindProperty("_batteryCapacity").floatValue = batteryCapacity;
            so.FindProperty("_batteryRate").floatValue = batteryRate;
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

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
