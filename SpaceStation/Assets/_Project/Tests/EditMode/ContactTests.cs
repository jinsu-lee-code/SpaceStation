using System;
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
    /// 12-3c 지구급 방문 세력 + 평판: 등급 전 없음 · 통신 세기별 해독 · 세력별 교역 · 경쟁 세력 평판 · 직전 선택 기억 · 무법 경계 습격(침입 드론) · 세이브.
    /// </summary>
    public class ContactTests
    {
        private readonly List<Object> _created = new List<Object>();
        private readonly Queue<float> _rolls = new Queue<float>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ContactConfig _contact;
        private ModuleData _core, _relay;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 10;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 500), R(ResourceType.Metal, 800), R(ResourceType.Food, 100), R(ResourceType.Water, 500));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 1000), R(ResourceType.Metal, 1000), R(ResourceType.Food, 1000), R(ResourceType.Water, 1000));
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", false, 0);
            var cso = new SerializedObject(_core);
            cso.FindProperty("_housingCapacity").intValue = 100;
            cso.ApplyModifiedPropertiesWithoutUndo();
            _relay = Module("MD_TestRelay", true, 1);

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minPopulation").intValue = 0;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minModules").intValue = 0;
            g.ApplyModifiedPropertiesWithoutUndo();

            _contact = Create<ContactConfig>();
            var c = new SerializedObject(_contact);
            c.FindProperty("_minGrade").intValue = 0;
            c.FindProperty("_visitsPerRank").intValue = 1;
            c.ApplyModifiedPropertiesWithoutUndo();
            _rolls.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void NoConfig_NoSystem_And_LockedBeforeGrade()
        {
            Assert.IsNull(Sim(null).Contact);
            var c = new SerializedObject(_contact);
            c.FindProperty("_minGrade").intValue = 99;
            c.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim(_contact);
            sim.Tick(1000f);
            Assert.AreEqual(ContactSystem.ContactPhase.Idle, sim.Contact.Phase);
            Assert.AreEqual(0, sim.Contact.Visits, "지구급 전에는 방문 없음");
        }

        [Test]
        public void Decode_NeedsRelayPower_AndExtraPowerIsFaster()
        {
            var sim = Sim(_contact);
            var contact = sim.Contact;
            _rolls.Enqueue(0.1f); // 상인
            _rolls.Enqueue(0.1f); // 계급 (첫 방문 = 말단)
            contact.BeginVisit();
            Assert.AreEqual(0, contact.Rank);
            Assert.IsFalse(contact.CanDecode, "중계소 없음 → 해독 불가");
            sim.Tick(10f);
            Assert.AreEqual(0f, contact.DecodeProgress);
            Assert.AreEqual("정체 불명", contact.VisitorName);

            Place(new Vector3Int(1, 0, 0));
            sim.Tick(15f);
            Assert.AreEqual(0.5f, contact.DecodeProgress, 0.02f, "세기 1 = 기준 → 30초");
            Place(new Vector3Int(-1, 0, 0));
            sim.Tick(8f);
            Assert.AreEqual(0.9f, contact.DecodeProgress, 0.02f, "기준을 넘는 세기 1마다 1.5배 빨라짐");
            sim.Tick(3f);
            Assert.IsTrue(contact.Decoded);
            StringAssert.Contains("상인 연합", contact.VisitorName);
        }

        [Test]
        public void HigherRank_NeedsMoreRelays()
        {
            var sim = Sim(_contact);
            var contact = sim.Contact;
            Place(new Vector3Int(1, 0, 0));
            // 두 번 경계로 보내면 방문 2회 → 최고 계급 2 (간부, 세기 4 필요)
            for (int i = 0; i < 2; i++)
            {
                _rolls.Enqueue(0.1f);
                _rolls.Enqueue(0.1f);
                contact.BeginVisit();
                Assert.IsTrue(contact.Choose(ContactChoice.Guard));
            }
            _rolls.Enqueue(0.1f);
            _rolls.Enqueue(0.1f);
            contact.BeginVisit();
            Assert.AreEqual(2, contact.Rank);
            Assert.IsFalse(contact.CanDecode, "세기 1로는 간부(4) 해독 불가");
            Place(new Vector3Int(-1, 0, 0));
            Place(new Vector3Int(0, 0, 1));
            Place(new Vector3Int(0, 0, -1));
            Assert.IsTrue(contact.CanDecode, "중계소 4개 = 세기 4");
            Assert.Greater(contact.Scale, 1.5f, "계급 · 방문 횟수만큼 규모가 커짐");
        }

        [Test]
        public void MerchantTrade_ExchangesForScarcestGood_RivalLosesRep()
        {
            var sim = Sim(_contact);
            var contact = sim.Contact;
            Place(new Vector3Int(1, 0, 0));
            _rolls.Enqueue(0.1f);
            _rolls.Enqueue(0.1f);
            contact.BeginVisit();
            sim.Tick(31f); // 해독
            Assert.IsTrue(contact.Decoded);
            Assert.AreEqual(ResourceType.Food, contact.MerchantGood, "가장 모자란 생활 자원");
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            float food = sim.Resources.GetStock(ResourceType.Food);
            Assert.IsTrue(contact.Choose(ContactChoice.Trade));
            Assert.AreEqual(metal - 120f, sim.Resources.GetStock(ResourceType.Metal), 0.5f);
            Assert.AreEqual(food + 90f, sim.Resources.GetStock(ResourceType.Food), 1f);
            Assert.AreEqual(5f, contact.Reputation(ContactFaction.Merchant), 1e-3f);
            Assert.AreEqual(-2f, contact.Reputation(ContactFaction.Outlaw), 1e-3f, "경쟁 세력 = 변화 × 0.4만큼 멀어짐");
            Assert.AreEqual(0f, contact.Reputation(ContactFaction.Scholar), 1e-3f);
            Assert.AreEqual(ContactSystem.ContactPhase.Idle, contact.Phase);
            Assert.AreEqual(ContactChoice.Trade, contact.LastChoice(ContactFaction.Merchant));
        }

        [Test]
        public void LastChoice_GuardMakesNextOfferWorse()
        {
            var sim = Sim(_contact);
            var contact = sim.Contact;
            _rolls.Enqueue(0.1f);
            _rolls.Enqueue(0.6f);
            contact.BeginVisit();
            Assert.AreEqual(1f, contact.OfferMultiplier, 1e-4f);
            contact.Choose(ContactChoice.Guard);
            Assert.AreEqual(-5f, contact.Reputation(ContactFaction.Merchant), 1e-3f);
            _rolls.Enqueue(0.1f);
            _rolls.Enqueue(0.6f);
            contact.BeginVisit();
            Assert.AreEqual(ContactFaction.Merchant, contact.Faction);
            Assert.AreEqual(0.8f, contact.OfferMultiplier, 1e-4f, "직전에 경계 → 제안 ×0.8");
            contact.Choose(ContactChoice.Negotiate);
            Assert.AreEqual(-5f + 15f * 0.5f, contact.Reputation(ContactFaction.Merchant), 1e-3f, "직전 경계 → 평판 오름 ×0.5");
        }

        [Test]
        public void OutlawGuard_CanRaid_BoardingDronesDisabled()
        {
            var sim = Sim(_contact);
            var contact = sim.Contact;
            Place(new Vector3Int(1, 0, 0));
            _rolls.Enqueue(0.9f); // 무법 선단
            _rolls.Enqueue(0.1f);
            contact.BeginVisit();
            Assert.AreEqual(ContactFaction.Outlaw, contact.Faction);
            _rolls.Enqueue(0.1f); // 습격 판정 (< 50%)
            _rolls.Enqueue(0.9f); // 침입 드론
            _rolls.Enqueue(0.0f); // 붙을 모듈
            contact.Choose(ContactChoice.Guard);
            Assert.IsTrue(contact.Boarding);
            Assert.AreEqual(1, contact.DronesTotal, "말단 2기 × 경계 태세 0.6 → 1기");
            contact.DisableDrone();
            Assert.IsFalse(contact.Boarding, "모두 끄면 끝");
        }

        [Test]
        public void Boarding_Timeout_DamagesModule_AndCostsMetal()
        {
            var sim = Sim(_contact);
            var contact = sim.Contact;
            var relay = Place(new Vector3Int(1, 0, 0));
            _rolls.Enqueue(0.0f); // 붙을 모듈
            contact.ForceRaid(ContactSystem.RaidKind.Boarding);
            Assert.AreEqual(relay, contact.BoardingTarget);
            Assert.AreEqual(2, contact.DronesLeft);
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            sim.Tick(_contact.BoardingSeconds + 1f);
            Assert.IsFalse(contact.Boarding);
            Assert.IsTrue(sim.Damage.IsDamaged(relay), "못 막으면 그 모듈 파손");
            Assert.Less(sim.Resources.GetStock(ResourceType.Metal), metal - 100f, "금속 손실");
        }

        [Test]
        public void SaveRestore_KeepsReputationMemoryAndBoarding()
        {
            var sim = Sim(_contact);
            var contact = sim.Contact;
            var relay = Place(new Vector3Int(1, 0, 0));
            _rolls.Enqueue(0.1f);
            _rolls.Enqueue(0.1f);
            contact.BeginVisit();
            contact.Choose(ContactChoice.Negotiate);
            _rolls.Enqueue(0.0f);
            contact.ForceRaid(ContactSystem.RaidKind.Boarding);
            string state = contact.Capture(m => m == relay ? 7 : -1);

            var loaded = Sim(_contact);
            var relay2 = PlaceIn(loaded, new Vector3Int(1, 0, 0));
            loaded.Contact.Restore(state, i => i == 7 ? relay2 : null);
            Assert.AreEqual(contact.Reputation(ContactFaction.Merchant), loaded.Contact.Reputation(ContactFaction.Merchant), 1e-3f);
            Assert.AreEqual(ContactChoice.Negotiate, loaded.Contact.LastChoice(ContactFaction.Merchant));
            Assert.AreEqual(1, loaded.Contact.Visits);
            Assert.IsTrue(loaded.Contact.Boarding);
            Assert.AreEqual(relay2, loaded.Contact.BoardingTarget);
        }

        // ---------------- 도우미 ----------------

        private StationSimulation _last;

        private StationSimulation Sim(ContactConfig contact)
        {
            _last = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                HazardRandom01 = () => _rolls.Count > 0 ? _rolls.Dequeue() : 0.99f,
                Contact = contact,
            });
            return _last;
        }

        private ModuleInstance Place(Vector3Int at) => PlaceIn(_last, at);

        private ModuleInstance PlaceIn(StationSimulation sim, Vector3Int at)
        {
            Assert.IsTrue(sim.Grid.TryPlace(_relay, at, 0, out var m));
            return m;
        }

        private ModuleData Module(string name, bool removable, int relay)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            so.FindProperty("_relayPower").intValue = relay;
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
