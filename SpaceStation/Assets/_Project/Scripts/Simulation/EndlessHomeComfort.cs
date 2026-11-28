using System;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// CONNECT_1121 7번 ① 엔드리스 집 환경 (사용자 결정 2026-11-28 — 방사선 · "보통 한도 4 + 특성"): main M-120 고리 <see cref="ResidentRoster.HomeStress"/>를 붙인다.
    /// 서사판 <c>HomeComfortSystem</c>과 같은 틀 — 한도 = 보통 한도(<see cref="BalanceConfig.RadiationComfort"/>) × (1 + 견딤),
    /// 견딤 = 방사선 민감 <see cref="HeatPowerConfig.SensitiveTolerance"/> · 내성 <see cref="HeatPowerConfig.TolerantTolerance"/> · 그 밖 0.
    /// 힘든 정도 = 집 방사선이 한도를 넘은 만큼 (집 열은 넣지 않음 — 엔드리스 열은 온기 · 과열로 따로).
    /// 효과(고리가 처리): 자동 배정이 피함 · 덜 힘든 빈집으로 옮김 · 이탈 때 먼저 떠남 · 힘든 주민 비율만큼 만족도 상한 감소 (최대 <see cref="HeatPowerConfig.StressMoodMax"/>).
    /// 방사선 규칙이 열리기 전에는 모두 0.
    /// </summary>
    public sealed class EndlessHomeComfort : StationSystem
    {
        public const string SystemKey = "endlesscomfort";
        private readonly HeatPowerConfig _config;

        private EndlessHomeComfort(HeatPowerConfig config) => _config = config;

        public override string Key => SystemKey;

        /// <summary>시뮬레이션에 붙임 (설정 · 주민 명단 · 방사선이 없으면 아무것도 안 함).</summary>
        public static EndlessHomeComfort AttachTo(StationSimulation sim, HeatPowerConfig config)
        {
            if (sim == null || config == null || sim.Radiation == null || sim.Residents == null)
                return null;
            return sim.AddSystem(new EndlessHomeComfort(config), RadiationSystem.SystemKey);
        }

        protected override void OnAttached()
        {
            if (Sim.Residents != null)
                Sim.Residents.HomeStress = StressOf;
        }

        /// <summary>이 주민이 이 집에서 견디는 방사선 한도.</summary>
        public float LimitFor(Resident r)
            => Limit(Sim.Balance.RadiationComfort, _config, r != null && r.Has(ResidentTrait.RadiationSensitive), r != null && r.Has(ResidentTrait.RadiationTolerant));

        /// <summary>한도 = 보통 한도 × (1 + 견딤) — 민감 · 내성 견딤은 설정 값 (둘 다면 민감이 먼저).</summary>
        public static float Limit(float comfort, HeatPowerConfig config, bool sensitive, bool tolerant)
        {
            float tolerance = sensitive ? config.SensitiveTolerance : tolerant ? config.TolerantTolerance : 0f;
            return comfort * Math.Max(0f, 1f + tolerance);
        }

        /// <summary>힘든 정도 (0 = 괜찮음).</summary>
        public float StressOf(Resident r, ModuleInstance home)
        {
            if (home == null || Sim.Radiation == null || !Sim.Radiation.Active)
                return 0f;
            return Mathf.Max(0f, Sim.Radiation.LevelOf(home) - LimitFor(r));
        }

        public override void Tick(float dt)
        {
            var roster = Sim.Residents;
            if (roster == null)
                return;
            roster.StressMoodMax = _config.StressMoodMax;
            roster.StressMoodPerResident = _config.StressMoodMax / Math.Max(1, roster.Residents.Count); // 비율 — 인구가 늘어도 같은 비율이면 같은 감소
            roster.StressExempt = Sim.Radiation != null && Sim.Radiation.StormActive ? Sim.Radiation.ShelterCapacity : 0;
        }
    }
}
