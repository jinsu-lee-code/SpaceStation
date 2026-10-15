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
    /// 13-3 타이쿤판 역병: 열리기 전 없음 · 굴림 → 경고 · 선택지 4개 · 역병 피해(인구 · 생산) · 부족할수록 확률 ↑ · 세이브.
    /// </summary>
    public class PlagueTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private PlagueConfig _plague;
        private PolicyConfig _policies;
        private ModuleData _core;
        private float _roll;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 100;
            b.FindProperty("_startingSatisfaction").floatValue = 80f;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 50000), R(ResourceType.Metal, 50000), R(ResourceType.Food, 50000), R(ResourceType.Water, 50000));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 100000), R(ResourceType.Metal, 100000), R(ResourceType.Food, 100000), R(ResourceType.Water, 100000));
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Create<ModuleData>();
            _core.name = "MD_TestCore";
            var c = new SerializedObject(_core);
            c.FindProperty("_cellOffsets").arraySize = 1;
            c.FindProperty("_removable").boolValue = false;
            c.FindProperty("_housingCapacity").intValue = 1000;
            c.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();

            _plague = Create<PlagueConfig>();
            var p = new SerializedObject(_plague);
            p.FindProperty("_minGrade").intValue = 0;
            p.FindProperty("_rollInterval").floatValue = 10f;
            p.FindProperty("_baseChance").floatValue = 0.1f;
            p.FindProperty("_shortageChance").floatValue = 0.5f;
            p.ApplyModifiedPropertiesWithoutUndo();
            _policies = Create<PolicyConfig>();
            _roll = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void ClosedRule_NoPlague()
        {
            var (sim, plague) = Sim();
            sim.SetRules(new NoRules());
            Step(sim, 30f);
            Assert.AreEqual(PlagueSystem.PlaguePhase.Calm, plague.Phase, "사회 사건 규칙 전에는 없음");
        }

        [Test]
        public void Roll_BelowChance_StartsWarning_ThenIgnoreBreaksOut()
        {
            var (sim, plague) = Sim();
            _roll = 0.99f;
            Step(sim, 11f);
            Assert.AreEqual(PlagueSystem.PlaguePhase.Calm, plague.Phase, "확률(0.1) 위로 굴리면 없음");
            _roll = 0.05f;
            Step(sim, 11f);
            Assert.AreEqual(PlagueSystem.PlaguePhase.Warning, plague.Phase);

            int died = 0;
            sim.Population.PopulationChanged += (delta, reason) =>
            {
                if (reason == PopulationChangeReason.Crisis)
                    died -= delta;
            };
            Assert.IsTrue(plague.Choose(PlagueSystem.Choice.Ignore));
            Assert.AreEqual(PlagueSystem.PlaguePhase.Outbreak, plague.Phase);
            sim.Tick(0.1f);
            Assert.Less(sim.Effects.Policy(PolicyStat.Production), 1f, "역병 중 생산 감소");
            Step(sim, _plague.OutbreakSeconds + 1f);
            Assert.AreEqual(PlagueSystem.PlaguePhase.Calm, plague.Phase);
            Assert.Greater(plague.Lost, 0);
            Assert.AreEqual(plague.Lost, died, "잃은 주민 = 사건으로 줄어든 인구");
        }

        [Test]
        public void Choices_Supplies_Lockdown_Campaign()
        {
            var (sim, plague) = Sim();
            plague.BeginWarning("");
            float food = sim.Resources.GetStock(ResourceType.Food);
            Assert.IsTrue(plague.Choose(PlagueSystem.Choice.Supplies));
            Assert.AreEqual(PlagueSystem.PlaguePhase.Calm, plague.Phase);
            Assert.Less(sim.Resources.GetStock(ResourceType.Food), food);

            plague.BeginWarning("");
            int pop = sim.Resources.Population;
            Assert.IsTrue(plague.Choose(PlagueSystem.Choice.Lockdown));
            Assert.AreEqual(pop - _plague.LockdownLeave, sim.Resources.Population);

            plague.BeginWarning("");
            Assert.IsTrue(plague.Choose(PlagueSystem.Choice.Campaign));
            Assert.AreEqual(PlagueSystem.PlaguePhase.Calm, plague.Phase);
            sim.Tick(0.1f);
            Assert.AreEqual(_plague.CampaignProduction, sim.Effects.Policy(PolicyStat.Production), Eps, "일시 정책 생산 감소");
            Assert.IsFalse(plague.Choose(PlagueSystem.Choice.Ignore), "경고 중이 아니면 선택 불가");
        }

        [Test]
        public void Chance_RisesWithShortage()
        {
            var (sim, plague) = Sim();
            Assert.AreEqual(0f, plague.Shortage, Eps, "요구가 없으면 부족 없음");
            Assert.AreEqual(0.1f, plague.Chance, Eps);
            // 의료 요구를 등급에 넣으면 서비스가 없으니 100% 부족
            var g = new SerializedObject(_grades);
            var needs = g.FindProperty("_grades").GetArrayElementAtIndex(0).FindPropertyRelative("_newNeeds");
            needs.arraySize = 1;
            needs.GetArrayElementAtIndex(0).enumValueIndex = (int)ResidentNeed.Medical;
            g.ApplyModifiedPropertiesWithoutUndo();
            sim.Tick(0.1f);
            Assert.AreEqual(1f, plague.Shortage, Eps);
            Assert.AreEqual(0.6f, plague.Chance, Eps);
        }

        [Test]
        public void SaveRestore_KeepsOutbreak()
        {
            var (sim, plague) = Sim();
            plague.BeginWarning("");
            plague.Choose(PlagueSystem.Choice.Ignore);
            Step(sim, 20f);
            string state = plague.Capture(_ => -1);
            var (_, copy) = Sim();
            copy.Restore(state, _ => null);
            Assert.AreEqual(PlagueSystem.PlaguePhase.Outbreak, copy.Phase);
            Assert.AreEqual(plague.TimeLeft, copy.TimeLeft, Eps);
            Assert.AreEqual(plague.Lost, copy.Lost);
        }

        // ---------------- 도우미 ----------------

        private sealed class NoRules : IStationRules
        {
            public bool Allows(StationRule rule) => false;
        }

        private (StationSimulation, PlagueSystem) Sim()
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Policies = _policies,
                Random01 = () => 0.5f,
                HazardRandom01 = () => 0.99f,
            });
            return (sim, PlagueSystem.AttachTo(sim, _plague, () => _roll));
        }

        private static void Step(StationSimulation sim, float seconds)
        {
            for (float t = 0f; t < seconds; t += 1f)
                sim.Tick(1f);
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
