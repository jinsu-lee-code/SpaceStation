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
    /// CONNECT_1121 A2 · A8 인접 태그 틀 (2026-11-27): 태그만으로 정한 규칙 · 모듈과 태그를 함께 정한 규칙(둘 다 맞아야 함) ·
    /// 같은 종류 제외 · 생산 짝 찾기 · 설명 · 실제 데이터(기본 36종 모두 태그 · 규칙의 태그가 그 모듈에 있음).
    /// </summary>
    public class AdjacencyTagTests
    {
        private readonly List<Object> _created = new List<Object>();
        private AdjacencyTag _waterUse, _waterSource, _greenery;
        private ModuleData _farm, _oxygen, _recycler, _extractor, _solar;

        [SetUp]
        public void SetUp()
        {
            _waterUse = Tag("물 사용");
            _waterSource = Tag("수원");
            _greenery = Tag("녹지");
            _farm = Module("MD_TFarm", ResourceType.Food, _greenery, _waterUse);
            _oxygen = Module("MD_TOxygen", ResourceType.Oxygen, _waterUse);
            _recycler = Module("MD_TRecycler", ResourceType.Water, _waterSource);
            _extractor = Module("MD_TExtractor", ResourceType.Water, _waterSource);
            _solar = Module("MD_TSolar", ResourceType.Power);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void TagOnlyRule_AppliesToEveryTaggedTarget_AndCountsTaggedNeighbors()
        {
            var system = new AdjacencySystem(Rules(Rule(null, _waterUse, null, _waterSource, AdjacencyEffect.Consumption, -0.25f, 3)));
            var applied = new List<AppliedAdjacency>();

            system.Evaluate(_farm, new[] { _recycler, _extractor, _solar }, applied);
            Assert.AreEqual(1, applied.Count);
            Assert.AreEqual(2, applied[0].Stacks, "수원 태그 둘 (재활용기 · 추출기)");
            Assert.AreSame(_farm, applied[0].Target, "적용된 모듈 종류를 기억");

            system.Evaluate(_oxygen, new[] { _recycler }, applied);
            Assert.AreEqual(1, applied.Count, "물 사용 태그면 다른 모듈에도");

            system.Evaluate(_solar, new[] { _recycler }, applied);
            Assert.AreEqual(0, applied.Count, "태그 없는 모듈은 대상 아님");

            StringAssert.Contains("소비", AdjacencySystem.Describe(applied.Count > 0 ? applied[0] : Applied(system, _farm, _recycler)));
        }

        [Test]
        public void ModuleAndTag_BothMustMatch()
        {
            // 대상 = 수경 농장 + 물 사용, 이웃 = 물 재활용기 + 수원 (기존 규칙 재현 방식)
            var rule = Rule(_farm, _waterUse, _recycler, _waterSource, AdjacencyEffect.Consumption, -0.25f, 3);
            var system = new AdjacencySystem(Rules(rule));
            var applied = new List<AppliedAdjacency>();

            system.Evaluate(_farm, new[] { _recycler, _extractor }, applied);
            Assert.AreEqual(1, applied[0].Stacks, "추출기는 수원이지만 모듈이 달라 안 셈");
            system.Evaluate(_oxygen, new[] { _recycler }, applied);
            Assert.AreEqual(0, applied.Count, "물 사용이지만 대상 모듈이 다름");

            // 태그가 빠지면 규칙이 꺼짐 (데이터 실수를 잡음)
            var bare = Module("MD_TBareFarm", ResourceType.Food);
            Assert.IsFalse(rule.AppliesTo(bare));
            Assert.IsFalse(Rule(bare, _waterUse, null, null, AdjacencyEffect.Production, 0.1f, 1).AppliesTo(bare));
            Assert.AreEqual("물 사용 ← 수원", rule.TagRelation);
        }

        [Test]
        public void ExcludeSameType_WithTagTarget()
        {
            var sun = Tag("집광");
            var solar = Module("MD_TSolar2", ResourceType.Power, sun);
            var system = new AdjacencySystem(Rules(Rule(null, sun, null, null, AdjacencyEffect.Production, -0.1f, 3, free: 1, excludeSame: true)));
            var applied = new List<AppliedAdjacency>();
            system.Evaluate(solar, new[] { solar, solar, _farm, _oxygen, _recycler }, applied);
            Assert.AreEqual(2, applied[0].Stacks, "같은 종류 둘은 빼고 셋 중 1개 면제");
        }

        [Test]
        public void ProductionBooster_TagNeighbor_NeedsCandidates()
        {
            var system = new AdjacencySystem(Rules(Rule(null, _greenery, null, _waterSource, AdjacencyEffect.Production, 0.5f, 1)));
            Assert.IsNull(system.FindProductionBooster(_farm), "후보 목록이 없으면 태그 이웃은 모름");
            Assert.AreSame(_recycler, system.FindProductionBooster(_farm, new[] { _solar, _recycler, _extractor }));

            var lines = new List<string>();
            system.DescribeRulesFor(_extractor, lines);
            Assert.AreEqual(1, lines.Count, "이웃 쪽 설명 (태그로 맞음)");
            StringAssert.Contains("녹지", lines[0]);
        }

        [Test]
        public void RealData_AllBasicModulesTagged_RuleTagsMatchModules()
        {
            var guids = AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data/Modules" });
            int basic = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Evolved/"))
                    continue;
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(path);
                basic++;
                Assert.Greater(data.AdjacencyTags.Count, 0, $"{data.name}: 기본 모듈은 모두 태그가 하나 이상 (A3)");
            }
            Assert.GreaterOrEqual(basic, 36);

            var set = AssetDatabase.LoadAssetAtPath<AdjacencyRuleSet>("Assets/_Project/Data/AdjacencyRules.asset");
            Assert.IsNotNull(set);
            int tagged = 0;
            foreach (var rule in set.Rules)
            {
                if (rule.Target != null && rule.TargetTag != null)
                    Assert.IsTrue(rule.Target.HasAdjacencyTag(rule.TargetTag), $"{rule.Label}: 대상 모듈에 대상 태그");
                if (rule.Neighbor != null && rule.NeighborTag != null)
                    Assert.IsTrue(rule.Neighbor.HasAdjacencyTag(rule.NeighborTag), $"{rule.Label}: 이웃 모듈에 이웃 태그");
                if (rule.TargetTag != null)
                    tagged++;
            }
            Assert.GreaterOrEqual(tagged, 12, "기존 12개 규칙에 태그");
        }

        // ---------------- 도우미 ----------------

        private static AppliedAdjacency Applied(AdjacencySystem system, ModuleData target, ModuleData neighbor)
        {
            var list = new List<AppliedAdjacency>();
            system.Evaluate(target, new[] { neighbor }, list);
            return list[0];
        }

        private AdjacencyTag Tag(string name)
        {
            var t = Create<AdjacencyTag>();
            t.EditorSet(name, "");
            return t;
        }

        private ModuleData Module(string name, ResourceType produces, params AdjacencyTag[] tags)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            var prod = so.FindProperty("_production");
            prod.arraySize = 1;
            prod.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)produces;
            prod.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 1f;
            var cons = so.FindProperty("_consumption");
            cons.arraySize = 1;
            cons.GetArrayElementAtIndex(0).FindPropertyRelative("Type").enumValueIndex = (int)ResourceType.Water;
            cons.GetArrayElementAtIndex(0).FindPropertyRelative("Amount").floatValue = 1f;
            var list = so.FindProperty("_adjacencyTags");
            list.arraySize = tags.Length;
            for (int i = 0; i < tags.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = tags[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private AdjacencyRule Rule(ModuleData target, AdjacencyTag targetTag, ModuleData neighbor, AdjacencyTag neighborTag,
            AdjacencyEffect effect, float value, int max, int free = 0, bool excludeSame = false)
        {
            // 규칙은 직렬화 클래스라 규칙 묶음에 넣어 값을 채움
            var holder = Create<AdjacencyRuleSet>();
            var so = new SerializedObject(holder);
            var list = so.FindProperty("_rules");
            list.arraySize = 1;
            var r = list.GetArrayElementAtIndex(0);
            r.FindPropertyRelative("_target").objectReferenceValue = target;
            r.FindPropertyRelative("_targetTag").objectReferenceValue = targetTag;
            r.FindPropertyRelative("_neighbor").objectReferenceValue = neighbor;
            r.FindPropertyRelative("_neighborTag").objectReferenceValue = neighborTag;
            r.FindPropertyRelative("_effect").enumValueIndex = (int)effect;
            r.FindPropertyRelative("_valuePerNeighbor").floatValue = value;
            r.FindPropertyRelative("_maxStacks").intValue = max;
            r.FindPropertyRelative("_freeNeighbors").intValue = free;
            r.FindPropertyRelative("_excludeSameType").boolValue = excludeSame;
            so.ApplyModifiedPropertiesWithoutUndo();
            return holder.Rules[0];
        }

        private AdjacencyRuleSet Rules(params AdjacencyRule[] rules)
        {
            // 규칙 묶음 하나에 모음 (같은 규칙 인스턴스를 그대로 담을 수 없어 값으로 다시 채움)
            var set = Create<AdjacencyRuleSet>();
            var so = new SerializedObject(set);
            so.FindProperty("_maxMultiplier").floatValue = 2f;
            var list = so.FindProperty("_rules");
            list.arraySize = rules.Length;
            for (int i = 0; i < rules.Length; i++)
            {
                var r = list.GetArrayElementAtIndex(i);
                var src = rules[i];
                r.FindPropertyRelative("_target").objectReferenceValue = src.Target;
                r.FindPropertyRelative("_targetTag").objectReferenceValue = src.TargetTag;
                r.FindPropertyRelative("_neighbor").objectReferenceValue = src.Neighbor;
                r.FindPropertyRelative("_neighborTag").objectReferenceValue = src.NeighborTag;
                r.FindPropertyRelative("_effect").enumValueIndex = (int)src.Effect;
                r.FindPropertyRelative("_valuePerNeighbor").floatValue = src.ValuePerNeighbor;
                r.FindPropertyRelative("_maxStacks").intValue = src.MaxStacks;
                r.FindPropertyRelative("_freeNeighbors").intValue = src.FreeNeighbors;
                r.FindPropertyRelative("_excludeSameType").boolValue = src.ExcludeSameType;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return set;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }
    }
}
