using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-3c 도시급 핵심 규칙 — 유성우 방향 예보 (사용자 결정 2026-10-13, PHASESET 5번 C-2, BALANCE 34번).
    /// 도시급부터 무작위 간격으로: 예보(오는 방향 = 6방향 중 하나, 기본 60초) → 여러 파(기본 3파 · 4초 간격)로 운석.
    /// 맞을 수 있는 모듈 = 그 방향에서 보이는 앞면(그 칸에서 오는 방향으로 쭉 가는 줄에 다른 모듈이 없음)만 → 앞을 가리면 뒤쪽은 안전.
    /// 판정(포탑 격추 · 실드 빗겨냄 · 튕김 · 피해)은 일반 운석과 같고, 난수는 유성우 전용(일반 이벤트 난수 순서를 바꾸지 않음).
    /// </summary>
    public sealed class MeteorShowerSystem : StationSystem
    {
        public const string SystemKey = "meteorShower";

        public enum ShowerPhase
        {
            Idle,
            Warning,
            Falling,
        }

        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1), Vector3Int.up, Vector3Int.down,
        };

        private readonly Func<float> _random01;

        /// <summary>13-2 공통 고리: 유성우 운석 수 배율 (운석 1회 개수에 곱함). null이면 BalanceConfig 값 (타이쿤판은 노출 면에 따라).</summary>
        public Func<float> HitsMultiplier { get; set; }
        private readonly List<ModuleInstance> _facing = new List<ModuleInstance>();
        private readonly Dictionary<ModuleInstance, int> _facingCells = new Dictionary<ModuleInstance, int>();
        private readonly List<Vector3Int> _faceCells = new List<Vector3Int>();
        private readonly List<ModuleInstance> _pool = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _targets = new List<ModuleInstance>();
        private bool _armed;
        private float _timer;
        private bool _facingDirty = true;

        /// <summary>단계 · 방향 · 남은 시간 · 맞을 면이 바뀜 (UI).</summary>
        public event Action Changed;

        public override string Key => SystemKey;
        public ShowerPhase Phase { get; private set; }
        /// <summary>운석이 오는 쪽 (정거장에서 본 방향). Idle이면 의미 없음.</summary>
        public Vector3Int Direction { get; private set; }
        /// <summary>Warning: 첫 파까지 / Falling: 다음 파까지 / Idle: 다음 예보까지(도시급 전이면 멈춤).</summary>
        public float TimeLeft => _timer;
        /// <summary>지금까지 떨어진 파 수.</summary>
        public int WavesDone { get; private set; }
        public int Waves => IsRaid ? _raidWaves : _currentWaves > 0 ? _currentWaves : Sim.Balance.ShowerWaves;
        /// <summary>
        /// 12-3c 지구급 습격 포격 (방문 세력, <see cref="ContactSystem"/>) — 판정 · 표시는 유성우와 같고 이름만 다름.
        /// M-20: 판이 부르는 특별 유성우(서사판 장 절정)도 같은 틀 (<see cref="BeginRaid"/>의 label) — 평소 유성우 시계를 건드리지 않음.
        /// </summary>
        public bool IsRaid { get; private set; }
        /// <summary>화면에 쓰는 이름 ("유성우" / "포격" / 특별 유성우 이름).</summary>
        public string Label => IsRaid ? _raidLabel : "유성우";
        private int _raidWaves;
        private string _raidLabel = "포격";
        /// <summary>M-20: 이번 평소 유성우의 파 수 (0 = 밸런스 값), 다음 한 번만 쓸 파 수 (<see cref="ScheduleNext"/>).</summary>
        private int _currentWaves, _nextWaves;
        private float _resumeTimer;
        /// <summary>이번 유성우 한 파의 운석 수.</summary>
        public int HitsPerWave { get; private set; }
        public bool Active => Phase != ShowerPhase.Idle;

        /// <param name="random01">유성우 전용 [0, 1) 난수 (null이면 시스템 난수).</param>
        public MeteorShowerSystem(Func<float> random01 = null)
        {
            if (random01 == null)
            {
                var rng = new System.Random();
                random01 = () => (float)rng.NextDouble();
            }
            _random01 = random01;
        }

        /// <summary>오는 방향에서 보이는 앞면 모듈 (코어 제외, 파손 포함). 경보 · 유성우 중에만 채워짐.</summary>
        public IReadOnlyList<ModuleInstance> FacingModules
        {
            get
            {
                RefreshFacing();
                return _facing;
            }
        }

        /// <summary>맞을 수 있는 칸 (그 칸의 오는 방향 면이 표적, UI 면 표시).</summary>
        public IReadOnlyList<Vector3Int> FacingCells
        {
            get
            {
                RefreshFacing();
                return _faceCells;
            }
        }

        public static string DirectionName(Vector3Int d)
        {
            if (d == Vector3Int.right) return "동쪽(+X)";
            if (d == Vector3Int.left) return "서쪽(-X)";
            if (d.z > 0) return "북쪽(+Z)";
            if (d.z < 0) return "남쪽(-Z)";
            if (d == Vector3Int.up) return "위쪽";
            if (d == Vector3Int.down) return "아래쪽";
            return "?";
        }

        public override void Tick(float dt)
        {
            if (dt <= 0f || Sim.Session.IsGameOver)
                return;
            switch (Phase)
            {
                case ShowerPhase.Idle:
                    if (Sim.Events.Held || !Sim.RuleOpen(StationRule.MeteorShower, Sim.Progression.GradeIndex >= Sim.Balance.ShowerMinGrade))
                        return; // 튜토리얼 · 도시급(13-1 규칙을 정하는 쪽이 있으면 그 규칙) 전에는 시계가 멈춤
                    if (!_armed)
                    {
                        _armed = true;
                        _timer = NextInterval();
                        return;
                    }
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        IsRaid = false;
                        BeginWarning(Sim.Balance.ShowerWarningSeconds + Sim.SensorWarningBonus); // 12-5 센서 어레이
                    }
                    break;
                case ShowerPhase.Warning:
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        Phase = ShowerPhase.Falling;
                        _timer = 0f;
                        FireDueWaves();
                    }
                    else
                        Changed?.Invoke();
                    break;
                case ShowerPhase.Falling:
                    _timer -= dt;
                    FireDueWaves();
                    break;
            }
        }

        /// <summary>
        /// 예보 시작 (확인용 에디터 메뉴도 씀). 방향 · 운석 수를 정하고 알림.
        /// </summary>
        public void BeginWarning(float warningSeconds)
        {
            Direction = PickDirection(Vector3Int.zero);
            var grade = Sim.EventScale; // 13-2 규모를 정하는 쪽이 있으면 그쪽
            int span = grade.MeteorHitsMax - grade.MeteorHitsMin + 1;
            int hits = grade.MeteorHitsMin + Math.Min(span - 1, (int)(_random01() * span));
            float multiplier = HitsMultiplier != null ? HitsMultiplier() : Sim.Balance.ShowerHitsMultiplier; // 13-2 규모를 정하는 쪽이 있으면 그쪽
            HitsPerWave = Math.Max(1, (int)Math.Ceiling(hits * multiplier / Sim.Balance.ShowerWaves)); // 파당 크기는 늘 보통 파 수 기준
            _currentWaves = _nextWaves; // M-20: 앞당긴 한 번만 다른 파 수
            _nextWaves = 0;
            WavesDone = 0;
            Phase = ShowerPhase.Warning;
            _timer = Math.Max(0.01f, warningSeconds);
            _facingDirty = true;
            Sim.Report($"유성우 예보: {DirectionName(Direction)}에서 {_timer:0}초 뒤 {Waves}파 · 붉은 면이 맞습니다 (가리거나 실드 · 포탑으로 보강)", false);
            Changed?.Invoke();
        }

        /// <summary>
        /// 12-3c 지구급 습격 포격: 유성우와 같은 예보 · 앞면 판정으로 <paramref name="waves"/>파 × <paramref name="hitsPerWave"/>발.
        /// 유성우가 진행 중이면 시작하지 않음 (false — 동시에 일어나지 않게 부르는 쪽이 다시 시도).
        /// M-20: <paramref name="label"/> = 화면 이름(서사판 절정 "큰 유성우" 등), <paramref name="avoid"/> = 이 방향은 고르지 않음 (0이면 아무 방향).
        /// </summary>
        public bool BeginRaid(float warningSeconds, int waves, int hitsPerWave, string source, string label = "포격", Vector3Int avoid = default)
        {
            if (Active)
                return false;
            _resumeTimer = _armed ? _timer : NextInterval();
            _armed = true;
            IsRaid = true;
            _raidLabel = string.IsNullOrEmpty(label) ? "포격" : label;
            _raidWaves = Math.Max(1, waves);
            Direction = PickDirection(avoid);
            HitsPerWave = Math.Max(1, hitsPerWave);
            WavesDone = 0;
            Phase = ShowerPhase.Warning;
            _timer = Math.Max(0.01f, warningSeconds + Sim.SensorWarningBonus); // 12-5 센서 어레이
            _facingDirty = true;
            string from = string.IsNullOrEmpty(source) ? "" : source + " ";
            Sim.Report($"{from}{_raidLabel} 예보: {DirectionName(Direction)}에서 {_timer:0}초 뒤 {_raidWaves}파 · 붉은 면이 맞습니다 (가리거나 실드 · 포탑으로 보강)", false);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// M-20 판 규칙 (서사판 2장 첫 유성우): 다음 평소 유성우를 <paramref name="seconds"/> 뒤로 맞추고, <paramref name="waves"/> &gt; 0이면 그 한 번만 파 수를 바꿈.
        /// 유성우 · 포격이 진행 중이면 false. 규칙이 아직 닫혀 있으면 시계가 멈춰 있다가 열린 뒤 남은 시간부터.
        /// </summary>
        public bool ScheduleNext(float seconds, int waves = 0)
        {
            if (Active)
                return false;
            _armed = true;
            _timer = Math.Max(0.01f, seconds);
            _nextWaves = Math.Max(0, waves);
            Changed?.Invoke();
            return true;
        }

        /// <summary>M-20 판 규칙: 예보 중인 평소 유성우를 거둠 (서사판 장 절정이 대신 옴). 다음 평소 유성우는 보통 간격 뒤. 포격 · 낙하 중이면 false.</summary>
        public bool CancelWarning()
        {
            if (Phase != ShowerPhase.Warning || IsRaid)
                return false;
            Phase = ShowerPhase.Idle;
            _currentWaves = 0;
            _timer = NextInterval();
            _facingDirty = true;
            Changed?.Invoke();
            return true;
        }

        /// <summary>6방향 중 하나 (avoid는 빼고). avoid가 0이면 예전과 같은 난수 사용 (결과가 바뀌지 않음).</summary>
        private Vector3Int PickDirection(Vector3Int avoid)
        {
            int n = Array.IndexOf(Directions, avoid) >= 0 ? Directions.Length - 1 : Directions.Length;
            int pick = Math.Min(n - 1, (int)(_random01() * n));
            foreach (var d in Directions)
            {
                if (d == avoid)
                    continue;
                if (pick-- == 0)
                    return d;
            }
            return Directions[0];
        }

        private void FireDueWaves()
        {
            while (Phase == ShowerPhase.Falling && _timer <= 0f)
            {
                FireWave();
                WavesDone++;
                if (WavesDone >= Waves)
                {
                    Phase = ShowerPhase.Idle;
                    _facingDirty = true;
                    if (IsRaid)
                    {
                        string label = Label;
                        IsRaid = false;
                        _timer = _resumeTimer; // 포격은 유성우 시계를 건드리지 않음
                        Sim.Report($"{label}{(HasFinalConsonant(label) ? "이" : "가")} 끝났습니다", true);
                    }
                    else
                    {
                        _currentWaves = 0;
                        _timer = NextInterval();
                        Sim.Report("유성우가 지나갔습니다", true);
                    }
                    break;
                }
                _timer += Math.Max(0.01f, Sim.Balance.ShowerWaveInterval);
            }
            Changed?.Invoke();
        }

        private void FireWave()
        {
            RefreshFacing();
            _pool.Clear();
            foreach (var m in _facing)
            {
                if (!Sim.Damage.IsDamaged(m))
                    _pool.Add(m);
            }
            _targets.Clear();
            while (_targets.Count < HitsPerWave && _pool.Count > 0)
            {
                // 가중치 = 보이는 칸 수 × 모듈 배율 (8-5 장갑 격벽은 미끼라 더 잘 맞음)
                float total = 0f;
                foreach (var m in _pool)
                    total += Weight(m);
                float roll = _random01() * total;
                int pick = _pool.Count - 1;
                for (int i = 0; i < _pool.Count; i++)
                {
                    float w = Weight(_pool[i]);
                    if (roll < w)
                    {
                        pick = i;
                        break;
                    }
                    roll -= w;
                }
                _targets.Add(_pool[pick]);
                _pool.RemoveAt(pick);
            }
            Sim.ShowerMeteors(_targets, _random01, Direction, $"{Label} {WavesDone + 1}/{Waves}파");
            _facingDirty = true; // 파괴로 모양이 바뀌었을 수 있음
        }

        private float Weight(ModuleInstance m)
            => (_facingCells.TryGetValue(m, out int n) ? n : 1) * (m.Data != null ? m.Data.MeteorWeightMultiplier : 1f);

        private float NextInterval()
            => Sim.Balance.ShowerIntervalMin + _random01() * (Sim.Balance.ShowerIntervalMax - Sim.Balance.ShowerIntervalMin);

        /// <summary>한글 마지막 글자에 받침이 있는지 (조사 이/가).</summary>
        private static bool HasFinalConsonant(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            char c = text[text.Length - 1];
            return c >= '가' && c <= '힣' && (c - '가') % 28 != 0;
        }

        // ---------------- 앞면 판정 ----------------

        private void RefreshFacing()
        {
            if (!_facingDirty)
                return;
            _facingDirty = false;
            FindFacing(Sim.Grid, Sim.Core, Active ? Direction : Vector3Int.zero, _facing, _facingCells, _faceCells);
        }

        /// <summary>
        /// dir 쪽에서 보이는 앞면: 모듈 칸에서 dir로 쭉 가는 줄(정거장 경계까지)에 다른 칸이 없으면 그 칸이 보임.
        /// 보이는 칸이 하나라도 있는 모듈(코어 제외)과 칸 수, 보이는 칸 목록. dir이 0이면 모두 비움.
        /// </summary>
        public static void FindFacing(StationGrid grid, ModuleInstance core, Vector3Int dir,
            List<ModuleInstance> modules, Dictionary<ModuleInstance, int> cellCount, List<Vector3Int> cells)
        {
            modules.Clear();
            cellCount.Clear();
            cells.Clear();
            if (dir == Vector3Int.zero)
                return;
            bool any = false;
            Vector3Int min = Vector3Int.zero, max = Vector3Int.zero;
            foreach (var m in grid.Modules)
            {
                foreach (var c in m.Cells)
                {
                    if (!any)
                    {
                        min = max = c;
                        any = true;
                    }
                    min = Vector3Int.Min(min, c);
                    max = Vector3Int.Max(max, c);
                }
            }
            foreach (var m in grid.Modules)
            {
                if (m == core)
                    continue;
                int n = 0;
                foreach (var c in m.Cells)
                {
                    bool blocked = false;
                    for (var p = c + dir; p.x >= min.x && p.y >= min.y && p.z >= min.z && p.x <= max.x && p.y <= max.y && p.z <= max.z; p += dir)
                    {
                        if (grid.IsOccupied(p))
                        {
                            blocked = true;
                            break;
                        }
                    }
                    if (blocked)
                        continue;
                    n++;
                    cells.Add(c);
                }
                if (n > 0)
                {
                    modules.Add(m);
                    cellCount[m] = n;
                }
            }
        }

        public override void OnModulePlaced(ModuleInstance module) => MarkFacingDirty();
        public override void OnModuleRemoved(ModuleInstance module) => MarkFacingDirty();
        public override void OnModuleMoved(ModuleInstance module) => MarkFacingDirty();

        private void MarkFacingDirty()
        {
            if (!Active)
                return;
            _facingDirty = true;
            Changed?.Invoke();
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public bool Armed;
            public int Phase;
            public Vector3Int Direction;
            public float Timer;
            public int WavesDone;
            public int HitsPerWave;
            public bool Raid;
            public int RaidWaves;
            public float ResumeTimer;
            public string RaidLabel;
            public int CurrentWaves;
            public int NextWaves;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            if (!_armed)
                return null;
            return JsonUtility.ToJson(new State
            {
                Armed = _armed, Phase = (int)Phase, Direction = Direction, Timer = _timer, WavesDone = WavesDone, HitsPerWave = HitsPerWave,
                Raid = IsRaid, RaidWaves = _raidWaves, ResumeTimer = _resumeTimer,
                RaidLabel = _raidLabel, CurrentWaves = _currentWaves, NextWaves = _nextWaves,
            });
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            _armed = s.Armed;
            Phase = (ShowerPhase)Mathf.Clamp(s.Phase, 0, 2);
            Direction = s.Direction;
            _timer = s.Timer;
            WavesDone = s.WavesDone;
            HitsPerWave = s.HitsPerWave;
            IsRaid = s.Raid && Phase != ShowerPhase.Idle;
            _raidWaves = s.RaidWaves;
            _resumeTimer = s.ResumeTimer;
            _raidLabel = string.IsNullOrEmpty(s.RaidLabel) ? "포격" : s.RaidLabel;
            _currentWaves = Math.Max(0, s.CurrentWaves);
            _nextWaves = Math.Max(0, s.NextWaves);
            _facingDirty = true;
        }
    }
}
