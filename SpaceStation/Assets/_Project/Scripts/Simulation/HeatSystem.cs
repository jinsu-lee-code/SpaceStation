using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-3c 대륙급 핵심 규칙 — 칸 단위 열 + 방열판 인접 (사용자 결정 2026-10-13, PHASESET 5번 C-2, BALANCE 36번).
    /// 대륙급부터 매 틱: 발열 모듈(핵융합로 · 제련소 · 연료전지 …, 가동 중)의 열이 반경 2칸 안 모듈에 거리마다 줄어 더해짐(자기 100% · 1칸 50% · 2칸 25%),
    /// 방열판(가동 중 + 우주로 노출된 면이 있음)은 면이 맞닿은 모듈마다 일정량을 뺌.
    /// 열이 한도를 넘으면 과열 = 생산 효율이 넘은 만큼 줄고(위험 배율에서 최대 -50%), 위험 상태가 60초 이어지면 파손.
    /// 대륙급 전에는 열이 모두 0 (기존 결과 그대로).
    /// </summary>
    public sealed class HeatSystem : StationSystem
    {
        public const string SystemKey = "heat";

        private readonly Dictionary<ModuleInstance, float> _heat = new Dictionary<ModuleInstance, float>();
        private readonly Dictionary<ModuleInstance, float> _danger = new Dictionary<ModuleInstance, float>();
        private readonly List<ModuleInstance> _sources = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _damageNow = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _keys = new List<ModuleInstance>();
        private readonly Dictionary<ModuleInstance, int> _near = new Dictionary<ModuleInstance, int>();

        /// <summary>열 지도가 다시 계산됨 (UI).</summary>
        public event Action Changed;

        public override string Key => SystemKey;
        public bool Active => Sim.RuleOpen(StationRule.Heat, Sim.Progression.GradeIndex >= Sim.Balance.HeatMinGrade); // 13-1 장 틀이면 장 규칙
        public float Limit => Sim.Balance.HeatLimit;
        public float DangerLevel => Sim.Balance.HeatLimit * Sim.Balance.HeatDangerRatio;
        /// <summary>열이 있는 모듈 (0 초과).</summary>
        public IReadOnlyDictionary<ModuleInstance, float> HeatMap => _heat;

        public float HeatOf(ModuleInstance module) => module != null && _heat.TryGetValue(module, out var h) ? h : 0f;
        public bool IsOverheated(ModuleInstance module) => HeatOf(module) > Limit + 1e-4f;
        public bool IsDanger(ModuleInstance module) => HeatOf(module) >= DangerLevel - 1e-4f;
        /// <summary>위험 상태로 버틴 시간 (파손까지 <see cref="BalanceConfig.HeatDamageSeconds"/>).</summary>
        public float DangerSeconds(ModuleInstance module) => module != null && _danger.TryGetValue(module, out var t) ? t : 0f;

        /// <summary>과열 생산 배율: 한도까지 1, 넘은 만큼 줄어 위험 배율에서 1 − 최대 감소.</summary>
        public float ProductionMultiplier(ModuleInstance module)
        {
            float h = HeatOf(module);
            float limit = Limit;
            if (h <= limit)
                return 1f;
            float span = Math.Max(1e-4f, DangerLevel - limit);
            return 1f - Sim.Balance.HeatMaxPenalty * Math.Min(1f, (h - limit) / span);
        }

        private bool _announced;

        public override void Tick(float dt)
        {
            Recalculate();
            if (dt <= 0f || !Active || Sim.Session.IsGameOver)
                return;
            if (!_announced)
            {
                _announced = true;
                int over = 0;
                foreach (var kv in _heat)
                    if (kv.Value > Limit + 1e-4f)
                        over++;
                Sim.Report($"열 관리 시작: 핵융합로 · 제련소 · 연료전지가 주변을 데웁니다 · 과열 {over}개 (J: 열 지도, 방열판 = 바깥에 노출되게 붙이기)", false);
            }
            // 위험 상태가 이어지면 파손 (파손된 모듈은 열을 내지 않지만 받는 열은 남음 — 방치하면 수리 뒤 다시 위험)
            _damageNow.Clear();
            _keys.Clear();
            _keys.AddRange(_danger.Keys);
            foreach (var m in _keys)
            {
                if (!IsDanger(m) || Sim.Damage.IsDamaged(m))
                    _danger.Remove(m);
            }
            foreach (var kv in _heat)
            {
                var m = kv.Key;
                if (!IsDanger(m) || Sim.Damage.IsDamaged(m) || m == Sim.Core)
                    continue;
                float t = DangerSeconds(m) + dt;
                if (t >= Sim.Balance.HeatDamageSeconds)
                {
                    _damageNow.Add(m);
                    t = 0f;
                }
                _danger[m] = t;
            }
            foreach (var m in _damageNow)
            {
                _danger.Remove(m);
                if (Sim.Damage.Damage(m))
                    Sim.Report($"과열 파손: {(m.Data != null ? m.Data.DisplayName : "모듈")} (열 {HeatOf(m):0.0} / 한도 {Limit:0}) · 발열 모듈과 떨어뜨리거나 방열판을 붙이세요", false);
            }
        }

        /// <summary>열 지도를 지금 다시 계산 (틱 · 미리보기).</summary>
        public void Recalculate()
        {
            _heat.Clear();
            if (Active)
            {
                var grid = Sim.Grid;
                _sources.Clear();
                foreach (var m in grid.Modules)
                {
                    if (m.Data != null && m.Data.HeatOutput > 0f && Running(m))
                        _sources.Add(m);
                }
                if (_sources.Count > 0)
                {
                    // 12-3c-opt: 발열원마다 주변 반경 2칸(5×5×5)만 격자에서 찾아 모듈별 최소 칸 거리로 더함
                    // (예전: 모든 모듈 × 모든 발열원 칸 거리 비교 — 대륙급 모듈 579 · 발열원 83에서 틱당 29ms). 결과는 같음
                    float f1 = Sim.Balance.HeatFalloff1, f2 = Sim.Balance.HeatFalloff2;
                    foreach (var s in _sources)
                    {
                        _near.Clear();
                        foreach (var c in s.Cells)
                        {
                            for (int dx = -2; dx <= 2; dx++)
                            for (int dy = -2; dy <= 2; dy++)
                            for (int dz = -2; dz <= 2; dz++)
                            {
                                if (!grid.TryGetModule(new Vector3Int(c.x + dx, c.y + dy, c.z + dz), out var m))
                                    continue;
                                int d = m == s ? 0 : Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));
                                if (!_near.TryGetValue(m, out int old) || d < old)
                                    _near[m] = d;
                            }
                        }
                        float output = s.Data.HeatOutput;
                        foreach (var kv in _near)
                        {
                            float k = kv.Value == 0 ? 1f : kv.Value == 1 ? f1 : f2;
                            _heat.TryGetValue(kv.Key, out float h);
                            _heat[kv.Key] = h + output * k;
                        }
                    }
                    // 방열판: 가동 중 + 바깥으로 노출된 면 → 맞닿은 모듈마다 뺌
                    foreach (var r in grid.Modules)
                    {
                        if (r.Data == null || r.Data.HeatRemoval <= 0f || !Running(r) || !DamageSystem.IsExterior(grid, r))
                            continue;
                        grid.GetNeighborModules(r, _neighbors);
                        foreach (var n in _neighbors)
                        {
                            if (_heat.TryGetValue(n, out var h))
                            {
                                h -= r.Data.HeatRemoval;
                                if (h <= 1e-4f)
                                    _heat.Remove(n);
                                else
                                    _heat[n] = h;
                            }
                        }
                    }
                }
            }
            Changed?.Invoke();
        }

        /// <summary>방열판이 지금 동작하는지 (가동 중 + 바깥 노출). UI 안내용.</summary>
        public bool RadiatorWorking(ModuleInstance r)
            => r != null && r.Data != null && r.Data.HeatRemoval > 0f && Running(r) && DamageSystem.IsExterior(Sim.Grid, r);

        private bool Running(ModuleInstance m)
            => Sim.Connectivity.IsActive(m) && !Sim.Damage.IsDamaged(m) && !Sim.IsHalted(m);

        public override void OnModuleRemoved(ModuleInstance module) => _danger.Remove(module);

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public bool Announced;
            public List<int> Modules = new List<int>();
            public List<float> Seconds = new List<float>();
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            if (_danger.Count == 0 && !_announced)
                return null;
            var s = new State { Announced = _announced };
            foreach (var kv in _danger)
            {
                int i = indexOf(kv.Key);
                if (i < 0)
                    continue;
                s.Modules.Add(i);
                s.Seconds.Add(kv.Value);
            }
            return JsonUtility.ToJson(s);
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            _danger.Clear();
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            _announced = s.Announced;
            for (int i = 0; i < s.Modules.Count && i < s.Seconds.Count; i++)
            {
                var m = moduleAt(s.Modules[i]);
                if (m != null)
                    _danger[m] = s.Seconds[i];
            }
        }
    }
}
