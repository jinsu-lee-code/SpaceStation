using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>14-4 도착해 답을 기다리는 사건 · 청원 하나 (문구 · 무게는 일어나는 순간 정해 저장).</summary>
    [Serializable]
    public sealed class SocialPending
    {
        public string Id;
        public bool Petition;
        public string Title;
        /// <summary>말하는 무리 이름 (청원만).</summary>
        public string Speaker;
        public string Body;
        public string SubjectA;
        public string SubjectB;
        public int People;
        public int Level;
        public float Weight = 1f;
    }

    /// <summary>
    /// 14-4 서사판 사회 사건 (사용자 결정 2026-11-02 D-18 · REDESIGN S3 · S4): 사건은 장 진행에 맞춰 늘 오고, 긴장도 단계가 대가 배율과 고를 수 있는 답을 바꿈.
    /// '곧 터질 듯'이면 소란이 한 번 더 옴. 위기 예고가 먼저 시작되면 남은 사건을 간격을 두고 보냄. 위기 '물음의 시간'(<see cref="InquiryClimax"/>)의 청원도 여기로 옴.
    /// 화면이 게임을 멈추고 답을 고름. 화면이 없으면(봇 · 시험) 시간이 지나 마지막 답으로.
    /// '감시 강화'(위기 기준 -10)는 이 단계를 계산할 때 긴장도에서 빼서 대가를 줄임.
    /// </summary>
    public sealed class SocialEventSystem : StationSystem
    {
        public const string SystemKey = "socialEvents";

        private readonly SocietyConfig _society;
        private readonly SocialEventConfig _config;
        private readonly List<string> _fired = new List<string>();
        private readonly List<string> _usedPetitions = new List<string>();
        private SocialPending _pending;
        private float _pendingWait;
        private float _sinceLast;
        private bool _riotFired;

        /// <summary>사건 · 청원이 도착해 답을 기다림 (화면이 게임을 멈추고 묻는다).</summary>
        public event Action<SocialPending> Arrived;
        /// <summary>답을 정함 (사건, 답 번호).</summary>
        public event Action<SocialPending, int> Resolved;

        public SocialEventSystem(SocietyConfig society, SocialEventConfig config)
        {
            _society = society ?? throw new ArgumentNullException(nameof(society));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public override string Key => SystemKey;
        public SocialEventConfig Config => _config;
        public SocialPending Pending => _pending;
        /// <summary>이 판에서 떠난 주민 (청원 근거, 이 시스템이 붙은 뒤부터).</summary>
        public int DepartedTotal { get; private set; }

        private TensionSystem Tension => Sim.GetSystem<TensionSystem>();
        private ArkShipSystem Ark => Sim.GetSystem<ArkShipSystem>();

        /// <summary>대가를 계산하는 긴장도 (감시 강화가 낮춤).</summary>
        public float EffectiveTension => Tension != null ? Mathf.Max(0f, Tension.Value + Sim.Effects.Policy(PolicyStat.CrisisThreshold)) : 0f;
        /// <summary>대가를 계산하는 긴장도 단계 (0 잔잔함 ~ 3 곧 터질 듯).</summary>
        public int EffectiveLevel => _society.Level(EffectiveTension);

        /// <summary>다른 창이 먼저 답을 기다리는 중 (사건을 미룸).</summary>
        public bool Busy => _pending != null || Ark?.Pending != null || Sim.GetSystem<RecordArchiveSystem>()?.Pending != null;

        protected override void OnAttached()
        {
            Sim.Population.PopulationChanged += HandlePopulation;
            if (Sim.Chapters != null)
                Sim.Chapters.ChapterStarted += HandleChapterStarted;
        }

        private void HandlePopulation(int delta, PopulationChangeReason reason)
        {
            if (delta < 0)
                DepartedTotal -= delta;
        }

        private void HandleChapterStarted(int index)
        {
            _fired.Clear();
            _usedPetitions.Clear();
            _riotFired = false;
            _sinceLast = 0f;
        }

        // ---------------- 시계 ----------------

        public override void Tick(float dt)
        {
            if (dt <= 0f || Sim.Session.IsGameOver)
                return;
            if (_pending != null)
            {
                _pendingWait += dt; // 화면이 있으면 게임이 멈춰 여기까지 오지 않음 (봇 · 시험)
                if (_pendingWait >= _config.AutoResolveSeconds)
                    Answer(Answers(_pending).Count - 1);
                return;
            }
            var chapters = Sim.Chapters;
            if (chapters == null)
                return;
            var phase = chapters.Phase;
            if (phase != ChapterPhase.Preparation && phase != ChapterPhase.Warning)
                return;
            _sinceLast += dt;
            if (Busy)
                return;
            int number = chapters.Index + 1;
            var riot = _config.Riot;
            if (!_riotFired && riot != null && !string.IsNullOrEmpty(riot.Id) && riot.Chapter == number && EffectiveLevel >= 3)
            {
                _riotFired = true;
                Fire(riot);
                return;
            }
            foreach (var e in _config.Events)
            {
                if (e == null || e.Chapter != number || _fired.Contains(e.Id))
                    continue;
                if (chapters.ChapterSeconds >= e.AtSeconds || (phase == ChapterPhase.Warning && _sinceLast >= _config.WarningGap))
                {
                    _fired.Add(e.Id);
                    Fire(e);
                }
                return; // 차례대로
            }
        }

        private void Fire(SocialEvent e)
        {
            int level = EffectiveLevel;
            var p = new SocialPending
            {
                Id = e.Id,
                Title = e.Title,
                Level = level,
                Weight = _config.Weight(level),
            };
            FillSubject(p, e.Subject);
            p.Body = Fill(e.Body, p);
            Open(p);
        }

        private void Open(SocialPending p)
        {
            _pending = p;
            _pendingWait = 0f;
            _sinceLast = 0f;
            Arrived?.Invoke(p);
        }

        private void FillSubject(SocialPending p, SocialEventSubject subject)
        {
            int population = Sim.Resources.Population;
            switch (subject)
            {
                case SocialEventSubject.BiggestTwo:
                    Biggest(out p.SubjectA, out p.SubjectB);
                    break;
                case SocialEventSubject.MostDiscontent:
                    p.SubjectA = MostDiscontent();
                    p.People = Mathf.Max(1, Mathf.CeilToInt(population * _config.LeavingShare));
                    break;
                case SocialEventSubject.Sick:
                    p.People = Mathf.Max(1, Mathf.CeilToInt(population * Mathf.Max(_config.SickShareMin, 1f - NeedRatio(ResidentNeed.Medical))));
                    break;
            }
        }

        /// <summary>그 요구를 채운 비율 (그 요구가 아직 없으면 1).</summary>
        private float NeedRatio(ResidentNeed need)
        {
            foreach (var status in Sim.Needs.Statuses)
                if (status.Need == need)
                    return status.Ratio;
            return 1f;
        }

        private void Biggest(out string first, out string second)
        {
            first = second = null;
            int a = -1, b = -1;
            var ark = Ark;
            var roster = Sim.Residents;
            if (ark != null && roster != null)
                foreach (var s in ark.Config.Species)
                {
                    if (s == null)
                        continue;
                    int n = roster.CountKind(s.Id);
                    if (n <= 0)
                        continue;
                    if (n > a)
                    {
                        b = a;
                        second = first;
                        a = n;
                        first = s.DisplayName;
                    }
                    else if (n > b)
                    {
                        b = n;
                        second = s.DisplayName;
                    }
                }
            first = first ?? "주민";
            second = second ?? "새로 온";
        }

        /// <summary>중시 요구를 가장 못 채운 무리 (같으면 큰 쪽, 코어를 믿는 무리는 뒤로 — 통제 순응이 가장 높은 쪽).</summary>
        private string MostDiscontent()
        {
            var ark = Ark;
            var roster = Sim.Residents;
            string best = null;
            float bestScore = float.MinValue;
            if (ark != null && roster != null)
                foreach (var s in ark.Config.Species)
                {
                    if (s == null)
                        continue;
                    int n = roster.CountKind(s.Id);
                    if (n <= 0)
                        continue;
                    float score = (1f - NeedRatio(s.FavoredNeed)) * 100f - s.ControlReaction * 10f + n * 0.01f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = s.DisplayName;
                    }
                }
            return best ?? "주민";
        }

        private static string Fill(string text, SocialPending p) => string.IsNullOrEmpty(text)
            ? ""
            : text.Replace("{a}", p.SubjectA ?? "").Replace("{b}", p.SubjectB ?? "").Replace("{n}", p.People.ToString());

        // ---------------- 청원 ----------------

        /// <summary>위기가 청원 하나를 보냄 (남은 청원이 없으면 false — order = 몇 번째, count = 모두 몇 건).</summary>
        public bool SendPetition(string title, int order, int count)
        {
            if (Busy)
                return false;
            var pick = PickPetition(out int n);
            if (pick == null)
                return false;
            _usedPetitions.Add(pick.Id);
            int level = EffectiveLevel;
            var p = new SocialPending
            {
                Id = pick.Id,
                Petition = true,
                Title = $"{title} {order + 1}/{count}",
                Speaker = Ark?.SpeciesName(pick.Speaker) ?? "",
                People = n,
                Level = level,
                Weight = _config.Weight(level),
            };
            p.Body = Fill(pick.Body, p);
            Open(p);
            return true;
        }

        /// <summary>근거가 맞는 청원 먼저 (목록 순서), 없으면 근거 없는 청원.</summary>
        private SocialPetition PickPetition(out int n)
        {
            SocialPetition fallback = null;
            n = 0;
            foreach (var p in _config.Petitions)
            {
                if (p == null || _usedPetitions.Contains(p.Id) || !SpeakerHere(p.Speaker))
                    continue;
                if (p.Cause == PetitionCause.Always)
                {
                    fallback = fallback ?? p;
                    continue;
                }
                if (Holds(p.Cause, out n))
                    return p;
            }
            n = 0;
            return fallback;
        }

        private bool SpeakerHere(string species)
        {
            var roster = Sim.Residents;
            return string.IsNullOrEmpty(species) || (roster != null && roster.CountKind(species) > 0);
        }

        private bool Holds(PetitionCause cause, out int n)
        {
            n = 0;
            switch (cause)
            {
                case PetitionCause.RefusedShips:
                    n = Ark != null ? Ark.TurnedAway : 0;
                    return n > 0;
                case PetitionCause.ControlPolicy:
                    n = Sim.GetSystem<ChapterPolicySystem>()?.ActiveControl ?? 0;
                    return n > 0;
                case PetitionCause.Departed:
                    n = DepartedTotal;
                    return n > 0;
                case PetitionCause.OverwroteRecord:
                    n = Sim.GetSystem<RecordArchiveSystem>()?.OverwrittenCount ?? 0;
                    return n > 0;
                case PetitionCause.ForgotEarth:
                    var core = Sim.GetSystem<CorePersonalitySystem>();
                    return core != null && core.Value(CoreAxis.Memory) >= _config.ForgetLean;
                case PetitionCause.LostOutcome:
                    var last = Sim.Chapters?.LastRecord;
                    return last != null && last.Outcome != ChapterOutcome.Clean;
                default:
                    return true;
            }
        }

        // ---------------- 답 ----------------

        public IReadOnlyList<SocialAnswer> Answers(SocialPending p)
        {
            if (p == null)
                return Array.Empty<SocialAnswer>();
            if (p.Petition)
                return _config.PetitionAnswers;
            var e = _config.Find(p.Id);
            if (e == null)
                return Array.Empty<SocialAnswer>();
            return e.Answers;
        }

        /// <summary>이 답의 대가 (지금 사건의 무게를 곱함).</summary>
        public List<ResourceAmount> CostOf(SocialAnswer answer)
        {
            var cost = new List<ResourceAmount>();
            float weight = _pending != null ? _pending.Weight : 1f;
            if (answer != null)
                foreach (var a in answer.Cost)
                    cost.Add(new ResourceAmount(a.Type, Mathf.Ceil(a.Amount * weight)));
            return cost;
        }

        /// <summary>이 답의 긴장도 변화 (오르는 쪽만 무게를 곱함).</summary>
        public float TensionOf(SocialAnswer answer)
        {
            if (answer == null)
                return 0f;
            float weight = _pending != null ? _pending.Weight : 1f;
            return answer.Tension > 0f ? answer.Tension * weight : answer.Tension;
        }

        /// <summary>이 답으로 떠나는 수 (한 명은 남김).</summary>
        public int LeaveOf(SocialAnswer answer)
        {
            if (answer == null || _pending == null || (answer.Leave <= 0 && answer.LeaveShare <= 0f))
                return 0;
            int n = Mathf.CeilToInt((answer.Leave + answer.LeaveShare * _pending.People) * _pending.Weight);
            return Mathf.Clamp(n, 0, Mathf.Max(0, Sim.Resources.Population - 1));
        }

        public bool CanAnswer(int index, out string reason)
        {
            reason = "";
            var answers = Answers(_pending);
            if (_pending == null || index < 0 || index >= answers.Count)
                return false;
            var a = answers[index];
            if (a.LockedFromLevel >= 0 && _pending.Level >= a.LockedFromLevel)
            {
                reason = a.LockedReason;
                return false;
            }
            if (!Sim.Resources.CanAfford(CostOf(a)))
            {
                reason = "자원이 모자랍니다";
                return false;
            }
            return true;
        }

        public bool Answer(int index)
        {
            if (!CanAnswer(index, out _))
                return false;
            var p = _pending;
            var a = Answers(p)[index];
            var cost = CostOf(a);
            float tension = TensionOf(a);
            int leave = LeaveOf(a);
            _pending = null;
            _pendingWait = 0f;
            Sim.Resources.TrySpend(cost);
            if (Mathf.Abs(a.Satisfaction) > 1e-3f)
                Sim.Population.AdjustSatisfaction(a.Satisfaction);
            Tension?.Adjust(tension);
            if (leave > 0)
                Sim.Population.Remove(leave, PopulationChangeReason.Crisis);
            if (a.Temporary.Count > 0 && a.TemporarySeconds > 0f)
                Sim.Policies?.AddTemporary(a.TemporaryName, a.TemporarySeconds, ToArray(a.Temporary));
            var core = Sim.GetSystem<CorePersonalitySystem>();
            core?.Apply(a.Shifts, $"{p.Title}: {a.Title}");
            if (!string.IsNullOrEmpty(a.Line))
                core?.Log(Fill(a.Line, p));
            Sim.Report($"{p.Title}: {a.Title}" + (leave > 0 ? $" · {leave}명이 떠났습니다" : ""), leave == 0);
            Resolved?.Invoke(p, index);
            return true;
        }

        private static PolicyModifier[] ToArray(IReadOnlyList<PolicyModifier> list)
        {
            var array = new PolicyModifier[list.Count];
            for (int i = 0; i < array.Length; i++)
                array[i] = list[i];
            return array;
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public List<string> Fired = new List<string>();
            public List<string> UsedPetitions = new List<string>();
            public bool HasPending;
            public SocialPending Pending;
            public float PendingWait;
            public float SinceLast;
            public bool RiotFired;
            public int DepartedTotal;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf) => JsonUtility.ToJson(new State
        {
            Fired = new List<string>(_fired),
            UsedPetitions = new List<string>(_usedPetitions),
            HasPending = _pending != null,
            Pending = _pending,
            PendingWait = _pendingWait,
            SinceLast = _sinceLast,
            RiotFired = _riotFired,
            DepartedTotal = DepartedTotal,
        });

        public override void Restore(string state, Func<int, ModuleInstance> moduleAt)
        {
            var s = string.IsNullOrEmpty(state) ? null : JsonUtility.FromJson<State>(state);
            if (s == null)
                return;
            _fired.Clear();
            if (s.Fired != null)
                _fired.AddRange(s.Fired);
            _usedPetitions.Clear();
            if (s.UsedPetitions != null)
                _usedPetitions.AddRange(s.UsedPetitions);
            _pending = s.HasPending ? s.Pending : null;
            _pendingWait = s.PendingWait;
            _sinceLast = s.SinceLast;
            _riotFired = s.RiotFired;
            DepartedTotal = s.DepartedTotal;
        }
    }
}
