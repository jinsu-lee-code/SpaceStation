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
    /// <summary>13-3 서사판 이민선 시계 + 종족: 예보 · 도착 · 받기 / 빈 집만큼 / 거부 · 위기 완충 · 장의 새 종족 · 종족 기본값 · 세이브.</summary>
    public class ArkShipTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _habitat;
        private ResidentConfig _residents;
        private SpeciesData _rabbit, _bear;
        private ArkShipConfig _ark;
        private CoreConfig _coreConfig;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 4;
            b.FindProperty("_startingSatisfaction").floatValue = 80f;
            b.FindProperty("_growthMinSatisfaction").floatValue = 100f; // 자연 증가 없음 (이민선만 보기)
            b.FindProperty("_overcrowdedLossInterval").floatValue = 10000f;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 100), R(ResourceType.Water, 100), R(ResourceType.Food, 100), R(ResourceType.Metal, 500));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 1000));
            b.FindProperty("_destroyAfterSeconds").floatValue = 600f;
            b.FindProperty("_eventGracePeriod").floatValue = 10000f;
            b.FindProperty("_eventIntervalMin").floatValue = 10000f;
            b.FindProperty("_eventIntervalMax").floatValue = 10000f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 4, removable: false);
            _habitat = Module("MD_TestHabitat", 3);
            _residents = Create<ResidentConfig>();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.FindProperty("_grades").GetArrayElementAtIndex(0).FindPropertyRelative("_displayName").stringValue = "G0";
            g.ApplyModifiedPropertiesWithoutUndo();

            _rabbit = Create<SpeciesData>();
            _rabbit.EditorSet("rabbit", "토끼", "", ResidentNeed.Medical, 0.5f, 0.5f, ResidentTrait.Gardener, 0.02f, 0.1f, 0f, 0f);
            _bear = Create<SpeciesData>();
            _bear.EditorSet("bear", "곰", "", ResidentNeed.Recreation, 0.5f, -0.5f, ResidentTrait.Technician, 0.03f, 0.1f, 0.5f, 0f);
            _ark = Create<ArkShipConfig>();
            _ark.EditorSet(_rabbit, new List<SpeciesData> { _rabbit, _bear },
                new List<AxisShift> { new AxisShift(CoreAxis.Care, CorePole.Second, ShiftSize.Medium) },
                new List<AxisShift> { new AxisShift(CoreAxis.Care, CorePole.First, ShiftSize.Small) },
                new List<AxisShift> { new AxisShift(CoreAxis.Care, CorePole.First, ShiftSize.Medium) });
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
        public void Clock_ForecastThenArrival_PartialTakesFreeHousing_RefuseLogsCore_AutoResolvesHeadless()
        {
            var sim = Sim(Chapters(Chapter("1장", ships: true, newSpecies: null, growth: 3f)), out var ark, out var core);
            Assert.IsTrue(sim.Residents.Residents.All(r => r.Kind == "rabbit"), "시작 주민 = 시작 종족");
            Assert.IsTrue(sim.GetSystem<ImmigrationSystem>().Suppressed, "포트 이민선은 꺼짐");
            Assert.AreSame(ark, sim.Arrivals, "여객 도킹 포트 도착 안내판 = 서사판 이민선 (main M-42)");
            Assert.IsTrue(sim.Grid.TryPlace(_habitat, Vector3Int.right, 0, out _)); // 장 해금과 무관하게 집 하나 (빈 집 3)
            int entries = core.Entries.Count;

            sim.Tick(1f);
            Assert.AreEqual(3f, sim.GrowthIntervalScale, Eps, "장의 자연 증가 간격 배율");
            Assert.AreEqual(99f, ark.TimeLeft, Eps);
            Assert.IsNull(ark.NextShip);
            Tick(sim, 69);
            Assert.IsNotNull(ark.NextShip, "도착 30초 전 예보");
            Assert.AreEqual("rabbit", ark.NextShip.Species);
            Assert.AreEqual(5, ark.NextShip.Size);
            Tick(sim, 30);
            Assert.IsNotNull(ark.Pending, "도착 → 답을 기다림");
            Assert.AreEqual(1, ark.Ships);

            Assert.AreEqual(3, ark.FreeHousing);
            Assert.AreEqual(5, ark.Boarding(ShipAnswer.Accept));
            Assert.AreEqual(3, ark.Boarding(ShipAnswer.Partial), "빈 집만큼");
            Assert.AreEqual(0, ark.Boarding(ShipAnswer.Refuse));
            Assert.IsTrue(ark.Answer(ShipAnswer.Partial));
            Assert.AreEqual(7, sim.Resources.Population);
            Assert.AreEqual(7, sim.Residents.CountKind("rabbit"));
            Assert.AreEqual(3, ark.Accepted);
            Assert.AreEqual(2, ark.TurnedAway);
            Assert.Less(core.Value(CoreAxis.Care), 0f, "빈 집만큼 = 효율 쪽 조금");
            Assert.AreEqual(entries + 1, core.Entries.Count, "코어 기록");
            Assert.IsTrue(sim.Chapters.HasFlag("ship.accepted"));

            Tick(sim, 100);
            Assert.IsNotNull(ark.Pending);
            float care = core.Value(CoreAxis.Care);
            Assert.IsTrue(ark.Answer(ShipAnswer.Refuse));
            Assert.AreEqual(7, sim.Resources.Population, "거부 = 아무도 안 내림");
            Assert.Less(core.Value(CoreAxis.Care), care, "거부 = 효율 쪽");
            Assert.AreEqual(2, sim.Arrivals.Visits, "답한 배 수 (안내판이 답한 순간을 알아챔)");
            Assert.AreEqual(3, sim.Arrivals.Arrived, "안내판 도착 인원 = 받은 수");
            Assert.AreEqual(entries + 2, core.Entries.Count);

            Tick(sim, 100);
            Assert.IsNotNull(ark.Pending);
            Tick(sim, 30); // 화면이 없으면(봇 · 시험) 기다리다 빈 집만큼
            Assert.IsNull(ark.Pending);
            Assert.AreEqual(3, ark.Ships);
        }

        [Test]
        public void CrisisSlowsClock_ClimaxHolds_NoShipsChapterTurnsOff()
        {
            var sim = Sim(Chapters(Chapter("1장", ships: true, newSpecies: null, growth: 1f)), out var ark, out _);
            sim.Tick(1f);
            Assert.AreEqual(99f, ark.TimeLeft, Eps);
            sim.Population.SetSatisfaction(10f); // 기준 50 미만 = 위기
            sim.Tick(1f);
            Assert.IsTrue(ark.Slowed);
            Assert.AreEqual(98.5f, ark.TimeLeft, Eps, "위기 동안 절반 속도");

            var off = Sim(Chapters(Chapter("1장", ships: false, newSpecies: null, growth: 1f)), out var ark2, out _);
            Tick(off, 300);
            Assert.IsFalse(ark2.ClockOn, "이민선이 없는 장 (1장)");
            Assert.AreEqual(0, ark2.Ships);
        }

        [Test]
        public void ChapterNewSpecies_FirstShip_AddsTechAndFavoredNeed_BirthsFollowMix_SurvivesSave()
        {
            var sim = Sim(Chapters(Chapter("3장", ships: true, newSpecies: _bear, growth: 1f)), out var ark, out _);
            Tick(sim, 100);
            Assert.IsNotNull(ark.Pending);
            Assert.AreEqual("bear", ark.Pending.Species, "장의 첫 배 = 새 종족");
            Assert.IsTrue(ark.Pending.NewSpecies);
            Assert.IsTrue(ark.Answer(ShipAnswer.Accept));
            Assert.AreEqual(9, sim.Resources.Population, "모두 받기 = 집이 모자라도 내림 (과밀)");
            Assert.AreEqual(5, sim.Residents.CountKind("bear"));
            CollectionAssert.AreEquivalent(new[] { "rabbit", "bear" }, ark.MetSpecies);
            StringAssert.Contains("곰 5", ark.Composition());

            Assert.AreEqual(0.1f, sim.Residents.Ability(ResidentTrait.Technician), Eps, "곰 5 × 3% = 15% → 종족 상한 10%");
            Assert.AreEqual(0.08f, sim.Residents.Ability(ResidentTrait.Gardener), Eps, "토끼 4 × 2%");
            Assert.AreEqual(1f + 0.5f * 5f / 9f, sim.Needs.PenaltyScale(ResidentNeed.Recreation), Eps, "여가 중시 곰 비율만큼 감점 ↑");
            Assert.AreEqual(1f + 0.5f * 4f / 9f, sim.Needs.PenaltyScale(ResidentNeed.Medical), Eps);
            Assert.AreEqual(1f, sim.Needs.PenaltyScale(ResidentNeed.Education), Eps);

            Tick(sim, 100);
            Assert.IsNotNull(ark.Pending);
            Assert.AreEqual("rabbit", ark.Pending.Species, "그 뒤 배 = 만난 종족이 섞임 (난수 0 → 첫 종족)");
            Assert.IsFalse(ark.Pending.NewSpecies);

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var loaded = Sim(Chapters(Chapter("3장", ships: true, newSpecies: _bear, growth: 1f)), out var ark2, out _);
            StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));
            Assert.AreEqual(ark.TimeLeft, ark2.TimeLeft, Eps);
            Assert.IsNotNull(ark2.Pending, "기다리던 배도 저장");
            Assert.AreEqual("rabbit", ark2.Pending.Species);
            Assert.AreEqual(2, ark2.Ships);
            Assert.AreEqual(5, ark2.Accepted);
            Assert.AreEqual(5, loaded.Residents.CountKind("bear"), "주민 종족도 저장");
            CollectionAssert.AreEquivalent(new[] { "rabbit", "bear" }, ark2.MetSpecies);
        }

        // ---------------- 도우미 ----------------

        private static void Tick(StationSimulation sim, int seconds)
        {
            for (int i = 0; i < seconds; i++)
                sim.Tick(1f);
        }

        private StationSimulation Sim(ChapterConfig chapters, out ArkShipSystem ark, out CorePersonalitySystem core)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                Residents = _residents,
                ResidentRandom01 = () => 0.5f,
                Chapters = chapters,
            });
            core = sim.AddSystem(new CorePersonalitySystem(_coreConfig), TickStepNames.Progression);
            ark = sim.AddSystem(new ArkShipSystem(_ark, () => 0f), TickStepNames.Population);
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

        /// <summary>목표 하나(오래 버팀)라 준비 단계가 이어짐. 이민선 간격 100 · 5명 · 30초 전 예보.</summary>
        private ChapterData Chapter(string name, bool ships, SpeciesData newSpecies, float growth)
        {
            var c = Create<ChapterData>();
            c.name = "CH_" + name;
            var so = new SerializedObject(c);
            so.FindProperty("_displayName").stringValue = name;
            var goals = so.FindProperty("_goals");
            goals.arraySize = 1;
            goals.GetArrayElementAtIndex(0).FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.SurviveSeconds;
            goals.GetArrayElementAtIndex(0).FindPropertyRelative("_amount").floatValue = 100000f;
            so.FindProperty("_ships").boolValue = ships;
            so.FindProperty("_shipInterval").floatValue = 100f;
            so.FindProperty("_shipSizeMin").intValue = 5;
            so.FindProperty("_shipSizeMax").intValue = 5;
            so.FindProperty("_shipForecast").floatValue = 30f;
            so.FindProperty("_newSpecies").objectReferenceValue = newSpecies;
            so.FindProperty("_growthIntervalScale").floatValue = growth;
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
