using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 방어 모듈 (4-8, BALANCE 19번). 둘 다 범위형, 거리는 격자 칸 기준 체비셰프 거리(셀끼리 최소값).
    /// 포탑: 범위 안 모듈로 오는 운석 1발당 격추(완전 제거) 확률 = Σ(확률 × 가동률), 상한 BalanceConfig.
    /// 실드: 범위 안 모듈로 오는 운석 1발을 빗겨낼 확률 = 감소율 × 가동률. 여러 실드는 가장 강한 것 하나만.
    ///   빗겨낸 운석은 일정 확률(BalanceConfig)로 그 실드 범위 밖 외곽 모듈에 맞는다 (StationSimulation.ApplyMeteor).
    /// 가동률은 소유자가 제공 (파손·비활성 0, 전력 효율·내구도 효율 반영).
    /// 운석이 올 때만 계산하므로 캐시하지 않는다.
    /// </summary>
    public sealed class DefenseSystem
    {
        private readonly BalanceConfig _config;
        private readonly Func<ModuleInstance, float> _strength;

        public DefenseSystem(BalanceConfig config, Func<ModuleInstance, float> strength)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _strength = strength ?? (_ => 1f);
        }

        /// <summary>방어 모듈의 현재 가동률 (0~1).</summary>
        public float GetStrength(ModuleInstance defender) => Mathf.Clamp01(_strength(defender));

        /// <summary>이 모듈로 오는 운석 1발을 실드가 빗겨낼 확률 (0 = 보호 없음).</summary>
        public float GetShieldBlockChance(StationGrid grid, ModuleInstance target) => GetShieldBlockChance(grid, target, out _);

        /// <param name="shield">가장 강한(빗겨낼) 실드. 없으면 null.</param>
        public float GetShieldBlockChance(StationGrid grid, ModuleInstance target, out ModuleInstance shield)
        {
            float best = 0f;
            shield = null;
            foreach (var d in grid.Modules)
            {
                var data = d.Data;
                if (data == null || !data.IsShield || !InRange(d.Cells, target.Cells, data.ShieldRadius))
                    continue;
                float chance = data.ShieldReduction * GetStrength(d);
                if (chance > best)
                {
                    best = chance;
                    shield = d;
                }
            }
            return best;
        }

        /// <summary>이 실드의 범위 안인지 (튕김 대상 제외용).</summary>
        public static bool IsInShieldRange(ModuleInstance shield, ModuleInstance module)
            => shield?.Data != null && InRange(shield.Cells, module.Cells, shield.Data.ShieldRadius);

        /// <summary>이 모듈로 오는 운석 1발의 격추 확률.</summary>
        public float GetInterceptChance(StationGrid grid, ModuleInstance target)
        {
            float sum = 0f;
            foreach (var d in grid.Modules)
            {
                var data = d.Data;
                if (data == null || !data.IsTurret || !InRange(d.Cells, target.Cells, data.TurretRadius))
                    continue;
                sum += data.TurretInterceptChance * GetStrength(d);
            }
            return Mathf.Min(sum, _config.TurretMaxIntercept);
        }

        /// <summary>
        /// 이 자리에 방어 모듈을 두면 범위 안에 들어올 기존 모듈 수 (배치 미리보기·봇용). 방어 모듈이 아니면 0.
        /// </summary>
        public static int CountCovered(StationGrid grid, ModuleData data, IReadOnlyList<Vector3Int> cells, List<ModuleInstance> results = null)
        {
            results?.Clear();
            if (data == null || !data.IsDefense)
                return 0;
            int radius = Math.Max(data.ShieldRadius, data.TurretRadius);
            int count = 0;
            foreach (var m in grid.Modules)
            {
                if (!InRange(cells, m.Cells, radius))
                    continue;
                count++;
                results?.Add(m);
            }
            return count;
        }

        /// <summary>두 셀 집합 사이의 최소 체비셰프 거리가 radius 이하인지.</summary>
        public static bool InRange(IReadOnlyList<Vector3Int> a, IReadOnlyList<Vector3Int> b, int radius)
        {
            for (int i = 0; i < a.Count; i++)
            {
                for (int j = 0; j < b.Count; j++)
                {
                    var d = a[i] - b[j];
                    if (Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z))) <= radius)
                        return true;
                }
            }
            return false;
        }
    }
}
