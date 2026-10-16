using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceStation.Tests
{
    /// <summary>13-6 타이쿤판 1인칭 가볍게: 고리 값 설정 · 등급 첫 도달 연구 포인트(여러 등급 한 번에 · 내려갔다 올라도 한 번) · 세이브.</summary>
    public class InteriorBonusTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private InteriorBonusConfig _bonus;
        private ModuleData _core;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            _core = Create<ModuleData>();
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                list.GetArrayElementAtIndex(i).FindPropertyRelative("_minPopulation").intValue = i * 10;
                list.GetArrayElementAtIndex(i).FindPropertyRelative("_minModules").intValue = 0;
            }
            g.ApplyModifiedPropertiesWithoutUndo();
            _bonus = Create<InteriorBonusConfig>();
            _bonus.Set(0f, 0.5f, new List<int> { 0, 1, 2 }); // 목록 밖 등급 = 마지막 값 2
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Attach_SetsHooks()
        {
            var (sim, _) = Sim();
            Assert.AreEqual(0f, sim.Incidents.MissScale);
            Assert.AreEqual(0.5f, sim.Supply.AutoCollectRate);
            Assert.AreEqual("손실 없음", sim.MissIncident(IncidentKind.Fire, sim.Core));
        }

        [Test]
        public void GradeUp_AwardsPointsOnce_IncludingSkippedGrades_AndSaves()
        {
            var (sim, b) = Sim();
            b.Tick(1f);
            Assert.AreEqual(0, sim.Supply.ResearchPoints, "등급 없음 = 0");
            Reach(sim, 1);
            b.Tick(1f);
            Assert.AreEqual(1, sim.Supply.ResearchPoints);
            Reach(sim, 3);
            b.Tick(1f);
            Assert.AreEqual(1 + 2 + 2, sim.Supply.ResearchPoints, "건너뛴 등급 2도 받음, 3은 목록 밖 = 마지막 값");
            Reach(sim, 1);
            b.Tick(1f);
            Reach(sim, 3);
            b.Tick(1f);
            Assert.AreEqual(5, sim.Supply.ResearchPoints, "내려갔다 다시 올라도 한 번만");

            var state = b.Capture(_ => 0);
            var (sim2, b2) = Sim();
            b2.Restore(state, _ => null);
            Assert.AreEqual(3, b2.RewardedGrade);
            Reach(sim2, 3);
            b2.Tick(1f);
            Assert.AreEqual(0, sim2.Supply.ResearchPoints, "복원한 판은 이미 받은 등급을 다시 안 줌");
        }

        private (StationSimulation, InteriorBonusSystem) Sim()
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
            return (sim, InteriorBonusSystem.AttachTo(sim, _bonus));
        }

        private static void Reach(StationSimulation sim, int grade)
        {
            sim.Progression.Evaluate(grade * 10, 0);
            Assert.AreEqual(grade, sim.Progression.GradeIndex);
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
