using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Data;
using SpaceStation.Interior;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>위아래 이동 장치 목적지 (2026-11-19): 위로만 · 아래로만 이어 간 모듈, 높은 층부터, 옆으로 돌아 닿는 모듈은 없음.</summary>
    public class InteriorTransitRoutesTests
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

        [Test]
        public void Stack_ListsWholeColumn_HighestFirst()
        {
            var b1 = Place(Single, Vector3Int.down);
            var ground = Place(Single, Vector3Int.zero);
            var f2 = Place(Single, Vector3Int.up);
            var f3 = Place(Single, Vector3Int.up * 2);
            var routes = new InteriorTransitRoutes(InteriorLayout.Build(_grid, _ => true, ground));

            var stops = routes.From(ground);
            CollectionAssert.AreEqual(new[] { f3, f2, b1 }, stops.Select(s => s.Module).ToArray());
            CollectionAssert.AreEqual(new[] { true, true, false }, stops.Select(s => s.Up).ToArray());
            CollectionAssert.AreEqual(new[] { 2, 1, -1 }, stops.Select(s => s.Floor).ToArray());
            Assert.IsTrue(routes.HasAny(f3));
        }

        [Test]
        public void SidewaysNeighbors_AreNotStops()
        {
            var a = Place(Single, Vector3Int.zero);
            var side = Place(Single, Vector3Int.right);
            var sideUp = Place(Single, Vector3Int.right + Vector3Int.up);
            var routes = new InteriorTransitRoutes(InteriorLayout.Build(_grid, _ => true, a));

            Assert.IsFalse(routes.HasAny(a));
            Assert.AreEqual(0, routes.From(a).Count);
            CollectionAssert.AreEqual(new[] { sideUp }, routes.From(side).Select(s => s.Module).ToArray());
        }

        [Test]
        public void WideModule_TwoModulesOnSameFloor_BothListed()
        {
            var wide = Place(TwoByOne, Vector3Int.zero);
            var left = Place(Single, Vector3Int.up);
            var right = Place(Single, Vector3Int.up + Vector3Int.right);
            var routes = new InteriorTransitRoutes(InteriorLayout.Build(_grid, _ => true, wide));

            var stops = routes.From(wide);
            Assert.AreEqual(2, stops.Count);
            Assert.IsTrue(stops.All(s => s.Up && s.Floor == 1));
            CollectionAssert.AreEquivalent(new[] { left, right }, stops.Select(s => s.Module).ToArray());
            // 위 칸 하나에서는 아래 넓은 방만 (옆 칸은 아래로 돌아가야 닿으므로 없음)
            CollectionAssert.AreEqual(new[] { wide }, routes.From(left).Select(s => s.Module).ToArray());
        }

        [Test]
        public void TopWithoutConnectionPoint_StillListed()
        {
            // 큰 진화 모듈은 외형 연결점 칸에만 바깥 통로 — 연결점 없는 칸 위 모듈도 목적지 (2026-11-20 사용자 "2 · 3단계 일부는 표기 안 됨")
            var data = ScriptableObject.CreateInstance<ModuleData>();
            try
            {
                var so = new SerializedObject(data);
                var list = so.FindProperty("_cellOffsets");
                list.arraySize = 2;
                list.GetArrayElementAtIndex(0).vector3IntValue = Vector3Int.zero;
                list.GetArrayElementAtIndex(1).vector3IntValue = Vector3Int.right;
                so.ApplyModifiedPropertiesWithoutUndo();
                data.SetFaceDepths(new List<FaceDepth> { new FaceDepth(Vector3Int.right, Vector3Int.up, -1f) });
                Assert.IsTrue(_grid.TryPlace(data, Vector3Int.zero, 0, out var wide));
                var onTop = Place(Single, Vector3Int.up + Vector3Int.right);
                var routes = new InteriorTransitRoutes(InteriorLayout.Build(_grid, _ => true, wide));

                CollectionAssert.AreEqual(new[] { onTop }, routes.From(wide).Select(s => s.Module).ToArray());
                CollectionAssert.AreEqual(new[] { wide }, routes.From(onTop).Select(s => s.Module).ToArray());
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }

        [Test]
        public void FloorLabel_GroundIs1F_BelowIsBasement()
        {
            Assert.AreEqual("1F", InteriorTransitRoutes.FloorLabel(0));
            Assert.AreEqual("3F", InteriorTransitRoutes.FloorLabel(2));
            Assert.AreEqual("B1", InteriorTransitRoutes.FloorLabel(-1));
        }
    }
}
