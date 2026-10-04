using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>Phase 10 거주자: 명단·집 배정·이탈 순서·이사·환경 특성·능력·소비·만족도 상한·세이브.</summary>
    public class ResidentTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private ModuleData _core, _habitat, _reactor;
        private ResidentConfig _residents;
        private System.Random _rng;
        private StationGradeConfig _grades;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 4;
            b.FindProperty("_startingSatisfaction").floatValue = 100f;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Water, 150), R(ResourceType.Food, 150), R(ResourceType.Metal, 150));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 200), R(ResourceType.Water, 200), R(ResourceType.Food, 200), R(ResourceType.Metal, 200));
            Fill(b.FindProperty("_consumptionPerResident"), R(ResourceType.Food, 1f));
            b.FindProperty("_repairDuration").floatValue = 100f;
            b.FindProperty("_baseRepairSlots").intValue = 1;
            b.FindProperty("_destroyAfterSeconds").floatValue = 600f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 4, removable: false);
            _habitat = Module("MD_TestHabitat", 6);
            _reactor = Module("MD_TestReactor", 0);
            _residents = Create<ResidentConfig>();
            _rng = new System.Random(7);

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_eventIntervalMultiplier").floatValue = 1f;
            var unlocks = list.GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks"); // 세이브 복원이 이름으로 찾음
            unlocks.arraySize = 2;
            unlocks.GetArrayElementAtIndex(0).objectReferenceValue = _habitat;
            unlocks.GetArrayElementAtIndex(1).objectReferenceValue = _reactor;
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
        public void Roster_FollowsPopulation_AndFillsHomes()
        {
            UseTraits(AllTraits());
            var sim = Sim();
            Assert.AreEqual(4, sim.Residents.Residents.Count);
            Assert.IsTrue(sim.Residents.Residents.All(r => r.Home == sim.Core));
            Assert.IsTrue(sim.Grid.TryPlace(_habitat, Vector3Int.right, 0, out var habitat));
            sim.Resources.SetPopulation(10);
            sim.Tick(0f);

            var list = sim.Residents.Residents;
            Assert.AreEqual(10, list.Count);
            Assert.AreEqual(4, sim.Residents.GetOccupantCount(sim.Core));
            Assert.AreEqual(6, sim.Residents.GetOccupantCount(habitat));
            Assert.AreEqual(10, list.Select(r => r.Name).Distinct().Count(), "이름 중복 없음");
            foreach (var r in list)
            {
                Assert.That(r.Traits.Count, Is.InRange(1, 2));
                Assert.IsFalse(r.Has(ResidentTrait.Optimist) && r.Has(ResidentTrait.Complainer), "모순 특성");
                Assert.IsFalse(r.Has(ResidentTrait.BigEater) && r.Has(ResidentTrait.LightEater));
            }

            sim.Resources.SetPopulation(12); // 수용 초과 → 집 없음
            sim.Tick(0f);
            Assert.AreEqual(2, sim.Residents.Residents.Count(r => r.Home == null));
        }

        [Test]
        public void Leaving_PicksMostDiscontent_NotNewest()
        {
            _config = WithStartingPopulation(1);
            UseTraits(Def(ResidentTrait.Complainer, -2f, 10f));
            var sim = Sim();
            var complainer = sim.Residents.Residents[0];
            UseTraits(Def(ResidentTrait.Optimist, 2f, 10f));
            sim.Resources.SetPopulation(4);
            sim.Tick(0f);
            Assert.AreEqual(4, sim.Residents.Residents.Count);

            sim.Resources.SetPopulation(3);
            sim.Tick(0f);
            CollectionAssert.DoesNotContain(sim.Residents.Residents, complainer, "불평꾼이 먼저 떠남");
            Assert.IsTrue(sim.Residents.Residents.All(r => r.Has(ResidentTrait.Optimist)));
        }

        [Test]
        public void ManualMove_IsPinned_UntilHomeRemoved()
        {
            UseTraits(Def(ResidentTrait.Optimist, 2f, 10f));
            var sim = Sim();
            Assert.IsTrue(sim.Grid.TryPlace(_habitat, Vector3Int.right, 0, out var a));
            Assert.IsTrue(sim.Grid.TryPlace(_habitat, Vector3Int.left, 0, out var b));
            sim.Tick(0f);
            var r = sim.Residents.Residents[0];
            var target = r.Home == b ? a : b;
            Assert.IsTrue(sim.Residents.TryMove(r, target));
            Assert.IsTrue(r.Pinned);
            sim.Tick(0f);
            Assert.AreEqual(target, r.Home, "자동 배정이 되돌리지 않음");

            Assert.IsTrue(sim.TryRemove(target));
            sim.Tick(0f);
            Assert.IsNotNull(r.Home, "남은 빈자리로 다시 배정");
            Assert.AreNotEqual(target, r.Home);
            Assert.IsFalse(r.Pinned);
        }

        [Test]
        public void Radiation_TolerantPreferIrradiated_SensitiveAvoidAndLowerCap()
        {
            _config = WithStartingPopulation(0);
            UseTraits(Def(ResidentTrait.RadiationTolerant, 0f, 0f));
            var sim = Sim();
            Assert.IsTrue(sim.Grid.TryPlace(_habitat, Vector3Int.right, 0, out var irradiated));
            Assert.IsTrue(sim.Grid.TryPlace(_reactor, new Vector3Int(2, 0, 0), 0, out _));
            sim.Resources.SetPopulation(4);
            sim.Tick(0f);
            Assert.AreNotEqual(HomeEnvironment.None, sim.Residents.GetEnvironment(irradiated) & HomeEnvironment.Radiation);
            Assert.IsTrue(sim.Residents.Residents.All(r => r.Home == irradiated), "내성 주민은 방사선 집 먼저");

            UseTraits(Def(ResidentTrait.RadiationSensitive, -3f, 15f));
            sim.Resources.SetPopulation(8);
            sim.Tick(0f);
            var sensitive = sim.Residents.Residents.Where(r => r.Has(ResidentTrait.RadiationSensitive)).ToList();
            Assert.AreEqual(4, sensitive.Count);
            Assert.IsTrue(sensitive.All(r => r.Home == sim.Core), "민감 주민은 피함");
            Assert.AreEqual(0f, sim.Residents.MoodTotal, Eps);

            Assert.IsTrue(sim.Residents.TryMove(sensitive[0], irradiated));
            sim.Tick(0f);
            Assert.AreEqual(-3f, sim.Residents.MoodTotal, Eps);
            Assert.AreEqual(97f, sim.Population.SatisfactionCap, Eps, "상한 100 − 3");
        }

        [Test]
        public void Abilities_ScaleWithCount_AndCap()
        {
            UseTraits(Def(ResidentTrait.Technician, 0.04f, 0.2f));
            var sim = Sim();
            sim.Tick(0f);
            Assert.AreEqual(0.16f, sim.Residents.Ability(ResidentTrait.Technician), Eps);
            Assert.AreEqual(84f, sim.Effects.RepairDuration, Eps, "100 × (1 − 0.16)");

            Assert.IsTrue(sim.Grid.TryPlace(_habitat, Vector3Int.right, 0, out _));
            sim.Resources.SetPopulation(10);
            sim.Tick(0f);
            Assert.AreEqual(0.2f, sim.Residents.Ability(ResidentTrait.Technician), Eps, "상한 20%");
            Assert.AreEqual(80f, sim.Effects.RepairDuration, Eps);
        }

        [Test]
        public void BigEaters_RaiseFoodConsumption()
        {
            UseTraits(Def(ResidentTrait.BigEater, 0.5f, 0f));
            var sim = Sim();
            sim.Tick(0f);
            float before = sim.Resources.GetStock(ResourceType.Food);
            sim.Tick(1f);
            Assert.AreEqual(before - 4f * 1.5f, sim.Resources.GetStock(ResourceType.Food), Eps);
        }

        [Test]
        public void SaveRoundTrip_KeepsRoster()
        {
            UseTraits(AllTraits());
            var sim = Sim();
            Assert.IsTrue(sim.Grid.TryPlace(_habitat, Vector3Int.right, 0, out var habitat));
            sim.Resources.SetPopulation(7);
            sim.Tick(0f);
            var mover = sim.Residents.Residents.First(r => r.Home == sim.Core);
            Assert.IsTrue(sim.Residents.TryMove(mover, habitat));

            var json = JsonUtility.ToJson(StationStateSerializer.Capture(sim));
            var loaded = Sim();
            StationStateSerializer.Restore(loaded, JsonUtility.FromJson<StationState>(json));

            var a = sim.Residents.Residents;
            var b = loaded.Residents.Residents;
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Name, b[i].Name);
                CollectionAssert.AreEqual(a[i].Traits, b[i].Traits);
                Assert.AreEqual(a[i].Home.Origin, b[i].Home.Origin);
                Assert.AreEqual(a[i].Pinned, b[i].Pinned);
            }

            // 명단 없는 이전 세이브: 인원수만큼 새로 생성
            var old = JsonUtility.FromJson<StationState>(json);
            old.Residents.Clear();
            var fromOld = Sim();
            StationStateSerializer.Restore(fromOld, old);
            Assert.AreEqual(7, fromOld.Residents.Residents.Count);
        }

        // ---------------- 도우미 ----------------

        private TraitDefinition Def(ResidentTrait t, float per, float max) => new TraitDefinition(t, t.ToString(), "", per >= 0f, per, max);

        private TraitDefinition[] AllTraits() => new[]
        {
            Def(ResidentTrait.Technician, 0.04f, 0.2f), Def(ResidentTrait.Scientist, 0.04f, 0.2f),
            Def(ResidentTrait.Optimist, 2f, 10f).WithConflict(ResidentTrait.Complainer), Def(ResidentTrait.Complainer, -2f, 10f),
            Def(ResidentTrait.BigEater, 0.5f, 0f).WithConflict(ResidentTrait.LightEater), Def(ResidentTrait.LightEater, -0.3f, 0f),
            Def(ResidentTrait.Sociable, 2f, 10f), Def(ResidentTrait.NatureLover, 2f, 10f),
        };

        private void UseTraits(params TraitDefinition[] traits)
        {
            _residents.EditorSet(traits, traits.Length > 1 ? 0.5f : 0f, new[] { _reactor }, new ModuleData[0], new ModuleData[0],
                new[] { "엘레나", "라지브", "민서", "카이", "소피아", "유키" }, new[] { "박", "첸", "김", "오카모토", "멘사" });
        }

        private BalanceConfig WithStartingPopulation(int population)
        {
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = population;
            b.ApplyModifiedPropertiesWithoutUndo();
            return _config;
        }

        private StationSimulation Sim()
        {
            return new StationSimulation(new StationSimulationSettings
            {
                Balance = _config,
                Grades = _grades,
                CoreModule = _core,
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                Residents = _residents,
                ResidentRandom01 = () => (float)_rng.NextDouble(),
            });
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
