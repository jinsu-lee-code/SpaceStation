using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceStation.Tests
{
    /// <summary>
    /// 14-4 6장 사회 사건: 사건은 늘 오고 긴장도 단계가 대가 · 고를 수 있는 답을 바꿈 · '곧 터질 듯'이면 소란 한 번 · 위기 예고가 먼저면 남은 사건을 간격을 두고 ·
    /// 청원은 앞선 결정을 근거로 · 물음의 시간 판정 · 치안 · 달램 · 저장 · 6장 데이터.
    /// </summary>
    public class SocialEventTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core;
        private ResidentConfig _residents;
        private SpeciesData _rabbit;
        private ArkShipConfig _ark;
        private SocietyConfig _society;
        private CoreConfig _coreConfig;
        private PolicyConfig _policies;
        private SocialEventConfig _events;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 20;
            b.FindProperty("_startingSatisfaction").floatValue = 80f;
            b.FindProperty("_growthMinSatisfaction").floatValue = 100f; // 자연 증가 없음
            b.FindProperty("_overcrowdedLossInterval").floatValue = 100000f;
            Fill(b.FindProperty("_startingResources"), new ResourceAmount(ResourceType.Oxygen, 500), new ResourceAmount(ResourceType.Water, 500), new ResourceAmount(ResourceType.Food, 500), new ResourceAmount(ResourceType.Metal, 1000));
            Fill(b.FindProperty("_baseStorageCapacity"), new ResourceAmount(ResourceType.Oxygen, 1000), new ResourceAmount(ResourceType.Water, 1000), new ResourceAmount(ResourceType.Food, 1000), new ResourceAmount(ResourceType.Metal, 1000));
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Create<ModuleData>();
            _core.name = "MD_TestCore";
            var c = new SerializedObject(_core);
            c.FindProperty("_removable").boolValue = false;
            c.FindProperty("_housingCapacity").intValue = 100;
            var offsets = c.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            c.ApplyModifiedPropertiesWithoutUndo();

            _residents = Create<ResidentConfig>();
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();

            _rabbit = Create<SpeciesData>();
            _rabbit.EditorSet("rabbit", "토끼", "", ResidentNeed.Medical, 0.5f, 0.5f, ResidentTrait.Gardener, 0.02f, 0.1f, 0f, 0f);
            _ark = Create<ArkShipConfig>();
            _ark.EditorSet(_rabbit, new List<SpeciesData> { _rabbit }, new List<AxisShift>(), new List<AxisShift>(), new List<AxisShift>());
            _society = Create<SocietyConfig>();
            _coreConfig = Create<CoreConfig>();
            _policies = Create<PolicyConfig>();

            _events = Create<SocialEventConfig>();
            var care = new AxisShift(CoreAxis.Care, CorePole.Second, ShiftSize.Small);
            var cold = new AxisShift(CoreAxis.Care, CorePole.First, ShiftSize.Small);
            var keep = new AxisShift(CoreAxis.Scope, CorePole.First, ShiftSize.Small);
            SocialAnswer[] Answers() => new[]
            {
                new SocialAnswer("돌본다", "", -6f, "돌봤다", care).WithCost(new ResourceAmount(ResourceType.Metal, 100f)),
                new SocialAnswer("계산한다", "", 4f, "계산했다", cold).LockedFrom(3, "듣지 않음"),
                new SocialAnswer("지킨다", "", 2f, "지켰다", keep).WithLeave(1, 0f),
            };
            _events.EditorSet(
                new List<SocialEvent>
                {
                    new SocialEvent("first", 1, "첫 사건", "{a} 무리 {n}명", 5f, SocialEventSubject.MostDiscontent, Answers()),
                    new SocialEvent("second", 1, "둘째 사건", "둘째", 50f, SocialEventSubject.None, Answers()),
                },
                new SocialEvent("riot", 1, "소란", "소란", 0f, SocialEventSubject.None, Answers()),
                new List<SocialPetition>
                {
                    new SocialPetition("departed", "rabbit", PetitionCause.Departed, "떠난 이 {n}명"),
                    new SocialPetition("plain", "rabbit", PetitionCause.Always, "왜 정했나요"),
                    new SocialPetition("plain2", "rabbit", PetitionCause.Always, "어디로 가나요"),
                },
                new List<SocialAnswer>
                {
                    new SocialAnswer("들어준다", "", -8f, "", care).WithCost(new ResourceAmount(ResourceType.Metal, 10f)),
                    new SocialAnswer("미룬다", "", 6f, "", keep).WithLeave(2, 0f),
                });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void ScheduledEvent_WeightFollowsTension_AnswerAppliesCostTensionAxis()
        {
            var sim = Sim(Chapter(hold: true));
            var social = sim.GetSystem<SocialEventSystem>();
            var tension = sim.GetSystem<TensionSystem>();
            Tick(sim, 4);
            Assert.IsNull(social.Pending, "시간 전에는 없음");
            Tick(sim, 2);
            var p = social.Pending;
            Assert.IsNotNull(p, "장 시간이 되면 옴");
            StringAssert.Contains("토끼 무리 1명", p.Body, "대상 · 인원을 문구에");
            Assert.AreEqual(0, p.Level, "잔잔함");
            Assert.AreEqual(0.5f, p.Weight, Eps, "잔잔함이면 대가 절반");
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            tension.Adjust(10f);
            Assert.IsTrue(social.Answer(0));
            Assert.IsNull(social.Pending);
            Assert.AreEqual(metal - 50f, sim.Resources.GetStock(ResourceType.Metal), Eps, "대가 × 0.5");
            Assert.AreEqual(4f, tension.Value, Eps, "내리는 양에는 무게를 곱하지 않음");
            Assert.AreEqual(5f, sim.GetSystem<CorePersonalitySystem>().Value(CoreAxis.Care), Eps, "연민 쪽 작게");
        }

        [Test]
        public void CriticalTension_RiotOnce_LocksCalculatingAnswer_HeavierCost()
        {
            var sim = Sim(Chapter(hold: true));
            var social = sim.GetSystem<SocialEventSystem>();
            sim.GetSystem<TensionSystem>().Adjust(80f);
            sim.Tick(1f);
            var p = social.Pending;
            Assert.AreEqual("riot", p?.Id, "'곧 터질 듯'이면 소란");
            Assert.AreEqual(1.5f, p.Weight, Eps);
            Assert.IsFalse(social.CanAnswer(1, out string reason), "계산하는 답은 막힘");
            Assert.AreEqual("듣지 않음", reason);
            Assert.AreEqual(150f, social.CostOf(social.Answers(p)[0])[0].Amount, Eps, "대가 × 1.5");
            Assert.IsTrue(social.Answer(2));
            Assert.AreEqual(2, 20 - sim.Resources.Population, "떠남도 × 1.5 (올림)");
            sim.Tick(1f);
            Assert.IsNull(social.Pending, "소란은 장마다 한 번");
        }

        [Test]
        public void WarningStarted_SendsRemainingEventsWithGap()
        {
            var sim = Sim(Chapter(hold: false, warning: 300f));
            var social = sim.GetSystem<SocialEventSystem>();
            sim.Chapters.SetFlag("go");
            sim.Tick(1f);
            Assert.AreEqual(ChapterPhase.Warning, sim.Chapters.Phase);
            Tick(sim, 5);
            Assert.AreEqual("first", social.Pending?.Id);
            social.Answer(2);
            Tick(sim, 30);
            Assert.IsNull(social.Pending, "간격 전");
            Tick(sim, 20);
            Assert.AreEqual("second", social.Pending?.Id, "둘째 사건 시간 전이라도 예고 중이면 간격 뒤에");
        }

        [Test]
        public void Surveillance_LowersWeightLevel()
        {
            var sim = Sim(Chapter(hold: true));
            var social = sim.GetSystem<SocialEventSystem>();
            sim.GetSystem<TensionSystem>().Adjust(55f);
            Assert.AreEqual(2, social.EffectiveLevel);
            sim.Policies.AddTemporary("감시", 100f, new PolicyModifier { Stat = PolicyStat.CrisisThreshold, Value = -10f });
            Assert.AreEqual(45f, social.EffectiveTension, Eps);
            Assert.AreEqual(1, social.EffectiveLevel, "감시 강화 = 단계를 계산할 때 긴장도 -10");
        }

        [Test]
        public void Inquiry_PetitionsUsePastDecisions_CalmEnding_IsClean()
        {
            var sim = Sim(Chapter(hold: false, kind: ChapterClimaxKind.Inquiry));
            var social = sim.GetSystem<SocialEventSystem>();
            social.Restore("{\"DepartedTotal\":3}", null); // 앞 장에서 3명 떠남
            sim.Chapters.SetFlag("go");
            Tick(sim, 2);
            Assert.AreEqual(ChapterPhase.Climax, sim.Chapters.Phase);
            Tick(sim, 2);
            var p = social.Pending;
            Assert.IsTrue(p != null && p.Petition, "첫 청원");
            Assert.AreEqual("departed", p.Id, "근거가 맞는 청원 먼저");
            StringAssert.Contains("떠난 이 3명", p.Body);
            Assert.AreEqual("토끼", p.Speaker);
            StringAssert.Contains("물음 1/2", p.Title);
            Assert.IsTrue(social.Answer(0));
            Tick(sim, 3);
            p = social.Pending;
            Assert.AreEqual("plain", p?.Id, "맞는 근거가 없으면 일반 청원");
            Assert.IsTrue(social.Answer(0));
            Tick(sim, 20);
            Assert.AreEqual(ChapterPhase.Transition, sim.Chapters.Phase, "시간이 지나고 다 답하면 판정");
            Assert.AreEqual(ChapterOutcome.Clean, sim.Chapters.LastRecord.Outcome);
        }

        [Test]
        public void Inquiry_HighTension_ResidentsLeave_EndsLost()
        {
            var sim = Sim(Chapter(hold: false, kind: ChapterClimaxKind.Inquiry));
            var social = sim.GetSystem<SocialEventSystem>();
            sim.GetSystem<TensionSystem>().Adjust(100f);
            sim.Chapters.SetFlag("go");
            int population = sim.Resources.Population;
            for (int i = 0; i < 40 && sim.Chapters.Phase != ChapterPhase.Transition; i++)
            {
                sim.Tick(1f);
                if (social.Pending != null)
                    Assert.IsTrue(social.Answer(social.Answers(social.Pending).Count - 1), "마지막 답은 늘 고를 수 있음");
            }
            Assert.AreEqual(ChapterPhase.Transition, sim.Chapters.Phase);
            Assert.Less(sim.Resources.Population, population, "'곧 터질 듯'이면 떠남");
            Assert.AreEqual(ChapterOutcome.Lost, sim.Chapters.LastRecord.Outcome, "끝 긴장도가 75 이상");
            StringAssert.Contains("끝 긴장도", sim.Chapters.LastRecord.Summary);
        }

        [Test]
        public void Tension_SecurityUnmet_Rises_CalmHoldFlag_CalmRate()
        {
            var sim = Sim(Chapter(hold: true, needs: new[] { ResidentNeed.Security }));
            var tension = sim.GetSystem<TensionSystem>();
            sim.Tick(1f);
            Assert.AreEqual(4f * 0.85f, tension.RatePerMinute(TensionSystem.Source.Security), Eps, "치안 0% = 분당 4 × (1 − 너그러움)");
            Assert.IsTrue(tension.TopRisingCause(out var cause));
            Assert.AreEqual(TensionSystem.Source.Security, cause);
            Assert.AreEqual(2f, tension.CalmPerMinute, Eps);
            sim.Effects.TraitCalmMultiplier = 1.1f;
            sim.Effects.SetLinked(new[] { new ResearchModifier { Stat = ResearchStat.TensionCalmBonus, Value = 1f } });
            Assert.AreEqual(3.2f, tension.CalmPerMinute, Eps, "달램 × 1.1 + 이웃 모임 1");

            var calm = Sim(Chapter(hold: true));
            Tick(calm, 59);
            Assert.IsFalse(calm.Chapters.HasFlag(TensionSystem.CalmGoalFlag));
            Tick(calm, 2);
            Assert.IsTrue(calm.Chapters.HasFlag(ChapterSystem.ScopedFlag(TensionSystem.CalmGoalFlag, 1)), "잔잔함으로 1분");
        }

        [Test]
        public void SaveRestore_KeepsPendingAndFired()
        {
            var sim = Sim(Chapter(hold: true));
            var social = sim.GetSystem<SocialEventSystem>();
            Tick(sim, 6);
            string state = social.Capture(null);

            var again = Sim(Chapter(hold: true));
            var restored = again.GetSystem<SocialEventSystem>();
            restored.Restore(state, null);
            Assert.AreEqual("first", restored.Pending?.Id);
            Assert.IsTrue(restored.Answer(2));
            Tick(again, 6);
            Assert.IsNull(restored.Pending, "이미 온 사건은 다시 오지 않음");
        }

        [Test]
        public void ChapterSixData_GoalsClimaxEventsLeanOnLeastUsedAxes()
        {
            var ch = AssetDatabase.LoadAssetAtPath<ChapterData>("Assets/_Project/Data/Chapters/CH_6.asset");
            Assert.IsNotNull(ch);
            Assert.AreEqual("갈라진 목소리", ch.Subtitle);
            Assert.AreEqual(5, ch.Goals.Count);
            Assert.AreEqual(ChapterClimaxKind.Inquiry, ch.Climax.Kind);
            Assert.AreEqual(6, ch.Recipes.Count);
            Assert.AreEqual(3, ch.PolicySlots);
            Assert.AreEqual(4, ch.Policies.Count);
            Assert.AreEqual("dog", ch.NewSpecies.Id);
            Assert.Contains(ResidentNeed.Security, new List<ResidentNeed>(ch.GradeUpNeeds));
            Assert.AreEqual(2, ch.Choices.Count);
            Assert.AreEqual(CoreAxis.Care, ch.Choices[0].Shifts[0].Axis);
            Assert.AreEqual(CorePole.First, ch.Choices[0].Shifts[0].Toward, "방향 = 효율 크게 (축 점검)");

            var society = AssetDatabase.LoadAssetAtPath<SocietyConfig>("Assets/_Project/Data/Chapters/SocietyConfig.asset");
            Assert.IsNotNull(society.Events);
            Assert.AreEqual(3, society.Events.Events.Count);
            foreach (var e in society.Events.Events)
            {
                bool efficient = false, preserve = false;
                foreach (var a in e.Answers)
                    foreach (var s in a.Shifts)
                    {
                        efficient |= s.Axis == CoreAxis.Care && s.Toward == CorePole.First;
                        preserve |= s.Axis == CoreAxis.Scope && s.Toward == CorePole.First;
                    }
                Assert.IsTrue(efficient && preserve, e.Id + ": 효율 · 보존 쪽 답을 꼭 하나씩");
            }
            Assert.GreaterOrEqual(society.Events.Petitions.Count, 4);
        }

        // ---------------- 도우미 ----------------

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
                Residents = _residents,
                ResidentRandom01 = () => 0.5f,
                Policies = _policies,
                Chapters = chapters,
            });
            sim.AddSystem(new CorePersonalitySystem(_coreConfig), TickStepNames.Progression);
            sim.AddSystem(new ArkShipSystem(_ark, () => 0f), TickStepNames.Population);
            sim.AddSystem(new ChapterPolicySystem(_society), TickStepNames.Population);
            sim.AddSystem(new TensionSystem(_society), TickStepNames.Population);
            sim.AddSystem(new SocialEventSystem(_society, _events), TickStepNames.Population);
            return sim;
        }

        private ChapterConfig Chapter(bool hold, ChapterClimaxKind kind = ChapterClimaxKind.None, float warning = 1f, ResidentNeed[] needs = null)
        {
            var c = Create<ChapterData>();
            c.name = "CH_Test";
            var so = new SerializedObject(c);
            so.FindProperty("_displayName").stringValue = "시험 장";
            var goals = so.FindProperty("_goals");
            goals.arraySize = 1;
            var first = goals.GetArrayElementAtIndex(0);
            if (hold)
            {
                first.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.SurviveSeconds;
                first.FindPropertyRelative("_amount").floatValue = 100000f;
            }
            else
            {
                first.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Flag;
                first.FindPropertyRelative("_flag").stringValue = "go";
            }
            if (needs != null)
            {
                var list = so.FindProperty("_newNeeds");
                list.arraySize = needs.Length;
                for (int i = 0; i < needs.Length; i++)
                    list.GetArrayElementAtIndex(i).intValue = (int)needs[i];
            }
            var climax = so.FindProperty("_climax");
            climax.FindPropertyRelative("_kind").enumValueIndex = (int)kind;
            climax.FindPropertyRelative("_title").stringValue = "물음";
            climax.FindPropertyRelative("_warningSeconds").floatValue = warning;
            climax.FindPropertyRelative("_inquirySeconds").floatValue = 12f;
            climax.FindPropertyRelative("_inquiryPetitions").intValue = 2;
            climax.FindPropertyRelative("_inquiryFirstLead").floatValue = 1f;
            climax.FindPropertyRelative("_inquiryInterval").floatValue = 2f;
            climax.FindPropertyRelative("_inquiryLeaveInterval").floatValue = 3f;
            climax.FindPropertyRelative("_inquiryLeaveTense").intValue = 1;
            climax.FindPropertyRelative("_inquiryLeaveCritical").intValue = 2;
            climax.FindPropertyRelative("_cleanMaxDeparted").intValue = 3;
            climax.FindPropertyRelative("_cleanMaxTension").floatValue = 50f;
            climax.FindPropertyRelative("_cleanMaxShortageSeconds").floatValue = 0f;
            climax.FindPropertyRelative("_hurtMaxDeparted").intValue = 4;
            climax.FindPropertyRelative("_hurtMaxTension").floatValue = 75f;
            climax.FindPropertyRelative("_hurtMaxShortageSeconds").floatValue = 60f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var config = Create<ChapterConfig>();
            var cso = new SerializedObject(config);
            cso.FindProperty("_chapters").arraySize = 1;
            cso.FindProperty("_chapters").GetArrayElementAtIndex(0).objectReferenceValue = c;
            cso.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        private static void Tick(StationSimulation sim, int seconds)
        {
            for (int i = 0; i < seconds; i++)
                sim.Tick(1f);
        }

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
