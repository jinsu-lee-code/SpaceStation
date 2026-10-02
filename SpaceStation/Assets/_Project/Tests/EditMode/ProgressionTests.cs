using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>GDD 12-3 등급 표 / BALANCE 11번 규칙.</summary>
    public class ProgressionTests
    {
        private readonly List<Object> _created = new List<Object>();
        private StationGradeConfig _config;
        private ModuleData _basic, _storage, _dock, _orphan;
        private StationProgression _progression;
        private StationGrid _grid;

        [SetUp]
        public void SetUp()
        {
            _basic = Module("Basic");
            _storage = Module("Storage");
            _dock = Module("Dock");
            _orphan = Module("Orphan");

            _config = ScriptableObject.CreateInstance<StationGradeConfig>();
            _created.Add(_config);
            var so = new SerializedObject(_config);
            var grades = so.FindProperty("_grades");
            grades.arraySize = 4;
            SetGrade(grades.GetArrayElementAtIndex(0), "초소형", 0, 0, 1, _basic, _dock);
            SetGrade(grades.GetArrayElementAtIndex(1), "소형", 10, 8, 2, _storage);
            SetGrade(grades.GetArrayElementAtIndex(2), "중형", 30, 20, 4);
            SetGrade(grades.GetArrayElementAtIndex(3), "대형", 60, 40, 6);
            so.FindProperty("_limitedModule").objectReferenceValue = _dock;
            so.ApplyModifiedPropertiesWithoutUndo();

            _progression = new StationProgression(_config);
            _grid = new StationGrid();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Grade_RequiresBothPopulationAndModules()
        {
            Assert.AreEqual(0, _progression.ComputeGrade(4, 1));
            Assert.AreEqual(0, _progression.ComputeGrade(10, 7), "모듈 부족");
            Assert.AreEqual(0, _progression.ComputeGrade(9, 8), "인구 부족");
            Assert.AreEqual(1, _progression.ComputeGrade(10, 8));
            Assert.AreEqual(2, _progression.ComputeGrade(30, 20));
            Assert.AreEqual(3, _progression.ComputeGrade(60, 40));
            Assert.AreEqual(1, _progression.ComputeGrade(100, 10), "모듈 수가 소형까지만 만족");
        }

        [Test]
        public void Grade_IsRealTime_AndCanDrop()
        {
            var changes = new List<(int, int)>();
            _progression.GradeChanged += (a, b) => changes.Add((a, b));

            _progression.Evaluate(10, 8);
            Assert.AreEqual(1, _progression.GradeIndex);
            _progression.Evaluate(9, 8);
            Assert.AreEqual(0, _progression.GradeIndex, "조건을 벗어나면 하락");
            CollectionAssert.AreEqual(new[] { (0, 1), (1, 0) }, changes);
        }

        [Test]
        public void FinalGrade_FiresOnlyOnce()
        {
            int fired = 0;
            _progression.FinalGradeReached += () => fired++;
            _progression.Evaluate(60, 40);
            _progression.Evaluate(59, 40);
            _progression.Evaluate(60, 40);
            Assert.AreEqual(1, fired);
            Assert.IsTrue(_progression.HasReachedFinalGrade);
        }

        [Test]
        public void Unlocks_FollowCurrentGrade()
        {
            Assert.IsTrue(_progression.IsUnlocked(_basic));
            Assert.IsFalse(_progression.IsUnlocked(_storage));
            Assert.AreEqual(1, _progression.GetUnlockGrade(_storage));
            Assert.AreEqual(-1, _progression.GetUnlockGrade(_orphan));
            Assert.AreEqual(PlacementResult.ModuleLocked, _progression.CheckBuildable(_storage, _grid));

            _progression.Evaluate(10, 8);
            Assert.AreEqual(PlacementResult.Valid, _progression.CheckBuildable(_storage, _grid));

            _progression.Evaluate(0, 0);
            Assert.AreEqual(PlacementResult.ModuleLocked, _progression.CheckBuildable(_storage, _grid), "하락 시 다시 잠김");
        }

        [Test]
        public void LimitedModule_CappedPerGrade_ExistingKept()
        {
            _grid.TryPlace(_dock, Vector3Int.zero, 0, out _);
            Assert.AreEqual(PlacementResult.LimitReached, _progression.CheckBuildable(_dock, _grid), "초소형 1개");

            _progression.Evaluate(10, 8);
            Assert.AreEqual(PlacementResult.Valid, _progression.CheckBuildable(_dock, _grid), "소형 2개");
            _grid.TryPlace(_dock, new Vector3Int(5, 0, 0), 0, out _);
            Assert.AreEqual(PlacementResult.LimitReached, _progression.CheckBuildable(_dock, _grid));

            _progression.Evaluate(0, 0);
            Assert.AreEqual(2, _progression.CountLimited(_grid), "하락해도 이미 지은 것은 유지");
            Assert.AreEqual(PlacementResult.LimitReached, _progression.CheckBuildable(_dock, _grid));
            Assert.AreEqual(PlacementResult.Valid, _progression.CheckBuildable(_basic, _grid), "제한 대상이 아닌 모듈은 무관");
        }

        [Test]
        public void FinalGrade_LimitGrowsWithModuleCount()
        {
            var so = new SerializedObject(_config);
            so.FindProperty("_extraLimitEveryModules").intValue = 30;
            so.ApplyModifiedPropertiesWithoutUndo();

            _progression.Evaluate(30, 20); // 중형: 확장 없음
            Assert.AreEqual(4, _progression.LimitFor(500), "최고 등급이 아니면 그대로");
            Assert.AreEqual(-1, _progression.ModulesUntilNextExtra(500));

            _progression.Evaluate(60, 40); // 대형 (최소 모듈 40)
            Assert.AreEqual(6, _progression.LimitFor(40));
            Assert.AreEqual(6, _progression.LimitFor(69));
            Assert.AreEqual(7, _progression.LimitFor(70), "40 + 30");
            Assert.AreEqual(28, _progression.LimitFor(700), "6 + 660/30 = 6 + 22");
            Assert.AreEqual(30, _progression.ModulesUntilNextExtra(40));
            Assert.AreEqual(1, _progression.ModulesUntilNextExtra(69));
        }

        [Test]
        public void GameOver_OnlyAfterHavingPopulation_Once()
        {
            var session = new GameSession();
            int overs = 0;
            session.GameOver += () => overs++;

            session.ObservePopulation(0);
            Assert.IsFalse(session.IsGameOver, "처음부터 0이면 게임 오버 아님");
            session.ObservePopulation(4);
            session.ObservePopulation(7);
            session.ObservePopulation(0);
            session.ObservePopulation(0);
            Assert.IsTrue(session.IsGameOver);
            Assert.AreEqual(1, overs);
            Assert.AreEqual(7, session.MaxPopulation);
        }

        // ---- helpers ----

        private ModuleData Module(string name)
        {
            var m = ScriptableObject.CreateInstance<ModuleData>();
            m.name = name;
            _created.Add(m);
            return m;
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
