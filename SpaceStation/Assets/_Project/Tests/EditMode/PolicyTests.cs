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
    /// 12-3c 국가급 상시 정책 (슬롯 · 적용 대기 · 교체 대기 · 중복 · 효과 합산) + 사회 위기 시험 사건 '파업' (경고 → 선택지 4개 · 저절로 풀림 · 세이브).
    /// </summary>
    public class PolicyTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private PolicyConfig _policyConfig;
        private PolicyData _nightShift, _rationing, _festival;
        private ModuleData _core;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 10;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Metal, 500), R(ResourceType.Food, 200));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 1000), R(ResourceType.Metal, 1000), R(ResourceType.Food, 1000));
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_satisfactionRecoveryPerSecond").floatValue = 0f;
            b.FindProperty("_startingSatisfaction").floatValue = 80f;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Create<ModuleData>();
            _core.name = "MD_TestCore";
            var cso = new SerializedObject(_core);
            cso.FindProperty("_removable").boolValue = false;
            var offsets = cso.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            cso.FindProperty("_housingCapacity").intValue = 100;
            cso.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minPopulation").intValue = 0;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minModules").intValue = 0;
            g.ApplyModifiedPropertiesWithoutUndo();

            _nightShift = Policy("PO_NightShift", "야간 근무", M(PolicyStat.Production, 1.2f), M(PolicyStat.SatisfactionCap, -5f));
            _rationing = Policy("PO_Rationing", "배급제", M(PolicyStat.FoodWaterConsumption, 0.8f), M(PolicyStat.SatisfactionCap, -10f));
            _festival = Policy("PO_Festival", "축제", M(PolicyStat.Production, 0.9f), M(PolicyStat.SatisfactionCap, 10f));
            _policyConfig = Create<PolicyConfig>();
            var p = new SerializedObject(_policyConfig);
            var slots = p.FindProperty("_slotsByGrade");
            slots.arraySize = 1;
            slots.GetArrayElementAtIndex(0).intValue = 2;
            p.FindProperty("_applyDelay").floatValue = 30f;
            p.FindProperty("_swapCooldown").floatValue = 300f;
            p.FindProperty("_crisisMinGrade").intValue = 0;
            p.FindProperty("_strikeThreshold").floatValue = 50f;
            p.FindProperty("_strikeBrewSeconds").floatValue = 10f;
            p.FindProperty("_strikeWarningSeconds").floatValue = 60f;
            p.FindProperty("_strikeRecoverMargin").floatValue = 10f;
            p.FindProperty("_strikeDuration").floatValue = 20f;
            p.FindProperty("_strikeProduction").floatValue = 0.5f;
            p.FindProperty("_crisisCooldown").floatValue = 100f;
            p.FindProperty("_crackdownLeave").intValue = 3;
            p.FindProperty("_crackdownSatisfaction").floatValue = -10f;
            p.ApplyModifiedPropertiesWithoutUndo();
            _policyConfig.SetPolicies(new List<PolicyData> { _nightShift, _rationing, _festival });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void WithoutPolicyConfig_NoSystems_EffectsNeutral()
        {
            var sim = Sim(null);
            Assert.IsNull(sim.Policies);
            Assert.IsNull(sim.Crisis);
            Assert.AreEqual(1f, sim.Effects.Policy(PolicyStat.Production));
            Assert.AreEqual(0f, sim.Effects.Policy(PolicyStat.SatisfactionCap));
        }

        [Test]
        public void Assign_TakesEffectAfterDelay_ThenSlotCoolsDown()
        {
            var sim = Sim(_policyConfig);
            var pol = sim.Policies;
            Assert.AreEqual(2, pol.SlotCount);
            Assert.AreEqual(PolicySystem.AssignResult.Ok, pol.Assign(0, _nightShift));
            sim.Tick(10f);
            Assert.AreEqual(1f, sim.Effects.Policy(PolicyStat.Production), "적용 대기 중에는 효과 없음");
            sim.Tick(25f);
            Assert.AreEqual(1.2f, sim.Effects.Policy(PolicyStat.Production), 1e-4f, "30초 뒤 시행");
            Assert.AreEqual(-5f, sim.Effects.Policy(PolicyStat.SatisfactionCap), 1e-4f);
            Assert.IsTrue(pol.IsActive(_nightShift));

            Assert.AreEqual(PolicySystem.AssignResult.Cooldown, pol.Assign(0, _rationing), "교체 대기");
            Assert.AreEqual(PolicySystem.AssignResult.Duplicate, pol.Assign(1, _nightShift), "같은 정책을 두 슬롯에 못 검");
            Assert.AreEqual(PolicySystem.AssignResult.NoSlot, pol.Assign(2, _rationing));
            Assert.AreEqual(PolicySystem.AssignResult.Ok, pol.Assign(1, _festival));
            sim.Tick(30f);
            Assert.AreEqual(1.2f * 0.9f, sim.Effects.Policy(PolicyStat.Production), 1e-4f, "배율은 곱함");
            Assert.AreEqual(5f, sim.Effects.Policy(PolicyStat.SatisfactionCap), 1e-4f, "상한은 더함");

            sim.Tick(300f);
            Assert.AreEqual(PolicySystem.AssignResult.Ok, pol.Assign(0, _rationing), "교체 대기가 끝나면 바꿀 수 있음");
            sim.Tick(1f);
            Assert.AreEqual(1.2f * 0.9f, sim.Effects.Policy(PolicyStat.Production), 1e-4f, "새 정책이 발효될 때까지 옛 정책 유지");
            sim.Tick(30f);
            Assert.AreEqual(0.9f, sim.Effects.Policy(PolicyStat.Production), 1e-4f);
            Assert.AreEqual(0.8f, sim.Resources.GetResidentConsumptionMultiplier(ResourceType.Food), 1e-4f, "배급제 → 주민 식량 소비");
        }

        [Test]
        public void Strike_WarnsAfterLowSatisfaction_RecoversOrChoices()
        {
            var sim = Sim(_policyConfig);
            var crisis = sim.Crisis;
            sim.Population.SetSatisfaction(40f);
            sim.Tick(5f);
            Assert.AreEqual(SocialCrisisSystem.CrisisPhase.Calm, crisis.Phase);
            sim.Tick(6f);
            Assert.AreEqual(SocialCrisisSystem.CrisisPhase.Warning, crisis.Phase, "만족도 50 미만 10초 → 경고");

            sim.Population.SetSatisfaction(61f);
            sim.Tick(1f);
            Assert.AreEqual(SocialCrisisSystem.CrisisPhase.Calm, crisis.Phase, "기준 + 10 이상이면 저절로 풀림");
        }

        [Test]
        public void Strike_Choices_Crackdown_Ignore_Wage()
        {
            var sim = Sim(_policyConfig);
            var crisis = sim.Crisis;
            crisis.BeginWarning();
            int pop = sim.Resources.Population;
            sim.Population.SetSatisfaction(40f);
            Assert.IsTrue(crisis.Choose(SocialCrisisSystem.Choice.Crackdown));
            Assert.AreEqual(pop - 3, sim.Resources.Population, "강경 대응 = 주민 이탈");
            Assert.AreEqual(30f, sim.Population.Satisfaction, 1e-3f);
            Assert.AreEqual(SocialCrisisSystem.CrisisPhase.Calm, crisis.Phase);
            Assert.IsFalse(crisis.Choose(SocialCrisisSystem.Choice.Ignore), "경고가 아니면 선택 불가");

            crisis.BeginWarning();
            Assert.IsTrue(crisis.Choose(SocialCrisisSystem.Choice.Ignore));
            Assert.AreEqual(SocialCrisisSystem.CrisisPhase.Striking, crisis.Phase);
            Assert.AreEqual(0.5f, sim.Effects.Policy(PolicyStat.Production), 1e-4f, "파업 = 생산 절반");
            sim.Tick(21f);
            Assert.AreEqual(SocialCrisisSystem.CrisisPhase.Calm, crisis.Phase);
            Assert.AreEqual(1f, sim.Effects.Policy(PolicyStat.Production), 1e-4f, "파업이 끝나면 원래대로");

            crisis.BeginWarning();
            Assert.IsTrue(crisis.Choose(SocialCrisisSystem.Choice.Wage));
            Assert.Greater(sim.Effects.Policy(PolicyStat.SatisfactionCap), 0f, "임금 인상 = 일시 만족도 상한");
        }

        [Test]
        public void SaveRestore_KeepsSlotsAndCrisis()
        {
            var sim = Sim(_policyConfig);
            sim.Policies.Assign(0, _nightShift);
            sim.Tick(31f);
            sim.Population.SetSatisfaction(40f); // 높으면 경고가 저절로 풀림
            sim.Crisis.BeginWarning();
            sim.Tick(5f);
            string ps = sim.Policies.Capture(_ => -1);
            string cs = sim.Crisis.Capture(_ => -1);

            var loaded = Sim(_policyConfig);
            loaded.Policies.Restore(ps, _ => null);
            loaded.Crisis.Restore(cs, _ => null);
            Assert.IsTrue(loaded.Policies.IsActive(_nightShift));
            Assert.AreEqual(1.2f, loaded.Effects.Policy(PolicyStat.Production), 1e-4f, "복원하면 효과도 다시 계산");
            Assert.Greater(loaded.Policies.Slots[0].Cooldown, 0f);
            Assert.AreEqual(SocialCrisisSystem.CrisisPhase.Warning, loaded.Crisis.Phase);
            Assert.AreEqual(sim.Crisis.TimeLeft, loaded.Crisis.TimeLeft, 1e-3f);
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim(PolicyConfig policies) => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
            HazardRandom01 = () => 0.5f,
            Policies = policies,
        });

        private static PolicyModifier M(PolicyStat s, float v) => new PolicyModifier { Stat = s, Value = v };

        private PolicyData Policy(string file, string name, params PolicyModifier[] modifiers)
        {
            var p = Create<PolicyData>();
            p.name = file;
            p.Set(name, "", modifiers);
            return p;
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
