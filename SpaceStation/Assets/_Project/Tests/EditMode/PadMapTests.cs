using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceStation.Core;
using SpaceStation.Interior;
using UnityEngine;

namespace SpaceStation.Tests
{
    /// <summary>11-12 휴대 패드 미니맵: 층 칸 목록 · 같은 모듈 잇기 · 갈 수 있는 방 · 해치 · 층 이동 · 내 위치.</summary>
    public class PadMapTests
    {
        [Test]
        public void Build_ListsOnlyThatFloor_JoinsSameModule_MarksReachableAndHatch()
        {
            var grid = new StationGrid();
            Assert.IsTrue(grid.TryPlace(new[] { Vector3Int.zero }, Vector3Int.zero, 0, out var core));
            Assert.IsTrue(grid.TryPlace(new[] { Vector3Int.zero, Vector3Int.right }, new Vector3Int(1, 0, 0), 0, out var wide));   // 2칸 (x 1 · 2)
            Assert.IsTrue(grid.TryPlace(new[] { Vector3Int.zero }, new Vector3Int(0, 1, 0), 0, out var upper));                     // 위층
            Assert.IsTrue(grid.TryPlace(new[] { Vector3Int.zero }, new Vector3Int(0, 0, 3), 0, out var far));                       // 떨어진 모듈

            var cells = new List<PadMapCell>();
            PadMap.Build(grid, 0, m => m != far, (c, d) => c == Vector3Int.zero && d == Vector3Int.up, cells);

            Assert.AreEqual(4, cells.Count, "0층 칸만 (코어 1 + 넓은 모듈 2 + 떨어진 모듈 1)");
            var c0 = cells.Single(c => c.Cell == new Vector2Int(0, 0));
            var c1 = cells.Single(c => c.Cell == new Vector2Int(1, 0));
            var c2 = cells.Single(c => c.Cell == new Vector2Int(2, 0));
            var c3 = cells.Single(c => c.Cell == new Vector2Int(0, 3));
            Assert.AreSame(core, c0.Module);
            Assert.IsFalse(c0.JoinX, "코어와 넓은 모듈은 다른 모듈 → 잇지 않음");
            Assert.IsTrue(c1.JoinX, "같은 모듈 칸끼리 이음");
            Assert.IsFalse(c2.JoinX);
            Assert.IsTrue(c0.Reachable && c1.Reachable);
            Assert.IsFalse(c3.Reachable, "내부에 없는 방");
            Assert.IsTrue(c0.HatchUp);
            Assert.IsFalse(c0.HatchDown);
            Assert.IsFalse(c1.HatchUp);

            PadMap.Build(grid, 1, null, null, cells);
            Assert.AreEqual(1, cells.Count);
            Assert.AreSame(upper, cells[0].Module);
            Assert.IsFalse(cells[0].Reachable);
        }

        [Test]
        public void Floors_Step_AndPlayerPosition()
        {
            var grid = new StationGrid();
            grid.TryPlace(new[] { Vector3Int.zero }, Vector3Int.zero, 0, out _);
            grid.TryPlace(new[] { Vector3Int.zero }, new Vector3Int(0, 2, 0), 0, out _);
            grid.TryPlace(new[] { Vector3Int.zero }, new Vector3Int(0, -1, 0), 0, out _);
            var floors = PadMap.Floors(grid);
            CollectionAssert.AreEqual(new[] { -1, 0, 2 }, floors);
            Assert.AreEqual(2, PadMap.Step(floors, 0, 1), "빈 층(1)은 건너뜀");
            Assert.AreEqual(-1, PadMap.Step(floors, 0, -1));
            Assert.AreEqual(2, PadMap.Step(floors, 2, 1), "맨 위에서 더 올라가면 그대로");

            var origin = new Vector3(0f, -5000f, 0f);
            var p = PadMap.ToMap(origin + new Vector3(8f * 2.5f, 3f, -8f), origin, 8f);
            Assert.AreEqual(2.5f, p.x, 1e-4f);
            Assert.AreEqual(-1f, p.y, 1e-4f);
        }
    }
}
