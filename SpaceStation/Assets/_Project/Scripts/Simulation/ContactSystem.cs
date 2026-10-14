using System;
using System.Collections.Generic;
using SpaceStation.Core;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Simulation
{
    /// <summary>
    /// 12-3c 지구급 핵심 규칙 — 방문 세력 + 평판 (사용자 결정 2026-10-14, PHASESET 5번 C-2, BALANCE 37번).
    /// 지구급부터 무작위 간격으로 외부 함선이 접근 (예보 60초 + 도착 뒤 45초 대기):
    /// - 세력 3개(상인 연합 · 학술 함대 · 무법 선단)마다 평판 −100~+100, 단계 적대 · 냉담 · 중립 · 우호 · 동맹.
    /// - 함선 계급 4단계(말단 · 중간 간부 · 간부 · 수뇌). 가동 중인 통신 중계소의 통신 세기 합이 계급 기준(1 · 2 · 4 · 8) 이상이면
    ///   접근하는 동안 신호를 해독(넘는 세기만큼 빨라짐) → 세력 · 계급 · 요구가 보임. 못 하면 '정체 불명'인 채 골라야 함.
    /// - 대응: 교역(상인 = 자원 맞교환, 학술 = 자원 → 연구 진행, 무법 = 통행료) · 협상(큰 비용 → 평판 크게) · 경계(비용 없음, 평판 하락).
    ///   한 세력과 가까워지면 경쟁 세력(상인 · 학술 ↔ 무법)과 멀어짐. 대기 시간이 지나면 경계로 처리.
    /// - 직전 선택 기억: 그 세력의 다음 방문에서 직전에 경계했으면 평판 · 제안이 박하고 무법 습격 확률↑, 협상했으면 후함.
    /// - 습격: 무법 선단을 경계하면 확률로, 적대 단계 세력은 협상하지 않으면 항상. 포격(유성우와 같은 판정) 또는 침입 드론(내부에서 끔) —
    ///   동시에 일어나지 않음 (진행 중이면 끝난 뒤). 경계했으면 규모가 줄어듦.
    /// - 끝없이 커짐: 방문 횟수마다 최고 계급이 오르고 규모 배율(요구 · 보상 · 습격)이 계속 커짐. 우호 이상 상인 · 학술은 도착 때 선물.
    /// 지구급 전에는 아무것도 하지 않음 (기존 결과 그대로).
    /// </summary>
    public sealed class ContactSystem : StationSystem
    {
        public const string SystemKey = "contact";
        public const int FactionCount = 3;

        public enum ContactPhase
        {
            Idle,
            Approaching,
            Waiting,
        }

        public enum RaidKind
        {
            None,
            Bombard,
            Boarding,
        }

        private readonly ContactConfig _config;
        private readonly Func<float> _random01;
        private readonly float[] _reputation = new float[FactionCount];
        private readonly ContactChoice[] _last = new ContactChoice[FactionCount];
        private bool _armed;
        private float _timer;
        // 대기 중인 습격 (진행 중인 습격 · 유성우가 끝나면 시작)
        private RaidKind _pendingRaid;
        private float _pendingScale;
        private int _pendingRank;
        private bool _pendingGuard;
        private string _pendingSource;

        public event Action Changed;

        public ContactSystem(ContactConfig config, Func<float> random01 = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (random01 == null)
            {
                var rng = new System.Random();
                random01 = () => (float)rng.NextDouble();
            }
            _random01 = random01;
        }

        public override string Key => SystemKey;
        public ContactConfig Config => _config;
        public bool Unlocked => Sim.RuleOpen(StationRule.Contact, Sim.Progression.GradeIndex >= _config.MinGrade); // 13-1 규칙을 정하는 쪽이 있으면 그쪽
        public ContactPhase Phase { get; private set; }
        /// <summary>Approaching: 도착까지 / Waiting: 대응 마감까지 / Idle: 다음 방문까지.</summary>
        public float TimeLeft => _timer;
        public int Visits { get; private set; }

        // ---------------- 지금 방문 ----------------
        public ContactFaction Faction { get; private set; }
        public int Rank { get; private set; }
        public float Scale { get; private set; } = 1f;
        /// <summary>0~1, 1이면 해독 완료 (세력 · 계급 · 요구가 보임).</summary>
        public float DecodeProgress { get; private set; }
        public bool Decoded => DecodeProgress >= 1f;
        /// <summary>지금 통신 세기로 이 계급을 해독할 수 있는지.</summary>
        public bool CanDecode => RelayPower >= _config.RankPower(Rank);
        public bool Visiting => Phase != ContactPhase.Idle;

        // ---------------- 침입 드론 ----------------
        public ModuleInstance BoardingTarget { get; private set; }
        public int DronesLeft { get; private set; }
        public int DronesTotal { get; private set; }
        public float BoardingTimeLeft { get; private set; }
        public bool Boarding => BoardingTarget != null && DronesLeft > 0;
        public RaidKind PendingRaid => _pendingRaid;

        public float Reputation(ContactFaction f) => _reputation[(int)f];
        public ReputationTier Tier(ContactFaction f) => _config.TierOf(_reputation[(int)f]);
        public ContactChoice LastChoice(ContactFaction f) => _last[(int)f];

        /// <summary>가동 중인(연결 · 파손 아님 · 합성 중 아님) 모듈의 통신 세기 합.</summary>
        public int RelayPower
        {
            get
            {
                int sum = 0;
                foreach (var m in Sim.Grid.Modules)
                {
                    if (m.Data == null || m.Data.RelayPower <= 0)
                        continue;
                    if (!Sim.Connectivity.IsActive(m) || Sim.Damage.IsDamaged(m) || Sim.IsHalted(m))
                        continue;
                    sum += m.Data.RelayPower;
                }
                return sum + Sim.Effects.RelayPowerBonus; // 12-4 연계 연구 '심우주 청취'
            }
        }

        public override void Tick(float dt)
        {
            if (dt <= 0f || Sim.Session.IsGameOver)
                return;
            TickBoarding(dt);
            TryStartPendingRaid();
            switch (Phase)
            {
                case ContactPhase.Idle:
                    if (Sim.Events.Held || !Unlocked)
                        return; // 튜토리얼 · 지구급 전에는 시계가 멈춤
                    if (!_armed)
                    {
                        _armed = true;
                        _timer = NextInterval();
                        Sim.Report("외부 접촉 시작: 함선이 찾아옵니다 · 통신 중계소가 있어야 신호를 해독해 정체를 미리 압니다 (U: 외부 접촉 창)", false);
                        Changed?.Invoke();
                        return;
                    }
                    _timer -= dt;
                    if (_timer <= 0f)
                        BeginVisit();
                    break;
                case ContactPhase.Approaching:
                    Decode(dt);
                    _timer -= dt;
                    if (_timer <= 0f)
                        Arrive();
                    Changed?.Invoke();
                    break;
                case ContactPhase.Waiting:
                    Decode(dt);
                    _timer -= dt;
                    if (_timer <= 0f)
                    {
                        Sim.Report($"{VisitorName} 함선이 응답을 기다리다 떠납니다 · 경계로 처리", false);
                        Resolve(ContactChoice.Guard);
                    }
                    else
                        Changed?.Invoke();
                    break;
            }
        }

        /// <summary>방문 시작 (확인용 에디터 메뉴도 씀). 세력 · 계급 · 규모를 정하고 예보.</summary>
        public void BeginVisit()
        {
            Faction = PickFaction();
            int ceiling = Math.Min(_config.RankCount - 1, Visits / _config.VisitsPerRank);
            Rank = Math.Max(0, ceiling - (_random01() < 0.5f ? 0 : 1));
            Scale = (1f + Rank * _config.RankScale) * (1f + Visits * _config.GrowthPerVisit);
            DecodeProgress = 0f;
            Phase = ContactPhase.Approaching;
            _timer = _config.ApproachSeconds;
            string decode = CanDecode
                ? "신호 해독 중"
                : $"신호 해독 불가 (통신 세기 {RelayPower} / 이 계급 {_config.RankPower(Rank)} 필요) · 정체를 모른 채 대응해야 합니다";
            Sim.Report($"미확인 함선 접근: {_timer:0}초 뒤 도착 · {decode}", false);
            Changed?.Invoke();
        }

        private ContactFaction PickFaction()
        {
            // 적대 단계 세력은 더 자주 옴 (습격 기회)
            var w = new float[FactionCount];
            float total = 0f;
            for (int i = 0; i < FactionCount; i++)
            {
                w[i] = Tier((ContactFaction)i) == ReputationTier.Hostile ? 1.5f : 1f;
                total += w[i];
            }
            float roll = _random01() * total;
            for (int i = 0; i < FactionCount; i++)
            {
                if (roll < w[i])
                    return (ContactFaction)i;
                roll -= w[i];
            }
            return ContactFaction.Outlaw;
        }

        private void Decode(float dt)
        {
            if (Decoded)
                return;
            int power = RelayPower;
            int need = _config.RankPower(Rank);
            if (power < need)
                return;
            float speed = 1f + _config.DecodeSpeedPerExtra * (power - need);
            DecodeProgress = Math.Min(1f, DecodeProgress + dt * speed / Math.Max(1f, _config.DecodeSeconds));
            if (Decoded)
                Sim.Report($"신호 해독: {VisitorName} 함선 · 평판 {ContactConfig.TierName(Tier(Faction))}", true);
        }

        private void Arrive()
        {
            Phase = ContactPhase.Waiting;
            _timer = _config.WaitSeconds;
            Sim.Report($"{VisitorName} 함선 도착 · {_timer:0}초 안에 교역 · 협상 · 경계 중 대응 (U)", false);
            GiveGift();
        }

        /// <summary>우호 이상 상인 · 학술은 도착하면 선물.</summary>
        private void GiveGift()
        {
            var tier = Tier(Faction);
            if (Faction == ContactFaction.Outlaw || tier < ReputationTier.Friendly)
                return;
            float reward = _config.TierReward(tier);
            if (Faction == ContactFaction.Merchant)
            {
                float metal = Sim.Resources.AddStock(ResourceType.Metal, _config.GiftMetal * reward * Scale);
                Sim.Report($"{VisitorName}의 선물: 금속 +{metal:0}", true);
            }
            else if (AddResearch(_config.GiftProgress * reward) > 0)
                Sim.Report($"{VisitorName}의 선물: 진행 중인 연구 +{_config.GiftProgress * reward * 100f:0}%", true);
        }

        /// <summary>화면용 방문자 이름: 해독 전이면 "정체 불명".</summary>
        public string VisitorName => Decoded ? $"{ContactConfig.FactionName(Faction)} {ContactConfig.RankName(Rank)}" : "정체 불명";

        // ---------------- 선택지 ----------------

        /// <summary>직전 선택에 따른 이번 방문 평판 변화 배율 (늘어나는 쪽만).</summary>
        private float RepGainMultiplier => _last[(int)Faction] switch
        {
            ContactChoice.Guard => _config.AfterGuardRep,
            ContactChoice.Negotiate => _config.AfterNegotiateRep,
            _ => 1f,
        };

        /// <summary>직전 선택 · 평판 단계에 따른 보상 배율.</summary>
        public float OfferMultiplier
        {
            get
            {
                float m = _config.TierReward(Tier(Faction));
                if (_last[(int)Faction] == ContactChoice.Guard)
                    m *= _config.AfterGuardOffer;
                else if (_last[(int)Faction] == ContactChoice.Negotiate)
                    m *= _config.AfterNegotiateOffer;
                return m;
            }
        }

        /// <summary>금속 비용 (규모 배율, 저장 한도 비율 상한).</summary>
        private float MetalCost(float baseAmount)
        {
            float cap = Sim.Resources.GetCapacity(ResourceType.Metal) * _config.CostCapRatio;
            return Math.Min(baseAmount * Scale, Math.Max(1f, cap));
        }

        public float CostOf(ContactChoice choice)
        {
            switch (choice)
            {
                case ContactChoice.Trade:
                    return MetalCost(Faction switch
                    {
                        ContactFaction.Merchant => _config.MerchantGive,
                        ContactFaction.Scholar => _config.ScholarGive,
                        _ => _config.OutlawToll,
                    });
                case ContactChoice.Negotiate:
                    return MetalCost(_config.NegotiateCost);
                default:
                    return 0f;
            }
        }

        /// <summary>상인 교역으로 받을 자원 (산소 · 물 · 식량 중 저장 비율이 가장 낮은 것).</summary>
        public ResourceType MerchantGood
        {
            get
            {
                var best = ResourceType.Food;
                float lowest = float.MaxValue;
                foreach (var t in new[] { ResourceType.Oxygen, ResourceType.Water, ResourceType.Food })
                {
                    float cap = Math.Max(1f, Sim.Resources.GetCapacity(t));
                    float r = Sim.Resources.GetStock(t) / cap;
                    if (r < lowest)
                    {
                        lowest = r;
                        best = t;
                    }
                }
                return best;
            }
        }

        public float MerchantAmount => _config.MerchantGet * Scale * OfferMultiplier;
        public float ScholarAmount => _config.ScholarProgress * OfferMultiplier;

        /// <summary>고를 수 없으면 사유 (해독 전이면 실제 세력 기준으로 판정하지 않음 — 정체를 모르니 도박).</summary>
        public string BlockReason(ContactChoice choice)
        {
            if (!Visiting)
                return "방문 중인 함선 없음";
            if (choice == ContactChoice.Guard || !Decoded)
                return null;
            if (choice == ContactChoice.Trade && Faction != ContactFaction.Outlaw && Tier(Faction) == ReputationTier.Hostile)
                return "적대 관계라 교역을 거절합니다";
            if (choice == ContactChoice.Trade && Faction == ContactFaction.Scholar && Sim.Research.Projects.Count == 0)
                return "진행 중인 연구가 없음";
            if (Sim.Resources.GetStock(ResourceType.Metal) < CostOf(choice))
                return $"금속 부족 ({CostOf(choice):0} 필요)";
            return null;
        }

        public bool Choose(ContactChoice choice)
        {
            if (!Visiting || choice == ContactChoice.None || BlockReason(choice) != null)
                return false;
            Resolve(choice);
            return true;
        }

        private void Resolve(ContactChoice choice)
        {
            string name = $"{ContactConfig.FactionName(Faction)} {ContactConfig.RankName(Rank)}";
            bool wasDecoded = Decoded;
            float gain = RepGainMultiplier;
            // 정체 불명으로 고른 대응을 못 하는 경우 (비용 부족 · 적대 교역 거절 · 연구 없음) → 경계
            if (choice != ContactChoice.Guard && !wasDecoded)
            {
                Decoded_ForResolve();
                string reason = BlockReason(choice);
                if (reason != null)
                {
                    Sim.Report($"정체 불명 함선은 {name}였습니다 · {ContactConfig.ChoiceName(choice, Faction)} 불가({reason}) → 경계로 처리", false);
                    choice = ContactChoice.Guard;
                }
            }
            bool hostile = Tier(Faction) == ReputationTier.Hostile;
            bool raid = false;
            switch (choice)
            {
                case ContactChoice.Trade:
                    Spend(CostOf(choice));
                    if (Faction == ContactFaction.Merchant)
                    {
                        var good = MerchantGood;
                        float got = Sim.Resources.AddStock(good, MerchantAmount);
                        Sim.Report($"교역 · {name}: 금속 -{CostOf(choice):0} → {good.DisplayName()} +{got:0}", true);
                    }
                    else if (Faction == ContactFaction.Scholar)
                    {
                        int n = AddResearch(ScholarAmount);
                        Sim.Report($"교역 · {name}: 금속 -{CostOf(choice):0} → 진행 중인 연구 {n}개 +{ScholarAmount * 100f:0}%", true);
                    }
                    else
                        Sim.Report($"통행료 · {name}: 금속 -{CostOf(choice):0} · 습격하지 않고 지나갑니다", true);
                    AddRep(Faction, (Faction == ContactFaction.Outlaw ? _config.OutlawTollRep : _config.TradeRep) * gain, rivals: true);
                    break;
                case ContactChoice.Negotiate:
                    Spend(CostOf(choice));
                    AddRep(Faction, (Faction == ContactFaction.Outlaw ? _config.OutlawNegotiateRep : _config.NegotiateRep) * gain, rivals: true);
                    Sim.Report($"협상 · {name}: 금속 -{CostOf(choice):0} · 평판 {ContactConfig.TierName(Tier(Faction))} ({Reputation(Faction):+0;-0})", true);
                    break;
                default:
                    AddRep(Faction, Faction == ContactFaction.Outlaw ? _config.OutlawGuardRep : _config.GuardRep, rivals: false);
                    float chance = 0f;
                    if (Faction == ContactFaction.Outlaw)
                        chance = _config.OutlawGuardRaidChance + (_last[(int)Faction] == ContactChoice.Guard ? _config.AfterGuardRaid : 0f);
                    raid = hostile || _random01() < chance;
                    Sim.Report($"경계 · {name}: 평판 {ContactConfig.TierName(Tier(Faction))} ({Reputation(Faction):+0;-0})" + (raid ? " · 함선이 적의를 드러냅니다" : " · 함선이 떠납니다"), !raid);
                    break;
            }
            if (raid)
                QueueRaid(name, choice == ContactChoice.Guard);
            _last[(int)Faction] = choice;
            Visits++;
            Phase = ContactPhase.Idle;
            _timer = NextInterval();
            DecodeProgress = 0f;
            Changed?.Invoke();
        }

        private void Decoded_ForResolve() => DecodeProgress = 1f; // 결과를 보여 주며 정체가 드러남

        private void Spend(float metal)
        {
            if (metal <= 0f)
                return;
            Sim.Resources.TrySpend(new[] { new ResourceAmount(ResourceType.Metal, metal) });
        }

        /// <summary>진행 중인 연구마다 진행률 + (끝내는 건 연구 틱이 함). 올린 연구 수.</summary>
        private int AddResearch(float amount)
        {
            int n = 0;
            foreach (var p in Sim.Research.Projects)
            {
                p.Progress = Math.Min(0.999f, p.Progress + amount);
                n++;
            }
            return n;
        }

        private void AddRep(ContactFaction f, float delta, bool rivals)
        {
            _reputation[(int)f] = Mathf.Clamp(_reputation[(int)f] + delta, -100f, 100f);
            if (!rivals || delta <= 0f)
                return;
            // 경쟁: 상인 · 학술과 가까워지면 무법과 멀어지고, 무법과 가까워지면 상인 · 학술과 멀어짐
            for (int i = 0; i < FactionCount; i++)
            {
                var other = (ContactFaction)i;
                if (other == f || (f != ContactFaction.Outlaw && other != ContactFaction.Outlaw))
                    continue;
                _reputation[i] = Mathf.Clamp(_reputation[i] - delta * _config.RivalRatio, -100f, 100f);
            }
        }

        private float NextInterval() => _config.IntervalMin + _random01() * (_config.IntervalMax - _config.IntervalMin);

        // ---------------- 습격 ----------------

        private void QueueRaid(string source, bool guard)
        {
            _pendingRaid = _random01() < 0.5f ? RaidKind.Bombard : RaidKind.Boarding;
            _pendingScale = Scale;
            _pendingRank = Rank;
            _pendingGuard = guard;
            _pendingSource = source;
            TryStartPendingRaid();
        }

        /// <summary>확인용: 지금 습격 (경계 아님, 지금 규모).</summary>
        public void ForceRaid(RaidKind kind)
        {
            _pendingRaid = kind;
            _pendingScale = Scale;
            _pendingRank = Rank;
            _pendingGuard = false;
            _pendingSource = "무법 선단";
            TryStartPendingRaid();
        }

        private void TryStartPendingRaid()
        {
            if (_pendingRaid == RaidKind.None || Boarding || (Sim.MeteorShower != null && Sim.MeteorShower.Active))
                return; // 동시에 일어나지 않음
            float defense = _pendingGuard ? _config.GuardDefense : 1f;
            if (_pendingRaid == RaidKind.Bombard)
            {
                int waves = Math.Max(1, (int)Math.Round((_config.BombardWaves + _pendingRank) * defense));
                int hits = Math.Max(1, (int)Math.Ceiling(_config.BombardHits * _pendingScale));
                if (Sim.MeteorShower == null || !Sim.MeteorShower.BeginRaid(_config.BombardWarning, waves, hits, _pendingSource))
                    return;
            }
            else if (!BeginBoarding(defense))
                return;
            _pendingRaid = RaidKind.None;
            Changed?.Invoke();
        }

        private readonly List<ModuleInstance> _candidates = new List<ModuleInstance>();

        private bool BeginBoarding(float defense)
        {
            // 바깥에 드러난 가동 중인 모듈 (코어 제외) 중 하나에 붙음
            _candidates.Clear();
            foreach (var m in Sim.Grid.Modules)
            {
                if (m == Sim.Core || !Sim.Connectivity.IsActive(m) || Sim.Damage.IsDamaged(m) || !DamageSystem.IsExterior(Sim.Grid, m))
                    continue;
                _candidates.Add(m);
            }
            if (_candidates.Count == 0)
                return true; // 붙을 곳이 없으면 그냥 끝
            BoardingTarget = _candidates[Math.Min(_candidates.Count - 1, (int)(_random01() * _candidates.Count))];
            _pendingScaleAtBoarding = _pendingScale;
            DronesTotal = DronesLeft = Math.Max(1, (int)Math.Round((_config.BoardingDrones + _pendingRank) * defense));
            BoardingTimeLeft = _config.BoardingSeconds / Math.Max(0.1f, defense); // 경계 태세면 시간 여유
            Sim.Report($"{_pendingSource} 침입: 드론 {DronesTotal}기가 {ModuleName(BoardingTarget)}에 들어왔습니다 · {BoardingTimeLeft:0}초 안에 내부에서 꺼야 합니다 (V 들어가기)", false);
            return true;
        }

        private void TickBoarding(float dt)
        {
            if (BoardingTarget == null)
                return;
            if (!Sim.Grid.TryGetModule(BoardingTarget.Origin, out var still) || still != BoardingTarget)
            {
                Sim.Report("침입 드론이 붙은 모듈이 사라져 드론도 함께 사라졌습니다", true);
                EndBoarding();
                return;
            }
            BoardingTimeLeft -= dt;
            if (BoardingTimeLeft > 0f)
            {
                Changed?.Invoke();
                return;
            }
            float metal = Math.Min(Sim.Resources.GetStock(ResourceType.Metal), _config.BoardingMetalLoss * _pendingScaleAtBoarding);
            Sim.Resources.TrySpend(new[] { new ResourceAmount(ResourceType.Metal, metal) });
            bool damaged = Sim.Damage.Damage(BoardingTarget);
            Sim.Report($"침입 드론을 막지 못했습니다: {ModuleName(BoardingTarget)}{(damaged ? " 파손" : "")} · 금속 -{metal:0}", false);
            EndBoarding();
        }

        private float _pendingScaleAtBoarding = 1f;

        /// <summary>내부에서 드론 하나를 끔 (InteriorBoarding).</summary>
        public void DisableDrone()
        {
            if (!Boarding)
                return;
            DronesLeft--;
            if (DronesLeft <= 0)
            {
                Sim.Report($"침입 드론을 모두 껐습니다 · {ModuleName(BoardingTarget)} 안전", true);
                EndBoarding();
            }
            else
                Changed?.Invoke();
        }

        private void EndBoarding()
        {
            BoardingTarget = null;
            DronesLeft = DronesTotal = 0;
            BoardingTimeLeft = 0f;
            Changed?.Invoke();
        }

        private static string ModuleName(ModuleInstance m) => m != null && m.Data != null ? m.Data.DisplayName : "모듈";

        public override void OnModuleRemoved(ModuleInstance module)
        {
            if (module == BoardingTarget)
            {
                Sim.Report("침입 드론이 붙은 모듈이 사라져 드론도 함께 사라졌습니다", true);
                EndBoarding();
            }
        }

        // ---------------- 세이브 ----------------

        [Serializable]
        private sealed class State
        {
            public bool Armed;
            public int Phase;
            public float Timer;
            public int Visits;
            public int Faction;
            public int Rank;
            public float Scale;
            public float Decode;
            public float[] Reputation;
            public int[] Last;
            public int PendingRaid;
            public float PendingScale;
            public int PendingRank;
            public bool PendingGuard;
            public string PendingSource;
            public int BoardingTarget = -1;
            public int DronesLeft;
            public int DronesTotal;
            public float BoardingTime;
            public float BoardingScale;
        }

        public override string Capture(Func<ModuleInstance, int> indexOf)
        {
            if (!_armed && Visits == 0 && BoardingTarget == null)
                return null;
            var last = new int[FactionCount];
            for (int i = 0; i < FactionCount; i++)
                last[i] = (int)_last[i];
            return JsonUtility.ToJson(new State
            {
                Armed = _armed, Phase = (int)Phase, Timer = _timer, Visits = Visits, Faction = (int)Faction, Rank = Rank, Scale = Scale,
                Decode = DecodeProgress, Reputation = (float[])_reputation.Clone(), Last = last,
                PendingRaid = (int)_pendingRaid, PendingScale = _pendingScale, PendingRank = _pendingRank, PendingGuard = _pendingGuard, PendingSource = _pendingSource,
                BoardingTarget = BoardingTarget != null ? indexOf(BoardingTarget) : -1, DronesLeft = DronesLeft, DronesTotal = DronesTotal,
                BoardingTime = BoardingTimeLeft, BoardingScale = _pendingScaleAtBoarding,
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
            Phase = (ContactPhase)Mathf.Clamp(s.Phase, 0, 2);
            _timer = s.Timer;
            Visits = s.Visits;
            Faction = (ContactFaction)Mathf.Clamp(s.Faction, 0, FactionCount - 1);
            Rank = s.Rank;
            Scale = s.Scale > 0f ? s.Scale : 1f;
            DecodeProgress = s.Decode;
            for (int i = 0; i < FactionCount; i++)
            {
                _reputation[i] = s.Reputation != null && i < s.Reputation.Length ? s.Reputation[i] : 0f;
                _last[i] = s.Last != null && i < s.Last.Length ? (ContactChoice)s.Last[i] : ContactChoice.None;
            }
            _pendingRaid = (RaidKind)Mathf.Clamp(s.PendingRaid, 0, 2);
            _pendingScale = s.PendingScale;
            _pendingRank = s.PendingRank;
            _pendingGuard = s.PendingGuard;
            _pendingSource = s.PendingSource;
            BoardingTarget = s.BoardingTarget >= 0 ? moduleAt(s.BoardingTarget) : null;
            DronesLeft = s.DronesLeft;
            DronesTotal = s.DronesTotal;
            BoardingTimeLeft = s.BoardingTime;
            _pendingScaleAtBoarding = s.BoardingScale > 0f ? s.BoardingScale : 1f;
            if (BoardingTarget == null)
                DronesLeft = DronesTotal = 0;
        }
    }
}
