using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>BALANCE.md 16번 인접 규칙.</summary>
    public class AdjacencyTests
    {
        private const float Eps = 1e-3f;
        private readonly List<Object> _created = new List<Object>();
        private ModuleData _core, _farm, _water, _oxygen, _solar, _habitat, _dock, _block;
        private AdjacencyRuleSet _rules;
        private BalanceConfig _balance;
        private StationGradeConfig _grades;

        [SetUp]
        public void SetUp()
        {
            _core = Module("Core", removable: false, prod: R(ResourceType.Power, 100));
            _farm = Module("Farm", prod: R(ResourceType.Food, 2), cons: R(ResourceType.Water, 1));
            _water = Module("Water", prod: R(ResourceType.Water, 2));
            _oxygen = Module("Oxygen", prod: R(ResourceType.Oxygen, 3));
            _solar = Module("Solar", prod: R(ResourceType.Power, 10));
            _habitat = Module("Habitat", housing: 6);
            _dock = Module("Dock", prod: R(ResourceType.Metal, 1));
            _block = Module("Block");

            _rules = Create<AdjacencyRuleSet>();
            var so = new SerializedObject(_rules);
            var list = so.FindProperty("_rules");
            list.arraySize = 5;
            Rule(list.GetArrayElementAtIndex(0), _farm, _water, AdjacencyEffect.Consumption, -0.25f, 0, "수로 공유");
            Rule(list.GetArrayElementAtIndex(1), _oxygen, _farm, AdjacencyEffect.Production, 0.15f, 0, "광합성");
            Rule(list.GetArrayElementAtIndex(2), _solar, null, AdjacencyEffect.Production, -0.10f, 1, "그늘");
            Rule(list.GetArrayElementAtIndex(3), _habitat, _dock, AdjacencyEffect.Housing, -2f, 0, "소음");
            Rule(list.GetArrayElementAtIndex(4), _habitat, _habitat, AdjacencyEffect.Housing, 1f, 0, "주거 단지");
            so.FindProperty("_minMultiplier").floatValue = 0f;
            so.FindProperty("_maxMultiplier").floatValue = 2f;
            so.ApplyModifiedPropertiesWithoutUndo();

            _balance = Create<BalanceConfig>();
            var b = new SerializedObject(_balance);
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Water, 100), R(ResourceType.Metal, 1000));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Water, 1000), R(ResourceType.Food, 1000), R(ResourceType.Oxygen, 1000), R(ResourceType.Metal, 1000));
            b.FindProperty("_eventGracePeriod").floatValue = 10000f;
            b.FindProperty("_eventIntervalMin").floatValue = 90f;
            b.FindProperty("_eventIntervalMax").floatValue = 150f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _grades = Create<StationGradeConfig>();
            var g = new SerializedObject(_grades);
            var grades = g.FindProperty("_grades");
            grades.arraySize = 1;
            var unlocks = grades.GetArrayElementAtIndex(0).FindPropertyRelative("_unlocks");
            var all = new[] { _farm, _water, _oxygen, _solar, _habitat, _dock, _block };
            unlocks.arraySize = all.Length;
            for (int i = 0; i < all.Length; i++)
                unlocks.GetArrayElementAtIndex(i).objectReferenceValue = all[i];
            g.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        private StationSimulation Sim() => new StationSimulation(new StationSimulationSettings
        {
            Balance = _balance, Grades = _grades, CoreModule = _core, Events = new GameEventData[0],
            AdjacencyRules = _rules, Random01 = () => 0f,
        });

        [Test]
        public void Evaluate_StacksPerNeighbor_UpToMax()
        {
            var adj = new AdjacencySystem(_rules);
            var results = new List<AppliedAdjacency>();
            adj.Evaluate(_farm, new[] { _water, _water, _block }, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(2, results[0].Stacks);
            Assert.AreEqual(-0.5f, results[0].Total, Eps);

            adj.Evaluate(_farm, new[] { _water, _water, _water, _water, _water }, results);
            Assert.AreEqual(3, results[0].Stacks, "최대 3회");
        }

        [Test]
        public void SolarShade_FirstNeighborFree_AnyModuleCounts()
        {
            var adj = new AdjacencySystem(_rules);
            var results = new List<AppliedAdjacency>();
            adj.Evaluate(_solar, new[] { _block }, results);
            Assert.AreEqual(0, results.Count, "1개는 면제");
            adj.Evaluate(_solar, new[] { _block, _water, _farm }, results);
            Assert.AreEqual(2, results[0].Stacks);
            Assert.AreEqual(-0.2f, results[0].Total, Eps);
        }

        [Test]
        public void SolarShade_OtherSolarPanelsDoNotShade()
        {
            var adj = new AdjacencySystem(_rules);
            var results = new List<AppliedAdjacency>();
            adj.Evaluate(_solar, new[] { _solar, _solar, _solar, _block }, results);
            Assert.AreEqual(0, results.Count, "태양광 이웃은 세지 않음 → 비태양광 1개는 면제");
            adj.Evaluate(_solar, new[] { _solar, _block, _water }, results);
            Assert.AreEqual(1, results[0].Stacks);
        }

        [Test]
        public void FindProductionBooster_ReturnsPositiveProductionNeighbor()
        {
            var adj = new AdjacencySystem(_rules);
            Assert.AreEqual(_farm, adj.FindProductionBooster(_oxygen), "광합성: 산소 ← 농장");
            Assert.IsNull(adj.FindProductionBooster(_solar), "그늘은 감소 + 이웃 지정 없음");
            Assert.IsNull(adj.FindProductionBooster(_farm), "소비 규칙은 해당 없음");
            Assert.IsNull(new AdjacencySystem(null).FindProductionBooster(_oxygen));
        }

        [Test]
        public void Integration_FarmNextToWater_ConsumesLess()
        {
            var sim = Sim();
            sim.TryPlace(_farm, Vector3Int.right, 0, out var farm);
            sim.TryPlace(_water, new Vector3Int(2, 0, 0), 0, out _);
            Assert.AreEqual(0.75f, sim.Adjacency.GetConsumptionMultiplier(farm), Eps);
            sim.Tick(1f);
            Assert.AreEqual(0.75f, sim.Resources.GetConsumption(ResourceType.Water), Eps);
        }

        [Test]
        public void Integration_OxygenNextToFarm_ProducesMore()
        {
            var sim = Sim();
            sim.TryPlace(_oxygen, Vector3Int.right, 0, out _);
            sim.TryPlace(_farm, new Vector3Int(2, 0, 0), 0, out _);
            sim.Tick(1f);
            Assert.AreEqual(3f * 1.15f, sim.Resources.GetProduction(ResourceType.Oxygen), Eps);
        }

        [Test]
        public void Integration_HabitatCluster_AndNoise()
        {
            var sim = Sim();
            sim.TryPlace(_habitat, Vector3Int.right, 0, out _);
            Assert.AreEqual(6, sim.Resources.HousingCapacity);
            sim.TryPlace(_habitat, new Vector3Int(2, 0, 0), 0, out _);
            Assert.AreEqual(6 + 6 + 2, sim.Resources.HousingCapacity, "서로 +1씩");
            sim.TryPlace(_dock, new Vector3Int(1, 1, 0), 0, out _);
            Assert.AreEqual(6 + 6 + 2 - 2, sim.Resources.HousingCapacity, "도킹 옆 거주 -2");
        }

        [Test]
        public void RemovingNeighbor_RemovesEffect()
        {
            var sim = Sim();
            sim.TryPlace(_farm, Vector3Int.right, 0, out var farm);
            sim.TryPlace(_water, new Vector3Int(2, 0, 0), 0, out var water);
            sim.TryRemove(water);
            Assert.AreEqual(1f, sim.Adjacency.GetConsumptionMultiplier(farm), Eps);
        }

        [Test]
        public void Preview_ShowsSelfAndNeighborEffects()
        {
            var sim = Sim();
            sim.TryPlace(_farm, Vector3Int.right, 0, out _);
            var self = new List<AppliedAdjacency>();
            var neighbors = new List<string>();
            // 농장 옆에 산소 생성기: 자신 산소 +15%
            sim.PreviewAdjacency(_oxygen, new Vector3Int(2, 0, 0), 0, self, neighbors);
            Assert.AreEqual(1, self.Count);
            Assert.AreEqual(0.15f, self[0].Total, Eps);
            // 농장 옆에 물 재활용기: 이웃(농장) 물 소비 -25%
            sim.PreviewAdjacency(_water, new Vector3Int(2, 0, 0), 0, self, neighbors);
            Assert.AreEqual(0, self.Count);
            Assert.AreEqual(1, neighbors.Count);
            StringAssert.Contains("-25%", neighbors[0]);
        }

        [Test]
        public void Preview_Structured_GivesNeighborModuleAndBenefit()
        {
            var sim = Sim();
            sim.TryPlace(_farm, Vector3Int.right, 0, out var farm);
            var self = new List<AppliedAdjacency>();
            var neighbors = new List<NeighborAdjacencyPreview>();
            sim.PreviewAdjacency(_water, new Vector3Int(2, 0, 0), 0, self, neighbors);
            Assert.AreEqual(1, neighbors.Count);
            Assert.AreSame(farm, neighbors[0].Module);
            Assert.AreEqual(1, neighbors[0].AddedStacks);
            Assert.AreEqual(-0.25f, neighbors[0].Total, Eps);
            Assert.IsTrue(AdjacencySystem.IsBeneficial(neighbors[0].Rule, neighbors[0].Total), "소비 −25%는 좋은 효과");
            Assert.AreEqual("-25%", AdjacencySystem.ShortValue(neighbors[0].Rule, neighbors[0].Total));
            Assert.AreEqual(ResourceType.Water, AdjacencySystem.EffectResource(neighbors[0].Rule));

            sim.PreviewAdjacency(_oxygen, new Vector3Int(2, 0, 0), 0, self, neighbors);
            Assert.AreEqual(1, self.Count);
            Assert.IsTrue(AdjacencySystem.IsBeneficial(self[0].Rule, self[0].Total), "생산 +15%");
            Assert.AreEqual(ResourceType.Oxygen, AdjacencySystem.EffectResource(self[0].Rule));
        }

        // ---- helpers ----

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }

        private ModuleData Module(string name, bool removable = true, int housing = 0, ResourceAmount? prod = null, ResourceAmount? cons = null)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_displayName").stringValue = name;
            so.FindProperty("_removable").boolValue = removable;
            so.FindProperty("_housingCapacity").intValue = housing;
            if (prod.HasValue) Fill(so.FindProperty("_production"), prod.Value);
            if (cons.HasValue) Fill(so.FindProperty("_consumption"), cons.Value);
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private static void Rule(SerializedProperty r, ModuleData target, ModuleData neighbor, AdjacencyEffect effect, float value, int free, string label)
        {
            r.FindPropertyRelative("_target").objectReferenceValue = target;
            r.FindPropertyRelative("_neighbor").objectReferenceValue = neighbor;
            r.FindPropertyRelative("_effect").enumValueIndex = (int)effect;
            r.FindPropertyRelative("_valuePerNeighbor").floatValue = value;
            r.FindPropertyRelative("_freeNeighbors").intValue = free;
            r.FindPropertyRelative("_maxStacks").intValue = 3;
            r.FindPropertyRelative("_label").stringValue = label;
            r.FindPropertyRelative("_excludeSameType").boolValue = target != null && neighbor == null; // 그늘: 태양광끼리 제외
        }

        private static ResourceAmount R(ResourceType t, float a) => new ResourceAmount(t, a);

        private static void Fill(SerializedProperty list, params ResourceAmount[] values)
        {
            list.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                list.GetArrayElementAtIndex(i).FindPropertyRelative("Type").enumValueIndex = (int)values[i].Type;
                list.GetArrayElementAtIndex(i).FindPropertyRelative("Amount").floatValue = values[i].Amount;
            }
        }
    }
}
