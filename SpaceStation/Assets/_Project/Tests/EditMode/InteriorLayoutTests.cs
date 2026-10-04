using System.Linq;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Interior;
using UnityEngine;

namespace SpaceStation.Tests
{
    public class InteriorLayoutTests
    {
        private static readonly Vector3Int[] Single = { Vector3Int.zero };
        private static readonly Vector3Int[] TwoByOne = { Vector3Int.zero, Vector3Int.right };

        private StationGrid _grid;

        [SetUp]
        public void SetUp()
        {
            _grid = new StationGrid();
        }

        private ModuleInstance Place(Vector3Int[] offsets, Vector3Int origin)
        {
            Assert.IsTrue(_grid.TryPlace(offsets, origin, 0, out var module));
            return module;
        }

        private static InteriorFaceKind KindAt(InteriorLayout layout, Vector3Int cell, Vector3Int dir)
        {
            return layout.Faces.Single(f => f.Cell == cell && f.Direction == dir).Kind;
        }

        [Test]
        public void SingleModule_AllWalls()
        {
            var core = Place(Single, Vector3Int.zero);
            var layout = InteriorLayout.Build(_grid, _ => true, core);
            Assert.AreEqual(1, layout.Rooms.Count);
            Assert.AreSame(core, layout.StartRoom.Module);
            Assert.AreEqual(6, layout.Faces.Count);
            Assert.IsTrue(layout.Faces.All(f => f.Kind == InteriorFaceKind.Wall));
        }

        [Test]
        public void HorizontalNeighbor_IsDoorOnBothSides()
        {
            var a = Place(Single, Vector3Int.zero);
            var b = Place(Single, Vector3Int.right);
            var layout = InteriorLayout.Build(_grid, _ => true, a);
            Assert.AreEqual(2, layout.Rooms.Count);
            Assert.IsTrue(layout.Contains(b));
            Assert.AreEqual(InteriorFaceKind.Door, KindAt(layout, Vector3Int.zero, Vector3Int.right));
            Assert.AreEqual(InteriorFaceKind.Door, KindAt(layout, Vector3Int.right, Vector3Int.left));
            Assert.AreEqual(InteriorFaceKind.Wall, KindAt(layout, Vector3Int.zero, Vector3Int.left));
        }

        [Test]
        public void VerticalNeighbor_IsHatch()
        {
            var a = Place(Single, Vector3Int.zero);
            Place(Single, Vector3Int.up);
            var layout = InteriorLayout.Build(_grid, _ => true, a);
            Assert.AreEqual(InteriorFaceKind.Hatch, KindAt(layout, Vector3Int.zero, Vector3Int.up));
            Assert.AreEqual(InteriorFaceKind.Hatch, KindAt(layout, Vector3Int.up, Vector3Int.down));
        }

        [Test]
        public void MultiCellModule_InnerFacesOpen()
        {
            var big = Place(TwoByOne, Vector3Int.zero);
            var layout = InteriorLayout.Build(_grid, _ => true, big);
            Assert.AreEqual(1, layout.Rooms.Count);
            Assert.AreEqual(12, layout.Faces.Count);
            Assert.AreEqual(InteriorFaceKind.Open, KindAt(layout, Vector3Int.zero, Vector3Int.right));
            Assert.AreEqual(InteriorFaceKind.Open, KindAt(layout, Vector3Int.right, Vector3Int.left));
            Assert.IsTrue(layout.TryGetRoom(Vector3Int.right, out var room) && room.Module == big);
        }

        [Test]
        public void ReachesWholeChain_ThroughDoors()
        {
            var a = Place(Single, Vector3Int.zero);
            Place(Single, Vector3Int.right);
            Place(Single, new Vector3Int(2, 0, 0));
            Place(Single, new Vector3Int(2, 1, 0));
            var layout = InteriorLayout.Build(_grid, _ => true, a);
            Assert.AreEqual(4, layout.Rooms.Count);
        }

        [Test]
        public void InactiveNeighbor_IsExcludedAndWalled()
        {
            var a = Place(Single, Vector3Int.zero);
            var b = Place(Single, Vector3Int.right);
            var layout = InteriorLayout.Build(_grid, m => m != b, a);
            Assert.AreEqual(1, layout.Rooms.Count);
            Assert.IsFalse(layout.Contains(b));
            Assert.AreEqual(InteriorFaceKind.Wall, KindAt(layout, Vector3Int.zero, Vector3Int.right));
        }

        [Test]
        public void InactiveStart_OnlyOwnRoom()
        {
            var a = Place(Single, Vector3Int.zero);
            Place(Single, Vector3Int.right);
            var layout = InteriorLayout.Build(_grid, m => m != a, a);
            Assert.AreEqual(1, layout.Rooms.Count);
            Assert.AreEqual(InteriorFaceKind.Wall, KindAt(layout, Vector3Int.zero, Vector3Int.right));
        }
    }
}
