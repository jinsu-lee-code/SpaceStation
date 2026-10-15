using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 13-1 타이쿤판 해금 선택 (TYCOON.md K2 · K2-2, BALANCE 41번). 공통 판정 고리(<see cref="IStationRules"/>)에 붙어
    /// 등급 숫자 대신 이 시스템이 유성우 · 합성 · 열 · 정책 · 사회 사건 · 방문 세력을 연다.
    /// - 등급에 처음 도달하면: 그 등급 자동 규칙 + 지난번에 '넘어온 채 또 안 고른' 후보를 연다 → 후보(넘어온 것 + 새 후보)를 띄운다.
    /// - 후보가 하나면 바로 열고, 둘 이상이면 플레이어가 고를 때까지 기다린다 (게임은 멈추지 않음).
    /// - 고르면: 남은 새 후보 → 다음 등급 후보로, 남은 넘어온 후보 → 다음 등급에 자동. 마지막 등급이면 남은 것을 모두 연다.
    /// - 등급이 내려가도 열린 규칙은 그대로. 고르기 전에 등급을 여러 개 넘으면 낮은 등급부터 차례로 고른다.
    /// </summary>
    public sealed class UnlockChoiceSystem : StationSystem, IStationRules
    {
        public const string SystemKey = "unlock-choice";

        /// <summary>후보 하나 (넘어온 후보인지 표시).</summary>
        public readonly struct Candidate
        {
            public readonly UnlockOption Option;
            /// <summary>지난 등급에서 넘어온 후보 — 이번에도 안 고르면 다음 등급에 자동으로 열림.</summary>
            public readonly bool Carried;

            public Candidate(UnlockOption option, bool carried)
            {
                Option = option;
                Carried = carried;
            }
        }

        private readonly UnlockChoiceConfig _config;
        private readonly List<Candidate> _offer = new List<Candidate>();
        private readonly List<UnlockOption> _carry = new List<UnlockOption>();
        private readonly List<UnlockOption> _autoNext = new List<UnlockOption>();
        private readonly List<string> _opened = new List<string>();

        /// <summary>후보가 바뀜 (새 선택 대기 · 고름).</summary>
        public event Action OfferChanged;
        /// <summary>후보가 열림 (고른 것 · 자동으로 열린 것 모두, 고름 = true).</summary>
        public event Action<UnlockOption, bool> OptionOpened;

        public UnlockChoiceSystem(UnlockChoiceConfig config)
        {
            _config = config ? config : throw new ArgumentNullException(nameof(config));
        }

        /// <summary>시뮬레이션에 붙이고 규칙을 정하는 쪽으로 연결 (등급 판정 바로 뒤에 틱). 세이브 복원 전에 부른다.</summary>
        public static UnlockChoiceSystem AttachTo(StationSimulation sim, UnlockChoiceConfig config)
        {
            var system = sim.AddSystem(new UnlockChoiceSystem(config), TickStepNames.Progression);
            sim.SetRules(system);
            return system;
        }

        public override string Key => SystemKey;
        public UnlockChoiceConfig Config => _config;
        /// <summary>지금 열린 규칙 전체.</summary>
        public StationRule Open { get; private set; }
        /// <summary>처리를 마친 가장 높은 등급 (선택 대기 중인 등급은 아직 아님).</summary>
        public int ProcessedGrade { get; private set; }
        /// <summary>선택 대기 중인 후보 (없으면 빈 목록).</summary>
        public IReadOnlyList<Candidate> Offer => _offer;
        public bool Pending => _offer.Count > 0;
        /// <summary>선택 대기 중인 등급 (대기 없으면 -1).</summary>
        public int OfferGrade { get; private set; } = -1;
        /// <summary>다음 등급에서 후보가 될 항목 (지난번에 고르지 않은 새 후보).</summary>
        public IReadOnlyList<UnlockOption> Carry => _carry;
        /// <summary>다음 등급에서 자동으로 열릴 항목.</summary>
        public IReadOnlyList<UnlockOption> AutoNext => _autoNext;
        /// <summary>열린 후보 키 (열린 순서).</summary>
        public IReadOnlyList<string> OpenedKeys => _opened;

        public bool Allows(StationRule rule) => rule != StationRule.None && (Open & rule) == rule;

        public bool IsOpened(UnlockOption option) => option != null && _opened.Contains(option.Key);

        public override void Tick(float dt) => Advance();

        /// <summary>도달한 등급까지 처리 (선택 대기가 생기면 멈춤). 틱마다 · 고른 뒤 호출.</summary>
        public void Advance()
        {
            int highest = Sim.Progression.HighestGradeIndex;
            while (!Pending && ProcessedGrade < highest)
                ProcessGrade(ProcessedGrade + 1);
        }

        /// <summary>대기 중인 후보 하나를 고른다. 대기가 없거나 범위 밖이면 false.</summary>
        public bool Choose(int index)
        {
            if (index < 0 || index >= _offer.Count)
                return false;
            var picked = _offer[index];
            int grade = OfferGrade;
            bool last = grade >= Sim.Progression.GradeCount - 1;
            OpenOption(picked.Option, true);
            foreach (var c in _offer)
            {
                if (c.Option == picked.Option)
                    continue;
                if (last)
                    OpenOption(c.Option, false); // 마지막 등급: 다음이 없으므로 모두 엶 (엔드리스)
                else if (c.Carried)
                    _autoNext.Add(c.Option);
                else
                    _carry.Add(c.Option);
            }
            _offer.Clear();
            OfferGrade = -1;
            ProcessedGrade = grade;
            OfferChanged?.Invoke();
            Advance();
            return true;
        }

        private void ProcessGrade(int grade)
        {
            var entry = _config.ForGrade(grade);
            if (entry != null)
                Open |= entry.AutoRules;
            foreach (var o in _autoNext)
                OpenOption(o, false);
            _autoNext.Clear();

            var candidates = new List<Candidate>();
            foreach (var o in _carry)
                if (!IsOpened(o))
                    candidates.Add(new Candidate(o, true));
            _carry.Clear();
            if (entry != null)
                foreach (var o in entry.Options)
                    if (o != null && !IsOpened(o))
                        candidates.Add(new Candidate(o, false));

            if (candidates.Count >= 2)
            {
                _offer.AddRange(candidates);
                OfferGrade = grade;
                OfferChanged?.Invoke();
                return; // 고를 때까지 ProcessedGrade는 그대로
            }
            if (candidates.Count == 1)
                OpenOption(candidates[0].Option, false); // 고를 것이 하나뿐이면 바로
            ProcessedGrade = grade;
        }

        private void OpenOption(UnlockOption option, bool chosen)
        {
            if (option == null || IsOpened(option))
                return;
            _opened.Add(option.Key);
            Open |= option.Rules;
            OptionOpened?.Invoke(option, chosen);
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public int Open;
            public int Processed;
            public int OfferGrade = -1;
            public List<string> Opened = new List<string>();
            public List<string> Offer = new List<string>();
            public List<bool> OfferCarried = new List<bool>();
            public List<string> Carry = new List<string>();
            public List<string> AutoNext = new List<string>();
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            var s = new State { Open = (int)Open, Processed = ProcessedGrade, OfferGrade = OfferGrade };
            s.Opened.AddRange(_opened);
            foreach (var c in _offer)
            {
                s.Offer.Add(c.Option.Key);
                s.OfferCarried.Add(c.Carried);
            }
            foreach (var o in _carry)
                s.Carry.Add(o.Key);
            foreach (var o in _autoNext)
                s.AutoNext.Add(o.Key);
            return JsonUtility.ToJson(s);
        }

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            if (string.IsNullOrEmpty(state))
                return;
            var s = JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            Open = (StationRule)s.Open;
            ProcessedGrade = s.Processed;
            OfferGrade = s.OfferGrade;
            _opened.Clear();
            _opened.AddRange(s.Opened);
            _offer.Clear();
            for (int i = 0; i < s.Offer.Count; i++)
            {
                var o = _config.Find(s.Offer[i]);
                if (o != null)
                    _offer.Add(new Candidate(o, i < s.OfferCarried.Count && s.OfferCarried[i]));
            }
            if (_offer.Count == 0)
                OfferGrade = -1;
            Fill(_carry, s.Carry);
            Fill(_autoNext, s.AutoNext);
            OfferChanged?.Invoke();
        }

        private void Fill(List<UnlockOption> list, List<string> keys)
        {
            list.Clear();
            foreach (var k in keys)
            {
                var o = _config.Find(k);
                if (o != null)
                    list.Add(o);
            }
        }
    }
}
