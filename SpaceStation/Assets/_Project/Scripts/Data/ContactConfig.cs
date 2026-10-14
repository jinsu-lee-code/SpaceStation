using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>방문 세력 (12-3c 지구급).</summary>
    public enum ContactFaction
    {
        /// <summary>상인 연합 — 교역 = 자원 맞교환.</summary>
        Merchant,
        /// <summary>학술 함대 — 교역 = 자원을 주고 연구 진행.</summary>
        Scholar,
        /// <summary>무법 선단 — 교역 = 통행료, 경계하면 습격할 수 있음.</summary>
        Outlaw,
    }

    /// <summary>방문 때 고르는 대응.</summary>
    public enum ContactChoice
    {
        None,
        Trade,
        Negotiate,
        Guard,
    }

    /// <summary>평판 단계 (적대 · 냉담 · 중립 · 우호 · 동맹).</summary>
    public enum ReputationTier
    {
        Hostile,
        Wary,
        Neutral,
        Friendly,
        Allied,
    }

    /// <summary>
    /// 12-3c 지구급 핵심 규칙 — 방문 세력 + 평판 (그레이박스, 사용자 결정 2026-10-14, BALANCE 37번).
    /// 메뉴 SpaceStation/Grade Rules/Setup Contact가 만든다.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Contact Config", fileName = "ContactConfig")]
    public sealed class ContactConfig : ScriptableObject
    {
        [Header("방문")]
        [Tooltip("이 등급부터 (7 = 지구급)")]
        [SerializeField, Min(0)] private int _minGrade = 7;
        [Tooltip("방문 사이 간격 최소 · 최대 (게임 초). 첫 방문도 등급 도달 뒤 이 간격")]
        [SerializeField] private Vector2 _interval = new Vector2(240f, 360f);
        [Tooltip("접근 예보 → 도착까지 (초). 이 동안 신호 해독")]
        [SerializeField, Min(1f)] private float _approachSeconds = 60f;
        [Tooltip("도착 뒤 대응을 기다려 주는 시간 (초). 다 지나면 '경계'로 처리")]
        [SerializeField, Min(1f)] private float _waitSeconds = 45f;

        [Header("신호 해독 (통신 중계소)")]
        [Tooltip("계급별 해독에 필요한 통신 세기 (말단 · 중간 간부 · 간부 · 수뇌). 중계소 = 1, 상위 모듈 = 더 큼")]
        [SerializeField] private List<int> _rankPower = new List<int> { 1, 2, 4, 8 };
        [Tooltip("기준 세기에서 해독 시간 (초). 기준을 넘는 세기 1마다 빨라짐")]
        [SerializeField, Min(1f)] private float _decodeSeconds = 30f;
        [Tooltip("기준을 넘는 세기 1마다 해독 속도 +")]
        [SerializeField, Min(0f)] private float _decodeSpeedPerExtra = 0.5f;
        [Tooltip("이 방문 횟수마다 올 수 있는 최고 계급이 한 단계 오름 (끝없이 커지는 방문)")]
        [SerializeField, Min(1)] private int _visitsPerRank = 3;

        [Header("규모 (끝없이 커짐)")]
        [Tooltip("계급 한 단계마다 요구 · 보상 · 습격 배율 +")]
        [SerializeField, Min(0f)] private float _rankScale = 0.6f;
        [Tooltip("방문 한 번마다 배율 +")]
        [SerializeField, Min(0f)] private float _growthPerVisit = 0.05f;
        [Tooltip("비용은 그 자원 저장 한도의 이 비율을 넘지 않음 (언제든 고를 수는 있게)")]
        [SerializeField, Range(0.1f, 1f)] private float _costCapRatio = 0.85f;

        [Header("평판")]
        [Tooltip("평판 범위 −100 ~ +100. 단계 경계: 적대 ≤ −60 < 냉담 ≤ −20 < 중립 < 20 ≤ 우호 < 60 ≤ 동맹")]
        [SerializeField] private float _hostileBelow = -60f;
        [SerializeField] private float _waryBelow = -20f;
        [SerializeField] private float _friendlyFrom = 20f;
        [SerializeField] private float _alliedFrom = 60f;
        [Tooltip("교역 · 협상 · 경계의 평판 변화 (상인 · 학술)")]
        [SerializeField] private float _tradeRep = 5f;
        [SerializeField] private float _negotiateRep = 15f;
        [SerializeField] private float _guardRep = -5f;
        [Tooltip("무법 선단: 통행료 · 협상 · 경계")]
        [SerializeField] private float _outlawTollRep = 3f;
        [SerializeField] private float _outlawNegotiateRep = 12f;
        [SerializeField] private float _outlawGuardRep = -8f;
        [Tooltip("한 세력과 가까워지면 경쟁 세력과는 이만큼 멀어짐 (협상 · 교역의 평판 변화 × 이 비율, 상인 ↔ 무법)")]
        [SerializeField, Range(0f, 1f)] private float _rivalRatio = 0.4f;
        [Tooltip("평판 단계별 보상 배율 (적대 · 냉담 · 중립 · 우호 · 동맹)")]
        [SerializeField] private List<float> _tierReward = new List<float> { 0.5f, 0.75f, 1f, 1.25f, 1.5f };

        [Header("직전 선택 기억 (그 세력의 다음 방문)")]
        [Tooltip("직전에 경계했으면: 평판 변화 배율 · 제안 배율 · 무법 습격 확률 +")]
        [SerializeField] private float _afterGuardRep = 0.5f;
        [SerializeField] private float _afterGuardOffer = 0.8f;
        [SerializeField, Range(0f, 1f)] private float _afterGuardRaid = 0.25f;
        [Tooltip("직전에 협상했으면: 평판 변화 배율 · 제안 배율")]
        [SerializeField] private float _afterNegotiateRep = 1.25f;
        [SerializeField] private float _afterNegotiateOffer = 1.2f;

        [Header("선택지 비용 · 보상 (규모 배율 전)")]
        [Tooltip("상인 교역: 금속을 주고 가장 모자란 생활 자원(산소 · 물 · 식량)을 받음")]
        [SerializeField, Min(0f)] private float _merchantGive = 120f;
        [SerializeField, Min(0f)] private float _merchantGet = 90f;
        [Tooltip("학술 교역: 금속을 주고 진행 중인 연구마다 진행률 +")]
        [SerializeField, Min(0f)] private float _scholarGive = 150f;
        [SerializeField, Range(0f, 1f)] private float _scholarProgress = 0.15f;
        [Tooltip("무법 통행료 (금속)")]
        [SerializeField, Min(0f)] private float _outlawToll = 100f;
        [Tooltip("협상 비용 (금속, 모든 세력)")]
        [SerializeField, Min(0f)] private float _negotiateCost = 250f;
        [Tooltip("우호 이상 상인 · 학술이 도착할 때 주는 선물 (금속 / 연구 진행률, 단계 보상 배율)")]
        [SerializeField, Min(0f)] private float _giftMetal = 60f;
        [SerializeField, Range(0f, 1f)] private float _giftProgress = 0.05f;

        [Header("습격 (포격 · 침입 드론 — 동시에 일어나지 않음)")]
        [Tooltip("무법 선단을 경계했을 때 습격할 확률 (적대 단계 세력은 협상하지 않으면 항상 습격)")]
        [SerializeField, Range(0f, 1f)] private float _outlawGuardRaidChance = 0.5f;
        [Tooltip("경계 태세로 습격 피해 배율 (포격 파 수 · 드론 수)")]
        [SerializeField, Range(0f, 1f)] private float _guardDefense = 0.6f;
        [Tooltip("포격: 예보 (초) · 파 수 (+ 계급) · 한 파 탄 수 (규모 배율)")]
        [SerializeField, Min(1f)] private float _bombardWarning = 30f;
        [SerializeField, Min(1)] private int _bombardWaves = 2;
        [SerializeField, Min(1f)] private float _bombardHits = 3f;
        [Tooltip("침입 드론: 수 (+ 계급) · 제한 시간 (초) · 끄는 데 누르는 시간 (초)")]
        [SerializeField, Min(1)] private int _boardingDrones = 2;
        [SerializeField, Min(10f)] private float _boardingSeconds = 150f;
        [SerializeField, Min(0.2f)] private float _droneHoldSeconds = 1.5f;
        [Tooltip("침입을 못 막으면: 그 모듈 파손 + 금속 손실 (규모 배율)")]
        [SerializeField, Min(0f)] private float _boardingMetalLoss = 120f;

        public int MinGrade => _minGrade;
        public float IntervalMin => Mathf.Min(_interval.x, _interval.y);
        public float IntervalMax => Mathf.Max(_interval.x, _interval.y);
        public float ApproachSeconds => _approachSeconds;
        public float WaitSeconds => _waitSeconds;
        public int RankCount => Mathf.Max(1, _rankPower.Count);
        public int RankPower(int rank) => _rankPower.Count == 0 ? 1 : Mathf.Max(1, _rankPower[Mathf.Clamp(rank, 0, _rankPower.Count - 1)]);
        public float DecodeSeconds => _decodeSeconds;
        public float DecodeSpeedPerExtra => _decodeSpeedPerExtra;
        public int VisitsPerRank => Mathf.Max(1, _visitsPerRank);
        public float RankScale => _rankScale;
        public float GrowthPerVisit => _growthPerVisit;
        public float CostCapRatio => _costCapRatio;
        public float TradeRep => _tradeRep;
        public float NegotiateRep => _negotiateRep;
        public float GuardRep => _guardRep;
        public float OutlawTollRep => _outlawTollRep;
        public float OutlawNegotiateRep => _outlawNegotiateRep;
        public float OutlawGuardRep => _outlawGuardRep;
        public float RivalRatio => _rivalRatio;
        public float AfterGuardRep => _afterGuardRep;
        public float AfterGuardOffer => _afterGuardOffer;
        public float AfterGuardRaid => _afterGuardRaid;
        public float AfterNegotiateRep => _afterNegotiateRep;
        public float AfterNegotiateOffer => _afterNegotiateOffer;
        public float MerchantGive => _merchantGive;
        public float MerchantGet => _merchantGet;
        public float ScholarGive => _scholarGive;
        public float ScholarProgress => _scholarProgress;
        public float OutlawToll => _outlawToll;
        public float NegotiateCost => _negotiateCost;
        public float GiftMetal => _giftMetal;
        public float GiftProgress => _giftProgress;
        public float OutlawGuardRaidChance => _outlawGuardRaidChance;
        public float GuardDefense => _guardDefense;
        public float BombardWarning => _bombardWarning;
        public int BombardWaves => _bombardWaves;
        public float BombardHits => _bombardHits;
        public int BoardingDrones => _boardingDrones;
        public float BoardingSeconds => _boardingSeconds;
        public float DroneHoldSeconds => _droneHoldSeconds;
        public float BoardingMetalLoss => _boardingMetalLoss;

        public ReputationTier TierOf(float reputation)
        {
            if (reputation <= _hostileBelow) return ReputationTier.Hostile;
            if (reputation <= _waryBelow) return ReputationTier.Wary;
            if (reputation >= _alliedFrom) return ReputationTier.Allied;
            if (reputation >= _friendlyFrom) return ReputationTier.Friendly;
            return ReputationTier.Neutral;
        }

        public float TierReward(ReputationTier tier)
            => _tierReward.Count == 0 ? 1f : _tierReward[Mathf.Clamp((int)tier, 0, _tierReward.Count - 1)];

        public static string FactionName(ContactFaction f) => f switch
        {
            ContactFaction.Merchant => "상인 연합",
            ContactFaction.Scholar => "학술 함대",
            _ => "무법 선단",
        };

        public static string RankName(int rank) => rank switch
        {
            0 => "말단",
            1 => "중간 간부",
            2 => "간부",
            _ => "수뇌",
        };

        public static string TierName(ReputationTier t) => t switch
        {
            ReputationTier.Hostile => "적대",
            ReputationTier.Wary => "냉담", // 선택지 '경계'와 헷갈리지 않게
            ReputationTier.Neutral => "중립",
            ReputationTier.Friendly => "우호",
            _ => "동맹",
        };

        public static string ChoiceName(ContactChoice c, ContactFaction f) => c switch
        {
            ContactChoice.Trade => f == ContactFaction.Outlaw ? "통행료" : "교역",
            ContactChoice.Negotiate => "협상",
            ContactChoice.Guard => "경계",
            _ => "-",
        };
    }
}
