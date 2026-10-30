using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>탐사 지점 하나 (장마다 신호로 잡힘).</summary>
    [Serializable]
    public sealed class ExploreSite
    {
        public SiteKind Kind;
        /// <summary>지점 칸 (정거장 격자 좌표).</summary>
        public Vector3Int Cell;
        /// <summary>정거장에서 본 지점 쪽 (가로 4방향).</summary>
        public Vector3Int Direction;
        public int Chapter;
        public bool Revealed;
        public bool Reached;
        /// <summary>자원 지점: 지금도 닿아 있음 (1초마다 다시 봄).</summary>
        public bool InReach;
        /// <summary>위험 지점: 다음 파편까지 (초).</summary>
        public float HazardTimer;
    }

    /// <summary>
    /// 13-5a 서사판 탐사 (REDESIGN V5, 사용자 결정 2026-10-18 D-6):
    /// - 장 데이터에 신호가 있고 센서 어레이가 가동 중이면 그 장의 신호(지점)를 만든다 — 정거장 끝에서 그 방향으로 6~10칸, 가로 4방향에 하나씩.
    /// - 한 장에 하나만 탐사선으로 밝힘 (금속 · 도착 시간). 밝히기 전에는 흐릿한 힌트만.
    /// - 밝힌 지점 근처(반경, 가로 · 세로 · 높이 각각)에 코어에 연결된 모듈이 들어가면 닿음 → 종류별 효과
    ///   (소행성 · 얼음 = 닿아 있는 동안 금속 · 물 / 난파선 = 기록 조각 + 연구 포인트 + 기록 로그 / 파편 지대 = 금속 + 그 뒤 그 방향 파편).
    /// - 장 목표 표시: 밝히면 'probe', 닿으면 'reach'. 지난 장 지점은 남아 효과를 이어감. 세이브.
    /// </summary>
    public sealed class ExplorationSystem : StationSystem
    {
        public const string SystemKey = "exploration";
        public const string ProbeFlag = "probe";
        public const string ReachFlag = "reach";

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
        /// <summary>난파선에서 찾은 기록 조각 수 (쓰는 쪽은 13-5b <see cref="RecordArchiveSystem"/>).</summary>
        public int Fragments { get; private set; }
        public bool ProbeInFlight => _probeSite >= 0;
        public float ProbeTimeLeft => _probeTimer;
        public ExploreSite ProbeTarget => _probeSite >= 0 && _probeSite < _sites.Count ? _sites[_probeSite] : null;

        private ChapterSystem Chapters => Sim.Chapters;
        private int ChapterIndex => Chapters != null ? Chapters.Index : 0;

        /// <summary>이번 장에 탐사선을 이미 보냈는지 (한 장 하나).</summary>
        public bool ProbeUsedThisChapter => _probedChapter == ChapterIndex;

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

        /// <summary>이 장에 신호가 있는지 (장 데이터).</summary>
        public bool ChapterHasSignals => Chapters != null && Chapters.Current.Signals.Count > 0;

        /// <summary>이번 장 지점들.</summary>
        public void CurrentSites(List<ExploreSite> result)
        {
            result.Clear();
            foreach (var s in _sites)
                if (s.Chapter == ChapterIndex)
                    result.Add(s);
        }

        // ---------------- 진행 ----------------

        public override void Tick(float dt)
        {
            if (Chapters == null || Sim.Session.IsGameOver)
                return;
            if (_generatedChapter != ChapterIndex && ChapterHasSignals && SensorActive)
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
            var data = Chapters.Current;
            var kinds = new List<SiteKind>(data.Signals);
            Shuffle(kinds);
            var dirs = new List<Vector3Int>(Horizontal);
            Shuffle(dirs);
            var core = Sim.Core != null ? Sim.Core.Origin : Vector3Int.zero;
            for (int i = 0; i < kinds.Count; i++)
            {
                var dir = dirs[i % dirs.Count];
                int edge = Edge(dir, core);
                int dist = data.SignalDistanceMin + Math.Min(data.SignalDistanceMax - data.SignalDistanceMin, (int)(_random01() * (data.SignalDistanceMax - data.SignalDistanceMin + 1)));
                var side = new Vector3Int(Math.Abs(dir.z), 0, Math.Abs(dir.x)); // 옆으로 조금
                int lateral = (int)(_random01() * 5f) - 2;
                int height = (int)(_random01() * 3f) - 1;
                var cell = core + dir * (edge + dist) + side * lateral + Vector3Int.up * height;
                _sites.Add(new ExploreSite { Kind = kinds[i], Cell = cell, Direction = dir, Chapter = ChapterIndex });
            }
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
                return "이번 장 신호가 아님";
            if (site.Revealed)
                return "이미 밝힘";
            if (ProbeInFlight)
                return "탐사선이 날아가는 중";
            if (ProbeUsedThisChapter)
                return "이번 장 탐사선은 이미 보냄";
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
            Sim.Report($"탐사선 도착: {ExplorationConfig.Name(site.Kind)} · {_config.Effect(site.Kind)} · {_config.ReachRadius}칸 이내 도달 시 탐사 시작", true);
            Chapters?.SetFlag(ProbeFlag);
            Revealed?.Invoke(site);
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

        public bool IsWithinReach(ExploreSite site) => Distance(site) <= _config.ReachRadius;

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
                            Sim.Report(near ? $"{ExplorationConfig.Name(site.Kind)} 탐사를 다시 시작합니다" : $"{ExplorationConfig.Name(site.Kind)}와 연결이 끊겨 탐사가 멈췄습니다", near);
                        Changed?.Invoke();
                    }
                }
                if (site.Reached || !near)
                    continue;
                site.Reached = true;
                site.HazardTimer = _config.DebrisInterval;
                ApplyReach(site);
                Chapters?.SetFlag(ReachFlag);
                Reached?.Invoke(site);
                Changed?.Invoke();
            }
        }

        private void ApplyReach(ExploreSite site)
        {
            string name = ExplorationConfig.Name(site.Kind);
            switch (site.Kind)
            {
                case SiteKind.Wreck:
                    Fragments += _config.WreckFragments;
                    Sim.GetSystem<RecordArchiveSystem>()?.AddFragments(_config.WreckFragments); // 13-5b 봉인된 기록을 푸는 데 씀 (알림은 아래 한 줄)
                    Sim.Supply.SetCounts(Sim.Supply.ResearchPoints + _config.WreckResearchPoints, Sim.Supply.FreeRepairs);
                    Sim.Report($"{name} 탐사 시작 · 기록 조각 +{_config.WreckFragments} · 연구 포인트 +{_config.WreckResearchPoints}", true);
                    var logs = _config.WreckLogs;
                    if (logs.Count > 0)
                    {
                        string text = logs[Math.Min(_wreckLogs, logs.Count - 1)];
                        _wreckLogs++;
                        Sim.GetSystem<CorePersonalitySystem>()?.Log("기록 조각 · " + text);
                    }
                    break;
                case SiteKind.DebrisField:
                    Sim.Resources.AddStock(ResourceType.Metal, _config.DebrisMetal);
                    Sim.Report($"{name} 탐사 시작 · 금속 +{_config.DebrisMetal:0} · 이제 {MeteorShowerSystem.DirectionName(site.Direction)}에서 파편이 날아옵니다", false);
                    break;
                default:
                    Sim.Report($"{name} 탐사 시작 · {_config.Effect(site.Kind)}", true);
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
