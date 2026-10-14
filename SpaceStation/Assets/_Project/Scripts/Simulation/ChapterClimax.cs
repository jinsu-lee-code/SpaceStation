using System;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>13-1 절정 결과 3등급 (REDESIGN P3-2). 합격/불합격이 아니라 결과가 갈리고, 어느 쪽이든 다음 장으로 넘어간다 (P3-3).</summary>
    public enum ChapterOutcome
    {
        /// <summary>깔끔히 버팀.</summary>
        Clean,
        /// <summary>피해를 입고 버팀.</summary>
        Hurt,
        /// <summary>일부를 잃음.</summary>
        Lost,
    }

    /// <summary>
    /// 13-1 절정 사건 하나 (순수 C#). 종류마다 진행과 결과 판정을 따로 가진다 (사용자 결정 2026-10-15: 절정마다 전용 기준).
    /// <see cref="ChapterSystem"/>이 절정 단계에서 매 틱 진행시키고, <see cref="Finished"/>가 되면 <see cref="Outcome"/>을 읽는다.
    /// </summary>
    public abstract class ChapterClimax
    {
        protected StationSimulation Sim { get; private set; }
        protected ChapterClimaxSettings Settings { get; private set; }

        public bool Finished { get; protected set; }
        public ChapterOutcome Outcome { get; protected set; }
        /// <summary>결과 사유 한 줄 (결과 화면).</summary>
        public string Summary { get; protected set; } = "";
        /// <summary>진행 표시 (0~1).</summary>
        public virtual float Progress => Finished ? 1f : 0f;
        /// <summary>진행 중 안내 한 줄 (HUD).</summary>
        public virtual string Status => "";

        public static ChapterClimax Create(ChapterClimaxKind kind)
        {
            switch (kind)
            {
                case ChapterClimaxKind.MeteorBarrage:
                    return new MeteorBarrageClimax();
                default:
                    return new NoClimax();
            }
        }

        internal void Attach(StationSimulation sim, ChapterClimaxSettings settings)
        {
            Sim = sim;
            Settings = settings ?? new ChapterClimaxSettings();
        }

        /// <summary>절정 시작 (새로 시작할 때만, 복원은 <see cref="Restore"/>).</summary>
        internal abstract void Begin();

        internal abstract void Tick(float dt);

        /// <summary>절정이 끝나거나 장 틀이 사라질 때 (구독 해제).</summary>
        internal virtual void Detach() { }

        internal virtual string Capture() => null;

        internal virtual void Restore(string state) { }
    }

    /// <summary>절정 없음: 시작하자마자 '깔끔히 버팀'.</summary>
    internal sealed class NoClimax : ChapterClimax
    {
        internal override void Begin()
        {
            Finished = true;
            Outcome = ChapterOutcome.Clean;
        }

        internal override void Tick(float dt) { }
    }

    /// <summary>
    /// 13-1 그레이박스 절정: 운석 연타. 일정 간격으로 파(파당 피해 개수 고정)를 보내고, 마지막 파 뒤 정리 시간이 지나면 판정.
    /// 판정 = 절정 동안 파손된 수 · 파괴된 수 — 파괴가 기준을 넘으면 일부를 잃음, 파괴 0 + 파손 기준 이하면 깔끔히, 그 사이는 피해를 입고.
    /// </summary>
    internal sealed class MeteorBarrageClimax : ChapterClimax
    {
        private int _wavesFired;
        private float _timer;
        private float _elapsed;
        private int _damaged;
        private int _destroyedAtStart;
        private bool _subscribed;

        public override float Progress
        {
            get
            {
                if (Finished)
                    return 1f;
                float total = (Settings.Waves - 1) * Settings.WaveInterval + Settings.SettleSeconds;
                return total > 0f ? Mathf.Clamp01(_elapsed / total) : 1f;
            }
        }

        public override string Status => _wavesFired < Settings.Waves
            ? $"운석 {_wavesFired}/{Settings.Waves}파 · 파손 {_damaged} · 파괴 {Destroyed}"
            : $"피해 정리 중 · 파손 {_damaged} · 파괴 {Destroyed}";

        private int Destroyed => Math.Max(0, Sim.Session.ModulesDestroyed - _destroyedAtStart);

        internal override void Begin()
        {
            _wavesFired = 0;
            _timer = 0f; // 첫 파는 바로
            _elapsed = 0f;
            _damaged = 0;
            _destroyedAtStart = Sim.Session.ModulesDestroyed;
            Subscribe();
        }

        internal override void Tick(float dt)
        {
            if (Finished)
                return;
            _elapsed += dt;
            _timer -= dt;
            if (_timer > 0f)
                return;
            if (_wavesFired < Settings.Waves)
            {
                FireWave();
                _wavesFired++;
                _timer = _wavesFired < Settings.Waves ? Settings.WaveInterval : Settings.SettleSeconds;
                return;
            }
            Judge();
        }

        private void FireWave()
        {
            foreach (var e in Sim.EventPool)
            {
                if (!(e is MeteorEventData))
                    continue;
                Sim.ForceNextMeteorHits(Settings.HitsPerWave);
                if (Sim.Events.Trigger(e))
                    return;
                Sim.ForceNextMeteorHits(0);
            }
        }

        private void Judge()
        {
            int destroyed = Destroyed;
            if (destroyed > Settings.HurtMaxDestroyed)
                Outcome = ChapterOutcome.Lost;
            else if (destroyed == 0 && _damaged <= Settings.CleanMaxDamaged)
                Outcome = ChapterOutcome.Clean;
            else
                Outcome = ChapterOutcome.Hurt;
            Summary = $"파손 {_damaged}개 · 파괴 {destroyed}개";
            Finished = true;
            Detach();
        }

        private void HandleDamaged(DamageInfo info) => _damaged++;

        private void Subscribe()
        {
            if (_subscribed)
                return;
            Sim.Damage.Damaged += HandleDamaged;
            _subscribed = true;
        }

        internal override void Detach()
        {
            if (!_subscribed)
                return;
            Sim.Damage.Damaged -= HandleDamaged;
            _subscribed = false;
        }

        [Serializable]
        private sealed class State
        {
            public int Waves;
            public float Timer;
            public float Elapsed;
            public int Damaged;
            public int DestroyedAtStart;
        }

        internal override string Capture() => JsonUtility.ToJson(new State
        {
            Waves = _wavesFired,
            Timer = _timer,
            Elapsed = _elapsed,
            Damaged = _damaged,
            DestroyedAtStart = _destroyedAtStart,
        });

        internal override void Restore(string state)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s != null)
            {
                _wavesFired = s.Waves;
                _timer = s.Timer;
                _elapsed = s.Elapsed;
                _damaged = s.Damaged;
                _destroyedAtStart = s.DestroyedAtStart;
            }
            Subscribe();
        }
    }
}
