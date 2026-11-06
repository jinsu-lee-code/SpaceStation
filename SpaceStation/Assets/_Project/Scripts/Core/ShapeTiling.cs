using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Core
{
    /// <summary>
    /// 칸 묶음을 모듈 모양(90도 회전만)으로 빈틈없이 채우는 배치 찾기 (정확 덮기, 작은 완전 탐색 — 레시피 규격 · 합성 틀 크기면 금방 끝남).
    /// 재료의 비워 둘 칸(<see cref="ModuleData.KeepClearCells"/>)에는 다른 재료가 못 온다. 레시피 창 그림(재료 놓는 모양) · 레시피 검사 테스트가 쓴다.
    /// </summary>
    public static class ShapeTiling
    {
        public struct Piece
        {
            public ModuleData Data;
            public Vector3Int Origin;
            public int Rotation;
        }

        /// <summary>cells를 pieces(각 1개씩)로 빈틈없이 채우는 배치 (output, 실패면 false).</summary>
        public static bool TryTile(IEnumerable<Vector3Int> cells, IReadOnlyList<ModuleData> pieces, List<Piece> output)
        {
            output.Clear();
            var sorted = new List<ModuleData>(pieces);
            sorted.Sort((a, b) => b.CellOffsets.Count.CompareTo(a.CellOffsets.Count)); // 큰 조각부터
            return Cover(new HashSet<Vector3Int>(cells), sorted, new bool[sorted.Count], new HashSet<Vector3Int>(), output);
        }

        /// <summary>남은 첫 칸(아래 · 앞 · 왼쪽)을 덮을 재료 · 회전 · 위치를 하나씩 시도.</summary>
        private static bool Cover(HashSet<Vector3Int> target, List<ModuleData> pieces, bool[] used, HashSet<Vector3Int> covered, List<Piece> output)
        {
            Vector3Int? first = null;
            foreach (var c in target)
            {
                if (covered.Contains(c))
                    continue;
                if (first == null || Less(c, first.Value))
                    first = c;
            }
            if (first == null)
                return true;
            var tried = new HashSet<ModuleData>();
            for (int i = 0; i < pieces.Count; i++)
            {
                if (used[i] || pieces[i] == null || !tried.Add(pieces[i]))
                    continue;
                for (int rot = 0; rot < 4; rot++)
                {
                    var rel = StationGrid.ResolveCells(pieces[i].CellOffsets, Vector3Int.zero, rot);
                    var keep = StationGrid.ResolveCells(pieces[i].KeepClearCells, Vector3Int.zero, rot);
                    foreach (var anchor in rel)
                    {
                        var origin = first.Value - anchor;
                        bool fits = true;
                        foreach (var c in rel)
                            fits &= target.Contains(c + origin) && !covered.Contains(c + origin);
                        foreach (var c in keep)
                            fits &= !target.Contains(c + origin); // 비워 둘 칸에는 다른 재료가 못 옴
                        if (!fits)
                            continue;
                        foreach (var c in rel)
                            covered.Add(c + origin);
                        used[i] = true;
                        output.Add(new Piece { Data = pieces[i], Origin = origin, Rotation = rot });
                        if (Cover(target, pieces, used, covered, output))
                            return true;
                        output.RemoveAt(output.Count - 1);
                        used[i] = false;
                        foreach (var c in rel)
                            covered.Remove(c + origin);
                    }
                }
            }
            return false;
        }

        private static bool Less(Vector3Int a, Vector3Int b)
        {
            if (a.y != b.y) return a.y < b.y;
            if (a.z != b.z) return a.z < b.z;
            return a.x < b.x;
        }
    }
}
