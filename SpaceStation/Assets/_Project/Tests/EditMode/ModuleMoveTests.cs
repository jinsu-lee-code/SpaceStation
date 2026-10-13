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
    /// 12-3b 모듈 이동: 같은 인스턴스(내구도 그대로) · 비용 건설비 20% · 자기 칸과 겹치는 자리 · 회전만,
    /// 옮길 수 없는 경우(코어 · 파손 · 합성 중), 자리 판정(그대로 · 점유 · 면 접촉 없음 · 자원 부족), 연결 재계산, 알림.
    /// </summary>
    public class ModuleMoveTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _a, _b, _other, _result;
        private RecipeData _recipe;

        [SetUp]
        public void SetUp()
        {
            _config = Create<BalanceConfig>();
            var b = new SerializedObject(_config);
            b.FindProperty("_startingPopulation").intValue = 0;
            Fill(b.FindProperty("_startingResources"), R(ResourceType.Oxygen, 150), R(ResourceType.Metal, 200));
            Fill(b.FindProperty("_baseStorageCapacity"), R(ResourceType.Oxygen, 1000), R(ResourceType.Metal, 1000));
            b.FindProperty("_eventGracePeriod").floatValue = 100000f;
            b.FindProperty("_eventIntervalMin").floatValue = 100000f;
            b.FindProperty("_eventIntervalMax").floatValue = 100000f;
            b.FindProperty("_destroyAfterSeconds").floatValue = 1000f;
            b.FindProperty("_durabilityDecayPerSecond").floatValue = 1f;
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 0f, new[] { Vector3Int.zero }, removable: false);
            _a = Module("MD_TestA", 10f, new[] { Vector3Int.zero });
            _b = Module("MD_TestB", 20f, new[] { Vector3Int.zero, Vector3Int.right });
            _other = Module("MD_TestOther", 5f, new[] { Vector3Int.zero });
            _result = Module("MD_TestEvolved", 60f, new[] { Vector3Int.zero, Vector3Int.right, new Vector3Int(2, 0, 0) });
            _grades = Grades();
            _recipe = Recipe();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Move_KeepsSameInstanceAndDurability_ChargesTwentyPercent()
        {
            var sim = Sim();
            var a = Place(sim, _a, new Vector3Int(1, 0, 0));
            sim.Tick(5f);
            Assert.IsTrue(sim.Durability.TryGetInfo(a, out var before));
            float durability = before.Current;
            Assert.Less(durability, 100f, "노후가 진행된 상태에서 옮김");

            int placed = 0, removed = 0, moved = 0;
            sim.Grid.ModulePlaced += _ => placed++;
            sim.Grid.ModuleRemoved += _ => removed++;
            sim.Grid.ModuleMoved += (_, __) => moved++;

            float metal = sim.Resources.GetStock(ResourceType.Metal);
            var target = new Vector3Int(0, 0, 1);
            Assert.AreEqual(PlacementResult.Valid, sim.EvaluateMove(a, target, 0));
            Assert.IsTrue(sim.TryMove(a, target, 0));

            Assert.AreEqual(metal - 2f, sim.Resources.GetStock(ResourceType.Metal), 1e-3f, "건설비 10 × 20%");
            Assert.AreEqual(target, a.Origin);
            Assert.IsTrue(sim.Grid.TryGetModule(target, out var at) && at == a, "같은 인스턴스가 새 칸에");
            Assert.IsFalse(sim.Grid.IsOccupied(new Vector3Int(1, 0, 0)), "옛 칸은 비움");
            Assert.IsTrue(sim.Durability.TryGetInfo(a, out var after));
            Assert.AreEqual(durability, after.Current, 1e-4f, "내구도 그대로");
            Assert.AreEqual((0, 0, 1), (placed, removed, moved), "철거 · 배치 알림 없이 이동 알림 하나");
            Assert.IsTrue(sim.Connectivity.IsActive(a));
        }

        [Test]
        public void Move_OverlappingOwnCells_AndRotateInPlace()
        {
            var sim = Sim();
            var b = Place(sim, _b, new Vector3Int(1, 0, 0)); // (1,0,0)~(2,0,0)
            Place(sim, _other, new Vector3Int(3, 0, 0));     // 오른쪽 끝에 닿는 모듈

            // 한 칸 오른쪽으로 (자기 칸 (2,0,0)과 겹침) → (2,0,0)~(3,0,0)은 Other와 겹침
            Assert.AreEqual(PlacementResult.Occupied, sim.EvaluateMove(b, new Vector3Int(2, 0, 0), 0));

            // 제자리 회전: (1,0,0)을 축으로 z 방향 — 코어와 계속 닿음
            int rot = -1;
            for (int r = 1; r < 4 && rot < 0; r++)
            {
                if (sim.EvaluateMove(b, new Vector3Int(1, 0, 0), r) == PlacementResult.Valid)
                    rot = r;
            }
            Assert.GreaterOrEqual(rot, 1, "어느 회전이든 제자리 회전 가능");
            Assert.IsTrue(sim.TryMove(b, new Vector3Int(1, 0, 0), rot));
            Assert.AreEqual(rot, b.Rotation);
            Assert.IsFalse(sim.Grid.TryGetModule(new Vector3Int(2, 0, 0), out _), "옛 둘째 칸은 비움");
            foreach (var c in b.Cells)
                Assert.IsTrue(sim.Grid.TryGetModule(c, out var m) && m == b);
            Assert.AreEqual(PlacementResult.NoChange, sim.EvaluateMove(b, new Vector3Int(1, 0, 0), rot));
        }

        [Test]
        public void Move_BlockedReasons_AndPlaceRules()
        {
            var sim = Sim();
            Assert.AreEqual(MoveBlock.NotMovable, sim.CheckMovable(sim.Core));

            var a = Place(sim, _a, new Vector3Int(1, 0, 0));
            Assert.AreEqual(MoveBlock.None, sim.CheckMovable(a));
            // 옛 자리 바로 옆: 옮기면 자기 옛 칸만 닿음 → 면 접촉 없음
            Assert.AreEqual(PlacementResult.NeedsContact, sim.EvaluateMove(a, new Vector3Int(2, 0, 0), 0));
            Assert.AreEqual(PlacementResult.Occupied, sim.EvaluateMove(a, Vector3Int.zero, 0));

            Assert.IsTrue(sim.Damage.Damage(a));
            Assert.AreEqual(MoveBlock.Damaged, sim.CheckMovable(a));
            Assert.IsFalse(sim.TryMove(a, new Vector3Int(0, 0, 1), 0), "파손 중엔 이동 불가");
            sim.Damage.CompleteNow(a);
            Assert.AreEqual(MoveBlock.None, sim.CheckMovable(a));

            // 자원 부족
            sim.Resources.TrySpend(new List<ResourceAmount> { R(ResourceType.Metal, sim.Resources.GetStock(ResourceType.Metal)) });
            Assert.AreEqual(PlacementResult.InsufficientResources, sim.EvaluateMove(a, new Vector3Int(0, 0, 1), 0));
        }

        [Test]
        public void Move_SynthesizingMaterial_IsBlocked()
        {
            var sim = Sim();
            var origin = new Vector3Int(1, 0, 0);
            var b = Place(sim, _b, origin);
            Place(sim, _a, new Vector3Int(3, 0, 0));
            Assert.AreEqual(SynthesisResult.Valid, sim.Synthesis.TryStart(_recipe, origin, 0, out _));
            Assert.AreEqual(MoveBlock.Synthesizing, sim.CheckMovable(b));
            Assert.AreEqual(PlacementResult.InvalidDefinition, sim.EvaluateMove(b, new Vector3Int(0, 0, 1), 0));
        }

        [Test]
        public void Move_RecalculatesConnectivity()
        {
            var sim = Sim();
            var a = Place(sim, _a, new Vector3Int(1, 0, 0));
            var other = Place(sim, _other, new Vector3Int(2, 0, 0)); // 코어 - A - Other
            Assert.IsTrue(sim.Connectivity.IsActive(other));
            Assert.IsTrue(sim.TryMove(a, new Vector3Int(0, 0, 1), 0));
            Assert.IsFalse(sim.Connectivity.IsActive(other), "다리 역할 모듈을 옮기면 끊김 (철거와 같음)");
            Assert.IsTrue(sim.Connectivity.IsActive(a));
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim() => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
            Recipes = new[] { _recipe },
        });

        private ModuleInstance Place(StationSimulation sim, ModuleData data, Vector3Int origin)
        {
            Assert.IsTrue(sim.TryPlace(data, origin, 0, out var m), $"{data.name} @ {origin}");
            return m;
        }

        private StationGradeConfig Grades()
        {
            var grades = Create<StationGradeConfig>();
            var g = new SerializedObject(grades);
            var list = g.FindProperty("_grades");
            list.arraySize = 1;
            var grade = list.GetArrayElementAtIndex(0);
            grade.FindPropertyRelative("_minPopulation").intValue = 0;
            grade.FindPropertyRelative("_minModules").intValue = 0;
            var unlocks = grade.FindPropertyRelative("_unlocks");
            unlocks.arraySize = 3;
            unlocks.GetArrayElementAtIndex(0).objectReferenceValue = _a;
            unlocks.GetArrayElementAtIndex(1).objectReferenceValue = _b;
            unlocks.GetArrayElementAtIndex(2).objectReferenceValue = _other;
            g.ApplyModifiedPropertiesWithoutUndo();
            return grades;
        }

        private RecipeData Recipe()
        {
            var r = Create<RecipeData>();
            r.name = "RC_TestMove";
            var so = new SerializedObject(r);
            so.FindProperty("_displayName").stringValue = "시험 합성";
            so.FindProperty("_result").objectReferenceValue = _result;
            var ing = so.FindProperty("_ingredients");
            ing.arraySize = 2;
            ing.GetArrayElementAtIndex(0).FindPropertyRelative("Module").objectReferenceValue = _a;
            ing.GetArrayElementAtIndex(0).FindPropertyRelative("Count").intValue = 1;
            ing.GetArrayElementAtIndex(1).FindPropertyRelative("Module").objectReferenceValue = _b;
            ing.GetArrayElementAtIndex(1).FindPropertyRelative("Count").intValue = 1;
            Fill(so.FindProperty("_cost"), R(ResourceType.Metal, 30f));
            so.FindProperty("_duration").floatValue = 10f;
            so.FindProperty("_unlockGrade").intValue = 0;
            so.FindProperty("_unlockedByDefault").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            return r;
        }

        private ModuleData Module(string name, float metal, Vector3Int[] cells, bool removable = true)
        {
            var m = Create<ModuleData>();
            m.name = name;
            var so = new SerializedObject(m);
            so.FindProperty("_removable").boolValue = removable;
            var offsets = so.FindProperty("_cellOffsets");
            offsets.arraySize = cells.Length;
            for (int i = 0; i < cells.Length; i++)
                offsets.GetArrayElementAtIndex(i).vector3IntValue = cells[i];
            Fill(so.FindProperty("_buildCost"), R(ResourceType.Metal, metal));
            so.FindProperty("_gradeModuleCount").intValue = cells.Length == 3 ? 2 : 1;
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
