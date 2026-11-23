using System.Collections.Generic;
using SpaceStation.Core;

namespace SpaceStation.Interior
{
    /// <summary>위아래 이동 장치의 목적지 하나: 모듈 · 층 (모듈 가장 낮은 칸의 y) · 위쪽 줄기인지.</summary>
    public readonly struct TransitStop
    {
        public readonly ModuleInstance Module;
        public readonly int Floor;
        public readonly bool Up;

        public TransitStop(ModuleInstance module, int floor, bool up)
        {
            Module = module;
            Floor = floor;
            Up = up;
        }
    }

    /// <summary>
    /// 위아래 이동 장치 목적지 (순수 로직, 사용자 2026-11-19 "위아래로 이어진 모듈 전부, 깔끔하게"):
    /// 지금 모듈에서 수직 통로 (<see cref="InteriorFaceKind.Hatch"/> 면)로 위로만 · 아래로만 이어 간 모듈 — 옆으로 돌아 같은 층에 닿는 모듈은 넣지 않음.
    /// 위쪽 줄기 (높은 층부터) → 아래쪽 줄기 (높은 층부터), 같은 층은 원점 x · z 순.
    /// </summary>
    public sealed class InteriorTransitRoutes
    {
        private readonly Dictionary<ModuleInstance, List<ModuleInstance>> _up = new Dictionary<ModuleInstance, List<ModuleInstance>>();
        private readonly Dictionary<ModuleInstance, List<ModuleInstance>> _down = new Dictionary<ModuleInstance, List<ModuleInstance>>();

        public InteriorTransitRoutes(InteriorLayout layout)
        {
            if (layout == null)
                return;
            foreach (var face in layout.Faces)
            {
                if (face.Kind != InteriorFaceKind.Hatch)
                    continue;
                if (!layout.TryGetRoom(face.Cell, out var a) || !layout.TryGetRoom(face.OtherCell, out var b) || a.Module == b.Module)
                    continue;
                Link(face.Direction.y > 0 ? _up : _down, a.Module, b.Module);
            }
        }

        private static void Link(Dictionary<ModuleInstance, List<ModuleInstance>> map, ModuleInstance from, ModuleInstance to)
        {
            if (!map.TryGetValue(from, out var list))
                map[from] = list = new List<ModuleInstance>();
            if (!list.Contains(to))
                list.Add(to);
        }

        /// <summary>위나 아래로 이어진 모듈이 하나라도 있음 (장치를 둘 방).</summary>
        public bool HasAny(ModuleInstance module) => module != null && (_up.ContainsKey(module) || _down.ContainsKey(module));

        /// <summary>갈 수 있는 곳 (지금 모듈 빼고): 위쪽 줄기 → 아래쪽 줄기.</summary>
        public List<TransitStop> From(ModuleInstance module)
        {
            var result = new List<TransitStop>();
            if (module == null)
                return result;
            var seen = new HashSet<ModuleInstance> { module };
            result.AddRange(Walk(module, _up, true, seen));
            result.AddRange(Walk(module, _down, false, seen));
            return result;
        }

        private static List<TransitStop> Walk(ModuleInstance start, Dictionary<ModuleInstance, List<ModuleInstance>> map, bool up, HashSet<ModuleInstance> seen)
        {
            var found = new List<TransitStop>();
            var queue = new Queue<ModuleInstance>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                if (!map.TryGetValue(queue.Dequeue(), out var next))
                    continue;
                foreach (var m in next)
                {
                    if (!seen.Add(m))
                        continue;
                    found.Add(new TransitStop(m, FloorOf(m), up));
                    queue.Enqueue(m);
                }
            }
            found.Sort((a, b) =>
            {
                if (a.Floor != b.Floor)
                    return b.Floor.CompareTo(a.Floor);
                if (a.Module.Origin.x != b.Module.Origin.x)
                    return a.Module.Origin.x.CompareTo(b.Module.Origin.x);
                return a.Module.Origin.z.CompareTo(b.Module.Origin.z);
            });
            return found;
        }

        /// <summary>모듈의 층 = 가장 낮은 칸 y.</summary>
        public static int FloorOf(ModuleInstance module)
        {
            int floor = int.MaxValue;
            foreach (var c in module.Cells)
            {
                if (c.y < floor)
                    floor = c.y;
            }
            return floor == int.MaxValue ? module.Origin.y : floor;
        }

        /// <summary>층 이름: 0 이상 = 1F, 2F … · 음수 = B1, B2 … (건물 지하처럼).</summary>
        public static string FloorLabel(int floor) => floor >= 0 ? $"{floor + 1}F" : $"B{-floor}";
    }
}
