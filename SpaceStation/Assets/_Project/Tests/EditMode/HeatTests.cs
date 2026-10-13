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
    /// 12-3c 대륙급 열 관리: 반경 거리 감쇠 합산 · 과열 효율 · 방열판(맞닿음 + 바깥 노출) · 위험 60초 → 파손 · 등급 전에는 열 없음.
    /// </summary>
    public class HeatTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _hot, _plain, _radiator;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 100000f;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.FindProperty("_heatMinGrade").intValue = 0;
            b.FindProperty("_heatLimit").floatValue = 6f;
            b.FindProperty("_heatFalloff1").floatValue = 0.5f;
            b.FindProperty("_heatFalloff2").floatValue = 0.25f;
            b.FindProperty("_heatMaxPenalty").floatValue = 0.5f;
            b.FindProperty("_heatDangerRatio").floatValue = 1.5f;
            b.FindProperty("_heatDamageSeconds").floatValue = 60f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 0f, 0f, removable: false);
            _hot = Module("MD_TestHot", 4f, 0f);
            _plain = Module("MD_TestPlain", 0f, 0f);
            _radiator = Module("MD_TestRadiator", 0f, 4f);
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minPopulation").intValue = 0;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minModules").intValue = 0;
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
        public void Heat_FallsOffWithDistance_AndAddsUp()
        {
            var sim = Sim();
            var hot = Place(sim, _hot, 1, 0, 0);
            var near = Place(sim, _plain, 2, 0, 0);
            var far = Place(sim, _plain, 3, 0, 0);
            var beyond = Place(sim, _plain, 4, 0, 0);
            sim.Tick(1f);
            var heat = sim.Heat;
            Assert.AreEqual(4f, heat.HeatOf(hot), 1e-4f, "자기 칸 100%");
            Assert.AreEqual(2f, heat.HeatOf(near), 1e-4f, "1칸 50%");
            Assert.AreEqual(1f, heat.HeatOf(far), 1e-4f, "2칸 25%");
            Assert.AreEqual(0f, heat.HeatOf(beyond), "3칸부터 없음");
            Assert.AreEqual(1f, sim.HeatMultiplier(hot), "한도 이하 = 효율 그대로");
        }

        [Test]
        public void Overheat_ReducesEfficiency()
        {
            var sim = Sim();
            Place(sim, _hot, 1, 0, 0);
            var mid = Place(sim, _hot, 2, 0, 0);
            Place(sim, _hot, 3, 0, 0);
            sim.Tick(1f);
            Assert.AreEqual(8f, sim.Heat.HeatOf(mid), 1e-4f, "4 + 2 + 2");
            Assert.IsTrue(sim.Heat.IsOverheated(mid));
            Assert.AreEqual(1f - 0.5f * (2f / 3f), sim.HeatMultiplier(mid), 1e-4f, "한도 6 ~ 위험 9 사이 비율만큼 최대 50% 감소");
        }

        [Test]
        public void Radiator_RemovesHeat_OnlyWhenExposed()
        {
            var sim = Sim();
            var hot = Place(sim, _hot, 1, 0, 0);
            var rad = Place(sim, _radiator, 2, 0, 0);
            sim.Tick(1f);
            Assert.AreEqual(0f, sim.Heat.HeatOf(hot), 1e-4f, "맞닿은 방열판이 4를 뺌");
            Assert.IsTrue(sim.Heat.RadiatorWorking(rad));

            // 방열판의 남은 면을 모두 막음 → 바깥 노출 없음 → 동작 안 함
            Place(sim, _plain, 3, 0, 0);
            Place(sim, _plain, 2, 0, 1);
            Place(sim, _plain, 2, 0, -1);
            Place(sim, _plain, 2, 1, 0);
            Place(sim, _plain, 2, -1, 0);
            sim.Tick(1f);
            Assert.IsFalse(sim.Heat.RadiatorWorking(rad), "안쪽에 묻힌 방열판");
            Assert.AreEqual(4f, sim.Heat.HeatOf(hot), 1e-4f);
        }

        [Test]
        public void Danger_ForSixtySeconds_Damages()
        {
            var sim = Sim();
            var a = Place(sim, _hot, 1, 0, 0);
            var b = Place(sim, _hot, 2, 0, 0);
            var c = Place(sim, _hot, 1, 0, 1);
            var d = Place(sim, _hot, 2, 0, 1); // 2×2: 각자 4 + 이웃 3 × 2 = 10 ≥ 위험 9
            sim.Tick(1f);
            Assert.IsTrue(sim.Heat.IsDanger(b));
            for (int i = 0; i < 58; i++)
                sim.Tick(1f);
            Assert.IsFalse(sim.Damage.IsDamaged(b), "60초 전에는 버팀");
            sim.Tick(1f);
            sim.Tick(1f);
            int damaged = 0;
            foreach (var m in new[] { a, b, c, d })
                if (sim.Damage.IsDamaged(m))
                    damaged++;
            Assert.Greater(damaged, 0, "위험 60초 → 파손");
        }

        [Test]
        public void BeforeMinGrade_NoHeat()
        {
            var b = new SerializedObject(_config);
            b.FindProperty("_heatMinGrade").intValue = 1;
            b.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim();
            var hot = Place(sim, _hot, 1, 0, 0);
            sim.Tick(1f);
            Assert.IsFalse(sim.Heat.Active);
            Assert.AreEqual(0f, sim.Heat.HeatOf(hot));
            Assert.AreEqual(1f, sim.HeatMultiplier(hot));
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim() => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
            HazardRandom01 = () => 0.5f,
        });

        private static ModuleInstance Place(StationSimulation sim, ModuleData data, int x, int y, int z)
        {
            Assert.IsTrue(sim.Grid.TryPlace(data, new Vector3Int(x, y, z), 0, out var m), $"{data.name} @ {x},{y},{z}");
            return m;
        }

        private ModuleData Module(string name, float heat, float removal, bool removable = true)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            so.FindProperty("_heatOutput").floatValue = heat;
            so.FindProperty("_heatRemoval").floatValue = removal;
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
