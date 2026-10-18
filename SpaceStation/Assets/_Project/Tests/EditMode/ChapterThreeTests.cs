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
    /// 13-6a 3장 정책 · 종족 반응 · 긴장도: 정책 규칙이 열린 장부터 1칸 → 등급 목표 때 2칸 · 장 정책만 고름 · 장 시간 ·
    /// 무리 비율로 통제형 감점 · 자율형 효과가 바뀜 · 긴장도(예보 뒤 피해 · 과밀 · 정책 반발 → 잔잔해짐 · 저장).
    /// </summary>
    public class ChapterThreeTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _block, _home;
        private ResidentConfig _residents;
        private SpeciesData _rabbit, _bear;
        private ArkShipConfig _ark;
        private SocietyConfig _society;
        private PolicyConfig _policyConfig;
        private PolicyData _rationing, _nightShift, _festival;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 4;
            b.FindProperty("_startingSatisfaction").floatValue = 80f;
            b.FindProperty("_growthMinSatisfaction").floatValue = 100f; // 자연 증가 없음
            b.FindProperty("_overcrowdedLossInterval").floatValue = 100000f;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 500), R(ResourceType.Water, 500), R(ResourceType.Food, 500), R(ResourceType.Metal, 1000));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 1000), R(ResourceType.Water, 1000), R(ResourceType.Food, 1000), R(ResourceType.Metal, 1000));
            b.FindProperty("_destroyAfterSeconds").floatValue = 600f;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_showerMinGrade").intValue = 99; // 평소 유성우는 장 규칙으로만 (여기선 없음)
            b.FindProperty("_showerIntervalMin").floatValue = 1000f;
            b.FindProperty("_showerIntervalMax").floatValue = 1000f;
            b.FindProperty("_showerWarningSeconds").floatValue = 2f;
            b.FindProperty("_showerWaves").intValue = 3;
            b.FindProperty("_showerWaveInterval").floatValue = 1f;
            b.FindProperty("_showerHitsMultiplier").floatValue = 3f;
            b.FindProperty("_turretMaxIntercept").floatValue = 1f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 100, removable: false);
            _block = Module("MD_TestBlock", 0);
            _home = Module("MD_TestHome", 10);
            _residents = Create<ResidentConfig>();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var grades = g.FindProperty("_grades");
            grades.arraySize = 2;
            grades.GetArrayElementAtIndex(0).FindPropertyRelative("_displayName").stringValue = "G0";
            grades.GetArrayElementAtIndex(1).FindPropertyRelative("_displayName").stringValue = "대형";
            grades.GetArrayElementAtIndex(1).FindPropertyRelative("_minPopulation").intValue = 0;
            grades.GetArrayElementAtIndex(1).FindPropertyRelative("_minModules").intValue = 4; // 코어 + 블록 3
            g.ApplyModifiedPropertiesWithoutUndo();

            _rabbit = Create<SpeciesData>();
            _rabbit.EditorSet("rabbit", "토끼", "", ResidentNeed.Medical, 0.5f, 0.5f, ResidentTrait.Gardener, 0.02f, 0.1f, 0f, 0f);
            _bear = Create<SpeciesData>();
            _bear.EditorSet("bear", "곰", "", ResidentNeed.Recreation, 0.5f, -0.5f, ResidentTrait.Technician, 0.02f, 0.1f, 0.5f, 0f);
            _ark = Create<ArkShipConfig>();
            _ark.EditorSet(_rabbit, new List<SpeciesData> { _rabbit, _bear }, new List<AxisShift>(), new List<AxisShift>(), new List<AxisShift>());
            var a = new SerializedObject(_ark);
            a.FindProperty("_autoResolveSeconds").floatValue = 100000f; // 다음 배는 답을 기다리며 멈춰 있음 (무리 구성이 그대로)
            a.ApplyModifiedPropertiesWithoutUndo();
            _society = Create<SocietyConfig>(); // 기본값: 통제 배율 1 · 자율 배율 0.5 · 파손 3 · 과밀 6/분 · 반발 4/분 · 잔잔 1/분

            _rationing = Policy("PO_Rationing", "배급제", PolicyStance.Control, M(PolicyStat.FoodWaterConsumption, 0.8f), M(PolicyStat.SatisfactionCap, -10f));
            _nightShift = Policy("PO_NightShift", "야간 근무", PolicyStance.Control, M(PolicyStat.Production, 1.15f), M(PolicyStat.SatisfactionCap, -5f));
            _festival = Policy("PO_Festival", "축제 문화", PolicyStance.Autonomy, M(PolicyStat.SatisfactionCap, 10f), M(PolicyStat.Production, 0.9f));
            _policyConfig = Create<PolicyConfig>();
            _policyConfig.SetPolicies(new List<PolicyData> { _rationing, _nightShift, _festival });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Policies_OpenWithChapterRule_OneSlotThenTwoAtGradeGoal_ChapterListAndTimes()
        {
            var ch1 = Chapter("1장", StationRule.None, slots: 0, hold: false);
            var ch3 = Chapter("3장", StationRule.Policies, slots: 1, hold: true, gradeGoal: true, policies: new[] { _rationing, _festival });
            var sim = Sim(Chapters(ch1, ch3));
            var pol = sim.Policies;
            Assert.IsInstanceOf<ChapterPolicySystem>(pol.Gate, "서사판이 정책 판 고리를 꽂음");
            Assert.AreEqual(0, pol.SlotCount, "정책 규칙 전 = 슬롯 없음");

            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Transition);
            Assert.IsTrue(sim.Chapters.Choose(-1));
            Assert.AreEqual(1, sim.Chapters.Index);
            Assert.AreEqual(1, pol.SlotCount, "3장 = 1칸");
            Assert.AreEqual(30f, pol.ApplyDelay, Eps);
            Assert.AreEqual(180f, pol.SwapCooldown, Eps);
            Assert.IsFalse(pol.Offers(_nightShift), "장 목록에 없는 정책");
            Assert.AreEqual(PolicySystem.AssignResult.NotOffered, pol.Assign(0, _nightShift));
            Assert.AreEqual(PolicySystem.AssignResult.Ok, pol.Assign(0, _rationing));
            Tick(sim, 31);
            Assert.IsTrue(pol.IsActive(_rationing));
            Assert.IsTrue(sim.Chapters.HasFlag(ChapterPolicySystem.PolicyFlag), "정책 하나 시행 = 장 표시");
            Assert.AreEqual(-5f, sim.Effects.Policy(PolicyStat.SatisfactionCap), Eps, "토끼만 = 순응 0.5 → 통제형 감점 절반");
            Assert.AreEqual("통제형 · 토끼 순응 (감점 ×0.5)", ((IPolicyGate)pol.Gate).Note(_rationing));

            Assert.AreEqual("대형 도달 때", ((IPolicyGate)pol.Gate).LockedSlot(1));
            foreach (var d in new[] { Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1) })
                Assert.IsTrue(sim.Grid.TryPlace(_block, d, 0, out _)); // 모듈 4개 = '대형'
            Tick(sim, 2);
            Assert.IsTrue(sim.Chapters.GradeUpApplied);
            Assert.AreEqual(2, pol.SlotCount, "대형 도달 목표 → 2칸");
        }

        [Test]
        public void SpeciesMix_BearsAmplifyControlAndAutonomy_BacklashRaisesTension()
        {
            var sim = Sim(Chapters(Chapter("3장", StationRule.Policies, slots: 2, hold: true, bears: true, policies: new[] { _rationing, _festival })));
            var pol = sim.Policies;
            var tension = sim.GetSystem<TensionSystem>();
            var ark = sim.GetSystem<ArkShipSystem>();
            RunUntil(sim, () => ark.Pending != null);
            Assert.IsTrue(ark.Answer(ShipAnswer.Accept)); // 토끼 4 · 곰 12 → 반응 = 0.25 × 0.5 + 0.75 × −0.5 = −0.25
            Assert.AreEqual(12, sim.Residents.CountKind("bear"));
            pol.Assign(0, _rationing);
            pol.Assign(1, _festival);
            Tick(sim, 31);
            Assert.AreEqual(-0.25f, sim.GetSystem<ChapterPolicySystem>().Reaction, Eps);
            Assert.AreEqual(-10f * 1.25f + 10f * 1.125f, sim.Effects.Policy(PolicyStat.SatisfactionCap), Eps, "통제형 감점 ×1.25 · 자율형 효과 ×1.125");
            Assert.AreEqual(0.9f, sim.Effects.Policy(PolicyStat.Production), Eps, "자율형 부작용은 그대로");
            StringAssert.Contains("곰 반발", ((IPolicyGate)pol.Gate).Note(_rationing));
            StringAssert.Contains("곰 반김", ((IPolicyGate)pol.Gate).Note(_festival));

            float before = tension.Value;
            Tick(sim, 60);
            Assert.AreEqual(before + 4f * 0.375f, tension.Value, 0.05f, "반발 무리 0.75 × 0.5 · 통제형 1개 → 분당 1.5");
            Assert.IsTrue(tension.TopChapterCause(out var cause));
            Assert.AreEqual(TensionSystem.Source.Backlash, cause);
            StringAssert.Contains("정책 반발", tension.RisingCauses());
        }

        [Test]
        public void Tension_ForecastHitAndCrowd_ThenCalms_SurvivesSave()
        {
            var sim = Sim(Chapters(Chapter("3장", StationRule.None, slots: 0, hold: true)));
            var tension = sim.GetSystem<TensionSystem>();
            foreach (var d in new[] { Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1), Vector3Int.up, Vector3Int.down })
                Assert.IsTrue(sim.Grid.TryPlace(_block, d, 0, out _));
            sim.MeteorShower.BeginRaid(2f, 1, 1, null);
            RunUntil(sim, () => sim.MeteorShower.Phase == MeteorShowerSystem.ShowerPhase.Idle);
            Assert.Greater(sim.Damage.DamagedCount, 0, "포격이 맞음");
            Assert.AreEqual(3f * sim.Damage.DamagedCount, tension.ChapterTotal(TensionSystem.Source.ForecastHit), Eps, "예보 뒤 파손마다 +3");

            var crowd = Sim(Chapters(Chapter("3장", StationRule.None, slots: 0, hold: true)), housing: 2);
            var t2 = crowd.GetSystem<TensionSystem>();
            Tick(crowd, 30); // 수용 2에 4명 = 100% 넘음
            Assert.AreEqual(3f, t2.Value, 0.05f, "과밀 100% → 분당 6");
            Assert.AreEqual("과밀", t2.RisingCauses());

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(crowd));
            var loaded = Sim(Chapters(Chapter("3장", StationRule.None, slots: 0, hold: true)), housing: 2);
            StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));
            var t3 = loaded.GetSystem<TensionSystem>();
            Assert.AreEqual(t2.Value, t3.Value, Eps, "저장");
            Assert.AreEqual(t2.ChapterTotal(TensionSystem.Source.Crowd), t3.ChapterTotal(TensionSystem.Source.Crowd), Eps);

            Assert.IsTrue(crowd.Grid.TryPlace(_home, Vector3Int.right, 0, out _)); // 집을 늘려 과밀이 풀림
            Tick(crowd, 60);
            Assert.AreEqual(2f, t2.Value, 0.05f, "오를 이유가 없으면 분당 1씩 내려감");
            Assert.AreEqual("", t2.RisingCauses());
        }

        // ---------------- 도우미 ----------------

        private static void Tick(StationSimulation sim, int seconds)
        {
            for (int i = 0; i < seconds; i++)
                sim.Tick(1f);
        }

        private static void RunUntil(StationSimulation sim, System.Func<bool> done)
        {
            for (int i = 0; i < 400 && !done(); i++)
                sim.Tick(1f);
            Assert.IsTrue(done(), "시간 안에 끝나지 않음");
        }

        private StationSimulation Sim(ChapterConfig chapters, int housing = 100)
        {
            var so = new SerializedObject(_core);
            so.FindProperty("_housingCapacity").intValue = housing;
            so.ApplyModifiedPropertiesWithoutUndo();
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
                Policies = _policyConfig,
                Chapters = chapters,
            });
            sim.AddSystem(new ArkShipSystem(_ark, () => 0f), TickStepNames.Population);
            sim.AddSystem(new ChapterPolicySystem(_society), TickStepNames.Population);
            sim.AddSystem(new TensionSystem(_society), TickStepNames.Population);
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

        /// <summary>
        /// 정책 칸(슬롯 · 등급 목표 때 +1 · 30초 · 3분 · 목록). hold = 필수 '오래 버티기'(준비 단계 유지), 아니면 코어 기동 표시(튜토리얼이 없으면 저절로)만
        /// → 바로 절정(없음) → 전환. gradeGoal = 필수 '대형 도달' 추가. bears = 이민선(간격 10초 · 곰 12명 · 5초 전 예보).
        /// </summary>
        private ChapterData Chapter(string name, StationRule rules, int slots, bool hold, bool gradeGoal = false, bool bears = false, PolicyData[] policies = null)
        {
            policies = policies ?? new PolicyData[0];
            var c = Create<ChapterData>();
            c.name = "CH_" + name;
            var so = new SerializedObject(c);
            so.FindProperty("_displayName").stringValue = name;
            so.FindProperty("_rules").intValue = (int)rules;
            so.FindProperty("_policySlots").intValue = slots;
            so.FindProperty("_gradeUpPolicySlots").intValue = 1;
            so.FindProperty("_policyApplyDelay").floatValue = 30f;
            so.FindProperty("_policySwapCooldown").floatValue = 180f;
            var list = so.FindProperty("_policies");
            list.arraySize = policies.Length;
            for (int i = 0; i < policies.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = policies[i];
            var goals = so.FindProperty("_goals");
            goals.arraySize = gradeGoal ? 2 : 1;
            var first = goals.GetArrayElementAtIndex(0);
            if (hold)
            {
                first.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.SurviveSeconds;
                first.FindPropertyRelative("_amount").floatValue = 100000f;
            }
            else
            {
                first.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Flag;
                first.FindPropertyRelative("_flag").stringValue = ChapterSystem.TutorialFlag;
            }
            if (gradeGoal)
            {
                var grade = goals.GetArrayElementAtIndex(1);
                grade.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.ReachGrade;
                grade.FindPropertyRelative("_grade").intValue = 1;
            }
            so.FindProperty("_ships").boolValue = bears;
            so.FindProperty("_shipInterval").floatValue = 10f;
            so.FindProperty("_shipSizeMin").intValue = 12;
            so.FindProperty("_shipSizeMax").intValue = 12;
            so.FindProperty("_shipForecast").floatValue = 5f;
            so.FindProperty("_newSpecies").objectReferenceValue = bears ? _bear : null;
            var climax = so.FindProperty("_climax");
            climax.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterClimaxKind.None;
            climax.FindPropertyRelative("_warningSeconds").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return c;
        }

        private ModuleData Module(string name, int housing, bool removable = true)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            so.FindProperty("_housingCapacity").intValue = housing;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private static PolicyModifier M(PolicyStat s, float v) => new PolicyModifier { Stat = s, Value = v };

        private PolicyData Policy(string file, string name, PolicyStance stance, params PolicyModifier[] modifiers)
        {
            var p = Create<PolicyData>();
            p.name = file;
            p.Set(name, "", modifiers);
            p.SetStance(stance);
            return p;
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
