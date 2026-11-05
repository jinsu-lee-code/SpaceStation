using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    public class PlacementRulesTests
    {
        private readonly List<Object> _created = new List<Object>();
        private StationGrid _grid;
        private ModuleData _block, _bar, _dock;

        [SetUp]
        public void SetUp()
        {
            _grid = new StationGrid();
            _block = Module("Block", new[] { Vector3Int.zero }, terminal: false);
            _bar = Module("Bar", new[] { Vector3Int.zero, Vector3Int.right }, terminal: false);
            _dock = Module("Dock", new[] { Vector3Int.zero }, terminal: true);
            _grid.TryPlace(_block, Vector3Int.zero, 0, out _); // 코어 역할
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void NormalModule_OnFreeCell_IsValid()
        {
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, Vector3Int.right, 0));
        }

        [Test]
        public void Occupied_IsRejected()
        {
            Assert.AreEqual(PlacementResult.Occupied, PlacementRules.Evaluate(_grid, _block, Vector3Int.zero, 0));
        }

        // ---- 8-4 면에 붙이는 원점 (가운데 원점 모듈) ----

        private ModuleData Ring()
        {
            var cells = new List<Vector3Int>();
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                    cells.Add(new Vector3Int(x, 0, z));
            return Module("Ring", cells.ToArray(), terminal: false);
        }

        [Test]
        public void AnchorOnFace_CenteredRing_SideFace_ShiftsOutward_AndIsValid()
        {
            var ring = Ring();
            // 블록(0,0,0)의 +x 면을 가리킴 → 맞닿은 칸 (1,0,0)
            var origin = PlacementRules.AnchorOnFace(ring, Vector3Int.right, Vector3Int.right, 0);
            Assert.AreEqual(new Vector3Int(2, 0, 0), origin, "가운데 원점이 한 칸 더 바깥 → 안쪽 열이 면에 붙음");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, ring, origin, 0));
            Assert.AreEqual(new Vector3Int(0, 0, -2), PlacementRules.AnchorOnFace(ring, Vector3Int.back, Vector3Int.back, 0));
            Assert.AreEqual(Vector3Int.up, PlacementRules.AnchorOnFace(ring, Vector3Int.up, Vector3Int.up, 0), "위 면은 평평해서 그대로");
        }

        [Test]
        public void AnchorOnFace_OtherModules_OnlyShiftWhenTheyWouldReachBack()
        {
            Assert.AreEqual(Vector3Int.left, PlacementRules.AnchorOnFace(_block, Vector3Int.left, Vector3Int.left, 0), "1칸은 그대로");
            Assert.AreEqual(Vector3Int.right, PlacementRules.AnchorOnFace(_bar, Vector3Int.right, Vector3Int.right, 0), "바깥으로 뻗는 2칸은 그대로");
            // 왼쪽 면에 2칸(원점 + 오른쪽)을 대면 오른쪽 칸이 블록과 겹침 → 한 칸 더 바깥
            var origin = PlacementRules.AnchorOnFace(_bar, Vector3Int.left, Vector3Int.left, 0);
            Assert.AreEqual(new Vector3Int(-2, 0, 0), origin);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _bar, origin, 0));
        }

        // ---- 도킹 규칙 (8-0: 뒷면 연결 + 앞쪽 접근로 2칸) ----

        private static readonly Vector3Int Fwd = new Vector3Int(0, 0, 1); // 회전 0 입구 방향 (ModuleData 기본값)

        private int RotationFacing(Vector3Int front)
        {
            for (int r = 0; r < 4; r++)
                if (PlacementRules.DockFrontWorld(_dock, r) == front)
                    return r;
            Assert.Fail($"입구를 {front}로 돌릴 수 없음");
            return 0;
        }

        [Test]
        public void Dock_BackAttached_LaneFree_IsValid()
        {
            Assert.AreEqual(Fwd, PlacementRules.DockFrontWorld(_dock, 0));
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _dock, Fwd, 0), "뒷면 = 코어(0,0,0)");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _dock, Vector3Int.right, RotationFacing(Vector3Int.right)), "+X로 돌리면 뒷면이 코어");
        }

        [Test]
        public void Dock_WithoutBackContact_IsRejected()
        {
            // (1,0,0)에 회전 0: 뒷면 칸 (1,0,-1)이 비어 있음 (코어와는 옆면으로만 닿음)
            Assert.AreEqual(PlacementResult.DockNeedsBackContact, PlacementRules.Evaluate(_grid, _dock, Vector3Int.right, 0));
            Assert.AreEqual(PlacementResult.DockNeedsBackContact, PlacementRules.Evaluate(_grid, _dock, new Vector3Int(5, 5, 5), 0));
        }

        [Test]
        public void Dock_LaneAlreadyBlocked_IsRejected()
        {
            _grid.TryPlace(_block, new Vector3Int(0, 0, 3), 0, out _); // 접근로 두 번째 칸
            Assert.AreEqual(PlacementResult.DockLaneBlocked, PlacementRules.Evaluate(_grid, _dock, Fwd, 0));
        }

        [Test]
        public void AfterDock_OnlyLaneIsBlocked_SidesAreFree()
        {
            _grid.TryPlace(_dock, Fwd, 0, out _); // (0,0,1), 접근로 (0,0,2)·(0,0,3)
            Assert.AreEqual(PlacementResult.BlockedByDockLane, PlacementRules.Evaluate(_grid, _block, new Vector3Int(0, 0, 2), 0));
            Assert.AreEqual(PlacementResult.BlockedByDockLane, PlacementRules.Evaluate(_grid, _block, new Vector3Int(0, 0, 3), 0));
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(0, 0, 4), 0), "접근로 너머");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(1, 0, 1), 0), "도킹 옆");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(0, 1, 1), 0), "도킹 위");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(0, -1, 1), 0), "도킹 아래");
        }

        [Test]
        public void Docks_CanStandSideBySide()
        {
            _grid.TryPlace(_dock, Fwd, 0, out _);
            _grid.TryPlace(_block, Vector3Int.right, 0, out _);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _dock, new Vector3Int(1, 0, 1), 0), "옆 도킹 (뒷면 = (1,0,0))");
        }

        [Test]
        public void TwoCellTerminal_OneBackContactEnough_LaneForEveryFrontCell()
        {
            // 8-5 화물 터미널: (0,0,1)-(1,0,1), 입구 +Z. 뒷면 칸 (0,0,0)=코어, (1,0,0)=비어 있음 → 하나면 충분
            var terminal = Module("Terminal", new[] { Vector3Int.zero, Vector3Int.right }, terminal: true);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, terminal, Fwd, 0));
            // 두 번째 칸 앞 접근로가 막혀 있으면 불가
            _grid.TryPlace(_block, new Vector3Int(1, 0, 3), 0, out var blocker);
            Assert.AreEqual(PlacementResult.DockLaneBlocked, PlacementRules.Evaluate(_grid, terminal, Fwd, 0));
            _grid.Remove(blocker);

            _grid.TryPlace(terminal, Fwd, 0, out _);
            Assert.AreEqual(PlacementResult.BlockedByDockLane, PlacementRules.Evaluate(_grid, _block, new Vector3Int(1, 0, 2), 0), "두 번째 칸 접근로");
            Assert.AreEqual(PlacementResult.BlockedByDockLane, PlacementRules.Evaluate(_grid, _block, new Vector3Int(0, 0, 3), 0));
            var lane = new List<Vector3Int>();
            PlacementRules.GetDockLane(terminal, Fwd, 0, lane);
            Assert.AreEqual(4, lane.Count, "앞면 칸 2개 × 2칸");
        }

        [Test]
        public void MultiCellModule_OverlappingLane_IsBlocked()
        {
            _grid.TryPlace(_dock, Fwd, 0, out _);
            // bar (-1,0,2)-(0,0,2): (0,0,2)가 접근로
            Assert.AreEqual(PlacementResult.BlockedByDockLane, PlacementRules.Evaluate(_grid, _bar, new Vector3Int(-1, 0, 2), 0));
        }

        [Test]
        public void AfterDockRemoved_LaneIsFreeAgain()
        {
            _grid.TryPlace(_dock, Fwd, 0, out var dock);
            _grid.Remove(dock);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(0, 0, 2), 0));
        }

        [Test]
        public void ModuleBetween_StopsLaneScan()
        {
            // 접근로가 이미 막힌 예전 도킹: 막은 모듈 너머 칸은 접근로로 보지 않음
            _grid.TryPlace(_dock, Fwd, 0, out _);
            _grid.TryPlace(_block, new Vector3Int(0, 0, 2), 0, out _); // 그리드 직접 배치 (예전 세이브처럼)
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(0, 0, 3), 0));
        }

        // ---- 비워 둘 칸 (대형 토러스 고리 안쪽, 사용자 2026-11-06 "채굴 도킹이나 여객포트처럼 해당 칸에 배치를 못하게") ----

        /// <summary>3×3 고리 (가운데 빔) + 비워 둘 칸 = 가운데. 원점 = 고리 왼쪽 앞 모서리.</summary>
        private ModuleData Donut(params Vector3Int[] keepClear)
        {
            var cells = new List<Vector3Int>();
            for (int x = 0; x < 3; x++)
                for (int z = 0; z < 3; z++)
                    if (x != 1 || z != 1)
                        cells.Add(new Vector3Int(x, 0, z));
            var donut = Module("Donut", cells.ToArray(), terminal: false);
            var so = new SerializedObject(donut);
            var list = so.FindProperty("_keepClearCells");
            list.arraySize = keepClear.Length;
            for (int i = 0; i < keepClear.Length; i++)
                list.GetArrayElementAtIndex(i).vector3IntValue = keepClear[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return donut;
        }

        [Test]
        public void KeepClear_AfterPlaced_NothingCanBeBuiltThere()
        {
            var donut = Donut(new Vector3Int(1, 0, 1));
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, donut, Vector3Int.right, 0));
            _grid.TryPlace(donut, Vector3Int.right, 0, out _); // (1..3, 0..2), 가운데 (2,0,1)
            Assert.AreEqual(PlacementResult.BlockedByKeepClear, PlacementRules.Evaluate(_grid, _block, new Vector3Int(2, 0, 1), 0));
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(2, 1, 1), 0), "고리 위는 자유 (비워 둘 칸만)");
        }

        [Test]
        public void KeepClear_MustBeEmpty_WhenPlacing()
        {
            var donut = Donut(new Vector3Int(1, 0, 1));
            _grid.TryPlace(_block, new Vector3Int(2, 0, 1), 0, out _); // 고리 가운데에 미리 지음 (예전 세이브처럼)
            Assert.AreEqual(PlacementResult.KeepClearBlocked, PlacementRules.Evaluate(_grid, donut, Vector3Int.right, 0));
        }

        [Test]
        public void KeepClear_FollowsRotation()
        {
            // 비워 둘 칸이 고리 밖 (원점 앞 −Z 한 칸) → 회전하면 다른 칸
            var donut = Donut(new Vector3Int(0, 0, -1));
            var origin = new Vector3Int(5, 0, 5);
            var cells = new List<Vector3Int>();
            for (int r = 0; r < 4; r++)
            {
                PlacementRules.GetKeepClearCells(donut, origin, r, cells);
                Assert.AreEqual(origin + GridDirections.Rotate(new Vector3Int(0, 0, -1), r), cells[0]);
            }
            _grid.TryPlace(donut, origin, 1, out _);
            PlacementRules.GetKeepClearCells(donut, origin, 1, cells);
            Assert.AreEqual(PlacementResult.BlockedByKeepClear, PlacementRules.Evaluate(_grid, _block, cells[0], 0));
            Assert.AreEqual(new Vector3Int(4, 0, 5), cells[0]);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(5, 0, 6), 0), "다른 회전(2)의 자리는 아님");
        }

        [Test]
        public void KeepClear_IgnoredModule_DoesNotBlock_AndRemovedFreesIt()
        {
            var donut = Donut(new Vector3Int(1, 0, 1));
            _grid.TryPlace(donut, Vector3Int.right, 0, out var placed);
            // 옮기는 고리 자신: 옛 자리 비워 둘 칸은 없다고 봄
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(2, 0, 1), 0, placed));
            Assert.IsTrue(PlacementRules.HitsKeepClear(_grid, new[] { new Vector3Int(2, 0, 1) }));
            _grid.Remove(placed);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(2, 0, 1), 0));
        }

        // ---- 받침 규칙 (7-7: 코어 2층 옆 칸만 받침 필요) ----

        private StationGrid BigCoreGrid()
        {
            // 2x2x2 코어: 2층(y=1)은 연결점 없는 탑
            var core = Module("BigCore", new[]
            {
                new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1),
                new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(0, 1, 1), new Vector3Int(1, 1, 1),
            }, terminal: false);
            var so = new SerializedObject(core);
            so.FindProperty("_upperSidesNeedSupport").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            var grid = new StationGrid();
            grid.TryPlace(core, Vector3Int.zero, 0, out _);
            return grid;
        }

        [Test]
        public void CoreUpperSide_NeedsModuleBelow()
        {
            var grid = BigCoreGrid();
            // 코어 2층 옆 8칸 (x=-1·2 / z=-1·2, y=1)
            foreach (var cell in new[] { new Vector3Int(2, 1, 0), new Vector3Int(2, 1, 1), new Vector3Int(-1, 1, 0), new Vector3Int(0, 1, -1), new Vector3Int(1, 1, 2) })
                Assert.AreEqual(PlacementResult.NeedsSupport, PlacementRules.Evaluate(grid, _block, cell, 0), $"{cell}");
            grid.TryPlace(_block, new Vector3Int(2, 0, 0), 0, out _);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(2, 1, 0), 0), "1층을 먼저 지으면 가능");
        }

        [Test]
        public void EverywhereElse_IsFree()
        {
            var grid = BigCoreGrid();
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(2, 0, 0), 0), "1층 옆");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(0, -1, 0), 0), "코어 아래");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(2, -1, 0), 0), "코어 옆 아래층");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(3, 1, 0), 0), "코어에서 한 칸 떨어진 2층");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(2, 2, 0), 0), "코어 옆 3층");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(2, 1, 2), 0), "대각선 모서리 칸은 옆면이 아님");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(1, 1, 0), 0), "1층짜리 모듈 옆은 조건 없음");
        }

        [Test]
        public void MultiCell_EachCoreSideCell_NeedsSupport()
        {
            var grid = BigCoreGrid();
            // bar (2,1,0)-(3,1,0): (2,1,0)만 코어 2층 옆 → 그 아래만 있으면 됨
            Assert.AreEqual(PlacementResult.NeedsSupport, PlacementRules.Evaluate(grid, _bar, new Vector3Int(2, 1, 0), 0));
            grid.TryPlace(_block, new Vector3Int(2, 0, 0), 0, out _);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _bar, new Vector3Int(2, 1, 0), 0));
        }

        [Test]
        public void SupportingModule_CannotBeRemoved_UntilUpperIsGone()
        {
            var grid = BigCoreGrid();
            grid.TryPlace(_block, new Vector3Int(2, 0, 0), 0, out var lower);
            grid.TryPlace(_block, new Vector3Int(2, 1, 0), 0, out var upper);
            Assert.IsTrue(PlacementRules.SupportsOthers(grid, lower));
            Assert.IsFalse(PlacementRules.SupportsOthers(grid, upper));
            grid.Remove(upper);
            Assert.IsFalse(PlacementRules.SupportsOthers(grid, lower));
        }

        [Test]
        public void CoreTop_NeedsSideContactWithNonCoreModule()
        {
            var grid = BigCoreGrid();
            var top = new Vector3Int(0, 2, 0);
            Assert.AreEqual(PlacementResult.CoreTopNeedsSideContact, PlacementRules.Evaluate(grid, _block, top, 0), "코어하고만 닿음");
            // 코어 옆 3층 (2층 옆이 아니므로 자유) → 그 옆이면 코어 위 가능
            Assert.IsTrue(grid.TryPlace(_block, new Vector3Int(-1, 2, 0), 0, out var side));
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, top, 0));
            grid.TryPlace(_block, top, 0, out var onTop);
            // 코어 위끼리 이어 붙이기: 코어 위 모듈이 옆에 있으면 됨
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(1, 2, 0), 0));
            // 유일한 옆 연결은 철거 불가, 코어 위 모듈을 먼저 치우면 가능
            Assert.IsTrue(PlacementRules.SupportsOthers(grid, side));
            grid.Remove(onTop);
            Assert.IsFalse(PlacementRules.SupportsOthers(grid, side));
        }

        [Test]
        public void ExistingInvalidLayout_DoesNotLockNeighbors()
        {
            // 예전 규칙 세이브처럼 이미 조건을 못 채우는 코어 위 모듈이 있어도 이웃 철거는 막지 않음
            var grid = BigCoreGrid();
            grid.TryPlace(_block, new Vector3Int(2, 1, 0), 0, out _); // 코어 2층 옆, 아래 받침 없음 (그리드 직접 배치 = 규칙 우회)
            grid.TryPlace(_block, new Vector3Int(3, 1, 0), 0, out var neighbor);
            Assert.IsFalse(PlacementRules.SupportsOthers(grid, neighbor), "원래부터 조건 밖인 모듈 때문에 잠기지 않음");
        }

        [Test]
        public void FreeCellAbove_DoesNotBlockRemoval()
        {
            var grid = BigCoreGrid();
            grid.TryPlace(_block, new Vector3Int(3, 0, 0), 0, out var lower);
            grid.TryPlace(_block, new Vector3Int(3, 1, 0), 0, out _);
            Assert.IsFalse(PlacementRules.SupportsOthers(grid, lower), "코어 2층 옆이 아닌 칸은 받침 관계 없음");
        }

        private ModuleData Module(string name, Vector3Int[] offsets, bool terminal)
        {
            var data = ScriptableObject.CreateInstance<ModuleData>();
            data.name = name;
            _created.Add(data);
            var so = new SerializedObject(data);
            var list = so.FindProperty("_cellOffsets");
            list.arraySize = offsets.Length;
            for (int i = 0; i < offsets.Length; i++)
                list.GetArrayElementAtIndex(i).vector3IntValue = offsets[i];
            so.FindProperty("_terminalOnly").boolValue = terminal;
            so.ApplyModifiedPropertiesWithoutUndo();
            return data;
        }
    }
}
