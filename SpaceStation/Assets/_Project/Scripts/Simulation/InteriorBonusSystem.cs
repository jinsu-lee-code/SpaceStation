using System;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-6 타이쿤판 1인칭 가볍게 (TYCOON.md Q3 '선택형 보너스', BALANCE 46번). 공통 고리(M-7)에 값을 정하고 등급 보상 연구 포인트를 준다.
    /// - 놓친 내부 사고 손실 배율(<see cref="IncidentEffects.MissScale"/>) · 보급 상자 자동 회수(<see cref="SupplyCrateSystem.AutoCollectRate"/>)
    /// - 등급에 처음 오를 때마다 연구 포인트 (등급이 내려갔다 다시 올라도 한 번만, 저장됨)
    /// </summary>
    public sealed class InteriorBonusSystem : StationSystem
    {
        public const string SystemKey = "interior-bonus";

        private readonly InteriorBonusConfig _config;

        public InteriorBonusSystem(InteriorBonusConfig config)
        {
            _config = config ? config : throw new ArgumentNullException(nameof(config));
        }

        /// <summary>시뮬레이션에 붙이고 두 고리 값을 정함. 세이브 복원 전에 부른다.</summary>
        public static InteriorBonusSystem AttachTo(StationSimulation sim, InteriorBonusConfig config)
        {
            var system = sim.AddSystem(new InteriorBonusSystem(config), TickStepNames.Progression);
            sim.Incidents.MissScale = config.IncidentMissScale;
            sim.Supply.AutoCollectRate = config.CrateAutoCollectRate;
            return system;
        }

        public override string Key => SystemKey;
        public InteriorBonusConfig Config => _config;
        /// <summary>등급 보상을 받은 가장 높은 등급.</summary>
        public int RewardedGrade { get; private set; }

        public override void Tick(float dt)
        {
            int highest = Sim.Progression.HighestGradeIndex;
            if (highest <= RewardedGrade)
                return;
            int points = 0;
            for (int g = RewardedGrade + 1; g <= highest; g++)
                points += _config.GradeUpPoints(g);
            RewardedGrade = highest;
            if (points <= 0)
                return;
            var supply = Sim.Supply;
            supply.SetCounts(supply.ResearchPoints + points, supply.FreeRepairs);
            Sim.Report($"등급 보상: 연구 포인트 +{points} (보유 {supply.ResearchPoints} · 레시피 해금 · 연구 할인)", true);
        }

        [Serializable]
        private sealed class State
        {
            public int Rewarded;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf) => JsonUtility.ToJson(new State { Rewarded = RewardedGrade });

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s != null)
                RewardedGrade = Math.Max(0, s.Rewarded);
        }
    }
}
