using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Editor;
using SpaceStation.Simulation;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SpaceStation.Tests
{
    /// <summary>
    /// CONNECT_1121 3번 ④ V1 (2026-11-28): 효과 종류 14 (배율 · 더하기 · 뜻 있는 모듈만) · 모든 모듈 대상 · 설명 · 시스템 연결 (번짐 · 수리 시간 · 피격) ·
    /// 실제 관계 표 (태그 · 모듈이 다 있음, 기본 모듈이 거의 모두 관계에 참여).
    /// </summary>
    public class AdjacencyRelationTests
    {
        private const float Eps = 1e-4f;
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void NewEffects_MultiplierAndAddend_StackPerNeighbor()
        {
            var noise = Tag("소음원");
            var quiet = Tag("정숙");
            var medical = Module("MD_TMedical", m => { m.Set("_serviceNeed", (int)ResidentNeed.Medical); m.Set("_serviceCapacity", 12); }, quiet);
            var mine = Module("MD_TMine", null, noise);
            var set = Rules(AdjacencyRule.Create(AdjacencyEffect.ServiceCapacity, -0.2f, 2, "소란", targetTag: quiet, neighborTag: noise));
            var system = new AdjacencySystem(set);
            var grid = new StationGrid();
            var med = Place(grid, medical, 0);
            Place(grid, mine, 1);
            Place(grid, mine, -1);
            Place(grid, mine, 0, 1); // 셋째는 최대 2회라 안 셈
            system.Recalculate(grid);

            Assert.AreEqual(0.6f, system.Multiplier(med, AdjacencyEffect.ServiceCapacity), Eps, "−20% × 2회");
            Assert.AreEqual(0.6f, system.Value(med, AdjacencyEffect.ServiceCapacity), Eps, "배율형은 Value = 배율");
            Assert.AreEqual(1f, system.Multiplier(med, AdjacencyEffect.RepairTime), Eps, "다른 효과는 1");
            Assert.AreEqual(1f, system.GetProductionMultiplier(med), Eps);
            Assert.AreEqual(0f, system.Addend(med, AdjacencyEffect.Passengers), Eps);
        }

        [Test]
        public void Relevance_SameTagPair_SplitsByWhatTheModuleHas()
        {
            var noise = Tag("소음원");
            var quiet = Tag("정숙");
            var medical = Module("MD_TMedical", m => { m.Set("_serviceNeed", (int)ResidentNeed.Medical); m.Set("_serviceCapacity", 12); }, quiet);
            var lab = Module("MD_TLab", m => m.Set("_researchSlots", 1), quiet);
            var service = AdjacencyRule.Create(AdjacencyEffect.ServiceCapacity, -0.2f, 2, "소란", targetTag: quiet, neighborTag: noise);
            var research = AdjacencyRule.Create(AdjacencyEffect.ResearchSpeed, -0.05f, 2, "소음 방해", targetTag: quiet, neighborTag: noise);

            Assert.IsTrue(service.AppliesTo(medical), "의료실 = 서비스 정원");
            Assert.IsFalse(service.AppliesTo(lab), "연구소엔 서비스 정원이 없음");
            Assert.IsTrue(research.AppliesTo(lab), "연구소 = 연구 속도");
            Assert.IsFalse(research.AppliesTo(medical), "의료실엔 연구 속도가 없음");
        }

        [Test]
        public void AnyTarget_AppliesEverywhere_AndDescribesOnlyOnNeighborSide()
        {
            var maint = Tag("정비");
            var bay = Module("MD_TBay", null, maint);
            var farm = Module("MD_TFarm", null);
            var rule = AdjacencyRule.Create(AdjacencyEffect.RepairTime, -0.2f, 1, "정비 지원", neighborTag: maint, anyTarget: true);
            Assert.IsTrue(rule.AppliesTo(farm));
            Assert.IsTrue(rule.AppliesTo(bay));
            Assert.AreEqual("모든 모듈 ← 정비", rule.TagRelation);

            var system = new AdjacencySystem(Rules(rule));
            var lines = new List<string>();
            system.DescribeRulesFor(farm, lines);
            Assert.AreEqual(0, lines.Count, "모든 모듈 설명에 같은 줄을 붙이지 않음");
            system.DescribeRulesFor(bay, lines);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains("수리 시간 -20%", lines[0]);
            Assert.IsTrue(AdjacencySystem.IsBeneficial(rule, -0.2f), "수리 시간 −는 좋은 효과");
            Assert.AreEqual("-20%", AdjacencySystem.ShortValue(rule, -0.2f));
        }

        [Test]
        public void Additive_Passengers_ShowAsPeople()
        {
            var arrival = Tag("유입");
            var home = Tag("주거");
            var port = Module("MD_TPort", m => m.Set("_immigrationPort", true), arrival);
            var habitat = Module("MD_THab", null, home);
            var rule = AdjacencyRule.Create(AdjacencyEffect.Passengers, 1f, 1, "마중", targetTag: arrival, neighborTag: home);
            var system = new AdjacencySystem(Rules(rule));
            var grid = new StationGrid();
            var p = Place(grid, port, 0);
            Place(grid, habitat, 1);
            Place(grid, habitat, -1);
            system.Recalculate(grid);
            Assert.AreEqual(1f, system.Addend(p, AdjacencyEffect.Passengers), Eps, "최대 1회");
            Assert.AreEqual(1f, system.Value(p, AdjacencyEffect.Passengers), Eps, "더하는 효과는 Value = 합");
            Assert.AreEqual("+1", AdjacencySystem.ShortValue(rule, 1f));
            StringAssert.Contains("이민선 승객 +1", AdjacencySystem.Describe(rule, port, 1));
        }

        [Test]
        public void DamageSystem_UsesSpreadRepairAndHitWeight()
        {
            var config = Create<BalanceConfig>();
            var so = new SerializedObject(config);
            so.FindProperty("_spreadAfterSeconds").floatValue = 100f;
            so.FindProperty("_repairDuration").floatValue = 10f;
            so.FindProperty("_meteorExposureSlope").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var block = Module("MD_TBlock", null);
            var grid = new StationGrid();
            var m = Place(grid, block, 0);
            var damage = new DamageSystem(config)
            {
                AdjacencyLookup = (x, e) => e == AdjacencyEffect.SpreadTime ? 0.7f : e == AdjacencyEffect.RepairTime ? 0.8f : e == AdjacencyEffect.HitWeight ? 0.5f : 1f,
            };
            float plain = new DamageSystem(config).ModuleMeteorWeight(grid, m);
            Assert.AreEqual(plain * 0.5f, damage.ModuleMeteorWeight(grid, m), Eps, "피격 가중 × 0.5");
            Assert.IsTrue(damage.Damage(m));
            Assert.IsTrue(damage.TryGetInfo(m, out var info));
            Assert.AreEqual(70f, info.TimeUntilSpread, Eps, "번지기까지 100 × 0.7");
            Assert.IsTrue(damage.StartRepair(m));
            Assert.AreEqual(8f, info.RepairRemaining, Eps, "수리 10 × 0.8");
        }

        [Test]
        public void RealRelations_ResolveAndCoverBasicModules()
        {
            var rules = AdjacencyRelationSetup.Build(
                k => AssetDatabase.LoadAssetAtPath<AdjacencyTag>($"Assets/_Project/Data/Adjacency/Tags/AT_{k}.asset"),
                n => AssetDatabase.LoadAssetAtPath<ModuleData>($"Assets/_Project/Data/Modules/{n}.asset"),
                out var missing);
            Assert.AreEqual(0, missing.Count, "없는 태그 · 모듈: " + string.Join(", ", missing));

            // A3: 기본 36종 모두가 하나 이상의 관계에 — 규칙이 아닌 틀로 맡는 넷만 뺌 (완충 · 중계 = 인접 계산 자체, 냉각 = A10 열, 차폐 = 엔드리스 준비 중)
            var exempt = new HashSet<string> { "MD_StructureFrame", "MD_ConnectorHub", "MD_Radiator", "MD_RadiationShelter" };
            var lonely = new List<string>();
            int basics = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data/Modules" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data == null || data.name.StartsWith("MD_Evo_") || data.AdjacencyTags.Count == 0)
                    continue;
                basics++;
                if (exempt.Contains(data.name))
                    continue;
                bool any = false;
                foreach (var r in rules)
                    any |= (r.AppliesTo(data) && !r.AnyTarget) || (!r.AnyNeighbor && r.CouldNeighbor(data));
                if (!any)
                    lonely.Add(data.name);
            }
            Assert.GreaterOrEqual(basics, 36, "태그 단 기본 모듈");
            Assert.AreEqual(0, lonely.Count, "관계가 없는 기본 모듈: " + string.Join(", ", lonely));

            // 효과가 그 대상 모듈들에 뜻이 있는지 (태그로 넓혔더니 아무 데도 안 걸리는 관계가 없게)
            foreach (var r in rules)
            {
                bool hit = r.AnyTarget;
                foreach (var guid in AssetDatabase.FindAssets("t:ModuleData", new[] { "Assets/_Project/Data/Modules" }))
                    hit |= r.AppliesTo(AssetDatabase.LoadAssetAtPath<ModuleData>(AssetDatabase.GUIDToAssetPath(guid)));
                Assert.IsTrue(hit, $"관계 '{r.Label}' ({r.TagRelation})가 걸리는 모듈이 없음");
            }
        }

        [Test]
        public void InnerFor_FindsFacesAndBadNeighbors_InnerOnlyHasNoOuterEffect()
        {
            // CONNECT A5 안쪽 얼굴 (3번 ④ V2): 좋은 관계 = 안쪽 얼굴, 벌칙 관계 = 나쁜 이웃 (번짐), 그늘처럼 이웃을 가리지 않는 관계는 뺌
            var o2 = Tag("산소원");
            var heat = Tag("발열원");
            var core = Tag("중심");
            var sun = Tag("집광");
            var oxygen = Module("MD_TOxy", null, o2, sun);
            var furnace = Module("MD_THeat", null, heat);
            var hub = Module("MD_TCore", null, core);
            var set = Rules(
                AdjacencyRule.Create(AdjacencyEffect.SpreadTime, -0.3f, 2, "인화 위험", targetTag: o2, neighborTag: heat, inner: InnerFace.FireTwoFlames),
                AdjacencyRule.Create(AdjacencyEffect.InnerOnly, 0f, 1, "코어 처리", neighborTag: core, anyTarget: true, inner: InnerFace.MissNoLoss),
                AdjacencyRule.Create(AdjacencyEffect.Production, -0.1f, 3, "그늘", targetTag: sun, excludeSameType: true));
            var system = new AdjacencySystem(set);
            var grid = new StationGrid();
            var room = Place(grid, oxygen, 0);
            var heatRoom = Place(grid, furnace, 1);
            var coreRoom = Place(grid, hub, -1);
            system.Recalculate(grid);

            var hits = new List<InnerHit>();
            system.InnerFor(grid, room, hits);
            Assert.AreEqual(2, hits.Count, "인화 위험 + 코어 처리 (그늘은 뺌)");
            var bad = hits.Find(h => h.Bad);
            Assert.AreSame(heatRoom, bad.Neighbor, "나쁜 이웃 = 발열원 방");
            Assert.AreEqual(InnerFace.FireTwoFlames, bad.Face);
            var good = hits.Find(h => !h.Bad);
            Assert.AreSame(coreRoom, good.Neighbor);
            Assert.AreEqual(InnerFace.MissNoLoss, good.Face);

            var applied = new List<AppliedAdjacency>();
            system.Evaluate(furnace, new[] { hub }, applied);
            Assert.AreEqual(0, applied.Count, "안쪽만 관계(모든 모듈 ← 중심)는 바깥 효과 없음");
            Assert.AreEqual(1f, system.GetProductionMultiplier(coreRoom), Eps);

            var lines = new List<string>();
            system.DescribeRulesFor(hub, lines);
            Assert.AreEqual(1, lines.Count);
            StringAssert.Contains("안: 놓친 사고를 대신 처리", lines[0]);
            Assert.AreSame(set.Rules[1], system.FindInnerRule(furnace, InnerFace.MissNoLoss), "아무 방이나 받을 수 있는 안쪽 얼굴");
            Assert.IsNull(system.FindInnerRule(furnace, InnerFace.FireTwoFlames), "발열원 방은 인화 위험의 대상이 아님");
        }

        [Test]
        public void MissIncident_NeighborMods_ChangeLoss()
        {
            var config = Create<BalanceConfig>();
            var grades = Create<StationGradeConfig>();
            var g = new SerializedObject(grades);
            g.FindProperty("_grades").arraySize = 1;
            g.ApplyModifiedPropertiesWithoutUndo();
            var sim = new StationSimulation(new StationSimulationSettings
            {
                Balance = config,
                Grades = grades,
                CoreModule = Module("MD_TCoreSim", null),
                Events = new List<GameEventData>(),
                Random01 = () => 0.5f,
                HazardRandom01 = () => 0.99f,
            });
            sim.Population.SetSatisfaction(50f);
            Assert.AreEqual("손실 없음", sim.MissIncident(IncidentKind.Filter, sim.Core, ResourceType.Oxygen, IncidentMiss.NoLoss), "코어가 처리");
            Assert.AreEqual(50f, sim.Population.Satisfaction, Eps);
            Assert.AreEqual(0, sim.Incidents.ActiveCount);

            StringAssert.Contains("회수", sim.MissIncident(IncidentKind.Leak, sim.Core, ResourceType.Water, IncidentMiss.Recovered));
            Assert.AreEqual(0, sim.Incidents.ActiveCount, "샌 물을 회수 = 새는 것 없음");
            StringAssert.Contains("산소", sim.MissIncident(IncidentKind.Leak, sim.Core, ResourceType.Oxygen, IncidentMiss.Recovered), "산소 누출은 회수 안 됨");

            StringAssert.Contains("만족도 지킴", sim.MissIncident(IncidentKind.DecoLights, sim.Core, ResourceType.Oxygen, IncidentMiss.NoMood));
            Assert.AreEqual(50f, sim.Population.Satisfaction, Eps, "의료 이웃 = 만족도 지킴");
            sim.MissIncident(IncidentKind.DecoLights, sim.Core, ResourceType.Oxygen, IncidentMiss.Half);
            Assert.AreEqual(50f - config.IncidentMissSatisfaction * 0.5f, sim.Population.Satisfaction, Eps, "손상 통제실 ← 정비 = 절반");
        }

        // ---------------- 도우미 ----------------

        private sealed class Props
        {
            private readonly SerializedObject _so;
            public Props(SerializedObject so) => _so = so;
            public void Set(string name, int v) => _so.FindProperty(name).intValue = v;
            public void Set(string name, float v) => _so.FindProperty(name).floatValue = v;
            public void Set(string name, bool v) => _so.FindProperty(name).boolValue = v;
        }

        private T Create<T>() where T : ScriptableObject
        {
            var x = ScriptableObject.CreateInstance<T>();
            _created.Add(x);
            return x;
        }

        private AdjacencyTag Tag(string name)
        {
            var t = Create<AdjacencyTag>();
            t.EditorSet(name, "");
            return t;
        }

        private ModuleData Module(string name, System.Action<Props> fill, params AdjacencyTag[] tags)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            fill?.Invoke(new Props(so));
            var list = so.FindProperty("_adjacencyTags");
            list.arraySize = tags.Length;
            for (int i = 0; i < tags.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = tags[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return m;
        }

        private AdjacencyRuleSet Rules(params AdjacencyRule[] rules)
        {
            var set = Create<AdjacencyRuleSet>();
            set.EditorSetRules(rules);
            return set;
        }

        private static ModuleInstance Place(StationGrid grid, ModuleData data, int x, int y = 0)
        {
            Assert.IsTrue(grid.TryPlace(data, new Vector3Int(x, y, 0), 0, out var m), $"{data.name} ({x}, {y})");
            return m;
        }
    }
}
