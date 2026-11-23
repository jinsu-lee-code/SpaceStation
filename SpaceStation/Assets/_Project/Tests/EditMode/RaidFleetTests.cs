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
    /// REVIEW_1115 6번 외부 위협 1차 공통 고리 (사용자 결정 2026-11-24): 단계(정찰 · 함대 · 기함) · 평판 + 막아 낸 횟수 · 함선 체력 · 포탑 격추 반격 ·
    /// 기함 꺾음 = 그 세력 습격 멈춤 · 시간 안에 못 꺾으면 큰 포격 · 규칙이 닫히면 기존 습격 · 크기 배율 · 세이브.
    /// </summary>
    public class RaidFleetTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _block, _turret;
        private ContactConfig _contact;
        private RaidFleetConfig _fleet;

        private sealed class Rules : IStationRules
        {
            public StationRule Open;
            public bool Allows(StationRule rule) => rule != StationRule.None && (Open & rule) == rule;
        }

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Water, 150), R(ResourceType.Food, 150), R(ResourceType.Metal, 500));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 500), R(ResourceType.Water, 500), R(ResourceType.Food, 500), R(ResourceType.Metal, 1000));
            b.FindProperty("_destroyAfterSeconds").floatValue = 100000f;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.FindProperty("_turretMaxIntercept").floatValue = 1f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", false);
            _block = Module("MD_TestBlock", true);
            _turret = Module("MD_TestTurret", true);
            var t = new SerializedObject(_turret);
            t.FindProperty("_turretRadius").intValue = 6;
            t.FindProperty("_turretInterceptChance").floatValue = 1f;
            t.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();

            _fleet = Create<RaidFleetConfig>();
            _contact = Create<ContactConfig>();
            var c = new SerializedObject(_contact);
            c.FindProperty("_fleet").objectReferenceValue = _fleet;
            c.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void RuleClosed_LeavesLegacyRaids()
        {
            var sim = Sim(StationRule.Contact);
            Assert.IsNotNull(sim.RaidFleet, "함대 값이 있으면 붙음");
            Assert.IsFalse(sim.RaidFleet.Enabled, "규칙 '함대 습격'이 닫혀 있으면 넘겨받지 않음");
            sim.SetRules(new Rules { Open = StationRule.Contact | StationRule.FleetRaid });
            Assert.IsTrue(sim.RaidFleet.Enabled);
        }

        [Test]
        public void Stage_FollowsReputation_ThenHeldFleets()
        {
            var sim = Sim(StationRule.Contact | StationRule.FleetRaid);
            var raid = sim.RaidFleet;
            Assert.AreEqual(RaidStage.Scout, raid.StageFor(ContactFaction.Outlaw, ReputationTier.Wary), "냉담 = 정찰");
            Assert.AreEqual(RaidStage.Fleet, raid.StageFor(ContactFaction.Outlaw, ReputationTier.Hostile), "적대 = 함대");
            for (int n = 0; n < 2; n++)
            {
                raid.Start(RaidStage.Fleet, ContactFaction.Outlaw, "무법 선단");
                Assert.AreEqual(RaidStage.Fleet, raid.Stage);
                Assert.AreEqual(4, raid.Ships.Count);
                for (int i = 0; i < raid.Ships.Count; i++)
                    raid.DamageShip(i, 100f);
                Assert.AreEqual(RaidStage.None, raid.Stage, "함선을 모두 꺾으면 끝");
            }
            Assert.AreEqual(2, raid.FleetsHeld(ContactFaction.Outlaw));
            Assert.AreEqual(RaidStage.Flagship, raid.StageFor(ContactFaction.Outlaw, ReputationTier.Hostile), "함대를 2번 막으면 기함");
            Assert.AreEqual(RaidStage.Fleet, raid.StageFor(ContactFaction.Merchant, ReputationTier.Hostile), "세력마다 따로");
        }

        [Test]
        public void Flagship_Defeated_PausesFaction_ResetsHeld()
        {
            var sim = Sim(StationRule.Contact | StationRule.FleetRaid);
            var raid = sim.RaidFleet;
            RaidOutcome outcome = RaidOutcome.None;
            raid.Ended += (s, o) => outcome = o;
            raid.Start(RaidStage.Flagship, ContactFaction.Outlaw, "무법 선단");
            Assert.IsNotNull(raid.Flagship);
            Assert.AreEqual(40f, raid.Flagship.MaxHp, 1e-3f, "작은 정거장 = 크기 배율 1");
            Assert.IsTrue(sim.MeteorShower.IsRaid, "기함 포격");
            Assert.IsFalse(raid.DamageFlagship(10f));
            Assert.AreEqual(30f, raid.Flagship.Hp, 1e-3f);
            Assert.IsTrue(raid.DamageFlagship(30f), "체력 0 = 꺾임");
            Assert.AreEqual(RaidOutcome.Defeated, outcome);
            Assert.IsFalse(sim.MeteorShower.Active, "남은 포격도 멈춤");
            Assert.Greater(raid.PausedFor(ContactFaction.Outlaw), 0f, "그 세력 습격이 한동안 멈춤");
            Assert.AreEqual(0, raid.FleetsHeld(ContactFaction.Outlaw));
        }

        [Test]
        public void Flagship_NotDefeatedInTime_RetreatsWithBigVolley()
        {
            _fleet.EditorSetFlagship(100f, 20f, 1, 1, 5f);
            var sim = Sim(StationRule.Contact | StationRule.FleetRaid);
            var raid = sim.RaidFleet;
            RaidOutcome outcome = RaidOutcome.None;
            raid.Ended += (s, o) => outcome = o;
            raid.Start(RaidStage.Flagship, ContactFaction.Merchant, "상인 연합");
            var dir = raid.Direction;
            for (int i = 0; i < 21; i++)
                sim.Tick(1f);
            Assert.AreEqual(RaidOutcome.Retreated, outcome);
            Assert.IsTrue(sim.MeteorShower.IsRaid);
            Assert.AreEqual("기함 마지막 포격", sim.MeteorShower.Label);
            Assert.AreEqual(dir, sim.MeteorShower.Direction, "같은 쪽에서");
        }

        [Test]
        public void TurretIntercepts_CounterDamageShips()
        {
            var sim = Sim(StationRule.Contact | StationRule.FleetRaid);
            // 코어 둘레 사방 · 위아래에 블록, 포탑이 모두 지킴
            Assert.IsTrue(sim.Grid.TryPlace(_turret, new Vector3Int(0, 0, 1), 0, out _));
            foreach (var d in new[] { Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, -1), Vector3Int.up, Vector3Int.down })
                Assert.IsTrue(sim.Grid.TryPlace(_block, d, 0, out _));
            var raid = sim.RaidFleet;
            raid.Start(RaidStage.Scout, ContactFaction.Outlaw, "무법 선단");
            float before = 0f;
            foreach (var s in raid.Ships)
                before += s.Hp;
            int damaged = 0;
            sim.Damage.Damaged += _ => damaged++;
            for (int i = 0; i < 40 && raid.Active; i++)
                sim.Tick(1f);
            Assert.AreEqual(0, damaged, "격추 확률 1 → 피해 없음");
            Assert.IsFalse(raid.Active, "포격이 끝나면 정찰선이 물러가거나 꺾임");
        }

        [Test]
        public void SizeScale_FromExteriorModules_Capped()
        {
            Assert.AreEqual(1f, _fleet.SizeScale(10), 1e-4f);
            Assert.AreEqual(1.5f, _fleet.SizeScale(80), 1e-4f, "기준 40을 40 넘으면 +50%");
            Assert.AreEqual(2f, _fleet.SizeScale(1000), 1e-4f, "상한 ×2");
        }

        [Test]
        public void SaveRestore_KeepsFlagshipAndHeld()
        {
            var sim = Sim(StationRule.Contact | StationRule.FleetRaid);
            var raid = sim.RaidFleet;
            raid.Start(RaidStage.Flagship, ContactFaction.Scholar, "학술 함대");
            raid.DamageFlagship(5f);
            sim.Tick(3f);
            string state = raid.Capture(_ => -1);

            var loaded = Sim(StationRule.Contact | StationRule.FleetRaid);
            loaded.RaidFleet.Restore(state, _ => null);
            Assert.AreEqual(RaidStage.Flagship, loaded.RaidFleet.Stage);
            Assert.AreEqual(ContactFaction.Scholar, loaded.RaidFleet.Faction);
            Assert.AreEqual(35f, loaded.RaidFleet.Flagship.Hp, 1e-3f);
            Assert.AreEqual(raid.TimeLeft, loaded.RaidFleet.TimeLeft, 1e-3f);
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim(StationRule open)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                HazardRandom01 = () => 0.5f,
                Contact = _contact,
            });
            sim.SetRules(new Rules { Open = open });
            return sim;
        }

        private ModuleData Module(string name, bool removable)
        {
            var m = Create<ModuleData>();
            m.name = name;
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
