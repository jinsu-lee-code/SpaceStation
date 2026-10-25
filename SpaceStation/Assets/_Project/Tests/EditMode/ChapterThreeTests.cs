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

        [Test]
        public void ArkDocking_PortEntranceFirst_ElseNearestOuterEndFromCore()
        {
            var sim = Sim(Chapters(Chapter("3장", StationRule.None, slots: 0, hold: true)));
            // 코어에서 +X 3칸 · -X 1칸 · +Z 2칸 · -Z 0칸 → 바깥 끝이 가장 가까운 쪽 = -Z (코어 면)
            foreach (var c in new[] { new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0), new Vector3Int(3, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, 0, 2) })
                Assert.IsTrue(sim.Grid.TryPlace(_block, c, 0, out _));
            Assert.IsTrue(ArkDocking.Find(sim.Grid, sim.Core, null, out var nose, out var outward));
            Assert.AreEqual(new Vector3(0f, 0f, -1f), outward);
            Assert.AreEqual(new Vector3(0f, 0f, -(0.5f + ArkDocking.Gap)), nose, "코어 -Z 면 바로 앞");

            var port = Create<ModuleData>();
            port.name = "MD_TestPort";
            var so = new SerializedObject(port);
            so.FindProperty("_immigrationPort").boolValue = true;
            so.FindProperty("_terminalOnly").boolValue = true;
            so.FindProperty("_dockFront").vector3IntValue = new Vector3Int(0, 0, 1);
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 2;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            offsets.GetArrayElementAtIndex(1).vector3IntValue = new Vector3Int(1, 0, 0);
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.IsTrue(sim.Grid.TryPlace(port, new Vector3Int(4, 0, 0), 0, out _)); // +X 줄 끝에 붙임, 입구 = +Z
            Assert.IsTrue(ArkDocking.Find(sim.Grid, sim.Core, null, out nose, out outward));
            Assert.AreEqual(new Vector3(0f, 0f, 1f), outward, "포트 입구 방향");
            Assert.AreEqual(4.5f, nose.x, Eps, "두 칸 포트 입구 가운데");
            Assert.AreEqual(0.5f + ArkDocking.Gap, nose.z, Eps);
        }

        [Test]
        public void GreatMigration_FirstShipFlag_ThenThreeMixedShips_AcceptedRatioDecides_SurvivesSave()
        {
            var ch = MigrationChapter();
            var sim = Sim(Chapters(ch));
            var ark = sim.GetSystem<ArkShipSystem>();
            RunUntil(sim, () => ark.Pending != null);
            Assert.IsTrue(ark.Pending.NewSpecies, "3장 첫 배 = 처음 만나는 곰");
            Assert.IsTrue(ark.Answer(ShipAnswer.Refuse));
            Assert.IsTrue(sim.Chapters.HasFlag(ArkShipSystem.NewSpeciesFlag), "거부도 '첫 이민선 맞이하기'");
            Assert.IsTrue(sim.Chapters.HasFlag(ArkShipSystem.MetFlag("bear")), "14-1b 종족별 표시");
            Assert.IsFalse(sim.Chapters.HasFlag(ArkShipSystem.MetFlag("fox")));
            Tick(sim, 1);
            Assert.AreEqual(ChapterPhase.Climax, sim.Chapters.Phase, "필수 완료 · 예고 0초 → 대이주");
            Assert.IsTrue(ark.ConvoyComing);
            Assert.AreEqual("bear", ark.NextShip.Species, "가장 최근에 만난 종족부터");

            RunUntil(sim, () => ark.Pending != null);
            Assert.IsTrue(ark.Pending.Convoy);
            Assert.AreEqual(5, ark.Pending.Size);
            Assert.IsTrue(ark.Answer(ShipAnswer.Accept)); // 5/5
            Tick(sim, 1);
            Assert.AreEqual("rabbit", ark.NextShip.Species, "섞여서 옴");
            StringAssert.Contains("배 2/3", sim.Chapters.Climax.Status);

            // 배가 오는 중에 저장 → 불러온 판에서 이어감
            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var loaded = Sim(Chapters(ch));
            StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));
            var ark2 = loaded.GetSystem<ArkShipSystem>();
            Assert.IsTrue(ark2.ConvoyComing, "오는 배 저장");
            RunUntil(loaded, () => ark2.Pending != null);
            Assert.IsTrue(ark2.Answer(ShipAnswer.Refuse)); // 5/10
            RunUntil(loaded, () => ark2.Pending != null);
            Assert.AreEqual("bear", ark2.Pending.Species);
            Assert.IsTrue(ark2.Answer(ShipAnswer.Accept)); // 10/15 = 67%
            Tick(loaded, 5);
            StringAssert.StartsWith("정리", loaded.Chapters.Climax.Status, "마지막 배 뒤 정리 시간");
            RunUntil(loaded, () => loaded.Chapters.Phase == ChapterPhase.Transition);
            var record = loaded.Chapters.LastRecord;
            Assert.AreEqual(ChapterOutcome.Clean, record.Outcome, "60% 이상 · 떠남 0 · 바닥 0초");
            StringAssert.Contains("이주민 15명 중 10명 받음 (67%)", record.Summary);
        }

        [Test]
        public void GreatMigration_LowAcceptanceIsTight_CrowdDeparturesMeanMissed()
        {
            // 한 척만 받음 = 5/15 (33%) → 빠듯하게
            var sim = Sim(Chapters(MigrationChapter()));
            var ark = sim.GetSystem<ArkShipSystem>();
            RunUntil(sim, () => ark.Pending != null);
            ark.Answer(ShipAnswer.Refuse);
            foreach (var answer in new[] { ShipAnswer.Accept, ShipAnswer.Refuse, ShipAnswer.Refuse })
            {
                RunUntil(sim, () => ark.Pending != null && ark.Pending.Convoy);
                ark.Answer(answer);
            }
            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Transition);
            Assert.AreEqual(ChapterOutcome.Hurt, sim.Chapters.LastRecord.Outcome);

            // 다 받았지만 집이 모자라 하나둘 떠남 (3명 넘게) → 많은 이를 놓침
            var b = new SerializedObject(_config);
            b.FindProperty("_overcrowdedLossInterval").floatValue = 2f;
            b.ApplyModifiedPropertiesWithoutUndo();
            var crowd = Sim(Chapters(MigrationChapter()), housing: 10);
            var ark2 = crowd.GetSystem<ArkShipSystem>();
            RunUntil(crowd, () => ark2.Pending != null);
            ark2.Answer(ShipAnswer.Refuse);
            for (int i = 0; i < 3; i++)
            {
                RunUntil(crowd, () => ark2.Pending != null && ark2.Pending.Convoy);
                ark2.Answer(ShipAnswer.Accept);
            }
            RunUntil(crowd, () => crowd.Chapters.Phase == ChapterPhase.Transition);
            Assert.AreEqual(ChapterOutcome.Lost, crowd.Chapters.LastRecord.Outcome, crowd.Chapters.LastRecord.Summary);
            StringAssert.Contains("15명 중 15명 받음 (100%)", crowd.Chapters.LastRecord.Summary);
        }

        [Test]
        public void DebrisBand_BlocksNewCellsDuringBand_ShipsArrive_JudgedAfterBand_SurvivesSave()
        {
            // 14-1d 4장 위기 '파편 띠' (대이주 틀 + 칸 제한): 배 2척 · 띠 60초
            var ch = MigrationChapter();
            var so = new SerializedObject(ch);
            var climax = so.FindProperty("_climax");
            climax.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterClimaxKind.DebrisBand;
            climax.FindPropertyRelative("_migrationShips").intValue = 2;
            climax.FindPropertyRelative("_bandSeconds").floatValue = 60f;
            so.FindProperty("_unlocks").arraySize = 1;
            so.FindProperty("_unlocks").GetArrayElementAtIndex(0).objectReferenceValue = _block;
            so.ApplyModifiedPropertiesWithoutUndo();
            var sim = Sim(Chapters(ch));
            var ark = sim.GetSystem<ArkShipSystem>();
            Assert.IsTrue(sim.TryPlace(_block, new Vector3Int(1, 0, 0), 0, out var block), "띠 전에 지은 칸");
            RunUntil(sim, () => ark.Pending != null);
            Assert.IsTrue(ark.Answer(ShipAnswer.Accept)); // 첫 배(곰) → 필수 완료 · 예고 0초
            Tick(sim, 1);
            Assert.AreEqual(ChapterPhase.Climax, sim.Chapters.Phase);
            Assert.IsNotNull(sim.BuildZone, "띠 동안 칸 제한");
            Assert.AreEqual(PlacementResult.OutsideZone, sim.EvaluatePlacement(_block, new Vector3Int(-1, 0, 0), 0), "위기 시작 때 비어 있던 칸");
            StringAssert.Contains("파편 띠", sim.BuildZone.Reason);
            Assert.IsTrue(sim.TryRemove(block));
            Assert.AreEqual(PlacementResult.Valid, sim.EvaluatePlacement(_block, new Vector3Int(1, 0, 0), 0), "철거한 자리는 다시 지을 수 있음");
            StringAssert.StartsWith("파편 띠", sim.Chapters.Climax.Status);

            RunUntil(sim, () => ark.Pending != null && ark.Pending.Convoy);
            Assert.IsTrue(ark.Answer(ShipAnswer.Accept)); // 5/5

            // 띠 가운데 저장 → 불러온 판도 같은 칸 제한
            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var loaded = Sim(Chapters(ch));
            StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));
            Assert.IsNotNull(loaded.BuildZone, "띠 저장 · 복원");
            Assert.AreEqual(PlacementResult.OutsideZone, loaded.EvaluatePlacement(_block, new Vector3Int(-1, 0, 0), 0));
            Assert.AreEqual(PlacementResult.Valid, loaded.EvaluatePlacement(_block, new Vector3Int(1, 0, 0), 0));
            var ark2 = loaded.GetSystem<ArkShipSystem>();
            RunUntil(loaded, () => ark2.Pending != null && ark2.Pending.Convoy);
            Assert.IsTrue(ark2.Answer(ShipAnswer.Accept)); // 10/10
            Tick(loaded, 1);
            Assert.AreEqual(ChapterPhase.Climax, loaded.Chapters.Phase, "배에 다 답해도 띠가 지나갈 때까지");
            RunUntil(loaded, () => loaded.Chapters.Phase == ChapterPhase.Transition);
            Assert.IsNull(loaded.BuildZone, "띠가 지나가면 풀림");
            Assert.AreEqual(ChapterOutcome.Clean, loaded.Chapters.LastRecord.Outcome);
            StringAssert.Contains("이주민 10명 중 10명 받음", loaded.Chapters.LastRecord.Summary);
        }

        [Test]
        public void EarthLights_DimWithinChapter_DropAtNextChapter()
        {
            var ch1 = Chapter("1장", StationRule.None, slots: 0, hold: false);
            var ch2 = Chapter("2장", StationRule.None, slots: 0, hold: true);
            foreach (var (c, start, end) in new[] { (ch1, 1f, 0.9f), (ch2, 0.7f, 0.5f) })
            {
                var so = new SerializedObject(c);
                so.FindProperty("_earthLightsStart").floatValue = start;
                so.FindProperty("_earthLightsEnd").floatValue = end;
                so.FindProperty("_earthDimSeconds").floatValue = 100f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.AreEqual(1f, EarthLights.Value(null), "장 틀이 없으면 모두 켜짐");
            var sim = Sim(Chapters(ch1, ch2));
            Assert.AreEqual(1f, EarthLights.Value(sim.Chapters), Eps, "1장 시작");
            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Transition); // 1장은 바로 끝남
            Assert.AreEqual(0.9f, EarthLights.Value(sim.Chapters), Eps, "전환 = 이 장 끝 값");
            Assert.IsTrue(sim.Chapters.Choose(-1));
            Assert.AreEqual(0.7f, EarthLights.Value(sim.Chapters), Eps, "다음 장 시작 = 크게 꺼짐");
            Tick(sim, 50);
            Assert.AreEqual(0.6f, EarthLights.Value(sim.Chapters), 0.011f, "장 안에서 서서히");
            Tick(sim, 100);
            Assert.AreEqual(0.5f, EarthLights.Value(sim.Chapters), Eps, "끝 값에서 멈춤");

            // 13-7b 마지막 장: 방향을 고르면(캠페인 끝) 전환 연출에서 한 번 더 꺼진 값
            var last = Chapter("3장", StationRule.None, slots: 0, hold: false);
            var lso = new SerializedObject(last);
            lso.FindProperty("_earthLightsEnd").floatValue = 0.5f;
            lso.FindProperty("_earthLightsFinal").floatValue = 0.4f;
            lso.ApplyModifiedPropertiesWithoutUndo();
            var campaign = Sim(Chapters(last));
            RunUntil(campaign, () => campaign.Chapters.Phase == ChapterPhase.Transition);
            Assert.AreEqual(0.5f, EarthLights.Value(campaign.Chapters), Eps);
            Assert.IsTrue(campaign.Chapters.Choose(-1));
            Assert.AreEqual(ChapterPhase.Finished, campaign.Chapters.Phase);
            Assert.AreEqual(0.4f, EarthLights.Value(campaign.Chapters), Eps, "캠페인 끝 = 마지막 값");
        }

        [Test]
        public void ResidentRemarks_KeyOrderSpeciesAndExclude()
        {
            _rabbit.EditorSetRemarks(("chapter.start.2.Hurt", "밤새 무서웠어요."), ("chapter.start.2", "정거장이 커져요."), ("ship.new", "곰이라니요."));
            _bear.EditorSetRemarks(("ship.accept", "또 왔군."));
            var sim = Sim(Chapters(Chapter("3장", StationRule.None, slots: 0, hold: true)));
            var ark = sim.GetSystem<ArkShipSystem>();
            var residents = sim.Residents.Residents;
            Assert.AreEqual(4, residents.Count);

            Assert.IsTrue(ResidentRemarks.Pick(residents, ark.Find, ResidentRemarks.ChapterStartKeys(2, ChapterOutcome.Hurt), () => 0f, out var who, out var text));
            Assert.AreEqual("밤새 무서웠어요.", text, "지난 절정 결과가 붙은 키 먼저");
            Assert.AreEqual("rabbit", who.Kind);
            ResidentRemarks.Pick(residents, ark.Find, ResidentRemarks.ChapterStartKeys(2, ChapterOutcome.Clean), () => 0f, out _, out text);
            Assert.AreEqual("정거장이 커져요.", text, "결과 문장이 없으면 장 문장");
            Assert.IsFalse(ResidentRemarks.Pick(residents, ark.Find, ResidentRemarks.ShipKeys(ShipAnswer.Accept, false, 5), () => 0f, out _, out _),
                "그 키 문장을 가진 종족(곰)의 주민이 없으면 아무도 말하지 않음");
            Assert.IsFalse(ResidentRemarks.Pick(residents, ark.Find, ResidentRemarks.ShipKeys(ShipAnswer.Accept, true, 5), () => 0f, out _, out _, exclude: "rabbit"),
                "처음 온 종족을 맞는 말은 그 종족이 아닌 주민이");
            Assert.IsTrue(ResidentRemarks.Pick(residents, ark.Find, ResidentRemarks.ShipKeys(ShipAnswer.Accept, true, 5), () => 0.99f, out _, out text, exclude: "bear"));
            Assert.AreEqual("곰이라니요.", text);

            // 14-1b 종족마다 맞는 말 (ship.new.{종족} 먼저 → 없으면 ship.new)
            _rabbit.EditorSetRemarks(("ship.new.fox", "여우들이에요!"), ("ship.new", "누가 왔어요."));
            Assert.IsTrue(ResidentRemarks.Pick(residents, ark.Find, ResidentRemarks.ShipKeys(ShipAnswer.Accept, true, 5, "fox"), () => 0f, out _, out text, exclude: "fox"));
            Assert.AreEqual("여우들이에요!", text);
            Assert.IsTrue(ResidentRemarks.Pick(residents, ark.Find, ResidentRemarks.ShipKeys(ShipAnswer.Accept, true, 5, "bear"), () => 0f, out _, out text, exclude: "bear"));
            Assert.AreEqual("누가 왔어요.", text, "그 종족 문장이 없으면 공통 문장");
        }

        // ---------------- 도우미 ----------------

        /// <summary>13-6c 3장: 필수 = '첫 이민선 맞이하기'만 · 예고 0초 → 대이주 (배 3척 · 첫 배 5초 · 간격 10초 · 5명 · 정리 20초, 문턱은 사용자 결정 1안).</summary>
        private ChapterData MigrationChapter()
        {
            var ch = Chapter("3장", StationRule.None, slots: 0, hold: false, bears: true);
            var so = new SerializedObject(ch);
            var goal = so.FindProperty("_goals").GetArrayElementAtIndex(0);
            goal.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Flag;
            goal.FindPropertyRelative("_flag").stringValue = ArkShipSystem.NewSpeciesFlag;
            so.FindProperty("_shipSizeMin").intValue = 6;
            so.FindProperty("_shipSizeMax").intValue = 6;
            var climax = so.FindProperty("_climax");
            climax.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterClimaxKind.GreatMigration;
            climax.FindPropertyRelative("_warningSeconds").floatValue = 0f;
            climax.FindPropertyRelative("_migrationShips").intValue = 3;
            climax.FindPropertyRelative("_migrationFirstLead").floatValue = 5f;
            climax.FindPropertyRelative("_migrationInterval").floatValue = 10f;
            climax.FindPropertyRelative("_migrationSizeMin").intValue = 5;
            climax.FindPropertyRelative("_migrationSizeMax").intValue = 5;
            climax.FindPropertyRelative("_settleSeconds").floatValue = 20f;
            climax.FindPropertyRelative("_cleanMinAccepted").floatValue = 0.6f;
            climax.FindPropertyRelative("_cleanMaxDeparted").intValue = 0;
            climax.FindPropertyRelative("_cleanMaxShortageSeconds").floatValue = 0f;
            climax.FindPropertyRelative("_hurtMinAccepted").floatValue = 0.3f;
            climax.FindPropertyRelative("_hurtMaxDeparted").intValue = 3;
            climax.FindPropertyRelative("_hurtMaxShortageSeconds").floatValue = 60f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return ch;
        }

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
