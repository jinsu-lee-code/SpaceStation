using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceStation.Tests
{
    /// <summary>13-5b 기록 복원: 2장부터 열림 · 봉인 단계 = 기록 조각 · 마치면 효과 대기 → 보존 / 덮어쓰기(× 1.5) · 성격 축 · 세이브 · 연계 연구 칸은 합성 규칙부터 (13-5c).</summary>
    public class RecordTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _lab;
        private ResearchCategoryData _survival;
        private RecordConfig _records;
        private CoreConfig _coreConfig;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 500));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 1000), R(ResourceType.Water, 1000), R(ResourceType.Food, 1000), R(ResourceType.Metal, 1000));
            b.FindProperty("_destroyAfterSeconds").floatValue = 6000f;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.ApplyModifiedPropertiesWithoutUndo();
            _core = Module("MD_TestCore", false, 0);
            _lab = Module("MD_TestLab", true, 1);

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();

            _survival = Create<ResearchCategoryData>();
            _survival.name = "RC_TestSurvival";
            _survival.EditorSet(ResearchCategory.Production, "생존 기록", "event", new List<ResearchLevel>
            {
                Level(ResearchStat.LifeSupportProductionMultiplier, 1.1f),
                Level(ResearchStat.SolarMultiplier, 1.15f),
                Level(ResearchStat.LifeSupportProductionMultiplier, 1.2f),
            });
            _records = Create<RecordConfig>();
            _records.EditorSet(new List<RecordTrack>
                {
                    new RecordTrack(_survival, new List<string> { "온실", "태양", "종자 은행" }, "씨앗보다 먼저",
                        new List<ResearchModifier> { new ResearchModifier { Stat = ResearchStat.LifeSupportProductionMultiplier, Value = 1.3f } }, "생산 +30%"),
                },
                new List<AxisShift> { new AxisShift(CoreAxis.Memory, CorePole.First, ShiftSize.Small) },
                new List<AxisShift> { new AxisShift(CoreAxis.Memory, CorePole.First, ShiftSize.Medium) },
                new List<AxisShift> { new AxisShift(CoreAxis.Memory, CorePole.Second, ShiftSize.Medium) });
            OpenChapter(0);
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
        public void Closed_UntilOpenChapter()
        {
            OpenChapter(1); // 2장부터 — 시험 판은 1장
            var sim = Sim(out var records, out _);
            sim.Grid.TryPlace(_lab, Vector3Int.right, 0, out _);
            Assert.IsFalse(sim.Research.IsOpen, "[관리]에서 숨김");
            Assert.AreEqual(ResearchStartResult.Gated, sim.CanStartResearch(_survival));
            StringAssert.Contains("2장", records.BlockReason(_survival, 1));
            Assert.AreEqual("기록 복원", sim.Research.Gate.Title);
            OpenChapter(0);
            Assert.IsTrue(sim.Research.IsOpen);
            Assert.AreEqual(ResearchStartResult.Ok, sim.CanStartResearch(_survival));
        }

        [Test]
        public void AutoPreserve_LevelOne_LogsMemory_ShiftsTowardMemory()
        {
            var sim = Sim(out _, out var core);
            sim.Grid.TryPlace(_lab, Vector3Int.right, 0, out _);
            Assert.AreEqual(ResearchStartResult.Ok, sim.TryStartResearch(_survival));
            Tick(sim, 11);
            Assert.AreEqual(1, sim.Research.GetLevel(_survival));
            Assert.AreEqual(1.1f, sim.Effects.LifeSupportProductionMultiplier, Eps, "1단계는 바로 효과");
            StringAssert.Contains("온실", core.Entries.Last().Text, "되살린 기억 한 줄");
            Assert.Less(core.Value(CoreAxis.Memory), 0f, "기억 쪽 (앞쪽 극 = 음수)");
        }

        [Test]
        public void Sealed_NeedsFragment_PendingThenOverwrite_Boosted()
        {
            var sim = Sim(out var records, out var core);
            sim.Grid.TryPlace(_lab, Vector3Int.right, 0, out _);
            sim.Research.SetLevel(_survival, 2);
            Assert.AreEqual(ResearchStartResult.Gated, sim.CanStartResearch(_survival), "봉인 = 조각 필요");
            StringAssert.Contains("기록 조각", records.BlockReason(_survival, 3));
            records.AddFragments(1);
            Assert.AreEqual(ResearchStartResult.Ok, sim.TryStartResearch(_survival));
            Assert.AreEqual(0, records.Fragments, "시작할 때 조각을 씀");
            Tick(sim, 11);
            Assert.AreEqual(3, sim.Research.GetLevel(_survival));
            Assert.AreEqual(records.Config.Tracks[0], records.Pending, "고를 차례");
            Assert.AreEqual(1.1f, sim.Effects.LifeSupportProductionMultiplier, Eps, "고를 때까지 봉인 단계 효과 대기 (1단계 값)");

            float memory = core.Value(CoreAxis.Memory);
            Assert.IsTrue(records.Choose(records.Pending, preserve: false));
            Assert.AreEqual(1.3f, sim.Effects.LifeSupportProductionMultiplier, Eps, "덮어쓰기 = × 1.5");
            Assert.Greater(core.Value(CoreAxis.Memory), memory, "망각 쪽");
            Assert.IsNull(records.Pending);
            Assert.IsFalse(records.Choose(records.Config.Tracks[0], true), "한 번만");
        }

        [Test]
        public void Sealed_Preserve_LogsHuman_SurvivesSave()
        {
            var sim = Sim(out var records, out var core);
            sim.Research.SetLevel(_survival, 3);
            records.AddFragments(2);
            Assert.IsTrue(records.Choose(records.Pending, preserve: true));
            Assert.AreEqual(1.2f, sim.Effects.LifeSupportProductionMultiplier, Eps, "보존 = 레벨 표 그대로");
            Assert.IsTrue(core.Entries.Any(e => e.Text.Contains("씨앗보다 먼저")), "인간 미스터리 한 줄");
            Assert.Less(core.Value(CoreAxis.Memory), 0f, "기억 쪽");

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var loaded = Sim(out var records2, out _);
            StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));
            Assert.AreEqual(2, records2.Fragments);
            Assert.AreEqual(RecordChoice.Preserve, records2.ChoiceOf(records2.Config.Tracks[0]));
            Assert.IsNull(records2.Pending);
            Assert.AreEqual(1.2f, loaded.Effects.LifeSupportProductionMultiplier, Eps, "고른 효과가 돌아옴");
        }

        [Test]
        public void CleanClimax_GivesChapterFragments()
        {
            var sim = Sim(out var records, out _);
            records.RewardClimax(new ChapterRecord { Chapter = 0, Outcome = ChapterOutcome.Hurt });
            Assert.AreEqual(0, records.Fragments, "피해를 입으면 없음");
            records.RewardClimax(new ChapterRecord { Chapter = 0, Outcome = ChapterOutcome.Clean });
            Assert.AreEqual(1, records.Fragments, "깔끔히 버티면 장 데이터만큼");
        }

        [Test]
        public void LinkedResearch_HiddenUntilSynthesisRule()
        {
            var sim = Sim(out _, out _);
            Assert.IsFalse(sim.Research.ShowLinked, "합성 규칙 전(1~3장) = 연계 연구 칸 숨김");
            var so = new SerializedObject(sim.Chapters.Current);
            so.FindProperty("_rules").intValue = (int)StationRule.Synthesis;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(sim.Research.ShowLinked, "합성이 열리는 장(4장)부터 보임");
        }

        // ---------------- 도우미 ----------------

        private void OpenChapter(int index)
        {
            var so = new SerializedObject(_records);
            so.FindProperty("_openChapter").intValue = index;
            so.FindProperty("_sealedLevel").intValue = 3;
            so.FindProperty("_fragmentCost").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ResearchLevel Level(ResearchStat stat, float value)
        {
            var l = new ResearchLevel { PowerDemand = 0f, Duration = 10f, Description = stat.ToString() };
            l.StartCost.Add(new ResourceAmount(ResourceType.Metal, 10f));
            l.Modifiers.Add(new ResearchModifier { Stat = stat, Value = value });
            return l;
        }

        private static void Tick(StationSimulation sim, int seconds)
        {
            for (int i = 0; i < seconds; i++)
                sim.Tick(1f);
        }

        private StationSimulation Sim(out RecordArchiveSystem records, out CorePersonalitySystem core)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                Chapters = Chapters(),
                ResearchCategories = new List<ResearchCategoryData> { _survival },
            });
            core = sim.AddSystem(new CorePersonalitySystem(_coreConfig), TickStepNames.Progression);
            records = sim.AddSystem(new RecordArchiveSystem(_records), TickStepNames.Research);
            return sim;
        }

        private ChapterConfig Chapters()
        {
            var c = Create<ChapterData>();
            c.name = "CH_Test";
            var so = new SerializedObject(c);
            so.FindProperty("_displayName").stringValue = "시험 장";
            var goals = so.FindProperty("_goals");
            goals.arraySize = 1;
            goals.GetArrayElementAtIndex(0).FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.SurviveSeconds;
            goals.GetArrayElementAtIndex(0).FindPropertyRelative("_amount").floatValue = 100000f;
            so.FindProperty("_cleanFragments").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
            var cfg = Create<ChapterConfig>();
            var cso = new SerializedObject(cfg);
            cso.FindProperty("_chapters").arraySize = 1;
            cso.FindProperty("_chapters").GetArrayElementAtIndex(0).objectReferenceValue = c;
            cso.ApplyModifiedPropertiesWithoutUndo();
            return cfg;
        }

        private ModuleData Module(string name, bool removable, int researchSlots)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            so.FindProperty("_researchSlots").intValue = researchSlots;
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
