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
    /// <summary>12-1 등급 틀: ★ · 결과 화면 등급 첫 도달 (한 번씩, 차례로), 구간 번호, 구간 기록, 세이브 · 이전 세이브 호환.</summary>
    public class GradeMilestoneTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _block;

        [SetUp]
        public void SetUp()
        {
            _nextX = 1; // 복원한 판에 이어서 놓을 때도 겹치지 않게 판마다 되돌리지 않음
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Metal, 1000));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Metal, 2000));
            b.FindProperty("_eventGracePeriod").floatValue = 1000f;
            b.FindProperty("_eventIntervalMin").floatValue = 1000f;
            b.FindProperty("_eventIntervalMax").floatValue = 1000f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 0f, removable: false);
            _block = Module("MD_TestBlock", 10f);

            // 초소형 · 결과(모듈 3) · ★(4) · ★(5) · 결과(6) · 최종 결과(7) — 인구 조건 없음, 승리 등급 = 1
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 6;
            Grade(list.GetArrayElementAtIndex(0), "G0", 0, GradeMilestone.None, _block);
            Grade(list.GetArrayElementAtIndex(1), "G1", 3, GradeMilestone.Result);
            Grade(list.GetArrayElementAtIndex(2), "G2", 4, GradeMilestone.Star);
            Grade(list.GetArrayElementAtIndex(3), "G3", 5, GradeMilestone.Star);
            Grade(list.GetArrayElementAtIndex(4), "G4", 6, GradeMilestone.Result);
            Grade(list.GetArrayElementAtIndex(5), "G5", 7, GradeMilestone.Result);
            g.FindProperty("_victoryGradeIndex").intValue = 1;
            g.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Milestones_FireOncePerGrade_InOrder_EvenWhenSkipping()
        {
            var p = new StationProgression(_grades);
            var fired = new List<(int, GradeMilestone)>();
            p.MilestoneReached += (i, m) => fired.Add((i, m));

            p.Evaluate(0, 3);
            p.Evaluate(0, 2); // 하락 후 다시 올라도 다시 없음
            p.Evaluate(0, 3);
            CollectionAssert.AreEqual(new[] { (1, GradeMilestone.Result) }, fired);

            p.Evaluate(0, 6); // 두 ★를 건너뛰어도 낮은 등급부터 차례로
            CollectionAssert.AreEqual(new[]
            {
                (1, GradeMilestone.Result), (2, GradeMilestone.Star), (3, GradeMilestone.Star), (4, GradeMilestone.Result),
            }, fired);
            Assert.AreEqual(4, p.HighestGradeIndex);
            p.Evaluate(0, 0);
            Assert.AreEqual(4, p.HighestGradeIndex, "등급이 내려가도 최고 기록은 유지");

            Assert.AreEqual(1, p.ResultNumber(1));
            Assert.AreEqual(2, p.ResultNumber(4));
            Assert.AreEqual(3, p.ResultNumber(5));
            Assert.AreEqual(0, p.ResultNumber(2), "★ 등급은 구간 번호 없음");
            Assert.IsFalse(p.IsFinalResult(4));
            Assert.IsTrue(p.IsFinalResult(5));
        }

        [Test]
        public void ResultGrade_ClosesSegment_WithSegmentAndKeepsTotals()
        {
            var sim = Sim();
            Place(sim, 2); // 코어 + 2 = 3 → G1 결과
            var first = sim.Session.LastSegment;
            Assert.IsNotNull(first);
            Assert.AreEqual(3, first.ModulesAdded, "처음 구간은 0부터 (코어 포함)");
            Assert.AreEqual(0f, first.Seconds, 0.001f);

            for (int i = 0; i < 5; i++)
                sim.Tick(1f);
            Place(sim, 1); // 4 → ★ (구간은 그대로)
            Assert.AreSame(first, sim.Session.LastSegment);
            for (int i = 0; i < 3; i++)
                sim.Tick(1f);
            Place(sim, 2); // 6 → ★ · G4 결과
            var second = sim.Session.LastSegment;
            Assert.AreNotSame(first, second);
            Assert.AreEqual(8f, second.Seconds, 0.001f);
            Assert.AreEqual(3, second.ModulesAdded);
        }

        [Test]
        public void Save_KeepsHighestAndSegment_RestoreDoesNotReopenMilestones()
        {
            var sim = Sim();
            Place(sim, 2);
            for (int i = 0; i < 4; i++)
                sim.Tick(1f);
            Place(sim, 1);
            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));

            var copy = Sim();
            Assert.AreEqual(0, StationStateSerializer.Restore(copy, JsonUtility.FromJson<StationState>(json)));
            Assert.AreEqual(2, copy.Progression.HighestGradeIndex);
            Assert.AreEqual(JsonUtility.ToJson(sim.Session.CaptureSegment()), JsonUtility.ToJson(copy.Session.CaptureSegment()),
                "복원 중 등급이 오르며 생긴 구간 마감은 지워지고 저장된 시작 값으로");

            int fired = 0;
            copy.Progression.MilestoneReached += (i, m) => fired++;
            copy.Tick(1f);
            Assert.AreEqual(0, fired, "이미 도달한 등급은 다시 알리지 않음");
            Place(copy, 1); // 5 → G3 ★ (처음)
            Assert.AreEqual(1, fired);
        }

        [Test]
        public void LegacySave_WithoutHighestGrade_EstimatesFromOldFlags()
        {
            Assert.AreEqual(-1, JsonUtility.FromJson<StationState>("{\"Elapsed\":3}").HighestGrade, "이전 세이브에는 값이 없음");

            // 12-1 전 '초대형 달성' = 승리 등급 바로 위 → 그 등급까지 알림 없이 도달한 것으로, 마지막 등급 도달은 아님
            var old = StationStateSerializer.Capture(Sim());
            old.HighestGrade = -1;
            old.ReachedFinalGrade = true;
            old.ReachedTopGrade = true;
            var sim = Sim();
            StationStateSerializer.Restore(sim, old);
            Assert.AreEqual(2, sim.Progression.HighestGradeIndex);
            Assert.IsTrue(sim.Progression.HasReachedVictory);
            Assert.IsFalse(sim.Progression.HasReachedTopGrade);
        }

        // ---------------- 도우미 ----------------

        private int _nextX = 1;

        private void Place(StationSimulation sim, int count)
        {
            for (int i = 0; i < count; i++)
                Assert.IsTrue(sim.TryPlace(_block, new Vector3Int(_nextX++, 0, 0), 0, out _));
        }

        private StationSimulation Sim()
        {
            return new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
            });
        }

        private static void Grade(SerializedProperty grade, string name, int modules, GradeMilestone milestone, params ModuleData[] unlocks)
        {
            grade.FindPropertyRelative("_displayName").stringValue = name;
            grade.FindPropertyRelative("_minModules").intValue = modules;
            grade.FindPropertyRelative("_milestone").enumValueIndex = (int)milestone;
            var list = grade.FindPropertyRelative("_unlocks");
            list.arraySize = unlocks.Length;
            for (int i = 0; i < unlocks.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = unlocks[i];
        }

        private ModuleData Module(string name, float metal, bool removable = true)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            Fill(so.FindProperty("_buildCost"), R(ResourceType.Metal, metal));
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
