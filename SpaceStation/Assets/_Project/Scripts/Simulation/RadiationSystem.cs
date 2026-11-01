using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// M-120 칸 단위 방사선 (REDESIGN S1, 사용자 결정 2026-11-01, BALANCE 칸 단위 방사선). 영향은 집에만 — 모듈을 망가뜨리지 않는다.
    /// 집의 방사선 = 우주 방사선과 방출 모듈 중 큰 값 − 차폐 (0 아래로 안 내려감).
    /// - 우주 방사선: 바깥까지 쌓인 두께(겹)로 줄어듦 — 0겹 100% · 1겹 · 2겹 비율 · 3겹 이상 0, 폭풍 배율(<see cref="SpaceMultiplier"/>)을 곱함.
    ///   두께는 칸마다 6방향 중 가장 얇은 쪽 (자기 칸은 세지 않음, 빈 칸을 사이에 둔 모듈도 막음). 장갑 격벽 · 외벽은 2겹.
    /// - 방출 모듈: 가동 중이면 1칸 안 집에 방출값, 2칸 집에 비율만큼. 합산하지 않고 가장 센 값 — 모아 지어도 바로 옆만 위험.
    /// - 차폐: 가동 중인 차폐 모듈이 면이 맞닿은 집마다 뺌 (방열판과 같은 모양).
    /// 규칙을 정하는 쪽이 <see cref="StationRule.Radiation"/>을 열어야 동작 (등급 조건 없음, 열기 전에는 모두 0 — 기존 결과 그대로).
    /// </summary>
    public sealed class RadiationSystem : StationSystem
    {
        public const string SystemKey = "radiation";
        private const int MaxLayers = 3;

        private readonly Dictionary<ModuleInstance, float> _level = new Dictionary<ModuleInstance, float>();
        private readonly Dictionary<ModuleInstance, float> _emitted = new Dictionary<ModuleInstance, float>();
        private readonly Dictionary<ModuleInstance, int> _near = new Dictionary<ModuleInstance, int>();
        private readonly List<ModuleInstance> _sources = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private Vector3Int _min, _max;
        private bool _announced;

        /// <summary>방사선 값이 다시 계산됨 (UI).</summary>
        public event Action Changed;

        public override string Key => SystemKey;
        public bool Active => Sim.RuleOpen(StationRule.Radiation, false);
        /// <summary>보통 주민이 편한 집 방사선 한도.</summary>
        public float Comfort => Sim.Balance.RadiationComfort;
        /// <summary>우주 방사선 배율 (폭풍 등, 1 = 평소). 저장은 바꾸는 쪽이.</summary>
        public float SpaceMultiplier { get; set; } = 1f;
        /// <summary>집마다 방사선 (0 초과만).</summary>
        public IReadOnlyDictionary<ModuleInstance, float> HomeLevels => _level;
        /// <summary>가동 중인 대피 시설 정원 합.</summary>
        public int ShelterCapacity { get; private set; }

        public float LevelOf(ModuleInstance home) => home != null && _level.TryGetValue(home, out var v) ? v : 0f;

        /// <summary>두께(겹)에 따른 우주 방사선 (폭풍 배율 포함).</summary>
        public float SpaceLevel(int layers)
        {
            var b = Sim.Balance;
            float k = layers <= 0 ? 1f : layers == 1 ? b.RadiationLayer1 : layers == 2 ? b.RadiationLayer2 : 0f;
            return b.RadiationSpace * k * SpaceMultiplier;
        }

        public override void Tick(float dt)
        {
            Recalculate();
            if (dt <= 0f || !Active || Sim.Session.IsGameOver || _announced)
                return;
            _announced = true;
            Sim.Report("방사선 관리 시작: 바깥 칸 · 핵융합로 옆 집일수록 높습니다 (J: 지도, 안쪽 · 장갑 격벽 · 차폐실로 낮추기)", false);
        }

        /// <summary>집마다 방사선을 지금 다시 계산 (틱 · 미리보기).</summary>
        public void Recalculate()
        {
            _level.Clear();
            ShelterCapacity = 0;
            if (Active)
            {
                CollectBounds();
                EmitFromSources();
                foreach (var m in Sim.Grid.Modules)
                {
                    if (m.Data == null || m.Data.HousingCapacity <= 0)
                        continue;
                    float level = Math.Max(SpaceLevel(LayersOf(m)), _emitted.TryGetValue(m, out var e) ? e : 0f);
                    if (level > 1e-4f)
                        _level[m] = level;
                }
                ApplyShielding();
            }
            Changed?.Invoke();
        }

        /// <summary>바깥까지 쌓인 두께 (겹, 최대 <see cref="MaxLayers"/>) — 칸마다 6방향 중 가장 얇은 쪽.</summary>
        public int LayersOf(ModuleInstance module)
        {
            var grid = Sim.Grid;
            int best = MaxLayers;
            foreach (var cell in module.Cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    int sum = 0;
                    var p = cell + dir;
                    while (sum < best && InBounds(p))
                    {
                        if (grid.TryGetModule(p, out var other) && other != module)
                            sum += other.Data != null ? other.Data.RadiationLayers : 1;
                        p += dir;
                    }
                    if (sum < best)
                        best = sum;
                    if (best == 0)
                        return 0;
                }
            }
            return best;
        }

        private void CollectBounds()
        {
            bool first = true;
            foreach (var m in Sim.Grid.Modules)
            {
                foreach (var c in m.Cells)
                {
                    _min = first ? c : Vector3Int.Min(_min, c);
                    _max = first ? c : Vector3Int.Max(_max, c);
                    first = false;
                }
            }
        }

        private bool InBounds(Vector3Int p) =>
            p.x >= _min.x && p.x <= _max.x && p.y >= _min.y && p.y <= _max.y && p.z >= _min.z && p.z <= _max.z;

        /// <summary>방출 모듈마다 반경 2칸 안 집에 — 집마다 가장 센 값만 남김.</summary>
        private void EmitFromSources()
        {
            var grid = Sim.Grid;
            float f2 = Sim.Balance.RadiationFalloff2;
            _emitted.Clear();
            _sources.Clear();
            foreach (var m in grid.Modules)
            {
                if (m.Data == null || !Running(m))
                    continue;
                ShelterCapacity += m.Data.ShelterCapacity;
                if (m.Data.RadiationOutput > 0f)
                    _sources.Add(m);
            }
            foreach (var s in _sources)
            {
                _near.Clear();
                foreach (var c in s.Cells)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        if (!grid.TryGetModule(new Vector3Int(c.x + dx, c.y + dy, c.z + dz), out var m) || m == s || m.Data == null || m.Data.HousingCapacity <= 0)
                            continue;
                        int d = Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));
                        if (!_near.TryGetValue(m, out int old) || d < old)
                            _near[m] = d;
                    }
                }
                foreach (var kv in _near)
                {
                    float v = s.Data.RadiationOutput * (kv.Value <= 1 ? 1f : f2);
                    if (!_emitted.TryGetValue(kv.Key, out float old) || v > old)
                        _emitted[kv.Key] = v;
                }
            }
        }

        /// <summary>차폐: 가동 중 + 맞닿은 집마다 뺌.</summary>
        private void ApplyShielding()
        {
            var grid = Sim.Grid;
            foreach (var r in grid.Modules)
            {
                if (r.Data == null || r.Data.RadiationRemoval <= 0f || !Running(r))
                    continue;
                grid.GetNeighborModules(r, _neighbors);
                foreach (var n in _neighbors)
                {
                    if (!_level.TryGetValue(n, out var v))
                        continue;
                    v -= r.Data.RadiationRemoval;
                    if (v <= 1e-4f)
                        _level.Remove(n);
                    else
                        _level[n] = v;
                }
            }
        }

        private bool Running(ModuleInstance m)
            => Sim.Connectivity.IsActive(m) && !Sim.Damage.IsDamaged(m) && !Sim.IsHalted(m);

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public bool Announced;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf) =>
            _announced ? JsonUtility.ToJson(new State { Announced = true }) : null;

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            _announced = false;
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s != null)
                _announced = s.Announced;
        }
    }
}
