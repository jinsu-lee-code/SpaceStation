using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>단계형 습격의 단계 (REVIEW_1115 6번).</summary>
    public enum RaidStage
    {
        None,
        /// <summary>정찰 — 냉담 세력 (작은 함선 몇 척 · 포격 한 파).</summary>
        Scout,
        /// <summary>함대 — 적대 세력 (호위함 여럿 · 포격 여러 파 · 끝나면 침입 드론).</summary>
        Fleet,
        /// <summary>기함 — 적대 세력의 함대를 몇 번 막아 낸 뒤 (체력 막대, 꺾거나 버텨야 끝남). 서사판 7장 대함대.</summary>
        Flagship,
    }

    public enum RaidShipClass
    {
        Scout,
        Escort,
        Flagship,
    }

    /// <summary>습격이 끝난 까닭.</summary>
    public enum RaidOutcome
    {
        None,
        /// <summary>함선 · 기함을 모두 꺾음.</summary>
        Defeated,
        /// <summary>포격을 마치고 남은 함선이 물러감 (정찰 · 함대).</summary>
        Withdrew,
        /// <summary>기함이 시간 안에 꺾이지 않아 큰 포격을 남기고 물러감.</summary>
        Retreated,
    }

    /// <summary>X1b 비장의 카드 (한 판에 한 번).</summary>
    public enum TrumpCard
    {
        None,
        /// <summary>방주 방패 — 한동안 습격 포격을 모두 막음, 코어의 발생기를 침입 드론에게서 지켜야 함.</summary>
        ArkShield,
        /// <summary>궤도 포격 — 기함에 큰 피해, 고른 발사대를 충전 동안 지켜야 함.</summary>
        OrbitalStrike,
    }

    [Serializable]
    public sealed class RaidShip
    {
        public RaidShipClass Class;
        public float Hp;
        public float MaxHp;
        public bool Alive => Hp > 0f;
    }

    /// <summary>
    /// REVIEW_1115 6번 외부 위협 1차 공통 고리 (사용자 결정 2026-11-15 · 11-24, CONNECT_1121 X1 · X4):
    /// - 방문 세력의 습격 요청(<see cref="ContactSystem.RaidOverride"/>)을 넘겨받아 단계를 정함 — 냉담 = 정찰, 적대 = 함대, 적대인데 함대를 몇 번 막아 냈으면 기함.
    ///   기함을 꺾으면 그 세력 습격이 한동안 멈춤.
    /// - 함선은 체력이 있다. 포격(유성우와 같은 판정 <see cref="MeteorShowerSystem.BeginRaid"/>)을 포탑이 격추할 때마다 반격으로 깎임 (기함 단계 = 기함).
    ///   함선을 모두 꺾으면 남은 포격도 멈춤. 포수 의자 · 비장의 카드는 <see cref="DamageShip"/> · <see cref="DamageFlagship"/>로 더 깎음.
    /// - 기함: 쉬었다 쏘기를 되풀이, 체력이 절반 아래면 침입 드론 한 번, 시간 안에 못 꺾으면 큰 포격 한 번 남기고 물러감.
    /// - 규모 = 정거장 크기(바깥에 드러난 모듈, <see cref="SizeScale"/>) × 방문 횟수 규모, 상한.
    /// - 규칙 <see cref="StationRule.FleetRaid"/>가 열려야 넘겨받음 (없으면 기존 습격 그대로). 판(서사판 7장 위기)은 <see cref="Start"/>로 바로 부름.
    /// </summary>
    public sealed class RaidFleetSystem : StationSystem
    {
        public const string SystemKey = "raidFleet";
        public override string Key => SystemKey;

        private const int FactionCount = 3;

        private readonly RaidFleetConfig _config;
        private readonly Func<float> _random01;
        private readonly List<RaidShip> _ships = new List<RaidShip>();
        private readonly int[] _fleetsHeld = new int[FactionCount];
        private readonly float[] _pausedUntil = new float[FactionCount];
        private bool _bombarding;
        private bool _dronesSent;
        private float _gap;
        private float _scale = 1f;
        private float _hitScale = 1f;

        /// <summary>단계 · 함선 · 체력 · 시간이 바뀜 (화면).</summary>
        public event Action Changed;
        /// <summary>함선 하나가 꺾임.</summary>
        public event Action<RaidShip> ShipDestroyed;
        /// <summary>습격이 끝남 (단계, 까닭).</summary>
        public event Action<RaidStage, RaidOutcome> Ended;

        public RaidFleetSystem(RaidFleetConfig config, Func<float> random01 = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (random01 == null)
            {
                var rng = new System.Random();
                random01 = () => (float)rng.NextDouble();
            }
            _random01 = random01;
        }

        public RaidFleetConfig Config => _config;
        /// <summary>규칙이 열렸는지 (방문 세력이 있어야 함).</summary>
        public bool Enabled => Sim.Contact != null && Sim.RuleOpen(StationRule.FleetRaid, false);

        public RaidStage Stage { get; private set; }
        public ContactFaction Faction { get; private set; }
        public string Source { get; private set; } = "";
        /// <summary>함선이 있는 쪽 (포격이 오는 쪽).</summary>
        public Vector3Int Direction { get; private set; }
        public IReadOnlyList<RaidShip> Ships => _ships;
        public bool Active => Stage != RaidStage.None;
        /// <summary>기함 단계: 물러가기까지 (초).</summary>
        public float TimeLeft { get; private set; }
        /// <summary>시작을 기다림 (유성우 · 다른 포격이 끝나면).</summary>
        public bool Pending => _pendingStage != RaidStage.None;

        public RaidShip Flagship
        {
            get
            {
                foreach (var s in _ships)
                    if (s.Class == RaidShipClass.Flagship)
                        return s;
                return null;
            }
        }

        public int AliveShips
        {
            get
            {
                int n = 0;
                foreach (var s in _ships)
                    if (s.Alive)
                        n++;
                return n;
            }
        }

        /// <summary>그 세력의 적대 함대를 막아 낸 횟수 (기함까지).</summary>
        public int FleetsHeld(ContactFaction f) => _fleetsHeld[(int)f];

        /// <summary>그 세력 습격이 기함을 꺾여 멈춰 있는 동안 (남은 초, 0 = 아님).</summary>
        public float PausedFor(ContactFaction f) => Math.Max(0f, _pausedUntil[(int)f] - Sim.ElapsedSeconds);

        /// <summary>CONNECT X1 고리: 정거장 크기 배율 (null = 바깥에 드러난 모듈 수로 — 엔드리스 성장 비례 위협이 바꿀 수 있음).</summary>
        public Func<float> SizeScale { get; set; }

        protected override void OnAttached()
        {
            Sim.MeteorResolved += HandleResolved;
            if (Sim.Contact != null)
            {
                Sim.Contact.RaidOverride = HandleRequest;
                Sim.Contact.BoardingFinished += HandleBoardingFinished;
            }
            if (Sim.MeteorShower != null)
            {
                Sim.MeteorShower.RaidBlocker = () => ShieldTimeLeft > 0f ? "방주 방패" : null;
                Sim.MeteorShower.RaidFocus = () => StrikeCharging ? _launcher : null;
                Sim.MeteorShower.RaidFocusShare = _config.StrikeFocus;
            }
        }

        // ---------------- X1b 비장의 카드 ----------------

        private const string ShieldTag = "arkShield";
        private ModuleInstance _launcher;

        /// <summary>고른 비장의 카드 (None = 아직 안 고름).</summary>
        public TrumpCard Trump { get; private set; }
        public bool TrumpUsed { get; private set; }
        /// <summary>함대 습격이 열렸는데 아직 카드를 안 고름 (화면이 고르기 창을 띄움).</summary>
        public bool TrumpChoicePending => Enabled && Trump == TrumpCard.None;
        public float ShieldTimeLeft { get; private set; }
        public float StrikeTimeLeft { get; private set; }
        public bool StrikeCharging => StrikeTimeLeft > 0f && _launcher != null;
        public ModuleInstance Launcher => _launcher;

        /// <summary>비장의 카드를 고름 (한 번만).</summary>
        public bool ChooseTrump(TrumpCard card)
        {
            if (card == TrumpCard.None || Trump != TrumpCard.None)
                return false;
            Trump = card;
            Sim.Report($"비장의 카드: {TrumpName(card)} · 한 판에 한 번 쓸 수 있습니다", true);
            Changed?.Invoke();
            return true;
        }

        public static string TrumpName(TrumpCard card) => card == TrumpCard.ArkShield ? "방주 방패" : card == TrumpCard.OrbitalStrike ? "궤도 포격" : "-";

        /// <summary>지금 쓸 수 있는지 (안 되면 까닭).</summary>
        public bool CanUseTrump(out string reason)
        {
            reason = null;
            if (Trump == TrumpCard.None)
                reason = "아직 고르지 않았습니다";
            else if (TrumpUsed)
                reason = "이미 썼습니다";
            else if (!Active)
                reason = "습격 중에만";
            else if (Trump == TrumpCard.OrbitalStrike && Stage != RaidStage.Flagship)
                reason = "기함에게만";
            return reason == null;
        }

        /// <summary>방주 방패를 켬: 한동안 포격을 모두 막고, 코어의 발생기로 침입 드론이 들어옴 (못 끄면 방패가 바로 꺼짐).</summary>
        public bool UseArkShield()
        {
            if (Trump != TrumpCard.ArkShield || !CanUseTrump(out _))
                return false;
            TrumpUsed = true;
            ShieldTimeLeft = _config.ShieldSeconds;
            Sim.Report($"방주 방패를 펼쳤습니다 · {ShieldTimeLeft:0}초 동안 포격을 모두 막습니다 · 코어의 발생기로 침입 드론이 들어옵니다", true);
            Sim.Contact?.BeginBoardingAt(Sim.Core, _config.ShieldDrones, _config.ShieldDroneSeconds, Source, ShieldTag);
            Changed?.Invoke();
            return true;
        }

        /// <summary>궤도 포격 충전 시작: 바깥에 드러난 가동 중인 모듈 하나를 발사대로 (그동안 포격이 몰림 · 파손되면 실패).</summary>
        public bool UseOrbitalStrike(ModuleInstance launcher)
        {
            if (Trump != TrumpCard.OrbitalStrike || !CanUseTrump(out _) || !CanLaunchFrom(launcher))
                return false;
            TrumpUsed = true;
            _launcher = launcher;
            StrikeTimeLeft = _config.StrikeCharge;
            Sim.Report($"궤도 포격 충전 · {launcher.Data?.DisplayName ?? "모듈"} · {StrikeTimeLeft:0}초 동안 지키세요 (포격이 몰립니다)", true);
            Changed?.Invoke();
            return true;
        }

        /// <summary>발사대가 될 수 있는 모듈 (코어가 아닌, 바깥에 드러난 가동 중인 모듈).</summary>
        public bool CanLaunchFrom(ModuleInstance m) =>
            m != null && m != Sim.Core && Sim.Connectivity.IsActive(m) && !Sim.Damage.IsDamaged(m) && DamageSystem.IsExterior(Sim.Grid, m);

        private void HandleBoardingFinished(string tag, bool cleared)
        {
            if (tag != ShieldTag || ShieldTimeLeft <= 0f)
                return;
            if (cleared)
                Sim.Report("발생기의 침입 드론을 모두 껐습니다 · 방주 방패가 그대로 이어집니다", true);
            else
            {
                ShieldTimeLeft = 0f;
                Sim.Report("발생기를 지키지 못해 방주 방패가 꺼졌습니다", false);
            }
            Changed?.Invoke();
        }

        private void TickTrump(float dt)
        {
            if (ShieldTimeLeft > 0f)
            {
                ShieldTimeLeft = Math.Max(0f, ShieldTimeLeft - dt);
                if (ShieldTimeLeft <= 0f)
                    Sim.Report("방주 방패가 꺼졌습니다", true);
            }
            if (StrikeTimeLeft <= 0f || _launcher == null)
                return;
            if (!Sim.Grid.TryGetModule(_launcher.Origin, out var still) || still != _launcher || Sim.Damage.IsDamaged(_launcher))
            {
                Sim.Report("발사대가 파손되어 궤도 포격이 실패했습니다", false);
                StrikeTimeLeft = 0f;
                _launcher = null;
                Changed?.Invoke();
                return;
            }
            StrikeTimeLeft -= dt;
            if (StrikeTimeLeft > 0f)
                return;
            StrikeTimeLeft = 0f;
            _launcher = null;
            var f = Flagship;
            if (f == null)
                return;
            Sim.Report($"궤도 포격 발사 · 기함 체력 -{f.MaxHp * _config.StrikeDamage:0}", true);
            DamageFlagship(f.MaxHp * _config.StrikeDamage);
        }

        // ---------------- 요청 → 단계 ----------------

        private bool HandleRequest(ContactSystem.RaidRequest r)
        {
            if (!Enabled)
                return false;
            string name = ContactConfig.FactionName(r.Faction);
            if (Active || Pending)
            {
                Sim.Report($"{name} 함선이 물러납니다 (이미 습격 중)", true);
                return true;
            }
            if (PausedFor(r.Faction) > 0f)
            {
                Sim.Report($"{name}: 기함을 잃은 뒤라 함선이 다가오지 못합니다", true);
                return true;
            }
            var stage = StageFor(r.Faction, r.Tier);
            Start(stage, r.Faction, r.Source, r.Scale, r.Guard ? Sim.Contact.Config.GuardDefense : 1f);
            return true;
        }

        /// <summary>평판 단계 + 막아 낸 횟수로 정하는 단계.</summary>
        public RaidStage StageFor(ContactFaction f, ReputationTier tier)
        {
            if (tier != ReputationTier.Hostile)
                return RaidStage.Scout;
            return _fleetsHeld[(int)f] >= _config.FleetsToFlagship ? RaidStage.Flagship : RaidStage.Fleet;
        }

        private RaidStage _pendingStage;
        private ContactFaction _pendingFaction;
        private string _pendingSource = "";
        private float _pendingVisitScale = 1f;
        private float _pendingDefense = 1f;

        /// <summary>
        /// 습격 시작 (판이 바로 부를 때도 — 서사판 7장 위기 = 기함). 유성우 · 다른 포격이 진행 중이면 끝난 뒤 시작.
        /// <paramref name="visitScale"/> = 방문 횟수 규모, <paramref name="defense"/> = 경계 태세 배율 (탄 수).
        /// </summary>
        public void Start(RaidStage stage, ContactFaction faction, string source, float visitScale = 1f, float defense = 1f)
        {
            if (stage == RaidStage.None || Active)
                return;
            _pendingStage = stage;
            _pendingFaction = faction;
            _pendingSource = string.IsNullOrEmpty(source) ? ContactConfig.FactionName(faction) : source;
            _pendingVisitScale = Math.Max(0.1f, visitScale);
            _pendingDefense = Math.Max(0.1f, defense);
            TryStartPending();
        }

        private void TryStartPending()
        {
            if (_pendingStage == RaidStage.None || Active || Sim.MeteorShower == null || Sim.MeteorShower.Active)
                return;
            Stage = _pendingStage;
            Faction = _pendingFaction;
            Source = _pendingSource;
            _pendingStage = RaidStage.None;
            _scale = Math.Min(_config.TotalScaleMax, CurrentSizeScale() * _pendingVisitScale);
            _hitScale = _scale * _pendingDefense;
            _dronesSent = false;
            _gap = 0f;
            Direction = Vector3Int.zero; // 첫 포격이 정함
            _ships.Clear();
            switch (Stage)
            {
                case RaidStage.Scout:
                    AddShips(RaidShipClass.Scout, _config.ScoutShips, _config.ScoutHp);
                    Bombard(_config.ScoutWaves, _config.ScoutHits, "정찰선 포격");
                    break;
                case RaidStage.Fleet:
                    AddShips(RaidShipClass.Escort, _config.FleetShips, _config.FleetHp);
                    Bombard(_config.FleetWaves, _config.FleetHits, "함대 포격");
                    break;
                default:
                    AddShips(RaidShipClass.Escort, _config.FlagshipEscorts, _config.FleetHp);
                    AddShips(RaidShipClass.Flagship, 1, _config.FlagshipHp);
                    TimeLeft = _config.FlagshipSeconds;
                    Bombard(_config.VolleyWaves, _config.VolleyHits, "기함 포격");
                    break;
            }
            Sim.Report(StageText(), false);
            Changed?.Invoke();
        }

        private string StageText()
        {
            switch (Stage)
            {
                case RaidStage.Scout: return $"{Source} 정찰선 {_ships.Count}척이 {MeteorShowerSystem.DirectionName(Direction)}에 나타났습니다 · 포탑이 격추할 때마다 반격합니다";
                case RaidStage.Fleet: return $"{Source} 함대 {_ships.Count}척이 {MeteorShowerSystem.DirectionName(Direction)}에 나타났습니다 · 포격이 끝나면 침입 드론을 보냅니다";
                default: return $"{Source} 기함이 {MeteorShowerSystem.DirectionName(Direction)}에 나타났습니다 · {TimeLeft / 60f:0.#}분 안에 꺾지 못하면 큰 포격을 남기고 물러갑니다";
            }
        }

        private void AddShips(RaidShipClass cls, int count, float hp)
        {
            float max = Math.Max(1f, hp * _scale);
            for (int i = 0; i < count; i++)
                _ships.Add(new RaidShip { Class = cls, Hp = max, MaxHp = max });
        }

        private void Bombard(int waves, int hits, string label)
        {
            var shower = Sim.MeteorShower;
            int h = Math.Max(1, (int)Math.Ceiling(hits * _hitScale));
            // 같은 함대는 같은 쪽에서 — 첫 포격이 방향을 정하고, 다음부터는 그 방향
            _bombarding = shower.BeginRaid(_config.Warning, waves, h, Source, label, toward: Direction);
            if (_bombarding)
                Direction = shower.Direction;
        }

        private float CurrentSizeScale()
        {
            if (SizeScale != null)
                return Math.Max(1f, SizeScale());
            int exterior = 0;
            foreach (var m in Sim.Grid.Modules)
                if (m != Sim.Core && DamageSystem.IsExterior(Sim.Grid, m))
                    exterior++;
            return _config.SizeScale(exterior);
        }

        // ---------------- 진행 ----------------

        public override void Tick(float dt)
        {
            if (Sim.Session.IsGameOver)
                return;
            if (dt > 0f)
                TickTrump(dt);
            if (!Active)
            {
                TryStartPending();
                return;
            }
            if (dt <= 0f)
                return;
            var shower = Sim.MeteorShower;
            bool showerDone = _bombarding && !shower.Active;
            if (showerDone)
                _bombarding = false;
            switch (Stage)
            {
                case RaidStage.Scout:
                case RaidStage.Fleet:
                    if (!showerDone)
                        break;
                    if (Stage == RaidStage.Fleet && !_dronesSent && AliveShips > 0)
                    {
                        _dronesSent = true;
                        Sim.Contact?.BeginBoardingRaid(Math.Max(1, (int)Math.Round(_config.FleetDrones * _hitScale)), _config.DroneSeconds, _scale, Source);
                    }
                    End(RaidOutcome.Withdrew);
                    break;
                default:
                    TimeLeft -= dt;
                    if (TimeLeft <= 0f)
                    {
                        Retreat();
                        break;
                    }
                    if (!_dronesSent && Flagship != null && Flagship.Hp <= Flagship.MaxHp * 0.5f && Sim.Contact != null && !Sim.Contact.Boarding)
                    {
                        _dronesSent = true;
                        Sim.Contact.BeginBoardingRaid(Math.Max(1, (int)Math.Round(_config.FlagshipDrones * _hitScale)), _config.DroneSeconds, _scale, Source);
                    }
                    if (!_bombarding)
                    {
                        _gap -= dt;
                        if (_gap <= 0f && !shower.Active)
                        {
                            Bombard(_config.VolleyWaves, _config.VolleyHits, "기함 포격");
                            _gap = _config.VolleyGap;
                        }
                    }
                    else if (showerDone)
                        _gap = _config.VolleyGap;
                    Changed?.Invoke();
                    break;
            }
        }

        private void Retreat()
        {
            var shower = Sim.MeteorShower;
            if (_bombarding)
                shower.EndRaid();
            _bombarding = false;
            // 물러가며 큰 포격 한 번 (예보를 짧게)
            shower.BeginRaid(Math.Min(10f, _config.Warning), 1, Math.Max(1, (int)Math.Ceiling(_config.RetreatHits * _hitScale)), Source, "기함 마지막 포격", toward: Direction);
            End(RaidOutcome.Retreated);
        }

        // ---------------- 반격 · 피해 ----------------

        /// <summary>습격 포격을 포탑이 격추하면 반격 (기함 단계 = 기함, 아니면 살아 있는 첫 함선).</summary>
        private void HandleResolved(MeteorFlight flight)
        {
            if (!Active || !_bombarding || !Sim.MeteorShower.IsRaid || flight.InterceptedAt == null || _config.CounterDamage <= 0f)
                return;
            if (Stage == RaidStage.Flagship)
                DamageFlagship(_config.CounterDamage);
            else
                DamageShip(FirstAlive(), _config.CounterDamage);
        }

        private int FirstAlive()
        {
            for (int i = 0; i < _ships.Count; i++)
                if (_ships[i].Alive && _ships[i].Class != RaidShipClass.Flagship)
                    return i;
            return -1;
        }

        /// <summary>함선 하나를 깎음 (포수 의자 등). 꺾였으면 true.</summary>
        public bool DamageShip(int index, float amount)
        {
            if (!Active || index < 0 || index >= _ships.Count || amount <= 0f)
                return false;
            var s = _ships[index];
            if (!s.Alive)
                return false;
            s.Hp = Math.Max(0f, s.Hp - amount);
            Changed?.Invoke();
            if (s.Alive)
                return false;
            ShipDestroyed?.Invoke(s);
            if (s.Class == RaidShipClass.Flagship)
            {
                Sim.Report($"{Source} 기함을 꺾었습니다 · 남은 함선이 물러갑니다", true);
                End(RaidOutcome.Defeated);
            }
            else if (Stage != RaidStage.Flagship && AliveShips == 0)
            {
                Sim.Report($"{Source} 함선을 모두 꺾었습니다 · 포격이 멈췄습니다", true);
                End(RaidOutcome.Defeated);
            }
            return true;
        }

        /// <summary>기함을 깎음 (반격 · 포수 의자 · 비장의 카드). 기함 단계가 아니면 false.</summary>
        public bool DamageFlagship(float amount)
        {
            var f = Flagship;
            return f != null && DamageShip(_ships.IndexOf(f), amount);
        }

        // ---------------- 끝 ----------------

        private void End(RaidOutcome outcome)
        {
            var stage = Stage;
            if (_bombarding && outcome != RaidOutcome.Retreated)
                Sim.MeteorShower.EndRaid();
            _bombarding = false;
            int f = (int)Faction;
            if (stage == RaidStage.Fleet)
                _fleetsHeld[f]++; // 함대 습격을 끝까지 버팀 (꺾었든 물러갔든)
            if (stage == RaidStage.Flagship && outcome == RaidOutcome.Defeated)
            {
                _fleetsHeld[f] = 0;
                _pausedUntil[f] = Sim.ElapsedSeconds + _config.FlagshipPause;
            }
            if (outcome == RaidOutcome.Withdrew)
                Sim.Report($"{Source} 함선 {AliveShips}척이 물러갔습니다", true);
            if (StrikeCharging) // 쏠 기함이 없어짐 (카드는 쓴 것으로)
            {
                StrikeTimeLeft = 0f;
                _launcher = null;
            }
            Stage = RaidStage.None;
            _ships.Clear();
            TimeLeft = 0f;
            Ended?.Invoke(stage, outcome);
            Changed?.Invoke();
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public int Stage;
            public int Faction;
            public string Source = "";
            public Vector3Int Direction;
            public List<RaidShip> Ships = new List<RaidShip>();
            public float TimeLeft;
            public bool Bombarding;
            public bool DronesSent;
            public float Gap;
            public float Scale = 1f;
            public float HitScale = 1f;
            public int[] FleetsHeld = new int[FactionCount];
            public float[] PausedUntil = new float[FactionCount];
            public int PendingStage;
            public int PendingFaction;
            public string PendingSource = "";
            public float PendingVisitScale = 1f;
            public float PendingDefense = 1f;
            public int Trump;
            public bool TrumpUsed;
            public float Shield;
            public float Strike;
            public int Launcher = -1;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            bool any = Active || Pending || Trump != TrumpCard.None;
            for (int i = 0; i < FactionCount && !any; i++)
                any = _fleetsHeld[i] > 0 || _pausedUntil[i] > 0f;
            if (!any)
                return null;
            return JsonUtility.ToJson(new State
            {
                Stage = (int)Stage, Faction = (int)Faction, Source = Source, Direction = Direction, Ships = new List<RaidShip>(_ships),
                TimeLeft = TimeLeft, Bombarding = _bombarding, DronesSent = _dronesSent, Gap = _gap, Scale = _scale, HitScale = _hitScale,
                FleetsHeld = (int[])_fleetsHeld.Clone(), PausedUntil = (float[])_pausedUntil.Clone(),
                PendingStage = (int)_pendingStage, PendingFaction = (int)_pendingFaction, PendingSource = _pendingSource,
                PendingVisitScale = _pendingVisitScale, PendingDefense = _pendingDefense,
                Trump = (int)Trump, TrumpUsed = TrumpUsed, Shield = ShieldTimeLeft, Strike = StrikeTimeLeft,
                Launcher = _launcher != null ? indexOf(_launcher) : -1,
            });
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            Stage = (RaidStage)s.Stage;
            Faction = (ContactFaction)s.Faction;
            Source = s.Source ?? "";
            Direction = s.Direction;
            _ships.Clear();
            if (s.Ships != null)
                _ships.AddRange(s.Ships);
            TimeLeft = s.TimeLeft;
            _bombarding = s.Bombarding;
            _dronesSent = s.DronesSent;
            _gap = s.Gap;
            _scale = s.Scale > 0f ? s.Scale : 1f;
            _hitScale = s.HitScale > 0f ? s.HitScale : 1f;
            for (int i = 0; i < FactionCount; i++)
            {
                _fleetsHeld[i] = s.FleetsHeld != null && i < s.FleetsHeld.Length ? s.FleetsHeld[i] : 0;
                _pausedUntil[i] = s.PausedUntil != null && i < s.PausedUntil.Length ? s.PausedUntil[i] : 0f;
            }
            _pendingStage = (RaidStage)s.PendingStage;
            _pendingFaction = (ContactFaction)s.PendingFaction;
            _pendingSource = s.PendingSource ?? "";
            _pendingVisitScale = s.PendingVisitScale > 0f ? s.PendingVisitScale : 1f;
            _pendingDefense = s.PendingDefense > 0f ? s.PendingDefense : 1f;
            Trump = (TrumpCard)Mathf.Clamp(s.Trump, 0, 2);
            TrumpUsed = s.TrumpUsed;
            ShieldTimeLeft = Math.Max(0f, s.Shield);
            _launcher = s.Launcher >= 0 ? moduleAt(s.Launcher) : null;
            StrikeTimeLeft = _launcher != null ? Math.Max(0f, s.Strike) : 0f;
            Changed?.Invoke();
        }
    }
}
