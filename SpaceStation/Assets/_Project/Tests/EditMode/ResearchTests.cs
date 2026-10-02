using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>Phase 6 연구: 시작 조건·진행·일시정지·상한·취소·효과 조회 지점 (RESEARCH.md).</summary>
    public class ResearchTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private ModuleData _core, _lab, _block, _habitat, _shield;
        private StationGradeConfig _grades;
        private ResearchLevelCapConfig _caps;
        private MeteorEventData _meteor;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 4;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 200));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 200));
            b.FindProperty("_minPowerEfficiency").floatValue = 0.25f;
            b.FindProperty("_repairCostRate").floatValue = 0.3f;
            b.FindProperty("_repairDuration").floatValue = 30f;
            b.FindProperty("_demolishRefundRate").floatValue = 0.5f;
            b.FindProperty("_meteorDurabilityDamage").floatValue = 20f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 120f;
            b.FindProperty("_startingSatisfaction").floatValue = 70f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module(removable: false);
            _lab = Module();
            var l = new SerializedObject(_lab);
            l.FindProperty("_researchSlots").intValue = 1;
            l.ApplyModifiedPropertiesWithoutUndo();
            _block = Module();
            var bs = new SerializedObject(_block);
            Fill(bs.FindProperty("_buildCost"), R(ResourceType.Metal, 40));
            bs.ApplyModifiedPropertiesWithoutUndo();
            _habitat = Module();
            var h = new SerializedObject(_habitat);
            h.FindProperty("_housingCapacity").intValue = 6;
            h.ApplyModifiedPropertiesWithoutUndo();
            _shield = Module();
            var s = new SerializedObject(_shield);
            s.FindProperty("_shieldRadius").intValue = 2;
            s.FindProperty("_shieldReduction").floatValue = 1f;
            s.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 2;
            list.GetArrayElementAtIndex(1).FindPropertyRelative("_minPopulation").intValue = 999; // 등급 1은 도달 불가
            g.ApplyModifiedPropertiesWithoutUndo();

            _caps = Create<ResearchLevelCapConfig>();
            _caps.EditorSet(new List<ResearchLevelCapConfig.Requirement>
            {
                new ResearchLevelCapConfig.Requirement { Grade = 0, MinPopulation = 4 },
                new ResearchLevelCapConfig.Requirement { Grade = 1, MinPopulation = 15 },
            });
            _meteor = Create<MeteorEventData>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ---------------- 시작·진행 ----------------

        [Test]
        public void NoResearch_EffectsEqualBalance()
        {
            var sim = Sim(Category(ResearchStat.RepairCostRate, 0.25f));
            Assert.AreEqual(0.3f, sim.Effects.RepairCostRate, Eps);
            Assert.AreEqual(30f, sim.Effects.RepairDuration, Eps);
            Assert.AreEqual(0.25f, sim.Effects.MinPowerEfficiency, Eps);
            Assert.AreEqual(1f, sim.Effects.BuildCostMultiplier, Eps);
            Assert.AreEqual(40f, sim.GetBuildCost(_block)[0].Amount, Eps);
        }

        [Test]
        public void Start_NeedsLab_PaysStartCost()
        {
            var category = Category(ResearchStat.RepairCostRate, 0.25f);
            var sim = Sim(category);
            Assert.AreEqual(ResearchStartResult.NoFreeLab, sim.TryStartResearch(category), "연구소 없음");

            Assert.IsTrue(sim.Grid.TryPlace(_lab, Vector3Int.left, 0, out _));
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.AreEqual(ResearchStartResult.Ok, sim.TryStartResearch(category));
            Assert.AreEqual(metal - 30f, sim.Resources.GetStock(ResourceType.Metal), Eps, "시작 비용 금속 30");
            Assert.AreEqual(ResearchStartResult.AlreadyResearching, sim.CanStartResearch(category), "같은 분야는 동시에 하나");
        }

        [Test]
        public void Progress_CompletesAndAppliesEffect()
        {
            var category = Category(ResearchStat.RepairCostRate, 0.25f);
            var sim = Sim(category);
            sim.Grid.TryPlace(_lab, Vector3Int.left, 0, out _);
            sim.TryStartResearch(category);
            int completed = 0;
            sim.Research.Completed += (c, lv) => completed++;

            for (int i = 0; i < 30; i++)
                sim.Tick(1f);
            Assert.AreEqual(0.5f, sim.Research.Projects[0].Progress, 0.02f, "전력 100%: 60초 중 30초");
            for (int i = 0; i < 30; i++)
                sim.Tick(1f);
            Assert.AreEqual(1, sim.Research.GetLevel(category));
            Assert.AreEqual(1, completed);
            Assert.AreEqual(0, sim.Research.Projects.Count);
            Assert.AreEqual(0.25f, sim.Effects.RepairCostRate, Eps, "효과 적용");
        }

        [Test]
        public void PowerDemand_SlowsResearch()
        {
            var category = Category(ResearchStat.RepairCostRate, 0.25f, powerDemand: 5f);
            var sim = Sim(category);
            sim.Grid.TryPlace(_lab, Vector3Int.left, 0, out _);
            sim.TryStartResearch(category);
            sim.Tick(1f);
            Assert.AreEqual(5f, sim.Resources.PowerDemand, Eps, "연구 중 전력 수요 +5");
            for (int i = 0; i < 59; i++)
                sim.Tick(1f);
            Assert.AreEqual(0.25f, sim.Research.Projects[0].Progress, 0.02f, "공급 0 → 최소 효율 25%로 느리게 진행");
        }

        [Test]
        public void Paused_WhenLabDamaged_KeepsProgress()
        {
            var category = Category(ResearchStat.RepairCostRate, 0.25f);
            var sim = Sim(category);
            sim.Grid.TryPlace(_lab, Vector3Int.left, 0, out var lab);
            sim.TryStartResearch(category);
            for (int i = 0; i < 10; i++)
                sim.Tick(1f);
            float progress = sim.Research.Projects[0].Progress;
            sim.Damage.Damage(lab);
            for (int i = 0; i < 10; i++)
                sim.Tick(1f);
            Assert.IsTrue(sim.Research.Projects[0].Paused);
            Assert.AreEqual(progress, sim.Research.Projects[0].Progress, Eps, "멈춤 동안 진행 없음, 진행률 유지");
        }

        [Test]
        public void Caps_GradeGatesNextLevel()
        {
            var category = Category(ResearchStat.RepairCostRate, 0.25f);
            var sim = Sim(category);
            sim.Grid.TryPlace(_lab, Vector3Int.left, 0, out _);
            sim.Research.SetLevel(category, 1);
            Assert.AreEqual(ResearchStartResult.GradeTooLow, sim.CanStartResearch(category), "Lv.2는 등급 1 필요");
        }

        [Test]
        public void Cancel_NoRefund()
        {
            var category = Category(ResearchStat.RepairCostRate, 0.25f);
            var sim = Sim(category);
            sim.Grid.TryPlace(_lab, Vector3Int.left, 0, out _);
            sim.TryStartResearch(category);
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.IsTrue(sim.CancelResearch(category));
            Assert.AreEqual(metal, sim.Resources.GetStock(ResourceType.Metal), Eps, "환급 없음");
            Assert.AreEqual(0, sim.Research.GetLevel(category));
            Assert.AreEqual(ResearchStartResult.Ok, sim.CanStartResearch(category), "다시 시작 가능");
        }

        // ---------------- 효과 조회 지점 ----------------

        [Test]
        public void Effects_BuildCost_Housing_Repair()
        {
            var category = Category(ResearchStat.BuildCostMultiplier, 0.5f, ResearchStat.HousingBonus, 2f, ResearchStat.RepairDuration, 15f);
            var sim = Sim(category);
            sim.Grid.TryPlace(_habitat, Vector3Int.left, 0, out _);
            Assert.AreEqual(6, sim.Resources.HousingCapacity);
            sim.Research.SetLevel(category, 1);
            Assert.AreEqual(20f, sim.GetBuildCost(_block)[0].Amount, Eps, "건설 비용 50%");
            Assert.AreEqual(8, sim.Resources.HousingCapacity, "거주 +2 (효과가 바뀌면 바로 다시 계산)");

            sim.Grid.TryPlace(_block, Vector3Int.right * 2, 0, out var block);
            sim.Damage.Damage(block);
            sim.TryRepair(block);
            Assert.IsTrue(sim.Damage.TryGetInfo(block, out var info) && info.IsRepairing);
            Assert.AreEqual(15f, info.RepairRemaining, Eps, "수리 시간 15초");
        }

        [Test]
        public void Effects_DefenseRadiusBonus_ExtendsShield()
        {
            var category = Category(ResearchStat.DefenseRadiusBonus, 1f);
            var sim = Sim(category);
            sim.Grid.TryPlace(_shield, new Vector3Int(-1, 0, 0), 0, out _);
            sim.Grid.TryPlace(_block, new Vector3Int(-4, 0, 0), 0, out var far); // 실드와 거리 3
            Assert.AreEqual(0f, sim.Defense.GetShieldBlockChance(sim.Grid, far), Eps);
            sim.Research.SetLevel(category, 1);
            Assert.AreEqual(1f, sim.Defense.GetShieldBlockChance(sim.Grid, far), Eps, "반경 2 → 3");
            Assert.AreEqual(3, sim.Effects.ShieldRadius(_shield));
            Assert.AreEqual(0, sim.Effects.ShieldRadius(_block), "방어 모듈이 아니면 0");
        }

        [Test]
        public void Effects_HitImmunity_NoDamageButDurabilityLoss()
        {
            var category = Category(ResearchStat.HitImmunityChance, 1f);
            var sim = Sim(category);
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out var target);
            sim.Research.SetLevel(category, 1);
            var flights = new List<MeteorFlight>();
            sim.MeteorResolved += flights.Add;
            sim.Events.Trigger(_meteor);
            Assert.AreEqual(1, flights.Count);
            Assert.IsTrue(flights[0].Immune);
            Assert.IsFalse(sim.Damage.IsDamaged(target), "파손 면역");
            Assert.IsTrue(sim.Durability.TryGetInfo(target, out var d));
            Assert.AreEqual(80f, d.Current, Eps, "내구도 피해는 그대로");
        }

        [Test]
        public void Effects_DecayAndEfficiencyFloor()
        {
            var category = Category(ResearchStat.DecayMultiplier, 0.5f, ResearchStat.DurabilityEfficiencyFloor, 0.3f);
            var sim = Sim(category);
            var so = new SerializedObject(_config);
            so.FindProperty("_durabilityDecayPerSecond").floatValue = 1f;
            so.FindProperty("_durabilityEfficiencyThreshold").floatValue = 50f;
            so.ApplyModifiedPropertiesWithoutUndo();
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out var block);
            sim.Research.SetLevel(category, 1);
            sim.Durability.Tick(10f);
            Assert.IsTrue(sim.Durability.TryGetInfo(block, out var d));
            Assert.AreEqual(95f, d.Current, Eps, "노후 속도 절반");
            Assert.AreEqual(0.3f, sim.Durability.EfficiencyFor(5f), Eps, "효율 최저 30%");
            Assert.AreEqual(1f, sim.Durability.EfficiencyFor(60f), Eps);
        }

        // ---------------- 자동화 (정비 자동화 연구) ----------------

        [Test]
        public void Automation_MinGrade_GatesFirstLevel()
        {
            var category = Category(ResearchStat.MaintenanceAutomation, 1f);
            category.EditorSet(ResearchCategory.Automation, "자동화", "module", new List<ResearchLevel>(category.Levels), minGrade: 1);
            var sim = Sim(category);
            sim.Grid.TryPlace(_lab, Vector3Int.left, 0, out _);
            Assert.AreEqual(ResearchStartResult.GradeTooLow, sim.CanStartResearch(category), "레벨 상한 표는 Lv.1 = 등급 0이지만 카테고리 최소 등급 1");
        }

        [Test]
        public void Automation_LockedUntilResearched_ThenMaintains()
        {
            var (sim, category) = AutomationSim();
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out var block);
            sim.Durability.ApplyImpact(block, 60f); // 내구도 40 < 기준 55
            Ticks(sim, 2);
            sim.Durability.TryGetInfo(block, out var d);
            Assert.AreEqual(40f, d.Current, Eps, "연구 전에는 동작하지 않음");

            sim.Research.SetLevel(category, 1);
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Ticks(sim, 2);
            Assert.AreEqual(85f, d.Current, Eps, "자동 정비: 최대 100 - 15");
            Assert.AreEqual(metal - 24f, sim.Resources.GetStock(ResourceType.Metal), Eps, "비용은 그대로 냄 (40 × 1.0 × 0.6)");
            Assert.AreEqual(1, sim.Automation.AutoMaintainCount);
        }

        [Test]
        public void Automation_RebuildsWhenMaintenanceNotEnough()
        {
            var (sim, category) = AutomationSim();
            sim.Research.SetLevel(category, 1);
            sim.Automation.AutoMaintain = false; // 준비 중 끼어들지 않게
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out var block);
            for (int i = 0; i < 3; i++)
            {
                sim.Durability.ApplyImpact(block, 50f);
                sim.Durability.Maintain(block); // 최대 100 → 85 → 70 → 55
            }
            sim.Durability.ApplyImpact(block, 10f); // 45, 정비해도 최대 40 < 55 → 재건축
            sim.Automation.AutoMaintain = true;
            ModuleInstance rebuilt = null;
            sim.Automation.ModuleRebuilt += (old, now) => rebuilt = now;
            Ticks(sim, 2);

            Assert.IsNotNull(rebuilt, "재건축됨");
            Assert.AreNotSame(block, rebuilt);
            Assert.IsTrue(sim.Durability.TryGetInfo(rebuilt, out var d));
            Assert.AreEqual(100f, d.Max, Eps);
            Assert.AreEqual(1, sim.Automation.AutoRebuildCount);
        }

        [Test]
        public void Automation_ReserveBlocksAuto_BatchIgnoresIt()
        {
            var (sim, category) = AutomationSim();
            sim.Research.SetLevel(category, 1);
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out var block);
            sim.Durability.ApplyImpact(block, 60f);
            sim.Resources.SetStock(ResourceType.Metal, 100f); // 정비 24 → 76 남음 < 보호선 200 × 40% = 80
            Ticks(sim, 2);
            sim.Durability.TryGetInfo(block, out var d);
            Assert.AreEqual(40f, d.Current, Eps, "보호선 아래라 미룸");
            Assert.IsTrue(sim.Automation.WaitingForReserve);

            sim.Automation.ReserveRatio = 0.3f; // 76 ≥ 60
            Ticks(sim, 2);
            Assert.AreEqual(85f, d.Current, Eps, "보호선을 낮추면 진행");

            sim.Durability.ApplyImpact(block, 50f);
            sim.Automation.ReserveRatio = 0.8f;
            sim.Automation.AutoMaintain = false;
            var (maintained, rebuilt) = sim.Automation.RunBatch();
            Assert.AreEqual(1, maintained + rebuilt, "일괄 정비는 보호선 무시");
        }

        [Test]
        public void Automation_SkipsDamagedAndRespectsToggle()
        {
            var (sim, category) = AutomationSim();
            sim.Research.SetLevel(category, 1);
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out var damaged);
            sim.Grid.TryPlace(_block, Vector3Int.right, 0, out var worn);
            sim.Durability.ApplyImpact(damaged, 60f);
            sim.Durability.ApplyImpact(worn, 60f);
            sim.Damage.Damage(damaged);
            Assert.AreEqual(1, sim.Automation.Plan().Count, "운석 파손 모듈은 수리 시스템에 맡김");

            sim.Automation.AutoMaintain = false;
            Ticks(sim, 2);
            sim.Durability.TryGetInfo(worn, out var d);
            Assert.AreEqual(40f, d.Current, Eps, "자동 정비 꺼짐");
        }

        /// <summary>자동화 카테고리 + 노후 없음 + 정비 비용 비율 1 (비용 = 건설비 × 손실 비율).</summary>
        private (StationSimulation sim, ResearchCategoryData category) AutomationSim()
        {
            var so = new SerializedObject(_config);
            so.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            so.FindProperty("_maintenanceCostRate").floatValue = 1f;
            so.FindProperty("_maintenanceMaxLoss").floatValue = 15f;
            so.FindProperty("_maxDurabilityFloor").floatValue = 25f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var g = new SerializedObject(_grades); // 재건축은 해금된 모듈만
            var unlocks = g.FindProperty("_grades").GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks");
            unlocks.arraySize = 1;
            unlocks.GetArrayElementAtIndex(0).objectReferenceValue = _block;
            g.ApplyModifiedPropertiesWithoutUndo();
            var category = Category(ResearchStat.MaintenanceAutomation, 1f);
            return (Sim(category), category);
        }

        private static void Ticks(StationSimulation sim, int seconds)
        {
            for (int i = 0; i < seconds; i++)
                sim.Tick(1f);
        }

        // ---------------- 도우미 ----------------

        /// <summary>랜덤 이벤트 없음 (간격 기본값 1초라 넣으면 매 틱 운석) — 운석은 Events.Trigger로 직접.</summary>
        private StationSimulation Sim(params ResearchCategoryData[] categories)
        {
            return new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = null,
                Random01 = () => 0f,
                ResearchCategories = categories,
                ResearchCaps = _caps,
            });
        }

        /// <summary>레벨 2개짜리 카테고리 (레벨 1 = 주어진 효과, 비용 금속 30, 60초).</summary>
        private ResearchCategoryData Category(ResearchStat stat, float value, float powerDemand = 0f)
            => CategoryWith(powerDemand, (stat, value));

        private ResearchCategoryData Category(ResearchStat a, float va, ResearchStat b, float vb, ResearchStat? c = null, float vc = 0f)
            => c.HasValue ? CategoryWith(0f, (a, va), (b, vb), (c.Value, vc)) : CategoryWith(0f, (a, va), (b, vb));

        private ResearchCategoryData CategoryWith(float powerDemand, params (ResearchStat stat, float value)[] mods)
        {
            var category = Create<ResearchCategoryData>();
            var lv1 = new ResearchLevel
            {
                StartCost = new List<ResourceAmount> { R(ResourceType.Metal, 30) },
                PowerDemand = powerDemand,
                Duration = 60f,
                Description = "Lv1",
            };
            foreach (var (stat, value) in mods)
                lv1.Modifiers.Add(new ResearchModifier { Stat = stat, Value = value });
            var lv2 = new ResearchLevel { StartCost = new List<ResourceAmount>(), PowerDemand = 0f, Duration = 60f, Description = "Lv2" };
            category.EditorSet(ResearchCategory.Maintenance, "테스트", "repair", new List<ResearchLevel> { lv1, lv2 });
            return category;
        }

        private ModuleData Module(bool removable = true)
        {
            var m = Create<ModuleData>();
            var so = new SerializedObject(m);
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

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
