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
    /// CONNECT M7 묶음 선택 · 옮기기 (사용자 결정 2026-11-27): 가지 선택(빼면 끊기는 쪽) · 모양 그대로 한 덩어리 · 대표 중심 회전 ·
    /// 비용 = 이동 비용의 합 · 모듈마다 이동 알림 하나 · 끊기면 경고만(놓기는 허용) · 막히는 경우 · 판정 뒤 격자 그대로.
    /// </summary>
    public class GroupMoveTests
    {
        private readonly List<Object> _created = new List<Object>();
        private BalanceConfig _config;
        private StationGradeConfig _grades;
        private ModuleData _core, _a, _b, _other;

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
            b.ApplyModifiedPropertiesWithoutUndo();

            _core = Module("MD_TestCore", 0f, new[] { Vector3Int.zero }, removable: false);
            _a = Module("MD_TestA", 10f, new[] { Vector3Int.zero });
            _b = Module("MD_TestB", 20f, new[] { Vector3Int.zero, Vector3Int.right });
            _other = Module("MD_TestOther", 5f, new[] { Vector3Int.zero });
            _grades = Grades();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Branch_CollectsModulesBeyond_LoopKeepsOne()
        {
            var sim = Sim();
            var a = Place(sim, _a, new Vector3Int(1, 0, 0));
            var o1 = Place(sim, _other, new Vector3Int(2, 0, 0));
            var o2 = Place(sim, _other, new Vector3Int(3, 0, 0));
            var side = Place(sim, _other, new Vector3Int(0, 0, 1)); // 코어 옆 (가지 아님)

            var branch = new List<ModuleInstance>();
            sim.CollectBranch(a, branch);
            CollectionAssert.AreEquivalent(new[] { a, o1, o2 }, branch, "A를 빼면 끊기는 쪽 = A 너머 전부");
            Assert.AreSame(a, branch[0], "고른 모듈이 맨 앞");
            sim.CollectBranch(o1, branch);
            CollectionAssert.AreEquivalent(new[] { o1, o2 }, branch);
            CollectionAssert.DoesNotContain(branch, side);

            // 고리: 코어 → (0,0,1) → (1,0,1) → (2,0,1) → O1 로 다른 길이 생기면 A를 빼도 안 끊김
            Place(sim, _other, new Vector3Int(1, 0, 1));
            Place(sim, _other, new Vector3Int(2, 0, 1));
            sim.CollectBranch(a, branch);
            CollectionAssert.AreEqual(new[] { a }, branch, "다른 길이 있으면 자기 하나뿐");
        }

        [Test]
        public void GroupMove_KeepsShape_ChargesSum_OneEventEach()
        {
            var sim = Sim();
            var a = Place(sim, _a, new Vector3Int(1, 0, 0));
            var o = Place(sim, _other, new Vector3Int(2, 0, 0)); // 코어 - A - O
            var group = new List<ModuleInstance> { a, o };
            int placed = 0, removed = 0, moved = 0;
            sim.Grid.ModulePlaced += _ => placed++;
            sim.Grid.ModuleRemoved += _ => removed++;
            sim.Grid.ModuleMoved += (_, __) => moved++;

            var origins = new List<Vector3Int>();
            var rotations = new List<int>();
            GroupMove.Resolve(group, a, new Vector3Int(0, 0, 1), 0, origins, rotations); // 코어 앞쪽으로
            CollectionAssert.AreEqual(new[] { new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1) }, origins, "모양 그대로");

            float metal = sim.Resources.GetStock(ResourceType.Metal);
            Assert.AreEqual(PlacementResult.Valid, sim.EvaluateGroupMove(group, origins, rotations, out int cut));
            Assert.AreEqual(0, cut);
            Assert.IsTrue(sim.TryMoveGroup(group, origins, rotations));
            Assert.AreEqual(metal - 3f, sim.Resources.GetStock(ResourceType.Metal), 1e-3f, "이동 비용 합 = 10 × 20% + 5 × 20%");
            Assert.AreEqual((0, 0, 2), (placed, removed, moved), "철거 · 배치 없이 모듈마다 이동 알림 하나");
            Assert.IsTrue(sim.Grid.TryGetModule(new Vector3Int(1, 0, 1), out var at) && at == o, "같은 인스턴스가 새 칸에");
            Assert.IsFalse(sim.Grid.IsOccupied(new Vector3Int(2, 0, 0)), "옛 칸은 비움");
            Assert.IsTrue(sim.Connectivity.IsActive(a) && sim.Connectivity.IsActive(o));
        }

        [Test]
        public void GroupMove_RotatesAroundPivot_OverlappingOwnCells()
        {
            var sim = Sim();
            var b = Place(sim, _b, new Vector3Int(1, 0, 0));     // (1,0,0)~(2,0,0)
            var o = Place(sim, _other, new Vector3Int(3, 0, 0)); // 끝
            var group = new List<ModuleInstance> { b, o };
            var origins = new List<Vector3Int>();
            var rotations = new List<int>();

            GroupMove.Resolve(group, b, b.Origin, 0, origins, rotations);
            Assert.AreEqual(PlacementResult.NoChange, sim.EvaluateGroupMove(group, origins, rotations, out _));

            // 대표(B) 원점을 축으로 90도: B = (1,0,0)·(1,0,-1), O = (1,0,-2) — B 자기 칸 (1,0,0)과 겹치는 자리
            GroupMove.Resolve(group, b, b.Origin, 1, origins, rotations);
            Assert.AreEqual(new Vector3Int(1, 0, -2), origins[1]);
            Assert.AreEqual(PlacementResult.Valid, sim.EvaluateGroupMove(group, origins, rotations, out _));
            Assert.IsTrue(sim.TryMoveGroup(group, origins, rotations));
            Assert.AreEqual(1, b.Rotation);
            Assert.IsTrue(sim.Grid.TryGetModule(new Vector3Int(1, 0, -1), out var m1) && m1 == b);
            Assert.IsTrue(sim.Grid.TryGetModule(new Vector3Int(1, 0, -2), out var m2) && m2 == o);
            Assert.IsFalse(sim.Grid.IsOccupied(new Vector3Int(2, 0, 0)));
            Assert.IsFalse(sim.Grid.IsOccupied(new Vector3Int(3, 0, 0)));
            Assert.IsTrue(sim.Connectivity.IsActive(o));
        }

        [Test]
        public void GroupMove_CutModules_WarnOnly()
        {
            var sim = Sim();
            var a = Place(sim, _a, new Vector3Int(1, 0, 0));
            var o1 = Place(sim, _other, new Vector3Int(2, 0, 0));
            var o2 = Place(sim, _other, new Vector3Int(3, 0, 0)); // 코어 - A - O1 - O2
            var group = new List<ModuleInstance> { a, o1 };       // O2는 빼고 옮김
            var origins = new List<Vector3Int>();
            var rotations = new List<int>();
            GroupMove.Resolve(group, a, new Vector3Int(0, 0, 1), 0, origins, rotations);

            Assert.AreEqual(PlacementResult.Valid, sim.EvaluateGroupMove(group, origins, rotations, out int cut), "끊겨도 놓기는 허용");
            Assert.AreEqual(1, cut, "O2가 끊김");
            Assert.IsTrue(sim.TryMoveGroup(group, origins, rotations));
            Assert.IsFalse(sim.Connectivity.IsActive(o2));
            Assert.IsTrue(sim.Connectivity.IsActive(o1));
        }

        [Test]
        public void GroupMove_BlockedReasons_AndPeekRestoresGrid()
        {
            var sim = Sim();
            var a = Place(sim, _a, new Vector3Int(1, 0, 0));
            var o = Place(sim, _other, new Vector3Int(2, 0, 0));
            var side = Place(sim, _other, new Vector3Int(0, 0, 1));
            var group = new List<ModuleInstance> { a, o };
            var origins = new List<Vector3Int>();
            var rotations = new List<int>();
            int version = sim.Grid.Version;

            // 멀리 떨어진 자리 = 묶음 밖과 면이 안 닿음
            GroupMove.Resolve(group, a, new Vector3Int(5, 0, 5), 0, origins, rotations);
            Assert.AreEqual(PlacementResult.NeedsContact, sim.EvaluateGroupMove(group, origins, rotations, out _));
            // 묶음 밖 모듈과 겹침 (A → 옆 모듈 자리)
            GroupMove.Resolve(group, a, side.Origin, 0, origins, rotations);
            Assert.AreEqual(PlacementResult.Occupied, sim.EvaluateGroupMove(group, origins, rotations, out _));

            Assert.AreEqual(version, sim.Grid.Version, "판정은 격자를 바꾸지 않음");
            Assert.AreEqual(new Vector3Int(1, 0, 0), a.Origin);
            Assert.IsTrue(sim.Grid.TryGetModule(new Vector3Int(2, 0, 0), out var at) && at == o, "잠깐 옮긴 뒤 되돌림");

            // 코어는 묶음에 못 넣음 · 파손 모듈이 있으면 막음
            Assert.AreEqual(MoveBlock.NotMovable, sim.CheckGroupMovable(new List<ModuleInstance> { a, sim.Core }, out var blocker));
            Assert.AreSame(sim.Core, blocker);
            Assert.IsTrue(sim.Damage.Damage(o));
            Assert.AreEqual(MoveBlock.Damaged, sim.CheckGroupMovable(group, out blocker));
            Assert.AreSame(o, blocker);
            GroupMove.Resolve(group, a, new Vector3Int(0, 0, -1), 0, origins, rotations);
            Assert.AreEqual(PlacementResult.InvalidDefinition, sim.EvaluateGroupMove(group, origins, rotations, out _));
            Assert.IsFalse(sim.TryMoveGroup(group, origins, rotations));
        }

        [Test]
        public void AnchorOnFace_PushesGroupOutOfClickedFace()
        {
            var sim = Sim();
            var a = Place(sim, _a, new Vector3Int(1, 0, 0));
            var o = Place(sim, _other, new Vector3Int(2, 0, 0));
            var side = Place(sim, _other, new Vector3Int(0, 0, 1));
            var group = new List<ModuleInstance> { o, a }; // 대표 = 바깥쪽 O, A는 대표의 -x 쪽

            // 옆 모듈의 +x 면을 가리킴 (면 바로 바깥 칸 (1,0,1)): 대표를 거기 두면 A가 옆 모듈 칸 (0,0,1)에 파고듦
            // → 묶음의 가장 안쪽 층(A)이 그 칸에 오게 대표 원점을 한 칸 바깥으로
            var anchor = GroupMove.AnchorOnFace(group, o, new Vector3Int(1, 0, 1), Vector3Int.right, 0);
            Assert.AreEqual(new Vector3Int(2, 0, 1), anchor);
            var origins = new List<Vector3Int>();
            var rotations = new List<int>();
            GroupMove.Resolve(group, o, anchor, 0, origins, rotations);
            Assert.AreEqual(new Vector3Int(1, 0, 1), origins[1], "A는 면 바로 바깥 칸");
            Assert.AreEqual(PlacementResult.Valid, sim.EvaluateGroupMove(group, origins, rotations, out _), "A가 옆 모듈과 닿음");

            // 바깥쪽으로만 뻗는 묶음이면 그대로 (한 개 배치의 AnchorOnFace와 같음)
            Assert.AreEqual(new Vector3Int(-1, 0, 0), GroupMove.AnchorOnFace(new List<ModuleInstance> { a, o }, a, new Vector3Int(-1, 0, 0), Vector3Int.left, 2));
            Assert.IsNotNull(side);
        }

        // ---------------- 도우미 ----------------

        private StationSimulation Sim() => new StationSimulation(new StationSimulationSettings
        {
            Balance = _config,
            Grades = _grades,
            CoreModule = _core,
            Events = new List<GameEventData>(),
            Random01 = () => 0.5f,
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
