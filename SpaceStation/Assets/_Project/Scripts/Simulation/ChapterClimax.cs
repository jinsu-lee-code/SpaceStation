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
                case ChapterClimaxKind.LongNight:
                    return new LongNightClimax();
                case ChapterClimaxKind.DirectionalShower:
                    return new DirectionalShowerClimax();
                case ChapterClimaxKind.GreatMigration:
                    return new GreatMigrationClimax();
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
    /// 13-4 1장 절정 '첫 긴 밤' (사용자 결정 2026-10-18): 시작하면 바로 해가 지고 정해진 길이(180초)의 밤이 이어짐
    /// (<see cref="DayNightCycle.OverrideNight"/> — 끝나면 새벽). 밤 동안 운석(파 수 × 파당 개수)을 고르게 나눠 보냄.
    /// 새벽 뒤 정리 시간이 지나면 판정 = 밤 동안 산소 · 물 · 식량 중 하나라도 바닥이었던 시간 + 파괴 개수.
    /// </summary>
    internal sealed class LongNightClimax : ChapterClimax
    {
        private float _start;
        private float _elapsed;
        private int _meteors;
        private float _shortage;
        private int _damaged;
        private int _destroyedAtStart;
        private bool _subscribed;

        private float Night => Settings.NightSeconds;
        private float Total => Night + Settings.SettleSeconds;
        private int Meteors => Settings.Waves;
        private int Destroyed => Math.Max(0, Sim.Session.ModulesDestroyed - _destroyedAtStart);

        public override float Progress => Finished ? 1f : Total > 0f ? Mathf.Clamp01(_elapsed / Total) : 1f;

        public override string Status
        {
            get
            {
                string loss = $"바닥 {Mathf.FloorToInt(_shortage)}초 · 파괴 {Destroyed}"; // 카드 한 줄 (파손 수는 결과 요약에)
                if (_elapsed < Night)
                {
                    int left = Mathf.CeilToInt(Night - _elapsed);
                    return $"새벽까지 {left / 60}:{left % 60:00} · 운석 {_meteors}/{Meteors} · {loss}";
                }
                return $"새벽 · 피해 정리 중 · {loss}";
            }
        }

        internal override void Begin()
        {
            _start = Sim.ElapsedSeconds;
            _elapsed = 0f;
            _meteors = 0;
            _shortage = 0f;
            _damaged = 0;
            _destroyedAtStart = Sim.Session.ModulesDestroyed;
            Sim.DayNight.OverrideNight(_start, Night);
            Subscribe();
        }

        internal override void Tick(float dt)
        {
            if (Finished)
                return;
            _elapsed += dt;
            if (_elapsed <= Night && IsShort())
                _shortage += dt;
            while (_meteors < Meteors && _elapsed >= Night * (_meteors + 1) / (Meteors + 1))
            {
                FireMeteor();
                _meteors++;
            }
            if (_elapsed >= Total)
                Judge();
        }

        private bool IsShort()
        {
            var r = Sim.Resources;
            return r.Population > 0 && (r.IsDepleted(ResourceType.Oxygen) || r.IsDepleted(ResourceType.Water) || r.IsDepleted(ResourceType.Food));
        }

        private void FireMeteor()
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
            if (destroyed > Settings.HurtMaxDestroyed || _shortage > Settings.HurtMaxShortageSeconds + 1e-3f)
                Outcome = ChapterOutcome.Lost;
            else if (destroyed == 0 && _shortage <= Settings.CleanMaxShortageSeconds + 1e-3f)
                Outcome = ChapterOutcome.Clean;
            else
                Outcome = ChapterOutcome.Hurt;
            Summary = $"생명 유지 바닥 {Mathf.FloorToInt(_shortage)}초 · 파손 {_damaged}개 · 파괴 {destroyed}개";
            Finished = true;
            Detach(); // 덮은 밤 · 새벽은 지우지 않음 (끝난 뒤 원래 새벽까지 낮으로 덮은 구간이 남아 있을 수 있음)
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
            public float Start;
            public float Elapsed;
            public int Meteors;
            public float Shortage;
            public int Damaged;
            public int DestroyedAtStart;
        }

        internal override string Capture() => JsonUtility.ToJson(new State
        {
            Start = _start,
            Elapsed = _elapsed,
            Meteors = _meteors,
            Shortage = _shortage,
            Damaged = _damaged,
            DestroyedAtStart = _destroyedAtStart,
        });

        internal override void Restore(string state)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s != null)
            {
                _start = s.Start;
                _elapsed = s.Elapsed;
                _meteors = s.Meteors;
                _shortage = s.Shortage;
                _damaged = s.Damaged;
                _destroyedAtStart = s.DestroyedAtStart;
            }
            Sim.DayNight.OverrideNight(_start, Night); // 덮은 밤은 저장하지 않으므로 다시 덮음
            Subscribe();
        }
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

    /// <summary>
    /// 13-5c 2장 절정 '큰 유성우' (사용자 결정 2026-10-18): 예보(길게) → 한 방향 N파 → 곧바로 다른 방향 예보(짧게) → N파 (방향 수만큼, 같은 방향 연속 없음).
    /// 공통 유성우 틀의 특별 유성우(<see cref="MeteorShowerSystem.BeginRaid"/>, 이름 = 절정 제목, M-20)를 써서 예보 · 붉은 면 · 명중 판정이 평소 유성우와 같다.
    /// 예보 중인 평소 유성우는 거두고, 떨어지는 중이면 끝나길 기다림. 마지막 방향 뒤 남은 파손이 모두 고쳐지거나 파괴될 때까지(최대 정리 시간) 기다려 판정:
    /// 깔끔 = 파괴 0 + 막아 낸 비율(포탑 격추 · 실드가 빗겨내 빗나감 · 파손 면역) 기준 이상 / 피해 = 파괴 기준 이하 / 그 밖 = 일부를 잃음. 파손 수는 결과에 정보로만.
    /// </summary>
    internal sealed class DirectionalShowerClimax : ChapterClimax
    {
        private int _directionsDone;
        private bool _started;
        private Vector3Int _last;
        private int _meteors, _blocked, _damaged, _destroyedAtStart;
        private float _settle = -1f; // 정리 중 남은 시간 (음수 = 아직 떨어지는 중)
        private bool _subscribed;

        private MeteorShowerSystem Shower => Sim.MeteorShower;
        private string Label => string.IsNullOrEmpty(Settings.Title) ? "큰 유성우" : Settings.Title;
        private int Destroyed => Math.Max(0, Sim.Session.ModulesDestroyed - _destroyedAtStart);
        private bool Ours => _started && Shower.IsRaid && Shower.Label == Label;

        public override float Progress
        {
            get
            {
                if (Finished)
                    return 1f;
                float steps = Settings.ShowerDirections + 1f; // 방향마다 + 정리
                float done = _directionsDone;
                if (Ours && Shower.Phase == MeteorShowerSystem.ShowerPhase.Falling)
                    done += (float)Shower.WavesDone / Math.Max(1, Shower.Waves);
                if (_settle >= 0f)
                    done = Settings.ShowerDirections + 1f - _settle / Math.Max(1f, Settings.SettleSeconds);
                return Mathf.Clamp01(done / steps);
            }
        }

        public override string Status
        {
            get
            {
                string blocked = $"막음 {_blocked}/{_meteors}";
                if (_settle >= 0f)
                    return $"피해 정리 {Clock(_settle)} · 남은 파손 {Sim.Damage.DamagedCount} · 파괴 {Destroyed} · {blocked}";
                if (!Ours)
                    return "지나가는 유성우가 끝나길 기다리는 중";
                string step = $"방향 {_directionsDone + 1}/{Settings.ShowerDirections} · {MeteorShowerSystem.DirectionName(Shower.Direction)}"; // 카드에 절정 이름이 이미 있음
                return Shower.Phase == MeteorShowerSystem.ShowerPhase.Warning
                    ? $"{step} 예보 {Clock(Shower.TimeLeft)} · {blocked}"
                    : $"{step} {Shower.WavesDone}/{Shower.Waves}파 · {blocked}";
            }
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        internal override void Begin()
        {
            _directionsDone = 0;
            _started = false;
            _last = Vector3Int.zero;
            _meteors = _blocked = _damaged = 0;
            _settle = -1f;
            _destroyedAtStart = Sim.Session.ModulesDestroyed;
            Subscribe();
            StartNext();
        }

        internal override void Tick(float dt)
        {
            if (Finished)
                return;
            if (_settle >= 0f)
            {
                _settle -= dt;
                if (_settle <= 0f || Sim.Damage.DamagedCount == 0)
                    Judge();
                return;
            }
            if (!_started)
            {
                StartNext();
                return;
            }
            if (Shower.Phase != MeteorShowerSystem.ShowerPhase.Idle)
                return;
            _started = false; // 이 방향이 다 떨어짐
            _directionsDone++;
            if (_directionsDone < Settings.ShowerDirections)
            {
                StartNext();
                return;
            }
            _settle = Math.Max(0.01f, Settings.SettleSeconds);
            if (Sim.Damage.DamagedCount == 0)
                Judge();
        }

        private void StartNext()
        {
            if (Shower.Active && !Shower.IsRaid)
                Shower.CancelWarning(); // 예보 중인 평소 유성우는 거둠 (떨어지는 중이면 끝나길 기다림)
            if (Shower.Active)
                return;
            float warning = _directionsDone == 0 ? Settings.ShowerFirstWarning : Settings.ShowerNextWarning;
            _started = Shower.BeginRaid(warning, Settings.Waves, Settings.HitsPerWave, null, Label, _directionsDone == 0 ? Vector3Int.zero : _last);
            if (_started)
                _last = Shower.Direction;
        }

        private void Judge()
        {
            int destroyed = Destroyed;
            float ratio = _meteors > 0 ? (float)_blocked / _meteors : 1f;
            if (destroyed == 0 && ratio >= Settings.CleanMinBlocked - 1e-4f)
                Outcome = ChapterOutcome.Clean;
            else if (destroyed <= Settings.HurtMaxDestroyed)
                Outcome = ChapterOutcome.Hurt;
            else
                Outcome = ChapterOutcome.Lost;
            Summary = $"운석 {_meteors}개 중 {_blocked}개 막음 ({ratio * 100f:0}%) · 파손 {_damaged} · 파괴 {destroyed}";
            Finished = true;
            Detach();
        }

        private void HandleResolved(MeteorFlight flight)
        {
            if (Finished || !Ours || flight == null)
                return;
            _meteors++;
            if (flight.Hit == null || flight.Immune)
                _blocked++;
        }

        private void HandleDamaged(DamageInfo info)
        {
            if (!Finished)
                _damaged++;
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;
            Sim.MeteorResolved += HandleResolved;
            Sim.Damage.Damaged += HandleDamaged;
            _subscribed = true;
        }

        internal override void Detach()
        {
            if (!_subscribed)
                return;
            Sim.MeteorResolved -= HandleResolved;
            Sim.Damage.Damaged -= HandleDamaged;
            _subscribed = false;
        }

        [Serializable]
        private sealed class State
        {
            public int DirectionsDone;
            public bool Started;
            public Vector3Int Last;
            public int Meteors;
            public int Blocked;
            public int Damaged;
            public int DestroyedAtStart;
            public float Settle;
        }

        internal override string Capture() => JsonUtility.ToJson(new State
        {
            DirectionsDone = _directionsDone,
            Started = _started,
            Last = _last,
            Meteors = _meteors,
            Blocked = _blocked,
            Damaged = _damaged,
            DestroyedAtStart = _destroyedAtStart,
            Settle = _settle,
        });

        internal override void Restore(string state)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s != null)
            {
                _directionsDone = s.DirectionsDone;
                _started = s.Started;
                _last = s.Last;
                _meteors = s.Meteors;
                _blocked = s.Blocked;
                _damaged = s.Damaged;
                _destroyedAtStart = s.DestroyedAtStart;
                _settle = s.Settle;
            }
            Subscribe(); // 유성우 자체(방향 · 남은 시간 · 파)는 공통 틀이 저장
        }
    }

    /// <summary>
    /// 13-6c 3장 절정 '대이주' (사용자 결정 2026-10-18 · 19): 섞인 종족 이민선 N척(3)이 짧은 간격으로 연달아 — 첫 배는 시작 20초 뒤,
    /// 그다음은 앞 배에 답하고 45초 뒤 (<see cref="ArkShipSystem.SendConvoy"/>, 배마다 평소처럼 게임을 멈추고 받기 · 빈 집만큼 · 거부).
    /// 마지막 배에 답한 뒤 정리 시간(90초) 동안 더 지켜보고 판정 = 받은 인원 비율 · 떠난 주민(절정 동안 인구가 줄어든 수) · 생명 유지 바닥 시간:
    /// 넉넉히 맞이함 = 60% 이상 · 0명 · 0초 / 빠듯하게 맞이함 = 30% 이상 · 3명 이하 · 60초 이하 / 그 밖 = 많은 이를 놓침. 유성우는 평소대로.
    /// </summary>
    internal sealed class GreatMigrationClimax : ChapterClimax
    {
        private int _sent, _answered, _offered, _accepted, _departed;
        private float _shortage;
        private float _settle = -1f; // 정리 중 남은 시간 (음수 = 아직 배가 옴)
        private bool _subscribed;

        private ArkShipSystem Ark => Sim.GetSystem<ArkShipSystem>();
        private int Ships => Settings.MigrationShips;

        public override float Progress
        {
            get
            {
                if (Finished)
                    return 1f;
                float steps = Ships + 1f; // 배마다 + 정리
                float done = _settle >= 0f ? Ships + 1f - _settle / Math.Max(1f, Settings.SettleSeconds) : _answered;
                return Mathf.Clamp01(done / steps);
            }
        }

        public override string Status
        {
            get
            {
                string tally = $"받음 {_accepted}/{_offered} · 떠남 {_departed}";
                if (_settle >= 0f)
                    return $"정리 {Clock(_settle)} · {tally} · 바닥 {Mathf.FloorToInt(_shortage)}초";
                var ark = Ark;
                string step = $"배 {Math.Min(Ships, _answered + 1)}/{Ships}";
                if (ark != null && ark.Pending != null && ark.Pending.Convoy)
                    return $"{step} 도착 · {tally}";
                if (ark != null && ark.ConvoyComing)
                    return $"{step} {ark.SpeciesName(ark.NextShip.Species)} {ark.NextShip.Size}명 · {Clock(ark.TimeLeft)} 뒤 · {tally}";
                return $"{step} · {tally}";
            }
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        internal override void Begin()
        {
            _sent = _answered = _offered = _accepted = _departed = 0;
            _shortage = 0f;
            _settle = -1f;
            Subscribe();
            if (Ark == null)
            {
                _settle = 0.01f; // 이민선이 없는 판 (봇 · 시험) → 바로 판정
                return;
            }
            SendNext(Settings.MigrationFirstLead);
        }

        internal override void Tick(float dt)
        {
            if (Finished)
                return;
            if (IsShort())
                _shortage += dt;
            if (_settle >= 0f)
            {
                _settle -= dt;
                if (_settle <= 0f)
                    Judge();
                return;
            }
            if (_answered >= Ships)
            {
                _settle = Math.Max(0.01f, Settings.SettleSeconds);
                return;
            }
            if (_sent == _answered)
                SendNext(Settings.MigrationInterval); // 앞 배에 답함 → 다음 배
        }

        private void SendNext(float seconds)
        {
            var ark = Ark;
            if (ark == null || ark.Pending != null)
                return; // 평소 배가 아직 답을 기다리면 그 뒤에
            ark.SendConvoy(_sent, Settings.MigrationSizeMin, Settings.MigrationSizeMax, seconds);
            _sent++;
        }

        private bool IsShort()
        {
            var r = Sim.Resources;
            return r.Population > 0 && (r.IsDepleted(ResourceType.Oxygen) || r.IsDepleted(ResourceType.Water) || r.IsDepleted(ResourceType.Food));
        }

        private void Judge()
        {
            float ratio = _offered > 0 ? (float)_accepted / _offered : 1f;
            bool Within(float minAccepted, int maxDeparted, float maxShortage) =>
                ratio >= minAccepted - 1e-4f && _departed <= maxDeparted && _shortage <= maxShortage + 1e-3f;
            if (Within(Settings.CleanMinAccepted, Settings.CleanMaxDeparted, Settings.CleanMaxShortageSeconds))
                Outcome = ChapterOutcome.Clean;
            else if (Within(Settings.HurtMinAccepted, Settings.HurtMaxDeparted, Settings.HurtMaxShortageSeconds))
                Outcome = ChapterOutcome.Hurt;
            else
                Outcome = ChapterOutcome.Lost;
            Summary = $"이주민 {_offered}명 중 {_accepted}명 받음 ({ratio * 100f:0}%) · 떠난 주민 {_departed}명 · 생명 유지 바닥 {Mathf.FloorToInt(_shortage)}초";
            Finished = true;
            Detach();
        }

        private void HandleResolved(ArkShip ship, ShipAnswer answer, int boarded)
        {
            if (Finished || ship == null || !ship.Convoy)
                return;
            _answered++;
            _offered += ship.Size;
            _accepted += boarded;
        }

        private void HandlePopulation(int delta, PopulationChangeReason reason)
        {
            if (!Finished && delta < 0)
                _departed -= delta;
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;
            var ark = Ark;
            if (ark != null)
                ark.Resolved += HandleResolved;
            Sim.Population.PopulationChanged += HandlePopulation;
            _subscribed = true;
        }

        internal override void Detach()
        {
            if (!_subscribed)
                return;
            var ark = Ark;
            if (ark != null)
                ark.Resolved -= HandleResolved;
            Sim.Population.PopulationChanged -= HandlePopulation;
            _subscribed = false;
        }

        [Serializable]
        private sealed class State
        {
            public int Sent;
            public int Answered;
            public int Offered;
            public int Accepted;
            public int Departed;
            public float Shortage;
            public float Settle;
        }

        internal override string Capture() => JsonUtility.ToJson(new State
        {
            Sent = _sent,
            Answered = _answered,
            Offered = _offered,
            Accepted = _accepted,
            Departed = _departed,
            Shortage = _shortage,
            Settle = _settle,
        });

        internal override void Restore(string state)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s != null)
            {
                _sent = s.Sent;
                _answered = s.Answered;
                _offered = s.Offered;
                _accepted = s.Accepted;
                _departed = s.Departed;
                _shortage = s.Shortage;
                _settle = s.Settle;
            }
            Subscribe(); // 오는 배 · 답을 기다리는 배는 이민선 시스템이 저장
        }
    }
}
