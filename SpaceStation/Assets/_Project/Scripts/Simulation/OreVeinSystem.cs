using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 타이쿤판(엔드리스) 광맥 — CONNECT_1121 1번 (사용자 결정 2026-11-21 · 11-27, BALANCE 50번). 공통 탐사 틀(<see cref="ExplorationSystem"/>)의 신호를 정하는 쪽.
    /// - 중형(<see cref="OreVeinConfig.StartGrade"/>)부터 제자리 채굴 도킹은 캔 양이 쌓일수록 생산이 줄어 바닥값(40%)에서 멈춤 — 도킹마다 따로,
    ///   같은 자리 재건축 · 이동은 이어받고, 새로 지은 도킹은 지금 제자리 도킹들의 평균 고갈에서 시작 (철거 · 새로 짓기로 되살리지 못하게, 사용자 결정 2026-11-27).
    /// - 광맥 칸에 면이 맞닿은 도킹은 원래의 2배를 캐고 그만큼 광맥 매장량이 줄어듦. 다 캐면 붙어 있던 도킹은 바닥값.
    /// - 신호: 중형부터 등급이 오를 때 + 광맥 하나를 다 캔 뒤 <see cref="OreVeinConfig.RespawnDelay"/>초. 위치는 바로 보임(<see cref="ExplorationSystem.AutoReveal"/>),
    ///   남은 매장량 숫자는 감지 모듈(센서 어레이)이 가동 중일 때만.
    /// 생산 배율은 공통 고리 <see cref="StationSimulation.ModuleProductionScale"/>로 건다 (자원 틱 · 모듈 효율).
    /// </summary>
    public sealed class OreVeinSystem : StationSystem, IExploreSignals
    {
        public const string SystemKey = "oreveins";

        private static readonly Vector3Int[] SixDirections =
        {
            Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1), Vector3Int.up, Vector3Int.down,
        };

        private const float RebuildWindow = 10f; // 지운 도킹 자리를 이 시간(초) 안에 다시 쓰면 고갈을 이어받음 (재건축 = 지우고 같은 칸에 새로)

        private readonly OreVeinConfig _config;
        private readonly Func<float> _random01;
        private readonly List<SiteKind> _kinds = new List<SiteKind>();
        private readonly Dictionary<ModuleInstance, float> _mined = new Dictionary<ModuleInstance, float>();
        private readonly Dictionary<ModuleInstance, ExploreSite> _onVein = new Dictionary<ModuleInstance, ExploreSite>();
        private readonly Dictionary<Vector3Int, (float mined, float time)> _removed = new Dictionary<Vector3Int, (float, float)>();
        private readonly List<ModuleInstance> _docks = new List<ModuleInstance>();
        private ExplorationSystem _explore;
        private int _round;
        private int _roundGrade = -1;
        private int _signalGrade = -1;
        private float _respawn = -1f;
        private float _veinCheck;
        private float _baseMetal;

        /// <summary>새 광맥 신호가 잡힘 (묶음 번호).</summary>
        public event Action<int> SignalsFound;
        /// <summary>광맥 하나를 다 캠.</summary>
        public event Action<ExploreSite> VeinExhausted;

        private OreVeinSystem(OreVeinConfig config, Func<float> random01)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _random01 = random01 ?? throw new ArgumentNullException(nameof(random01));
        }

        /// <summary>시뮬레이션에 붙임 — 탐사 틀이 없으면 만들고, 신호 · 닿음 · 효과 문구 · 생산 배율을 이쪽으로.</summary>
        public static OreVeinSystem AttachTo(StationSimulation sim, OreVeinConfig config, Func<float> random01)
        {
            if (config == null || config.Exploration == null || config.Dock == null)
                return null;
            var explore = sim.GetSystem<ExplorationSystem>() ?? sim.AddSystem(new ExplorationSystem(config.Exploration, random01), TickStepNames.Progression);
            var system = sim.AddSystem(new OreVeinSystem(config, random01), TickStepNames.Progression);
            system._explore = explore;
            explore.Signals = system;
            explore.AutoReveal = true;
            explore.ReachRule = system.Touches;
            explore.EffectText = system.Describe;
            sim.ModuleProductionScale = system.Scale;
            return system;
        }

        public override string Key => SystemKey;
        public OreVeinConfig Config => _config;
        /// <summary>다 캔 광맥 수 (측정 · 화면).</summary>
        public int ExhaustedCount { get; private set; }
        public ExplorationSystem Exploration => _explore;

        /// <summary>고갈 · 광맥이 켜져 있음 (중형에 한 번이라도 오름).</summary>
        public bool Active => Sim != null && Sim.Progression.HighestGradeIndex >= _config.StartGrade;

        /// <summary>남은 매장량을 숫자로 볼 수 있음 (감지 모듈 가동 중 — 없으면 늘).</summary>
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

        protected override void OnAttached()
        {
            _baseMetal = 0f;
            foreach (var a in _config.Dock.Production)
                if (a.Type == ResourceType.Metal)
                    _baseMetal += a.Amount;
            foreach (var m in Sim.Grid.Modules)
                if (m.Data == _config.Dock)
                    _docks.Add(m);
        }

        // ---------------- IExploreSignals ----------------

        public int Round => _round;
        public IReadOnlyList<SiteKind> Kinds => _kinds;
        public int DistanceMin => _config.DistanceMinFor(Math.Max(_config.StartGrade, _signalGrade));
        public int DistanceMax => _config.DistanceMaxFor(Math.Max(_config.StartGrade, _signalGrade));
        public IReadOnlyList<Vector3Int> Directions => SixDirections;
        public string RoundName(int round) => $"광맥 신호 {round}";

        // ---------------- 도킹 ----------------

        /// <summary>도킹의 생산 배율 (공통 고리). 도킹이 아니거나 아직 중형 전이면 1.</summary>
        public float Scale(ModuleInstance module)
        {
            if (module == null || module.Data != _config.Dock || !Active)
                return 1f;
            if (_onVein.TryGetValue(module, out var site) && site.Reserve > 0f)
                return _config.VeinMultiplier;
            return InPlaceScale(module);
        }

        /// <summary>제자리 생산 비율 (1 = 처음, <see cref="OreVeinConfig.Floor"/> = 바닥).</summary>
        public float InPlaceScale(ModuleInstance module)
        {
            float mined = _mined.TryGetValue(module, out var v) ? v : AverageMined();
            float t = Mathf.Clamp01(mined / _config.DepletionAmount);
            return Mathf.Max(_config.Floor, 1f - (1f - _config.Floor) * t);
        }

        /// <summary>이 도킹이 캐고 있는 광맥 (없으면 null).</summary>
        public ExploreSite VeinOf(ModuleInstance module) =>
            module != null && _onVein.TryGetValue(module, out var site) && site.Reserve > 0f ? site : null;

        /// <summary>도킹 정보 한 줄 (선택 창 등). 도킹이 아니면 null.</summary>
        public string DockInfo(ModuleInstance module)
        {
            if (module == null || module.Data != _config.Dock)
                return null;
            if (!Active)
                return $"제자리 채굴 · 중형부터 캔 양이 쌓이면 생산이 줄어듦 (바닥 {_config.Floor * 100f:0}%)";
            var vein = VeinOf(module);
            if (vein != null)
                return SensorActive
                    ? $"광맥 채굴 · 생산 {_config.VeinMultiplier * 100f:0}% · 남은 매장량 {vein.Reserve:N0}"
                    : $"광맥 채굴 · 생산 {_config.VeinMultiplier * 100f:0}% · 남은 매장량은 센서 어레이가 있어야 보임";
            return $"제자리 채굴 · 생산 {InPlaceScale(module) * 100f:0}% (캔 양이 쌓일수록 줄어 바닥 {_config.Floor * 100f:0}%) · 광맥에 맞닿게 옮기면 {_config.VeinMultiplier * 100f:0}%";
        }

        /// <summary>측정용: 지금 광맥을 캐는 도킹 수 · 제자리 도킹 생산 비율 평균 (제자리 도킹이 없으면 1).</summary>
        public void DockSummary(out int onVein, out float inPlaceAverage)
        {
            onVein = 0;
            float sum = 0f;
            int n = 0;
            foreach (var m in _docks)
            {
                if (VeinOf(m) != null)
                    onVein++;
                else
                {
                    sum += InPlaceScale(m);
                    n++;
                }
            }
            inPlaceAverage = n > 0 ? sum / n : 1f;
        }

        /// <summary>광맥 칸에 면이 맞닿은 가동 중 도킹이 있는지 (탐사 틀의 닿음 판정).</summary>
        public bool Touches(ExploreSite site)
        {
            foreach (var m in _docks)
                if (Sim.Connectivity.IsActive(m) && Adjacent(m, site.Cell))
                    return true;
            return false;
        }

        private static bool Adjacent(ModuleInstance m, Vector3Int cell)
        {
            foreach (var c in m.Cells)
            {
                var d = c - cell;
                if (Math.Abs(d.x) + Math.Abs(d.y) + Math.Abs(d.z) == 1)
                    return true;
            }
            return false;
        }

        /// <summary>탐사 틀의 효과 문구 (닿을 때 알림 · 탐사 창).</summary>
        public string Describe(ExploreSite site)
        {
            if (site.Reserve == 0f)
                return "다 캠";
            string reserve = SensorActive && site.Reserve > 0f ? $" · 매장량 {site.Reserve:N0}" : "";
            return $"맞닿은 채굴 도킹 생산 {_config.VeinMultiplier * 100f:0}%{reserve}";
        }

        private float AverageMined()
        {
            float sum = 0f;
            int n = 0;
            foreach (var kv in _mined)
            {
                if (_onVein.ContainsKey(kv.Key))
                    continue;
                sum += kv.Value;
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        // ---------------- 진행 ----------------

        public override void Tick(float dt)
        {
            if (Sim.Session.IsGameOver)
                return;
            UpdateRound(dt);
            AssignReserves();
            if (!Active || dt <= 0f)
                return;
            _veinCheck -= dt;
            if (_veinCheck <= 0f)
            {
                _veinCheck = 1f;
                RefreshVeins();
            }
            foreach (var m in _docks)
            {
                if (!Sim.Connectivity.IsActive(m))
                    continue;
                EnsureTracked(m);
                float produced = _baseMetal * Sim.GetModuleEfficiency(m) * dt; // 효율에 이 시스템의 배율도 들어 있음
                if (produced <= 0f)
                    continue;
                if (_onVein.TryGetValue(m, out var site) && site.Reserve > 0f)
                {
                    site.Reserve -= produced;
                    if (site.Reserve <= 0f)
                        Exhaust(site);
                }
                else
                {
                    _mined[m] = Math.Min(_config.DepletionAmount, _mined[m] + produced);
                }
            }
        }

        private void UpdateRound(float dt)
        {
            int grade = Sim.Progression.HighestGradeIndex;
            if (grade >= _config.StartGrade && grade > _roundGrade)
            {
                _roundGrade = grade;
                NewRound(grade);
            }
            if (_respawn >= 0f && dt > 0f)
            {
                _respawn -= dt;
                if (_respawn < 0f)
                {
                    _respawn = -1f;
                    NewRound(Math.Max(_config.StartGrade, Sim.Progression.GradeIndex));
                }
            }
        }

        private void NewRound(int grade)
        {
            _round++;
            _signalGrade = grade;
            _kinds.Clear();
            int span = _config.VeinsMax - _config.VeinsMin + 1;
            int count = _config.VeinsMin + Math.Min(span - 1, (int)(_random01() * span));
            for (int i = 0; i < count; i++)
                _kinds.Add(SiteKind.Asteroid);
        }

        /// <summary>탐사 틀이 막 만든 광맥에 매장량을 정하고 알림.</summary>
        private void AssignReserves()
        {
            if (_explore == null)
                return;
            int found = 0;
            var dirs = new List<string>();
            foreach (var site in _explore.Sites)
            {
                if (site.Kind != SiteKind.Asteroid || site.Reserve >= 0f)
                    continue;
                site.Reserve = _config.ReserveFor(Math.Max(_config.StartGrade, _signalGrade));
                found++;
                dirs.Add(MeteorShowerSystem.DirectionName(site.Direction));
            }
            if (found == 0)
                return;
            Sim.Report($"새 광맥 신호 {found}개 · {string.Join(" · ", dirs)} · 채굴 도킹을 광맥에 맞닿게 지으면 {_config.VeinMultiplier * 100f:0}% 생산", true);
            SignalsFound?.Invoke(_round);
            _veinCheck = 0f;
        }

        private void RefreshVeins()
        {
            _onVein.Clear();
            foreach (var site in _explore.Sites)
            {
                if (site.Kind != SiteKind.Asteroid || site.Reserve <= 0f)
                    continue;
                foreach (var m in _docks)
                    if (!_onVein.ContainsKey(m) && Sim.Connectivity.IsActive(m) && Adjacent(m, site.Cell))
                        _onVein[m] = site;
            }
        }

        private void Exhaust(ExploreSite site)
        {
            site.Reserve = 0f;
            ExhaustedCount++;
            int docks = 0;
            foreach (var m in _docks)
            {
                if (_onVein.TryGetValue(m, out var s) && s == site)
                {
                    _mined[m] = _config.DepletionAmount; // 다 캔 광맥에 붙어 있던 도킹 = 바닥값
                    _onVein.Remove(m);
                    docks++;
                }
            }
            if (_respawn < 0f)
                _respawn = _config.RespawnDelay;
            Sim.Report($"{MeteorShowerSystem.DirectionName(site.Direction)} 광맥을 다 캤습니다 · 붙어 있던 도킹 {docks}개가 바닥 생산 ({_config.Floor * 100f:0}%) · 새 신호는 {_config.RespawnDelay / 60f:0.#}분 뒤", false);
            VeinExhausted?.Invoke(site);
        }

        private void EnsureTracked(ModuleInstance m)
        {
            if (_mined.ContainsKey(m))
                return;
            float start;
            if (_removed.TryGetValue(m.Origin, out var old) && Sim.ElapsedSeconds - old.time <= RebuildWindow)
            {
                start = old.mined; // 같은 자리 재건축 = 이어받음
                _removed.Remove(m.Origin);
            }
            else
            {
                start = AverageMined(); // 새 도킹 = 지금 제자리 도킹들의 평균 (철거 · 새로 짓기로 되살리지 못하게)
            }
            _mined[m] = start;
        }

        public override void OnModulePlaced(ModuleInstance module)
        {
            if (module?.Data == _config.Dock && !_docks.Contains(module))
                _docks.Add(module);
            _veinCheck = 0f;
        }

        public override void OnModuleRemoved(ModuleInstance module)
        {
            if (module?.Data != _config.Dock)
                return;
            _docks.Remove(module);
            _onVein.Remove(module);
            if (_mined.TryGetValue(module, out var mined))
            {
                _removed[module.Origin] = (mined, Sim.ElapsedSeconds);
                _mined.Remove(module);
            }
            if (_removed.Count > 32) // 오래된 자리 정리
            {
                var old = new List<Vector3Int>();
                foreach (var kv in _removed)
                    if (Sim.ElapsedSeconds - kv.Value.time > RebuildWindow)
                        old.Add(kv.Key);
                foreach (var k in old)
                    _removed.Remove(k);
            }
        }

        public override void OnModuleMoved(ModuleInstance module) => _veinCheck = 0f;

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public int Round;
            public int RoundGrade = -1;
            public int SignalGrade = -1;
            public float Respawn = -1f;
            public int KindCount;
            public List<int> Docks = new List<int>();
            public List<float> Mined = new List<float>();
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            var s = new State { Round = _round, RoundGrade = _roundGrade, SignalGrade = _signalGrade, Respawn = _respawn, KindCount = _kinds.Count };
            foreach (var kv in _mined)
            {
                int i = indexOf(kv.Key);
                if (i < 0)
                    continue;
                s.Docks.Add(i);
                s.Mined.Add(kv.Value);
            }
            return JsonUtility.ToJson(s);
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            _round = s.Round;
            _roundGrade = s.RoundGrade;
            _signalGrade = s.SignalGrade;
            _respawn = s.Respawn;
            _kinds.Clear();
            for (int i = 0; i < s.KindCount; i++)
                _kinds.Add(SiteKind.Asteroid);
            _mined.Clear();
            _docks.Clear();
            foreach (var m in Sim.Grid.Modules)
                if (m.Data == _config.Dock)
                    _docks.Add(m);
            for (int i = 0; i < s.Docks.Count && i < s.Mined.Count; i++)
            {
                var m = moduleAt(s.Docks[i]);
                if (m != null)
                    _mined[m] = s.Mined[i];
            }
            _veinCheck = 0f;
        }
    }
}
