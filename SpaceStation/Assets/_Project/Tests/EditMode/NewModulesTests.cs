using System;
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
    /// 12-5 새 기본 모듈: 준비 중은 지을 수 없음 · 센서 어레이 예보 +초(상한) · 데이터 센터 연구 속도 · 여객 도킹 포트 이민선 · 세이브.
    /// (연결 허브는 AdjacencyTests.ConnectorHub_RelaysAdjacency_OneStepOnly)
    /// </summary>
    public class NewModulesTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _sensor, _dataCenter, _port, _school;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 10;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 50000), R(ResourceType.Metal, 50000), R(ResourceType.Food, 50000), R(ResourceType.Water, 50000));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 100000), R(ResourceType.Metal, 100000), R(ResourceType.Food, 100000), R(ResourceType.Water, 100000));
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.FindProperty("_startingSatisfaction").floatValue = 80f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", so =>
            {
                so.FindProperty("_removable").boolValue = false;
                so.FindProperty("_housingCapacity").intValue = 1000; // 자연 증가로 먼저 차지 않게
            });
            _sensor = Module("MD_TestSensor", so => so.FindProperty("_warningBonus").floatValue = 40f);
            _dataCenter = Module("MD_TestDataCenter", so => so.FindProperty("_researchSpeedBonus").floatValue = 0.1f);
            _port = Module("MD_TestPort", so => so.FindProperty("_immigrationPort").boolValue = true);
            _school = Module("MD_TestSchool", so =>
            {
                so.FindProperty("_comingSoon").boolValue = true;
                so.FindProperty("_comingSoonNote").stringValue = "새 요구 '교육' · 12-6";
            });

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minPopulation").intValue = 0;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minModules").intValue = 0;
            var unlocks = list.GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks");
            var all = new[] { _sensor, _dataCenter, _port, _school };
            unlocks.arraySize = all.Length;
            for (int i = 0; i < all.Length; i++)
                unlocks.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
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
        public void ComingSoon_IsListedButNotBuildable()
        {
            var sim = Sim();
            Assert.IsTrue(_school.ComingSoon);
            Assert.IsFalse(sim.Progression.IsUnlocked(_school), "등급 해금 목록에 있어도 준비 중이면 잠김");
            Assert.IsFalse(sim.TryPlace(_school, Vector3Int.right, 0, out _));
            Assert.IsTrue(sim.Progression.IsUnlocked(_sensor));
        }

        [Test]
        public void SensorArray_AddsWarningSeconds_UpToCap()
        {
            var sim = Sim();
            Assert.AreEqual(0f, sim.SensorWarningBonus, Eps);
            Assert.IsTrue(sim.TryPlace(_sensor, Vector3Int.right, 0, out _));
            sim.Tick(0.1f);
            Assert.AreEqual(40f, sim.SensorWarningBonus, Eps);
            Assert.IsTrue(sim.TryPlace(_sensor, Vector3Int.left, 0, out _));
            sim.Tick(0.1f);
            Assert.AreEqual(_config.SensorWarningCap, sim.SensorWarningBonus, Eps, "합 80 → 상한 60");
        }

        [Test]
        public void DataCenter_AddsResearchSpeed()
        {
            var sim = Sim();
            float before = sim.Effects.ResearchSpeedMultiplier;
            Assert.IsTrue(sim.TryPlace(_dataCenter, Vector3Int.right, 0, out _));
            Assert.IsTrue(sim.TryPlace(_dataCenter, Vector3Int.left, 0, out _));
            sim.Tick(0.1f);
            Assert.AreEqual(0.2f, sim.Effects.DataCenterResearchBonus, Eps);
            Assert.AreEqual(before * 1.2f, sim.Effects.ResearchSpeedMultiplier, Eps);
        }

        [Test]
        public void PassengerPort_BringsImmigrants_FasterWithMorePorts()
        {
            // 만족도 조건은 아래 테스트에서 따로 (시험 정거장은 요구 상한 때문에 만족도가 낮게 묶임)
            var b = new SerializedObject(_config);
            b.FindProperty("_immigrationMinSatisfaction").floatValue = 0f;
            b.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim();
            Step(sim, _config.ImmigrationInterval + 5f);
            Assert.AreEqual(0, sim.Immigration.Arrived, "포트 없으면 이민선 없음");
            Assert.Less(sim.Immigration.TimeLeft, 0f);

            int changes = 0;
            sim.Population.PopulationChanged += (n, reason) =>
            {
                if (reason == PopulationChangeReason.Immigration)
                    changes += n;
            };
            Assert.IsTrue(sim.TryPlace(_port, Vector3Int.right, 0, out _));
            Step(sim, _config.ImmigrationInterval + 2f);
            Assert.AreEqual(_config.ImmigrationSize, sim.Immigration.Arrived);
            Assert.AreEqual(_config.ImmigrationSize, changes);

            Assert.IsTrue(sim.TryPlace(_port, Vector3Int.left, 0, out _));
            Step(sim, _config.ImmigrationInterval / 2f + 2f);
            Assert.AreEqual(_config.ImmigrationSize * 2, sim.Immigration.Arrived, "포트 2개 → 간격 절반");
        }

        [Test]
        public void PassengerPort_NoRoomOrLowSatisfaction_NobodyLands()
        {
            var b = new SerializedObject(_config);
            b.FindProperty("_immigrationMinSatisfaction").floatValue = 101f;
            b.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim();
            Assert.IsTrue(sim.TryPlace(_port, Vector3Int.right, 0, out _));
            Step(sim, _config.ImmigrationInterval + 2f);
            Assert.AreEqual(0, sim.Immigration.Arrived, "만족도 기준 미달");

            b.FindProperty("_immigrationMinSatisfaction").floatValue = 0f;
            b.ApplyModifiedPropertiesWithoutUndo();
            sim.Resources.SetPopulation(sim.Resources.HousingCapacity);
            Step(sim, _config.ImmigrationInterval + 2f);
            Assert.AreEqual(0, sim.Immigration.Arrived, "빈 거주 공간 없음");
        }

        [Test]
        public void Immigration_SaveRestore_KeepsTimerAndCount()
        {
            var sim = Sim();
            Assert.IsTrue(sim.TryPlace(_port, Vector3Int.right, 0, out _));
            Step(sim, _config.ImmigrationInterval + 30f);
            string state = sim.Immigration.Capture(_ => -1);
            Assert.IsNotNull(state);

            var loaded = Sim();
            loaded.Immigration.Restore(state, _ => null);
            Assert.AreEqual(sim.Immigration.Arrived, loaded.Immigration.Arrived);
            Assert.AreEqual(sim.Immigration.TimeLeft, loaded.Immigration.TimeLeft, Eps);
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim() => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
            HazardRandom01 = () => 0.99f,
        });

        private static void Step(StationSimulation sim, float seconds)
        {
            for (float t = 0f; t < seconds; t += 1f)
                sim.Tick(1f);
        }

        private ModuleData Module(string name, Action<SerializedObject> setup)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            setup(so);
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
