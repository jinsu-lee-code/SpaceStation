using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>BALANCE.md 8번 수치로 인구/만족도 규칙을 검증한다.</summary>
    public class PopulationSimulationTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private ResourceSimulation _resources;
        private PopulationSimulation _population;
        private List<(int delta, PopulationChangeReason reason)> _events;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BalanceConfig>();
            _created.Add(_config);
            var so = new SerializedObject(_config);
            so.FindProperty("_startingPopulation").intValue = 4;
            Fill(so.FindProperty("_consumptionPerResident"), new[] { R(ResourceType.Oxygen, 0.2f), R(ResourceType.Water, 0.1f), R(ResourceType.Food, 0.1f) });
            Fill(so.FindProperty("_startingResources"), new[] { R(ResourceType.Oxygen, 100), R(ResourceType.Water, 100), R(ResourceType.Food, 100) });
            Fill(so.FindProperty("_baseStorageCapacity"), new[] { R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200) });
            so.FindProperty("_startingSatisfaction").floatValue = 70f;
            Fill(so.FindProperty("_satisfactionPenaltyPerSecond"), new[] { R(ResourceType.Oxygen, 2), R(ResourceType.Water, 1), R(ResourceType.Food, 1) });
            so.FindProperty("_satisfactionRecoveryPerSecond").floatValue = 0.5f;
            so.FindProperty("_growthMinSatisfaction").floatValue = 50f;
            so.FindProperty("_growthIntervalAtMinSatisfaction").floatValue = 30f;
            so.FindProperty("_growthIntervalAtMaxSatisfaction").floatValue = 10f;
            so.FindProperty("_oxygenDepletedLossInterval").floatValue = 10f;
            so.FindProperty("_lowSatisfactionThreshold").floatValue = 25f;
            so.FindProperty("_lowSatisfactionLossInterval").floatValue = 15f;
            so.FindProperty("_overcrowdedLossInterval").floatValue = 5f;
            so.ApplyModifiedPropertiesWithoutUndo();

            _resources = new ResourceSimulation(_config);
            _population = new PopulationSimulation(_config, _resources);
            _events = new List<(int, PopulationChangeReason)>();
            _population.PopulationChanged += (d, r) => _events.Add((d, r));
            SetHousing(10);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Satisfaction_StartsFromConfig_AndRecoversWithoutShortage()
        {
            Assert.AreEqual(70f, _population.Satisfaction, Eps);
            Ticks(10);
            Assert.AreEqual(75f, _population.Satisfaction, Eps);
            Assert.AreEqual(0.5f, _population.SatisfactionRate, Eps);
        }

        [Test]
        public void Satisfaction_DropsPerDepletedResource_Summed_NoRecovery()
        {
            _resources.SetStock(ResourceType.Food, 0f);
            _resources.SetStock(ResourceType.Water, 0f);
            Ticks(1);
            Assert.AreEqual(-2f, _population.SatisfactionRate, Eps);
            Assert.AreEqual(68f, _population.Satisfaction, Eps);
        }

        [Test]
        public void Satisfaction_ClampedTo0And100()
        {
            _population.SetSatisfaction(99.9f);
            Ticks(1);
            Assert.AreEqual(100f, _population.Satisfaction, Eps);

            _population.SetSatisfaction(0.5f);
            _resources.SetStock(ResourceType.Oxygen, 0f);
            Ticks(1);
            Assert.AreEqual(0f, _population.Satisfaction, Eps);
        }

        [Test]
        public void GrowthInterval_ScalesWithSatisfaction()
        {
            Assert.AreEqual(30f, _population.GetGrowthInterval(50f), Eps);
            Assert.AreEqual(20f, _population.GetGrowthInterval(75f), Eps);
            Assert.AreEqual(10f, _population.GetGrowthInterval(100f), Eps);
            Assert.IsTrue(float.IsPositiveInfinity(_population.GetGrowthInterval(49.9f)));
        }

        [Test]
        public void GrowthInterval_RingMultiplier_Shortens()
        {
            // 8-4 회전 링: 간격 ×0.8
            _population.GrowthIntervalMultiplier = 0.8f;
            Assert.AreEqual(24f, _population.GetGrowthInterval(50f), Eps);
            Assert.AreEqual(8f, _population.GetGrowthInterval(100f), Eps);
            Assert.IsTrue(float.IsPositiveInfinity(_population.GetGrowthInterval(49.9f)));
        }

        [Test]
        public void Growth_AtMaxSatisfaction_Every10Seconds()
        {
            _population.SetSatisfaction(100f);
            Ticks(9);
            Assert.AreEqual(4, _resources.Population);
            Ticks(1);
            Assert.AreEqual(5, _resources.Population);
            CollectionAssert.AreEqual(new[] { (1, PopulationChangeReason.Growth) }, _events);
        }

        [Test]
        public void Growth_StopsAtHousingCapacity()
        {
            SetHousing(4);
            _population.SetSatisfaction(100f);
            Ticks(100);
            Assert.AreEqual(4, _resources.Population);
            Assert.IsFalse(_population.IsGrowing);
        }

        [Test]
        public void Growth_BelowMinimumSatisfaction_None()
        {
            _population.SetSatisfaction(40f);
            Ticks(10); // 45까지 회복, 아직 50 미만
            Assert.AreEqual(4, _resources.Population);
            Assert.AreEqual(0f, _population.GrowthProgress, Eps);
        }

        [Test]
        public void Growth_BlockedWhileResidentResourceDepleted()
        {
            _population.SetSatisfaction(100f);
            _resources.SetStock(ResourceType.Food, 0f);
            Ticks(15); // 만족도 100 → 85, 여전히 50 이상이지만 식량 고갈
            Assert.AreEqual(4, _resources.Population);
            Assert.IsTrue(_population.IsGrowthBlockedByShortage);
            Assert.IsFalse(_population.IsGrowing);

            _resources.SetStock(ResourceType.Food, 50f);
            Ticks(1);
            Assert.IsFalse(_population.IsGrowthBlockedByShortage);
            Assert.IsTrue(_population.IsGrowing, "고갈이 풀리면 다시 증가");
        }

        [Test]
        public void Growth_NotBlockedByMetalDepletion()
        {
            _population.SetSatisfaction(100f);
            _resources.SetStock(ResourceType.Metal, 0f);
            Ticks(10);
            Assert.AreEqual(5, _resources.Population, "금속은 거주자 소비 자원이 아님");
        }

        [Test]
        public void OxygenDepleted_LosesOneEvery10Seconds()
        {
            SetHousing(4);
            _population.SetSatisfaction(100f);
            _resources.SetStock(ResourceType.Oxygen, 0f);

            Ticks(9);
            Assert.AreEqual(4, _resources.Population);
            Ticks(1);
            Assert.AreEqual(3, _resources.Population);
            Ticks(10);
            Assert.AreEqual(2, _resources.Population);
            Assert.IsTrue(_events.TrueForAll(e => e.reason == PopulationChangeReason.OxygenDepleted));
        }

        [Test]
        public void LowSatisfaction_LosesOneEvery15Seconds()
        {
            _population.SetSatisfaction(10f); // 회복 +0.5/s → 15초 후 17.5, 여전히 25 미만
            Ticks(14);
            Assert.AreEqual(4, _resources.Population);
            Ticks(1);
            Assert.AreEqual(3, _resources.Population);
            CollectionAssert.AreEqual(new[] { (-1, PopulationChangeReason.LowSatisfaction) }, _events);
        }

        [Test]
        public void Overcrowded_LosesEvery5Seconds_UntilItFits()
        {
            SetHousing(4);
            _resources.SetPopulation(6);
            _population.SetSatisfaction(100f);

            Ticks(5);
            Assert.AreEqual(5, _resources.Population);
            Ticks(5);
            Assert.AreEqual(4, _resources.Population);
            Ticks(20);
            Assert.AreEqual(4, _resources.Population, "수용 인구 이하가 되면 멈춤");
        }

        [Test]
        public void LossTimer_ResetsWhenConditionClears()
        {
            SetHousing(4);
            _population.SetSatisfaction(100f);
            _resources.SetStock(ResourceType.Oxygen, 0f);
            Ticks(9);
            _resources.SetStock(ResourceType.Oxygen, 50f);
            Ticks(1);
            _resources.SetStock(ResourceType.Oxygen, 0f);
            Ticks(9);
            Assert.AreEqual(4, _resources.Population, "고갈이 끊기면 타이머 초기화");
        }

        [Test]
        public void Population_NeverBelowZero()
        {
            _resources.SetPopulation(1);
            _population.SetSatisfaction(0f);
            _resources.SetStock(ResourceType.Oxygen, 0f);
            Ticks(100);
            Assert.AreEqual(0, _resources.Population);
        }

        // ---- helpers ----

        private void Ticks(int count)
        {
            for (int i = 0; i < count; i++)
                _population.Tick(1f);
        }

        private void SetHousing(int capacity)
        {
            var data = ScriptableObject.CreateInstance<ModuleData>();
            _created.Add(data);
            var so = new SerializedObject(data);
            so.FindProperty("_housingCapacity").intValue = capacity;
            so.ApplyModifiedPropertiesWithoutUndo();
            _resources.RefreshCapacities(new[] { data });
        }

        private static ResourceAmount R(ResourceType type, float amount) => new ResourceAmount(type, amount);

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
