using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Simulation
{
    /// <summary>주민 한 명 (Phase 10). 이동 AI 없이 이름·특성·집만 가진다.</summary>
    public sealed class Resident
    {
        private readonly List<ResidentTrait> _traits = new List<ResidentTrait>();

        public int Id { get; }
        public string Name { get; }
        public IReadOnlyList<ResidentTrait> Traits => _traits;
        /// <summary>사는 거주 모듈 (없으면 null = 집 없음).</summary>
        public ModuleInstance Home { get; internal set; }
        /// <summary>플레이어가 직접 옮긴 주민: 집이 없어지기 전까지 자동 재배치하지 않음.</summary>
        public bool Pinned { get; internal set; }

        internal Resident(int id, string name, IEnumerable<ResidentTrait> traits)
        {
            Id = id;
            Name = name;
            _traits.AddRange(traits);
        }

        public bool Has(ResidentTrait trait) => _traits.Contains(trait);
    }

    /// <summary>집 주변 환경 (특성 조건).</summary>
    [Flags]
    public enum HomeEnvironment
    {
        None = 0,
        Radiation = 1,  // 면이 맞닿은 핵융합로
        Noise = 2,      // 면이 맞닿은 제련소
        Nature = 4,     // 면이 맞닿은 수경 농장
        Gravity = 8,    // 회전 링 범위 안 (링 자체 포함)
        Social = 16,    // 여가 시설(휴게실·회전 링) 범위 안
    }

    /// <summary>
    /// Phase 10 주민 명단 (순수 C#, BALANCE 26번).
    /// - 인원수는 지금처럼 <see cref="ResourceSimulation.Population"/>이 기준이고, 명단은 그 수에 맞춰 도착·이탈한다.
    ///   이탈할 주민은 불만 점수(집 없음, 이탈 우선 특성, 본인 만족도 감점)가 가장 높은 사람 → 같으면 최근에 온 사람.
    /// - 집: 거주 모듈(수용 인구 > 0, 활성)마다 수용 인원까지. 새 주민·집을 잃은 주민은 특성에 맞는 빈자리로 자동 배정.
    ///   플레이어가 옮긴 주민(Pinned)은 집이 사라지거나 수용이 줄어 넘칠 때만 다시 배정.
    /// - 효과: 능력(정거장 전체 배율), 소비(본인 배율 평균), 만족도 상한 보정(특성별 합계 상한).
    /// </summary>
    public sealed class ResidentRoster
    {
        private readonly ResidentConfig _config;
        private readonly Func<float> _random01;
        private readonly List<Resident> _residents = new List<Resident>();
        private readonly Dictionary<ModuleInstance, HomeEnvironment> _environment = new Dictionary<ModuleInstance, HomeEnvironment>();
        private readonly Dictionary<ModuleInstance, int> _capacity = new Dictionary<ModuleInstance, int>();
        private readonly Dictionary<ModuleInstance, int> _occupants = new Dictionary<ModuleInstance, int>();
        private readonly List<ModuleInstance> _homes = new List<ModuleInstance>();
        private readonly List<ModuleInstance> _neighbors = new List<ModuleInstance>();
        private readonly List<TraitDefinition> _pool = new List<TraitDefinition>();
        private readonly HashSet<string> _usedNames = new HashSet<string>();
        private readonly Dictionary<ResidentTrait, float> _moodByTrait = new Dictionary<ResidentTrait, float>();
        private int _nextId = 1;

        /// <summary>새 주민 도착 (사유: 인구 증가 등, 불러오기·명단 보정이면 null).</summary>
        public event Action<Resident, PopulationChangeReason?> Arrived;
        /// <summary>주민 이탈.</summary>
        public event Action<Resident, PopulationChangeReason?> Left;
        /// <summary>명단·집·효과가 바뀐 뒤 (UI).</summary>
        public event Action Changed;

        public ResidentConfig Config => _config;
        public IReadOnlyList<Resident> Residents => _residents;

        // 시뮬레이션이 연결
        internal StationGrid Grid;
        internal StationConnectivity Connectivity;
        internal Func<ModuleInstance, float> Housing;
        internal Func<ModuleInstance, float> Strength;
        internal ResearchEffects Effects;

        public ResidentRoster(ResidentConfig config, Func<float> random01)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _random01 = random01 ?? throw new ArgumentNullException(nameof(random01));
        }

        // ---------------- 조회 ----------------

        public int CountWith(ResidentTrait trait)
        {
            int n = 0;
            foreach (var r in _residents)
                if (r.Has(trait))
                    n++;
            return n;
        }

        /// <summary>능력 특성의 정거장 전체 효과 (비율, 상한 적용). 예: 기술자 3명 → 0.12.</summary>
        public float Ability(ResidentTrait trait)
        {
            var def = _config.Get(trait);
            return def != null ? def.ClampTotal(CountWith(trait) * def.PerResident) : 0f;
        }

        /// <summary>만족도 상한 보정 합계 (특성별 상한을 적용한 뒤 더함).</summary>
        public float MoodTotal { get; private set; }

        /// <summary>특성별 만족도 보정 (상한 적용 후, UI).</summary>
        public float MoodOf(ResidentTrait trait) => _moodByTrait.TryGetValue(trait, out var v) ? v : 0f;

        public HomeEnvironment GetEnvironment(ModuleInstance home) =>
            home != null && _environment.TryGetValue(home, out var env) ? env : HomeEnvironment.None;

        public int GetCapacity(ModuleInstance home) => home != null && _capacity.TryGetValue(home, out var c) ? c : 0;
        public int GetOccupantCount(ModuleInstance home) => home != null && _occupants.TryGetValue(home, out var c) ? c : 0;
        public int GetFreeSlots(ModuleInstance home) => Math.Max(0, GetCapacity(home) - GetOccupantCount(home));
        public IReadOnlyList<ModuleInstance> Homes => _homes;

        public void GetOccupants(ModuleInstance home, List<Resident> result)
        {
            result.Clear();
            foreach (var r in _residents)
                if (r.Home == home)
                    result.Add(r);
        }

        /// <summary>주민 한 명의 만족도 보정 (상한 적용 전, 특성 조건 반영).</summary>
        public float PersonalMood(Resident r)
        {
            float sum = 0f;
            foreach (var t in r.Traits)
                sum += MoodContribution(r, t);
            return sum;
        }

        /// <summary>이 집에 산다면 본인 만족도 보정 (이사 미리보기).</summary>
        public float MoodAt(Resident r, ModuleInstance home)
        {
            var env = GetEnvironment(home);
            float sum = 0f;
            foreach (var t in r.Traits)
            {
                var def = _config.Get(t);
                if (def == null)
                    continue;
                sum += t == ResidentTrait.Optimist || t == ResidentTrait.Complainer ? def.PerResident : EnvironmentMood(t, env);
            }
            return sum;
        }

        /// <summary>본인 소비 배율 (대식가·소식가).</summary>
        public float PersonalConsumption(Resident r, ResourceType type)
        {
            float m = 1f;
            if (type == ResourceType.Food && r.Has(ResidentTrait.BigEater))
                m += Value(ResidentTrait.BigEater);
            if ((type == ResourceType.Food || type == ResourceType.Water) && r.Has(ResidentTrait.LightEater))
                m += Value(ResidentTrait.LightEater);
            return Math.Max(0f, m);
        }

        /// <summary>정거장 전체 주민 소비 배율 (본인 배율의 평균, 주민이 없으면 1).</summary>
        public float ConsumptionMultiplier(ResourceType type)
        {
            if (_residents.Count == 0)
                return 1f;
            float sum = 0f;
            foreach (var r in _residents)
                sum += PersonalConsumption(r, type);
            return sum / _residents.Count;
        }

        /// <summary>이탈 순서 점수 (높을수록 먼저 떠남).</summary>
        public float Discontent(Resident r)
        {
            float d = r.Home == null ? _config.HomelessDiscontent : 0f;
            var env = GetEnvironment(r.Home);
            if (r.Has(ResidentTrait.Complainer) || (r.Has(ResidentTrait.RadiationSensitive) && (env & HomeEnvironment.Radiation) != 0))
                d += _config.LeaveFirstDiscontent;
            float mood = PersonalMood(r);
            if (mood < 0f)
                d -= mood;
            return d;
        }

        private float Value(ResidentTrait trait) => _config.Get(trait)?.PerResident ?? 0f;

        private float MoodContribution(Resident r, ResidentTrait trait)
        {
            var def = _config.Get(trait);
            if (def == null)
                return 0f;
            var env = GetEnvironment(r.Home);
            switch (trait)
            {
                case ResidentTrait.Optimist:
                case ResidentTrait.Complainer:
                    return def.PerResident;
                case ResidentTrait.Sociable:
                    return (env & HomeEnvironment.Social) != 0 ? def.PerResident : def.PerResidentOtherwise;
                case ResidentTrait.GravityLover:
                    return (env & HomeEnvironment.Gravity) != 0 ? def.PerResident : def.PerResidentOtherwise;
                case ResidentTrait.RadiationSensitive:
                    return (env & HomeEnvironment.Radiation) != 0 ? def.PerResident : 0f;
                case ResidentTrait.NoiseSensitive:
                    return (env & HomeEnvironment.Noise) != 0 ? def.PerResident : 0f;
                case ResidentTrait.NatureLover:
                    return (env & HomeEnvironment.Nature) != 0 ? def.PerResident : 0f;
                default:
                    return 0f; // 능력·소비·방사선 내성은 만족도 무관
            }
        }

        // ---------------- 진행 ----------------

        /// <summary>
        /// 매 틱: 집 목록·환경 갱신 → 인원 맞추기 → 집 검사·배정 → 효과 합계.
        /// </summary>
        /// <param name="reason">이번 인원 변화 사유 (알림용, 없으면 null)</param>
        internal void Update(int population, PopulationChangeReason? reason = null)
        {
            RefreshHomes();
            bool changed = false;
            while (_residents.Count > population)
            {
                var leaving = PickLeaving();
                RemoveResident(leaving);
                Left?.Invoke(leaving, reason);
                changed = true;
            }
            changed |= ValidateHomes();
            while (_residents.Count < population)
            {
                var r = CreateResident();
                _residents.Add(r);
                AssignHome(r);
                Arrived?.Invoke(r, reason);
                changed = true;
            }
            RecountOccupants();
            RecalculateMood();
            if (changed)
                Changed?.Invoke();
        }

        /// <summary>플레이어 이사: 빈자리가 있는 다른 집으로 옮기고 고정.</summary>
        public bool TryMove(Resident r, ModuleInstance home)
        {
            if (r == null || home == null || r.Home == home || !_residents.Contains(r) || GetFreeSlots(home) <= 0)
                return false;
            r.Home = home;
            r.Pinned = true;
            RecountOccupants();
            RecalculateMood();
            Changed?.Invoke();
            return true;
        }

        private void RefreshHomes()
        {
            _homes.Clear();
            _capacity.Clear();
            _environment.Clear();
            if (Grid == null)
                return;
            foreach (var m in Grid.Modules)
            {
                int cap = Housing != null ? (int)Math.Floor(Housing(m) + 1e-4f) : 0;
                if (cap <= 0)
                    continue;
                _homes.Add(m);
                _capacity[m] = cap;
                _environment[m] = ComputeEnvironment(m);
            }
        }

        private HomeEnvironment ComputeEnvironment(ModuleInstance home)
        {
            var env = HomeEnvironment.None;
            Grid.GetNeighborModules(home, _neighbors);
            foreach (var n in _neighbors)
            {
                if (n.Data == null || !IsActive(n))
                    continue;
                if (Contains(_config.RadiationSources, n.Data)) env |= HomeEnvironment.Radiation;
                if (Contains(_config.NoiseSources, n.Data)) env |= HomeEnvironment.Noise;
                if (Contains(_config.NatureSources, n.Data)) env |= HomeEnvironment.Nature;
            }
            foreach (var s in Grid.Modules)
            {
                var data = s.Data;
                if (data == null || !IsActive(s))
                    continue;
                bool gravity = data.GrowthIntervalMultiplier < 1f;
                bool social = data.ServiceNeed == ResidentNeed.Recreation;
                if (!gravity && !social)
                    continue;
                bool inRange = s == home || DefenseSystem.InRange(s.Cells, home.Cells, Effects != null ? Effects.ServiceRadius(data) : data.ServiceRadius);
                if (!inRange)
                    continue;
                if (gravity) env |= HomeEnvironment.Gravity;
                if (social && (Strength == null || Strength(s) > 0f)) env |= HomeEnvironment.Social; // 여가는 가동 중일 때만
            }
            return env;
        }

        private bool IsActive(ModuleInstance m) => Connectivity == null || Connectivity.IsActive(m);

        private static bool Contains(IReadOnlyList<ModuleData> list, ModuleData data)
        {
            foreach (var d in list)
                if (d == data)
                    return true;
            return false;
        }

        private void RecountOccupants()
        {
            _occupants.Clear();
            foreach (var r in _residents)
                if (r.Home != null)
                    _occupants[r.Home] = _occupants.TryGetValue(r.Home, out var c) ? c + 1 : 1;
        }

        /// <summary>사라진 집·넘친 집의 주민을 내보내고, 집 없는 주민을 빈자리에 배정. 바뀌었으면 true.</summary>
        private bool ValidateHomes()
        {
            bool changed = false;
            foreach (var r in _residents)
            {
                if (r.Home != null && !_capacity.ContainsKey(r.Home))
                {
                    r.Home = null;
                    r.Pinned = false;
                    changed = true;
                }
            }
            RecountOccupants();
            // 넘친 집: 고정하지 않은 사람 → 최근에 온 사람부터 내보냄
            foreach (var home in _homes)
            {
                int over = GetOccupantCount(home) - GetCapacity(home);
                for (int pass = 0; pass < 2 && over > 0; pass++)
                {
                    for (int i = _residents.Count - 1; i >= 0 && over > 0; i--)
                    {
                        var r = _residents[i];
                        if (r.Home != home || (pass == 0 && r.Pinned))
                            continue;
                        r.Home = null;
                        r.Pinned = false;
                        over--;
                        changed = true;
                    }
                }
            }
            RecountOccupants();
            foreach (var r in _residents)
            {
                if (r.Home == null && AssignHome(r))
                    changed = true;
            }
            return changed;
        }

        /// <summary>특성에 가장 맞는 빈자리 (같으면 빈자리 비율이 큰 곳). 빈자리가 없으면 false.</summary>
        private bool AssignHome(Resident r)
        {
            ModuleInstance best = null;
            float bestScore = float.NegativeInfinity;
            foreach (var home in _homes)
            {
                int cap = GetCapacity(home);
                int free = cap - GetOccupantCount(home);
                if (free <= 0)
                    continue;
                float score = Preference(r, GetEnvironment(home)) * 10f + (float)free / cap;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = home;
                }
            }
            if (best == null)
                return false;
            r.Home = best;
            _occupants[best] = GetOccupantCount(best) + 1;
            return true;
        }

        /// <summary>자동 배정 선호: 본인 만족도 보정 + 방사선 내성은 방사선 집 우선, 나머지는 방사선 집을 살짝 피함 (내성 주민 몫).</summary>
        private float Preference(Resident r, HomeEnvironment env)
        {
            float p = 0f;
            foreach (var t in r.Traits)
                p += EnvironmentMood(t, env);
            if ((env & HomeEnvironment.Radiation) != 0)
                p += r.Has(ResidentTrait.RadiationTolerant) ? 1f : -0.05f;
            return p;
        }

        private float EnvironmentMood(ResidentTrait trait, HomeEnvironment env)
        {
            var def = _config.Get(trait);
            if (def == null)
                return 0f;
            switch (trait)
            {
                case ResidentTrait.Sociable: return (env & HomeEnvironment.Social) != 0 ? def.PerResident : def.PerResidentOtherwise;
                case ResidentTrait.GravityLover: return (env & HomeEnvironment.Gravity) != 0 ? def.PerResident : def.PerResidentOtherwise;
                case ResidentTrait.RadiationSensitive: return (env & HomeEnvironment.Radiation) != 0 ? def.PerResident : 0f;
                case ResidentTrait.NoiseSensitive: return (env & HomeEnvironment.Noise) != 0 ? def.PerResident : 0f;
                case ResidentTrait.NatureLover: return (env & HomeEnvironment.Nature) != 0 ? def.PerResident : 0f;
                default: return 0f;
            }
        }

        private Resident PickLeaving()
        {
            Resident pick = null;
            float best = float.NegativeInfinity;
            foreach (var r in _residents) // 같은 점수면 뒤(최근)가 이김
            {
                float d = Discontent(r);
                if (d >= best)
                {
                    best = d;
                    pick = r;
                }
            }
            return pick;
        }

        private void RemoveResident(Resident r)
        {
            _residents.Remove(r);
            _usedNames.Remove(r.Name);
            if (r.Home != null && _occupants.TryGetValue(r.Home, out var c))
                _occupants[r.Home] = c - 1;
        }

        private void RecalculateMood()
        {
            _moodByTrait.Clear();
            foreach (var r in _residents)
                foreach (var t in r.Traits)
                {
                    float v = MoodContribution(r, t);
                    if (v != 0f)
                        _moodByTrait[t] = (_moodByTrait.TryGetValue(t, out var s) ? s : 0f) + v;
                }
            float total = 0f;
            var keys = new List<ResidentTrait>(_moodByTrait.Keys);
            foreach (var t in keys)
            {
                var def = _config.Get(t);
                float clamped = def != null ? def.ClampTotal(_moodByTrait[t]) : _moodByTrait[t];
                _moodByTrait[t] = clamped;
                total += clamped;
            }
            MoodTotal = total;
        }

        // ---------------- 새 주민 ----------------

        private Resident CreateResident()
        {
            var traits = new List<ResidentTrait>(2);
            var first = PickTrait(traits);
            if (first != null)
            {
                traits.Add(first.Trait);
                if (_random01() < _config.SecondTraitChance)
                {
                    var second = PickTrait(traits);
                    if (second != null)
                        traits.Add(second.Trait);
                }
            }
            return new Resident(_nextId++, PickName(), traits);
        }

        private TraitDefinition PickTrait(List<ResidentTrait> already)
        {
            _pool.Clear();
            float total = 0f;
            foreach (var t in _config.Traits)
            {
                if (t == null || t.Weight <= 0f || already.Contains(t.Trait) || Conflicts(t, already))
                    continue;
                _pool.Add(t);
                total += t.Weight;
            }
            if (_pool.Count == 0)
                return null;
            float roll = _random01() * total;
            foreach (var t in _pool)
            {
                if (roll < t.Weight)
                    return t;
                roll -= t.Weight;
            }
            return _pool[_pool.Count - 1];
        }

        private bool Conflicts(TraitDefinition t, List<ResidentTrait> already)
        {
            foreach (var a in already)
            {
                if (t.HasConflict && t.ConflictsWith == a)
                    return true;
                var other = _config.Get(a);
                if (other != null && other.HasConflict && other.ConflictsWith == t.Trait)
                    return true;
            }
            return false;
        }

        private string PickName()
        {
            var given = _config.GivenNames;
            var sur = _config.Surnames;
            if (given.Count == 0 || sur.Count == 0)
                return $"주민 {_nextId}";
            string name = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                name = $"{given[Index(given.Count)]} {sur[Index(sur.Count)]}";
                if (!_usedNames.Contains(name))
                    break;
            }
            if (_usedNames.Contains(name))
                name += $" {_nextId}";
            _usedNames.Add(name);
            return name;
        }

        private int Index(int count) => Math.Min(count - 1, (int)(_random01() * count));

        // ---------------- 세이브 ----------------

        internal void Capture(List<ResidentState> result, Func<ModuleInstance, int> moduleIndex)
        {
            result.Clear();
            foreach (var r in _residents)
            {
                var s = new ResidentState { Id = r.Id, Name = r.Name, Home = r.Home != null ? moduleIndex(r.Home) : -1, Pinned = r.Pinned };
                foreach (var t in r.Traits)
                    s.Traits.Add(t.ToString());
                result.Add(s);
            }
        }

        internal void Restore(IReadOnlyList<ResidentState> states, Func<int, ModuleInstance> moduleAt)
        {
            _residents.Clear();
            _usedNames.Clear();
            _nextId = 1;
            if (states == null)
                return;
            foreach (var s in states)
            {
                var traits = new List<ResidentTrait>();
                foreach (var name in s.Traits)
                    if (Enum.TryParse(name, out ResidentTrait t))
                        traits.Add(t);
                var r = new Resident(s.Id, s.Name, traits) { Home = s.Home != -1 ? moduleAt(s.Home) : null, Pinned = s.Pinned };
                _residents.Add(r);
                _usedNames.Add(r.Name);
                _nextId = Math.Max(_nextId, s.Id + 1);
            }
        }
    }

    [Serializable]
    public sealed class ResidentState
    {
        public int Id;
        public string Name;
        public List<string> Traits = new List<string>();
        /// <summary>StationState.Modules 순번, 코어 = -2, 집 없음 = -1.</summary>
        public int Home = -1;
        public bool Pinned;
    }
}
