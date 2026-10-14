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
    /// <summary>13-1 장 틀: 준비 목표(필수 · 선택 보상) → 절정 예고 → 절정(운석 연타, 전용 판정) → 전환(방향 선택) → 다음 장 · 캠페인 끝, 장 해금 · 규칙 · 규모, 세이브.</summary>
    public class ChapterTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ChapterConfig _chapters;
        private ModuleData _core, _block, _battery;
        private MeteorEventData _meteor;

        [SetUp]
        public void SetUp()
        {
            _nextX = 1;
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Metal, 500));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Metal, 1000));
            b.FindProperty("_destroyAfterSeconds").floatValue = 600f;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 1000f;
            b.FindProperty("_eventIntervalMin").floatValue = 1000f;
            b.FindProperty("_eventIntervalMax").floatValue = 1000f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 0f, removable: false);
            _block = Module("MD_TestBlock", 10f);
            _battery = Module("MD_TestBattery", 10f);

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 2;
            g.FindProperty("_grades").GetArrayElementAtIndex(0).FindPropertyRelative("_displayName").stringValue = "G0";
            var g1 = g.FindProperty("_grades").GetArrayElementAtIndex(1);
            g1.FindPropertyRelative("_displayName").stringValue = "G1";
            g1.FindPropertyRelative("_minModules").intValue = 4;
            g.ApplyModifiedPropertiesWithoutUndo();

            _meteor = Create<MeteorEventData>();
            _meteor.name = "EV_TestMeteor";
            var m = new SerializedObject(_meteor);
            m.FindProperty("_weight").floatValue = 0f;
            m.ApplyModifiedPropertiesWithoutUndo();

            // 1장: 블록 2개(필수) + 표시(선택, 금속 50 보상), 운석 연타 2파 · 깔끔 기준 파손 0 / 2장: 배터리 해금 · 유성우 규칙 · 운석 3개, 등급 G1(필수)
            var ch1 = Chapter("1장", new[] { _block }, StationRule.None, 1, choices: new[] { "a", "b" });
            var so1 = new SerializedObject(ch1);
            var goals = so1.FindProperty("_goals");
            goals.arraySize = 2;
            Goal(goals.GetArrayElementAtIndex(0), ChapterGoalKind.BuildModule, module: _block, count: 2);
            var flag = goals.GetArrayElementAtIndex(1);
            Goal(flag, ChapterGoalKind.Flag, optional: true, flag: "explore");
            Fill(flag.FindPropertyRelative("_rewardResources"), R(ResourceType.Metal, 50));
            flag.FindPropertyRelative("_rewardResearchPoints").intValue = 1;
            Climax(so1.FindProperty("_climax"), ChapterClimaxKind.MeteorBarrage, warning: 30f);
            so1.ApplyModifiedPropertiesWithoutUndo();

            var ch2 = Chapter("2장", new[] { _battery }, StationRule.MeteorShower, 3);
            var so2 = new SerializedObject(ch2);
            so2.FindProperty("_goals").arraySize = 1;
            Goal(so2.FindProperty("_goals").GetArrayElementAtIndex(0), ChapterGoalKind.ReachGrade, grade: 1);
            so2.FindProperty("_newNeeds").arraySize = 1;
            so2.FindProperty("_newNeeds").GetArrayElementAtIndex(0).enumValueIndex = (int)ResidentNeed.Medical;
            Climax(so2.FindProperty("_climax"), ChapterClimaxKind.None, warning: 0f);
            so2.ApplyModifiedPropertiesWithoutUndo();

            _chapters = Create<ChapterConfig>();
            var c = new SerializedObject(_chapters);
            var list = c.FindProperty("_chapters");
            list.arraySize = 2;
            list.GetArrayElementAtIndex(0).objectReferenceValue = ch1;
            list.GetArrayElementAtIndex(1).objectReferenceValue = ch2;
            c.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Preparation_RequiredGoals_StartWarning_OptionalGivesReward_UnlocksByChapter()
        {
            var sim = Sim();
            var ch = sim.Chapters;
            Assert.IsNotNull(ch);
            Assert.AreEqual(0, ch.Index);
            Assert.AreEqual(ChapterPhase.Preparation, ch.Phase);
            Assert.AreEqual(PlacementResult.ModuleLocked, sim.CheckBuildable(_battery), "2장 모듈은 아직 잠김");
            Assert.AreEqual(PlacementResult.Valid, sim.CheckBuildable(_block));
            Assert.IsFalse(sim.RuleOpen(StationRule.MeteorShower, true), "장 틀이 있으면 등급 조건 대신 장 규칙");

            Place(sim, 1);
            sim.Tick(1f);
            ch.GoalProgress(0, out float cur, out float req);
            Assert.AreEqual(1f, cur);
            Assert.AreEqual(2f, req);
            Assert.AreEqual(ChapterPhase.Preparation, ch.Phase);

            float metal = sim.Resources.GetStock(ResourceType.Metal);
            ch.SetFlag("explore");
            Assert.IsTrue(ch.IsGoalDone(1), "선택 목표");
            Assert.AreEqual(metal + 50f, sim.Resources.GetStock(ResourceType.Metal), 0.01f, "선택 목표 보상");
            Assert.AreEqual(1, sim.Supply.ResearchPoints);
            Assert.AreEqual(ChapterPhase.Preparation, ch.Phase, "선택 목표만으로는 예고 없음");

            Place(sim, 1);
            sim.Tick(1f);
            Assert.AreEqual(ChapterPhase.Warning, ch.Phase);
            Assert.AreEqual(30f, ch.WarningLeft, 0.01f);
            sim.Tick(1f);
            Assert.AreEqual(29f, ch.WarningLeft, 0.01f);
        }

        [Test]
        public void Climax_MeteorBarrage_JudgesOwnCriteria_ThenChoiceAdvances_LastChapterFinishes()
        {
            var sim = Sim();
            var ch = sim.Chapters;
            Place(sim, 2);
            sim.Tick(1f);
            Assert.IsTrue(ch.StartClimaxNow(), "[바로 맞이하기]");
            Assert.AreEqual(ChapterPhase.Climax, ch.Phase);
            Assert.IsTrue(sim.Events.Held, "절정 동안 무작위 이벤트 멈춤");

            ChapterRecord ended = null;
            ch.ClimaxEnded += r => ended = r;
            RunUntil(sim, () => ch.Phase != ChapterPhase.Climax);
            Assert.AreEqual(ChapterPhase.Transition, ch.Phase);
            Assert.IsNotNull(ended);
            Assert.Greater(sim.Damage.DamagedCount, 0, "운석 연타가 파손을 냄");
            Assert.AreEqual(ChapterOutcome.Hurt, ended.Outcome, "깔끔 기준(파손 0)을 넘고 파괴 없음 → 피해를 입고 버팀");
            StringAssert.Contains("파괴 0개", ended.Summary);

            Assert.IsFalse(ch.Choose(-1), "선택지가 있으면 골라야 함");
            Assert.IsFalse(ch.Choose(2));
            int started = -1;
            ch.ChapterStarted += i => started = i;
            Assert.IsTrue(ch.Choose(1));
            Assert.AreEqual("b", ch.Records[0].Choice);
            Assert.AreEqual(1, started);
            Assert.AreEqual(1, ch.Index);
            Assert.AreEqual(ChapterPhase.Preparation, ch.Phase);
            Assert.IsFalse(sim.Events.Held, "새 장에서 무작위 이벤트 다시");
            Assert.AreEqual(PlacementResult.Valid, sim.CheckBuildable(_battery), "2장 해금");
            Assert.AreEqual(PlacementResult.Valid, sim.CheckBuildable(_block), "지난 장 해금 유지");
            Assert.IsTrue(sim.RuleOpen(StationRule.MeteorShower, false));
            Assert.AreEqual(3, sim.EventScale.MeteorHitsMin, "이벤트 규모는 장이 정함");
            sim.Tick(1f);
            CollectionAssert.Contains(sim.ActiveNeeds, ResidentNeed.Medical, "장의 새 요구");

            // 2장: 등급 G1(모듈 4) 도달 → 예고 0초 → 절정 없음 → 전환 → 선택지 없음(-1) → 캠페인 끝
            bool finished = false;
            ch.CampaignFinished += () => finished = true;
            Place(sim, 1); // 코어 + 블록 3 = 모듈 4 → G1
            sim.Tick(1f);
            Assert.AreEqual(ChapterPhase.Transition, ch.Phase);
            Assert.AreEqual(ChapterOutcome.Clean, ch.LastRecord.Outcome);
            Assert.IsTrue(ch.Choose(-1));
            Assert.AreEqual(ChapterPhase.Finished, ch.Phase);
            Assert.IsTrue(finished);
            Assert.AreEqual(2, ch.Records.Count);
        }

        [Test]
        public void Save_RestoresChapterGoalsFlagsAndClimaxMidway()
        {
            var sim = Sim();
            var ch = sim.Chapters;
            ch.SetFlag("explore");
            Place(sim, 2);
            sim.Tick(1f);
            ch.StartClimaxNow();
            for (int i = 0; i < 3; i++)
                sim.Tick(1f); // 1파 뒤

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var copy = Sim();
            Assert.AreEqual(0, StationStateSerializer.Restore(copy, JsonUtility.FromJson<StationState>(json)));
            var cc = copy.Chapters;
            Assert.AreEqual(ChapterPhase.Climax, cc.Phase);
            Assert.IsTrue(cc.IsGoalDone(0));
            Assert.IsTrue(cc.IsGoalDone(1));
            Assert.IsTrue(cc.HasFlag("explore"));
            Assert.IsTrue(copy.Events.Held, "복원한 절정도 무작위 이벤트 멈춤");
            Assert.AreEqual(sim.Supply.ResearchPoints, copy.Supply.ResearchPoints, "보상을 다시 주지 않음");
            Assert.AreEqual(ch.Climax.Status, cc.Climax.Status, "절정 진행(파 수 · 파손 수) 이어짐");

            RunUntil(sim, () => ch.Phase != ChapterPhase.Climax);
            RunUntil(copy, () => cc.Phase != ChapterPhase.Climax);
            Assert.AreEqual(ch.LastRecord.Outcome, cc.LastRecord.Outcome);

            var json2 = JsonUtility.ToJson(StationStateSerializer.Capture(copy));
            var again = Sim();
            StationStateSerializer.Restore(again, JsonUtility.FromJson<StationState>(json2));
            Assert.AreEqual(ChapterPhase.Transition, again.Chapters.Phase, "전환 중 저장 → 방향 선택부터");
            Assert.AreEqual(1, again.Chapters.Records.Count);
            Assert.IsTrue(again.Chapters.Choose(0));
            Assert.AreEqual(1, again.Chapters.Index);
        }

        [Test]
        public void GradeGoal_AppliesGradeUpNeedsAndScale_AtTheMomentGradeRises_KeptAfterSaveAndNextChapter()
        {
            // 1장: 등급 G1(모듈 4) 도달이 필수 → 그 순간 의료 요구 + 운석 5개 / 2장: 장 시작 요구 없음
            var ch1 = Chapter("1장", new[] { _block }, StationRule.None, 1, choices: new[] { "a" });
            var so1 = new SerializedObject(ch1);
            so1.FindProperty("_goals").arraySize = 1;
            Goal(so1.FindProperty("_goals").GetArrayElementAtIndex(0), ChapterGoalKind.ReachGrade, grade: 1);
            so1.FindProperty("_gradeUpNeeds").arraySize = 1;
            so1.FindProperty("_gradeUpNeeds").GetArrayElementAtIndex(0).enumValueIndex = (int)ResidentNeed.Medical;
            so1.FindProperty("_gradeUpScale").boolValue = true;
            so1.FindProperty("_gradeUpMeteorHitsMin").intValue = 5;
            so1.FindProperty("_gradeUpMeteorHitsMax").intValue = 5;
            Climax(so1.FindProperty("_climax"), ChapterClimaxKind.None, warning: 100f);
            so1.ApplyModifiedPropertiesWithoutUndo();
            var ch2 = Chapter("2장", new ModuleData[0], StationRule.None, 2);
            var config = Create<ChapterConfig>();
            var c = new SerializedObject(config);
            c.FindProperty("_chapters").arraySize = 2;
            c.FindProperty("_chapters").GetArrayElementAtIndex(0).objectReferenceValue = ch1;
            c.FindProperty("_chapters").GetArrayElementAtIndex(1).objectReferenceValue = ch2;
            c.ApplyModifiedPropertiesWithoutUndo();
            _chapters = config;

            var sim = Sim();
            Place(sim, 2); // 코어 + 2 = 3 (G1 전)
            sim.Tick(1f);
            Assert.IsFalse(sim.Chapters.GradeUpApplied);
            CollectionAssert.DoesNotContain(sim.ActiveNeeds, ResidentNeed.Medical, "등급이 오르기 전엔 요구 없음");
            Assert.AreEqual(1, sim.EventScale.MeteorHitsMin);

            Place(sim, 1); // 4 → G1
            sim.Tick(1f);
            Assert.IsTrue(sim.Chapters.GradeUpApplied);
            Assert.AreEqual(5, sim.EventScale.MeteorHitsMin, "이벤트 규모는 등급이 오르는 순간");
            sim.Tick(1f); // 요구 충족 단계는 틱 순서상 장 단계보다 앞 → 요구는 다음 틱(1초 뒤)부터 (공통 바닥 틱 순서는 그대로)
            CollectionAssert.Contains(sim.ActiveNeeds, ResidentNeed.Medical, "등급이 오른 다음 틱에 요구");
            Assert.AreEqual(ChapterPhase.Warning, sim.Chapters.Phase, "같은 순간 절정 예고");

            var copy = Sim();
            StationStateSerializer.Restore(copy, JsonUtility.FromJson<StationState>(JsonUtility.ToJson(StationStateSerializer.Capture(sim))));
            Assert.IsTrue(copy.Chapters.GradeUpApplied, "저장 · 복원 유지");
            CollectionAssert.Contains(copy.ActiveNeeds, ResidentNeed.Medical);
            Assert.AreEqual(5, copy.EventScale.MeteorHitsMin);

            sim.Chapters.StartClimaxNow();
            Assert.IsTrue(sim.Chapters.Choose(0));
            sim.Tick(1f);
            Assert.AreEqual(1, sim.Chapters.Index);
            Assert.IsFalse(sim.Chapters.GradeUpApplied, "새 장은 등급 목표 전부터");
            CollectionAssert.Contains(sim.ActiveNeeds, ResidentNeed.Medical, "지난 장 등급 목표의 요구는 다음 장에도 유지");
            Assert.AreEqual(2, sim.EventScale.MeteorHitsMin, "새 장 시작 규모");
        }

        [Test]
        public void WithoutChapters_GradeRulesUnchanged()
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData> { _meteor },
                Random01 = () => 0.5f,
            });
            Assert.IsNull(sim.Chapters);
            Assert.AreSame(sim.Progression.Current, sim.EventScale);
            Assert.IsTrue(sim.RuleOpen(StationRule.MeteorShower, true));
            Assert.IsFalse(sim.RuleOpen(StationRule.MeteorShower, false));
        }

        // ---------------- 도우미 ----------------

        private int _nextX = 1;

        private void Place(StationSimulation sim, int count)
        {
            for (int i = 0; i < count; i++)
                Assert.IsTrue(sim.TryPlace(_block, new Vector3Int(_nextX++, 0, 0), 0, out _));
        }

        private static void RunUntil(StationSimulation sim, System.Func<bool> done)
        {
            for (int i = 0; i < 500 && !done(); i++)
                sim.Tick(1f);
            Assert.IsTrue(done(), "시간 안에 끝나지 않음");
        }

        private StationSimulation Sim()
        {
            return new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData> { _meteor },
                Random01 = () => 0.5f,
                Chapters = _chapters,
            });
        }

        private ChapterData Chapter(string name, ModuleData[] unlocks, StationRule rules, int hits, string[] choices = null)
        {
            var c = Create<ChapterData>();
            c.name = "CH_" + name;
            var so = new SerializedObject(c);
            so.FindProperty("_displayName").stringValue = name;
            so.FindProperty("_rules").intValue = (int)rules;
            so.FindProperty("_meteorHitsMin").intValue = hits;
            so.FindProperty("_meteorHitsMax").intValue = hits;
            var list = so.FindProperty("_unlocks");
            list.arraySize = unlocks.Length;
            for (int i = 0; i < unlocks.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = unlocks[i];
            choices = choices ?? new string[0];
            var ch = so.FindProperty("_choices");
            ch.arraySize = choices.Length;
            for (int i = 0; i < choices.Length; i++)
            {
                ch.GetArrayElementAtIndex(i).FindPropertyRelative("_id").stringValue = choices[i];
                ch.GetArrayElementAtIndex(i).FindPropertyRelative("_title").stringValue = choices[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        private static void Goal(SerializedProperty g, ChapterGoalKind kind, bool optional = false, ModuleData module = null, int count = 1,
            int grade = 0, string flag = "")
        {
            g.FindPropertyRelative("_kind").enumValueIndex = (int)kind;
            g.FindPropertyRelative("_optional").boolValue = optional;
            g.FindPropertyRelative("_module").objectReferenceValue = module;
            g.FindPropertyRelative("_count").intValue = count;
            g.FindPropertyRelative("_grade").intValue = grade;
            g.FindPropertyRelative("_flag").stringValue = flag;
        }

        private static void Climax(SerializedProperty c, ChapterClimaxKind kind, float warning)
        {
            c.FindPropertyRelative("_kind").enumValueIndex = (int)kind;
            c.FindPropertyRelative("_warningSeconds").floatValue = warning;
            c.FindPropertyRelative("_waves").intValue = 2;
            c.FindPropertyRelative("_waveInterval").floatValue = 10f;
            c.FindPropertyRelative("_hitsPerWave").intValue = 1;
            c.FindPropertyRelative("_settleSeconds").floatValue = 5f;
            c.FindPropertyRelative("_cleanMaxDamaged").intValue = 0;
            c.FindPropertyRelative("_hurtMaxDestroyed").intValue = 0;
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
