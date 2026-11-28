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
    /// CONNECT_1121 7번 ② 타이쿤판 비리: 열리기 전 없음 · 굴림 → 경고 · 선택지 4개 · 횡령 (금속 생산 · 저장 금속) · 행정 부족할수록 확률 ↑ ·
    /// 파업 연결 (소비 부족 → 기준 ↑ · 방치 → 다음 파업 기준 +10, 다음 파업 사건이 끝나면 사라짐) · 세이브.
    /// </summary>
    public class CorruptionTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private CorruptionConfig _corruption;
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
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 50000), R(ResourceType.Metal, 1000), R(ResourceType.Food, 50000), R(ResourceType.Water, 50000));
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

            _corruption = Create<CorruptionConfig>();
            var p = new SerializedObject(_corruption);
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
        public void ClosedRule_NoCorruption_NoStrikeBonus()
        {
            var (sim, corruption) = Sim();
            sim.SetRules(new NoRules());
            AddNeed(ResidentNeed.Commerce);
            Step(sim, 30f);
            Assert.AreEqual(CorruptionSystem.CorruptionPhase.Calm, corruption.Phase, "사회 사건 규칙 전에는 없음");
            Assert.AreEqual(0f, corruption.StrikeBonus, Eps, "규칙 전에는 파업 기준도 그대로");
        }

        [Test]
        public void Roll_BelowChance_StartsWarning_ThenIgnoreEmbezzles()
        {
            var (sim, corruption) = Sim();
            _roll = 0.99f;
            Step(sim, 11f);
            Assert.AreEqual(CorruptionSystem.CorruptionPhase.Calm, corruption.Phase, "확률(0.1) 위로 굴리면 없음");
            _roll = 0.05f;
            Step(sim, 11f);
            Assert.AreEqual(CorruptionSystem.CorruptionPhase.Warning, corruption.Phase);

            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.IsTrue(corruption.Choose(CorruptionSystem.Choice.Ignore));
            Assert.AreEqual(CorruptionSystem.CorruptionPhase.Embezzling, corruption.Phase);
            Assert.AreEqual(metal * _corruption.EmbezzleStoredLoss, corruption.Lost, 0.5f, "저장 금속 10%");
            Assert.AreEqual(metal - corruption.Lost, sim.Resources.GetStock(ResourceType.Metal), 0.5f);
            sim.Tick(0.1f);
            Assert.AreEqual(_corruption.EmbezzleMetal, sim.Effects.Policy(PolicyStat.MetalProduction), Eps, "횡령 중 금속 생산 절반");
            Assert.IsTrue(corruption.StrikePending);
            Step(sim, _corruption.EmbezzleSeconds + 1f);
            Assert.AreEqual(CorruptionSystem.CorruptionPhase.Calm, corruption.Phase);
            Assert.AreEqual(1f, sim.Effects.Policy(PolicyStat.MetalProduction), Eps, "끝나면 금속 생산 회복");
        }

        [Test]
        public void WarningTimeout_Embezzles()
        {
            var (sim, corruption) = Sim();
            corruption.BeginWarning();
            Step(sim, _corruption.WarningSeconds + 1f);
            Assert.AreEqual(CorruptionSystem.CorruptionPhase.Embezzling, corruption.Phase, "대응하지 않으면 횡령");
        }

        [Test]
        public void Choices_Audit_Purge_Transparency()
        {
            var (sim, corruption) = Sim();
            corruption.BeginWarning();
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.IsTrue(corruption.Choose(CorruptionSystem.Choice.Audit));
            Assert.AreEqual(CorruptionSystem.CorruptionPhase.Calm, corruption.Phase);
            Assert.AreEqual(metal - 100f, sim.Resources.GetStock(ResourceType.Metal), 0.5f, "감사 = 금속 100");
            Assert.IsFalse(corruption.StrikePending, "막으면 파업 기준 그대로");

            corruption.BeginWarning();
            int pop = sim.Resources.Population;
            Assert.IsTrue(corruption.Choose(CorruptionSystem.Choice.Purge));
            Assert.AreEqual(pop - _corruption.PurgeLeave, sim.Resources.Population);

            corruption.BeginWarning();
            Assert.IsTrue(corruption.Choose(CorruptionSystem.Choice.Transparency));
            Assert.AreEqual(CorruptionSystem.CorruptionPhase.Calm, corruption.Phase);
            sim.Tick(0.1f);
            Assert.AreEqual(_corruption.TransparencyResearch, sim.Effects.Policy(PolicyStat.ResearchSpeed), Eps, "일시 정책 연구 감소");
            Assert.IsFalse(corruption.Choose(CorruptionSystem.Choice.Ignore), "경고 중이 아니면 선택 불가");
        }

        [Test]
        public void Audit_NeedsMetal()
        {
            var (sim, corruption) = Sim();
            sim.Resources.SetStock(ResourceType.Metal, 50f);
            corruption.BeginWarning();
            Assert.IsFalse(corruption.CanChoose(CorruptionSystem.Choice.Audit), "금속 100이 없으면 감사 불가");
            Assert.IsTrue(corruption.CanChoose(CorruptionSystem.Choice.Purge));
        }

        [Test]
        public void Chance_RisesWithAdministrationShortage()
        {
            var (sim, corruption) = Sim();
            Assert.AreEqual(0f, corruption.Shortage, Eps, "요구가 없으면 부족 없음");
            Assert.AreEqual(0.1f, corruption.Chance, Eps);
            AddNeed(ResidentNeed.Administration); // 행정 구역이 없으니 100% 부족
            sim.Tick(0.1f);
            Assert.AreEqual(1f, corruption.Shortage, Eps);
            Assert.AreEqual(0.6f, corruption.Chance, Eps);
        }

        [Test]
        public void StrikeThreshold_CommerceShortage_AndIgnoredCorruption()
        {
            var (sim, corruption) = Sim();
            float baseThreshold = sim.Crisis.Threshold;
            AddNeed(ResidentNeed.Commerce); // 상업 구역이 없으니 100% 부족
            sim.Tick(0.1f);
            Assert.AreEqual(1f, corruption.CommerceShortage, Eps);
            Assert.AreEqual(baseThreshold + _corruption.CommerceStrikeMax, sim.Crisis.Threshold, Eps, "소비 부족 100% = 기준 +20");

            corruption.BeginWarning();
            corruption.Choose(CorruptionSystem.Choice.Ignore);
            Assert.AreEqual(baseThreshold + _corruption.CommerceStrikeMax + _corruption.IgnoredStrikeBonus, sim.Crisis.Threshold, Eps, "방치 = 다음 파업 기준 +10");
        }

        [Test]
        public void IgnoredBonus_ClearsAfterNextStrikeEpisode()
        {
            var (sim, corruption) = Sim();
            corruption.BeginWarning();
            corruption.Choose(CorruptionSystem.Choice.Ignore);
            Assert.IsTrue(corruption.StrikePending);
            sim.Tick(0.1f);
            Assert.IsTrue(corruption.StrikePending, "파업 경고 전에는 남음");
            sim.Crisis.BeginWarning(); // 다음 파업 경고 (만족도 80이 기준 위라 곧 가라앉음)
            for (int i = 0; i < 5; i++)
                sim.Tick(1f);
            Assert.AreEqual(SocialCrisisSystem.CrisisPhase.Calm, sim.Crisis.Phase, "만족도가 기준 위라 가라앉음");
            Assert.IsFalse(corruption.StrikePending, "다음 파업 사건이 끝나면 한 번만");
        }

        [Test]
        public void SaveRestore_KeepsEmbezzle()
        {
            var (sim, corruption) = Sim();
            corruption.BeginWarning();
            corruption.Choose(CorruptionSystem.Choice.Ignore);
            Step(sim, 20f);
            string state = corruption.Capture(_ => -1);
            var (_, copy) = Sim();
            copy.Restore(state, _ => null);
            Assert.AreEqual(CorruptionSystem.CorruptionPhase.Embezzling, copy.Phase);
            Assert.AreEqual(corruption.TimeLeft, copy.TimeLeft, Eps);
            Assert.AreEqual(corruption.Lost, copy.Lost, Eps);
            Assert.IsTrue(copy.StrikePending);
        }

        // ---------------- 도우미 ----------------

        private sealed class NoRules : IStationRules
        {
            public bool Allows(StationRule rule) => false;
        }

        private void AddNeed(ResidentNeed need)
        {
            var g = new SerializedObject(_grades);
            var needs = g.FindProperty("_grades").GetArrayElementAtIndex(0).FindPropertyRelative("_newNeeds");
            needs.arraySize++;
            needs.GetArrayElementAtIndex(needs.arraySize - 1).enumValueIndex = (int)need;
            g.ApplyModifiedPropertiesWithoutUndo();
        }

        private (StationSimulation, CorruptionSystem) Sim()
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
            return (sim, CorruptionSystem.AttachTo(sim, _corruption, () => _roll));
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
