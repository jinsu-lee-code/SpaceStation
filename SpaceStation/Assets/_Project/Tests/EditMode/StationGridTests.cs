using NUnit.Framework;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Tests
{
    public class StationGridTests
    {
        private static readonly Vector3Int[] Single = { Vector3Int.zero };
        private static readonly Vector3Int[] TwoByOne = { Vector3Int.zero, Vector3Int.right };

        private StationGrid _grid;

        [SetUp]
        public void SetUp()
        {
            _grid = new StationGrid();
        }

        [Test]
        public void Place_SingleCell_OccupiesCell()
        {
            Assert.IsTrue(_grid.TryPlace(Single, new Vector3Int(1, 2, 3), 0, out var module));
            Assert.IsTrue(_grid.IsOccupied(new Vector3Int(1, 2, 3)));
            Assert.IsTrue(_grid.TryGetModule(new Vector3Int(1, 2, 3), out var found));
            Assert.AreSame(module, found);
            Assert.AreEqual(1, _grid.ModuleCount);
        }

        [Test]
        public void Place_OnOccupiedCell_IsRejected()
        {
            Assert.IsTrue(_grid.TryPlace(Single, Vector3Int.zero, 0, out _));
            Assert.IsFalse(_grid.CanPlace(Single, Vector3Int.zero, 0));
            Assert.IsFalse(_grid.TryPlace(Single, Vector3Int.zero, 0, out var second));
            Assert.IsNull(second);
            Assert.AreEqual(1, _grid.ModuleCount);
        }

        [Test]
        public void Place_MultiCellPartialOverlap_IsRejectedAndLeavesNoTrace()
        {
            _grid.TryPlace(Single, Vector3Int.right, 0, out _);

            Assert.IsFalse(_grid.TryPlace(TwoByOne, Vector3Int.zero, 0, out _));
            Assert.IsFalse(_grid.IsOccupied(Vector3Int.zero), "실패한 배치가 셀을 점유하면 안 됨");
            Assert.AreEqual(1, _grid.ModuleCount);
        }

        [Test]
        public void Place_MultiCell_AllCellsMapToSameModule()
        {
            Assert.IsTrue(_grid.TryPlace(TwoByOne, Vector3Int.zero, 0, out var module));
            Assert.AreEqual(2, module.Cells.Count);
            _grid.TryGetModule(Vector3Int.zero, out var a);
            _grid.TryGetModule(Vector3Int.right, out var b);
            Assert.AreSame(module, a);
            Assert.AreSame(module, b);
        }

        [Test]
        public void Place_EmptyOrDuplicateOffsets_IsRejected()
        {
            Assert.IsFalse(_grid.TryPlace(new Vector3Int[0], Vector3Int.zero, 0, out _));
            Assert.IsFalse(_grid.TryPlace(new[] { Vector3Int.zero, Vector3Int.zero }, Vector3Int.zero, 0, out _));
            Assert.AreEqual(0, _grid.ModuleCount);
        }

        [Test]
        public void Rotate_QuarterTurns_AroundY()
        {
            var offset = new Vector3Int(1, 0, 0);
            Assert.AreEqual(new Vector3Int(0, 0, -1), GridDirections.Rotate(offset, 1));
            Assert.AreEqual(new Vector3Int(-1, 0, 0), GridDirections.Rotate(offset, 2));
            Assert.AreEqual(new Vector3Int(0, 0, 1), GridDirections.Rotate(offset, 3));
            Assert.AreEqual(offset, GridDirections.Rotate(offset, 4));
            Assert.AreEqual(GridDirections.Rotate(offset, 3), GridDirections.Rotate(offset, -1));
            Assert.AreEqual(new Vector3Int(0, 5, 0), GridDirections.Rotate(new Vector3Int(0, 5, 0), 1), "Y는 회전에 영향받지 않음");
        }

        [Test]
        public void Place_RotatedMultiCell_OccupiesRotatedCells()
        {
            Assert.IsTrue(_grid.TryPlace(TwoByOne, Vector3Int.zero, 1, out var module));
            Assert.IsTrue(_grid.IsOccupied(new Vector3Int(0, 0, -1)));
            Assert.IsFalse(_grid.IsOccupied(Vector3Int.right));
            Assert.AreEqual(1, module.Rotation);
        }

        [Test]
        public void Remove_FreesAllCells_AndAllowsReplacement()
        {
            _grid.TryPlace(TwoByOne, Vector3Int.zero, 0, out var module);

            Assert.IsTrue(_grid.Remove(module));
            Assert.IsFalse(_grid.IsOccupied(Vector3Int.zero));
            Assert.IsFalse(_grid.IsOccupied(Vector3Int.right));
            Assert.AreEqual(0, _grid.ModuleCount);
            Assert.IsTrue(_grid.TryPlace(Single, Vector3Int.right, 0, out _));
        }

        [Test]
        public void Remove_UnknownOrTwice_ReturnsFalse()
        {
            _grid.TryPlace(Single, Vector3Int.zero, 0, out var module);
            Assert.IsTrue(_grid.Remove(module));
            Assert.IsFalse(_grid.Remove(module));
            Assert.IsFalse(_grid.Remove(null));
        }

        [Test]
        public void Events_FireOnPlaceAndRemove()
        {
            ModuleInstance placed = null, removed = null;
            _grid.ModulePlaced += m => placed = m;
            _grid.ModuleRemoved += m => removed = m;

            _grid.TryPlace(Single, Vector3Int.zero, 0, out var module);
            Assert.AreSame(module, placed);

            _grid.TryPlace(Single, Vector3Int.zero, 0, out _);
            Assert.AreSame(module, placed, "실패한 배치는 이벤트를 발생시키지 않음");

            _grid.Remove(module);
            Assert.AreSame(module, removed);
        }

        [Test]
        public void NeighborModules_SixDirections_NoDuplicates()
        {
            _grid.TryPlace(Single, Vector3Int.zero, 0, out var center);
            foreach (var dir in GridDirections.Faces)
                _grid.TryPlace(Single, dir, 0, out _);
            _grid.TryPlace(Single, new Vector3Int(1, 1, 0), 0, out var diagonal);

            var neighbors = _grid.GetNeighborModules(center);
            Assert.AreEqual(6, neighbors.Count);
            CollectionAssert.DoesNotContain(neighbors, diagonal, "대각선은 이웃이 아님");
            Assert.AreEqual(6, _grid.CountOccupiedFaces(Vector3Int.zero));
        }

        [Test]
        public void NeighborModules_MultiCellTouchingTwice_CountedOnce()
        {
            _grid.TryPlace(TwoByOne, Vector3Int.zero, 0, out var bar);
            _grid.TryPlace(TwoByOne, Vector3Int.up, 0, out var top); // 두 셀 모두 bar와 맞닿음

            var neighbors = _grid.GetNeighborModules(bar);
            Assert.AreEqual(1, neighbors.Count);
            Assert.AreSame(top, neighbors[0]);
        }

        [Test]
        public void GridConfig_CellWorldRoundTrip()
        {
            var cell = new Vector3Int(-3, 4, 7);
            Assert.AreEqual(cell, GridConfig.WorldToCell(GridConfig.CellToWorld(cell)));
        }
    }
}
