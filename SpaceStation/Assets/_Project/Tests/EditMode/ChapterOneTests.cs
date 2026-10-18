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
    /// <summary>13-4 1장: 코어 기동(튜토리얼)이 끝나면 목표 표시 · '첫 긴 밤' 절정(밤 덮기 · 운석 · 생명 유지 바닥 시간 판정 · 저장 뒤 다시 덮기).</summary>
    public class ChapterOneTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core;
        private MeteorEventData _meteor;
        private int _meteors;

        [SetUp]
        public void SetUp()
        {
            _config = Config(oxygen: 150f);
            _core = Create<ModuleData>();
            _core.name = "MD_TestCore";
            var c = new SerializedObject(_core);
            c.FindProperty("_removable").boolValue = false;
            c.FindProperty("_housingCapacity").intValue = 4;
            c.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.FindProperty("_grades").GetArrayElementAtIndex(0).FindPropertyRelative("_displayName").stringValue = "G0";
            g.ApplyModifiedPropertiesWithoutUndo();

            _meteor = Create<MeteorEventData>();
            _meteor.name = "EV_TestMeteor";
            var m = new SerializedObject(_meteor);
            m.FindProperty("_weight").floatValue = 0f;
            m.ApplyModifiedPropertiesWithoutUndo();
            _meteors = 0;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void CoreBootGoal_DoneWhenTutorialEnds_OrWhenNoTutorial()
        {
            var sim = Sim(Chapters(Chapter(night: 20f, hurtShortage: 30f)));
            var tutorial = Create<TutorialData>();
            tutorial.SetSteps(new[] { new TutorialStep("감각 연결", "", TutorialGoal.Camera) });
            var t = sim.StartTutorial(tutorial);
            sim.Tick(1f);
            Assert.IsFalse(sim.Chapters.IsGoalDone(0), "코어 기동 중");
            t.Skip();
            sim.Tick(1f);
            Assert.IsTrue(sim.Chapters.IsGoalDone(0), "건너뛰어도 채워짐");

            var plain = Sim(Chapters(Chapter(night: 20f, hurtShortage: 30f)));
            plain.Tick(1f);
            Assert.IsTrue(plain.Chapters.IsGoalDone(0), "튜토리얼을 끈 판");
        }

        [Test]
        public void LongNight_CoversNight_FiresMeteors_CleanWithoutShortage_ReappliesAfterLoad()
        {
            var chapters = Chapters(Chapter(night: 20f, hurtShortage: 30f));
            var sim = Sim(chapters);
            sim.Tick(1f); // 목표 → 예고 1초
            sim.Tick(1f);
            Assert.AreEqual(ChapterPhase.Climax, sim.Chapters.Phase);
            Assert.IsTrue(sim.DayNight.HasOverride);
            for (int i = 0; i < 10; i++)
                sim.Tick(1f);
            Assert.IsFalse(sim.DayNight.IsDay(sim.ElapsedSeconds), "덮은 밤 (주기가 꺼져 있어도)");
            Assert.AreEqual(1, _meteors, "밤 1/3 지점에 첫 운석");

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var loaded = Sim(chapters);
            StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));
            Assert.AreEqual(ChapterPhase.Climax, loaded.Chapters.Phase);
            Assert.IsFalse(loaded.DayNight.IsDay(loaded.ElapsedSeconds), "저장에서 불러와도 다시 덮음");

            for (int i = 0; i < 30 && sim.Chapters.Phase == ChapterPhase.Climax; i++)
                sim.Tick(1f);
            Assert.AreEqual(2, _meteors, "밤 동안 운석 2개");
            Assert.IsTrue(sim.DayNight.IsDay(sim.ElapsedSeconds), "새벽 뒤");
            Assert.AreEqual(ChapterPhase.Transition, sim.Chapters.Phase);
            Assert.AreEqual(ChapterOutcome.Clean, sim.Chapters.LastRecord.Outcome, "바닥 0초 · 파괴 0");
            StringAssert.Contains("바닥 0초", sim.Chapters.LastRecord.Summary);
        }

        [Test]
        public void LongNight_ShortageSeconds_GradeHurtThenLost()
        {
            _config = Config(oxygen: 0f); // 산소 바닥인 채로 밤을 맞음
            var hurt = Sim(Chapters(Chapter(night: 20f, hurtShortage: 30f)));
            RunToTransition(hurt);
            Assert.AreEqual(ChapterOutcome.Hurt, hurt.Chapters.LastRecord.Outcome, "바닥 20초 ≤ 30초");

            var lost = Sim(Chapters(Chapter(night: 20f, hurtShortage: 10f)));
            RunToTransition(lost);
            Assert.AreEqual(ChapterOutcome.Lost, lost.Chapters.LastRecord.Outcome, "바닥 20초 > 10초");
        }

        // ---------------- 도우미 ----------------

        private static void RunToTransition(StationSimulation sim)
        {
            for (int i = 0; i < 100 && sim.Chapters.Phase != ChapterPhase.Transition; i++)
                sim.Tick(1f);
            Assert.AreEqual(ChapterPhase.Transition, sim.Chapters.Phase);
        }

        private BalanceConfig Config(float oxygen)
        {
            var c = Create<BalanceConfig>();
            var b = new SerializedObject(c);
            b.FindProperty("_startingPopulation").intValue = 4;
            b.FindProperty("_startingSatisfaction").floatValue = 80f;
            b.FindProperty("_growthMinSatisfaction").floatValue = 100f;
            b.FindProperty("_oxygenDepletedLossInterval").floatValue = 10000f;
            b.FindProperty("_oxygenFailSeconds").floatValue = 10000f; // 산소 바닥 시험이 게임 오버로 끝나지 않게
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, oxygen), R(ResourceType.Water, 150), R(ResourceType.Food, 150), R(ResourceType.Metal, 500));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 1000));
            b.FindProperty("_destroyAfterSeconds").floatValue = 600f;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 10000f;
            b.FindProperty("_eventIntervalMin").floatValue = 10000f;
            b.FindProperty("_eventIntervalMax").floatValue = 10000f;
            b.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        private StationSimulation Sim(ChapterConfig chapters)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData> { _meteor },
                Random01 = () => 0.5f,
                Chapters = chapters,
            });
            sim.Events.EventStarted += e =>
            {
                if (e is MeteorEventData)
                    _meteors++;
            };
            return sim;
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

        /// <summary>필수 = 코어 기동 표시 하나 · 예고 1초 · 긴 밤(운석 2개 × 1, 새벽 뒤 정리 2초, 깔끔 = 바닥 0초).</summary>
        private ChapterData Chapter(float night, float hurtShortage)
        {
            var c = Create<ChapterData>();
            c.name = "CH_1";
            var so = new SerializedObject(c);
            so.FindProperty("_displayName").stringValue = "1장";
            var goals = so.FindProperty("_goals");
            goals.arraySize = 1;
            var g = goals.GetArrayElementAtIndex(0);
            g.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Flag;
            g.FindPropertyRelative("_flag").stringValue = ChapterSystem.TutorialFlag;
            var climax = so.FindProperty("_climax");
            climax.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterClimaxKind.LongNight;
            climax.FindPropertyRelative("_warningSeconds").floatValue = 1f;
            climax.FindPropertyRelative("_waves").intValue = 2;
            climax.FindPropertyRelative("_hitsPerWave").intValue = 1;
            climax.FindPropertyRelative("_settleSeconds").floatValue = 2f;
            climax.FindPropertyRelative("_nightSeconds").floatValue = night;
            climax.FindPropertyRelative("_cleanMaxShortageSeconds").floatValue = 0f;
            climax.FindPropertyRelative("_hurtMaxShortageSeconds").floatValue = hurtShortage;
            climax.FindPropertyRelative("_hurtMaxDestroyed").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            return c;
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
