using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// REVIEW_1115 5번 주민 추방 공통 고리 (사용자 결정 2026-11-24): 문제 주민 · 대가 · 같은 종류 배율 · 정책 4종(추방령 · 사면 · 이주 지원 · 귀환 허가) · 규칙 · 떠난 주민 기록 · 세이브.
    /// </summary>
    public class ExileTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private PolicyConfig _policies;
        private ResidentConfig _residents;
        private ModuleData _core;
        private System.Random _rng;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 6;
            b.FindProperty("_startingSatisfaction").floatValue = 80f;
            b.FindProperty("_satisfactionRecoveryPerSecond").floatValue = 0f;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Water, 150), R(ResourceType.Food, 150), R(ResourceType.Metal, 150));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 500), R(ResourceType.Water, 500), R(ResourceType.Food, 500), R(ResourceType.Metal, 500));
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Create<ModuleData>();
            _core.name = "MD_TestCore";
            var cso = new SerializedObject(_core);
            cso.FindProperty("_removable").boolValue = false;
            var offsets = cso.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            cso.FindProperty("_housingCapacity").intValue = 20;
            cso.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minPopulation").intValue = 0;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minModules").intValue = 0;
            g.ApplyModifiedPropertiesWithoutUndo();

            _policies = Create<PolicyConfig>(); // 추방 값 = 기본 (이유 없음 5 · 문제 주민 1 · 같은 종류 최대 ×2)
            _residents = Create<ResidentConfig>();
            UseTraits(Def(ResidentTrait.Technician));
            _rng = new System.Random(3);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Exile_NoReason_CostsFive_AndThatResidentLeaves()
        {
            var sim = Sim();
            var exile = sim.Exile;
            Assert.IsNotNull(exile, "정책 설정 + 명단이 있으면 붙음");
            Assert.IsTrue(exile.Unlocked, "규칙을 정하는 쪽이 없으면 늘 열림");
            var target = sim.Residents.Residents[2];
            var preview = exile.Preview(target);
            Assert.IsFalse(preview.IsProblem);
            Assert.AreEqual(5f, preview.Satisfaction, 1e-4f, "이유 없는 추방 = 만족도 5 (종류 없음 ×1)");

            DepartedResident fired = null;
            exile.Exiled += (d, p) => fired = d;
            Assert.IsTrue(exile.Exile(target));
            Assert.AreEqual(5, sim.Resources.Population);
            Assert.IsFalse(sim.Residents.Residents.Contains(target), "고른 주민이 떠남 (불만이 큰 주민이 아니라)");
            Assert.AreEqual(75f, sim.Population.Satisfaction, 1e-3f);
            Assert.IsNotNull(fired);
            Assert.AreEqual(target.Name, fired.Name);
            Assert.IsTrue(fired.Exiled);
            Assert.AreEqual(1, exile.ExiledCount);
            Assert.AreEqual(1, exile.Departed.Count(d => d.Exiled), "떠난 주민 기록에 남음");
        }

        [Test]
        public void Exile_ProblemResident_CostsOne()
        {
            UseTraits(Def(ResidentTrait.Complainer, perResident: -2f)); // 모두 불평꾼
            var sim = Sim();
            var target = sim.Residents.Residents[0];
            Assert.AreEqual(ResidentProblem.Complainer, sim.Residents.ProblemOf(target) & ResidentProblem.Complainer);
            var preview = sim.Exile.Preview(target);
            Assert.IsTrue(preview.IsProblem);
            Assert.AreEqual(1f, preview.Satisfaction, 1e-4f, "문제 주민 = 만족도 1");
        }

        [Test]
        public void Homeless_IsProblem()
        {
            var sim = Sim();
            sim.Resources.SetPopulation(24); // 집 20칸 → 4명 집 없음
            sim.Tick(0f);
            var homeless = sim.Residents.Residents.First(r => r.Home == null);
            Assert.AreEqual(ResidentProblem.Homeless, sim.Residents.ProblemOf(homeless));
        }

        [Test]
        public void SameKind_RaisesCost_UpToDouble()
        {
            var sim = Sim();
            foreach (var r in sim.Residents.Residents)
                SetKind(r, "rabbit");
            var target = sim.Residents.Residents[0];
            Assert.AreEqual(2f, sim.Exile.KindMultiplier(target), 1e-4f, "모두 같은 종류 = ×2");
            Assert.AreEqual(10f, sim.Exile.Preview(target).Satisfaction, 1e-4f);
            for (int i = 3; i < sim.Residents.Residents.Count; i++)
                SetKind(sim.Residents.Residents[i], "bear");
            // 토끼 3 · 곰 3 → 다른 주민 5명 중 같은 종류 2명
            Assert.AreEqual(1f + 2f / 5f, sim.Exile.KindMultiplier(target), 1e-4f);
        }

        [Test]
        public void Amnesty_Blocks_DecreeHalves()
        {
            var sim = Sim();
            var target = sim.Residents.Residents[1];
            sim.Policies.AddTemporary("사면", 1000f, M(PolicyStat.ExileBlocked, 1f));
            Assert.IsTrue(sim.Exile.Blocked);
            Assert.IsFalse(sim.Exile.CanExile(target, out string reason));
            StringAssert.Contains("사면", reason);
            Assert.IsFalse(sim.Exile.Exile(target));
            Assert.AreEqual(6, sim.Resources.Population);

            sim.Policies.AddTemporary("사면", 0.01f);
            sim.Tick(1f);
            sim.Policies.AddTemporary("추방령", 1000f, M(PolicyStat.ExileCost, 0.5f));
            Assert.IsFalse(sim.Exile.Blocked);
            Assert.AreEqual(2.5f, sim.Exile.Preview(target).Satisfaction, 1e-4f, "추방령 = 대가 절반");
        }

        [Test]
        public void RuleClosed_CannotExile()
        {
            var sim = Sim();
            sim.SetRules(new Rules { Open = StationRule.None });
            Assert.IsFalse(sim.Exile.Unlocked);
            Assert.IsFalse(sim.Exile.Exile(sim.Residents.Residents[0]));
            sim.SetRules(new Rules { Open = StationRule.Exile });
            Assert.IsTrue(sim.Exile.Unlocked);
        }

        [Test]
        public void LastResident_CannotBeExiled()
        {
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 1;
            b.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim();
            Assert.IsFalse(sim.Exile.CanExile(sim.Residents.Residents[0], out _));
        }

        [Test]
        public void ResettlementAid_SendsFoodAndMetal_WithLeavers()
        {
            var sim = Sim();
            sim.Policies.AddTemporary("이주 지원", 1000f, M(PolicyStat.SendOffSupplies, 10f), M(PolicyStat.ExileCost, 0.5f));
            float food = sim.Resources.GetStock(ResourceType.Food), metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.IsTrue(sim.Exile.Exile(sim.Residents.Residents[0]));
            Assert.AreEqual(food - 10f, sim.Resources.GetStock(ResourceType.Food), 1e-3f);
            Assert.AreEqual(metal - 10f, sim.Resources.GetStock(ResourceType.Metal), 1e-3f);
            Assert.AreEqual(77.5f, sim.Population.Satisfaction, 1e-3f, "대가 절반");
        }

        [Test]
        public void ReturnPermit_DepartedComesBack_OnImmigration()
        {
            var sim = Sim();
            var target = sim.Residents.Residents[3];
            string name = target.Name;
            var traits = target.Traits.ToList();
            Assert.IsTrue(sim.Exile.Exile(target));
            AddImmigrants(sim, 1);
            Assert.IsFalse(sim.Residents.Residents.Any(r => r.Name == name), "정책이 없으면 새 주민");

            sim.Policies.AddTemporary("귀환 허가", 1000f, M(PolicyStat.ReturnChance, 1f));
            DepartedResident back = null;
            sim.Exile.Returned += (d, r) => back = d;
            AddImmigrants(sim, 1);
            var returned = sim.Residents.Residents.FirstOrDefault(r => r.Name == name);
            Assert.IsNotNull(returned, "확률 100% → 떠났던 주민이 돌아옴");
            CollectionAssert.AreEqual(traits, returned.Traits.ToList(), "특성 그대로");
            Assert.IsNotNull(back);
            Assert.IsTrue(back.Returned);
            AddImmigrants(sim, 1);
            Assert.AreEqual(1, sim.Residents.Residents.Count(r => r.Name == name), "한 번 돌아온 기록은 다시 안 씀");
        }

        [Test]
        public void Policies_ComplainerWeight_LeaveInterval_ImmigrationSize()
        {
            UseTraits(Def(ResidentTrait.Complainer, perResident: -2f), Def(ResidentTrait.Technician));
            var sim = Sim();
            sim.Policies.AddTemporary("추방령", 1000f, M(PolicyStat.ComplainerChance, 0f), M(PolicyStat.LeaveInterval, 1.5f));
            int before = sim.Residents.CountWith(ResidentTrait.Complainer);
            sim.Resources.SetPopulation(18);
            sim.Tick(0f);
            Assert.AreEqual(before, sim.Residents.CountWith(ResidentTrait.Complainer), "불평꾼 가중치 0 → 새 주민 중 불평꾼 없음");
            Assert.AreEqual(1.5f, sim.Population.LossIntervalMultiplier, 1e-4f, "사면 · 저절로 떠나는 간격");
        }

        [Test]
        public void SaveRestore_KeepsDepartedRecords()
        {
            var sim = Sim();
            string name = sim.Residents.Residents[0].Name;
            sim.Exile.Exile(sim.Residents.Residents[0]);
            string state = sim.Exile.Capture(_ => -1);

            var loaded = Sim();
            loaded.Exile.Restore(state, _ => null);
            Assert.AreEqual(1, loaded.Exile.ExiledCount);
            var d = loaded.Exile.Departed.Single();
            Assert.AreEqual(name, d.Name);
            Assert.IsTrue(d.Exiled);
        }

        [Test]
        public void WithoutPolicies_NoExileSystem()
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config, Grades = _grades, CoreModule = _core, Events = new List<GameEventData>(),
                Random01 = () => 0.5f, HazardRandom01 = () => 0.5f, Residents = _residents, ResidentRandom01 = () => (float)_rng.NextDouble(),
            });
            Assert.IsNull(sim.Exile);
            Assert.AreEqual(1f, sim.Population.LossIntervalMultiplier);
        }

        // ---------------- 도우미 ----------------

        private sealed class Rules : IStationRules
        {
            public StationRule Open;
            public bool Allows(StationRule rule) => (Open & rule) == rule && rule != StationRule.None;
        }

        private static void AddImmigrants(StationSimulation sim, int n)
        {
            typeof(PopulationSimulation).GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(sim.Population, new object[] { n, PopulationChangeReason.Immigration });
        }

        private static void SetKind(Resident r, string kind) =>
            typeof(Resident).GetProperty("Kind").SetValue(r, kind);

        private StationSimulation Sim() => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
            HazardRandom01 = () => 0.5f,
            Policies = _policies,
            Residents = _residents,
            ResidentRandom01 = () => (float)_rng.NextDouble(),
        });

        private static TraitDefinition Def(ResidentTrait t, float perResident = 0.04f) => new TraitDefinition(t, t.ToString(), "", perResident >= 0f, perResident, 10f);

        private void UseTraits(params TraitDefinition[] traits) =>
            _residents.EditorSet(traits, 0f, new ModuleData[0], new ModuleData[0], new ModuleData[0],
                new[] { "두부", "모찌", "호두", "보리", "콩이", "밤톨", "율무", "깨비", "솜이", "단비" }, new string[0]);

        private static PolicyModifier M(PolicyStat s, float v) => new PolicyModifier { Stat = s, Value = v };

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
