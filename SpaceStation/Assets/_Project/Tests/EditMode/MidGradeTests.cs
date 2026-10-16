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
    /// <summary>13-7 타이쿤판 등급 중간 단계: 등급 + 인구 · 모듈 조건 → 다음 등급 모듈 먼저 엶 · 연구 포인트 한 번 · 다음 등급으로 넘어가도 열림 · 세이브.</summary>
    public class MidGradeTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private MidGradeConfig _mid;
        private ModuleData _core, _box, _early;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            _core = Module(so =>
            {
                so.FindProperty("_removable").boolValue = false;
                so.FindProperty("_housingCapacity").intValue = 50;
            });
            _box = Module(_ => { });
            _early = Module(_ => { });
            // 등급 0 (box) · 1 (인구 10) · 2 (인구 100, early 해금)
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 3;
            int[] pops = { 0, 10, 100 };
            for (int i = 0; i < 3; i++)
            {
                list.GetArrayElementAtIndex(i).FindPropertyRelative("_minPopulation").intValue = pops[i];
                list.GetArrayElementAtIndex(i).FindPropertyRelative("_minModules").intValue = 0;
                list.GetArrayElementAtIndex(i).FindPropertyRelative("_unlocks").arraySize = 0;
            }
            var u0 = list.GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks");
            u0.arraySize = 1;
            u0.GetArrayElementAtIndex(0).objectReferenceValue = _box;
            var u2 = list.GetArrayElementAtIndex(2).FindPropertyRelative("_unlocks");
            u2.arraySize = 1;
            u2.GetArrayElementAtIndex(0).objectReferenceValue = _early;
            g.ApplyModifiedPropertiesWithoutUndo();

            _mid = Create<MidGradeConfig>();
            _mid.Set(new List<MidGradeStep>
            {
                new MidGradeStep { DisplayName = "확장", Grade = 1, MinPopulation = 20, MinModules = 0, Modules = new List<ModuleData> { _early }, ResearchPoints = 1 },
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
        public void Step_OpensNextGradeModule_OnceWithPoint_AndSaves()
        {
            var (sim, mid) = Sim();
            sim.Progression.Evaluate(10, 0);
            sim.Resources.SetPopulation(15);
            mid.Tick(1f);
            Assert.IsFalse(sim.Progression.IsUnlocked(_early), "인구 조건 전");
            sim.Resources.SetPopulation(20);
            mid.Tick(1f);
            Assert.IsTrue(mid.IsReached(0));
            Assert.IsTrue(sim.Progression.IsUnlocked(_early), "중간 단계에서 먼저 열림");
            Assert.AreEqual(PlacementResult.Valid, sim.Progression.CheckBuildable(_early, sim.Grid));
            Assert.AreEqual(1, sim.Supply.ResearchPoints);
            mid.Tick(1f);
            Assert.AreEqual(1, sim.Supply.ResearchPoints, "한 번만");

            sim.Progression.Evaluate(0, 0);
            Assert.IsTrue(sim.Progression.IsUnlocked(_early), "등급이 내려가도 유지");

            var state = mid.Capture(_ => 0);
            var (sim2, mid2) = Sim();
            mid2.Restore(state, _ => null);
            Assert.IsTrue(sim2.Progression.IsUnlocked(_early));
            Assert.AreEqual(0, sim2.Supply.ResearchPoints, "복원은 보상 없이");
        }

        [Test]
        public void SkippingPastGrade_StillOpens()
        {
            var (sim, mid) = Sim();
            sim.Progression.Evaluate(100, 0); // 조건 없이 등급 2로
            sim.Resources.SetPopulation(5);
            mid.Tick(1f);
            Assert.IsTrue(mid.IsReached(0));
        }

        private (StationSimulation, MidGradeSystem) Sim()
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
            return (sim, MidGradeSystem.AttachTo(sim, _mid));
        }

        private ModuleData Module(System.Action<SerializedObject> setup)
        {
            var m = Create<ModuleData>();
            var so = new SerializedObject(m);
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            setup(so);
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
