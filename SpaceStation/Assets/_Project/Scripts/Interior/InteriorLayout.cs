using System;
using System.Collections.Generic;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>칸의 한 면이 내부에서 어떻게 보이는지.</summary>
    public enum InteriorFaceKind
    {
        /// <summary>막힌 벽 (바깥, 통로 없는 이웃, 방문 범위 밖 모듈)</summary>
        Wall,
        /// <summary>같은 모듈의 칸 (벽 없음)</summary>
        Open,
        /// <summary>옆 모듈로 가는 문 (수평 통로)</summary>
        Door,
        /// <summary>위·아래 모듈로 가는 해치 (수직 통로)</summary>
        Hatch,
    }

    public readonly struct InteriorFace
    {
        public readonly Vector3Int Cell;
        public readonly Vector3Int Direction;
        public readonly InteriorFaceKind Kind;

        public InteriorFace(Vector3Int cell, Vector3Int direction, InteriorFaceKind kind)
        {
            Cell = cell;
            Direction = direction;
            Kind = kind;
        }

        /// <summary>문·해치 너머 칸.</summary>
        public Vector3Int OtherCell => Cell + Direction;
    }

    /// <summary>모듈 하나 = 방 하나.</summary>
    public sealed class InteriorRoom
    {
        public ModuleInstance Module { get; }
        public IReadOnlyList<Vector3Int> Cells => Module.Cells;

        public InteriorRoom(ModuleInstance module)
        {
            Module = module;
        }
    }

    /// <summary>
    /// Phase 11 내부 방문: 정거장 배치를 방·면 목록으로 바꾼다 (순수 로직).
    /// 시작 모듈에서 외부 연결 통로(<see cref="ConnectorLayout"/>)를 따라 활성 모듈로 넓혀 가고,
    /// 통로가 있는 면 = 문(수평)·해치(수직), 같은 모듈끼리 = 열림, 나머지 = 벽.
    /// 시작 모듈이 비활성이면 그 방 하나만 (이웃도 같은 비활성 덩어리라 들어가지 않음).
    /// </summary>
    public sealed class InteriorLayout
    {
        private readonly List<InteriorRoom> _rooms = new List<InteriorRoom>();
        private readonly List<InteriorFace> _faces = new List<InteriorFace>();
        private readonly Dictionary<ModuleInstance, InteriorRoom> _byModule = new Dictionary<ModuleInstance, InteriorRoom>();
        private readonly Dictionary<Vector3Int, InteriorRoom> _byCell = new Dictionary<Vector3Int, InteriorRoom>();

        public IReadOnlyList<InteriorRoom> Rooms => _rooms;
        public IReadOnlyList<InteriorFace> Faces => _faces;
        public InteriorRoom StartRoom { get; private set; }

        private InteriorLayout()
        {
        }

        public bool Contains(ModuleInstance module) => module != null && _byModule.ContainsKey(module);

        public bool TryGetRoom(Vector3Int cell, out InteriorRoom room) => _byCell.TryGetValue(cell, out room);

        public static InteriorLayout Build(StationGrid grid, Func<ModuleInstance, bool> isActive, ModuleInstance start)
        {
            if (grid == null)
                throw new ArgumentNullException(nameof(grid));
            var layout = new InteriorLayout();
            if (start == null)
                return layout;

            // 1) 통로를 따라 방 모으기 (BFS)
            var connectors = new List<ConnectorSpec>();
            var passages = new HashSet<(Vector3Int, Vector3Int)>();
            var queue = new Queue<ModuleInstance>();
            layout.AddRoom(start);
            layout.StartRoom = layout._rooms[0];
            queue.Enqueue(start);
            bool startActive = isActive == null || isActive(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                ConnectorLayout.ForModule(grid, current, connectors);
                foreach (var spec in connectors)
                {
                    passages.Add(spec.Key);
                    if (!startActive)
                        continue;
                    var cell = current.Cells.Contains(spec.CellA) ? spec.CellB : spec.CellA;
                    if (!grid.TryGetModule(cell, out var other) || layout.Contains(other))
                        continue;
                    if (isActive != null && !isActive(other))
                        continue;
                    layout.AddRoom(other);
                    queue.Enqueue(other);
                }
            }

            // 2) 면 분류
            foreach (var room in layout._rooms)
            {
                foreach (var cell in room.Cells)
                {
                    foreach (var dir in GridDirections.Faces)
                    {
                        var next = cell + dir;
                        InteriorFaceKind kind;
                        if (grid.TryGetModule(next, out var other) && other == room.Module)
                            kind = InteriorFaceKind.Open;
                        else if (other != null && layout.Contains(other) && passages.Contains(ConnectorLayout.MakeKey(cell, next)))
                            kind = dir.y != 0 ? InteriorFaceKind.Hatch : InteriorFaceKind.Door;
                        else
                            kind = InteriorFaceKind.Wall;
                        layout._faces.Add(new InteriorFace(cell, dir, kind));
                    }
                }
            }
            return layout;
        }

        private void AddRoom(ModuleInstance module)
        {
            var room = new InteriorRoom(module);
            _rooms.Add(room);
            _byModule.Add(module, room);
            foreach (var cell in module.Cells)
                _byCell[cell] = room;
        }
    }

    internal static class CellListExtensions
    {
        public static bool Contains(this IReadOnlyList<Vector3Int> cells, Vector3Int cell)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i] == cell)
                    return true;
            }
            return false;
        }
    }
}
