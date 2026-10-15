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
    /// 13-1 타이쿤판 해금 선택: 자동 규칙 · 후보 띄우기 · 고르기 · 남은 새 후보 = 다음 후보 · 넘어온 후보 미선택 = 다음 등급 자동 ·
    /// 마지막 등급은 남은 것 모두 · 여러 등급을 한 번에 넘으면 차례로 · 등급이 내려가도 유지 · 세이브 · 규칙이 시스템 · 모듈을 엶.
    /// </summary>
    public class UnlockChoiceTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private UnlockChoiceConfig _choices;
        private ModuleData _core, _radiator;
        private UnlockOption _a, _b, _c, _d, _e;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_heatMinGrade").intValue = 0; // 규칙을 정하는 쪽이 있으면 등급 조건은 무시됨
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Create<ModuleData>();
            _core.name = "MD_TestCore";
            _radiator = Create<ModuleData>();
            _radiator.name = "MD_TestRadiator";
            var r = new SerializedObject(_radiator);
            r.FindProperty("_requiredRule").intValue = (int)StationRule.Heat;
            r.ApplyModifiedPropertiesWithoutUndo();

            // 등급 0~4 (인구 0 · 10 · 20 · 30 · 40), 방열판은 등급 1에서 해금 목록
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 5;
            for (int i = 0; i < 5; i++)
            {
                list.GetArrayElementAtIndex(i).FindPropertyRelative("_minPopulation").intValue = i * 10;
                list.GetArrayElementAtIndex(i).FindPropertyRelative("_minModules").intValue = 0;
                list.GetArrayElementAtIndex(i).FindPropertyRelative("_unlocks").arraySize = 0;
            }
            var unlocks = list.GetArrayElementAtIndex(1).FindPropertyRelative("_unlocks");
            unlocks.arraySize = 1;
            unlocks.GetArrayElementAtIndex(0).objectReferenceValue = _radiator;
            g.ApplyModifiedPropertiesWithoutUndo();

            _a = new UnlockOption("a", "A", "", StationRule.Synthesis);
            _b = new UnlockOption("b", "B", "", StationRule.MeteorShower);
            _c = new UnlockOption("c", "C", "", StationRule.Heat);
            _d = new UnlockOption("d", "D", "", StationRule.SocialCrisis);
            _e = new UnlockOption("e", "E", "", StationRule.Contact);
            _choices = Create<UnlockChoiceConfig>();
            _choices.SetGrades(new List<UnlockGradeEntry>
            {
                new UnlockGradeEntry(1, StationRule.Policies),
                new UnlockGradeEntry(2, StationRule.None, _a, _b),
                new UnlockGradeEntry(3, StationRule.None, _c),
                new UnlockGradeEntry(4, StationRule.None, _d, _e),
            });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void FullPath_CarryThenAuto_LastGradeOpensRest()
        {
            var (sim, u) = Sim();
            Reach(sim, u, 1);
            Assert.IsTrue(u.Allows(StationRule.Policies), "소형 자동 규칙");
            Assert.IsFalse(u.Pending);

            Reach(sim, u, 2);
            Assert.IsTrue(u.Pending);
            Assert.AreEqual(2, u.OfferGrade);
            CollectionAssert.AreEqual(new[] { "a", "b" }, Keys(u));
            Assert.IsFalse(u.Offer[0].Carried);
            Assert.IsTrue(u.Choose(0));
            Assert.IsTrue(u.Allows(StationRule.Synthesis));
            Assert.IsFalse(u.Allows(StationRule.MeteorShower), "남은 새 후보는 아직 닫힘");
            CollectionAssert.AreEqual(new[] { _b }, u.Carry);

            Reach(sim, u, 3);
            CollectionAssert.AreEqual(new[] { "b", "c" }, Keys(u), "넘어온 후보 + 새 후보");
            Assert.IsTrue(u.Offer[0].Carried);
            Assert.IsTrue(u.Choose(1)); // C
            Assert.IsTrue(u.Allows(StationRule.Heat));
            CollectionAssert.AreEqual(new[] { _b }, u.AutoNext, "넘어온 후보를 또 안 고르면 다음 등급 자동");

            Reach(sim, u, 4);
            Assert.IsTrue(u.Allows(StationRule.MeteorShower), "다음 등급 도달 때 자동으로 열림");
            CollectionAssert.AreEqual(new[] { "d", "e" }, Keys(u));
            Assert.IsTrue(u.Choose(1)); // E
            Assert.IsTrue(u.Allows(StationRule.Contact));
            Assert.IsTrue(u.Allows(StationRule.SocialCrisis), "마지막 등급은 남은 것도 모두 엶");
            Assert.IsFalse(u.Pending);
            Assert.AreEqual(4, u.ProcessedGrade);
        }

        [Test]
        public void MultiGradeJump_OffersInOrder()
        {
            var (sim, u) = Sim();
            Reach(sim, u, 3);
            Assert.AreEqual(2, u.OfferGrade, "낮은 등급부터");
            u.Choose(1); // B
            Assert.AreEqual(3, u.OfferGrade, "고르면 바로 다음 등급 후보");
            CollectionAssert.AreEqual(new[] { "a", "c" }, Keys(u));
        }

        [Test]
        public void SingleCandidate_OpensWithoutChoice()
        {
            _choices.SetGrades(new List<UnlockGradeEntry> { new UnlockGradeEntry(1, StationRule.None, _c) });
            var (sim, u) = Sim();
            Reach(sim, u, 1);
            Assert.IsFalse(u.Pending);
            Assert.IsTrue(u.Allows(StationRule.Heat));
        }

        [Test]
        public void Rules_GateSystemsAndModules_KeptWhenGradeDrops()
        {
            var (sim, u) = Sim();
            Reach(sim, u, 1);
            Assert.IsFalse(sim.Heat.Active, "등급 조건(0)이 있어도 열 규칙 전에는 꺼짐");
            Assert.IsFalse(sim.Progression.IsUnlocked(_radiator), "방열판은 열 규칙 전 잠김");
            Reach(sim, u, 2);
            u.Choose(0);
            Reach(sim, u, 3);
            u.Choose(1); // C = 열
            Assert.IsTrue(sim.Heat.Active);
            Assert.IsTrue(sim.Progression.IsUnlocked(_radiator));

            sim.Progression.Evaluate(0, 0);
            u.Advance();
            Assert.IsTrue(sim.Heat.Active, "등급이 내려가도 열린 규칙은 그대로");
        }

        [Test]
        public void SaveRestore_KeepsOfferCarryAndOpen()
        {
            var (sim, u) = Sim();
            Reach(sim, u, 2);
            u.Choose(0);
            Reach(sim, u, 3);
            string state = u.Capture(_ => -1);

            var (copySim, copy) = Sim();
            copySim.Progression.Evaluate(30, 0);
            copy.Restore(state, _ => null);
            Assert.AreEqual(u.Open, copy.Open);
            Assert.AreEqual(3, copy.OfferGrade);
            CollectionAssert.AreEqual(new[] { "b", "c" }, Keys(copy));
            Assert.IsTrue(copy.Offer[0].Carried);
            copy.Advance();
            Assert.AreEqual(3, copy.OfferGrade, "복원 뒤 같은 후보 그대로 대기");
            copy.Choose(0);
            Assert.IsTrue(copy.Allows(StationRule.MeteorShower));
        }

        // ---------------- 도우미 ----------------

        private (StationSimulation, UnlockChoiceSystem) Sim()
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                HazardRandom01 = () => 0.99f,
            });
            return (sim, UnlockChoiceSystem.AttachTo(sim, _choices));
        }

        private static void Reach(StationSimulation sim, UnlockChoiceSystem u, int grade)
        {
            sim.Progression.Evaluate(grade * 10, 0);
            Assert.AreEqual(grade, sim.Progression.GradeIndex);
            u.Advance();
        }

        private static string[] Keys(UnlockChoiceSystem u)
        {
            var keys = new string[u.Offer.Count];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = u.Offer[i].Option.Key;
            return keys;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
