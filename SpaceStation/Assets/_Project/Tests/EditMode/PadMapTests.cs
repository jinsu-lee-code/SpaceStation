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

        /// <summary>11-14 내부 건설: 후보 칸(빈 · 닿은 칸, 위아래 층 포함) · 건설용 층 목록(위아래 한 층 더) · 붙는 면 방향.</summary>
        [Test]
        public void BuildCandidates_Floors_Outward()
        {
            var grid = new StationGrid();
            Assert.IsTrue(grid.TryPlace(new[] { Vector3Int.zero }, Vector3Int.zero, 0, out _));
            Assert.IsTrue(grid.TryPlace(new[] { Vector3Int.zero }, Vector3Int.right, 0, out _));

            var list = new List<Vector3Int>();
            PadMap.BuildCandidates(grid, 0, list);
            // (0,0,0) · (1,0,0) 둘레: x −1 · 2, z ±1 칸 두 개씩 = 6칸
            Assert.AreEqual(6, list.Count);
            Assert.IsTrue(list.Contains(new Vector3Int(-1, 0, 0)) && list.Contains(new Vector3Int(2, 0, 0)));
            Assert.IsFalse(list.Contains(Vector3Int.zero), "점유 칸은 아님");
            PadMap.BuildCandidates(grid, 1, list);
            Assert.AreEqual(2, list.Count, "위층 = 두 모듈 바로 위");
            PadMap.BuildCandidates(grid, 3, list);
            Assert.AreEqual(0, list.Count, "닿지 않는 층");

            CollectionAssert.AreEqual(new[] { -1, 0, 1 }, PadMap.BuildFloors(grid));

            Assert.AreEqual(Vector3Int.right, PadMap.Outward(grid, new Vector3Int(2, 0, 0)), "왼쪽 이웃에서 바깥(+x)");
            Assert.AreEqual(Vector3Int.up, PadMap.Outward(grid, new Vector3Int(0, 1, 0)), "아래 모듈 위");
            Assert.AreEqual(Vector3Int.zero, PadMap.Outward(grid, new Vector3Int(5, 0, 5)));
        }

        /// <summary>11-13 모형 고르기: 비스듬한 광선 → 바닥 높이 수평면과 만나는 칸. 수평 · 뒤쪽 광선은 실패.</summary>
        [Test]
        public void RayToCell_SlantedRay_HitsFloorCell()
        {
            // (2, 0.5, -1) 칸 가운데를 위 뒤쪽에서 비스듬히 내려다봄
            var target = new Vector3(2f, 0.5f, -1f);
            var origin = target + new Vector3(0.3f, 3f, -2f);
            Assert.IsTrue(PadMap.RayToCell(new Ray(origin, target - origin), 0.5f, out var cell));
            Assert.AreEqual(new Vector2Int(2, -1), cell);
            Assert.IsTrue(PadMap.RayToCell(new Ray(origin, target + new Vector3(0.45f, 0f, 0.3f) - origin), 0.5f, out cell));
            Assert.AreEqual(new Vector2Int(2, -1), cell, "칸 안쪽 반 칸 이내");
            Assert.IsFalse(PadMap.RayToCell(new Ray(origin, Vector3.forward), 0.5f, out _), "수평");
            Assert.IsFalse(PadMap.RayToCell(new Ray(origin, Vector3.up), 0.5f, out _), "뒤쪽(위로)");
        }

        /// <summary>11-13 홀로그램 외곽선: 정육면체(면마다 정점 따로 = UV 갈라짐) → 각진 모서리 12개만, 면 대각선은 없음. 짧은 선분 버림.</summary>
        [Test]
        public void HoloEdges_Cube_TwelveSharpEdges_NoDiagonals_DropsShort()
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Face(Vector3 n, Vector3 u, Vector3 v)
            {
                int b = verts.Count;
                var c = n * 0.5f;
                verts.Add(c - u * 0.5f - v * 0.5f);
                verts.Add(c + u * 0.5f - v * 0.5f);
                verts.Add(c + u * 0.5f + v * 0.5f);
                verts.Add(c - u * 0.5f + v * 0.5f);
                tris.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            Face(Vector3.up, Vector3.right, Vector3.forward);
            Face(Vector3.down, Vector3.right, Vector3.back);
            Face(Vector3.right, Vector3.forward, Vector3.up);
            Face(Vector3.left, Vector3.back, Vector3.up);
            Face(Vector3.forward, Vector3.left, Vector3.up);
            Face(Vector3.back, Vector3.right, Vector3.up);

            var lines = new List<Vector3>();
            HoloEdges.Extract(verts, tris, 38f, 0.01f, lines);
            Assert.AreEqual(24, lines.Count, "모서리 12개 = 끝점 24개 (면 대각선은 평평해서 빠짐)");
            for (int i = 0; i < lines.Count; i += 2)
                Assert.AreEqual(1f, (lines[i] - lines[i + 1]).magnitude, 1e-4f, "모서리 길이 1");

            lines.Clear();
            HoloEdges.Extract(verts, tris, 38f, 1.5f, lines);
            Assert.AreEqual(0, lines.Count, "최소 길이보다 짧으면 버림");
        }
    }
}
