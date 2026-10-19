using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-3c 국가급 핵심 규칙 — 상시 정책 (사용자 결정 2026-10-13, PHASESET 5번 C-2, BALANCE 35번).
    /// 등급마다 늘어나는 정책 슬롯(국가급 2 · 대륙급 3 · 지구급 4, 도달한 가장 높은 등급 기준)에 정책을 걸어 둠.
    /// 바꾸면 새 정책은 적용 대기(30초) 뒤 발효(그동안 옛 정책 유지), 그 슬롯은 재사용 대기(5분) 동안 다시 못 바꿈. 같은 정책을 두 슬롯에 못 검.
    /// 사회 위기 선택지의 일시 효과(임금 인상 · 파업)도 같은 효과 묶음으로 더함 (<see cref="AddTemporary"/>).
    /// 효과는 <see cref="ResearchEffects.Policy"/>로 각 시스템이 읽는다 (정책이 없으면 중립 = 기존 결과 그대로).
    /// M-22 판 고리(<see cref="IPolicyGate"/>)를 꽂으면 슬롯 수 · 고를 수 있는 정책 · 시간 · 실제 효과 값을 판이 정함.
    /// </summary>
    public sealed class PolicySystem : StationSystem
    {
        public const string SystemKey = "policies";

        public enum AssignResult
        {
            Ok,
            NoSlot,
            Cooldown,
            Duplicate,
            Same,
            /// <summary>판 고리가 고를 수 없게 한 정책 (M-22).</summary>
            NotOffered,
        }

        public sealed class Slot
        {
            public PolicyData Active { get; internal set; }
            /// <summary>적용 대기 중인 정책 (비우기 예약이면 null + <see cref="HasPending"/>).</summary>
            public PolicyData Pending { get; internal set; }
            public bool HasPending { get; internal set; }
            public float PendingLeft { get; internal set; }
            public float Cooldown { get; internal set; }
        }

        public sealed class Temporary
        {
            public string Name { get; internal set; }
            public List<PolicyModifier> Modifiers { get; } = new List<PolicyModifier>();
            public float Remaining { get; internal set; }
        }

        private readonly PolicyConfig _config;
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<Temporary> _temps = new List<Temporary>();
        private readonly float[] _values = new float[PolicyStats.Count];

        public event Action Changed;

        public PolicySystem(PolicyConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override string Key => SystemKey;
        public PolicyConfig Config => _config;
        public IReadOnlyList<PolicyData> Policies => _config.Policies;

        private IPolicyGate _gate;

        /// <summary>판별 정책 고리 (M-22, 없으면 지금과 같음). 바꾸면 효과를 다시 계산.</summary>
        public IPolicyGate Gate
        {
            get => _gate;
            set
            {
                _gate = value;
                Recompute();
            }
        }

        /// <summary>
        /// 지금 쓸 수 있는 슬롯 수 (도달한 가장 높은 등급 기준 — 등급이 내려가도 줄지 않음).
        /// 13-1 규칙을 정하는 쪽이 있으면 정책 규칙이 열려야 쓸 수 있고, 슬롯 확장 규칙이 열리면 확장 수만큼 더.
        /// M-22 판 고리가 있으면 그 값.
        /// </summary>
        public int SlotCount
        {
            get
            {
                int count = DefaultSlotCount;
                return _gate != null ? Math.Max(0, _gate.SlotCount(count)) : count;
            }
        }

        private int DefaultSlotCount
        {
            get
            {
                int byGrade = _config.SlotsAt(Math.Max(Sim.Progression.GradeIndex, Sim.Progression.HighestGradeIndex));
                if (Sim.Rules == null)
                    return byGrade;
                if (!Sim.Rules.Allows(StationRule.Policies))
                    return 0;
                return byGrade + (Sim.Rules.Allows(StationRule.PolicyExpansion) ? _config.ExpansionSlots : 0);
            }
        }

        /// <summary>고를 수 있는 정책인지 (M-22 판 고리, 없으면 모두).</summary>
        public bool Offers(PolicyData policy) => policy != null && (_gate == null || _gate.Offers(policy));

        /// <summary>새 정책이 효과를 내기까지 (초, M-22 판 고리가 바꿀 수 있음).</summary>
        public float ApplyDelay => _gate != null ? Math.Max(0f, _gate.ApplyDelay(_config.ApplyDelay)) : _config.ApplyDelay;

        /// <summary>한 슬롯을 바꾼 뒤 다시 바꿀 수 있기까지 (초, M-22 판 고리가 바꿀 수 있음).</summary>
        public float SwapCooldown => _gate != null ? Math.Max(0f, _gate.SwapCooldown(_config.SwapCooldown)) : _config.SwapCooldown;

        /// <summary>시행 중 효과 하나의 실제 값 (M-22 판 고리가 바꿀 수 있음).</summary>
        public float ValueOf(PolicyData policy, PolicyModifier modifier) => _gate != null ? _gate.Value(policy, modifier) : modifier.Value;

        /// <summary>판 고리가 효과 값을 바꿀 조건이 달라졌을 때 (예: 주민 구성) 다시 계산.</summary>
        public void Refresh() => Recompute();
        public IReadOnlyList<Slot> Slots
        {
            get
            {
                EnsureSlots();
                return _slots;
            }
        }
        public IReadOnlyList<Temporary> Temporaries => _temps;

        private void EnsureSlots()
        {
            while (_slots.Count < SlotCount)
                _slots.Add(new Slot());
        }

        public bool IsActive(PolicyData policy)
        {
            foreach (var s in _slots)
                if (s.Active == policy)
                    return true;
            return false;
        }

        public AssignResult CheckAssign(int slot, PolicyData policy)
        {
            EnsureSlots();
            if (slot < 0 || slot >= SlotCount)
                return AssignResult.NoSlot;
            var s = _slots[slot];
            if (s.Cooldown > 0f)
                return AssignResult.Cooldown;
            var target = s.HasPending ? s.Pending : s.Active;
            if (target == policy)
                return AssignResult.Same;
            if (policy != null && !Offers(policy))
                return AssignResult.NotOffered;
            if (policy != null)
            {
                for (int i = 0; i < _slots.Count; i++)
                {
                    if (i == slot)
                        continue;
                    var o = _slots[i];
                    if (o.Active == policy || (o.HasPending && o.Pending == policy))
                        return AssignResult.Duplicate;
                }
            }
            return AssignResult.Ok;
        }

        /// <summary>슬롯에 정책을 예약 (null = 비우기). 적용 대기 뒤 발효, 슬롯은 재사용 대기.</summary>
        public AssignResult Assign(int slot, PolicyData policy)
        {
            var result = CheckAssign(slot, policy);
            if (result != AssignResult.Ok)
                return result;
            var s = _slots[slot];
            s.Pending = policy;
            s.HasPending = true;
            s.PendingLeft = ApplyDelay;
            s.Cooldown = SwapCooldown;
            if (s.PendingLeft <= 0f)
                Commit(s);
            Sim.Report(policy != null
                ? $"정책 '{policy.DisplayName}' 예약 · {ApplyDelay:0}초 뒤 시행"
                : $"정책 해제 예약 · {ApplyDelay:0}초 뒤", true);
            Recompute();
            return AssignResult.Ok;
        }

        private void Commit(Slot s)
        {
            s.Active = s.Pending;
            s.Pending = null;
            s.HasPending = false;
            s.PendingLeft = 0f;
        }

        /// <summary>사회 위기 선택지 등 일시 효과 (같은 이름이 있으면 바꿈).</summary>
        public void AddTemporary(string name, float seconds, params PolicyModifier[] modifiers)
        {
            _temps.RemoveAll(t => t.Name == name);
            var temp = new Temporary { Name = name, Remaining = Math.Max(0.01f, seconds) };
            temp.Modifiers.AddRange(modifiers);
            _temps.Add(temp);
            Recompute();
        }

        public void RemoveTemporary(string name)
        {
            if (_temps.RemoveAll(t => t.Name == name) > 0)
                Recompute();
        }

        public override void Tick(float dt)
        {
            if (dt <= 0f)
                return;
            EnsureSlots();
            bool changed = false;
            foreach (var s in _slots)
            {
                if (s.Cooldown > 0f)
                    s.Cooldown = Math.Max(0f, s.Cooldown - dt);
                if (s.HasPending)
                {
                    s.PendingLeft -= dt;
                    if (s.PendingLeft <= 0f)
                    {
                        string name = s.Pending != null ? s.Pending.DisplayName : null;
                        Commit(s);
                        Sim.Report(name != null ? $"정책 '{name}' 시행" : "정책 해제", true);
                        changed = true;
                    }
                }
            }
            for (int i = _temps.Count - 1; i >= 0; i--)
            {
                _temps[i].Remaining -= dt;
                if (_temps[i].Remaining <= 0f)
                {
                    _temps.RemoveAt(i);
                    changed = true;
                }
            }
            if (changed)
                Recompute();
            else if (_slots.Count > 0)
                Changed?.Invoke(); // 남은 시간 표시
        }

        private void Recompute()
        {
            for (int i = 0; i < _values.Length; i++)
                _values[i] = PolicyStats.Neutral((PolicyStat)i);
            foreach (var s in _slots)
                if (s.Active != null)
                    foreach (var m in s.Active.Modifiers)
                        Apply(m.Stat, ValueOf(s.Active, m)); // M-22 판 고리가 실제 값을 정함
            foreach (var t in _temps)
                foreach (var m in t.Modifiers)
                    Apply(m.Stat, m.Value);
            Sim.Effects.SetPolicy(_values);
            Sim.ApplyConsumptionMultipliers();
            Changed?.Invoke();
        }

        private void Apply(PolicyStat stat, float value)
        {
            int i = (int)stat;
            if (PolicyStats.IsAdditive(stat))
                _values[i] += value;
            else
                _values[i] *= value;
        }

        private PolicyData Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            foreach (var p in _config.Policies)
                if (p != null && p.name == name)
                    return p;
            return null;
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class SlotState
        {
            public string Active;
            public string Pending;
            public bool HasPending;
            public float PendingLeft;
            public float Cooldown;
        }

        [Serializable]
        private sealed class TempState
        {
            public string Name;
            public float Remaining;
            public List<PolicyModifier> Modifiers = new List<PolicyModifier>();
        }

        [Serializable]
        private sealed class State
        {
            public List<SlotState> Slots = new List<SlotState>();
            public List<TempState> Temps = new List<TempState>();
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            if (_slots.Count == 0 && _temps.Count == 0)
                return null;
            var s = new State();
            foreach (var slot in _slots)
                s.Slots.Add(new SlotState
                {
                    Active = slot.Active != null ? slot.Active.name : "",
                    Pending = slot.Pending != null ? slot.Pending.name : "",
                    HasPending = slot.HasPending,
                    PendingLeft = slot.PendingLeft,
                    Cooldown = slot.Cooldown,
                });
            foreach (var t in _temps)
            {
                var ts = new TempState { Name = t.Name, Remaining = t.Remaining };
                ts.Modifiers.AddRange(t.Modifiers);
                s.Temps.Add(ts);
            }
            return JsonUtility.ToJson(s);
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            _slots.Clear();
            foreach (var ss in s.Slots)
                _slots.Add(new Slot
                {
                    Active = Find(ss.Active),
                    Pending = Find(ss.Pending),
                    HasPending = ss.HasPending,
                    PendingLeft = ss.PendingLeft,
                    Cooldown = ss.Cooldown,
                });
            _temps.Clear();
            foreach (var ts in s.Temps)
            {
                var t = new Temporary { Name = ts.Name, Remaining = ts.Remaining };
                t.Modifiers.AddRange(ts.Modifiers);
                _temps.Add(t);
            }
            Recompute();
        }
    }
}
