using System.Collections.Generic;
using NUnit.Framework;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Tests
{
    public class StationConnectivityTests
    {
        private static readonly Vector3Int[] Single = { Vector3Int.zero };

        private StationGrid _grid;
        private StationConnectivity _connectivity;
        private List<(ModuleInstance module, bool active)> _events;

        [SetUp]
        public void SetUp()
        {
            _grid = new StationGrid();
            _connectivity = new StationConnectivity(_grid, new FaceAdjacencyConnectionRule());
            _events = new List<(ModuleInstance, bool)>();
            _connectivity.ActiveStateChanged += (m, a) => _events.Add((m, a));
        }

        private ModuleInstance Place(int x, int y = 0, int z = 0)
        {
            Assert.IsTrue(_grid.TryPlace(Single, new Vector3Int(x, y, z), 0, out var m));
            return m;
        }

        [Test]
        public void ChainFromCore_AllActive()
        {
            var core = Place(0);
            _connectivity.Root = core;
            var a = Place(1);
            var b = Place(2);
            _connectivity.Recalculate();

            Assert.IsTrue(_connectivity.IsActive(core));
            Assert.IsTrue(_connectivity.IsActive(a));
            Assert.IsTrue(_connectivity.IsActive(b));
            Assert.AreEqual(3, _events.Count, "처음 계산 시 모든 모듈의 상태를 알림");
        }

        [Test]
        public void RemovingMiddle_DisconnectsFarSide()
        {
            var core = Place(0);
            _connectivity.Root = core;
            var middle = Place(1);
            var far = Place(2);
            var farther = Place(3);
            _connectivity.Recalculate();
            _events.Clear();

            _grid.Remove(middle);
            _connectivity.Recalculate();

            Assert.IsTrue(_connectivity.IsActive(core));
            Assert.IsFalse(_connectivity.IsActive(far));
            Assert.IsFalse(_connectivity.IsActive(farther));
            CollectionAssert.AreEquivalent(new[] { (far, false), (farther, false) }, _events,
                "바뀐 모듈만 알림. 제거된 모듈은 알리지 않음");
        }

        [Test]
        public void ReconnectingIsland_ReactivatesIt()
        {
            var core = Place(0);
            _connectivity.Root = core;
            var middle = Place(1);
            var far = Place(2);
            _connectivity.Recalculate();
            _grid.Remove(middle);
            _connectivity.Recalculate();
            _events.Clear();

            var bridge = Place(1);
            _connectivity.Recalculate();

            Assert.IsTrue(_connectivity.IsActive(far));
            CollectionAssert.AreEquivalent(new[] { (bridge, true), (far, true) }, _events);
        }

        [Test]
        public void MultiCellModule_ConnectsThroughAnyCell()
        {
            var core = Place(0);
            _connectivity.Root = core;
            // 2x1x1 bar (0,1,0)-(1,1,0): 코어와는 (0,1,0) 셀로만 맞닿음
            _grid.TryPlace(new[] { Vector3Int.zero, Vector3Int.right }, new Vector3Int(0, 1, 0), 0, out var bar);
            // bar의 먼 쪽 셀 (1,1,0)에만 닿는 모듈
            var far = Place(2, 1, 0);
            _connectivity.Recalculate();

            Assert.IsTrue(_connectivity.IsActive(bar));
            Assert.IsTrue(_connectivity.IsActive(far));

            _grid.Remove(bar);
            _connectivity.Recalculate();
            Assert.IsFalse(_connectivity.IsActive(far));
        }

        [Test]
        public void DiagonalOnly_IsInactive()
        {
            var core = Place(0);
            _connectivity.Root = core;
            var diagonal = Place(1, 1, 0);
            _connectivity.Recalculate();

            Assert.IsFalse(_connectivity.IsActive(diagonal));
        }

        [Test]
        public void NoRoot_NothingActive()
        {
            var a = Place(0);
            _connectivity.Recalculate();
            Assert.IsFalse(_connectivity.IsActive(a));
            Assert.AreEqual(0, _connectivity.ActiveCount);
        }

        [Test]
        public void RecalculateWithoutChanges_EmitsNothing()
        {
            _connectivity.Root = Place(0);
            Place(1);
            _connectivity.Recalculate();
            _events.Clear();

            _connectivity.Recalculate();
            Assert.AreEqual(0, _events.Count);
        }
    }
}
