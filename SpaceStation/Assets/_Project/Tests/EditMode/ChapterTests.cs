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
            Assert.IsTrue(sim.Progression.IsRevealed(_block), "건설 메뉴도 장 해금을 따름 (등급 해금 목록에 없어도)");
            Assert.IsFalse(sim.Progression.IsRevealed(_battery));
            Assert.AreEqual(0, sim.Progression.UnlockOrder(_block), "건설 메뉴 정렬 = 열리는 장");
            Assert.AreEqual(1, sim.Progression.UnlockOrder(_battery));

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

        // ---------------- 13-2 코어 성격 ----------------

        private CoreConfig CoreCfg(params (string key, CorePole pole, CoreAxis axis, string text)[] lines)
        {
            var c = Create<CoreConfig>();
            var so = new SerializedObject(c);
            var list = so.FindProperty("_lines");
            list.arraySize = lines.Length;
            for (int i = 0; i < lines.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Key").stringValue = lines[i].key;
                e.FindPropertyRelative("Pole").enumValueIndex = (int)lines[i].pole;
                e.FindPropertyRelative("Axis").enumValueIndex = (int)lines[i].axis;
                e.FindPropertyRelative("Text").stringValue = lines[i].text;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        private CorePersonalitySystem AddCore(StationSimulation sim, CoreConfig config)
            => sim.AddSystem(new CorePersonalitySystem(config), TickStepNames.Progression);

        [Test]
        public void Core_ShiftsBySize_Clamps_DescribesInWords_PicksLineByStrongestLean()
        {
            var cfg = CoreCfg(
                ("hello", CorePole.Neutral, CoreAxis.Care, "기본 {0}"),
                ("hello", CorePole.Second, CoreAxis.Care, "연민 {0}"),
                ("hello", CorePole.First, CoreAxis.Control, "통제 {0}"));
            var sim = Sim();
            var core = AddCore(sim, cfg);
            Assert.AreEqual("기본 1", core.Say("hello", "x", 1), "기울지 않으면 기본 문장");
            Assert.AreEqual("없음", core.Say("missing", "없음"), "표에 없으면 기본값");
            Assert.AreEqual("균형", core.Describe(CoreAxis.Care));

            core.Apply(new[] { new AxisShift(CoreAxis.Care, CorePole.Second, ShiftSize.Medium) }, "시험"); // +10
            Assert.AreEqual(10f, core.Value(CoreAxis.Care));
            Assert.AreEqual(CorePole.Neutral, core.Lean(CoreAxis.Care), "문턱(15) 아래");
            core.Apply(new[] { new AxisShift(CoreAxis.Care, CorePole.Second, ShiftSize.Large) }, "시험"); // +20 → 30
            Assert.AreEqual("연민 쪽으로 조금", core.Describe(CoreAxis.Care));
            Assert.AreEqual("연민 2", core.Say("hello", "x", 2));

            core.Apply(new[] { new AxisShift(CoreAxis.Control, CorePole.First, ShiftSize.Large), new AxisShift(CoreAxis.Control, CorePole.First, ShiftSize.Large) }, "규칙");
            Assert.AreEqual(-40f, core.Value(CoreAxis.Control));
            Assert.AreEqual("통제 쪽으로 기울어짐", core.Describe(CoreAxis.Control));
            Assert.AreEqual("통제 3", core.Say("hello", "x", 3), "더 강하게 기운 축의 문장이 우선");

            for (int i = 0; i < 10; i++)
                core.Apply(new[] { new AxisShift(CoreAxis.Scope, CorePole.Second, ShiftSize.Large) }, "확장");
            Assert.AreEqual(100f, core.Value(CoreAxis.Scope), "최대 100");
            Assert.AreEqual("확장에 깊이 기울어짐", core.Describe(CoreAxis.Scope));
            Assert.AreEqual(14, core.Shifts.Count, "원인 기록");
            Assert.AreEqual("규칙", core.Shifts[2].Cause);
        }

        [Test]
        public void Core_ChapterChoiceShiftsAxes_LogsAndMonologue_SurvivesSave()
        {
            var ch1 = new SerializedObject(_chapters.Chapters[0]);
            var shifts = ch1.FindProperty("_choices").GetArrayElementAtIndex(1).FindPropertyRelative("_shifts"); // 선택지 "b"
            shifts.arraySize = 1;
            shifts.GetArrayElementAtIndex(0).FindPropertyRelative("Axis").enumValueIndex = (int)CoreAxis.Scope;
            shifts.GetArrayElementAtIndex(0).FindPropertyRelative("Toward").enumValueIndex = (int)CorePole.First;
            shifts.GetArrayElementAtIndex(0).FindPropertyRelative("Size").enumValueIndex = (int)ShiftSize.Large;
            ch1.ApplyModifiedPropertiesWithoutUndo();
            var cfg = CoreCfg(
                ("outcome.Hurt", CorePole.Neutral, CoreAxis.Care, "버텼다 · {0}"),
                ("choice.b", CorePole.Neutral, CoreAxis.Care, "b를 골랐다"),
                ("goal.done", CorePole.Neutral, CoreAxis.Care, "완료 {0}"));

            var sim = Sim();
            var core = AddCore(sim, cfg);
            Place(sim, 2);
            sim.Tick(1f);
            Assert.IsTrue(core.Entries.Any(e => e.Text.StartsWith("완료 ")), "상태 줄 알림도 코어 문장 표로 (로그에 남음)");
            Assert.IsTrue(core.Entries.Any(e => e.Text.StartsWith("위기가 다가옵니다")), "표에 없는 계기는 기본 문장으로 로그");
            sim.Chapters.StartClimaxNow();
            RunUntil(sim, () => sim.Chapters.Phase != ChapterPhase.Climax);
            Assert.AreEqual("버텼다 · 1장", sim.Chapters.LastRecord.Monologue, "절정 결과 독백");

            Assert.IsTrue(sim.Chapters.Choose(1));
            Assert.AreEqual(-20f, core.Value(CoreAxis.Scope), "방향 선택 → 보존 쪽 크게");
            Assert.AreEqual("1장 방향: b", core.Shifts[0].Cause);
            Assert.IsTrue(core.Entries.Any(e => e.Text == "b를 골랐다"));

            var copy = Sim();
            var copyCore = AddCore(copy, cfg);
            StationStateSerializer.Restore(copy, JsonUtility.FromJson<StationState>(JsonUtility.ToJson(StationStateSerializer.Capture(sim))));
            Assert.AreEqual(-20f, copyCore.Value(CoreAxis.Scope), "저장 · 복원");
            Assert.AreEqual(core.Entries.Count, copyCore.Entries.Count);
            Assert.AreEqual(core.Shifts.Count, copyCore.Shifts.Count);
            Assert.AreEqual("버텼다 · 1장", copy.Chapters.Records[0].Monologue);
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

        [Test]
        public void CampaignRun_ProgressLabel_RunIdSurvivesSave_CheckpointReadable()
        {
            var sim = Sim();
            var chapters = sim.Chapters;
            Assert.IsFalse(string.IsNullOrEmpty(chapters.RunId), "새 캠페인 = 새 판 번호 (13-7d 체크포인트 구분)");
            Assert.AreEqual(chapters.ProgressText(), sim.ProgressLabel(), "main M-44 진행 표시 = 장 이름");
            Assert.AreEqual(chapters.Current.DisplayName, sim.ProgressLabel(), "부제가 없으면 장 이름만");
            Assert.AreNotEqual(chapters.RunId, Sim().Chapters.RunId, "판마다 다름");

            string state = chapters.Capture(_ => -1);
            Assert.IsTrue(ChapterSystem.TryReadRun(state, out string run, out int index, out var phase));
            Assert.AreEqual(chapters.RunId, run);
            Assert.AreEqual(0, index);
            Assert.AreEqual(ChapterPhase.Preparation, phase);

            var loaded = Sim();
            loaded.Chapters.Restore(state, _ => null);
            Assert.AreEqual(chapters.RunId, loaded.Chapters.RunId, "불러오면 같은 판");
            Assert.IsFalse(ChapterSystem.TryReadRun("{\"Index\":1}", out _, out _, out _), "판 번호가 없는 이전 저장");
        }

        /// <summary>13-8 ③ 보완 (사용자 결정 2026-10-25): 불빛이 줄어드는 시간을 3으로 나눈 지점마다 한 번 코어 혼잣말("코어 · ") · 불러온 뒤 지난 단계를 다시 알리지 않음.</summary>
        [Test]
        public void EarthDimmed_NoticesPerStep_NotRepeatedAfterLoad()
        {
            var chapter = Chapter("E", new ModuleData[0], StationRule.None, 1);
            var so = new SerializedObject(chapter);
            so.FindProperty("_earthLightsStart").floatValue = 1f;
            so.FindProperty("_earthLightsEnd").floatValue = 0.9f;
            so.FindProperty("_earthDimSeconds").floatValue = 30f;
            so.FindProperty("_earthDimNotices").intValue = 3;
            var goals = so.FindProperty("_goals");
            goals.arraySize = 1;
            Goal(goals.GetArrayElementAtIndex(0), ChapterGoalKind.BuildModule, module: _block, count: 99); // 끝나지 않는 목표 (준비 단계 유지)
            so.ApplyModifiedPropertiesWithoutUndo();
            var config = Create<ChapterConfig>();
            var c = new SerializedObject(config);
            c.FindProperty("_chapters").arraySize = 1;
            c.FindProperty("_chapters").GetArrayElementAtIndex(0).objectReferenceValue = chapter;
            c.ApplyModifiedPropertiesWithoutUndo();
            _chapters = config;

            var sim = Sim();
            var steps = new List<int>();
            var lines = new List<string>();
            sim.Chapters.EarthDimmed += steps.Add;
            sim.EffectReported += (text, _) => { if (text.StartsWith("코어 · ")) lines.Add(text); };
            for (int i = 0; i < 15; i++)
                sim.Tick(1f);
            CollectionAssert.AreEqual(new[] { 1 }, steps, "10초마다 한 단계 (15초 = 1)");
            Assert.AreEqual("코어 · 저 아래 불빛이 또 꺼졌다.", lines[0]);

            var loaded = Sim();
            var loadedSteps = new List<int>();
            loaded.Chapters.EarthDimmed += loadedSteps.Add;
            loaded.Chapters.Restore(sim.Chapters.Capture(_ => -1), _ => null);
            for (int i = 0; i < 30; i++)
                loaded.Tick(1f);
            CollectionAssert.AreEqual(new[] { 2, 3 }, loadedSteps, "불러온 뒤 지난 단계(1)는 다시 알리지 않고, 3단계에서 멈춤");
        }

        [Test]
        public void CampaignAsset_ChapterFour_IsConsistent()
        {
            // 14-1 4장 '좁아진 하늘' 데이터 (메뉴 Chapters/Setup Later Chapters로 만든 에셋)
            var config = AssetDatabase.LoadAssetAtPath<ChapterConfig>("Assets/_Project/Data/Chapters/Chapters.asset");
            Assert.IsNotNull(config);
            Assert.GreaterOrEqual(config.Count, 4);
            var ch4 = config.Chapters[3];
            Assert.AreEqual("좁아진 하늘", ch4.Subtitle);
            Assert.AreEqual(StationRule.Synthesis, ch4.Rules & StationRule.Synthesis, "합성 규칙");
            Assert.AreEqual(6, ch4.Recipes.Count, "진화 1단계 6종");
            foreach (var r in ch4.Recipes)
            {
                Assert.IsNotNull(r);
                Assert.AreEqual(1, r.Stage, r.name + ": 1단계 (합성 규칙으로 열림)");
            }
            Assert.AreEqual("fox", ch4.NewSpecies != null ? ch4.NewSpecies.Id : null, "첫 배 = 여우");
            Assert.AreEqual(ResidentTrait.Artisan, ch4.NewSpecies.TechTrait, "여우 = 합성 속도");
            Assert.IsNotNull(ch4.Guide, "합성 안내");
            Assert.IsTrue(ch4.Guide.Free && ch4.Guide.Transient, "막지 않고 저장하지 않는 안내");
            int required = 0;
            foreach (var g in ch4.Goals)
                if (!g.Optional)
                    required++;
            Assert.AreEqual(3, required, "필수 3");
            Assert.AreEqual(5, ch4.Goals.Count, "필수 3 + 선택 2");
        }

        [Test]
        public void ChapterOpensRecipes_SynthesisGoals_ScopedFlags()
        {
            // 14-1c: 2장이 레시피를 연구 없이 엶 + 합성 규칙, 목표 = 합성 1번 · 진화 1종 가동(선택) · 2장 안에서 기록 표시(선택)
            var evolved = Module("MD_TestEvolved", 30f);
            var eso = new SerializedObject(evolved);
            var cells = eso.FindProperty("_cellOffsets");
            cells.arraySize = 2;
            cells.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            cells.GetArrayElementAtIndex(1).vector3IntValue = Vector3Int.right;
            eso.ApplyModifiedPropertiesWithoutUndo();
            var recipe = Create<RecipeData>();
            recipe.name = "RC_TestEvolved";
            var rso = new SerializedObject(recipe);
            rso.FindProperty("_result").objectReferenceValue = evolved;
            var ing = rso.FindProperty("_ingredients");
            ing.arraySize = 1;
            ing.GetArrayElementAtIndex(0).FindPropertyRelative("Module").objectReferenceValue = _block;
            ing.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 2;
            Fill(rso.FindProperty("_cost"), R(ResourceType.Metal, 10f));
            rso.FindProperty("_duration").floatValue = 2f;
            rso.FindProperty("_unlockedByDefault").boolValue = false;
            rso.ApplyModifiedPropertiesWithoutUndo();

            var so2 = new SerializedObject(_chapters.Chapters[1]);
            so2.FindProperty("_rules").intValue = (int)(StationRule.MeteorShower | StationRule.Synthesis);
            so2.FindProperty("_recipes").arraySize = 1;
            so2.FindProperty("_recipes").GetArrayElementAtIndex(0).objectReferenceValue = recipe;
            var goals = so2.FindProperty("_goals");
            goals.arraySize = 3;
            Goal(goals.GetArrayElementAtIndex(0), ChapterGoalKind.Synthesize);
            Goal(goals.GetArrayElementAtIndex(1), ChapterGoalKind.EvolvedKinds, optional: true);
            Goal(goals.GetArrayElementAtIndex(2), ChapterGoalKind.Flag, optional: true, flag: ChapterSystem.ScopedFlag("record", 2));
            so2.ApplyModifiedPropertiesWithoutUndo();

            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config, Grades = _grades, CoreModule = _core, Events = new List<GameEventData> { _meteor },
                Random01 = () => 0.5f, Chapters = _chapters, Recipes = new[] { recipe },
            });
            var ch = sim.Chapters;
            Assert.IsFalse(sim.Synthesis.IsUnlocked(recipe), "1장 = 아직 (규칙 · 레시피 모두 2장)");
            ch.SetFlag("record");
            Assert.IsTrue(ch.HasFlag(ChapterSystem.ScopedFlag("record", 1)), "장 안 표시도 함께");
            Place(sim, 2);
            sim.Tick(1f);
            ch.StartClimaxNow();
            RunUntil(sim, () => ch.Phase != ChapterPhase.Climax);
            Assert.IsTrue(ch.Choose(0));

            Assert.AreEqual(1, ch.Index);
            Assert.IsTrue(sim.Synthesis.IsUnlocked(recipe), "2장이 연구 없이 엶");
            Assert.AreEqual("진화 모듈 하나 만들기", ch.GoalText(0));
            Assert.AreEqual("진화 모듈 1종 가동", ch.GoalText(1));
            sim.Tick(1f);
            Assert.IsFalse(ch.IsGoalDone(2), "1장에서 올린 표시는 2장 안 표시가 아님");
            ch.SetFlag("record");
            Assert.IsTrue(ch.IsGoalDone(2), "2장 안에서 다시 올리면 채워짐");

            Place(sim, 2); // 새 블록 둘 (1장 위기에 파손됐을 수 있는 블록은 쓰지 않음)
            Assert.AreEqual(SynthesisResult.Valid, sim.Synthesis.TryStart(recipe, new Vector3Int(3, 0, 0), 0, out _));
            RunUntil(sim, () => sim.Synthesis.Jobs.Count == 0);
            sim.Tick(1f);
            Assert.IsTrue(ch.IsGoalDone(0), "합성 1번");
            Assert.IsTrue(ch.IsGoalDone(1), "진화 1종 가동");
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
