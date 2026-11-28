using System;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// CONNECT_1121 7번 ④ 타이쿤판 지구급 상징 모듈 · 엔드리스 시작 (사용자 결정 2026-11-28, 추천안 — 값 = <see cref="SymbolConfig"/>).
    /// - 가동 중(연결 · 파손 아님 · 합성 중 아님)인 상징 모듈이 하나라도 있으면: 정거장 전체 생산 +10% (<see cref="StationSimulation.ModuleProductionScale"/>) ·
    ///   만족도 상한 +5 (<see cref="StationSimulation.SatisfactionCapBonus"/>) · 상인 · 학술 평판 +1/분. 여러 개 지어도 겹치지 않음.
    /// - 처음 가동되면 엔드리스 시작 → 10분마다 단계 +1: 방문 간격 −5%/단계 (바닥 50%, <see cref="ContactSystem.IntervalScale"/>) · 무법 선단 경계 습격 확률 +3%/단계 (최대 +30%, <see cref="ContactSystem.RaidChanceBonus"/>).
    ///   엔드리스는 상징 모듈이 부서져도 이어짐 (보너스 · 평판만 멈춤).
    /// </summary>
    public sealed class SymbolSystem : StationSystem
    {
        public const string SystemKey = "symbol";

        private readonly SymbolConfig _config;
        private float _endlessSeconds;

        public event Action Changed;

        public SymbolSystem(SymbolConfig config) => _config = config ? config : throw new ArgumentNullException(nameof(config));

        /// <summary>시뮬레이션에 붙임 (세이브 복원 전에, 공급 사슬 뒤) — 생산 · 만족도 상한 · 정보 창 · 외부 접촉 고리를 감쌈.</summary>
        public static SymbolSystem AttachTo(StationSimulation sim, SymbolConfig config)
        {
            if (sim == null || config == null || config.Symbol == null)
                return null;
            var system = sim.AddSystem(new SymbolSystem(config), TickStepNames.Progression);
            var production = sim.ModuleProductionScale;
            sim.ModuleProductionScale = m => (production != null ? production(m) : 1f) * system.ProductionMultiplier;
            var cap = sim.SatisfactionCapBonus;
            sim.SatisfactionCapBonus = () => (cap != null ? cap() : 0f) + system.SatisfactionBonus;
            var info = sim.ModuleInfoText;
            sim.ModuleInfoText = m =>
            {
                string a = info?.Invoke(m), b = system.Info(m);
                return string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : a + "\n" + b;
            };
            if (sim.Contact != null)
            {
                var interval = sim.Contact.IntervalScale;
                sim.Contact.IntervalScale = () => (interval != null ? interval() : 1f) * system.IntervalScale;
                var raid = sim.Contact.RaidChanceBonus;
                sim.Contact.RaidChanceBonus = () => (raid != null ? raid() : 0f) + system.RaidBonus;
            }
            return system;
        }

        public override string Key => SystemKey;
        public SymbolConfig Config => _config;

        /// <summary>상징 모듈이 처음 가동된 뒤 (엔드리스).</summary>
        public bool Started { get; private set; }
        /// <summary>지금 가동 중인 상징 모듈이 있는지 (지난 틱).</summary>
        public bool Active { get; private set; }
        /// <summary>엔드리스가 이어진 시간 (초).</summary>
        public float EndlessSeconds => _endlessSeconds;
        /// <summary>엔드리스 단계 (시작 전 0, 완성 = 1).</summary>
        public int Level => Started ? 1 + (int)Math.Floor(_endlessSeconds / (_config.LevelMinutes * 60f)) : 0;

        public float ProductionMultiplier => Active ? 1f + _config.ProductionBonus : 1f;
        public float SatisfactionBonus => Active ? _config.SatisfactionBonus : 0f;
        public float IntervalScale => Started ? Math.Max(_config.MinIntervalScale, 1f - _config.IntervalStep * Level) : 1f;
        public float RaidBonus => Started ? Math.Min(_config.MaxRaidBonus, _config.RaidStep * Level) : 0f;

        private bool Running(ModuleInstance m)
            => m.Data == _config.Symbol && Sim.Connectivity.IsActive(m) && !Sim.Damage.IsDamaged(m) && !Sim.IsHalted(m);

        public override void Tick(float dt)
        {
            if (dt <= 0f || Sim.Session.IsGameOver)
                return;
            bool active = false;
            foreach (var m in Sim.Grid.Modules)
            {
                if (Running(m))
                {
                    active = true;
                    break;
                }
            }
            int before = Level;
            if (active && !Started)
            {
                Started = true;
                Sim.Report($"상징 모듈 '{_config.Symbol.DisplayName}' 완성 · 엔드리스 시작: 정거장 생산 +{_config.ProductionBonus * 100f:0}% · 만족도 상한 +{_config.SatisfactionBonus:0}, {_config.LevelMinutes:0}분마다 방문이 잦아지고 습격이 커집니다", true);
            }
            if (active != Active)
            {
                Active = active;
                if (Started && !active)
                    Sim.Report($"상징 모듈이 멈췄습니다 · 생산 · 만족도 보너스와 명성이 멈춤 (엔드리스는 이어짐)", false);
                Changed?.Invoke();
            }
            if (Started)
                _endlessSeconds += dt;
            if (active && Sim.Contact != null && _config.FameReputationPerMinute > 0f)
            {
                float rep = _config.FameReputationPerMinute * dt / 60f;
                Sim.Contact.AdjustReputation(ContactFaction.Merchant, rep);
                Sim.Contact.AdjustReputation(ContactFaction.Scholar, rep);
            }
            if (Level != before && before > 0)
            {
                Sim.Report($"엔드리스 {Level}단계: 방문 간격 {IntervalScale * 100f:0}% · 무법 선단 습격 +{RaidBonus * 100f:0}%", false);
                Changed?.Invoke();
            }
        }

        /// <summary>정보 창 두 줄 (한 줄 24자 안쪽).</summary>
        public string Info(ModuleInstance module)
        {
            if (module == null || module.Data != _config.Symbol)
                return null;
            string line1 = $"상징 · 생산 +{_config.ProductionBonus * 100f:0}% · 상한 +{_config.SatisfactionBonus:0}";
            if (!Started)
                return line1 + "\n가동되면 엔드리스 시작";
            return line1 + $"\n엔드리스 {Level}단계 · 방문 {IntervalScale * 100f:0}%";
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public bool Started;
            public float Seconds;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
            => Started ? JsonUtility.ToJson(new State { Started = true, Seconds = _endlessSeconds }) : null;

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            Started = s.Started;
            _endlessSeconds = s.Seconds;
        }
    }
}
