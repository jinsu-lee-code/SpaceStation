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
        private readonly List<ModuleInstance> _radiators = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _damageNow = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _keys = new List<ModuleInstance>();
        private readonly Dictionary<ModuleInstance, int> _near = new Dictionary<ModuleInstance, int>();

        /// <summary>열 지도가 다시 계산됨 (UI).</summary>
        public event Action Changed;

        public override string Key => SystemKey;
        public bool Active => Sim.RuleOpen(StationRule.Heat, Sim.Progression.GradeIndex >= Sim.Balance.HeatMinGrade); // 13-1 규칙을 정하는 쪽이 있으면 그쪽
        /// <summary>보통 한도 (온도 무관 · 따뜻함 모듈). 서늘함 모듈은 <see cref="LimitOf"/>.</summary>
        public float Limit => Sim.Balance.HeatLimit;
        public float DangerLevel => Sim.Balance.HeatLimit * Sim.Balance.HeatDangerRatio;
        /// <summary>열이 있는 모듈 (0 초과).</summary>
        public IReadOnlyDictionary<ModuleInstance, float> HeatMap => _heat;

        /// <summary>
        /// CONNECT_1121 A10 온기 · A11 과부하 (엔드리스 — 사용자 결정 2026-11-28). null이면 예전 그대로 (모든 모듈 한도 같음 · 온기 없음).
        /// </summary>
        public HeatPowerConfig Comfort { get; set; }

        public float HeatOf(ModuleInstance module) => module != null && _heat.TryGetValue(module, out var h) ? h : 0f;
        public bool IsOverheated(ModuleInstance module) => HeatOf(module) > LimitOf(module) + 1e-4f;
        public bool IsDanger(ModuleInstance module) => HeatOf(module) >= DangerOf(module) - 1e-4f;
        /// <summary>위험 상태로 버틴 시간 (파손까지 <see cref="BalanceConfig.HeatDamageSeconds"/>).</summary>
        public float DangerSeconds(ModuleInstance module) => module != null && _danger.TryGetValue(module, out var t) ? t : 0f;

        /// <summary>CONNECT A10: 그 모듈이 좋아하는 온도 (설정이 없으면 모두 무관).</summary>
        public HeatPreference PreferenceOf(ModuleData data) => Comfort != null ? Comfort.PreferenceOf(data) : HeatPreference.None;
        public HeatPreference PreferenceOf(ModuleInstance module) => module != null ? PreferenceOf(module.Data) : HeatPreference.None;

        /// <summary>모듈의 한도 — 서늘함 모듈은 보통 × <see cref="HeatPowerConfig.CoolLimitRatio"/> (더 빨리 과열).</summary>
        public float LimitOf(ModuleData data) => PreferenceOf(data) == HeatPreference.Cool ? Limit * Comfort.CoolLimitRatio : Limit;
        public float LimitOf(ModuleInstance module) => LimitOf(module != null ? module.Data : null);
        public float DangerOf(ModuleInstance module) => LimitOf(module) * Sim.Balance.HeatDangerRatio;

        /// <summary>CONNECT A10: 열 규칙이 열렸고, 따뜻함 모듈이 온기 구간 안 (전력 ↓ · 생산 ↑ · 안쪽 = 따뜻한 방).</summary>
        public bool IsWarm(ModuleInstance module)
            => Comfort != null && Active && PreferenceOf(module) == HeatPreference.Warm && Comfort.InWarmBand(HeatOf(module));

        /// <summary>CONNECT A10: 전력 소비 배율 (온기 구간의 따뜻함 모듈 = 난방 전력 ↓, 그 밖 1).</summary>
        public float PowerDemandMultiplier(ModuleInstance module) => IsWarm(module) ? Comfort.WarmPowerMultiplier : 1f;

        /// <summary>
        /// 생산 배율: 한도까지 1 (온기 구간의 따뜻함 모듈은 <see cref="HeatPowerConfig.WarmProductionMultiplier"/>),
        /// 넘은 만큼 줄어 위험 배율에서 1 − 최대 감소. 한도는 모듈마다 (<see cref="LimitOf"/>).
        /// </summary>
        public float ProductionMultiplier(ModuleInstance module)
        {
            float h = HeatOf(module);
            float limit = LimitOf(module);
            if (h <= limit)
                return IsWarm(module) ? Comfort.WarmProductionMultiplier : 1f;
            float span = Math.Max(1e-4f, limit * Sim.Balance.HeatDangerRatio - limit);
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
                int over = 0, warm = 0;
                foreach (var kv in _heat)
                {
                    if (IsOverheated(kv.Key))
                        over++;
                    if (IsWarm(kv.Key))
                        warm++;
                }
                Sim.Report(Comfort != null
                    ? $"열 관리 시작: 발열 모듈이 주변을 데웁니다 · 거주 · 농장 · 물 설비는 열 {Comfort.WarmMin:0.#} ~ {Comfort.WarmMax:0.#}에서 온기 (전력 -{(1f - Comfort.WarmPowerMultiplier) * 100f:0}% · 생산 +{(Comfort.WarmProductionMultiplier - 1f) * 100f:0}%) · 온기 {warm}곳 · 과열 {over}개 (J: 열 지도)"
                    : $"열 관리 시작: 핵융합로 · 제련소 · 연료전지가 주변을 데웁니다 · 과열 {over}개 (J: 열 지도, 방열판 = 바깥에 노출되게 붙이기)", false);
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
                    Sim.Report($"과열 파손: {(m.Data != null ? m.Data.DisplayName : "모듈")} (열 {HeatOf(m):0.0} / 한도 {LimitOf(m):0.#}) · 발열 모듈과 떨어뜨리거나 방열판을 붙이세요", false);
            }
        }

        /// <summary>열 지도를 지금 다시 계산 (틱 · 미리보기).</summary>
        public void Recalculate()
        {
            _heat.Clear();
            _sources.Clear();
            _radiators.Clear();
            if (Active)
            {
                var grid = Sim.Grid;
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
                        _radiators.Add(r);
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

        // ---------------- 미리보기 (배치 안내 · 봇, CONNECT A10) ----------------

        private float Falloff(int d) => d == 0 ? 1f : d == 1 ? Sim.Balance.HeatFalloff1 : d == 2 ? Sim.Balance.HeatFalloff2 : 0f;

        /// <summary>
        /// 빈 칸들(놓을 모듈의 칸)이 받을 열 — 지금 가동 중인 발열원 (exclude 제외, 칸끼리 가장 가까운 거리) − 맞닿을 동작 중인 방열판.
        /// 마지막 계산(<see cref="Recalculate"/>) 기준. 열 규칙 전이면 0.
        /// </summary>
        public float PreviewHeatAt(IReadOnlyList<Vector3Int> cells, ModuleInstance exclude = null)
        {
            if (!Active || cells == null || cells.Count == 0)
                return 0f;
            float h = 0f;
            foreach (var s in _sources)
            {
                if (s == exclude)
                    continue;
                int best = int.MaxValue;
                foreach (var c in cells)
                {
                    foreach (var sc in s.Cells)
                    {
                        int d = Math.Max(Math.Abs(c.x - sc.x), Math.Max(Math.Abs(c.y - sc.y), Math.Abs(c.z - sc.z)));
                        if (d < best)
                            best = d;
                    }
                }
                if (best <= 2)
                    h += s.Data.HeatOutput * Falloff(best);
            }
            if (h <= 0f)
                return 0f;
            foreach (var r in _radiators)
            {
                if (r == exclude)
                    continue;
                bool touch = false;
                foreach (var c in cells)
                {
                    foreach (var rc in r.Cells)
                        touch |= Math.Abs(c.x - rc.x) + Math.Abs(c.y - rc.y) + Math.Abs(c.z - rc.z) == 1;
                }
                if (touch)
                    h -= r.Data.HeatRemoval;
            }
            return Math.Max(0f, h);
        }

        /// <summary>발열원을 그 칸들에 놓으면 — 새로 온기가 되는 따뜻함 모듈 · 온기를 잃는 모듈 · 새로 과열되는 모듈 수 (exclude = 옮기는 그 모듈).</summary>
        public void PreviewSource(ModuleData data, IReadOnlyList<Vector3Int> cells, ModuleInstance exclude, out int warmed, out int lostWarm, out int overheated)
        {
            warmed = lostWarm = overheated = 0;
            if (!Active || data == null || data.HeatOutput <= 0f || cells == null)
                return;
            var grid = Sim.Grid;
            _near.Clear();
            foreach (var c in cells)
            {
                for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    if (!grid.TryGetModule(new Vector3Int(c.x + dx, c.y + dy, c.z + dz), out var m) || m == exclude)
                        continue;
                    int d = Math.Max(Math.Abs(dx), Math.Max(Math.Abs(dy), Math.Abs(dz)));
                    if (!_near.TryGetValue(m, out int old) || d < old)
                        _near[m] = d;
                }
            }
            foreach (var kv in _near)
            {
                var m = kv.Key;
                float before = HeatOf(m);
                float after = before + data.HeatOutput * Falloff(kv.Value);
                if (Comfort != null && PreferenceOf(m) == HeatPreference.Warm)
                {
                    bool was = Comfort.InWarmBand(before), now = Comfort.InWarmBand(after);
                    if (!was && now)
                        warmed++;
                    else if (was && !now)
                        lostWarm++;
                }
                float limit = LimitOf(m);
                if (before <= limit + 1e-4f && after > limit + 1e-4f)
                    overheated++;
            }
        }

        private bool Running(ModuleInstance m)
            => Sim.Connectivity.IsActive(m) && !Sim.Damage.IsDamaged(m) && !Sim.IsHalted(m);

        // ---------------- 글 (CONNECT A10 — 색이 아니라 글자 · ♨ 표시로) ----------------

        private string WarmEffect(ModuleData data)
        {
            bool produces = false;
            if (data != null)
            {
                foreach (var p in data.Production)
                    produces |= p.Amount > 0f;
            }
            string power = $"전력 -{(1f - Comfort.WarmPowerMultiplier) * 100f:0}%";
            return produces ? $"{power} · 생산 +{(Comfort.WarmProductionMultiplier - 1f) * 100f:0}%" : power;
        }

        /// <summary>건설 메뉴 설명 한 줄: 좋아하는 온도 (설정이 없거나 무관이면 null).</summary>
        public string PreferenceText(ModuleData data)
        {
            if (Comfort == null)
                return null;
            switch (PreferenceOf(data))
            {
                case HeatPreference.Warm:
                    return $"따뜻함을 좋아함 · 열 {Comfort.WarmMin:0.#} ~ {Comfort.WarmMax:0.#}이면 ♨ 온기: {WarmEffect(data)} (발열 모듈 가까이, 칸 단위 열이 열린 뒤)";
                case HeatPreference.Cool:
                    return $"서늘함을 좋아함 · 열 한도 {LimitOf(data):0.#} (보통 {Limit:0.#}) · 발열 모듈과 떨어뜨리기";
                default:
                    return null;
            }
        }

        /// <summary>모듈 정보 창 한 줄: 지금 열 · 온기 · 과열 (열 규칙 전이거나 할 말이 없으면 null).</summary>
        public string StateText(ModuleInstance module)
        {
            if (!Active || module == null || module.Data == null)
                return null;
            float h = HeatOf(module);
            float limit = LimitOf(module);
            var pref = PreferenceOf(module);
            string tail = pref == HeatPreference.Cool ? $" / 한도 {limit:0.#} (서늘함)" : $" / 한도 {limit:0.#}";
            if (IsDanger(module))
                return $"열 {h:0.0}{tail} · <b>과열 위험</b> (생산 -{(1f - ProductionMultiplier(module)) * 100f:0}%, 이어지면 파손)";
            if (IsOverheated(module))
                return $"열 {h:0.0}{tail} · 과열 (생산 -{(1f - ProductionMultiplier(module)) * 100f:0}%)";
            if (pref == HeatPreference.Warm)
            {
                if (IsWarm(module))
                    return $"열 {h:0.0} · ♨ 온기 ({WarmEffect(module.Data)})";
                return h < Comfort.WarmMin
                    ? $"열 {h:0.0} · 온기 아님 (열 {Comfort.WarmMin:0.#} ~ {Comfort.WarmMax:0.#} · 발열 모듈 가까이에)"
                    : $"열 {h:0.0} · 온기보다 더움 (열 {Comfort.WarmMax:0.#} 넘음, 한도 {limit:0.#})";
            }
            return h > 0f && Comfort != null ? $"열 {h:0.0}{tail}" : null; // 설정이 없으면(캠페인) 과열일 때만
        }

        /// <summary>
        /// 배치 · 옮기기 안내 한 줄: 그 자리의 열 (따뜻함 · 서늘함 모듈) · 발열원이면 이웃에 생길 일 (열 규칙 전이거나 할 말이 없으면 null).
        /// </summary>
        public string PreviewText(ModuleData data, IReadOnlyList<Vector3Int> cells, ModuleInstance moving)
        {
            if (!Active || data == null)
                return null;
            var sb = new System.Text.StringBuilder();
            var pref = PreferenceOf(data);
            if (pref != HeatPreference.None)
            {
                float h = PreviewHeatAt(cells, moving);
                float limit = LimitOf(data);
                if (h > limit + 1e-4f)
                    sb.Append($"열 {h:0.0} · 과열 (한도 {limit:0.#})");
                else if (pref == HeatPreference.Warm && Comfort.InWarmBand(h))
                    sb.Append($"열 {h:0.0} · ♨ 온기 ({WarmEffect(data)})");
                else if (pref == HeatPreference.Warm)
                    sb.Append(h < Comfort.WarmMin ? $"열 {h:0.0} · 온기 아님 ({Comfort.WarmMin:0.#} ~ {Comfort.WarmMax:0.#})" : $"열 {h:0.0} · 온기보다 더움");
                else
                    sb.Append($"열 {h:0.0} / 한도 {limit:0.#} (서늘함)");
            }
            if (data.HeatOutput > 0f)
            {
                PreviewSource(data, cells, moving, out int warmed, out int lost, out int over);
                if (warmed > 0)
                    sb.Append(sb.Length > 0 ? "  ·  " : "").Append($"이웃 ♨ 온기 +{warmed}");
                if (lost > 0)
                    sb.Append(sb.Length > 0 ? "  ·  " : "").Append($"이웃 온기 잃음 {lost}");
                if (over > 0)
                    sb.Append(sb.Length > 0 ? "  ·  " : "").Append($"이웃 과열 {over}");
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

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
