using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>4-8 방어 모듈 (BALANCE 19번).</summary>
    public class DefenseTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private ModuleData _block, _shield, _turret;
        private StationGrid _grid;
        private readonly Dictionary<ModuleInstance, float> _strength = new Dictionary<ModuleInstance, float>();

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var so = new SerializedObject(_config);
            so.FindProperty("_turretMaxIntercept").floatValue = 0.6f;
            so.ApplyModifiedPropertiesWithoutUndo();

            _block = Create<ModuleData>();
            _shield = Create<ModuleData>();
            var s = new SerializedObject(_shield);
            s.FindProperty("_shieldRadius").intValue = 2;
            s.FindProperty("_shieldReduction").floatValue = 0.7f;
            s.ApplyModifiedPropertiesWithoutUndo();
            _turret = Create<ModuleData>();
            var t = new SerializedObject(_turret);
            t.FindProperty("_turretRadius").intValue = 2;
            t.FindProperty("_turretInterceptChance").floatValue = 0.2f;
            t.ApplyModifiedPropertiesWithoutUndo();

            _grid = new StationGrid();
            _strength.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        private DefenseSystem System() => new DefenseSystem(_config, m => _strength.TryGetValue(m, out var v) ? v : 1f);

        private ModuleInstance Place(ModuleData data, int x, int y = 0, int z = 0)
        {
            Assert.IsTrue(_grid.TryPlace(data, new Vector3Int(x, y, z), 0, out var m));
            return m;
        }

        [Test]
        public void Shield_BlocksInsideChebyshevRadius_NoStacking()
        {
            var defense = System();
            var shield = Place(_shield, 0);
            var diagonal = Place(_block, 2, 2, 0);
            var outside = Place(_block, 3, 0, 0);
            Assert.AreEqual(0.7f, defense.GetShieldBlockChance(_grid, diagonal), Eps, "대각선 2칸도 범위 안");
            Assert.AreEqual(0.7f, defense.GetShieldBlockChance(_grid, shield), Eps, "실드 자신도 보호");
            Assert.AreEqual(0f, defense.GetShieldBlockChance(_grid, outside), Eps);

            Place(_shield, 1, 2, 0);
            Assert.AreEqual(0.7f, defense.GetShieldBlockChance(_grid, diagonal), Eps, "중첩 없음");
        }

        [Test]
        public void Shield_ScalesWithStrength()
        {
            var defense = System();
            var shield = Place(_shield, 0);
            var target = Place(_block, 1);
            _strength[shield] = 0.5f;
            Assert.AreEqual(0.35f, defense.GetShieldBlockChance(_grid, target), Eps);
            _strength[shield] = 0f;
            Assert.AreEqual(0f, defense.GetShieldBlockChance(_grid, target), Eps, "파손·비활성 = 효과 없음");
        }

        [Test]
        public void Integration_ShieldBlocksMeteor()
        {
            var sim = MakeSim(out var meteor);
            sim.Grid.TryPlace(_shield, Vector3Int.right, 0, out var shield);
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out _);
            ModuleInstance deflectedBy = null;
            int deflectEvents = 0;
            sim.ShieldDeflected += m => { deflectedBy = m; deflectEvents++; };
            sim.Events.Trigger(meteor);
            Assert.AreEqual(0, sim.Damage.DamagedCount, "차단 → 피해 없음");
            Assert.AreEqual(1, sim.Session.MeteorsBlocked);
            Assert.AreEqual(0, sim.Session.MeteorsIntercepted);
            Assert.AreEqual(1, deflectEvents, "빗겨냄 이벤트 (실드 연출)");
            Assert.AreSame(shield, deflectedBy);
        }

        private void SetRicochet(float chance)
        {
            var so = new SerializedObject(_config);
            so.FindProperty("_shieldRicochetChance").floatValue = chance;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void Integration_ShieldDeflects_RicochetHitsModuleOutsideRange()
        {
            SetRicochet(0.7f);
            var sim = MakeSim(out var meteor);
            sim.Grid.TryPlace(_shield, Vector3Int.right, 0, out var shield);
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out var inside);         // 실드와 거리 2
            sim.Grid.TryPlace(_block, new Vector3Int(4, 0, 0), 0, out var outside); // 실드와 거리 3

            sim.Events.Trigger(meteor); // 운석 1발, 모든 확률 판정 성공(Random01 = 0)
            Assert.IsTrue(sim.Damage.IsDamaged(outside), "보호받는 곳을 노렸으면 튕겨서, 아니면 직접 → 어느 쪽이든 범위 밖 모듈");
            Assert.IsFalse(sim.Damage.IsDamaged(inside));
            Assert.IsFalse(sim.Damage.IsDamaged(shield));
            Assert.AreEqual(sim.Session.MeteorsBlocked, sim.Session.Ricochets, "빗겨냈다면 튕겨서 명중");
        }

        [Test]
        public void Integration_Ricochet_NoTargetOutside_GoesToSpace()
        {
            SetRicochet(1f);
            var sim = MakeSim(out var meteor);
            sim.Grid.TryPlace(_shield, Vector3Int.right, 0, out _);
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out _);
            sim.Events.Trigger(meteor);
            Assert.AreEqual(0, sim.Damage.DamagedCount, "모두 실드 안 → 튕길 곳 없음 → 우주로");
            Assert.AreEqual(1, sim.Session.MeteorsBlocked);
            Assert.AreEqual(0, sim.Session.Ricochets);
        }

        private StationSimulation MakeSim(out MeteorEventData meteor)
        {
            var core = Create<ModuleData>();
            var cso = new SerializedObject(core);
            cso.FindProperty("_removable").boolValue = false;
            cso.ApplyModifiedPropertiesWithoutUndo();
            var grades = Create<StationGradeConfig>();
            var g = new SerializedObject(grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();
            meteor = Create<MeteorEventData>();
            return new StationSimulation(new StationSimulationSettings
            {
                Balance = _config, Grades = grades, CoreModule = core, Events = new GameEventData[] { meteor },
                Random01 = () => 0f, // 확률 판정 항상 성공 (확률 > 0이면)
            });
        }

        [Test]
        public void Turret_SumsInRange_CappedAt60Percent()
        {
            var defense = System();
            var target = Place(_block, 0);
            Place(_turret, 1);
            Place(_turret, -1);
            Assert.AreEqual(0.4f, defense.GetInterceptChance(_grid, target), Eps);
            Place(_turret, 0, 1);
            Place(_turret, 0, -1);
            Assert.AreEqual(0.6f, defense.GetInterceptChance(_grid, target), Eps, "상한");
            var far = Place(_block, 10);
            Assert.AreEqual(0f, defense.GetInterceptChance(_grid, far), Eps);
        }

        [Test]
        public void CountCovered_ForPlacementPreview()
        {
            Place(_block, 0);
            Place(_block, 2);
            Place(_block, 5);
            var cells = StationGrid.ResolveCells(_shield.CellOffsets, new Vector3Int(1, 0, 0), 0);
            Assert.AreEqual(2, DefenseSystem.CountCovered(_grid, _shield, cells));
            Assert.AreEqual(0, DefenseSystem.CountCovered(_grid, _block, cells), "방어 모듈이 아니면 0");
        }

        [Test]
        public void Integration_TurretInterceptsMeteor_DamagedTurretDoesNot()
        {
            var sim = MakeSim(out var meteor);
            sim.Grid.TryPlace(_turret, Vector3Int.right, 0, out var turret);
            sim.Grid.TryPlace(_block, Vector3Int.left, 0, out var target);

            sim.Events.Trigger(meteor);
            Assert.AreEqual(0, sim.Damage.DamagedCount, "격추 → 피해 없음");
            Assert.AreEqual(1, sim.Session.MeteorsIntercepted);

            sim.Damage.Damage(turret); // 포탑 파손 → 가동률 0
            sim.Events.Trigger(meteor);
            Assert.AreEqual(2, sim.Damage.DamagedCount, "포탑이 꺼져 있으면 격추 없음 (포탑 + 대상)");
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
