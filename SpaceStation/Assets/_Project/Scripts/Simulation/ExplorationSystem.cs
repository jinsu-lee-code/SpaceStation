using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>탐사 지점 하나 (신호 묶음마다 잡힘).</summary>
    [Serializable]
    public sealed class ExploreSite
    {
        public SiteKind Kind;
        /// <summary>지점 칸 (정거장 격자 좌표).</summary>
        public Vector3Int Cell;
        /// <summary>정거장에서 본 지점 쪽.</summary>
        public Vector3Int Direction;
        /// <summary>잡힌 신호 묶음 번호 (<see cref="IExploreSignals.Round"/> — 서사판 = 장 번호. 세이브 이름이라 그대로 둠).</summary>
        public int Chapter;
        public bool Revealed;
        public bool Reached;
        /// <summary>자원 지점: 지금도 닿아 있음 (1초마다 다시 봄).</summary>
        public bool InReach;
        /// <summary>위험 지점: 다음 파편까지 (초).</summary>
        public float HazardTimer;
        /// <summary>CONNECT_1121 M3 공통 고리: 남은 매장량 (음수 = 끝없음 — 서사판). 쓰는 쪽(엔드리스 광맥)이 줄임.</summary>
        public float Reserve = -1f;
    }

    /// <summary>
    /// CONNECT_1121 M8 공통 고리: 신호를 정하는 쪽 (서사판 = 장 데이터, 엔드리스 = 광맥 — 등급 · 다 캔 뒤 주기).
    /// 묶음 번호가 바뀌고 종류가 있고 신호를 잡는 모듈이 가동 중이면 그 묶음의 지점을 만든다. 묶음마다 탐사선 하나.
    /// </summary>
    public interface IExploreSignals
    {
        /// <summary>신호 묶음 번호 (바뀌면 새 신호).</summary>
        int Round { get; }
        /// <summary>이번 묶음의 지점 종류 (비면 신호 없음).</summary>
        IReadOnlyList<SiteKind> Kinds { get; }
        /// <summary>정거장 끝에서 지점까지 칸 수 (최소 · 최대).</summary>
        int DistanceMin { get; }
        int DistanceMax { get; }
        /// <summary>지점을 놓을 방향 (null = 가로 4방향 — 서사판).</summary>
        IReadOnlyList<Vector3Int> Directions { get; }
        /// <summary>묶음 이름 (화면, 서사판 = 장 이름).</summary>
        string RoundName(int round);
    }

    /// <summary>
    /// 13-5a 탐사 (REDESIGN V5, 사용자 결정 2026-10-18 D-6) → CONNECT_1121 M8 공통 틀:
    /// - 신호를 정하는 쪽(<see cref="Signals"/>)에 신호가 있고 센서 어레이가 가동 중이면 그 묶음의 신호(지점)를 만든다 — 정거장 끝에서 그 방향으로 몇 칸, 방향마다 하나씩.
    /// - 한 묶음에 하나만 탐사선으로 밝힘 (금속 · 도착 시간). 밝히기 전에는 흐릿한 힌트만.
    /// - 밝힌 지점 근처(반경, 가로 · 세로 · 높이 각각 — 또는 <see cref="ReachRule"/>)에 코어에 연결된 모듈이 들어가면 닿음 → 종류별 효과
    ///   (소행성 · 얼음 = 닿아 있는 동안 금속 · 물 / 난파선 = 조각 수 + 연구 포인트 / 파편 지대 = 금속 + 그 뒤 그 방향 파편).
    ///   판의 보상(서사판 기록 조각 · 기록 로그 · 장 표시)은 <see cref="Revealed"/> · <see cref="Reached"/>를 받아 판이 더함.
    /// - 지난 묶음 지점은 남아 효과를 이어감. 세이브.
    /// </summary>
    public sealed class ExplorationSystem : StationSystem
    {
        public const string SystemKey = "exploration";
        /// <summary>판이 쓰는 표시 이름 (서사판 장 목표: 밝히면 'probe', 닿으면 'reach').</summary>
        public const string ProbeFlag = "probe";
        public const string ReachFlag = "reach";

        /// <summary>신호를 정하는 쪽 (null = 신호 없음).</summary>
        public IExploreSignals Signals { get; set; }

        /// <summary>닿음 판정을 판이 바꿈 (null = 반경 안에 코어에 연결된 모듈 — 서사판). 엔드리스 광맥 = 채굴 모듈이 맞닿음 등.</summary>
        public Func<ExploreSite, bool> ReachRule { get; set; }

        /// <summary>CONNECT_1121 M6 공통 고리: 지점을 만들자마자 밝힘 (탐사선 없음 — 엔드리스 광맥 "위치는 바로 보임"). 기본 꺼짐 = 서사판 그대로.</summary>
        public bool AutoReveal { get; set; }

        /// <summary>닿으면 무엇을 주는지 한 줄을 판이 바꿈 (null = 설정 문구 — 서사판). 엔드리스 광맥 = 도킹 생산 배율 · 매장량.</summary>
        public Func<ExploreSite, string> EffectText { get; set; }

        public string EffectOf(ExploreSite site) => EffectText != null ? EffectText(site) : _config.Effect(site.Kind);

        private static readonly Vector3Int[] Horizontal =
        {
            Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

        private readonly ExplorationConfig _config;
        private readonly Func<float> _random01;
        private readonly List<ExploreSite> _sites = new List<ExploreSite>();
        private readonly List<ModuleInstance> _facing = new List<ModuleInstance>();
        private readonly Dictionary<ModuleInstance, int> _facingCells = new Dictionary<ModuleInstance, int>();
        private readonly List<Vector3Int> _faceCells = new List<Vector3Int>();
        private readonly List<ModuleInstance> _targets = new List<ModuleInstance>();
        private int _generatedChapter = -1;
        private int _probeSite = -1;
        private float _probeTimer;
        private int _probedChapter = -1;
        private float _reachCheck;
        private int _wreckLogs;

        /// <summary>지점 목록이 바뀜 (생김 · 밝힘 · 닿음 · 탐사선 출발).</summary>
        public event Action Changed;
        public event Action<ExploreSite> Revealed;
        public event Action<ExploreSite> Reached;

        public ExplorationSystem(ExplorationConfig config, Func<float> random01 = null)
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
        public ExplorationConfig Config => _config;
        public IReadOnlyList<ExploreSite> Sites => _sites;
        /// <summary>난파선에서 찾은 조각 수 (서사판 = 기록 조각, 쓰는 쪽이 받아 씀).</summary>
        public int Fragments { get; private set; }
        public bool ProbeInFlight => _probeSite >= 0;
        public float ProbeTimeLeft => _probeTimer;
        public ExploreSite ProbeTarget => _probeSite >= 0 && _probeSite < _sites.Count ? _sites[_probeSite] : null;

        /// <summary>지금 신호 묶음 번호 (신호를 정하는 쪽이 없으면 0).</summary>
        public int Round => Signals != null ? Signals.Round : 0;
        private int ChapterIndex => Round;

        /// <summary>이번 묶음에 탐사선을 이미 보냈는지 (한 묶음 하나).</summary>
        public bool ProbeUsedThisRound => _probedChapter == ChapterIndex;

        /// <summary>묶음 이름 (화면, 없으면 빈 글).</summary>
        public string RoundName(int round) => Signals != null ? Signals.RoundName(round) ?? "" : "";

        /// <summary>난파선 기록 로그를 차례로 하나 꺼냄 (없으면 null) — 판이 기록 창에 씀.</summary>
        public string NextWreckLog()
        {
            var logs = _config.WreckLogs;
            if (logs.Count == 0)
                return null;
            string text = logs[Math.Min(_wreckLogs, logs.Count - 1)];
            _wreckLogs++;
            return text;
        }

        /// <summary>센서 어레이가 하나라도 가동 중.</summary>
        public bool SensorActive
        {
            get
            {
                if (_config.Sensor == null)
                    return true;
                foreach (var m in Sim.Grid.Modules)
                    if (m.Data == _config.Sensor && Sim.Connectivity.IsActive(m))
                        return true;
                return false;
            }
        }

        /// <summary>이번 묶음에 신호가 있는지.</summary>
        public bool HasSignals => Signals != null && Signals.Kinds != null && Signals.Kinds.Count > 0;

        /// <summary>이번 묶음 지점들.</summary>
        public void CurrentSites(List<ExploreSite> result)
        {
            result.Clear();
            foreach (var s in _sites)
                if (s.Chapter == ChapterIndex)
                    result.Add(s);
        }

        /// <summary>지난 묶음에 밝히지 못한 신호 (탐사선을 보낼 수 없으니 공간에 띄우지 않음).</summary>
        public bool IsExpired(ExploreSite site) => site != null && !site.Revealed && site.Chapter != ChapterIndex;

        // ---------------- 진행 ----------------

        public override void Tick(float dt)
        {
            if (Signals == null || Sim.Session.IsGameOver)
                return;
            if (_generatedChapter != ChapterIndex && HasSignals && SensorActive)
                Generate();
            if (dt <= 0f)
                return;
            if (_probeSite >= 0)
            {
                _probeTimer -= dt;
                if (_probeTimer <= 0f)
                    Reveal();
            }
            _reachCheck -= dt;
            if (_reachCheck <= 0f)
            {
                _reachCheck = 1f;
                CheckReach();
            }
            ApplyOngoing(dt);
        }

        private void Generate()
        {
            _generatedChapter = ChapterIndex;
            var data = Signals;
            var kinds = new List<SiteKind>(data.Kinds);
            Shuffle(kinds);
            var dirs = new List<Vector3Int>(data.Directions ?? Horizontal);
            Shuffle(dirs);
            var core = Sim.Core != null ? Sim.Core.Origin : Vector3Int.zero;
            for (int i = 0; i < kinds.Count; i++)
            {
                var dir = dirs[i % dirs.Count];
                int edge = Edge(dir, core);
                int dist = data.DistanceMin + Math.Min(data.DistanceMax - data.DistanceMin, (int)(_random01() * (data.DistanceMax - data.DistanceMin + 1)));
                // 옆으로 조금 (위아래 방향이면 가로 두 축 중 x)
                var side = dir.y != 0 ? Vector3Int.right : new Vector3Int(Math.Abs(dir.z), 0, Math.Abs(dir.x));
                int lateral = (int)(_random01() * 5f) - 2;
                int height = (int)(_random01() * 3f) - 1;
                var cell = core + dir * (edge + dist) + side * lateral + Vector3Int.up * height;
                _sites.Add(new ExploreSite { Kind = kinds[i], Cell = cell, Direction = dir, Chapter = ChapterIndex, Revealed = AutoReveal });
            }
            if (!AutoReveal) // 바로 밝히는 판은 신호를 정하는 쪽이 알림을 씀
                Sim.Report($"센서 어레이: 신호 {kinds.Count}개 · [관리] → 탐사에서 탐사선을 보낼 곳을 고르세요", true);
            Changed?.Invoke();
        }

        /// <summary>코어에서 그 방향으로 정거장이 뻗은 칸 수 (가장 먼 모듈 칸).</summary>
        private int Edge(Vector3Int dir, Vector3Int core)
        {
            int edge = 0;
            foreach (var m in Sim.Grid.Modules)
                foreach (var c in m.Cells)
                    edge = Math.Max(edge, Dot(c - core, dir));
            return edge;
        }

        private static int Dot(Vector3Int a, Vector3Int b) => a.x * b.x + a.y * b.y + a.z * b.z;

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Math.Min(i, (int)(_random01() * (i + 1)));
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // ---------------- 탐사선 ----------------

        /// <summary>보낼 수 없는 까닭 (보낼 수 있으면 null).</summary>
        public string ProbeBlocked(ExploreSite site)
        {
            if (site == null || site.Chapter != ChapterIndex)
                return "지난 신호임";
            if (site.Revealed)
                return "이미 밝힘";
            if (ProbeInFlight)
                return "탐사선이 날아가는 중";
            if (ProbeUsedThisRound)
                return "이번 신호의 탐사선은 이미 보냄";
            if (Sim.Resources.GetStock(ResourceType.Metal) < _config.ProbeMetal)
                return $"금속 {_config.ProbeMetal:0} 필요";
            return null;
        }

        public bool LaunchProbe(ExploreSite site)
        {
            if (ProbeBlocked(site) != null)
                return false;
            if (_config.ProbeMetal > 0f && !Sim.Resources.TrySpend(new[] { new ResourceAmount(ResourceType.Metal, _config.ProbeMetal) }))
                return false;
            _probeSite = _sites.IndexOf(site);
            _probeTimer = _config.ProbeSeconds;
            _probedChapter = ChapterIndex;
            Sim.Report($"탐사선 출발 · {MeteorShowerSystem.DirectionName(site.Direction)} · {_config.ProbeSeconds:0}초 뒤 도착", true);
            Changed?.Invoke();
            return true;
        }

        private void Reveal()
        {
            var site = ProbeTarget;
            _probeSite = -1;
            _probeTimer = 0f;
            if (site == null)
                return;
            site.Revealed = true;
            Sim.Report($"탐사선 도착: {ExplorationConfig.Name(site.Kind)} · {EffectOf(site)} · {_config.ReachRadius}칸 이내 도달 시 탐사 시작", true);
            Revealed?.Invoke(site); // 서사판: 장 표시 'probe'
            Changed?.Invoke();
        }

        // ---------------- 닿기 ----------------

        /// <summary>코어에 연결된 모듈에서 지점까지 가장 가까운 거리 (칸, 가로 · 세로 · 높이 중 큰 값).</summary>
        public int Distance(ExploreSite site)
        {
            int best = int.MaxValue;
            foreach (var m in Sim.Grid.Modules)
            {
                if (!Sim.Connectivity.IsActive(m))
                    continue;
                foreach (var c in m.Cells)
                {
                    var d = c - site.Cell;
                    best = Math.Min(best, Math.Max(Math.Abs(d.x), Math.Max(Math.Abs(d.y), Math.Abs(d.z))));
                }
            }
            return best;
        }

        public bool IsWithinReach(ExploreSite site) => ReachRule != null ? ReachRule(site) : Distance(site) <= _config.ReachRadius;

        private void CheckReach()
        {
            foreach (var site in _sites)
            {
                if (!site.Revealed)
                    continue;
                bool near = IsWithinReach(site);
                if (site.Kind == SiteKind.Asteroid || site.Kind == SiteKind.IceRock)
                {
                    if (site.InReach != near)
                    {
                        site.InReach = near;
                        if (site.Reached)
                            Sim.Report(near ? $"{ExplorationConfig.Name(site.Kind)} 탐사를 다시 시작합니다" : $"{ExplorationConfig.Name(site.Kind)} 연결이 끊겨 탐사가 멈췄습니다", near);
                        Changed?.Invoke();
                    }
                }
                if (site.Reached || !near)
                    continue;
                site.Reached = true;
                site.HazardTimer = _config.DebrisInterval;
                ApplyReach(site);
                Reached?.Invoke(site); // 서사판: 장 표시 'reach' · 난파선 기록 조각 · 기록 로그
                Changed?.Invoke();
            }
        }

        private void ApplyReach(ExploreSite site)
        {
            string name = ExplorationConfig.Name(site.Kind);
            switch (site.Kind)
            {
                case SiteKind.Wreck:
                    Fragments += _config.WreckFragments; // 판이 받아 씀 (서사판 = 봉인된 기록을 푸는 기록 조각, Reached에서)
                    Sim.Supply.SetCounts(Sim.Supply.ResearchPoints + _config.WreckResearchPoints, Sim.Supply.FreeRepairs);
                    Sim.Report($"{name} 탐사 시작 · 기록 조각 +{_config.WreckFragments} · 연구 포인트 +{_config.WreckResearchPoints}", true);
                    break;
                case SiteKind.DebrisField:
                    Sim.Resources.AddStock(ResourceType.Metal, _config.DebrisMetal);
                    Sim.Report($"{name} 탐사 시작 · 금속 +{_config.DebrisMetal:0} · 이제 {MeteorShowerSystem.DirectionName(site.Direction)}에서 파편이 날아옵니다", false);
                    break;
                default:
                    Sim.Report($"{name} 탐사 시작 · {EffectOf(site)}", true);
                    break;
            }
        }

        private void ApplyOngoing(float dt)
        {
            foreach (var site in _sites)
            {
                if (!site.Reached)
                    continue;
                switch (site.Kind)
                {
                    case SiteKind.Asteroid:
                        if (site.InReach)
                            Sim.Resources.AddStock(ResourceType.Metal, _config.AsteroidMetalPerSecond * dt);
                        break;
                    case SiteKind.IceRock:
                        if (site.InReach)
                            Sim.Resources.AddStock(ResourceType.Water, _config.IceWaterPerSecond * dt);
                        break;
                    case SiteKind.DebrisField:
                        site.HazardTimer -= dt;
                        if (site.HazardTimer <= 0f)
                        {
                            site.HazardTimer = _config.DebrisInterval;
                            FireDebris(site);
                        }
                        break;
                }
            }
        }

        private void FireDebris(ExploreSite site)
        {
            MeteorShowerSystem.FindFacing(Sim.Grid, Sim.Core, site.Direction, _facing, _facingCells, _faceCells);
            _targets.Clear();
            for (int i = 0; i < _config.DebrisHits && _facing.Count > 0; i++)
            {
                int k = Math.Min(_facing.Count - 1, (int)(_random01() * _facing.Count));
                _targets.Add(_facing[k]);
                _facing.RemoveAt(k);
            }
            if (_targets.Count > 0)
                Sim.ShowerMeteors(_targets, _random01, site.Direction, "파편 지대");
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public List<ExploreSite> Sites = new List<ExploreSite>();
            public int GeneratedChapter = -1;
            public int ProbeSite = -1;
            public float ProbeTimer;
            public int ProbedChapter = -1;
            public int Fragments;
            public int WreckLogs;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf) => JsonUtility.ToJson(new State
        {
            Sites = new List<ExploreSite>(_sites),
            GeneratedChapter = _generatedChapter,
            ProbeSite = _probeSite,
            ProbeTimer = _probeTimer,
            ProbedChapter = _probedChapter,
            Fragments = Fragments,
            WreckLogs = _wreckLogs,
        });

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            _sites.Clear();
            if (s.Sites != null)
                _sites.AddRange(s.Sites);
            _generatedChapter = s.GeneratedChapter;
            _probeSite = s.ProbeSite;
            _probeTimer = s.ProbeTimer;
            _probedChapter = s.ProbedChapter;
            Fragments = s.Fragments;
            _wreckLogs = s.WreckLogs;
            _reachCheck = 0f;
            Changed?.Invoke();
        }
    }
}
