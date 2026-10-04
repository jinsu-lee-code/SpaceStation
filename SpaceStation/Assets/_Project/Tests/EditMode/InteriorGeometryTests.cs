using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceStation.Data;
using SpaceStation.Interior;
using UnityEngine;

namespace SpaceStation.Tests
{
    public class InteriorGeometryTests
    {
        private const float F = InteriorGeometry.FloorOffset;
        private readonly List<WallPanel> _walls = new List<WallPanel>();
        private readonly List<FloorRect> _floors = new List<FloorRect>();

        [Test]
        public void SingleCell_FourWallsOfTwoPanels_FloorAndCeiling()
        {
            InteriorGeometry.FallbackWalls(new[] { Vector3Int.zero }, null, _walls);
            Assert.AreEqual(8, _walls.Count);
            Assert.IsTrue(_walls.All(w => Mathf.Approximately(w.Width, 3.2f) && !w.Door));
            Assert.IsTrue(_walls.All(w => Mathf.Approximately(w.Center.y, F + InteriorGeometry.PanelCenterAboveFloor)));
            InteriorGeometry.FallbackFloors(new[] { Vector3Int.zero }, null, _floors);
            Assert.AreEqual(2, _floors.Count);
            Assert.AreEqual(new Vector2(6.4f, 6.4f), _floors[0].Size);
            Assert.AreEqual(F + InteriorGeometry.RoomHeight, _floors.Single(f => f.Ceiling).Center.y, 0.001f);
        }

        [Test]
        public void DoorFace_CenteredDoorPanel()
        {
            var doors = new HashSet<(Vector3Int, Vector3Int)> { (Vector3Int.zero, Vector3Int.right) };
            InteriorGeometry.FallbackWalls(new[] { Vector3Int.zero }, doors, _walls);
            var door = _walls.Single(w => w.Door);
            Assert.AreEqual(3.2f, door.Center.x, 0.001f);
            Assert.AreEqual(0f, door.Center.z, 0.001f);
            Assert.AreEqual(Vector3.right, door.Normal);
            // 문 벽 줄 = 양옆 1.6 + 문 3.2
            Assert.AreEqual(6.4f, _walls.Where(w => w.Normal == Vector3.right).Sum(w => w.Width), 0.001f);
        }

        [Test]
        public void TwoCellModule_WallRunsReachSharedBoundary()
        {
            var cells = new[] { Vector3Int.zero, Vector3Int.right };
            InteriorGeometry.FallbackWalls(cells, null, _walls);
            var north = _walls.Where(w => w.Normal == new Vector3(0f, 0f, 1f)).ToList();
            // 칸마다 7.2 (벽 3.2 + 경계까지 4) → 3.6 × 2장씩
            Assert.AreEqual(4, north.Count);
            Assert.AreEqual(14.4f, north.Sum(w => w.Width), 0.001f);
            Assert.IsFalse(_walls.Any(w => Mathf.Approximately(w.Center.x, 4f) && w.Normal == Vector3.right), "같은 모듈 사이 벽 없음");
        }

        [Test]
        public void StackedCells_WallsTwoRows_NoFloorBetween()
        {
            var cells = new[] { Vector3Int.zero, Vector3Int.up };
            InteriorGeometry.FallbackWalls(cells, null, _walls);
            Assert.AreEqual(8 * 2 + 8, _walls.Count, "아래 칸은 위층 바닥까지 두 줄");
            InteriorGeometry.FallbackFloors(cells, null, _floors);
            Assert.AreEqual(2, _floors.Count, "맨 아래 바닥 + 맨 위 천장");
        }

        [Test]
        public void Tube_SpansBetweenWallSurfaces()
        {
            var tube = InteriorGeometry.Tube(Vector3Int.zero, Vector3Int.right, 3.2f, 3.2f);
            Assert.AreEqual(new Vector3(3.2f, F, 0f), tube.Start);
            Assert.AreEqual(1.6f, tube.Length, 0.001f);
            Assert.AreEqual(new Vector3(4.8f, F, 0f), tube.End);
        }

        [Test]
        public void TemplateSocket_FollowsModuleRotation()
        {
            var template = ScriptableObject.CreateInstance<InteriorTemplate>();
            template.EditorSet(null, null, new List<InteriorSocket> { new InteriorSocket(Vector3Int.right, Vector3Int.right, 3f) }, Vector3.zero, 0f);
            var origin = new Vector3Int(5, 0, 5);
            // 회전 1 (시계 90도): 로컬 +X → 월드 -Z
            Assert.IsTrue(template.TryGetSocket(origin, 1, origin + new Vector3Int(0, 0, -1), new Vector3Int(0, 0, -1), out var socket));
            Assert.AreEqual(3f, socket.Depth);
            Assert.IsFalse(template.TryGetSocket(origin, 1, origin + Vector3Int.right, Vector3Int.right, out _));
            Object.DestroyImmediate(template);
        }
    }
}
