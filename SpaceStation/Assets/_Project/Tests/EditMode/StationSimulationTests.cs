using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>StationSimulation 통합 동작 (배치 판정 순서, 틱 순서, 이벤트 효과, 방치 파괴).</summary>
    public class StationSimulationTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _balance;
        private StationGradeConfig _grades;
        private ModuleData _core, _solar, _storage;
        private MeteorEventData _meteor;
        private SupplyShipEventData _supply;

        [SetUp]
        public void SetUp()
        {
            _core = Module("Core", cost: 0, supply: 5, housing: 4, removable: false);
            _solar = Module("Solar", cost: 20, supply: 10);
            _storage = Module("Storage", cost: 30, storage: 150);

            _balance = Create<BalanceConfig>();
            var b = new SerializedObject(_balance);
            b.FindProperty("_startingPopulation").intValue = 4;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 150), R(ResourceType.Food, 150), R(ResourceType.Metal, 150));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 200));
            Fill(b.FindProperty("_consumptionPerResident"), R(ResourceType.Oxygen, 0.2f), R(ResourceType.Water, 0.1f), R(ResourceType.Food, 0.1f));
            b.FindProperty("_minPowerEfficiency").floatValue = 0.25f;
            b.FindProperty("_demolishRefundRate").floatValue = 0.5f;
            b.FindProperty("_repairCostRate").floatValue = 0.3f;
            b.FindProperty("_startingSatisfaction").floatValue = 70f;
            b.FindProperty("_growthMinSatisfaction").floatValue = 50f;
            b.FindProperty("_growthIntervalAtMinSatisfaction").floatValue = 30f;
            b.FindProperty("_growthIntervalAtMaxSatisfaction").floatValue = 10f;
            b.FindProperty("_oxygenDepletedLossInterval").floatValue = 10f;
            b.FindProperty("_lowSatisfactionThreshold").floatValue = 25f;
            b.FindProperty("_lowSatisfactionLossInterval").floatValue = 15f;
            b.FindProperty("_overcrowdedLossInterval").floatValue = 5f;
            b.FindProperty("_eventGracePeriod").floatValue = 1000f; // 테스트 중 랜덤 이벤트 없음
            b.FindProperty("_eventIntervalMin").floatValue = 90f;
            b.FindProperty("_eventIntervalMax").floatValue = 150f;
            b.FindProperty("_damagedProductionMultiplier").floatValue = 0.5f;
            b.FindProperty("_damagedOxygenLeakPerSecond").floatValue = 0.3f;
            b.FindProperty("_repairDuration").floatValue = 10f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 120f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 2;
            SetGrade(list.GetArrayElementAtIndex(0), "초소형", 0, 0, 1, _solar);
            SetGrade(list.GetArrayElementAtIndex(1), "소형", 10, 8, 2, _storage);
            g.ApplyModifiedPropertiesWithoutUndo();

            _meteor = Create<MeteorEventData>();
            _supply = Create<SupplyShipEventData>();
            var s = new SerializedObject(_supply);
            Fill(s.FindProperty("_rewards"), R(ResourceType.Metal, 60));
            s.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        private StationSimulation Sim() => new StationSimulation(new StationSimulationSettings
        {
            Balance = _balance, Grades = _grades, CoreModule = _core,
            Events = new GameEventData[] { _meteor, _supply }, Random01 = () => 0f,
        });

        [Test]
        public void Start_PlacesActiveCoreAndStartingState()
        {
            var sim = Sim();
            Assert.IsNotNull(sim.Core);
            Assert.IsTrue(sim.Connectivity.IsActive(sim.Core));
            Assert.AreEqual(4, sim.Resources.HousingCapacity);
            Assert.AreEqual(150f, sim.Resources.GetStock(ResourceType.Metal), Eps);
            Assert.IsFalse(sim.CanRemove(sim.Core));
        }

        [Test]
        public void Place_ChargesCost_AndRespectsUnlocks()
        {
            var sim = Sim();
            Assert.IsTrue(sim.TryPlace(_solar, Vector3Int.right, 0, out _));
            Assert.AreEqual(130f, sim.Resources.GetStock(ResourceType.Metal), Eps);
            Assert.AreEqual(PlacementResult.ModuleLocked, sim.EvaluatePlacement(_storage, Vector3Int.left, 0));
            Assert.AreEqual(PlacementResult.Occupied, sim.EvaluatePlacement(_solar, Vector3Int.right, 0), "공간 규칙이 먼저");
        }

        [Test]
        public void Remove_RefundsHalf()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var solar);
            Assert.IsTrue(sim.TryRemove(solar));
            Assert.AreEqual(140f, sim.Resources.GetStock(ResourceType.Metal), Eps);
        }

        [Test]
        public void Tick_AdvancesResourcesAndTime()
        {
            var sim = Sim();
            sim.Tick(1f);
            Assert.AreEqual(1f, sim.ElapsedSeconds, Eps);
            Assert.AreEqual(199.2f, sim.Resources.GetStock(ResourceType.Oxygen), Eps);
        }

        [Test]
        public void Meteor_DamagesExteriorModule_AndLeakAppliesNextTick()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var solar);
            var reports = new List<string>();
            sim.EffectReported += (m, _) => reports.Add(m);

            sim.Events.Trigger(_meteor);
            Assert.IsTrue(sim.Damage.IsDamaged(solar), "코어 면역 → 유일한 후보인 태양광");
            Assert.AreEqual(1, reports.Count);
            Assert.AreEqual(1, sim.Session.EventsExperienced);

            sim.Tick(1f);
            Assert.AreEqual(10f, sim.Resources.PowerSupply, Eps, "5 + 10 × 0.5");
            Assert.AreEqual(0.8f + 0.3f, sim.Resources.GetConsumption(ResourceType.Oxygen), Eps);
        }

        [Test]
        public void UnrepairedModule_DestroyedAndRemovedFromGrid()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var solar);
            sim.Damage.Damage(solar);
            for (int i = 0; i < 120; i++)
                sim.Tick(1f);
            Assert.IsFalse(sim.Grid.IsOccupied(Vector3Int.right));
            Assert.AreEqual(1, sim.Session.ModulesDestroyed);
        }

        [Test]
        public void Repair_ChargesCostAndRestores()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var solar);
            sim.Damage.Damage(solar);
            float before = sim.Resources.GetStock(ResourceType.Metal);
            Assert.AreEqual(RepairResult.Started, sim.TryRepair(solar));
            Assert.AreEqual(before - 6f, sim.Resources.GetStock(ResourceType.Metal), Eps);
            for (int i = 0; i < 10; i++)
                sim.Tick(1f);
            Assert.IsFalse(sim.Damage.IsDamaged(solar));
        }

        [Test]
        public void RepairSlots_CoreOnePlusBays_DamagedBayLosesSlot_CancelRefunds()
        {
            var bay = Module("Bay", cost: 100);
            var so = new SerializedObject(bay);
            so.FindProperty("_repairSlots").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();

            var sim = Sim();
            Assert.AreEqual(1, sim.CountRepairSlots(), "코어 기본 1");
            sim.Grid.TryPlace(bay, Vector3Int.left, 0, out var bayModule); // 해금 판정 우회
            Assert.AreEqual(2, sim.CountRepairSlots());
            Assert.AreEqual(2, sim.Damage.RepairCapacity);

            sim.Damage.Damage(bayModule);
            Assert.AreEqual(1, sim.Damage.RepairCapacity, "파손된 베이는 슬롯 없음");

            sim.TryPlace(_solar, Vector3Int.right, 0, out var a);
            sim.TryPlace(_solar, Vector3Int.up, 0, out var b);
            sim.Damage.Damage(a);
            sim.Damage.Damage(b);
            Assert.AreEqual(RepairResult.Started, sim.TryRepair(a));
            float before = sim.Resources.GetStock(ResourceType.Metal);
            Assert.AreEqual(RepairResult.Queued, sim.TryRepair(b));
            Assert.AreEqual(before - 6f, sim.Resources.GetStock(ResourceType.Metal), Eps, "대기도 선불");
            Assert.AreEqual(RepairResult.AlreadyQueued, sim.TryRepair(b));

            Assert.IsTrue(sim.TryCancelRepair(b));
            Assert.AreEqual(before, sim.Resources.GetStock(ResourceType.Metal), Eps, "취소 시 전액 환불");
        }

        [Test]
        public void OxygenLeak_LossCappedByMaxRatio()
        {
            // 8-6: 손실 비율 × 등급 강도가 상한(70%)을 넘지 않음
            var leak = Create<OxygenLeakEventData>();
            var so = new SerializedObject(leak);
            so.FindProperty("_stockLossRatio").floatValue = 0.9f;
            so.FindProperty("_maxLossRatio").floatValue = 0.7f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim();
            sim.Resources.SetStock(ResourceType.Oxygen, 100f);
            Assert.IsTrue(sim.Events.Trigger(leak));
            Assert.AreEqual(30f, sim.Resources.GetStock(ResourceType.Oxygen), Eps);
        }

        [Test]
        public void RotatingRing_ShortensGrowthInterval_NoStacking_StopsWhenDamaged()
        {
            // 8-4: 가동 중인 링 → 인구 증가 간격 ×0.8, 여러 개 중첩 없음, 파손이면 효과 없음
            var ring = Module("Ring", cost: 0, housing: 30);
            var so = new SerializedObject(ring);
            so.FindProperty("_growthIntervalMultiplier").floatValue = 0.8f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var sim = Sim();
            sim.Tick(1f);
            Assert.AreEqual(1f, sim.Population.GrowthIntervalMultiplier, Eps);
            sim.Grid.TryPlace(ring, Vector3Int.right, 0, out var a); // 해금 판정 우회
            sim.Grid.TryPlace(ring, Vector3Int.left, 0, out var b);
            sim.Tick(1f);
            Assert.AreEqual(0.8f, sim.Population.GrowthIntervalMultiplier, Eps, "중첩 없음");
            sim.Damage.Damage(a);
            sim.Damage.Damage(b);
            sim.Tick(1f);
            Assert.AreEqual(1f, sim.Population.GrowthIntervalMultiplier, Eps, "파손이면 효과 없음");
        }

        [Test]
        public void QueuedModule_DestroyedSameTickAsBayRepair_IsDestroyedNotRepaired()
        {
            // 회귀: 베이 수리 완료(슬롯 증가)와 대기 모듈의 파괴가 같은 틱이면, 파괴가 수리 완료로 뒤바뀌면 안 됨
            var bay = Module("Bay", cost: 100);
            var so = new SerializedObject(bay);
            so.FindProperty("_repairSlots").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim();
            sim.Resources.AddStock(ResourceType.Metal, 100);
            sim.Grid.TryPlace(bay, Vector3Int.left, 0, out var bayModule);
            sim.TryPlace(_solar, Vector3Int.right, 0, out var q);

            sim.Damage.Damage(bayModule); // 먼저 파손 → 사전 순회에서 먼저 처리됨
            sim.Damage.Damage(q);
            Assert.AreEqual(1, sim.Damage.RepairCapacity);
            for (int i = 0; i < 110; i++)
                sim.Tick(1f);
            Assert.AreEqual(RepairResult.Started, sim.TryRepair(bayModule)); // 10초 → t=120 완료
            Assert.AreEqual(RepairResult.Queued, sim.TryRepair(q));           // t=120 파괴 예정

            bool qRepaired = false;
            sim.Damage.Repaired += m => { if (m == q) qRepaired = true; };
            for (int i = 0; i < 10; i++)
                sim.Tick(1f);
            Assert.IsFalse(qRepaired, "파괴될 모듈이 수리 완료로 처리되면 안 됨");
            Assert.IsFalse(sim.Grid.IsOccupied(Vector3Int.right), "q는 파괴");
            Assert.IsFalse(sim.Damage.IsDamaged(bayModule), "베이는 수리 완료");
            Assert.AreEqual(2, sim.Damage.RepairCapacity);
            Assert.AreEqual(0, sim.Damage.RepairingCount);
        }

        [Test]
        public void CancelRepair_RefundsConfiguredRate()
        {
            var b = new SerializedObject(_balance);
            b.FindProperty("_repairCancelRefundRate").floatValue = 0.5f;
            b.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var a);
            sim.TryPlace(_solar, Vector3Int.up, 0, out var c);
            sim.Damage.Damage(a);
            sim.Damage.Damage(c);
            sim.TryRepair(a);
            float before = sim.Resources.GetStock(ResourceType.Metal);
            Assert.AreEqual(RepairResult.Queued, sim.TryRepair(c)); // 6 지불
            Assert.AreEqual(3f, sim.GetCancelRefund(c)[0].Amount, Eps);
            Assert.IsTrue(sim.TryCancelRepair(c));
            Assert.AreEqual(before - 3f, sim.Resources.GetStock(ResourceType.Metal), Eps, "50%만 환불");
        }

        [Test]
        public void FieldRepair_HalfCost_NoSlot_FreeWhenAlreadyPaid()
        {
            // 11-17: 현장 수리 = 보통 수리 비용(6) × 0.5, 수리 슬롯 · 시간 없이 바로 복구. 대기 중(이미 냄)이면 0, 대기열에서 빠짐
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var a);
            sim.TryPlace(_solar, Vector3Int.up, 0, out var c);
            sim.TryPlace(_solar, Vector3Int.left, 0, out var d);
            sim.Damage.Damage(a);
            sim.Damage.Damage(c);
            sim.Damage.Damage(d);
            Assert.AreEqual(RepairResult.Started, sim.TryRepair(a));  // 코어 슬롯 1개 사용
            Assert.AreEqual(RepairResult.Queued, sim.TryRepair(c));
            Assert.AreEqual(3f, sim.GetFieldRepairCost(d)[0].Amount, Eps);
            Assert.AreEqual(0, sim.GetFieldRepairCost(c).Count, "이미 수리 비용을 냄");

            bool repaired = false;
            sim.Damage.Repaired += m => { if (m == d) repaired = true; };
            float before = sim.Resources.GetStock(ResourceType.Metal);
            Assert.AreEqual(RepairResult.Completed, sim.TryFieldRepair(d), "슬롯이 꽉 차도 바로");
            Assert.IsTrue(repaired);
            Assert.AreEqual(before - 3f, sim.Resources.GetStock(ResourceType.Metal), Eps);

            Assert.AreEqual(RepairResult.Completed, sim.TryFieldRepair(c));
            Assert.AreEqual(before - 3f, sim.Resources.GetStock(ResourceType.Metal), Eps, "대기 중이던 모듈은 추가 비용 없음");
            Assert.AreEqual(0, sim.Damage.Queue.Count);
            Assert.AreEqual(RepairResult.Completed, sim.TryFieldRepair(a), "수리 중이던 것도 바로 끝");
            Assert.AreEqual(0, sim.Damage.RepairingCount);
            Assert.AreEqual(RepairResult.NotDamaged, sim.TryFieldRepair(a));

            sim.Damage.Damage(a);
            sim.Resources.SetStock(ResourceType.Metal, 1f);
            Assert.AreEqual(RepairResult.InsufficientResources, sim.TryFieldRepair(a));
            Assert.IsTrue(sim.Damage.IsDamaged(a));
        }

        [Test]
        public void SupplyShip_PlacesCrates_SpecialFirst_CappedAndPickedUp()
        {
            // 11-17 ②: 보급선 = 바깥 보상 + 내부 상자 (자원 2 + 특별 1, 화물 터미널 · 창고가 없으면 코어), 최대 6개
            var sim = Sim();
            sim.Events.Trigger(_supply);
            Assert.AreEqual(3, sim.Supply.Crates.Count);
            Assert.IsTrue(sim.Supply.Crates[0].IsSpecial, "특별 상자 1개 확정");
            Assert.AreEqual(sim.Core, sim.Supply.Crates[1].Module);
            var crate = sim.Supply.Crates[1];
            float before = sim.Resources.GetStock(crate.Resource);
            Assert.IsTrue(sim.TryPickupCrate(crate.Id, out var text));
            StringAssert.Contains(crate.Resource.DisplayName(), text);
            Assert.AreEqual(15f, crate.Amount, Eps, "15 × 등급 강도 1");
            Assert.AreEqual(Mathf.Min(before + 15f, sim.Resources.GetCapacity(crate.Resource)), sim.Resources.GetStock(crate.Resource), Eps);
            Assert.AreEqual(2, sim.Supply.Crates.Count);
            Assert.IsFalse(sim.TryPickupCrate(crate.Id, out _), "이미 주움");

            sim.Events.Trigger(_supply);
            sim.Events.Trigger(_supply);
            Assert.AreEqual(6, sim.Supply.Crates.Count, "최대 6개");
        }

        /// <summary>원하는 특별 보상이 나올 때까지 보급선을 부르고 상자를 줍는다.</summary>
        private void Collect(StationSimulation sim, CrateBonus bonus)
        {
            for (int i = 0; i < 40; i++)
            {
                foreach (var c in new List<SupplyCrate>(sim.Supply.Crates))
                {
                    sim.TryPickupCrate(c.Id, out _);
                    if (c.Bonus == bonus)
                        return;
                }
                sim.Events.Trigger(_supply);
            }
            Assert.Fail("보상이 나오지 않음: " + bonus);
        }

        [Test]
        public void FreeRepair_UsedByNextRepair_AndFieldRepair()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var a);
            Collect(sim, CrateBonus.FreeRepair);
            Assert.AreEqual(1, sim.Supply.FreeRepairs);
            sim.Damage.Damage(a);
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.AreEqual(0, sim.GetFieldRepairCost(a).Count, "수리권이 있으면 현장 수리도 0");
            Assert.AreEqual(RepairResult.Started, sim.TryRepair(a));
            Assert.AreEqual(metal, sim.Resources.GetStock(ResourceType.Metal), Eps, "무료");
            Assert.AreEqual(0, sim.Supply.FreeRepairs);

            Collect(sim, CrateBonus.FreeRepair);
            sim.TryPlace(_solar, Vector3Int.left, 0, out var b);
            sim.Damage.Damage(b);
            metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.IsTrue(sim.FieldRepairUsesFreeRepair(b));
            Assert.AreEqual(RepairResult.Completed, sim.TryFieldRepair(b));
            Assert.AreEqual(metal, sim.Resources.GetStock(ResourceType.Metal), Eps);
            Assert.AreEqual(0, sim.Supply.FreeRepairs);
        }

        [Test]
        public void Crates_Points_SurviveSaveRoundTrip()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out _);
            Collect(sim, CrateBonus.ResearchPoints);
            sim.Events.Trigger(_supply);
            int crates = sim.Supply.Crates.Count;
            var state = StationStateSerializer.Capture(sim);
            var loaded = Sim();
            StationStateSerializer.Restore(loaded, state);
            Assert.AreEqual(crates, loaded.Supply.Crates.Count);
            Assert.AreEqual(sim.Supply.ResearchPoints, loaded.Supply.ResearchPoints);
            Assert.AreEqual(sim.Supply.Crates[0].Bonus, loaded.Supply.Crates[0].Bonus);
        }

        [Test]
        public void FieldRepairPoints_BySize()
        {
            Assert.AreEqual(2, _balance.FieldRepairPoints(1));
            Assert.AreEqual(2, _balance.FieldRepairPoints(2));
            Assert.AreEqual(3, _balance.FieldRepairPoints(3));
            Assert.AreEqual(3, _balance.FieldRepairPoints(12));
        }

        [Test]
        public void Spread_DamagesHealthyNeighbor_NotCore_AndChains()
        {
            var b = new SerializedObject(_balance);
            b.FindProperty("_spreadAfterSeconds").floatValue = 60f;
            b.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var a);        // 코어 옆
            sim.TryPlace(_solar, new Vector3Int(2, 0, 0), 0, out var c); // a 옆
            ModuleInstance spreadTo = null;
            sim.DamageSpread += (_, to) => spreadTo = to;

            sim.Damage.Damage(a);
            for (int i = 0; i < 60; i++)
                sim.Tick(1f);
            Assert.AreSame(c, spreadTo, "a의 이웃은 코어와 c → 코어 제외");
            Assert.IsTrue(sim.Damage.IsDamaged(c));
            Assert.AreEqual(1, sim.Session.DamageSpreads);

            // c도 60초 방치 → 이웃 a는 이미 파손 → 번질 곳 없음
            for (int i = 0; i < 60; i++)
                sim.Tick(1f);
            Assert.AreEqual(1, sim.Session.DamageSpreads);
        }

        [Test]
        public void GradeScaling_MeteorCountAndIntensity()
        {
            // 두 번째 등급(소형): 운석 2~3개, 강도 ×1.3 / 조건을 낮춰 바로 도달
            var g = new SerializedObject(_grades);
            var small = g.FindProperty("_grades").GetArrayElementAtIndex(1);
            small.FindPropertyRelative("_minPopulation").intValue = 0;
            small.FindPropertyRelative("_minModules").intValue = 4;
            small.FindPropertyRelative("_meteorHitsMin").intValue = 2;
            small.FindPropertyRelative("_meteorHitsMax").intValue = 3;
            small.FindPropertyRelative("_eventIntensityMultiplier").floatValue = 1.3f;
            small.FindPropertyRelative("_eventIntervalMultiplier").floatValue = 0.75f;
            g.ApplyModifiedPropertiesWithoutUndo();

            var sim = Sim(); // Random01 = 0 → 최소 개수
            sim.TryPlace(_solar, Vector3Int.right, 0, out _);
            sim.TryPlace(_solar, Vector3Int.left, 0, out _);
            sim.TryPlace(_solar, Vector3Int.up, 0, out _);
            Assert.AreEqual(1, sim.Progression.GradeIndex);
            Assert.AreEqual(1.3f, sim.EventIntensity, Eps);

            sim.Events.Trigger(_meteor);
            Assert.AreEqual(2, sim.Damage.DamagedCount, "소형 최소 2개");

            float metal = sim.Resources.GetStock(ResourceType.Metal); // 150 - 60 = 90
            sim.Events.Trigger(_supply);
            Assert.AreEqual(metal + 60f * 1.3f, sim.Resources.GetStock(ResourceType.Metal), Eps, "보급 ×1.3");
        }

        [Test]
        public void SupplyShip_AddsRewards()
        {
            var sim = Sim();
            sim.Events.Trigger(_supply);
            Assert.AreEqual(200f, sim.Resources.GetStock(ResourceType.Metal), Eps, "150 + 60 → 한도 200");
        }

        // ---- helpers ----

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }

        [Test]
        public void ModuleEfficiency_HealthyDamagedAndDisconnected()
        {
            var sim = Sim();
            sim.TryPlace(_solar, Vector3Int.right, 0, out var near);
            sim.TryPlace(_solar, Vector3Int.right * 2, 0, out var far);
            Assert.AreEqual(1f, sim.GetModuleEfficiency(near), Eps);
            Assert.AreEqual(EfficiencyBand.Normal, EfficiencyBands.Classify(sim.GetModuleEfficiency(near)));

            sim.Damage.Damage(near);
            Assert.AreEqual(0.5f, sim.GetModuleEfficiency(near), Eps, "파손 배율");
            Assert.AreEqual(EfficiencyBand.Warning, EfficiencyBands.Classify(sim.GetModuleEfficiency(near)));

            Assert.IsTrue(sim.TryRemove(near));
            Assert.IsFalse(sim.Connectivity.IsActive(far));
            Assert.AreEqual(0f, sim.GetModuleEfficiency(far), Eps, "코어와 분리되면 0");
            Assert.AreEqual(EfficiencyBand.Critical, EfficiencyBands.Classify(sim.GetModuleEfficiency(far)));
        }

        [Test]
        public void EfficiencyBands_BoundariesBelongToUpperBand()
        {
            Assert.AreEqual(EfficiencyBand.Normal, EfficiencyBands.Classify(1.2f), "인접·연구 보너스로 100% 초과");
            Assert.AreEqual(EfficiencyBand.Normal, EfficiencyBands.Classify(0.9f));
            Assert.AreEqual(EfficiencyBand.Normal, EfficiencyBands.Classify(0.3f * 3f), "부동소수 오차 0.8999…");
            Assert.AreEqual(EfficiencyBand.Warning, EfficiencyBands.Classify(0.89f));
            Assert.AreEqual(EfficiencyBand.Warning, EfficiencyBands.Classify(0.5f));
            Assert.AreEqual(EfficiencyBand.Danger, EfficiencyBands.Classify(0.49f));
            Assert.AreEqual(EfficiencyBand.Danger, EfficiencyBands.Classify(0.25f));
            Assert.AreEqual(EfficiencyBand.Critical, EfficiencyBands.Classify(0.24f));
            Assert.AreEqual(EfficiencyBand.Critical, EfficiencyBands.Classify(0f));
        }

        private ModuleData Module(string name, float cost, float supply = 0, int housing = 0, float storage = 0, bool removable = true)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            if (cost > 0) Fill(so.FindProperty("_buildCost"), R(ResourceType.Metal, cost));
            if (supply > 0) Fill(so.FindProperty("_production"), R(ResourceType.Power, supply));
            so.FindProperty("_housingCapacity").intValue = housing;
            so.FindProperty("_storageBonus").floatValue = storage;
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

        private static void SetGrade(SerializedProperty grade, string name, int pop, int modules, int maxLimited, params ModuleData[] unlocks)
        {
            grade.FindPropertyRelative("_displayName").stringValue = name;
            grade.FindPropertyRelative("_minPopulation").intValue = pop;
            grade.FindPropertyRelative("_minModules").intValue = modules;
            grade.FindPropertyRelative("_maxLimitedModules").intValue = maxLimited;
            var list = grade.FindPropertyRelative("_unlocks");
            list.arraySize = unlocks.Length;
            for (int i = 0; i < unlocks.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = unlocks[i];
        }
    }
}
