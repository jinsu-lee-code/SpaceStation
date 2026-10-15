using System;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-3 타이쿤판 사회 사건 '역병' (사용자 결정 2026-10-15, BALANCE 43번).
    /// - 발생: 도시급 + '사회 사건' 규칙이 열린 뒤, 굴림 간격마다 (기본 확률 + 부족분 × 부족 확률) — 부족분 = 1 − 검역 · 의료 중 낮은 충족률.
    ///   이민선이 주민을 내릴 때도 한 번 더 굴림 (외부에서 들어옴). 끝난 뒤 쿨다운.
    /// - 경고(90초) 중 선택지: 방역 물자(식량 · 물 → 해소) · 격리 봉쇄(즉시 해소, 만족도↓ · 주민 이탈) · 위생 캠페인(일시 정책, 생산↓ · 역병 막음) · 방치.
    /// - 역병(90초): 15초마다 인구 2%(최소 1명, 검역 충족률만큼 최대 절반 감소) 사망 + 생산 −20%.
    /// 파업(SocialCrisisSystem, 공통 바닥)과 따로 도는 타이쿤판 시스템.
    /// </summary>
    public sealed class PlagueSystem : StationSystem
    {
        public const string SystemKey = "plague";
        public const string OutbreakEffect = "역병";
        public const string CampaignEffect = "위생 캠페인";

        public enum PlaguePhase
        {
            Calm,
            Warning,
            Outbreak,
        }

        public enum Choice
        {
            Supplies,
            Lockdown,
            Campaign,
            Ignore,
        }

        private readonly PlagueConfig _config;
        private readonly Func<float> _random01;
        private float _roll;
        private float _timer;
        private float _cooldown;
        private float _lossTimer;

        public event Action Changed;

        public PlagueSystem(PlagueConfig config, Func<float> random01)
        {
            _config = config ? config : throw new ArgumentNullException(nameof(config));
            var fallback = new System.Random();
            _random01 = random01 ?? (() => (float)fallback.NextDouble());
        }

        /// <summary>시뮬레이션에 붙임 (세이브 복원 전에).</summary>
        public static PlagueSystem AttachTo(StationSimulation sim, PlagueConfig config, Func<float> random01)
            => sim.AddSystem(new PlagueSystem(config, random01), TickStepNames.Population);

        public override string Key => SystemKey;
        public PlagueConfig Config => _config;
        public PlaguePhase Phase { get; private set; }
        /// <summary>경고: 역병까지 / 역병: 끝날 때까지.</summary>
        public float TimeLeft => _timer;
        /// <summary>이번 사건에서 잃은 주민 수.</summary>
        public int Lost { get; private set; }

        /// <summary>지금 역병이 생길 수 있는지 (등급 + 사회 사건 규칙).</summary>
        public bool Open => Sim.RuleOpen(StationRule.SocialCrisis, true) && Sim.Progression.GradeIndex >= _config.MinGrade;

        /// <summary>부족분 = 1 − 검역 · 의료 중 낮은 충족률 (요구가 아직 없으면 그 요구는 충분으로 봄).</summary>
        public float Shortage
        {
            get
            {
                float worst = 1f;
                foreach (var s in Sim.Needs.Statuses)
                    if (s.Need == ResidentNeed.Quarantine || s.Need == ResidentNeed.Medical)
                        worst = Math.Min(worst, s.Ratio);
                return 1f - worst;
            }
        }

        /// <summary>검역 충족률 (요구가 없으면 0).</summary>
        public float QuarantineRatio
        {
            get
            {
                foreach (var s in Sim.Needs.Statuses)
                    if (s.Need == ResidentNeed.Quarantine)
                        return s.Ratio;
                return 0f;
            }
        }

        /// <summary>굴림 한 번의 발생 확률.</summary>
        public float Chance => Mathf.Clamp01(_config.BaseChance + Shortage * _config.ShortageChance);

        private PolicySystem Policies => Sim.GetSystem<PolicySystem>();

        protected override void OnAttached()
        {
            Sim.Population.PopulationChanged += HandlePopulationChanged;
        }

        private void HandlePopulationChanged(int delta, PopulationChangeReason reason)
        {
            if (reason != PopulationChangeReason.Immigration || delta <= 0 || Phase != PlaguePhase.Calm || _cooldown > 0f || !Open)
                return;
            if (_random01() < Mathf.Clamp01(_config.ImmigrationChance + Shortage * _config.ShortageChance))
                BeginWarning("이민선과 함께 ");
        }

        public override void Tick(float dt)
        {
            if (dt <= 0f || Sim.Session.IsGameOver)
                return;
            switch (Phase)
            {
                case PlaguePhase.Calm:
                    if (_cooldown > 0f)
                        _cooldown = Math.Max(0f, _cooldown - dt);
                    if (_cooldown > 0f || Sim.Events.Held || !Open)
                    {
                        _roll = 0f;
                        return;
                    }
                    _roll += dt;
                    if (_roll < _config.RollInterval)
                        return;
                    _roll = 0f;
                    if (_random01() < Chance)
                        BeginWarning("");
                    break;
                case PlaguePhase.Warning:
                    _timer -= dt;
                    if (_timer <= 0f)
                        StartOutbreak("경고 시간 안에 대응하지 않아 ");
                    else
                        Changed?.Invoke();
                    break;
                case PlaguePhase.Outbreak:
                    _timer -= dt;
                    _lossTimer += dt;
                    while (_lossTimer >= _config.LossInterval && _timer > -_config.LossInterval)
                    {
                        _lossTimer -= _config.LossInterval;
                        LoseResidents();
                    }
                    if (_timer <= 0f)
                    {
                        Sim.Report($"역병이 잦아들었습니다 · 주민 {Lost}명을 잃었습니다", true);
                        Finish();
                    }
                    else
                        Changed?.Invoke();
                    break;
            }
        }

        /// <summary>경고 시작 (확인용 메뉴도 씀).</summary>
        public void BeginWarning(string cause)
        {
            Phase = PlaguePhase.Warning;
            _timer = _config.WarningSeconds;
            _roll = 0f;
            Lost = 0;
            Sim.Report($"{cause}역병 징후: 감염자가 나타났습니다 · {_timer:0}초 안에 대응 (검역 · 의료가 부족할수록 자주 일어남)", false);
            Changed?.Invoke();
        }

        public bool CanChoose(Choice choice)
        {
            if (Phase != PlaguePhase.Warning)
                return false;
            return choice != Choice.Supplies || Sim.Resources.CanAfford(_config.SuppliesCost);
        }

        public bool Choose(Choice choice)
        {
            if (!CanChoose(choice))
                return false;
            switch (choice)
            {
                case Choice.Supplies:
                    Sim.Resources.TrySpend(_config.SuppliesCost);
                    Sim.Report("방역 물자: 의약품과 깨끗한 물을 풀어 역병을 막았습니다", true);
                    Finish();
                    break;
                case Choice.Lockdown:
                    Sim.Population.AdjustSatisfaction(_config.LockdownSatisfaction);
                    if (_config.LockdownLeave > 0)
                        Sim.Population.Remove(_config.LockdownLeave, PopulationChangeReason.Crisis);
                    Sim.Report($"격리 봉쇄: 역병을 막았지만 만족도 {_config.LockdownSatisfaction:0} · 주민 {_config.LockdownLeave}명이 떠났습니다", false);
                    Finish();
                    break;
                case Choice.Campaign:
                    Policies?.AddTemporary(CampaignEffect, _config.CampaignSeconds,
                        new PolicyModifier { Stat = PolicyStat.Production, Value = _config.CampaignProduction });
                    Sim.Report($"일시 정책 '위생 캠페인': 역병을 막았습니다 · {_config.CampaignSeconds:0}초 동안 생산 -{(1f - _config.CampaignProduction) * 100f:0}%", true);
                    Finish();
                    break;
                case Choice.Ignore:
                    StartOutbreak("");
                    break;
            }
            return true;
        }

        private void StartOutbreak(string cause)
        {
            Phase = PlaguePhase.Outbreak;
            _timer = _config.OutbreakSeconds;
            _lossTimer = 0f;
            Policies?.AddTemporary(OutbreakEffect, _config.OutbreakSeconds,
                new PolicyModifier { Stat = PolicyStat.Production, Value = _config.OutbreakProduction });
            Sim.Report($"{cause}역병 발생: {_config.OutbreakSeconds:0}초 동안 주민이 줄고 생산 -{(1f - _config.OutbreakProduction) * 100f:0}% (검역소 범위의 주민은 피해가 적음)", false);
            Changed?.Invoke();
        }

        /// <summary>한 번에 잃는 주민 수 (인구 × 비율 × (1 − 검역 충족률 × 보호율), 최소 1).</summary>
        public int LossPerStep()
        {
            int population = Sim.Resources.Population;
            if (population <= 0)
                return 0;
            float ratio = _config.LossRatio * (1f - QuarantineRatio * _config.QuarantineProtection);
            return Math.Max(1, Mathf.RoundToInt(population * ratio));
        }

        private void LoseResidents()
        {
            int n = Math.Min(LossPerStep(), Math.Max(0, Sim.Resources.Population - 1));
            if (n <= 0)
                return;
            Sim.Population.Remove(n, PopulationChangeReason.Crisis);
            Lost += n;
        }

        private void Finish()
        {
            Phase = PlaguePhase.Calm;
            _timer = 0f;
            _roll = 0f;
            _lossTimer = 0f;
            _cooldown = _config.Cooldown;
            Changed?.Invoke();
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public int Phase;
            public float Roll;
            public float Timer;
            public float Cooldown;
            public float LossTimer;
            public int Lost;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            if (Phase == PlaguePhase.Calm && _roll <= 0f && _cooldown <= 0f)
                return null;
            return JsonUtility.ToJson(new State { Phase = (int)Phase, Roll = _roll, Timer = _timer, Cooldown = _cooldown, LossTimer = _lossTimer, Lost = Lost });
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            Phase = (PlaguePhase)Mathf.Clamp(s.Phase, 0, 2);
            _roll = s.Roll;
            _timer = s.Timer;
            _cooldown = s.Cooldown;
            _lossTimer = s.LossTimer;
            Lost = s.Lost;
        }
    }
}
