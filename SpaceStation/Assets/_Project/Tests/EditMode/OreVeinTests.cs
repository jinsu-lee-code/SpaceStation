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
    /// CONNECT_1121 1번 타이쿤판 광맥 (BALANCE 50번): 제자리 고갈 · 새 도킹은 평균에서 · 재건축 이어받기 · 광맥 2배 → 다 캐면 바닥 · 새 신호 · 세이브.
    /// </summary>
    public class OreVeinTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _dock;
        private OreVeinConfig _veins;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Water, 150), R(ResourceType.Food, 150), R(ResourceType.Metal, 100));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 100000));
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.ApplyModifiedPropertiesWithoutUndo();
            _core = Module("MD_TestCore", false, 0f);
            _dock = Module("MD_TestDock", true, 1f);
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();

            var explore = Create<ExplorationConfig>();
            explore.EditorSet(null, new List<string>()); // 신호 센서 없음 (엔드리스)
            _veins = Create<OreVeinConfig>();
            _veins.EditorSet(explore, _dock, null);
            var v = new SerializedObject(_veins);
            v.FindProperty("_startGrade").intValue = 0;
            v.FindProperty("_floor").floatValue = 0.4f;
            v.FindProperty("_depletionAmount").floatValue = 100f;
            v.FindProperty("_veinMultiplier").floatValue = 2f;
            v.FindProperty("_reserveBase").floatValue = 50f;
            v.FindProperty("_veinsMin").intValue = 1;
            v.FindProperty("_veinsMax").intValue = 1;
            v.FindProperty("_distanceMin").intValue = 2;
            v.FindProperty("_distanceMax").intValue = 2;
            v.FindProperty("_respawnDelay").floatValue = 30f;
            v.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void InPlace_DepletesToFloor_NewDockAverage_RebuildInherits()
        {
            var sim = NewSim(out var veins);
            Assert.IsTrue(sim.Grid.TryPlace(_dock, Vector3Int.right, 0, out var first));
            Assert.AreEqual(1f, veins.Scale(first), 1e-4f, "처음엔 그대로");
            Tick(sim, 30);
            float mid = veins.Scale(first);
            Assert.Less(mid, 1f, "캔 양이 쌓이면 줄어듦");
            Assert.Greater(mid, 0.4f);
            Tick(sim, 400);
            Assert.AreEqual(0.4f, veins.Scale(first), 1e-4f, "바닥값에서 멈춤");

            Assert.IsTrue(sim.Grid.TryPlace(_dock, Vector3Int.left, 0, out var second));
            Tick(sim, 1);
            Assert.AreEqual(0.4f, veins.Scale(second), 0.01f, "새 도킹 = 지금 제자리 도킹들의 평균 (철거 · 새로 짓기로 되살리지 못함)");

            var origin = first.Origin;
            Assert.IsTrue(sim.Grid.Remove(first));
            Assert.IsTrue(sim.Grid.TryPlace(_dock, origin, 0, out var rebuilt));
            Tick(sim, 1);
            Assert.AreEqual(0.4f, veins.Scale(rebuilt), 0.01f, "같은 자리 재건축 = 이어받음");
        }

        [Test]
        public void Vein_DoublesThenExhaustsToFloor_ThenNewSignal_SaveRestore()
        {
            var sim = NewSim(out var veins);
            Tick(sim, 2);
            Assert.AreEqual(1, veins.Round, "시작 등급에 오르면 신호 묶음 1");
            Assert.AreEqual(1, veins.Exploration.Sites.Count);
            var site = veins.Exploration.Sites[0];
            Assert.IsTrue(site.Revealed, "위치는 바로 보임 (탐사선 없음)");
            Assert.AreEqual(50f, site.Reserve, 1e-4f, "매장량을 정함");

            // 광맥은 코어에서 그 방향 2칸 → 코어 바로 옆(1칸)에 도킹을 두면 광맥과 맞닿음
            Assert.IsTrue(sim.Grid.TryPlace(_dock, site.Direction, 0, out var dock));
            Tick(sim, 2);
            Assert.AreEqual(2f, veins.Scale(dock), 1e-4f, "광맥에 맞닿으면 2배");
            Assert.AreSame(site, veins.VeinOf(dock));
            Assert.Less(site.Reserve, 50f, "캔 만큼 매장량이 줄어듦");

            var state = veins.Capture(m => m == dock ? 1 : m == sim.Core ? 0 : -1);
            StringAssert.Contains("\"Round\":1", state);

            Tick(sim, 40);
            Assert.AreEqual(0f, site.Reserve, 1e-4f, "다 캠");
            Assert.AreEqual(1, veins.ExhaustedCount);
            Assert.AreEqual(0.4f, veins.Scale(dock), 1e-4f, "다 캔 광맥에 붙어 있던 도킹 = 바닥값");
            Tick(sim, 32);
            Assert.AreEqual(2, veins.Round, "다 캔 뒤 일정 시간 → 새 신호");

            var copy = NewSim(out var restored);
            Assert.IsTrue(copy.Grid.TryPlace(_dock, site.Direction, 0, out var copyDock));
            restored.Restore(state, i => i == 1 ? copyDock : i == 0 ? copy.Core : null);
            Assert.AreEqual(1, restored.Round, "세이브: 묶음 번호");
        }

        private StationSimulation NewSim(out OreVeinSystem veins)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config, Grades = _grades, CoreModule = _core, Events = new List<GameEventData>(), Random01 = () => 0.5f,
            });
            veins = OreVeinSystem.AttachTo(sim, _veins, () => 0.5f);
            Assert.IsNotNull(veins);
            return sim;
        }

        private static void Tick(StationSimulation sim, int seconds)
        {
            for (int i = 0; i < seconds; i++)
                sim.Tick(1f);
        }

        private ModuleData Module(string name, bool removable, float metal)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            if (metal > 0f)
                Fill(so.FindProperty("_production"), R(ResourceType.Metal, metal));
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
