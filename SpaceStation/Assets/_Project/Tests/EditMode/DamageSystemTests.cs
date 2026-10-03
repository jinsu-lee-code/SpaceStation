using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>GDD 12-2 / BALANCE.md 10번 파손·수리·파괴 규칙.</summary>
    public class DamageSystemTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private DamageSystem _damage;
        private StationGrid _grid;
        private ModuleData _block;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BalanceConfig>();
            _created.Add(_config);
            var so = new SerializedObject(_config);
            so.FindProperty("_damagedProductionMultiplier").floatValue = 0.5f;
            so.FindProperty("_damagedOxygenLeakPerSecond").floatValue = 0.3f;
            so.FindProperty("_repairDuration").floatValue = 10f;
            so.FindProperty("_destroyAfterSeconds").floatValue = 120f;
            so.FindProperty("_repairCostRate").floatValue = 0.3f;
            so.FindProperty("_meteorExposureSlope").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            _block = ScriptableObject.CreateInstance<ModuleData>();
            _created.Add(_block);
            var mso = new SerializedObject(_block);
            var cost = mso.FindProperty("_buildCost");
            cost.arraySize = 1;
            cost.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Metal;
            cost.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 40f;
            mso.ApplyModifiedPropertiesWithoutUndo();

            _damage = new DamageSystem(_config);
            _grid = new StationGrid();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        private ModuleInstance Place(int x, int y = 0, int z = 0)
        {
            Assert.IsTrue(_grid.TryPlace(_block, new Vector3Int(x, y, z), 0, out var m));
            return m;
        }

        [Test]
        public void Damaged_HalvesProduction_AndLeaksOxygen()
        {
            var m = Place(0);
            Assert.AreEqual(1f, _damage.GetProductionMultiplier(m));
            Assert.IsTrue(_damage.Damage(m));
            Assert.IsFalse(_damage.Damage(m), "중복 파손 없음");
            Assert.AreEqual(0.5f, _damage.GetProductionMultiplier(m), Eps);
            Assert.AreEqual(0.3f, _damage.OxygenLeakPerSecond, Eps);

            _damage.Damage(Place(1));
            Assert.AreEqual(0.6f, _damage.OxygenLeakPerSecond, Eps, "파손 모듈 수만큼 누출");
        }

        [Test]
        public void Unrepaired_DestroyedAfter120Seconds()
        {
            var m = Place(0);
            ModuleInstance destroyed = null;
            _damage.Destroyed += d => destroyed = d;
            _damage.Damage(m);

            Ticks(119);
            Assert.IsNull(destroyed);
            Assert.IsTrue(_damage.TryGetInfo(m, out var info));
            Assert.AreEqual(1f, info.TimeUntilDestroyed, Eps);
            Ticks(1);
            Assert.AreSame(m, destroyed);
            Assert.IsFalse(_damage.IsDamaged(m));
        }

        [Test]
        public void Repair_Takes10Seconds_StopsLeakAndDestroyTimer_ZeroProduction()
        {
            var m = Place(0);
            ModuleInstance repaired = null, destroyed = null;
            _damage.Repaired += r => repaired = r;
            _damage.Destroyed += d => destroyed = d;
            _damage.Damage(m);
            Ticks(115); // 파괴 5초 전

            Assert.IsTrue(_damage.StartRepair(m));
            Assert.IsFalse(_damage.StartRepair(m), "이미 수리 중");
            Assert.AreEqual(0f, _damage.GetProductionMultiplier(m), "수리 중 생산 0");
            Assert.AreEqual(0f, _damage.OxygenLeakPerSecond, "수리 중 누출 없음");

            Ticks(9);
            Assert.IsNull(repaired);
            Assert.IsNull(destroyed, "수리 중에는 파괴 타이머가 멈춤");
            Ticks(1);
            Assert.AreSame(m, repaired);
            Assert.AreEqual(1f, _damage.GetProductionMultiplier(m));
        }

        [Test]
        public void RepairCost_Is30PercentOfBuildCost()
        {
            var cost = _damage.GetRepairCost(Place(0));
            Assert.AreEqual(1, cost.Count);
            Assert.AreEqual(ResourceType.Metal, cost[0].Type);
            Assert.AreEqual(12f, cost[0].Amount, Eps);
        }

        [Test]
        public void RepairOnUndamaged_Fails()
        {
            Assert.IsFalse(_damage.StartRepair(Place(0)));
        }

        // ---------------- 4-6 수리 슬롯 / 대기열 ----------------

        [Test]
        public void Capacity_ExtraRequestsQueue_AndStartWhenSlotFrees()
        {
            _damage.RepairCapacity = 1;
            var a = Place(0); var b = Place(1); var c = Place(2);
            _damage.Damage(a); _damage.Damage(b); _damage.Damage(c);

            Assert.IsTrue(_damage.StartRepair(a));
            Assert.IsTrue(_damage.StartRepair(b));
            Assert.IsTrue(_damage.StartRepair(c));
            Assert.IsFalse(_damage.StartRepair(b), "이미 대기 중");
            Assert.AreEqual(1, _damage.RepairingCount);
            Assert.AreEqual(1, _damage.GetQueuePosition(b));
            Assert.AreEqual(2, _damage.GetQueuePosition(c));
            Assert.AreEqual(0.6f, _damage.OxygenLeakPerSecond, Eps, "대기 중에는 계속 누출");

            Ticks(10); // a 완료 → b 시작
            Assert.IsFalse(_damage.IsDamaged(a));
            _damage.TryGetInfo(b, out var ib);
            Assert.IsTrue(ib.IsRepairing);
            Assert.AreEqual(1, _damage.GetQueuePosition(c));
            _damage.TryGetInfo(c, out var ic);
            Assert.AreEqual(110f, ic.TimeUntilDestroyed, Eps, "대기 중에도 파괴 타이머 진행");
        }

        [Test]
        public void Prioritize_MovesToFront_Cancel_RemovesFromQueue()
        {
            _damage.RepairCapacity = 1;
            var a = Place(0); var b = Place(1); var c = Place(2);
            foreach (var m in new[] { a, b, c }) { _damage.Damage(m); _damage.StartRepair(m); }

            Assert.IsFalse(_damage.Prioritize(b), "이미 맨 앞");
            Assert.IsTrue(_damage.Prioritize(c));
            Assert.AreEqual(1, _damage.GetQueuePosition(c));
            Assert.AreEqual(2, _damage.GetQueuePosition(b));

            Assert.IsTrue(_damage.CancelQueued(c));
            _damage.TryGetInfo(c, out var ic);
            Assert.IsFalse(ic.IsQueued);
            Assert.IsTrue(_damage.IsDamaged(c), "취소해도 파손 상태는 유지");
            Assert.AreEqual(1, _damage.GetQueuePosition(b));
            Assert.IsFalse(_damage.CancelQueued(a), "수리 중은 취소 불가");
        }

        [Test]
        public void QueuedModule_DestroyedIfWaitTooLong()
        {
            _damage.RepairCapacity = 0;
            var m = Place(0);
            ModuleInstance destroyed = null;
            _damage.Destroyed += d => destroyed = d;
            _damage.Damage(m);
            _damage.StartRepair(m);
            Ticks(120);
            Assert.AreSame(m, destroyed);
            Assert.AreEqual(0, _damage.Queue.Count);
        }

        // ---------------- 4-7 확산 ----------------

        private void EnableSpread(float seconds)
        {
            var so = new SerializedObject(_config);
            so.FindProperty("_spreadAfterSeconds").floatValue = seconds;
            so.ApplyModifiedPropertiesWithoutUndo();
            _damage = new DamageSystem(_config);
        }

        [Test]
        public void Spread_ModuleMultiplier_ShortensTimer()
        {
            // 8-1 핵융합로: 확산 시간 배율 0.5 → 60초가 30초
            EnableSpread(60f);
            var fast = ScriptableObject.CreateInstance<ModuleData>();
            _created.Add(fast);
            var so = new SerializedObject(fast);
            so.FindProperty("_spreadTimeMultiplier").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(_grid.TryPlace(fast, new Vector3Int(5, 0, 0), 0, out var reactor));
            var normal = Place(0);
            _damage.Damage(reactor);
            _damage.Damage(normal);
            _damage.TryGetInfo(reactor, out var ri);
            _damage.TryGetInfo(normal, out var ni);
            Assert.AreEqual(30f, ri.TimeUntilSpread, Eps);
            Assert.AreEqual(60f, ni.TimeUntilSpread, Eps);
        }

        // ---------------- 8-5 장갑 격벽 ----------------

        private ModuleData Armor()
        {
            var armor = ScriptableObject.CreateInstance<ModuleData>();
            _created.Add(armor);
            var so = new SerializedObject(armor);
            so.FindProperty("_meteorWeightMultiplier").floatValue = 3f;
            so.FindProperty("_armored").boolValue = true;
            so.FindProperty("_repairTimeMultiplier").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return armor;
        }

        [Test]
        public void Armored_NoLeak_NoSpread_NeverDestroyed_HalfRepair()
        {
            EnableSpread(60f);
            Assert.IsTrue(_grid.TryPlace(Armor(), new Vector3Int(5, 0, 0), 0, out var wall));
            ModuleInstance destroyed = null, spread = null;
            _damage.Destroyed += d => destroyed = d;
            _damage.SpreadDue += s => spread = s;
            _damage.Damage(wall);
            Assert.AreEqual(0f, _damage.OxygenLeakPerSecond, Eps, "누출 없음");
            Ticks(500);
            Assert.IsNull(destroyed, "방치해도 파괴 없음");
            Assert.IsNull(spread, "확산 없음");
            _damage.TryGetInfo(wall, out var info);
            Assert.IsTrue(info.NeverDestroyed);
            _damage.StartRepair(wall);
            Assert.AreEqual(5f, info.RepairRemaining, Eps, "수리 10초의 절반");
        }

        [Test]
        public void Armored_MeteorWeight_TripledForSameExposure()
        {
            Assert.IsTrue(_grid.TryPlace(Armor(), new Vector3Int(5, 0, 0), 0, out var wall));
            var normal = Place(-5);
            Assert.AreEqual(_damage.ModuleMeteorWeight(_grid, normal) * 3f, _damage.ModuleMeteorWeight(_grid, wall), Eps);
        }

        // ---------------- 8-3 손상 통제 ----------------

        [Test]
        public void Control_AtDamage_ScalesDestroyAndSpreadTimers()
        {
            EnableSpread(60f);
            var m = Place(0);
            _damage.ControlLookup = _ => new DamageControlEffect { SpreadMultiplier = 2f, DestroyMultiplier = 1.5f };
            _damage.Damage(m);
            _damage.TryGetInfo(m, out var info);
            Assert.AreEqual(180f, info.TimeUntilDestroyed, Eps);
            Assert.AreEqual(120f, info.TimeUntilSpread, Eps);
            Assert.IsTrue(info.IsControlled);
        }

        [Test]
        public void Control_AddedOrLostMidway_RescalesRemainingTime()
        {
            EnableSpread(60f);
            var m = Place(0);
            var effect = DamageControlEffect.None;
            _damage.ControlLookup = _ => effect;
            _damage.Damage(m);
            Ticks(20); // 남은 100 / 40
            effect = new DamageControlEffect { SpreadMultiplier = 2f, DestroyMultiplier = 1.5f };
            _damage.Tick(0f);
            _damage.TryGetInfo(m, out var info);
            Assert.AreEqual(150f, info.TimeUntilDestroyed, Eps, "통제실이 생기면 남은 시간 × 1.5");
            Assert.AreEqual(80f, info.TimeUntilSpread, Eps);

            Ticks(30); // 120 / 50
            effect = DamageControlEffect.None;
            _damage.Tick(0f);
            Assert.AreEqual(80f, info.TimeUntilDestroyed, Eps, "통제실이 사라지면 원래 비율로");
            Assert.AreEqual(25f, info.TimeUntilSpread, Eps);
        }

        [Test]
        public void DefenseSystem_DamageControl_RangeStrongestAndStrength()
        {
            var control = ScriptableObject.CreateInstance<ModuleData>();
            _created.Add(control);
            var so = new SerializedObject(control);
            so.FindProperty("_controlRadius").intValue = 2;
            so.FindProperty("_controlSpreadMultiplier").floatValue = 2f;
            so.FindProperty("_controlDestroyMultiplier").floatValue = 1.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(control.IsDamageControl && control.IsDefense);

            float strength = 1f;
            var defense = new DefenseSystem(_config, _ => strength);
            Assert.IsTrue(_grid.TryPlace(control, Vector3Int.zero, 0, out _));
            var near = Place(2);
            var far = Place(3);
            Assert.AreEqual(1.5f, defense.GetDamageControl(_grid, near).DestroyMultiplier, Eps);
            Assert.AreEqual(2f, defense.GetDamageControl(_grid, near).SpreadMultiplier, Eps);
            Assert.AreEqual(1f, defense.GetDamageControl(_grid, far).DestroyMultiplier, Eps, "범위 밖");
            strength = 0.5f;
            Assert.AreEqual(1.25f, defense.GetDamageControl(_grid, near).DestroyMultiplier, Eps, "가동률 반영");
            strength = 0f;
            Assert.AreEqual(1f, defense.GetDamageControl(_grid, near).DestroyMultiplier, Eps, "파손·비활성이면 효과 없음");
        }

        [Test]
        public void Spread_FiresOnceAfter60s_WhenLeftAlone()
        {
            EnableSpread(60f);
            var m = Place(0);
            int fired = 0;
            _damage.SpreadDue += s => { Assert.AreSame(m, s); fired++; };
            _damage.Damage(m);
            Ticks(59);
            Assert.AreEqual(0, fired);
            _damage.TryGetInfo(m, out var info);
            Assert.AreEqual(1f, info.TimeUntilSpread, Eps);
            Ticks(1);
            Assert.AreEqual(1, fired);
            Assert.IsTrue(info.HasSpread);
            Assert.IsFalse(info.SpreadPending);
            Ticks(59);
            Assert.AreEqual(1, fired, "한 번만 번짐");
        }

        [Test]
        public void Spread_PausedWhileQueued_StoppedByRepair_DisabledAtZero()
        {
            EnableSpread(60f);
            _damage.RepairCapacity = 0;
            var m = Place(0);
            int fired = 0;
            _damage.SpreadDue += _ => fired++;
            _damage.Damage(m);
            Ticks(30);
            _damage.StartRepair(m); // 슬롯 0 → 대기
            Ticks(60);
            Assert.AreEqual(0, fired, "대기 중에는 확산 멈춤");
            _damage.TryGetInfo(m, out var info);
            Assert.AreEqual(30f, info.TimeUntilSpread, Eps, "멈춘 시점 그대로");
            _damage.CancelQueued(m);
            Ticks(29);
            Assert.AreEqual(0, fired);

            _damage.RepairCapacity = 1;
            _damage.StartRepair(m); // 수리 시작 → 확산 없음
            Ticks(5);
            Assert.AreEqual(0, fired);

            EnableSpread(0f);
            var n = Place(1);
            _damage.Damage(n);
            _damage.TryGetInfo(n, out var off);
            Assert.IsFalse(off.SpreadPending, "0이면 확산 없음");
        }

        [Test]
        public void Prioritize_KeepsTimers_DoesNotPreemptRunningRepair()
        {
            EnableSpread(60f);
            _damage.RepairCapacity = 1;
            var a = Place(0); var b = Place(1); var c = Place(2);
            int spread = 0;
            _damage.SpreadDue += _ => spread++;
            foreach (var m in new[] { a, b, c }) _damage.Damage(m);
            _damage.StartRepair(a); // 수리 중
            Ticks(5);
            _damage.StartRepair(b); // 대기 1
            _damage.StartRepair(c); // 대기 2
            _damage.TryGetInfo(c, out var ic);
            float spreadBefore = ic.TimeUntilSpread, destroyBefore = ic.TimeUntilDestroyed;

            Assert.IsTrue(_damage.Prioritize(c));
            Assert.AreEqual(1, _damage.RepairingCount, "진행 중인 수리를 밀어내지 않음");
            _damage.TryGetInfo(a, out var ia);
            Assert.IsTrue(ia.IsRepairing);
            Assert.IsTrue(ic.IsQueued);
            Assert.AreEqual(spreadBefore, ic.TimeUntilSpread, Eps, "순서 변경이 타이머를 건드리지 않음");
            Assert.AreEqual(destroyBefore, ic.TimeUntilDestroyed, Eps);

            Ticks(5); // a 완료 → c(맨 앞) 시작
            Assert.IsTrue(ic.IsRepairing);
            _damage.TryGetInfo(b, out var ib);
            Assert.IsTrue(ib.IsQueued);
            Assert.AreEqual(1, _damage.GetQueuePosition(b));
            Assert.AreEqual(0, spread, "대기 중 확산 없음");
            Assert.IsFalse(_damage.Prioritize(a), "대기 중이 아니면 순서 변경 불가");
        }

        [Test]
        public void Spread_ContinuesWhileQueued_WhenOptionOff()
        {
            var so = new SerializedObject(_config);
            so.FindProperty("_queuePausesSpread").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EnableSpread(60f);
            _damage.RepairCapacity = 0;
            var m = Place(0);
            int fired = 0;
            _damage.SpreadDue += _ => fired++;
            _damage.Damage(m);
            _damage.StartRepair(m);
            Ticks(60);
            Assert.AreEqual(1, fired, "대기 중에도 번짐");
        }

        [Test]
        public void CapacityIncrease_StartsQueuedImmediately()
        {
            _damage.RepairCapacity = 1;
            var a = Place(0); var b = Place(1);
            _damage.Damage(a); _damage.Damage(b);
            _damage.StartRepair(a); _damage.StartRepair(b);
            _damage.RepairCapacity = 2;
            Assert.AreEqual(2, _damage.RepairingCount);
            Assert.AreEqual(0, _damage.Queue.Count);

            _damage.RepairCapacity = 1; // 줄어도 진행 중인 수리는 유지
            Assert.AreEqual(2, _damage.RepairingCount);
        }

        [Test]
        public void MeteorCandidates_ExteriorOnly_ExcludingCoreAndDamaged()
        {
            var core = Place(0);
            var inner = Place(0, 1, 0);           // 6면이 막힐 모듈
            foreach (var dir in GridDirections.Faces)
            {
                var c = new Vector3Int(0, 1, 0) + dir;
                if (!_grid.IsOccupied(c))
                    Place(c.x, c.y, c.z);
            }
            var results = new List<ModuleInstance>();
            _damage.FindMeteorCandidates(_grid, core, results);

            CollectionAssert.DoesNotContain(results, core, "코어 면역");
            CollectionAssert.DoesNotContain(results, inner, "외곽이 아닌 모듈 제외");
            Assert.AreEqual(5, results.Count);

            _damage.Damage(results[0]);
            var damagedOne = results[0];
            _damage.FindMeteorCandidates(_grid, core, results);
            CollectionAssert.DoesNotContain(results, damagedOne, "이미 파손된 모듈 제외");
        }

        [Test]
        public void ExposedFaces_CountsEmptyNeighbors()
        {
            var a = Place(0);
            Assert.AreEqual(6, DamageSystem.CountExposedFaces(_grid, a));
            Place(1);
            Assert.AreEqual(5, DamageSystem.CountExposedFaces(_grid, a));
        }

        [Test]
        public void MeteorTargets_DistinctAndCapped()
        {
            var core = Place(0);
            Place(1); Place(2); Place(3);
            var results = new List<ModuleInstance>();

            _damage.PickMeteorTargets(_grid, core, 2, () => 0.5f, results);
            Assert.AreEqual(2, results.Count);
            Assert.AreNotSame(results[0], results[1], "중복 없음");
            CollectionAssert.DoesNotContain(results, core);

            _damage.PickMeteorTargets(_grid, core, 10, () => 0.5f, results);
            Assert.AreEqual(3, results.Count, "후보보다 많이 요청하면 후보 전부");
        }

        [Test]
        public void MeteorWeight_LinearSlope3()
        {
            Assert.AreEqual(0f, _damage.GetMeteorWeight(0));
            Assert.AreEqual(1f, _damage.GetMeteorWeight(1), Eps);
            Assert.AreEqual(4f, _damage.GetMeteorWeight(2), Eps);
            Assert.AreEqual(13f, _damage.GetMeteorWeight(5), Eps, "가지 끝(5면)은 1면 대비 13배");
        }

        [Test]
        public void MeteorTargets_WeightedByExposure()
        {
            // 코어(0,0,0) 기준 +X로 뻗은 가지: (1,0,0) 노출 4 → 가중치 10, (2,0,0) 노출 5(끝) → 13, 합 23
            var core = Place(0);
            var middle = Place(1);
            var tip = Place(2);
            var results = new List<ModuleInstance>();
            _damage.PickMeteorTargets(_grid, core, 1, () => 0.5f, results); // 11.5 ≥ 10 → 끝 모듈
            Assert.AreSame(tip, results[0]);

            _damage.PickMeteorTargets(_grid, core, 1, () => 0.4f, results); // 9.2 < 10 → 첫 후보
            Assert.AreSame(middle, results[0]);
        }

        [Test]
        public void Forget_ClearsStateWithoutEvents()
        {
            var m = Place(0);
            bool destroyedFired = false;
            _damage.Destroyed += _ => destroyedFired = true;
            _damage.Damage(m);
            _damage.Forget(m);
            Ticks(200);
            Assert.IsFalse(destroyedFired);
            Assert.AreEqual(0, _damage.DamagedCount);
        }

        private void Ticks(int n)
        {
            for (int i = 0; i < n; i++)
                _damage.Tick(1f);
        }
    }
}
