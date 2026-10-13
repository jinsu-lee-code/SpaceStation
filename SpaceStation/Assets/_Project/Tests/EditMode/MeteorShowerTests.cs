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
    /// 12-3c 도시급 유성우 방향 예보: 오는 방향에서 보이는 앞면만 맞음(가리면 안전), 예보 → 여러 파 → 다시 대기, 등급 전에는 멈춤, 세이브.
    /// </summary>
    public class MeteorShowerTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _a;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Metal, 500));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 1000), R(ResourceType.Metal, 1000));
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 1000f;
            b.FindProperty("_showerMinGrade").intValue = 0;
            b.FindProperty("_showerIntervalMin").floatValue = 10f;
            b.FindProperty("_showerIntervalMax").floatValue = 10f;
            b.FindProperty("_showerWarningSeconds").floatValue = 5f;
            b.FindProperty("_showerWaves").intValue = 3;
            b.FindProperty("_showerWaveInterval").floatValue = 2f;
            b.FindProperty("_showerHitsMultiplier").floatValue = 3f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 0f, removable: false);
            _a = Module("MD_TestA", 10f);
            _grades = Grades(1);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void FindFacing_OnlyCellsWithClearLineToDirection()
        {
            var sim = Sim(() => 0f);
            var a1 = Place(sim, new Vector3Int(1, 0, 0));
            var a2 = Place(sim, new Vector3Int(2, 0, 0));
            var a3 = Place(sim, new Vector3Int(3, 0, 0)); // 코어 - a1 - a2 - a3 (x축 한 줄)
            var modules = new List<ModuleInstance>();
            var counts = new Dictionary<ModuleInstance, int>();
            var cells = new List<Vector3Int>();

            MeteorShowerSystem.FindFacing(sim.Grid, sim.Core, Vector3Int.right, modules, counts, cells);
            CollectionAssert.AreEquivalent(new[] { a3 }, modules, "+X에서는 맨 끝만 보임 (앞이 가리면 뒤는 안전)");

            MeteorShowerSystem.FindFacing(sim.Grid, sim.Core, Vector3Int.left, modules, counts, cells);
            Assert.IsEmpty(modules, "−X에서는 코어가 모두 가림 (코어는 대상 아님)");

            MeteorShowerSystem.FindFacing(sim.Grid, sim.Core, new Vector3Int(0, 0, 1), modules, counts, cells);
            CollectionAssert.AreEquivalent(new[] { a1, a2, a3 }, modules, "옆(+Z)에서는 모두 보임");
            Assert.AreEqual(3, cells.Count);
        }

        [Test]
        public void Shower_WarnsThenFallsInWaves_OnlyFacingModulesHit()
        {
            var sim = Sim(() => 0f); // 방향 = +X (첫 방향)
            var a1 = Place(sim, new Vector3Int(1, 0, 0));
            var a2 = Place(sim, new Vector3Int(2, 0, 0));
            var a3 = Place(sim, new Vector3Int(3, 0, 0));
            var shower = sim.MeteorShower;

            sim.Tick(1f); // 대기 시계 시작 (간격 10초)
            Assert.AreEqual(MeteorShowerSystem.ShowerPhase.Idle, shower.Phase);
            sim.Tick(10f);
            Assert.AreEqual(MeteorShowerSystem.ShowerPhase.Warning, shower.Phase, "간격이 지나면 예보");
            Assert.AreEqual(Vector3Int.right, shower.Direction);
            Assert.AreEqual(1, shower.HitsPerWave, "등급 운석 1 × 3배 ÷ 3파");
            CollectionAssert.AreEquivalent(new[] { a3 }, shower.FacingModules);
            Assert.IsFalse(sim.Damage.IsDamaged(a3), "예보 중에는 피해 없음");

            sim.Tick(5f);
            Assert.AreEqual(MeteorShowerSystem.ShowerPhase.Falling, shower.Phase);
            Assert.AreEqual(1, shower.WavesDone);
            Assert.IsTrue(sim.Damage.IsDamaged(a3), "첫 파 = 보이는 앞면");
            sim.Tick(2f);
            sim.Tick(2f);
            Assert.AreEqual(MeteorShowerSystem.ShowerPhase.Idle, shower.Phase, "3파 뒤 다시 대기");
            Assert.IsFalse(sim.Damage.IsDamaged(a1), "가려진 모듈은 안전");
            Assert.IsFalse(sim.Damage.IsDamaged(a2), "가려진 모듈은 안전");
        }

        [Test]
        public void Shower_WaitsUntilMinGrade()
        {
            var b = new SerializedObject(_config);
            b.FindProperty("_showerMinGrade").intValue = 1;
            b.ApplyModifiedPropertiesWithoutUndo();
            _grades = Grades(2, secondGradeModules: 3);
            var sim = Sim(() => 0f);
            Place(sim, new Vector3Int(1, 0, 0));
            sim.Tick(1f);
            sim.Tick(100f);
            Assert.AreEqual(MeteorShowerSystem.ShowerPhase.Idle, sim.MeteorShower.Phase, "등급 전에는 시계가 멈춤");
            Place(sim, new Vector3Int(2, 0, 0));
            Place(sim, new Vector3Int(3, 0, 0));
            Assert.AreEqual(1, sim.Progression.GradeIndex);
            sim.Tick(1f);
            sim.Tick(10f);
            Assert.AreEqual(MeteorShowerSystem.ShowerPhase.Warning, sim.MeteorShower.Phase, "등급 도달 뒤 간격이 지나면 예보");
        }

        [Test]
        public void Shower_SaveRestore_KeepsWarning()
        {
            var sim = Sim(() => 0.4f);
            Place(sim, new Vector3Int(1, 0, 0));
            sim.Tick(1f);
            sim.MeteorShower.BeginWarning(30f);
            sim.Tick(4f);
            string state = sim.MeteorShower.Capture(_ => -1);

            var loaded = Sim(() => 0.4f);
            loaded.MeteorShower.Restore(state, _ => null);
            Assert.AreEqual(MeteorShowerSystem.ShowerPhase.Warning, loaded.MeteorShower.Phase);
            Assert.AreEqual(sim.MeteorShower.Direction, loaded.MeteorShower.Direction);
            Assert.AreEqual(26f, loaded.MeteorShower.TimeLeft, 1e-3f);
            Assert.AreEqual(sim.MeteorShower.HitsPerWave, loaded.MeteorShower.HitsPerWave);
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim(System.Func<float> hazard) => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
            HazardRandom01 = hazard,
        });

        private ModuleInstance Place(StationSimulation sim, Vector3Int origin)
        {
            Assert.IsTrue(sim.TryPlace(_a, origin, 0, out var m), $"A @ {origin}");
            return m;
        }

        private StationGradeConfig Grades(int count, int secondGradeModules = 0)
        {
            var grades = Create<StationGradeConfig>();
            var g = new SerializedObject(grades);
            var list = g.FindProperty("_grades");
            list.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                var grade = list.GetArrayElementAtIndex(i);
                grade.FindPropertyRelative("_minPopulation").intValue = 0;
                grade.FindPropertyRelative("_minModules").intValue = i == 0 ? 0 : secondGradeModules;
                grade.FindPropertyRelative("_meteorHitsMin").intValue = 1;
                grade.FindPropertyRelative("_meteorHitsMax").intValue = 1;
                var unlocks = grade.FindPropertyRelative("_unlocks");
                unlocks.arraySize = 1;
                unlocks.GetArrayElementAtIndex(0).objectReferenceValue = _a;
            }
            g.ApplyModifiedPropertiesWithoutUndo();
            return grades;
        }

        private ModuleData Module(string name, float metal, bool removable = true)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
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
