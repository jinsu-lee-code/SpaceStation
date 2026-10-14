using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 13-1 공통 판정 고리: 규칙을 정하는 쪽이 없으면 기존 등급 조건 그대로 · 있으면 그쪽이 시스템 개방과 규칙 모듈 해금을 정함.
    /// </summary>
    public class StationRulesTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _radiator;

        private sealed class FakeRules : IStationRules
        {
            public StationRule Open;
            public bool Allows(StationRule rule) => (Open & rule) == rule;
        }

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_heatMinGrade").intValue = 99;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Create<ModuleData>();
            _core.name = "MD_TestCore";
            _radiator = Create<ModuleData>();
            _radiator.name = "MD_TestRadiator";
            var r = new SerializedObject(_radiator);
            r.FindProperty("_requiredRule").intValue = (int)StationRule.Heat;
            r.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            var unlocks = list.GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks");
            unlocks.arraySize = 1;
            unlocks.GetArrayElementAtIndex(0).objectReferenceValue = _radiator;
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
        public void NoRules_UsesGradeConditions()
        {
            var sim = Sim();
            Assert.IsNull(sim.Rules);
            Assert.IsFalse(sim.Heat.Active, "열 등급(99) 전");
            Assert.IsTrue(sim.RuleOpen(StationRule.Heat, true));
            Assert.IsTrue(sim.Progression.IsUnlocked(_radiator), "규칙을 정하는 쪽이 없으면 등급 해금만");
        }

        [Test]
        public void Rules_DecideSystemsAndRuleModules()
        {
            var sim = Sim();
            var rules = new FakeRules();
            sim.SetRules(rules);
            Assert.IsFalse(sim.Heat.Active);
            Assert.IsFalse(sim.Progression.IsUnlocked(_radiator), "열 규칙 전에는 방열판 잠김");
            Assert.IsTrue(sim.Progression.IsRuleLocked(_radiator));

            rules.Open = StationRule.Heat;
            Assert.IsTrue(sim.Heat.Active, "등급 조건(99)과 무관하게 규칙으로 열림");
            Assert.IsTrue(sim.Progression.IsUnlocked(_radiator));

            sim.SetRules(null);
            Assert.IsFalse(sim.Heat.Active, "떼면 다시 등급 조건");
        }

        [Test]
        public void Rules_PolicySlots_OpenAndExpand()
        {
            var policies = Create<PolicyConfig>();
            var p = new SerializedObject(policies);
            var slots = p.FindProperty("_slotsByGrade");
            slots.arraySize = 1;
            slots.GetArrayElementAtIndex(0).intValue = 1;
            p.FindProperty("_expansionSlots").intValue = 2;
            p.ApplyModifiedPropertiesWithoutUndo();

            var sim = Sim(policies);
            Assert.AreEqual(1, sim.Policies.SlotCount, "규칙 없음 = 등급 표");
            var rules = new FakeRules();
            sim.SetRules(rules);
            Assert.AreEqual(0, sim.Policies.SlotCount, "정책 규칙 전");
            rules.Open = StationRule.Policies;
            Assert.AreEqual(1, sim.Policies.SlotCount);
            rules.Open = StationRule.Policies | StationRule.PolicyExpansion;
            Assert.AreEqual(3, sim.Policies.SlotCount, "확장 +2");
        }

        [Test]
        public void SynthesisStage_MapsToRule()
        {
            Assert.AreEqual(StationRule.Synthesis, StationRules.ForSynthesisStage(1));
            Assert.AreEqual(StationRule.SynthesisStage2, StationRules.ForSynthesisStage(2));
            Assert.AreEqual(StationRule.SynthesisStage3, StationRules.ForSynthesisStage(3));
            Assert.AreEqual(StationRule.SynthesisStage3, StationRules.ForSynthesisStage(4));
        }

        private StationSimulation Sim(PolicyConfig policies = null) => new StationSimulation(new StationSimulationSettings
        {
            Policies = policies,
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
            HazardRandom01 = () => 0.99f,
        });

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
