using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>이민선 응답 (사용자 결정 2026-10-18 D-9: 일부만 = 빈 집만큼).</summary>
    public enum ShipAnswer
    {
        Accept,
        Partial,
        Refuse,
    }

    /// <summary>예보된 · 도착한 이민선 하나.</summary>
    [Serializable]
    public sealed class ArkShip
    {
        public string Species;
        public int Size;
        /// <summary>이 장에서 처음 오는 종족을 태운 배.</summary>
        public bool NewSpecies;
    }

    /// <summary>
    /// 13-3 서사판 이민선 시계 + 종족 기본값 (REDESIGN P3-1 · T5 · V2 · S2, 사용자 결정 2026-10-18 D-8 · D-9).
    /// - 장 데이터가 켜면(2장부터) 간격(약 4분)마다 배가 옴. 도착 1분 전 예보(종족 · 규모), 다음 배 시각은 늘 보임.
    ///   장의 첫 배 = 그 장의 새 종족(있으면), 그 뒤 배 = 만난 종족이 섞임. 여객 도킹 포트가 있으면 규모 · 예보 시간 +.
    /// - 도착하면 대기 → 화면이 게임을 멈추고 받기 / 빈 집만큼 / 거부를 고름 → 인구(대기열로 종족) · 코어 성격 축 · 코어 기록.
    ///   전부 받으면 집이 모자란 만큼 과밀 (공통 규칙: 과밀이면 주민이 차례로 떠남).
    /// - 위기 완충: 만족도가 기준 미만이거나 산소 · 물 · 식량 중 하나가 바닥이면 시계가 느려짐. 절정 · 전환 중에는 멈춤.
    /// - 종족 기본값: 새 주민 종류(자연 증가 = 지금 무리 비율대로), 기술 = 특성 통로에 더함 (종족 + 개인 합산 상한),
    ///   사회 = 중시 요구 감점 × (1 + 가중치 × 그 종족 비율), 견딤 · 정책 반응 = 값만 (5장 · 13-6).
    /// - 공통 바닥 포트 이민선(<see cref="ImmigrationSystem"/>)은 끔.
    /// </summary>
    public sealed class ArkShipSystem : StationSystem
    {
        public const string SystemKey = "arkships";

        private readonly ArkShipConfig _config;
        private readonly Func<float> _random01;
        private readonly List<string> _met = new List<string>();
        private float _timer = -1f;
        private ArkShip _forecast;
        private ArkShip _pending;
        private float _pendingWait;
        private int _newSpeciesChapter = -1;
        private bool _kindsChecked;

        /// <summary>예보가 나옴 (도착 전).</summary>
        public event Action<ArkShip> Forecast;
        /// <summary>배가 도착해 답을 기다림 (화면이 게임을 멈추고 묻는다).</summary>
        public event Action<ArkShip> Arrived;
        /// <summary>답을 정함 (배, 답, 내린 인원).</summary>
        public event Action<ArkShip, ShipAnswer, int> Resolved;

        public ArkShipSystem(ArkShipConfig config, Func<float> random01 = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (random01 == null)
            {
                var rng = new System.Random();
                random01 = () => (float)rng.NextDouble();
            }
            _random01 = random01;
        }

        public override string Key => SystemKey;
        public ArkShipConfig Config => _config;
        /// <summary>다음 배까지 (초, 시계가 꺼져 있으면 -1).</summary>
        public float TimeLeft => _timer;
        public bool ClockOn => _timer >= 0f;
        /// <summary>예보된 다음 배 (예보 전이면 null).</summary>
        public ArkShip NextShip => _forecast;
        /// <summary>도착해 답을 기다리는 배 (없으면 null).</summary>
        public ArkShip Pending => _pending;
        public IReadOnlyList<string> MetSpecies => _met;
        public int Ships { get; private set; }
        public int Accepted { get; private set; }
        public int TurnedAway { get; private set; }
        /// <summary>위기 완충으로 시계가 느린 중.</summary>
        public bool Slowed { get; private set; }

        private ChapterSystem Chapters => Sim.Chapters;
        private ChapterData Chapter => Chapters != null ? Chapters.Current : null;

        protected override void OnAttached()
        {
            var roster = Sim.Residents;
            if (roster != null)
            {
                roster.KindPicker = BirthKind;
                roster.KindAbility = KindAbility;
                roster.AbilityCap = _config.AbilityCap;
            }
            Sim.Needs.PenaltyScale = PenaltyScale;
            var port = Sim.GetSystem<ImmigrationSystem>();
            if (port != null)
                port.Suppressed = true;
            Meet(_config.StartSpecies);
            FillEmptyKinds();
        }

        public SpeciesData Find(string id) => _config.Find(id);

        // ---------------- 시계 ----------------

        public override void Tick(float dt)
        {
            if (!_kindsChecked)
                FillEmptyKinds();
            var chapter = Chapter;
            Sim.GrowthIntervalScale = chapter != null ? chapter.GrowthIntervalScale : 1f;
            if (dt <= 0f || Sim.Session.IsGameOver)
                return;
            if (_pending != null)
            {
                _pendingWait += dt; // 화면이 있으면 게임이 멈춰 여기까지 오지 않음 (봇 · 시험)
                if (_pendingWait >= _config.AutoResolveSeconds)
                    Answer(ShipAnswer.Partial);
                return;
            }
            if (chapter == null || !chapter.Ships)
            {
                _timer = -1f;
                _forecast = null;
                Slowed = false;
                return;
            }
            var phase = Chapters.Phase;
            if (phase != ChapterPhase.Preparation && phase != ChapterPhase.Warning)
                return; // 절정 · 전환 중에는 시계가 멈춤
            if (_timer < 0f)
                _timer = chapter.ShipInterval;
            Slowed = IsCrisis();
            _timer -= dt * (Slowed ? _config.CrisisClockScale : 1f);
            if (_forecast == null && _timer <= ForecastSeconds(chapter))
            {
                _forecast = Plan(chapter);
                Sim.Report($"이민선 예보: {SpeciesName(_forecast.Species)} {_forecast.Size}명 · {Clock(_timer)} 뒤 도착", true);
                Forecast?.Invoke(_forecast);
            }
            if (_timer > 0f)
                return;
            _pending = _forecast ?? Plan(chapter);
            _forecast = null;
            _pendingWait = 0f;
            _timer = chapter.ShipInterval;
            Ships++;
            Meet(Find(_pending.Species));
            Arrived?.Invoke(_pending);
        }

        /// <summary>예보 시간 = 장 값 + 포트 보너스.</summary>
        public float ForecastSeconds(ChapterData chapter) => chapter.ShipForecast + Ports * _config.PortForecastBonus;

        private int Ports => Math.Min(Sim.ImmigrationPorts, _config.MaxPortsCounted);

        private bool IsCrisis()
        {
            if (Sim.Resources.Population <= 0)
                return false;
            if (Sim.Population.Satisfaction < _config.CrisisSatisfaction)
                return true;
            var r = Sim.Resources;
            return r.IsDepleted(ResourceType.Oxygen) || r.IsDepleted(ResourceType.Water) || r.IsDepleted(ResourceType.Food);
        }

        private ArkShip Plan(ChapterData chapter)
        {
            var ship = new ArkShip();
            int index = Chapters.Index;
            if (chapter.NewSpecies != null && _newSpeciesChapter != index)
            {
                ship.Species = chapter.NewSpecies.Id;
                ship.NewSpecies = !_met.Contains(chapter.NewSpecies.Id);
                _newSpeciesChapter = index;
            }
            else
            {
                ship.Species = _met.Count > 0 ? _met[Math.Min(_met.Count - 1, (int)(_random01() * _met.Count))] : _config.StartSpecies?.Id ?? "";
            }
            int span = chapter.ShipSizeMax - chapter.ShipSizeMin + 1;
            ship.Size = chapter.ShipSizeMin + Math.Min(span - 1, (int)(_random01() * span)) + Ports * _config.PortSizeBonus;
            return ship;
        }

        // ---------------- 답 ----------------

        /// <summary>지금 빈 집 수 (수용 − 인구, 0 이상).</summary>
        public int FreeHousing => Math.Max(0, Sim.Resources.HousingCapacity - Sim.Resources.Population);

        /// <summary>이 답이면 내리는 인원.</summary>
        public int Boarding(ShipAnswer answer)
        {
            if (_pending == null)
                return 0;
            switch (answer)
            {
                case ShipAnswer.Accept: return _pending.Size;
                case ShipAnswer.Partial: return Math.Min(_pending.Size, FreeHousing);
                default: return 0;
            }
        }

        public bool Answer(ShipAnswer answer)
        {
            var ship = _pending;
            if (ship == null)
                return false;
            int n = Boarding(answer);
            int turned = ship.Size - n;
            _pending = null;
            _pendingWait = 0f;
            if (n > 0)
            {
                Sim.Residents?.QueueKinds(ship.Species, n);
                Sim.Population.Add(n, PopulationChangeReason.Immigration);
            }
            Accepted += n;
            TurnedAway += turned;
            string name = SpeciesName(ship.Species);
            var core = Sim.GetSystem<CorePersonalitySystem>();
            switch (answer)
            {
                case ShipAnswer.Accept:
                    core?.Apply(_config.AcceptShifts, $"이민선: {name} {n}명 모두 받음");
                    core?.SayAndLog("ship.accept", "{0} {1}명을 모두 받았습니다.", name, n, turned);
                    Sim.Report($"이민선: {name} {n}명이 들어왔습니다" + (n > 0 && Sim.Resources.Population > Sim.Resources.HousingCapacity ? " (집이 모자람)" : ""), true);
                    break;
                case ShipAnswer.Partial:
                    core?.Apply(_config.PartialShifts, $"이민선: {name} {n}명만 받음 ({turned}명 돌려보냄)");
                    core?.SayAndLog("ship.partial", "{0} {1}명을 받고 {2}명은 돌려보냈습니다.", name, n, turned);
                    Sim.Report($"이민선: {name} {n}명이 들어오고 {turned}명은 돌아갔습니다", n > 0);
                    break;
                default:
                    core?.Apply(_config.RefuseShifts, $"이민선: {name} {turned}명 거부");
                    core?.SayAndLog("ship.refuse", "{0} {2}명을 태운 배를 돌려보냈습니다.", name, n, turned);
                    Sim.Report($"이민선: {name} {turned}명을 돌려보냈습니다", false);
                    break;
            }
            Chapters?.SetFlag("ship");
            if (n > 0)
                Chapters?.SetFlag("ship.accepted");
            Resolved?.Invoke(ship, answer, n);
            return true;
        }

        // ---------------- 종족 ----------------

        public string SpeciesName(string id)
        {
            var s = Find(id);
            return s != null ? s.DisplayName : string.IsNullOrEmpty(id) ? "주민" : id;
        }

        private void Meet(SpeciesData species)
        {
            if (species != null && !_met.Contains(species.Id))
                _met.Add(species.Id);
        }

        /// <summary>종류가 없는 주민(새 판 시작 · 이전 세이브)은 시작 종족.</summary>
        private void FillEmptyKinds()
        {
            var roster = Sim.Residents;
            if (roster == null)
                return;
            _kindsChecked = true;
            string start = _config.StartSpecies != null ? _config.StartSpecies.Id : "";
            foreach (var r in roster.Residents)
                if (string.IsNullOrEmpty(r.Kind))
                    r.Kind = start;
        }

        /// <summary>자연 증가로 태어나는 주민: 지금 무리 비율대로 (없으면 시작 종족).</summary>
        private string BirthKind()
        {
            var residents = Sim.Residents.Residents;
            string start = _config.StartSpecies != null ? _config.StartSpecies.Id : "";
            if (residents.Count == 0)
                return start;
            var pick = residents[Math.Min(residents.Count - 1, (int)(_random01() * residents.Count))];
            return string.IsNullOrEmpty(pick.Kind) ? start : pick.Kind;
        }

        private float KindAbility(ResidentTrait trait)
        {
            float sum = 0f;
            foreach (var s in _config.Species)
                if (s != null && s.TechTrait == trait && s.TechPerResident > 0f)
                    sum += s.TechFor(Sim.Residents.CountKind(s.Id));
            return sum;
        }

        /// <summary>이 종족의 기술 효과 (지금 인원 기준, 상한 적용 — 주민 정보 "종족 기본" 칸).</summary>
        public float SpeciesTech(SpeciesData s) => s != null && Sim.Residents != null ? s.TechFor(Sim.Residents.CountKind(s.Id)) : 0f;

        private float PenaltyScale(ResidentNeed need)
        {
            var roster = Sim.Residents;
            if (roster == null || roster.Residents.Count == 0)
                return 1f;
            float scale = 1f;
            foreach (var s in _config.Species)
                if (s != null && s.FavoredNeed == need && s.FavoredWeight > 0f)
                    scale += s.FavoredWeight * roster.CountKind(s.Id) / roster.Residents.Count;
            return scale;
        }

        /// <summary>지금 무리 구성 ("토끼 12 · 곰 4").</summary>
        public string Composition()
        {
            var roster = Sim.Residents;
            if (roster == null)
                return "";
            var parts = new List<string>();
            foreach (var id in _met)
            {
                int n = roster.CountKind(id);
                if (n > 0)
                    parts.Add($"{SpeciesName(id)} {n}");
            }
            return string.Join(" · ", parts);
        }

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public float Timer = -1f;
            public bool HasForecast;
            public ArkShip Forecast;
            public bool HasPending;
            public ArkShip Pending;
            public float PendingWait;
            public int NewSpeciesChapter = -1;
            public List<string> Met = new List<string>();
            public int Ships;
            public int Accepted;
            public int TurnedAway;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf) => JsonUtility.ToJson(new State
        {
            Timer = _timer,
            HasForecast = _forecast != null,
            Forecast = _forecast,
            HasPending = _pending != null,
            Pending = _pending,
            PendingWait = _pendingWait,
            NewSpeciesChapter = _newSpeciesChapter,
            Met = new List<string>(_met),
            Ships = Ships,
            Accepted = Accepted,
            TurnedAway = TurnedAway,
        });

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            _timer = s.Timer;
            _forecast = s.HasForecast ? s.Forecast : null;
            _pending = s.HasPending ? s.Pending : null;
            _pendingWait = s.PendingWait;
            _newSpeciesChapter = s.NewSpeciesChapter;
            _met.Clear();
            if (s.Met != null)
                _met.AddRange(s.Met);
            Meet(_config.StartSpecies);
            Ships = s.Ships;
            Accepted = s.Accepted;
            TurnedAway = s.TurnedAway;
            _kindsChecked = false;
        }
    }
}
