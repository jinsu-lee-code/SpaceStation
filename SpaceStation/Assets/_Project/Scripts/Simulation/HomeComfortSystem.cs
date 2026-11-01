using System;
using System.Collections.Generic;
using System.Text;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 14-2b 서사판 집 환경 (REDESIGN S2, 사용자 결정 2026-11-01): 집의 열 · 방사선이 종족 견딤을 넘으면 힘든 주민 (main M-120 고리 <see cref="ResidentRoster.HomeStress"/>).
    /// 한도 = 보통 주민 한도 × (1 + 견딤) — 약함(−0.5) 절반 · 강함(+0.5) 1.5배. 힘든 정도 = 넘은 만큼의 합.
    /// 효과(고리가 처리): 자동 배정이 피함 · 덜 힘든 빈집으로 옮김 · 이탈 때 먼저 떠남 · 힘든 주민 비율만큼 만족도 상한 감소. 폭풍 동안은 대피 정원만큼 세지 않음.
    /// 열 · 방사선 규칙이 열리기 전에는 모두 0 (5장 전 결과 그대로).
    /// </summary>
    public sealed class HomeComfortSystem : StationSystem
    {
        public const string SystemKey = "homecomfort";
        /// <summary>장 표시: 힘든 주민 0명으로 버팀.</summary>
        public const string CalmFlag = "comfort.calm";

        private float _calm; // 저장하지 않음 — 불러오면 다시 셈
        private readonly SocietyConfig _config;
        private readonly Dictionary<string, int> _stressedKinds = new Dictionary<string, int>();
        private readonly StringBuilder _sb = new StringBuilder(64);
        private ArkShipSystem _ships;

        public HomeComfortSystem(SocietyConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override string Key => SystemKey;
        public bool Active => Sim.Radiation.Active || Sim.Heat.Active;
        protected override void OnAttached()
        {
            if (Sim.Residents != null)
                Sim.Residents.HomeStress = (r, home) => StressOf(r.Kind, home);
        }

        /// <summary>이 종족이 이 집에서 힘든 정도 (0 = 괜찮음).</summary>
        public float StressOf(string kind, ModuleInstance home)
        {
            if (home == null || !Active)
                return 0f;
            _ships ??= Sim.GetSystem<ArkShipSystem>(); // 주민 × 집마다 불림
            var species = _ships?.Find(kind);
            float heatLimit = Sim.Balance.HomeHeatComfort * (1f + (species != null ? species.HeatTolerance : 0f));
            float radiationLimit = Sim.Balance.RadiationComfort * (1f + (species != null ? species.RadiationTolerance : 0f));
            return Mathf.Max(0f, Sim.Heat.HeatOf(home) - heatLimit) + Mathf.Max(0f, Sim.Radiation.LevelOf(home) - radiationLimit);
        }

        public override void Tick(float dt)
        {
            var roster = Sim.Residents;
            if (roster == null)
                return;
            roster.StressMoodMax = _config.StressMoodMax;
            roster.StressMoodPerResident = _config.StressMoodMax / Math.Max(1, roster.Residents.Count); // 비율 — 인구가 늘어도 같은 비율이면 같은 감소
            roster.StressExempt = Sim.Radiation.StormActive ? Sim.Radiation.ShelterCapacity : 0;
            UpdateCalm(dt, roster);
        }

        /// <summary>힘든 주민 0명이 이어지면 장 표시 (5장 선택 목표 "힘든 주민 0명으로 1분 버티기").</summary>
        private void UpdateCalm(float dt, ResidentRoster roster)
        {
            if (!Active || roster.StressedCount > 0 || roster.Residents.Count == 0)
            {
                _calm = 0f;
                return;
            }
            _calm += dt;
            if (_calm >= _config.CalmSeconds && Sim.Chapters != null)
                Sim.Chapters.SetFlag(CalmFlag);
        }

        /// <summary>장 카드 줄: "힘든 주민 12명 (펭귄 8 · 토끼 4)", 폭풍 때 대피한 만큼은 "힘든 주민 8명 · 대피 2명". 없으면 "".</summary>
        public string Summary()
        {
            var roster = Sim.Residents;
            if (roster == null || !Active || roster.StressedCount <= 0)
                return "";
            _stressedKinds.Clear();
            int total = 0;
            foreach (var r in roster.Residents)
            {
                if (roster.StressAt(r, r.Home) <= 0f)
                    continue;
                total++;
                _stressedKinds.TryGetValue(r.Kind ?? "", out int n);
                _stressedKinds[r.Kind ?? ""] = n + 1;
            }
            _sb.Clear();
            _sb.Append("힘든 주민 ").Append(roster.StressedCount).Append('명');
            if (total > roster.StressedCount)
                return _sb.Append(" · 대피 ").Append(total - roster.StressedCount).Append('명').ToString();
            var ships = _ships ?? Sim.GetSystem<ArkShipSystem>();
            string sep = " (";
            foreach (var kv in _stressedKinds)
            {
                _sb.Append(sep).Append(ships != null ? ships.SpeciesName(kv.Key) : kv.Key).Append(' ').Append(kv.Value);
                sep = " · ";
            }
            return _sb.Append(')').ToString();
        }
    }
}
