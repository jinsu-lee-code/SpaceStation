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
