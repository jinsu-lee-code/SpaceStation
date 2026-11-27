using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Core
{
    /// <summary>
    /// CONNECT M7 묶음 옮기기의 자리 계산 (사용자 결정 2026-11-27: 모양 그대로 한 덩어리 · 묶음째 90도 회전).
    /// 기준 모듈(pivot)을 새 원점에 두고, 나머지는 pivot에서 떨어진 만큼을 같은 회전으로 돌려 둔다.
    /// </summary>
    public static class GroupMove
    {
        /// <summary>묶음의 새 원점 · 회전 (pivot 새 원점 + pivot 중심 deltaRotation × 90도).</summary>
        public static void Resolve(IReadOnlyList<ModuleInstance> modules, ModuleInstance pivot, Vector3Int pivotOrigin, int deltaRotation,
            List<Vector3Int> origins, List<int> rotations)
        {
            origins.Clear();
            rotations.Clear();
            foreach (var m in modules)
            {
                origins.Add(pivotOrigin + GridDirections.Rotate(m.Origin - pivot.Origin, deltaRotation));
                rotations.Add(GridDirections.NormalizeRotation(m.Rotation + deltaRotation));
            }
        }

        /// <summary>
        /// 면을 가리켜 놓을 때 pivot 원점: 묶음 칸 중 면 안쪽(outward 반대)으로 가장 튀어나온 층이 면 바로 바깥 칸 층에 오게 민다
        /// (<see cref="PlacementRules.AnchorOnFace"/>의 묶음판 — 가리킨 모듈 속으로 묶음이 파고들지 않게).
        /// </summary>
        public static Vector3Int AnchorOnFace(IReadOnlyList<ModuleInstance> modules, ModuleInstance pivot, Vector3Int faceCell, Vector3Int outward, int deltaRotation)
        {
            if (outward == Vector3Int.zero)
                return faceCell;
            int min = 0;
            foreach (var m in modules)
            {
                if (m.Data == null)
                    continue;
                var offset = GridDirections.Rotate(m.Origin - pivot.Origin, deltaRotation);
                foreach (var local in m.Data.CellOffsets)
                {
                    var c = offset + GridDirections.Rotate(local, m.Rotation + deltaRotation);
                    int d = c.x * outward.x + c.y * outward.y + c.z * outward.z;
                    if (d < min)
                        min = d;
                }
            }
            return faceCell - outward * min;
        }

        /// <summary>모든 모듈이 지금 자리 · 회전 그대로인지.</summary>
        public static bool IsUnchanged(IReadOnlyList<ModuleInstance> modules, IReadOnlyList<Vector3Int> origins, IReadOnlyList<int> rotations)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i].Origin != origins[i] || modules[i].Rotation != GridDirections.NormalizeRotation(rotations[i]))
                    return false;
            }
            return true;
        }
    }
}
