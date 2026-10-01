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

        // ---- 받침 규칙 (5-5) ----

        [Test]
        public void Upper_WithModuleBelow_IsValid()
        {
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, Vector3Int.up, 0), "코어 위");
        }

        [Test]
        public void Upper_Floating_NeedsSupport()
        {
            // (1,1,0): 코어 윗층 옆이지만 바로 아래 (1,0,0)이 비어 있음
            Assert.AreEqual(PlacementResult.NeedsSupport, PlacementRules.Evaluate(_grid, _block, new Vector3Int(1, 1, 0), 0));
            _grid.TryPlace(_block, Vector3Int.right, 0, out _);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, new Vector3Int(1, 1, 0), 0), "아래층을 먼저 지으면 가능");
        }

        [Test]
        public void MultiCell_OneSupportedCell_IsEnough()
        {
            // bar (1,1,0)-(2,1,0): (1,1,0) 아래에만 모듈
            _grid.TryPlace(_block, Vector3Int.right, 0, out _);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _bar, new Vector3Int(1, 1, 0), 0));
            Assert.AreEqual(PlacementResult.NeedsSupport, PlacementRules.Evaluate(_grid, _bar, new Vector3Int(2, 1, 0), 0), "(2,1,0)-(3,1,0) 아래는 모두 빔");
        }

        [Test]
        public void Lower_HangsFromModuleAbove()
        {
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(_grid, _block, Vector3Int.down, 0), "코어 아래 매달기");
            Assert.AreEqual(PlacementResult.NeedsSupport, PlacementRules.Evaluate(_grid, _block, new Vector3Int(1, -1, 0), 0));
        }

        [Test]
        public void SupportingModule_CannotBeRemoved_UntilUpperIsGone()
        {
            _grid.TryPlace(_block, Vector3Int.right, 0, out var lower);
            _grid.TryPlace(_block, new Vector3Int(1, 1, 0), 0, out var upper);
            Assert.IsTrue(PlacementRules.SupportsOthers(_grid, lower));
            Assert.IsFalse(PlacementRules.SupportsOthers(_grid, upper));
            _grid.Remove(upper);
            Assert.IsFalse(PlacementRules.SupportsOthers(_grid, lower));
        }

        [Test]
        public void ModuleWithoutTopSupport_CannotHoldModulesAbove_ButCanHangBelow()
        {
            // 2x2x2 코어처럼 위층이 비어 보이는 모듈: 윗면에는 못 얹고, 아래 매달기는 가능
            var core = Module("BigCore", new[]
            {
                new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 1),
                new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(0, 1, 1), new Vector3Int(1, 1, 1),
            }, terminal: false);
            var so = new SerializedObject(core);
            so.FindProperty("_supportsTop").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            var grid = new StationGrid();
            grid.TryPlace(core, Vector3Int.zero, 0, out _);
            Assert.AreEqual(PlacementResult.NeedsSupport, PlacementRules.Evaluate(grid, _block, new Vector3Int(0, 2, 0), 0), "코어 위");
            Assert.AreEqual(PlacementResult.NeedsSupport, PlacementRules.Evaluate(grid, _block, new Vector3Int(2, 1, 0), 0), "위층 옆: 아래가 비어 있음");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(2, 0, 0), 0), "1층 옆");
            grid.TryPlace(_block, new Vector3Int(2, 0, 0), 0, out _);
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(2, 1, 0), 0), "1층을 지으면 위층 옆 가능");
            Assert.AreEqual(PlacementResult.Valid, PlacementRules.Evaluate(grid, _block, new Vector3Int(0, -1, 0), 0), "코어 아래 매달기");
        }

        [Test]
        public void UpperWithAnotherSupport_DoesNotBlockRemoval()
        {
            // bar (0,1,0)-(1,1,0): 코어(0,0,0)와 (1,0,0) 둘 다 받침 → (1,0,0)은 철거 가능
            _grid.TryPlace(_block, Vector3Int.right, 0, out var lower);
            _grid.TryPlace(_bar, Vector3Int.up, 0, out _);
            Assert.IsFalse(PlacementRules.SupportsOthers(_grid, lower));
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
