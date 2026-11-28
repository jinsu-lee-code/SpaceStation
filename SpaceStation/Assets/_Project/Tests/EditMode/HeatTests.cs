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

        // ---------------- CONNECT A10 온기 (2026-11-28) ----------------

        [Test]
        public void Warmth_InBand_LowersPower_RaisesProduction_OnlyWithConfig()
        {
            var warm = Consumer("MD_TestWarm", 5f, ResourceType.Food, 2f);
            var sim = Sim();
            Place(sim, _hot, 1, 0, 0);
            var inBand = Place(sim, warm, 2, 0, 0);   // 1칸 = 2 (0.5 ~ 4)
            Place(sim, _plain, 3, 0, 0);              // 이음 (코어에 연결되게)
            var cold = Place(sim, warm, 4, 0, 0);     // 3칸 = 0
            sim.Tick(1f);
            Assert.IsFalse(sim.Heat.IsWarm(inBand), "설정이 없으면(캠페인) 온기 없음");
            Assert.AreEqual(1f, sim.Heat.PowerDemandMultiplier(inBand));
            Assert.AreEqual(1f, sim.HeatMultiplier(inBand));
            Assert.AreEqual(10f, sim.Resources.PowerDemand, 1e-3f, "예전 그대로");

            sim.Heat.Comfort = Comfort(new[] { warm }, new ModuleData[0]);
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.IsTrue(sim.Heat.IsWarm(inBand), "열 2 = 온기");
            Assert.IsFalse(sim.Heat.IsWarm(cold), "열 0 = 온기 아님");
            Assert.AreEqual(0.8f, sim.Heat.PowerDemandMultiplier(inBand), 1e-4f);
            Assert.AreEqual(1.15f, sim.HeatMultiplier(inBand), 1e-4f, "생산 +15%");
            Assert.AreEqual(1f, sim.HeatMultiplier(cold));
            Assert.AreEqual(5f * 0.8f + 5f, sim.Resources.PowerDemand, 1e-3f, "온기 방만 전력 -20%");
            StringAssert.Contains("♨ 온기", sim.Heat.StateText(inBand));
            StringAssert.Contains("온기 아님", sim.Heat.StateText(cold));
        }

        [Test]
        public void Warmth_AboveBand_NoBonus()
        {
            var warm = Consumer("MD_TestWarm", 5f, ResourceType.Food, 2f);
            var sim = Sim();
            sim.Heat.Comfort = Comfort(new[] { warm }, new ModuleData[0]);
            Place(sim, _hot, 1, 0, 0);
            Place(sim, _hot, 3, 0, 0);
            var between = Place(sim, warm, 2, 0, 0); // 2 + 2 = 4 (구간 끝 — 포함)
            var hotter = Place(sim, warm, 2, 0, 1);  // 두 발열원 모두 1칸(대각) = 4 → 같은 값
            Place(sim, _hot, 2, 0, 2);               // hotter만 1칸 더 = 6 (> 4, ≤ 한도 6)
            sim.Tick(1f);
            Assert.AreEqual(5f, sim.Heat.HeatOf(between), 1e-4f, "2 + 2 + 2칸 1");
            Assert.IsFalse(sim.Heat.IsWarm(between), "4 넘음 = 온기보다 더움");
            Assert.IsFalse(sim.Heat.IsWarm(hotter));
            Assert.AreEqual(1f, sim.HeatMultiplier(between), "더움 = 보너스 없음, 벌칙도 없음");
            StringAssert.Contains("온기보다 더움", sim.Heat.StateText(between));
        }

        [Test]
        public void CoolModule_OverheatsAtHalfLimit()
        {
            var cool = Consumer("MD_TestCool", 8f, ResourceType.Food, 0f);
            var sim = Sim();
            sim.Heat.Comfort = Comfort(new ModuleData[0], new[] { cool });
            Place(sim, _hot, 1, 0, 0);
            Place(sim, _hot, 3, 0, 0);
            var c = Place(sim, cool, 2, 0, 0);    // 2 + 2 = 4
            var p = Place(sim, _plain, 2, 0, 1);  // 같은 4
            sim.Tick(1f);
            Assert.AreEqual(3f, sim.Heat.LimitOf(c), 1e-4f, "보통 6의 절반");
            Assert.AreEqual(6f, sim.Heat.LimitOf(p), 1e-4f);
            Assert.IsTrue(sim.Heat.IsOverheated(c), "서늘함 = 4 > 3 과열");
            Assert.IsFalse(sim.Heat.IsOverheated(p), "보통 = 4 ≤ 6");
            Assert.IsFalse(sim.Heat.IsDanger(c), "위험 = 3 × 1.5 = 4.5");
            Assert.Less(sim.HeatMultiplier(c), 1f);
        }

        [Test]
        public void Preview_MatchesHeatAfterPlacing_AndCountsWarmedNeighbors()
        {
            var warm = Consumer("MD_TestWarm", 5f, ResourceType.Food, 2f);
            var sim = Sim();
            sim.Heat.Comfort = Comfort(new[] { warm }, new ModuleData[0]);
            Place(sim, _hot, 1, 0, 0);
            Place(sim, _radiator, 1, 0, 1); // 동작 중 (바깥 노출)
            sim.Tick(1f);
            var cells = new[] { new Vector3Int(2, 0, 1) };
            float preview = sim.Heat.PreviewHeatAt(cells);
            var placed = Place(sim, warm, 2, 0, 1);
            sim.Tick(1f);
            Assert.AreEqual(sim.Heat.HeatOf(placed), preview, 1e-4f, "미리보기 = 실제 (대각 1칸 2 − 맞닿은 방열판 4 → 0)");

            // 발열원 미리보기: 3칸 떨어진 따뜻함 모듈 옆에 새 발열원 → 온기 +1
            var far = Place(sim, warm, 5, 0, 0);
            sim.Tick(1f);
            Assert.IsFalse(sim.Heat.IsWarm(far));
            sim.Heat.PreviewSource(_hot, new[] { new Vector3Int(6, 0, 0) }, null, out int warmed, out int lost, out int over);
            Assert.AreEqual(1, warmed, "옆 1칸 = 2 → 온기");
            Assert.AreEqual(0, lost);
            Assert.AreEqual(0, over);
            StringAssert.Contains("이웃 ♨ 온기 +1", sim.Heat.PreviewText(_hot, new[] { new Vector3Int(6, 0, 0) }, null));
        }

        // ---------------- CONNECT ⑤ 방의 온도 · 과부하 (2026-11-28) ----------------

        [Test]
        public void CoolRoom_TouchesWorkingRadiator_OnlyWithConfig()
        {
            var sim = Sim();
            Place(sim, _hot, 1, 0, 0);
            var room = Place(sim, _plain, 2, 0, 0);
            Place(sim, _radiator, 3, 0, 0);
            var far = Place(sim, _plain, 1, 0, 1);
            sim.Tick(1f);
            Assert.IsFalse(sim.Heat.IsCoolRoom(room), "설정이 없으면 없음");
            sim.Heat.Comfort = Comfort(new ModuleData[0], new ModuleData[0]);
            sim.Tick(1f);
            Assert.IsTrue(sim.Heat.IsCoolRoom(room), "동작 중인 방열판과 맞닿음");
            Assert.IsFalse(sim.Heat.IsCoolRoom(far));
            var lines = new List<string>();
            sim.Heat.AppendRoomLines(lines, room);
            Assert.That(lines, Has.Some.Contains("서늘한 방"));
        }

        [Test]
        public void Overload_HeavyBasicOutsideReach_ClearsWithBattery()
        {
            var heavy = Consumer("MD_TestHeavy", 6f, ResourceType.Food, 0f);
            var light = Consumer("MD_TestLight", 2f, ResourceType.Food, 0f);
            var battery = Module("MD_TestBattery", 0f, 0f);
            var bso = new SerializedObject(battery);
            bso.FindProperty("_batteryCapacity").floatValue = 50f;
            bso.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim();
            sim.Heat.Comfort = Comfort(new ModuleData[0], new ModuleData[0]);
            Place(sim, battery, 1, 0, 0);                 // 배전 기준점 (코어는 시험용이라 전력을 안 냄)
            for (int x = 2; x <= 4; x++)
                Place(sim, _plain, x, 0, 0);
            var near = Place(sim, heavy, 4, 0, 1);       // 배터리에서 3칸 = 범위 안
            var farHeavy = Place(sim, heavy, 5, 0, 0);   // 4칸 = 범위 밖
            var farLight = Place(sim, light, 5, 0, 1);   // 4칸이지만 전력 2 = 대전력 아님
            sim.Tick(1f);
            Assert.IsFalse(sim.Heat.IsOverloaded(near));
            Assert.IsTrue(sim.Heat.IsOverloaded(farHeavy));
            Assert.IsFalse(sim.Heat.IsOverloaded(farLight));
            Assert.IsFalse(sim.Heat.IsHeavyBasic(_hot), "발열원은 제외");
            StringAssert.Contains("배전 범위 밖", sim.Heat.OverloadText(farHeavy));
            StringAssert.Contains("배전 범위에 넣음 1곳", sim.Heat.OverloadPreviewText(battery, new[] { new Vector3Int(6, 0, 0) }, null));
            StringAssert.Contains("배전 범위 밖", sim.Heat.OverloadPreviewText(heavy, new[] { new Vector3Int(6, 0, 1) }, null));

            Place(sim, battery, 6, 0, 0);
            sim.Tick(1f);
            Assert.IsFalse(sim.Heat.IsOverloaded(farHeavy), "범위 안에 배터리 = 풀림");
            Assert.IsNull(sim.Heat.OverloadText(farHeavy));

            sim.Heat.Comfort = null;
            Assert.IsFalse(sim.Heat.IsOverloaded(farHeavy), "설정이 없으면(캠페인) 과부하 없음");
        }

        [Test]
        public void RoomLines_WarmAndOverheated()
        {
            var warm = Consumer("MD_TestWarm", 5f, ResourceType.Food, 2f);
            var sim = Sim();
            sim.Heat.Comfort = Comfort(new[] { warm }, new ModuleData[0]);
            Place(sim, _hot, 1, 0, 0);
            var w = Place(sim, warm, 2, 0, 0);
            Place(sim, _hot, 3, 0, 1);
            Place(sim, _hot, 3, 0, -1);
            Place(sim, _hot, 4, 0, 0);
            var hot = Place(sim, _plain, 3, 0, 0); // 맞닿은 발열원 셋 2 + 2 + 2 + 2칸 1 = 7 > 한도 6
            sim.Tick(1f);
            var lines = new List<string>();
            sim.Heat.AppendRoomLines(lines, hot);
            Assert.Greater(sim.Heat.HeatOf(hot), 6f);
            Assert.That(lines, Has.Some.Contains("과열"));
            lines.Clear();
            Assert.IsFalse(sim.Heat.IsWarm(w), "옆에 발열원 셋 = 더움");
        }

        private HeatPowerConfig Comfort(IEnumerable<ModuleData> warm, IEnumerable<ModuleData> cool)
        {
            var c = HeatPowerConfig.CreateForTest(warm, cool);
            _created.Add(c);
            return c;
        }

        private ModuleData Consumer(string name, float power, ResourceType produces, float amount)
        {
            var m = Module(name, 0f, 0f);
            var so = new SerializedObject(m);
            var cons = so.FindProperty("_consumption");
            cons.arraySize = 1;
            cons.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Power;
            cons.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = power;
            if (amount > 0f)
            {
                var prod = so.FindProperty("_production");
                prod.arraySize = 1;
                prod.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)produces;
                prod.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = amount;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
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
