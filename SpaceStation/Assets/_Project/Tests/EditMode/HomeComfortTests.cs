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
    /// <summary>14-2b 서사판 집 환경: 종족 견딤 × 집의 열 · 방사선 → 힘든 주민 · 비율만큼 상한 감소 · 폭풍 때 대피 정원 · 긴장도 환경 불만 · 방열 종족 기술.</summary>
    public class HomeComfortTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _home, _hot, _shelter, _radiator, _armor;
        private ResidentConfig _residents;
        private SpeciesData _rabbit, _bear, _penguin, _fox;
        private ArkShipConfig _ark;
        private SocietyConfig _society;
        private ChapterConfig _chapters;
        private Rules _rules;

        private sealed class Rules : IStationRules
        {
            public StationRule Open = StationRule.Radiation | StationRule.Heat;
            public bool Allows(StationRule rule) => (Open & rule) == rule;
        }

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            b.FindProperty("_startingSatisfaction").floatValue = 80f;
            Fill(b.FindProperty("_startingResources"), ResourceType.Oxygen, ResourceType.Water, ResourceType.Food, ResourceType.Metal); // 고갈로 주민이 떠나지 않게
            Fill(b.FindProperty("_baseStorageCapacity"), ResourceType.Oxygen, ResourceType.Water, ResourceType.Food, ResourceType.Metal);
            b.FindProperty("_growthMinSatisfaction").floatValue = 100f;
            b.FindProperty("_overcrowdedLossInterval").floatValue = 100000f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 100000f;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 0f;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_heatFalloff1").floatValue = 0.5f;
            b.FindProperty("_heatFalloff2").floatValue = 0.25f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", removable: false);
            _home = Module("MD_TestHome", housing: 20);
            _hot = Module("MD_TestHot", heat: 6f);
            _shelter = Module("MD_TestShelter", shelter: 2);
            _radiator = Module("MD_TestRadiator", heatRemoval: 4f);
            _armor = Module("MD_TestArmor", layers: 2);
            _residents = Create<ResidentConfig>();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();

            _rabbit = Species("rabbit", "토끼", ResidentTrait.Gardener, 0f, 0f);
            _bear = Species("bear", "곰", ResidentTrait.Technician, 0.5f, 0f);
            _fox = Species("fox", "여우", ResidentTrait.Artisan, 0f, 0.5f);
            _penguin = Species("penguin", "펭귄", ResidentTrait.Cooler, -0.5f, 0f);
            _ark = Create<ArkShipConfig>();
            _ark.EditorSet(_rabbit, new List<SpeciesData> { _rabbit, _bear, _fox, _penguin }, new List<AxisShift>(), new List<AxisShift>(), new List<AxisShift>());
            var a = new SerializedObject(_ark);
            a.FindProperty("_abilityCap").floatValue = 1f;
            a.ApplyModifiedPropertiesWithoutUndo();
            _society = Create<SocietyConfig>();

            var chapter = Create<ChapterData>();
            var c = new SerializedObject(chapter);
            c.FindProperty("_rules").intValue = (int)StationRule.None;
            var goals = c.FindProperty("_goals");
            goals.arraySize = 1;
            goals.GetArrayElementAtIndex(0).FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Flag; // 올리지 않음 — 준비 단계 유지
            goals.GetArrayElementAtIndex(0).FindPropertyRelative("_flag").stringValue = "test.hold";
            c.ApplyModifiedPropertiesWithoutUndo();
            _chapters = Create<ChapterConfig>();
            var cc = new SerializedObject(_chapters);
            var list = cc.FindProperty("_chapters");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = chapter;
            cc.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Limits_FollowSpeciesTolerance()
        {
            var sim = Sim();
            var comfort = sim.GetSystem<HomeComfortSystem>();
            var hot = Place(sim, _hot, 1, 0, 0);
            var home = Place(sim, _home, 2, 0, 0); // 발열 6 × 50% = 3 (한도 보통 4 · 펭귄 2 · 곰 6)
            sim.Heat.Recalculate();
            sim.Radiation.Recalculate();
            Assert.AreEqual(3f, sim.Heat.HeatOf(home), Eps);
            Assert.AreEqual(3f, sim.Radiation.LevelOf(home), Eps, "바깥 노출 3 (한도 보통 4)");

            Assert.AreEqual(0f, comfort.StressOf("rabbit", home), Eps, "보통: 열 3 < 4 · 방사선 3 < 4");
            Assert.AreEqual(1f, comfort.StressOf("penguin", home), Eps, "열 약함: 한도 2 → 넘은 만큼 1");
            Assert.AreEqual(0f, comfort.StressOf("bear", home), Eps);

            sim.Radiation.SpaceMultiplier = 3f; // 폭풍: 9
            sim.Radiation.Recalculate();
            Assert.AreEqual(5f, comfort.StressOf("rabbit", home), Eps, "9 - 4");
            Assert.AreEqual(3f, comfort.StressOf("fox", home), Eps, "방사선 강함: 한도 6");
            Assert.AreEqual(6f, comfort.StressOf("penguin", home), Eps, "방사선 5 + 열 1");
            Assert.AreEqual(5f, comfort.StressOf("unknown", home), Eps, "종족을 모르면 보통");
        }

        [Test]
        public void ClosedRules_NoStress()
        {
            var sim = Sim();
            _rules.Open = StationRule.None;
            var comfort = sim.GetSystem<HomeComfortSystem>();
            var home = Place(sim, _home, 1, 0, 0);
            sim.Resources.SetPopulation(3);
            sim.Tick(1f);
            Assert.AreEqual(0f, comfort.StressOf("penguin", home));
            Assert.AreEqual(0, sim.Residents.StressedCount);
            Assert.AreEqual("", comfort.Summary());
            Assert.AreEqual(0f, sim.Residents.MoodTotal, Eps);
        }

        [Test]
        public void StressedShare_LowersCap_ShelterExemptsInStormOnly()
        {
            var sim = Sim();
            var comfort = sim.GetSystem<HomeComfortSystem>();
            Place(sim, _home, 1, 0, 0);
            Place(sim, _shelter, -1, 0, 0);
            sim.Radiation.SpaceMultiplier = 3f; // 노출 집 9 → 모두 힘듦
            sim.Resources.SetPopulation(10);
            sim.Tick(1f); // 명단 · 방사선 값
            sim.Tick(1f); // 비율 · 대피 정원 값
            sim.Tick(1f);
            Assert.AreEqual(8, sim.Residents.StressedCount, "폭풍: 대피 정원 2명 뺌");
            Assert.AreEqual(-16f, sim.Residents.StressMood, Eps, "20 × 8/10");
            Assert.AreEqual("힘든 주민 8명 · 대피 2명", comfort.Summary());

            sim.Radiation.SpaceMultiplier = 1f;
            var heat = new SerializedObject(_config);
            heat.FindProperty("_radiationComfort").floatValue = 1f; // 평소에도 힘듦 (노출 3 > 1)
            heat.ApplyModifiedPropertiesWithoutUndo();
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual(10, sim.Residents.StressedCount, "평소에는 대피 정원을 세지 않음");
            Assert.AreEqual(-20f, sim.Residents.StressMood, Eps);
            Assert.AreEqual("힘든 주민 10명 (토끼 10)", comfort.Summary());
        }

        [Test]
        public void Tension_RisesWithStressedShare_AndNamesCause()
        {
            var sim = Sim();
            var tension = sim.GetSystem<TensionSystem>();
            Place(sim, _home, 1, 0, 0);
            sim.Radiation.SpaceMultiplier = 3f;
            sim.Resources.SetPopulation(10);
            sim.Tick(1f);
            sim.Tick(1f);
            Assert.AreEqual(3f, tension.RatePerMinute(TensionSystem.Source.Environment), Eps, "모두 힘듦 = 분당 3");
            StringAssert.Contains("환경 불만", tension.RisingCauses());
            for (int i = 0; i < 60; i++)
                sim.Tick(1f);
            Assert.AreEqual(3f, tension.ChapterTotal(TensionSystem.Source.Environment), 0.2f);
            Assert.IsTrue(tension.TopChapterCause(out var cause));
            Assert.AreEqual(TensionSystem.Source.Environment, cause);
        }

        [Test]
        public void Calm_SetsFlagAfterSeconds_ResetsWhenStressed()
        {
            var sim = Sim();
            var so = new SerializedObject(_society);
            so.FindProperty("_calmSeconds").floatValue = 5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            Place(sim, _home, 1, 0, 0);
            sim.Resources.SetPopulation(3);
            string flag = ChapterSystem.ScopedFlag(HomeComfortSystem.CalmFlag, 1);

            sim.Radiation.SpaceMultiplier = 3f; // 노출 9 → 모두 힘듦
            for (int i = 0; i < 10; i++)
                sim.Tick(1f);
            Assert.IsFalse(sim.Chapters.HasFlag(flag), "힘든 주민이 있으면 세지 않음");

            sim.Radiation.SpaceMultiplier = 1f;
            for (int i = 0; i < 3; i++)
                sim.Tick(1f);
            Assert.IsFalse(sim.Chapters.HasFlag(flag), "아직 5초 안 됨");
            for (int i = 0; i < 5; i++)
                sim.Tick(1f);
            Assert.IsTrue(sim.Chapters.HasFlag(flag));
        }

        [Test]
        public void Penguin_CoolerSkill_BoostsRadiators()
        {
            var sim = Sim();
            var hot = Place(sim, _hot, 1, 0, 0);
            Place(sim, _radiator, 2, 0, 0);
            sim.Resources.SetPopulation(5);
            sim.Tick(1f);
            sim.Heat.Recalculate();
            Assert.AreEqual(2f, sim.Heat.HeatOf(hot), Eps, "펭귄 없음: 6 - 4");
            sim.Residents.QueueKinds("penguin", 5);
            sim.Resources.SetPopulation(10);
            sim.Tick(1f);
            sim.Tick(1f);
            sim.Heat.Recalculate();
            Assert.AreEqual(1.6f, sim.Heat.HeatOf(hot), Eps, "펭귄 5명 +10%: 6 - 4.4");
        }

        // ---------------- 14-2d 위기 '항성 폭풍' ----------------

        [Test]
        public void StellarStorm_ExposedHomes_StressedLeave_Lost_ScalesReset()
        {
            var sim = Sim(StormChapters());
            Place(sim, _home, 1, 0, 0);
            sim.Resources.SetPopulation(10);
            sim.Tick(1f);
            sim.Chapters.SetFlag("test.go");
            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Climax);
            Assert.AreEqual(3f, sim.Radiation.SpaceMultiplier, Eps, "바깥 칸 방사선 3배");
            Assert.AreEqual(1.5f, sim.Heat.OutputScale, Eps, "발열");
            Assert.AreEqual(0.5f, sim.Heat.RemovalScale, Eps, "방열판");
            Assert.IsTrue(sim.Radiation.StormActive);
            StringAssert.Contains("남음", sim.Chapters.Climax.Status);

            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Transition);
            var record = sim.Chapters.LastRecord;
            Assert.AreEqual(ChapterOutcome.Lost, record.Outcome, record.Summary);
            StringAssert.Contains("떠난 주민", record.Summary);
            Assert.Less(sim.Resources.Population, 10, "힘든 주민이 떠남");
            Assert.AreEqual(1f, sim.Radiation.SpaceMultiplier, Eps, "끝나면 되돌림");
            Assert.AreEqual(1f, sim.Heat.OutputScale, Eps);
            Assert.AreEqual(1f, sim.Heat.RemovalScale, Eps);
            Assert.IsFalse(sim.Radiation.StormActive);
        }

        [Test]
        public void StellarStorm_TwoLayersInside_Clean()
        {
            var sim = Sim(StormChapters());
            var home = Place(sim, _home, 1, 0, 0);
            foreach (var cell in new[] { new Vector3Int(2, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(1, 1, 0), new Vector3Int(1, -1, 0), new Vector3Int(1, 0, 1), new Vector3Int(1, 0, -1) })
                Place(sim, _armor, cell.x, cell.y, cell.z);
            sim.Resources.SetPopulation(10);
            sim.Tick(1f);
            sim.Chapters.SetFlag("test.go");
            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Climax);
            sim.Tick(1f);
            Assert.AreEqual(2, sim.Radiation.LayersOf(home), "코어 + 장갑 또는 장갑 두 겹 몫");
            Assert.AreEqual(2.25f, sim.Radiation.LevelOf(home), Eps, "9 × 25% — 한도 4 아래");

            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Transition);
            var record = sim.Chapters.LastRecord;
            Assert.AreEqual(ChapterOutcome.Clean, record.Outcome, record.Summary);
            StringAssert.Contains("떠난 주민 0명", record.Summary);
            Assert.AreEqual(10, sim.Resources.Population);
        }

        [Test]
        public void StellarStorm_DangerHeatDamage_Counted_Hurt_SurvivesSave()
        {
            var hotter = HotterModule();
            var chapters = StormChapters();
            var sim = Sim(chapters);
            Place(sim, hotter, 1, 0, 0);
            sim.Resources.SetPopulation(3);
            sim.Tick(1f);
            sim.Chapters.SetFlag("test.go");
            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Climax);
            for (int i = 0; i < 8; i++)
                sim.Tick(1f);
            StringAssert.Contains("과열 파손 1", sim.Chapters.Climax.Status);

            // 폭풍 가운데 저장 → 불러온 판도 같은 배율 · 같은 셈
            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var loaded = Sim(chapters);
            StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));
            Assert.AreEqual(3f, loaded.Radiation.SpaceMultiplier, Eps, "복원 때 배율을 다시 켬");
            Assert.AreEqual(1.5f, loaded.Heat.OutputScale, Eps);
            StringAssert.Contains("과열 파손 1", loaded.Chapters.Climax.Status);
            RunUntil(loaded, () => loaded.Chapters.Phase == ChapterPhase.Transition);
            Assert.AreEqual(ChapterOutcome.Hurt, loaded.Chapters.LastRecord.Outcome, loaded.Chapters.LastRecord.Summary);
        }

        [Test]
        public void StellarStorm_SameModuleBrokenAgain_CountsOnce()
        {
            var hotter = HotterModule();
            var sim = Sim(StormChapters());
            var hot = Place(sim, hotter, 1, 0, 0);
            sim.Resources.SetPopulation(3);
            sim.Tick(1f);
            sim.Chapters.SetFlag("test.go");
            RunUntil(sim, () => sim.Chapters.Phase == ChapterPhase.Climax);
            RunUntil(sim, () => sim.Damage.IsDamaged(hot));
            Assert.IsTrue(sim.Damage.StartRepair(hot));
            RunUntil(sim, () => !sim.Damage.IsDamaged(hot));
            RunUntil(sim, () => sim.Damage.IsDamaged(hot));
            Assert.AreEqual(ChapterPhase.Climax, sim.Chapters.Phase, "두 번째 파손도 폭풍 안에서");
            StringAssert.Contains("과열 파손 1", sim.Chapters.Climax.Status);
        }

        /// <summary>폭풍 때 12 = 위험인 모듈 (한도 8 × 1.5) · 5초 만에 파손 · 방사선 없이 열만 · 주민은 괜찮음.</summary>
        private ModuleData HotterModule()
        {
            var b = new SerializedObject(_config);
            b.FindProperty("_heatDamageSeconds").floatValue = 5f;
            b.FindProperty("_radiationSpace").floatValue = 0f;
            b.FindProperty("_homeHeatComfort").floatValue = 100f;
            b.ApplyModifiedPropertiesWithoutUndo();
            var hotter = Module("MD_TestHotter", heat: 8f);
            var g = new SerializedObject(_grades);
            var unlocks = g.FindProperty("_grades").GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks");
            unlocks.arraySize = 1;
            unlocks.GetArrayElementAtIndex(0).objectReferenceValue = hotter;
            g.ApplyModifiedPropertiesWithoutUndo();
            return hotter;
        }

        private static void RunUntil(StationSimulation sim, System.Func<bool> done)
        {
            for (int i = 0; i < 400 && !done(); i++)
                sim.Tick(1f);
            Assert.IsTrue(done(), "시간 안에 끝나지 않음");
        }

        /// <summary>필수 목표 표시 하나 → 예고 0초 → 폭풍 60초 · 떠남 10초마다 · 판정 깔끔 0 · 0 · 0초 / 빠듯 2명 · 3개 · 60초.</summary>
        private ChapterConfig StormChapters()
        {
            var chapter = Create<ChapterData>();
            var c = new SerializedObject(chapter);
            var goals = c.FindProperty("_goals");
            goals.arraySize = 1;
            goals.GetArrayElementAtIndex(0).FindPropertyRelative("_kind").enumValueIndex = (int)ChapterGoalKind.Flag;
            goals.GetArrayElementAtIndex(0).FindPropertyRelative("_flag").stringValue = "test.go";
            var climax = c.FindProperty("_climax");
            climax.FindPropertyRelative("_kind").enumValueIndex = (int)ChapterClimaxKind.StellarStorm;
            climax.FindPropertyRelative("_warningSeconds").floatValue = 0f;
            climax.FindPropertyRelative("_stormSeconds").floatValue = 60f;
            climax.FindPropertyRelative("_stormLeaveInterval").floatValue = 10f;
            climax.FindPropertyRelative("_cleanMaxDeparted").intValue = 0;
            climax.FindPropertyRelative("_cleanMaxDamaged").intValue = 0;
            climax.FindPropertyRelative("_cleanMaxShortageSeconds").floatValue = 0f;
            climax.FindPropertyRelative("_hurtMaxDeparted").intValue = 2;
            climax.FindPropertyRelative("_hurtMaxDamaged").intValue = 3;
            climax.FindPropertyRelative("_hurtMaxShortageSeconds").floatValue = 60f;
            c.ApplyModifiedPropertiesWithoutUndo();
            var config = Create<ChapterConfig>();
            var cc = new SerializedObject(config);
            var list = cc.FindProperty("_chapters");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = chapter;
            cc.ApplyModifiedPropertiesWithoutUndo();
            return config;
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim(ChapterConfig chapters = null)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                HazardRandom01 = () => 0.5f,
                Residents = _residents,
                ResidentRandom01 = () => 0.5f,
                Chapters = chapters ?? _chapters,
            });
            sim.AddSystem(new ArkShipSystem(_ark, () => 0f), TickStepNames.Population);
            sim.AddSystem(new TensionSystem(_society), TickStepNames.Population);
            sim.AddSystem(new HomeComfortSystem(_society), TickStepNames.Population);
            _rules = new Rules();
            sim.SetRules(_rules); // 장 규칙 대신 시험이 정함
            return sim;
        }

        private static ModuleInstance Place(StationSimulation sim, ModuleData data, int x, int y, int z)
        {
            Assert.IsTrue(sim.Grid.TryPlace(data, new Vector3Int(x, y, z), 0, out var m), $"{data.name} @ {x},{y},{z}");
            return m;
        }

        private static void Fill(SerializedProperty list, params ResourceType[] types)
        {
            list.arraySize = types.Length;
            for (int i = 0; i < types.Length; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Type").enumValueIndex = (int)types[i];
                e.FindPropertyRelative("Amount").floatValue = 1000f;
            }
        }

        private SpeciesData Species(string id, string name, ResidentTrait tech, float heat, float radiation)
        {
            var s = Create<SpeciesData>();
            s.EditorSet(id, name, "", ResidentNeed.None, 0f, 0f, tech, 0.02f, 0.1f, heat, radiation);
            return s;
        }

        private ModuleData Module(string name, bool removable = true, int housing = 0, float heat = 0f, int shelter = 0, float heatRemoval = 0f, int layers = 0)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            so.FindProperty("_housingCapacity").intValue = housing;
            so.FindProperty("_heatOutput").floatValue = heat;
            so.FindProperty("_heatRemoval").floatValue = heatRemoval;
            so.FindProperty("_shelterCapacity").intValue = shelter;
            so.FindProperty("_radiationLayers").intValue = layers;
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
