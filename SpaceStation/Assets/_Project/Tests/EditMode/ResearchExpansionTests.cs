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
    /// 12-4 연구 확장: 설정 없으면 자동 해금만 · 카테고리 Lv.5에서 그림자 · 레시피 연구(슬롯 · 포인트 · 비용 · 해금) · 슬롯 부족 멈춤 ·
    /// 연계 연구(모듈 필요 · 일반 슬롯 · 효과는 모듈 가동 중에만) · 세이브.
    /// </summary>
    public class ResearchExpansionTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ResearchCategoryData _production;
        private ModuleData _core, _lab, _complex, _eco, _farm;
        private RecipeData _auto, _ecoRecipe, _ecoRecipe2;
        private LinkedResearchData _closedLoop;
        private ResearchExpansionConfig _expansion;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 10;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 800), R(ResourceType.Metal, 900), R(ResourceType.Food, 800), R(ResourceType.Water, 800));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 1000), R(ResourceType.Metal, 1000), R(ResourceType.Food, 1000), R(ResourceType.Water, 1000));
            Fill(b.FindProperty("_consumptionPerResident"), R(ResourceType.Oxygen, 1f), R(ResourceType.Water, 1f));
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_showerMinGrade").intValue = 99;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 0, 0);
            var cso = new SerializedObject(_core);
            cso.FindProperty("_removable").boolValue = false;
            cso.FindProperty("_housingCapacity").intValue = 100;
            cso.ApplyModifiedPropertiesWithoutUndo();
            _lab = Module("MD_TestLab", 1, 0);
            _complex = Module("MD_TestComplex", 0, 1);
            _eco = Module("MD_TestEco", 0, 0);
            _farm = Module("MD_TestFarm", 0, 0);

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minPopulation").intValue = 0;
            list.GetArrayElementAtIndex(0).FindPropertyRelative("_minModules").intValue = 0;
            g.ApplyModifiedPropertiesWithoutUndo();

            _production = Create<ResearchCategoryData>();
            _production.name = "RC_TestProduction";
            var levels = new List<ResearchLevel>();
            for (int i = 0; i < 5; i++)
                levels.Add(new ResearchLevel { Duration = 10f, PowerDemand = 0f });
            _production.EditorSet(ResearchCategory.Production, "생산", "production", levels);

            _auto = Recipe("RC_TestAuto", true, 1);
            _ecoRecipe = Recipe("RC_TestEco", false, 1);
            _ecoRecipe2 = Recipe("RC_TestEco2", false, 1);

            _closedLoop = Create<LinkedResearchData>();
            _closedLoop.name = "LR_TestClosedLoop";
            _closedLoop.EditorSet("폐쇄 순환", ResearchCategory.Production, _eco, new ResearchLevel
            {
                StartCost = new List<ResourceAmount> { R(ResourceType.Metal, 100) },
                Duration = 20f,
                PowerDemand = 0f,
                Modifiers = new List<ResearchModifier> { new ResearchModifier { Stat = ResearchStat.ResidentLifeConsumption, Value = 0.9f } },
            });

            _expansion = Create<ResearchExpansionConfig>();
            _expansion.EditorSet(new List<RecipeResearchCost>
            {
                new RecipeResearchCost { StartCost = new List<ResourceAmount> { R(ResourceType.Metal, 150) }, ResearchPoints = 1, PowerDemand = 0f, Duration = 30f },
            }, new List<LinkedResearchData> { _closedLoop });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void NoConfig_OnlyAutoUnlockRecipes()
        {
            var sim = Sim(null);
            Assert.IsNull(sim.ResearchExpansion);
            Assert.IsTrue(sim.Synthesis.IsUnlocked(_auto));
            Assert.IsFalse(sim.Synthesis.IsUnlocked(_ecoRecipe));
        }

        [Test]
        public void CategoryLevel5_RevealsShadow_ResearchUnlocks()
        {
            var sim = Sim(_expansion);
            var ex = sim.ResearchExpansion;
            Assert.AreEqual(RecipeState.Unlocked, ex.StateOf(_auto), "연구소 계통 1단계는 자동 해금");
            sim.Research.SetLevel(_production, 4);
            Assert.AreEqual(RecipeState.Hidden, ex.StateOf(_ecoRecipe), "Lv.4까지는 미공개");
            sim.Research.SetLevel(_production, 5);
            Assert.AreEqual(RecipeState.Shadow, ex.StateOf(_ecoRecipe), "Lv.5 = 1단계 그림자");
            Assert.IsFalse(sim.Synthesis.IsUnlocked(_ecoRecipe));

            Assert.AreEqual(ExpansionStartResult.NoFreeSlot, ex.CanStart(_ecoRecipe), "레시피 연구 슬롯 없음");
            Place(sim, _complex, new Vector3Int(1, 0, 0));
            Assert.AreEqual(ExpansionStartResult.NoResearchPoints, ex.CanStart(_ecoRecipe));
            sim.Supply.SetCounts(2, 0);
            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.AreEqual(ExpansionStartResult.Ok, ex.TryStart(_ecoRecipe));
            Assert.AreEqual(metal - 150f, sim.Resources.GetStock(ResourceType.Metal), 0.5f);
            Assert.AreEqual(1, sim.Supply.ResearchPoints, "연구 포인트 1 사용");
            sim.Tick(31f);
            Assert.AreEqual(RecipeState.Unlocked, ex.StateOf(_ecoRecipe));
            Assert.IsTrue(sim.Synthesis.IsUnlocked(_ecoRecipe));
        }

        [Test]
        public void RecipeSlots_ExtraProjectsPause()
        {
            var sim = Sim(_expansion);
            var ex = sim.ResearchExpansion;
            sim.Research.SetLevel(_production, 5);
            Place(sim, _complex, new Vector3Int(1, 0, 0));
            sim.Supply.SetCounts(5, 0);
            Assert.AreEqual(ExpansionStartResult.Ok, ex.TryStart(_ecoRecipe));
            Assert.AreEqual(ExpansionStartResult.NoFreeSlot, ex.CanStart(_ecoRecipe2), "슬롯 1개 = 하나씩");
            sim.Tick(10f);
            Assert.AreEqual(1f / 3f, ex.ProjectOf(_ecoRecipe).Progress, 0.02f);
        }

        [Test]
        public void LinkedResearch_NeedsModule_EffectOnlyWhileActive()
        {
            var sim = Sim(_expansion);
            var ex = sim.ResearchExpansion;
            Place(sim, _lab, new Vector3Int(-1, 0, 0));
            sim.Tick(0.1f);
            Assert.AreEqual(ExpansionStartResult.ModuleMissing, ex.CanStart(_closedLoop));
            var eco = Place(sim, _eco, new Vector3Int(1, 0, 0));
            Assert.AreEqual(ExpansionStartResult.Ok, ex.TryStart(_closedLoop));
            Assert.IsFalse(sim.Research.HasFreeSlot, "연계 연구가 일반 슬롯을 씀");
            sim.Tick(21f);
            Assert.IsTrue(ex.IsDone(_closedLoop));
            Assert.IsTrue(ex.IsActive(_closedLoop));
            Assert.AreEqual(0.9f, sim.Effects.ResidentLifeConsumption, 1e-4f);
            Assert.AreEqual(0.9f, sim.Resources.GetResidentConsumptionMultiplier(ResourceType.Oxygen), 1e-4f, "주민 산소 소비 -10%");

            Assert.IsTrue(sim.TryRemove(eco));
            sim.Tick(0.1f);
            Assert.IsFalse(ex.IsActive(_closedLoop), "모듈을 잃으면 효과 꺼짐");
            Assert.AreEqual(1f, sim.Effects.ResidentLifeConsumption, 1e-4f);
        }

        [Test]
        public void SaveRestore_KeepsUnlockedAndProjects()
        {
            var sim = Sim(_expansion);
            var ex = sim.ResearchExpansion;
            sim.Research.SetLevel(_production, 5);
            Place(sim, _complex, new Vector3Int(1, 0, 0));
            sim.Supply.SetCounts(5, 0);
            ex.TryStart(_ecoRecipe);
            sim.Tick(31f);
            ex.TryStart(_ecoRecipe2);
            sim.Tick(5f);
            string state = ex.Capture(_ => -1);

            var loaded = Sim(_expansion);
            loaded.Research.SetLevel(_production, 5);
            loaded.ResearchExpansion.Restore(state, _ => null);
            Assert.AreEqual(RecipeState.Unlocked, loaded.ResearchExpansion.StateOf(_ecoRecipe));
            Assert.IsNotNull(loaded.ResearchExpansion.ProjectOf(_ecoRecipe2));
            Assert.AreEqual(ex.ProjectOf(_ecoRecipe2).Progress, loaded.ResearchExpansion.ProjectOf(_ecoRecipe2).Progress, 1e-3f);
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim(ResearchExpansionConfig expansion) => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
            HazardRandom01 = () => 0.5f,
            ResearchCategories = new List<ResearchCategoryData> { _production },
            Recipes = new List<RecipeData> { _auto, _ecoRecipe, _ecoRecipe2 },
            ResearchExpansion = expansion,
        });

        private static ModuleInstance Place(StationSimulation sim, ModuleData data, Vector3Int at)
        {
            Assert.IsTrue(sim.Grid.TryPlace(data, at, 0, out var m));
            return m;
        }

        private ModuleData Module(string name, int researchSlots, int recipeSlots)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = true;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = 1;
            offsets.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
            so.FindProperty("_researchSlots").intValue = researchSlots;
            so.FindProperty("_recipeSlots").intValue = recipeSlots;
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private RecipeData Recipe(string name, bool auto, int stage)
        {
            var r = Create<RecipeData>();
            r.name = name;
            var so = new SerializedObject(r);
            so.FindProperty("_result").objectReferenceValue = _eco;
            var ing = so.FindProperty("_ingredients");
            ing.arraySize = 1;
            ing.GetArrayElementAtIndex(0).FindPropertyRelative("Module").objectReferenceValue = _farm;
            ing.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 1;
            so.FindProperty("_stage").intValue = stage;
            so.FindProperty("_unlockGrade").intValue = 0;
            so.FindProperty("_unlockedByDefault").boolValue = auto;
            var cat = so.FindProperty("_researchCategory");
            cat.enumValueIndex = System.Array.IndexOf(cat.enumNames, "Production");
            so.ApplyModifiedPropertiesWithoutUndo();
            return r;
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
