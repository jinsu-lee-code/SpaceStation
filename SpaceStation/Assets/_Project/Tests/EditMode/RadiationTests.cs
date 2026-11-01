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
    /// M-120 칸 단위 방사선: 규칙 전 0 · 바깥까지 두께 · 장갑 2겹 · 방출(1칸 · 2칸 · 합산 없음 · 큰 값) · 차폐 · 폭풍 배율 · 대피 정원,
    /// 열 배율 · 방열 통로, 명단 집 환경 고리(힘든 주민 수 · 상한 · 대피 정원 · 더 나은 집으로 · 고정 · 먼저 떠남).
    /// </summary>
    public class RadiationTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ResidentConfig _residents;
        private ModuleData _core, _home, _plain, _armor, _reactor, _shield, _shelter, _hot, _radiator;

        private sealed class OpenRules : IStationRules
        {
            public StationRule Open = StationRule.Radiation;
            public bool Allows(StationRule rule) => (Open & rule) == rule;
        }

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 100000f;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.FindProperty("_heatMinGrade").intValue = 0;
            b.FindProperty("_heatFalloff1").floatValue = 0.5f;
            b.FindProperty("_heatFalloff2").floatValue = 0.25f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", removable: false);
            _home = Module("MD_TestHome", housing: 6);
            _plain = Module("MD_TestPlain");
            _armor = Module("MD_TestArmor", layers: 2);
            _reactor = Module("MD_TestReactor", output: 5f);
            _shield = Module("MD_TestShield", removal: 2f);
            _shelter = Module("MD_TestShelter", shelter: 12);
            _hot = Module("MD_TestHot", heat: 4f);
            _radiator = Module("MD_TestRadiator", heatRemoval: 4f);
            _residents = Create<ResidentConfig>();
            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            g.FindProperty("_grades").arraySize = 1;
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
        public void ClosedRule_NoRadiation()
        {
            var sim = Sim(rules: false);
            var home = Place(sim, _home, 1, 0, 0);
            sim.Tick(1f);
            Assert.IsFalse(sim.Radiation.Active, "규칙을 정하는 쪽이 없으면 등급과 무관하게 닫힘");
            Assert.AreEqual(0f, sim.Radiation.LevelOf(home));

            sim.SetRules(new OpenRules { Open = StationRule.Heat });
            sim.Tick(1f);
            Assert.IsFalse(sim.Radiation.Active);
            Assert.AreEqual(0f, sim.Radiation.LevelOf(home));
        }

        [Test]
        public void Space_FallsWithLayers_ThreeLayersIsZero()
        {
            var sim = Sim();
            var home = Place(sim, _home, 5, 5, 5);
            sim.Tick(1f);
            Assert.AreEqual(0, sim.Radiation.LayersOf(home));
            Assert.AreEqual(3f, sim.Radiation.LevelOf(home), Eps, "노출 100%");

            Shell(sim, 5, 5, 5, 1, _plain);
            sim.Tick(1f);
            Assert.AreEqual(1, sim.Radiation.LayersOf(home));
            Assert.AreEqual(1.5f, sim.Radiation.LevelOf(home), Eps, "한 겹 50%");

            Shell(sim, 5, 5, 5, 2, _plain);
            sim.Tick(1f);
            Assert.AreEqual(2, sim.Radiation.LayersOf(home));
            Assert.AreEqual(0.75f, sim.Radiation.LevelOf(home), Eps, "두 겹 25%");

            Shell(sim, 5, 5, 5, 3, _plain);
            sim.Tick(1f);
            Assert.AreEqual(3, sim.Radiation.LayersOf(home));
            Assert.AreEqual(0f, sim.Radiation.LevelOf(home), "세 겹 이상 0");
        }

        [Test]
        public void Space_ThinnestDirectionCounts_AndGapStillShields()
        {
            var sim = Sim();
            var home = Place(sim, _home, 5, 5, 5);
            Shell(sim, 5, 5, 5, 1, _plain);
            Assert.IsTrue(sim.Grid.TryGetModule(new Vector3Int(6, 5, 5), out var east));
            Assert.IsTrue(sim.TryRemove(east));
            sim.Tick(1f);
            Assert.AreEqual(0, sim.Radiation.LayersOf(home), "한 방향이라도 뚫리면 노출");

            Place(sim, _plain, 8, 5, 5); // 빈 칸 너머의 모듈도 막음
            sim.Tick(1f);
            Assert.AreEqual(1, sim.Radiation.LayersOf(home));
        }

        [Test]
        public void Armor_CountsTwoLayers()
        {
            var sim = Sim();
            var home = Place(sim, _home, 5, 5, 5);
            Shell(sim, 5, 5, 5, 1, _armor);
            sim.Tick(1f);
            Assert.AreEqual(2, sim.Radiation.LayersOf(home));
            Assert.AreEqual(0.75f, sim.Radiation.LevelOf(home), Eps);
        }

        [Test]
        public void Emission_OneTwoCells_NoStacking_TakesLargerThanSpace()
        {
            SetSpace(0f);
            var sim = Sim();
            Place(sim, _reactor, 1, 0, 0);
            var near = Place(sim, _home, 2, 0, 0);
            var far = Place(sim, _home, 3, 0, 0);
            var outside = Place(sim, _home, 4, 0, 0);
            sim.Tick(1f);
            Assert.AreEqual(5f, sim.Radiation.LevelOf(near), Eps, "1칸");
            Assert.AreEqual(2.5f, sim.Radiation.LevelOf(far), Eps, "2칸 50%");
            Assert.AreEqual(0f, sim.Radiation.LevelOf(outside), "3칸 밖");

            Place(sim, _reactor, 1, 1, 0); // 두 번째 방출 모듈도 near와 1칸 · far와 2칸
            sim.Tick(1f);
            Assert.AreEqual(5f, sim.Radiation.LevelOf(near), Eps, "합산 없이 가장 센 값");
            Assert.AreEqual(2.5f, sim.Radiation.LevelOf(far), Eps, "합산 없이 가장 센 값");

            SetSpace(3f);
            sim.Tick(1f);
            Assert.AreEqual(5f, sim.Radiation.LevelOf(near), Eps, "우주 3 < 방출 5 → 큰 값");
            Assert.AreEqual(3f, sim.Radiation.LevelOf(far), Eps, "우주 3 > 방출 2.5 → 큰 값");
        }

        [Test]
        public void Emission_OnlyRunningModules()
        {
            SetSpace(0f);
            var sim = Sim();
            var home = Place(sim, _home, -1, 0, 0);
            var reactor = Place(sim, _reactor, 1, 0, 0);
            sim.Tick(1f);
            Assert.AreEqual(2.5f, sim.Radiation.LevelOf(home), Eps);
            Assert.IsTrue(sim.Damage.Damage(reactor));
            sim.Tick(1f);
            Assert.AreEqual(0f, sim.Radiation.LevelOf(home), "파손되면 방출 없음");
        }

        [Test]
        public void Shield_RemovesFromTouchingHomes_NotBelowZero()
        {
            var sim = Sim();
            var home = Place(sim, _home, 1, 0, 0);
            Place(sim, _shield, 2, 0, 0);
            sim.Tick(1f);
            Assert.AreEqual(1f, sim.Radiation.LevelOf(home), Eps, "3 - 2");
            Place(sim, _shield, 1, 1, 0);
            sim.Tick(1f);
            Assert.AreEqual(0f, sim.Radiation.LevelOf(home), "0 아래로 안 내려감");
        }

        [Test]
        public void SpaceMultiplier_RaisesSpaceOnly()
        {
            var sim = Sim();
            var home = Place(sim, _home, 5, 5, 5);
            sim.Radiation.SpaceMultiplier = 3f;
            sim.Tick(1f);
            Assert.AreEqual(9f, sim.Radiation.LevelOf(home), Eps);

            SetSpace(0f);
            var reactorHome = Place(sim, _home, 1, 0, 0);
            Place(sim, _reactor, 2, 0, 0);
            sim.Tick(1f);
            Assert.AreEqual(5f, sim.Radiation.LevelOf(reactorHome), Eps, "방출은 폭풍 배율을 받지 않음");
        }

        [Test]
        public void Shelter_CountsRunningConnectedOnly()
        {
            var sim = Sim();
            var a = Place(sim, _shelter, 1, 0, 0);
            Place(sim, _shelter, 9, 9, 9); // 코어와 끊김
            sim.Tick(1f);
            Assert.AreEqual(12, sim.Radiation.ShelterCapacity);
            Assert.IsTrue(sim.Damage.Damage(a));
            sim.Tick(1f);
            Assert.AreEqual(0, sim.Radiation.ShelterCapacity);
        }

        [Test]
        public void Heat_ScalesAndCoolerPath()
        {
            var sim = Sim(rules: false); // 열은 등급 조건 (시험 0)
            var hot = Place(sim, _hot, 1, 0, 0);
            sim.Tick(1f);
            Assert.AreEqual(4f, sim.Heat.HeatOf(hot), Eps);
            sim.Heat.OutputScale = 1.5f;
            sim.Tick(1f);
            Assert.AreEqual(6f, sim.Heat.HeatOf(hot), Eps, "발열 배율");

            Place(sim, _radiator, 2, 0, 0);
            sim.Tick(1f);
            Assert.AreEqual(2f, sim.Heat.HeatOf(hot), Eps, "6 - 4");
            sim.Heat.RemovalScale = 0.5f;
            sim.Tick(1f);
            Assert.AreEqual(4f, sim.Heat.HeatOf(hot), Eps, "방열 배율 6 - 2");
            sim.Effects.TraitHeatRemovalMultiplier = 1.5f;
            sim.Tick(1f);
            Assert.AreEqual(3f, sim.Heat.HeatOf(hot), Eps, "방열 통로 6 - 3");
        }

        [Test]
        public void Roster_Stress_CountsCapsExemptsMovesAndLeavesFirst()
        {
            var sim = Sim(residents: true);
            sim.Resources.SetPopulation(4);
            var bad = Place(sim, _home, 1, 0, 0);
            sim.Tick(0f);
            var roster = sim.Residents;
            Assert.AreEqual(4, roster.GetOccupantCount(bad));
            Assert.AreEqual(0, roster.StressedCount, "고리가 없으면 0");
            Assert.AreEqual(0f, roster.MoodTotal);

            roster.HomeStress = (r, home) => home == bad ? 2f : 0f;
            roster.StressMoodPerResident = 1f;
            roster.StressMoodMax = 3f;
            sim.Tick(0f);
            Assert.AreEqual(4, roster.StressedCount, "옮길 집이 없음");
            Assert.AreEqual(-3f, roster.StressMood, Eps, "합계 최대");
            Assert.AreEqual(-3f, roster.MoodTotal, Eps);

            roster.StressExempt = 2;
            sim.Tick(0f);
            Assert.AreEqual(2, roster.StressedCount, "대피 정원만큼 뺌");
            Assert.AreEqual(-2f, roster.MoodTotal, Eps);
            roster.StressExempt = 0;

            var good = Place(sim, _home, -1, 0, 0);
            sim.Tick(0f);
            Assert.AreEqual(4, roster.GetOccupantCount(good), "더 나은 빈집으로 저절로");
            Assert.AreEqual(0, roster.StressedCount);
            Assert.AreEqual(0f, roster.MoodTotal);

            var pinned = roster.Residents[1];
            Assert.IsTrue(roster.TryMove(pinned, bad));
            sim.Tick(0f);
            Assert.AreEqual(bad, pinned.Home, "고정한 주민은 그대로");
            Assert.AreEqual(1, roster.StressedCount);
            Assert.AreEqual(-1f, roster.MoodAt(roster.Residents[0], bad), Eps, "이사 미리보기");

            sim.Resources.SetPopulation(3);
            sim.Tick(0f);
            Assert.IsFalse(new List<Resident>(roster.Residents).Contains(pinned), "힘든 주민이 먼저 떠남");
        }

        [Test]
        public void Save_KeepsAnnouncedOnly()
        {
            var sim = Sim();
            sim.Tick(1f);
            string state = sim.Radiation.Capture(m => -1);
            Assert.IsNotNull(state, "안내를 했으면 저장");
            var other = Sim();
            other.Radiation.Restore(state, i => null);
            Assert.IsNotNull(other.Radiation.Capture(m => -1));
            Assert.IsNull(Sim().Radiation.Capture(m => -1), "안내 전이면 저장 없음");
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim(bool rules = true, bool residents = false)
        {
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                HazardRandom01 = () => 0.5f,
                Residents = residents ? _residents : null,
                ResidentRandom01 = residents ? () => 0.5f : (System.Func<float>)null,
            });
            if (rules)
                sim.SetRules(new OpenRules());
            return sim;
        }

        private void SetSpace(float value)
        {
            var b = new SerializedObject(_config);
            b.FindProperty("_radiationSpace").floatValue = value;
            b.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ModuleInstance Place(StationSimulation sim, ModuleData data, int x, int y, int z)
        {
            Assert.IsTrue(sim.Grid.TryPlace(data, new Vector3Int(x, y, z), 0, out var m), $"{data.name} @ {x},{y},{z}");
            return m;
        }

        /// <summary>(x, y, z)에서 distance칸 떨어진 6방향에 모듈 하나씩.</summary>
        private static void Shell(StationSimulation sim, int x, int y, int z, int distance, ModuleData data)
        {
            foreach (var d in GridDirections.Faces)
                Place(sim, data, x + d.x * distance, y + d.y * distance, z + d.z * distance);
        }

        private ModuleData Module(string name, bool removable = true, int housing = 0, float output = 0f, int layers = 0, float removal = 0f, int shelter = 0, float heat = 0f, float heatRemoval = 0f)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            so.FindProperty("_housingCapacity").intValue = housing;
            so.FindProperty("_radiationOutput").floatValue = output;
            so.FindProperty("_radiationLayers").intValue = layers;
            so.FindProperty("_radiationRemoval").floatValue = removal;
            so.FindProperty("_shelterCapacity").intValue = shelter;
            so.FindProperty("_heatOutput").floatValue = heat;
            so.FindProperty("_heatRemoval").floatValue = heatRemoval;
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
