using System;
using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-6b 이민선이 붙는 자리 (사용자 결정 2026-10-19: 여객 도킹 포트가 있으면 포트 입구, 없으면 코어 쪽 바깥 끝).
    /// 결과 = 배 코(도킹 고리)가 닿을 점 + 바깥쪽 방향(배 몸통이 뻗는 쪽, 배 코는 반대 = 정거장을 봄). 화면 연출만 쓰고 시뮬레이션은 바꾸지 않는다.
    /// </summary>
    public static class ArkDocking
    {
        /// <summary>코 끝과 면 사이 틈 (맞닿는 틈 깜빡임 방지).</summary>
        public const float Gap = 0.03f;

        private static readonly Vector3Int[] Sides = { new Vector3Int(0, 0, 1), new Vector3Int(1, 0, 0), new Vector3Int(0, 0, -1), new Vector3Int(-1, 0, 0) };

        /// <param name="isActive">가동 중인 모듈만 포트로 셈 (null = 모두)</param>
        public static bool Find(StationGrid grid, ModuleInstance core, Func<ModuleInstance, bool> isActive, out Vector3 nose, out Vector3 outward)
        {
            nose = Vector3.zero;
            outward = Vector3.forward;
            if (grid == null)
                return false;
            // 1) 여객 도킹 포트 입구 (모듈 번호가 가장 작은 것 = 먼저 지은 것)
            ModuleInstance port = null;
            foreach (var m in grid.Modules)
                if (m.Data != null && m.Data.ImmigrationPort && (isActive == null || isActive(m)) && (port == null || m.Id < port.Id))
                    port = m;
            if (port != null)
            {
                var front = PlacementRules.DockFrontWorld(port.Data, port.Rotation);
                int best = int.MinValue;
                foreach (var c in port.Cells)
                    best = Math.Max(best, Dot(c, front));
                Vector3 sum = Vector3.zero;
                int n = 0;
                foreach (var c in port.Cells)
                    if (Dot(c, front) == best)
                    {
                        sum += GridConfig.CellToWorld(c);
                        n++;
                    }
                outward = (Vector3)front;
                nose = sum / Math.Max(1, n) + outward * (GridConfig.CellSize * 0.5f + Gap);
                return true;
            }
            // 2) 코어에서 수평 네 방향 중 바깥 끝이 가장 가까운 쪽 (그 줄의 마지막 가동 모듈 바깥면 — 끊긴 모듈에는 붙지 않음)
            if (core == null)
                return false;
            var start = core.Origin;
            int bestReach = int.MaxValue;
            foreach (var d in Sides)
            {
                int reach = 0;
                for (int k = 1; k <= 64; k++)
                    if (grid.TryGetModule(start + d * k, out var m) && (m == core || isActive == null || isActive(m)))
                        reach = k;
                if (reach < bestReach)
                {
                    bestReach = reach;
                    outward = (Vector3)d;
                }
            }
            nose = GridConfig.CellToWorld(start) + outward * ((bestReach + 0.5f) * GridConfig.CellSize + Gap);
            return true;
        }

        private static int Dot(Vector3Int a, Vector3Int b) => a.x * b.x + a.y * b.y + a.z * b.z;
    }
}
