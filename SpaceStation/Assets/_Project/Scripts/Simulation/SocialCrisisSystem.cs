using System;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-3c 국가급 핵심 규칙 — 사회 위기 선택지 그레이박스 (사용자 결정 2026-10-13: 선택지 틀 + 시험 사건 1개 '파업').
    /// 국가급부터, 만족도가 기준(50 + 정책) 아래로 30초 이어지면 경고(90초 × 정책) → 그동안 선택지 4개 중 하나:
    /// 무마(자원 → 해소 + 만족도) · 강경 대응(즉시 해소, 만족도↓ · 주민 이탈) · 일시 정책 '임금 인상'(3분 만족도 상한↑ · 생산↓) · 방치(파업 = 1분 생산↓).
    /// 경고 중 만족도가 기준 + 10 이상으로 오르면 저절로 풀림. 시간이 다 되면 방치와 같음. 끝나면 5분 동안 다시 안 일어남.
    /// 실제 사회 위기(비리 · 파업 · 역병 · 쿠데타)의 요구 · 수치는 12-6.
    /// </summary>
    public sealed class SocialCrisisSystem : StationSystem
    {
        public const string SystemKey = "socialCrisis";
        public const string StrikeEffect = "파업";
        public const string WageEffect = "임금 인상";

        public enum CrisisPhase
        {
            Calm,
            Warning,
            Striking,
        }

        public enum Choice
        {
            Bribe,
            Crackdown,
            Wage,
            Ignore,
        }

        private readonly PolicyConfig _config;
        private float _brew;
        private float _timer;
        private float _cooldown;

        public event Action Changed;

        public SocialCrisisSystem(PolicyConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override string Key => SystemKey;
        public PolicyConfig Config => _config;
        public CrisisPhase Phase { get; private set; }
        /// <summary>경고: 파업까지 / 파업: 끝날 때까지.</summary>
        public float TimeLeft => _timer;
        /// <summary>이번 경고의 전체 길이 (막대 표시용).</summary>
        public float WarningLength { get; private set; }
        /// <summary>지금 파업이 시작되는 만족도 기준 (정책 반영).</summary>
        public float Threshold => _config.StrikeThreshold + Sim.Effects.Policy(PolicyStat.CrisisThreshold) + (ThresholdModifier != null ? ThresholdModifier() : 0f);

        /// <summary>13-2 공통 고리: 파업 기준 만족도에 더하는 값 (예: 타이쿤판 밀집도). null이면 0.</summary>
        public Func<float> ThresholdModifier { get; set; }

        private PolicySystem Policies => Sim.GetSystem<PolicySystem>();

        public override void Tick(float dt)
        {
            if (dt <= 0f || Sim.Session.IsGameOver)
                return;
            float satisfaction = Sim.Population.Satisfaction;
            switch (Phase)
            {
                case CrisisPhase.Calm:
                    if (_cooldown > 0f)
                        _cooldown = Math.Max(0f, _cooldown - dt);
                    if (_cooldown > 0f || Sim.Events.Held || !Sim.RuleOpen(StationRule.SocialCrisis, Sim.Progression.GradeIndex >= _config.CrisisMinGrade) || satisfaction >= Threshold)
                    {
                        _brew = 0f;
                        return;
                    }
                    _brew += dt;
                    if (_brew >= _config.StrikeBrewSeconds)
                        BeginWarning();
                    break;
                case CrisisPhase.Warning:
                    _timer -= dt;
                    if (satisfaction >= Threshold + _config.StrikeRecoverMargin)
                    {
                        Sim.Report("만족도가 올라 파업 움직임이 가라앉았습니다", true);
                        Finish();
                    }
                    else if (_timer <= 0f)
                        StartStrike("경고 시간 안에 대응하지 않아 ");
                    else
                        Changed?.Invoke();
                    break;
                case CrisisPhase.Striking:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        Sim.Report("파업이 끝났습니다", true);
                        Finish();
                    }
                    else
                        Changed?.Invoke();
                    break;
            }
        }

        /// <summary>경고 시작 (확인용 에디터 메뉴도 씀).</summary>
        public void BeginWarning()
        {
            Phase = CrisisPhase.Warning;
            WarningLength = Math.Max(1f, _config.StrikeWarningSeconds * Sim.Effects.Policy(PolicyStat.CrisisWarning));
            _timer = WarningLength;
            _brew = 0f;
            Sim.Report($"파업 조짐: 만족도 {Threshold:0} 미만이 이어져 노동자들이 파업을 예고했습니다 · {_timer:0}초 안에 대응", false);
            Changed?.Invoke();
        }

        public bool CanChoose(Choice choice)
        {
            if (Phase != CrisisPhase.Warning)
                return false;
            return choice != Choice.Bribe || Sim.Resources.CanAfford(_config.BribeCost);
        }

        public bool Choose(Choice choice)
        {
            if (!CanChoose(choice))
                return false;
            switch (choice)
            {
                case Choice.Bribe:
                    Sim.Resources.TrySpend(_config.BribeCost);
                    Sim.Population.AdjustSatisfaction(_config.BribeSatisfaction);
                    Sim.Report($"무마: 보상을 지급해 파업을 막았습니다 (만족도 +{_config.BribeSatisfaction:0})", true);
                    Finish();
                    break;
                case Choice.Crackdown:
                    Sim.Population.AdjustSatisfaction(_config.CrackdownSatisfaction);
                    Sim.Population.Remove(_config.CrackdownLeave, PopulationChangeReason.Crisis);
                    Sim.Report($"강경 대응: 파업을 막았지만 만족도 {_config.CrackdownSatisfaction:0} · 주민 {_config.CrackdownLeave}명이 떠났습니다", false);
                    Finish();
                    break;
                case Choice.Wage:
                    Policies?.AddTemporary(WageEffect, _config.WageSeconds,
                        new PolicyModifier { Stat = PolicyStat.SatisfactionCap, Value = _config.WageSatisfactionCap },
                        new PolicyModifier { Stat = PolicyStat.Production, Value = _config.WageProduction });
                    Sim.Report($"일시 정책 '임금 인상': {_config.WageSeconds:0}초 동안 만족도 상한 +{_config.WageSatisfactionCap:0} · 생산 -{(1f - _config.WageProduction) * 100f:0}%", true);
                    Finish();
                    break;
                case Choice.Ignore:
                    StartStrike("");
                    break;
            }
            return true;
        }

        private void StartStrike(string cause)
        {
            Phase = CrisisPhase.Striking;
            _timer = _config.StrikeDuration;
            Policies?.AddTemporary(StrikeEffect, _config.StrikeDuration,
                new PolicyModifier { Stat = PolicyStat.Production, Value = _config.StrikeProduction });
            Sim.Report($"{cause}파업 발생: {_config.StrikeDuration:0}초 동안 생산 -{(1f - _config.StrikeProduction) * 100f:0}%", false);
            Changed?.Invoke();
        }

        private void Finish()
        {
            Phase = CrisisPhase.Calm;
            _timer = 0f;
            _brew = 0f;
            _cooldown = _config.CrisisCooldown;
            Changed?.Invoke();
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public int Phase;
            public float Brew;
            public float Timer;
            public float Cooldown;
            public float WarningLength;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            if (Phase == CrisisPhase.Calm && _brew <= 0f && _cooldown <= 0f)
                return null;
            return JsonUtility.ToJson(new State { Phase = (int)Phase, Brew = _brew, Timer = _timer, Cooldown = _cooldown, WarningLength = WarningLength });
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            Phase = (CrisisPhase)Mathf.Clamp(s.Phase, 0, 2);
            _brew = s.Brew;
            _timer = s.Timer;
            _cooldown = s.Cooldown;
            WarningLength = s.WarningLength;
        }
    }
}
