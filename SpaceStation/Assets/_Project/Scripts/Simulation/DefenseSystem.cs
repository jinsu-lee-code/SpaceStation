using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>8-3 손상 통제 효과 (파손 모듈의 확산·파괴 시간 배율, 1 = 효과 없음).</summary>
    public struct DamageControlEffect
    {
        public float SpreadMultiplier;
        public float DestroyMultiplier;
        public static DamageControlEffect None => new DamageControlEffect { SpreadMultiplier = 1f, DestroyMultiplier = 1f };
    }

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
            Effects = new ResearchEffects(config);
        }

        /// <summary>Phase 6 연구 효과 (반경 +칸, 격추 배율·상한). StationSimulation이 공유 인스턴스로 바꿔 끼운다.</summary>
        public ResearchEffects Effects { get; set; }

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
                if (data == null || !data.IsShield || !InRange(d.Cells, target.Cells, Effects.ShieldRadius(data)))
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
        public bool IsInShieldRange(ModuleInstance shield, ModuleInstance module)
            => shield?.Data != null && InRange(shield.Cells, module.Cells, Effects.ShieldRadius(shield.Data));

        /// <summary>이 모듈로 오는 운석 1발의 격추 확률.</summary>
        public float GetInterceptChance(StationGrid grid, ModuleInstance target)
        {
            float sum = 0f;
            foreach (var d in grid.Modules)
            {
                var data = d.Data;
                if (data == null || !data.IsTurret || !InRange(d.Cells, target.Cells, Effects.TurretRadius(data)))
                    continue;
                sum += data.TurretInterceptChance * Effects.TurretInterceptMultiplier * GetStrength(d);
            }
            return Mathf.Min(sum, Effects.TurretMaxIntercept);
        }

        /// <summary>미리보기 반경 (연구 반영). 방어 모듈이 아니면 0.</summary>
        public int RangeOf(ModuleData data) => Math.Max(Effects.ControlRadius(data), Math.Max(Effects.ShieldRadius(data), Effects.TurretRadius(data)));

        /// <summary>
        /// 8-3 손상 통제: 이 모듈이 파손됐을 때 적용될 (확산 시간 배율, 파괴 시간 배율). 범위 안 통제실 중 가장 강한 것 하나,
        /// 배율 = 1 + (배율 − 1) × 가동률. 통제실이 없으면 (1, 1).
        /// </summary>
        public DamageControlEffect GetDamageControl(StationGrid grid, ModuleInstance target)
        {
            var best = DamageControlEffect.None;
            foreach (var d in grid.Modules)
            {
                var data = d.Data;
                if (data == null || !data.IsDamageControl || !InRange(d.Cells, target.Cells, Effects.ControlRadius(data)))
                    continue;
                float s = GetStrength(d);
                if (s <= 0f)
                    continue;
                float spread = 1f + (data.ControlSpreadMultiplier - 1f) * s;
                float destroy = 1f + (data.ControlDestroyMultiplier - 1f) * s;
                if (spread > best.SpreadMultiplier) best.SpreadMultiplier = spread;
                if (destroy > best.DestroyMultiplier) best.DestroyMultiplier = destroy;
            }
            return best;
        }

        /// <summary>연구 반경을 반영한 CountCovered (배치 미리보기·선택 패널).</summary>
        public int CountCoveredWithResearch(StationGrid grid, ModuleData data, IReadOnlyList<Vector3Int> cells)
            => CountCovered(grid, data, cells, null, Effects.DefenseRadiusBonus);

        /// <summary>
        /// 이 자리에 방어 모듈을 두면 범위 안에 들어올 기존 모듈 수 (배치 미리보기·봇용). 방어 모듈이 아니면 0.
        /// </summary>
        public static int CountCovered(StationGrid grid, ModuleData data, IReadOnlyList<Vector3Int> cells, List<ModuleInstance> results = null, int radiusBonus = 0)
        {
            results?.Clear();
            if (data == null || !data.IsDefense)
                return 0;
            int radius = Math.Max(data.ControlRadius, Math.Max(data.ShieldRadius, data.TurretRadius)) + radiusBonus;
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
