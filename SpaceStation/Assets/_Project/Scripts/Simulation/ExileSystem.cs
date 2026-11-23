using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>REVIEW_1115 5번: 추방 대가 미리보기.</summary>
    public struct ExilePreview
    {
        /// <summary>문제 주민의 이유 (None = 이유 없는 추방).</summary>
        public ResidentProblem Problem;
        /// <summary>같은 종류 배율 × 정책 배율 (판이 더하는 대가에도 곱함).</summary>
        public float Multiplier;
        /// <summary>같은 종류 배율만 (1 ~ 설정 최대).</summary>
        public float KindMultiplier;
        /// <summary>깎이는 만족도 (양수).</summary>
        public float Satisfaction;
        public bool IsProblem => Problem != ResidentProblem.None;
    }

    /// <summary>REVIEW_1115 5번: 떠난 주민 한 명의 기록 (추모 공간 · 엔딩 연대기 · 귀환).</summary>
    [Serializable]
    public sealed class DepartedResident
    {
        public string Name;
        public string Kind = "";
        public List<string> Traits = new List<string>();
        /// <summary>정거장에 온 때 · 떠난 때 (게임 초).</summary>
        public float JoinedAt;
        public float LeftAt;
        /// <summary><see cref="PopulationChangeReason"/> 정수 (-1 = 모름).</summary>
        public int Reason = -1;
        /// <summary>이민선에 섞여 돌아옴 (귀환 허가).</summary>
        public bool Returned;

        public bool Exiled => Reason == (int)PopulationChangeReason.Exiled;
    }

    /// <summary>
    /// REVIEW_1115 5번 주민 추방 (사용자 결정 2026-11-15 · 11-24, 공통 고리): 명단에서 골라 한 명씩 내보냄.
    /// - 대가 = 만족도 감소 — 문제 주민(집 없음 · 불평꾼 · 힘든 집)은 작고, 이유 없는 추방은 큼. 같은 종류 주민이 많을수록 최대 <see cref="PolicyConfig.ExileKindMax"/>배, 정책 배율(추방령 · 이주 지원)
    /// - 정책 고리: 추방 금지(사면) · 떠나는 주민에게 식량 · 금속을 들려 보냄(이주 지원) · 떠난 주민의 귀환(귀환 허가) · 불평꾼 가중치(추방령) · 저절로 떠나는 간격(사면) · 이민선 규모
    /// - 떠난 주민 기록(이름 · 종류 · 사유)을 남김 → 판이 추모 · 연대기 · 청원에 씀
    /// - 규칙 <see cref="StationRule.Exile"/>: 규칙을 정하는 쪽이 없으면 늘 열림 (서사판 = 6장부터). 판의 대가(긴장도 · 코어 축)는 <see cref="Exiled"/>를 받아 더함
    /// </summary>
    public sealed class ExileSystem : StationSystem
    {
        public const string SystemKey = "exile";
        public override string Key => SystemKey;

        private readonly PolicyConfig _config;
        private readonly Func<float> _random01;
        private readonly List<DepartedResident> _departed = new List<DepartedResident>();

        /// <summary>내보낸 주민 (기록, 미리보기). 떠난 뒤 부름.</summary>
        public event Action<DepartedResident, ExilePreview> Exiled;
        /// <summary>떠났던 주민이 이민선으로 돌아옴 (기록 · 새로 들어온 주민).</summary>
        public event Action<DepartedResident, Resident> Returned;

        /// <summary>판이 미리보기에 덧붙이는 대가 문구 (예: 서사판 "긴장도 +6"). null = 없음.</summary>
        public Func<Resident, ExilePreview, string> PreviewSuffix { get; set; }

        public int ExiledCount { get; private set; }
        public IReadOnlyList<DepartedResident> Departed => _departed;

        public ExileSystem(PolicyConfig config, Func<float> random01)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _random01 = random01;
        }

        private ResidentRoster Roster => Sim.Residents;

        /// <summary>규칙이 열렸는지 (명단이 있어야 함).</summary>
        public bool Unlocked => Roster != null && Sim.RuleOpen(StationRule.Exile, true);

        /// <summary>정책(사면)으로 막힘.</summary>
        public bool Blocked => Sim.Effects.Policy(PolicyStat.ExileBlocked) > 0f;

        protected override void OnAttached()
        {
            if (Roster == null)
                return;
            Roster.Left += HandleLeft;
            Roster.Arrived += HandleArrived;
            Roster.TraitWeightScale = t => t == ResidentTrait.Complainer ? Sim.Effects.Policy(PolicyStat.ComplainerChance) : 1f;
            Roster.ArrivalTemplate = PickReturning;
            Sim.Population.PopulationChanged += HandlePopulation;
        }

        /// <summary>인구 단계 전에 (<see cref="TickStepNames.Needs"/> 뒤에 붙음): 저절로 떠나는 간격 배율(사면).</summary>
        public override void Tick(float dt) => Sim.Population.LossIntervalMultiplier = Sim.Effects.Policy(PolicyStat.LeaveInterval);

        // ---------------- 추방 ----------------

        public ExilePreview Preview(Resident r)
        {
            var p = new ExilePreview { Problem = Roster != null ? Roster.ProblemOf(r) : ResidentProblem.None, KindMultiplier = KindMultiplier(r) };
            p.Multiplier = p.KindMultiplier * Mathf.Max(0f, Sim.Effects.Policy(PolicyStat.ExileCost));
            p.Satisfaction = (p.IsProblem ? _config.ExileProblemSatisfaction : _config.ExileSatisfaction) * p.Multiplier;
            return p;
        }

        /// <summary>같은 종류 배율: 1 + (최대 − 1) × (같은 종류 다른 주민 ÷ 다른 주민). 종류가 없으면 1.</summary>
        public float KindMultiplier(Resident r)
        {
            if (r == null || string.IsNullOrEmpty(r.Kind) || Roster == null)
                return 1f;
            int others = Roster.Residents.Count - 1;
            if (others <= 0)
                return 1f;
            int same = Roster.CountKind(r.Kind) - 1;
            return 1f + (Mathf.Max(1f, _config.ExileKindMax) - 1f) * Mathf.Clamp01(same / (float)others);
        }

        /// <summary>내보낼 수 있는지 (안 되면 이유).</summary>
        public bool CanExile(Resident r, out string reason)
        {
            reason = null;
            if (!Unlocked)
                reason = "아직 열리지 않았습니다";
            else if (Blocked)
                reason = "정책 '사면' 중에는 내보낼 수 없습니다";
            else if (r == null || !OnRoster(r))
                reason = "명단에 없는 주민입니다";
            else if (Roster.Residents.Count <= 1)
                reason = "마지막 주민은 내보낼 수 없습니다";
            return reason == null;
        }

        private bool OnRoster(Resident r)
        {
            foreach (var x in Roster.Residents)
                if (x == r)
                    return true;
            return false;
        }

        /// <summary>한 명을 내보냄: 만족도 대가 → 그 주민이 떠남 → <see cref="Exiled"/>.</summary>
        public bool Exile(Resident r)
        {
            if (!CanExile(r, out _))
                return false;
            var preview = Preview(r);
            if (preview.Satisfaction > 0f)
                Sim.Population.AdjustSatisfaction(-preview.Satisfaction);
            _lastExiled = null;
            Roster.MarkLeaving(r);
            Sim.Population.Remove(1, PopulationChangeReason.Exiled);
            Roster.ClearLeaving();
            if (_lastExiled == null)
                return false;
            ExiledCount++;
            Sim.Report($"{r.Name} 주민을 내보냈습니다", false);
            Exiled?.Invoke(_lastExiled, preview);
            return true;
        }

        private DepartedResident _lastExiled;

        // ---------------- 떠난 주민 기록 · 들려 보내기 · 귀환 ----------------

        private void HandleLeft(Resident r, PopulationChangeReason? reason)
        {
            var d = new DepartedResident
            {
                Name = r.Name, Kind = r.Kind ?? "", JoinedAt = r.JoinedAt, LeftAt = Sim.ElapsedSeconds,
                Reason = reason.HasValue ? (int)reason.Value : -1,
            };
            foreach (var t in r.Traits)
                d.Traits.Add(t.ToString());
            if (reason == PopulationChangeReason.Exiled)
                _lastExiled = d;
            if (_config.DepartedMemory <= 0)
                return;
            _departed.Add(d);
            while (_departed.Count > _config.DepartedMemory)
                _departed.RemoveAt(0);
        }

        /// <summary>이주 지원: 떠나는 주민 1명당 식량 · 금속을 있는 만큼 들려 보냄.</summary>
        private void HandlePopulation(int delta, PopulationChangeReason reason)
        {
            float per = Sim.Effects.Policy(PolicyStat.SendOffSupplies);
            if (delta >= 0 || per <= 0f)
                return;
            float amount = per * -delta;
            var cost = new List<ResourceAmount>(2);
            foreach (var type in new[] { ResourceType.Food, ResourceType.Metal })
            {
                float a = Mathf.Min(amount, Sim.Resources.GetStock(type));
                if (a > 0f)
                    cost.Add(new ResourceAmount(type, a));
            }
            if (cost.Count > 0)
                Sim.Resources.TrySpend(cost);
        }

        /// <summary>귀환 허가: 이민선으로 오는 주민 한 명마다 확률로 떠났던 주민(돌아오지 않은, 이름이 겹치지 않는 가장 오래된 기록)이 대신 옴.</summary>
        private ResidentTemplate PickReturning(PopulationChangeReason? reason)
        {
            if (reason != PopulationChangeReason.Immigration || _random01 == null)
                return null;
            float chance = Sim.Effects.Policy(PolicyStat.ReturnChance);
            if (chance <= 0f || _random01() >= chance)
                return null;
            foreach (var d in _departed)
            {
                if (d.Returned || Roster.NameInUse(d.Name))
                    continue;
                d.Returned = true;
                var t = new ResidentTemplate { Name = d.Name, Kind = d.Kind ?? "" };
                foreach (var s in d.Traits)
                    if (Enum.TryParse(s, out ResidentTrait trait))
                        t.Traits.Add(trait);
                _returning = d;
                return t;
            }
            return null;
        }

        private DepartedResident _returning;

        /// <summary>명단이 귀환 주민을 받은 뒤 (Arrived 순서로 알림).</summary>
        private void HandleArrived(Resident r, PopulationChangeReason? reason)
        {
            if (_returning == null || r.Name != _returning.Name)
                return;
            var d = _returning;
            _returning = null;
            Sim.Report($"{r.Name} 주민이 이민선으로 돌아왔습니다", true);
            Returned?.Invoke(d, r);
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public int ExiledCount;
            public List<DepartedResident> Departed = new List<DepartedResident>();
        }

        public override string Capture(Func<ModuleInstance, int> indexOf) =>
            JsonUtility.ToJson(new State { ExiledCount = ExiledCount, Departed = new List<DepartedResident>(_departed) });

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            _departed.Clear();
            ExiledCount = 0;
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            ExiledCount = s.ExiledCount;
            if (s.Departed != null)
                _departed.AddRange(s.Departed);
        }
    }
}
