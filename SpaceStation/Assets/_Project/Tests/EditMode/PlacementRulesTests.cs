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

        [Test]
        public void Dock_WithExactlyOneContact_IsValid()
        {
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _dock, Vector3Int.up, 0));
        }

        [Test]
        public void Dock_WithTwoContacts_IsRejected()
        {
            // (1,0,1)은 (1,0,0)과 (0,0,1) 두 면에 닿음
            _grid.TryPlace(_block, Vector3Int.right, 0, out _);
            _grid.TryPlace(_block, new Vector3Int(0, 0, 1), 0, out _);
            Assert.AreEqual(PlacementResult.TerminalNeedsSingleContact,
                PlacementRules.Evaluate(_grid, _dock, new Vector3Int(1, 0, 1), 0));
        }

        [Test]
        public void Dock_WithNoContact_IsRejected()
        {
            Assert.AreEqual(PlacementResult.TerminalNeedsSingleContact,
                PlacementRules.Evaluate(_grid, _dock, new Vector3Int(5, 5, 5), 0));
        }

        [Test]
        public void AfterDock_ItsOtherFacesAreBlocked()
        {
            _grid.TryPlace(_dock, Vector3Int.up, 0, out _); // (0,1,0)
            foreach (var dir in GridDirections.Faces)
            {
                var cell = Vector3Int.up + dir;
                if (cell == Vector3Int.zero)
                    continue; // 부모(코어) 셀
                Assert.AreEqual(PlacementResult.BlockedByTerminal, PlacementRules.Evaluate(_grid, _block, cell, 0), $"cell {cell}");
            }
        }

        [Test]
        public void MultiCellModule_TouchingDockWithFarCell_IsBlocked()
        {
            _grid.TryPlace(_dock, Vector3Int.up, 0, out _); // (0,1,0)
            // bar (−1,2,0)-(0,2,0): (0,2,0)이 도킹 윗면에 닿음
            Assert.AreEqual(PlacementResult.BlockedByTerminal,
                PlacementRules.Evaluate(_grid, _bar, new Vector3Int(-1, 2, 0), 0));
        }

        [Test]
        public void DockOnDock_IsBlocked()
        {
            _grid.TryPlace(_dock, Vector3Int.up, 0, out _);
            Assert.AreEqual(PlacementResult.BlockedByTerminal, PlacementRules.Evaluate(_grid, _dock, new Vector3Int(0, 2, 0), 0));
        }

        [Test]
        public void AfterDockRemoved_FacesAreFreeAgain()
        {
            _grid.TryPlace(_dock, Vector3Int.up, 0, out var dock);
            _grid.Remove(dock);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, Vector3Int.right, 0));
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, Vector3Int.up, 0));
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
