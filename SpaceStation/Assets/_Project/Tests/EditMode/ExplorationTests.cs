using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceStation.Tests
{
    /// <summary>13-5a 탐사: 센서가 있어야 신호 · 탐사선(비용 · 시간 · 한 장 하나) · 뻗어서 닿기 · 종류별 효과 · 세이브.</summary>
    public class ExplorationTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _block, _sensor;
        private ExplorationConfig _explore;
        private CoreConfig _coreConfig;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Water, 50), R(ResourceType.Food, 150), R(ResourceType.Metal, 100));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 1000));
            b.FindProperty("_destroyAfterSeconds").floatValue = 6000f;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.ApplyModifiedPropertiesWithoutUndo();
            _core = Module("MD_TestCore", false);
            _block = Module("MD_TestBlock", true);
            _sensor = Module("MD_TestSensor", true);

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.FindProperty("_grades").GetArrayElementAtIndex(0).FindPropertyRelative("_displayName").stringValue = "G0";
            g.ApplyModifiedPropertiesWithoutUndo();

            _explore = Create<ExplorationConfig>();
            _explore.EditorSet(_sensor, new List<string> { "첫 기록", "둘째 기록" });
            _coreConfig = Create<CoreConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Signals_NeedSensor_ThreeSitesBeyondEdge_DistinctDirections()
        {
            var sim = Sim(out var ex, out _);
            Tick(sim, 3);
            Assert.AreEqual(0, ex.Sites.Count, "센서가 없으면 신호 없음");
            Assert.IsTrue(sim.Grid.TryPlace(_sensor, Vector3Int.right, 0, out _));
            Tick(sim, 1);
            Assert.AreEqual(3, ex.Sites.Count);
            Assert.AreEqual(3, ex.Sites.Select(s => s.Direction).Distinct().Count(), "방향이 겹치지 않음");
            CollectionAssert.AreEquivalent(new[] { SiteKind.Asteroid, SiteKind.Wreck, SiteKind.DebrisField }, ex.Sites.Select(s => s.Kind));
            foreach (var s in ex.Sites)
            {
                int edge = s.Direction == Vector3Int.right ? 1 : 0; // 센서가 +x 1칸
                Assert.AreEqual((edge + 6) * s.Direction, s.Cell, "정거장 끝에서 6칸 (난수 가운데 = 옆 · 높이 0)");
                Assert.IsFalse(s.Revealed);
            }
            Tick(sim, 3);
            Assert.AreEqual(3, ex.Sites.Count, "장마다 한 번만");
        }

        [Test]
        public void Probe_CostsMetal_RevealsAfterTime_OnlyOncePerChapter()
        {
            var sim = Sim(out var ex, out _);
            Assert.IsTrue(sim.Grid.TryPlace(_sensor, Vector3Int.right, 0, out _));
            Tick(sim, 1);
            var site = ex.Sites[0];
            Assert.IsNull(ex.ProbeBlocked(site));
            Assert.IsTrue(ex.LaunchProbe(site));
            Assert.AreEqual(40f, sim.Resources.GetStock(ResourceType.Metal), 0.5f, "금속 100 − 60");
            Assert.IsNotNull(ex.ProbeBlocked(ex.Sites[1]), "날아가는 중");
            Tick(sim, 44);
            Assert.IsFalse(site.Revealed);
            Tick(sim, 1);
            Assert.IsTrue(site.Revealed, "45초 뒤 밝힘");
            Assert.IsTrue(sim.Chapters.HasFlag(ExplorationSystem.ProbeFlag));
            StringAssert.Contains("이미 보냄", ex.ProbeBlocked(ex.Sites[1]), "한 장에 하나");
        }

        [Test]
        public void Reach_ByExtending_GivesKindEffects_AndSurvivesSave()
        {
            var sim = Sim(out var ex, out var core);
            Assert.IsTrue(sim.Grid.TryPlace(_sensor, new Vector3Int(0, 1, 0), 0, out _)); // 위로 (가로 끝 그대로)
            Tick(sim, 1);
            var wreck = ex.Sites.First(s => s.Kind == SiteKind.Wreck);
            Assert.IsTrue(ex.LaunchProbe(wreck));
            Tick(sim, 45);
            Assert.AreEqual(6, ex.Distance(wreck));
            for (int i = 1; i <= 4; i++) // 지점 6칸 → 4칸까지 이으면 반경 2 안
                Assert.IsTrue(sim.Grid.TryPlace(_block, wreck.Direction * i, 0, out _));
            Tick(sim, 1);
            Assert.AreEqual(2, ex.Distance(wreck));
            Assert.IsTrue(wreck.Reached);
            Assert.IsTrue(sim.Chapters.HasFlag(ExplorationSystem.ReachFlag));
            Assert.AreEqual(1, ex.Fragments, "기록 조각");
            Assert.AreEqual(2, sim.Supply.ResearchPoints, "연구 포인트 +2");
            StringAssert.Contains("첫 기록", core.Entries.Last().Text, "기록 로그가 코어 기록에");

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var loaded = Sim(out var ex2, out _);
            StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));
            Assert.AreEqual(3, ex2.Sites.Count);
            Assert.AreEqual(1, ex2.Fragments);
            Assert.IsTrue(ex2.Sites.First(s => s.Kind == SiteKind.Wreck).Reached);
            Assert.IsTrue(ex2.ProbeUsedThisChapter, "탐사선 기록도 저장");
        }

        [Test]
        public void Asteroid_GivesMetalWhileConnected_Debris_RewardThenFragments()
        {
            var sim = Sim(out var ex, out _);
            Assert.IsTrue(sim.Grid.TryPlace(_sensor, new Vector3Int(0, 1, 0), 0, out _));
            Tick(sim, 1);
            var rock = ex.Sites.First(s => s.Kind == SiteKind.Asteroid);
            rock.Revealed = true; // 시험: 탐사선 없이 밝힌 것으로
            var debris = ex.Sites.First(s => s.Kind == SiteKind.DebrisField);
            debris.Revealed = true;
            for (int i = 1; i <= 4; i++)
            {
                Assert.IsTrue(sim.Grid.TryPlace(_block, rock.Direction * i, 0, out _));
                Assert.IsTrue(sim.Grid.TryPlace(_block, debris.Direction * i, 0, out _));
            }
            Tick(sim, 1);
            Assert.IsTrue(rock.Reached && rock.InReach);
            Assert.IsTrue(debris.Reached);
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.GreaterOrEqual(metal, 100f + 150f - 1f, "파편 지대 금속 +150");
            Tick(sim, 10);
            Assert.AreEqual(metal + 0.4f * 10f, sim.Resources.GetStock(ResourceType.Metal), 0.5f, "소행성 초당 +0.4");

            int damaged = 0;
            sim.Damage.Damaged += _ => damaged++;
            Tick(sim, 120);
            Assert.GreaterOrEqual(damaged, 1, "2분 뒤 그 방향에서 파편");
        }

        // ---------------- 도우미 ----------------

        private static void Tick(StationSimulation sim, int seconds)
        {
            for (int i = 0; i < seconds; i++)
                sim.Tick(1f);
        }

        private StationSimulation Sim(out ExplorationSystem ex, out CorePersonalitySystem core)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                Chapters = Chapters(),
            });
            core = sim.AddSystem(new CorePersonalitySystem(_coreConfig), TickStepNames.Progression);
            ex = sim.AddSystem(new ExplorationSystem(_explore, () => 0.5f), TickStepNames.Population);
            return sim;
        }

        private ChapterConfig Chapters()
        {
            var c = Create<ChapterData>();
            c.name = "CH_2";
            var so = new SerializedObject(c);
            so.FindProperty("_displayName").stringValue = "2장";
            var goals = so.FindProperty("_goals");
            goals.arraySize = 1;
            goals.GetArrayElementAtIndex(0).FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.SurviveSeconds;
            goals.GetArrayElementAtIndex(0).FindPropertyRelative("_amount").floatValue = 100000f;
            var signals = so.FindProperty("_signals");
            signals.arraySize = 3;
            signals.GetArrayElementAtIndex(0).enumValueIndex = (int)SiteKind.Asteroid;
            signals.GetArrayElementAtIndex(1).enumValueIndex = (int)SiteKind.Wreck;
            signals.GetArrayElementAtIndex(2).enumValueIndex = (int)SiteKind.DebrisField;
            so.FindProperty("_signalDistanceMin").intValue = 6;
            so.FindProperty("_signalDistanceMax").intValue = 6;
            so.ApplyModifiedPropertiesWithoutUndo();
            var cfg = Create<ChapterConfig>();
            var cso = new SerializedObject(cfg);
            cso.FindProperty("_chapters").arraySize = 1;
            cso.FindProperty("_chapters").GetArrayElementAtIndex(0).objectReferenceValue = c;
            cso.ApplyModifiedPropertiesWithoutUndo();
            return cfg;
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
