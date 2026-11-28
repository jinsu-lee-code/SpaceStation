using System;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// CONNECT_1121 7번 ② 타이쿤판 국가급 사회 사건 '비리' (사용자 결정 2026-11-28, 추천안) — <see cref="PlagueSystem"/>과 같은 틀.
    /// - 발생: 국가급 + '사회 사건' 규칙이 열린 뒤, 굴림 간격마다 (기본 확률 + 행정 부족분 × 부족 확률). 끝난 뒤 쿨다운.
    /// - 경고(90초) 중 선택지: 감사(금속 → 해소) · 강경 숙청(즉시 해소, 만족도↓ · 주민 이탈) · 일시 정책 '투명 행정'(연구↓ · 해소) · 방치.
    /// - 횡령(90초): 금속 생산 절반이 새고, 시작할 때 저장 금속 일부를 잃음. 방치하면 다음 파업 기준 만족도 ↑ (다음 파업 사건이 끝나면 사라짐).
    /// - 파업 연결: 소비(상업 구역) 부족분만큼 파업 기준 만족도 ↑ — <see cref="SocialCrisisSystem.ThresholdModifier"/>에 더함 (성장 위협 밀집도 값은 그대로 감쌈).
    /// </summary>
    public sealed class CorruptionSystem : StationSystem
    {
        public const string SystemKey = "corruption";
        public const string EmbezzleEffect = "횡령";
        public const string TransparencyEffect = "투명 행정";

        public enum CorruptionPhase
        {
            Calm,
            Warning,
            Embezzling,
        }

        public enum Choice
        {
            Audit,
            Purge,
            Transparency,
            Ignore,
        }

        private readonly CorruptionConfig _config;
        private readonly Func<float> _random01;
        private float _roll;
        private float _timer;
        private float _cooldown;
        private bool _strikePending;
        private bool _strikeSeen;

        public event Action Changed;

        public CorruptionSystem(CorruptionConfig config, Func<float> random01)
        {
            _config = config ? config : throw new ArgumentNullException(nameof(config));
            var fallback = new System.Random();
            _random01 = random01 ?? (() => (float)fallback.NextDouble());
        }

        /// <summary>시뮬레이션에 붙임 (세이브 복원 전에) — 파업 기준 고리도 감쌈.</summary>
        public static CorruptionSystem AttachTo(StationSimulation sim, CorruptionConfig config, Func<float> random01)
        {
            if (sim == null || config == null)
                return null;
            var system = sim.AddSystem(new CorruptionSystem(config, random01), TickStepNames.Population);
            if (sim.Crisis != null)
            {
                var before = sim.Crisis.ThresholdModifier;
                sim.Crisis.ThresholdModifier = () => (before != null ? before() : 0f) + system.StrikeBonus;
                sim.Crisis.Changed += system.HandleCrisis;
            }
            return system;
        }

        public override string Key => SystemKey;
        public CorruptionConfig Config => _config;
        public CorruptionPhase Phase { get; private set; }
        /// <summary>경고: 횡령까지 / 횡령: 끝날 때까지.</summary>
        public float TimeLeft => _timer;
        /// <summary>이번 횡령으로 잃은 저장 금속.</summary>
        public float Lost { get; private set; }

        /// <summary>국가급 + 사회 사건 규칙.</summary>
        public bool Open => Sim.RuleOpen(StationRule.SocialCrisis, true) && Sim.Progression.GradeIndex >= _config.MinGrade;

        /// <summary>요구 부족분 (1 − 충족률, 요구가 아직 없으면 0).</summary>
        private float ShortageOf(ResidentNeed need)
        {
            foreach (var s in Sim.Needs.Statuses)
                if (s.Need == need)
                    return 1f - Mathf.Clamp01(s.Ratio);
            return 0f;
        }

        /// <summary>행정 부족분.</summary>
        public float Shortage => ShortageOf(ResidentNeed.Administration);

        /// <summary>소비(상업 구역) 부족분.</summary>
        public float CommerceShortage => ShortageOf(ResidentNeed.Commerce);

        /// <summary>굴림 한 번의 발생 확률.</summary>
        public float Chance => Mathf.Clamp01(_config.BaseChance + Shortage * _config.ShortageChance);

        /// <summary>파업 기준 만족도에 더하는 값 = 소비 부족 × 최대 + 방치한 비리 (사회 사건이 열린 판만).</summary>
        public float StrikeBonus => Open ? CommerceShortage * _config.CommerceStrikeMax + (_strikePending ? _config.IgnoredStrikeBonus : 0f) : 0f;

        /// <summary>다음 파업 기준을 올리는 방치한 비리가 남아 있는지 (다음 파업 사건이 끝나면 사라짐).</summary>
        public bool StrikePending => _strikePending;

        private PolicySystem Policies => Sim.GetSystem<PolicySystem>();

        /// <summary>방치한 비리 뒤 파업 경고가 떴는지 (사건 순서와 상관없이 놓치지 않게 경고 · 파업이 바뀔 때마다 적음).</summary>
        private void HandleCrisis()
        {
            if (_strikePending && Sim.Crisis.Phase != SocialCrisisSystem.CrisisPhase.Calm)
                _strikeSeen = true;
        }

        public override void Tick(float dt)
        {
            if (dt <= 0f || Sim.Session.IsGameOver)
                return;
            if (_strikePending && Sim.Crisis != null)
            {
                if (Sim.Crisis.Phase != SocialCrisisSystem.CrisisPhase.Calm)
                    _strikeSeen = true;
                else if (_strikeSeen)
                    _strikePending = _strikeSeen = false; // 다음 파업 사건(경고 → 가라앉음 또는 파업)이 끝남 — 한 번만
            }
            switch (Phase)
            {
                case CorruptionPhase.Calm:
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
                        BeginWarning();
                    break;
                case CorruptionPhase.Warning:
                    _timer -= dt;
                    if (_timer <= 0f)
                        StartEmbezzle("경고 시간 안에 대응하지 않아 ");
                    else
                        Changed?.Invoke();
                    break;
                case CorruptionPhase.Embezzling:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        Sim.Report($"횡령이 끝났습니다 · 금속 {Lost:0}을 잃었습니다 (다음 파업이 더 쉽게 일어남)", true);
                        Finish();
                    }
                    else
                        Changed?.Invoke();
                    break;
            }
        }

        /// <summary>경고 시작 (확인용 메뉴도 씀).</summary>
        public void BeginWarning()
        {
            Phase = CorruptionPhase.Warning;
            _timer = _config.WarningSeconds;
            _roll = 0f;
            Lost = 0f;
            Sim.Report($"비리 징후: 관리들이 금속을 빼돌리려 합니다 · {_timer:0}초 안에 대응 (행정이 부족할수록 자주 일어남)", false);
            Changed?.Invoke();
        }

        public bool CanChoose(Choice choice)
        {
            if (Phase != CorruptionPhase.Warning)
                return false;
            return choice != Choice.Audit || Sim.Resources.CanAfford(_config.AuditCost);
        }

        public bool Choose(Choice choice)
        {
            if (!CanChoose(choice))
                return false;
            switch (choice)
            {
                case Choice.Audit:
                    Sim.Resources.TrySpend(_config.AuditCost);
                    Sim.Report("감사: 장부를 맞춰 비리를 막았습니다", true);
                    Finish();
                    break;
                case Choice.Purge:
                    Sim.Population.AdjustSatisfaction(_config.PurgeSatisfaction);
                    if (_config.PurgeLeave > 0)
                        Sim.Population.Remove(_config.PurgeLeave, PopulationChangeReason.Crisis);
                    Sim.Report($"강경 숙청: 비리를 막았지만 만족도 {_config.PurgeSatisfaction:0} · 주민 {_config.PurgeLeave}명이 떠났습니다", false);
                    Finish();
                    break;
                case Choice.Transparency:
                    Policies?.AddTemporary(TransparencyEffect, _config.TransparencySeconds,
                        new PolicyModifier { Stat = PolicyStat.ResearchSpeed, Value = _config.TransparencyResearch });
                    Sim.Report($"일시 정책 '투명 행정': 비리를 막았습니다 · {_config.TransparencySeconds:0}초 동안 연구 -{(1f - _config.TransparencyResearch) * 100f:0}%", true);
                    Finish();
                    break;
                case Choice.Ignore:
                    StartEmbezzle("");
                    break;
            }
            return true;
        }

        private void StartEmbezzle(string cause)
        {
            Phase = CorruptionPhase.Embezzling;
            _timer = _config.EmbezzleSeconds;
            float stock = Sim.Resources.GetStock(ResourceType.Metal);
            Lost = stock * _config.EmbezzleStoredLoss;
            if (Lost > 0f)
                Sim.Resources.SetStock(ResourceType.Metal, stock - Lost);
            Policies?.AddTemporary(EmbezzleEffect, _config.EmbezzleSeconds,
                new PolicyModifier { Stat = PolicyStat.MetalProduction, Value = _config.EmbezzleMetal });
            _strikePending = true;
            _strikeSeen = Sim.Crisis != null && Sim.Crisis.Phase != SocialCrisisSystem.CrisisPhase.Calm;
            Sim.Report($"{cause}횡령: 저장 금속 {Lost:0}을 잃고 {_config.EmbezzleSeconds:0}초 동안 금속 생산 -{(1f - _config.EmbezzleMetal) * 100f:0}% · 다음 파업이 더 쉽게 일어남", false);
            Changed?.Invoke();
        }

        private void Finish()
        {
            Phase = CorruptionPhase.Calm;
            _timer = 0f;
            _roll = 0f;
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
            public float Lost;
            public bool StrikePending;
            public bool StrikeSeen;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            if (Phase == CorruptionPhase.Calm && _roll <= 0f && _cooldown <= 0f && !_strikePending)
                return null;
            return JsonUtility.ToJson(new State { Phase = (int)Phase, Roll = _roll, Timer = _timer, Cooldown = _cooldown, Lost = Lost, StrikePending = _strikePending, StrikeSeen = _strikeSeen });
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            Phase = (CorruptionPhase)Mathf.Clamp(s.Phase, 0, 2);
            _roll = s.Roll;
            _timer = s.Timer;
            _cooldown = s.Cooldown;
            Lost = s.Lost;
            _strikePending = s.StrikePending;
            _strikeSeen = s.StrikeSeen;
        }
    }
}
