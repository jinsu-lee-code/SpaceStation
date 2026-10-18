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
    /// 13-5c 2장: 장이 시작되면 첫 유성우 앞당김(1파) · 평소 유성우를 파괴 없이 버티면 'shower' 표시(잃으면 안 됨) ·
    /// 절정 '큰 유성우'(두 방향 · 같은 방향 연속 없음 · 막은 비율 + 파괴 판정 · 저장 뒤 이어서).
    /// </summary>
    public class ChapterTwoTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _block, _turret;

        [SetUp]
        public void SetUp()
        {
            _config = Config(destroyAfter: 600f);
            _core = Module("MD_TestCore", removable: false);
            _block = Module("MD_TestBlock");
            _turret = Module("MD_TestTurret");
            var t = new SerializedObject(_turret);
            t.FindProperty("_turretRadius").intValue = 6;
            t.FindProperty("_turretInterceptChance").floatValue = 1f;
            t.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_meteorHitsMin").intValue = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_meteorHitsMax").intValue = 1;
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
        public void FirstShower_ScheduledAtChapterStart_FlagWhenSurvivedWithoutLoss()
        {
            var ch1 = Chapter("CH_1", StationRule.None, ChapterClimaxKind.None);
            var ch2 = Chapter("CH_2", StationRule.MeteorShower, ChapterClimaxKind.None, firstShower: 5f, survive: 100000f);
            var sim = Sim(Chapters(ch1, ch2));
            var a = Place(sim, new Vector3Int(1, 0, 0));
            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Transition);
            Assert.IsTrue(sim.Chapters.Choose(-1));
            Assert.AreEqual(1, sim.Chapters.Index);
            var shower = sim.MeteorShower;
            Assert.AreEqual(5f, shower.TimeLeft, 1e-3f, "2장 시작 → 첫 유성우 앞당김");

            RunUntil(sim, () => shower.Phase == MeteorShowerSystem.ShowerPhase.Warning);
            Assert.AreEqual(1, shower.Waves, "첫 유성우는 1파");
            RunUntil(sim, () => shower.Phase == MeteorShowerSystem.ShowerPhase.Idle);
            Assert.IsTrue(sim.Damage.IsDamaged(a), "+X 앞면이 맞음");
            sim.Tick(1f);
            Assert.IsFalse(sim.Chapters.HasFlag(ChapterShowerSystem.ShowerFlag), "파손이 남아 있는 동안은 기다림");
            sim.Damage.CompleteNow(a); // 고침
            sim.Tick(1f);
            Assert.IsTrue(sim.Chapters.HasFlag(ChapterShowerSystem.ShowerFlag), "파괴 없이 버팀");
        }

        [Test]
        public void ShowerFlag_NotSet_WhenModuleLost()
        {
            _config = Config(destroyAfter: 3f);
            var sim = Sim(Chapters(Chapter("CH_2", StationRule.MeteorShower, ChapterClimaxKind.None, survive: 100000f)));
            var a = Place(sim, new Vector3Int(1, 0, 0));
            sim.Tick(1f);
            Assert.IsTrue(sim.MeteorShower.ScheduleNext(1f, 1));
            RunUntil(sim, () => sim.Session.ModulesDestroyed > 0);
            for (int i = 0; i < 10; i++)
                sim.Tick(1f);
            Assert.IsFalse(sim.Chapters.HasFlag(ChapterShowerSystem.ShowerFlag), "모듈을 잃으면 다음 유성우에서 다시");
        }

        [Test]
        public void GreatShower_TwoDirections_NoDefense_HurtByBlockedRatio_ResumesAfterLoad()
        {
            var chapters = Chapters(Chapter("CH_2", StationRule.None, ChapterClimaxKind.DirectionalShower));
            var sim = Sim(chapters);
            PlaceAround(sim);
            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Climax);
            var shower = sim.MeteorShower;
            Assert.IsTrue(shower.IsRaid);
            Assert.AreEqual("큰 유성우", shower.Label);
            var first = shower.Direction;
            Assert.AreEqual(Vector3Int.right, first, "난수 0 = 첫 방향");

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var loaded = Sim(chapters);
            StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));
            Assert.AreEqual(ChapterPhase.Climax, loaded.Chapters.Phase);
            Assert.AreEqual("큰 유성우", loaded.MeteorShower.Label, "특별 유성우 이름도 저장");
            StringAssert.Contains("1/2", loaded.Chapters.Climax.Status);

            RunUntil(sim, () => shower.IsRaid && shower.Direction != first || sim.Chapters.Phase != ChapterPhase.Climax);
            Assert.AreNotEqual(first, shower.Direction, "다음 방향은 다름");
            RunUntil(sim, () => sim.Chapters.Phase != ChapterPhase.Climax);
            Assert.AreEqual(ChapterOutcome.Hurt, sim.Chapters.LastRecord.Outcome, "파괴 0이지만 막은 비율 0% < 50%");
            StringAssert.Contains("4개 중 0개 막음", sim.Chapters.LastRecord.Summary, "2방향 × 2파 × 1개");
        }

        [Test]
        public void GreatShower_AllIntercepted_Clean()
        {
            var sim = Sim(Chapters(Chapter("CH_2", StationRule.None, ChapterClimaxKind.DirectionalShower)));
            PlaceAround(sim);
            Assert.IsTrue(sim.Grid.TryPlace(_turret, new Vector3Int(1, 1, 0), 0, out _)); // 모든 모듈이 반경 안
            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Transition);
            Assert.AreEqual(ChapterOutcome.Clean, sim.Chapters.LastRecord.Outcome, "모두 격추 · 파괴 0");
            StringAssert.Contains("(100%)", sim.Chapters.LastRecord.Summary);
        }

        // ---------------- 도우미 ----------------

        private static void RunUntil(StationSimulation sim, System.Func<bool> done)
        {
            for (int i = 0; i < 400 && !done(); i++)
                sim.Tick(1f);
            Assert.IsTrue(done(), "시간 안에 끝나지 않음");
        }

        private void PlaceAround(StationSimulation sim)
        {
            foreach (var d in new[] { Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1), Vector3Int.up, Vector3Int.down })
                Place(sim, d);
        }

        private ModuleInstance Place(StationSimulation sim, Vector3Int at)
        {
            Assert.IsTrue(sim.Grid.TryPlace(_block, at, 0, out var m), $"블록 @ {at}");
            return m;
        }

        private StationSimulation Sim(ChapterConfig chapters)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                HazardRandom01 = () => 0f,
                Chapters = chapters,
            });
            sim.AddSystem(new ChapterShowerSystem(), TickStepNames.Events);
            return sim;
        }

        private BalanceConfig Config(float destroyAfter)
        {
            var c = Create<BalanceConfig>();
            var b = new SerializedObject(c);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 1000));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 1000), R(ResourceType.Water, 1000), R(ResourceType.Food, 1000), R(ResourceType.Metal, 1000));
            b.FindProperty("_destroyAfterSeconds").floatValue = destroyAfter;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_showerMinGrade").intValue = 99; // 장 규칙으로만 열림
            b.FindProperty("_showerIntervalMin").floatValue = 1000f;
            b.FindProperty("_showerIntervalMax").floatValue = 1000f;
            b.FindProperty("_showerWarningSeconds").floatValue = 2f;
            b.FindProperty("_showerWaves").intValue = 3;
            b.FindProperty("_showerWaveInterval").floatValue = 1f;
            b.FindProperty("_showerHitsMultiplier").floatValue = 3f;
            b.FindProperty("_turretMaxIntercept").floatValue = 1f;
            b.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        private ChapterConfig Chapters(params ChapterData[] list)
        {
            var c = Create<ChapterConfig>();
            var so = new SerializedObject(c);
            var p = so.FindProperty("_chapters");
            p.arraySize = list.Length;
            for (int i = 0; i < list.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        /// <summary>필수 = 코어 기동 표시(튜토리얼이 없으면 저절로) [+ 버티기] · 예고 0초 · 큰 유성우 = 2방향 × 2파 × 1개, 예보 3 · 2초, 정리 최대 5초.</summary>
        private ChapterData Chapter(string name, StationRule rules, ChapterClimaxKind climaxKind, float firstShower = 0f, float survive = 0f)
        {
            var c = Create<ChapterData>();
            c.name = name;
            var so = new SerializedObject(c);
            so.FindProperty("_displayName").stringValue = name;
            so.FindProperty("_rules").intValue = (int)rules;
            so.FindProperty("_meteorHitsMin").intValue = 1;
            so.FindProperty("_meteorHitsMax").intValue = 1;
            so.FindProperty("_firstShowerDelay").floatValue = firstShower;
            so.FindProperty("_firstShowerWaves").intValue = 1;
            var goals = so.FindProperty("_goals");
            goals.arraySize = survive > 0f ? 2 : 1;
            var g = goals.GetArrayElementAtIndex(0);
            g.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Flag;
            g.FindPropertyRelative("_flag").stringValue = ChapterSystem.TutorialFlag;
            if (survive > 0f)
            {
                var s = goals.GetArrayElementAtIndex(1);
                s.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.SurviveSeconds;
                s.FindPropertyRelative("_amount").floatValue = survive;
            }
            var climax = so.FindProperty("_climax");
            climax.FindPropertyRelative("_kind").enumValueIndex = (int)climaxKind;
            climax.FindPropertyRelative("_title").stringValue = "큰 유성우";
            climax.FindPropertyRelative("_warningSeconds").floatValue = 0f;
            climax.FindPropertyRelative("_waves").intValue = 2;
            climax.FindPropertyRelative("_hitsPerWave").intValue = 1;
            climax.FindPropertyRelative("_settleSeconds").floatValue = 5f;
            climax.FindPropertyRelative("_hurtMaxDestroyed").intValue = 1;
            climax.FindPropertyRelative("_showerDirections").intValue = 2;
            climax.FindPropertyRelative("_showerFirstWarning").floatValue = 3f;
            climax.FindPropertyRelative("_showerNextWarning").floatValue = 2f;
            climax.FindPropertyRelative("_cleanMinBlocked").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        private ModuleData Module(string name, bool removable = true)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
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
