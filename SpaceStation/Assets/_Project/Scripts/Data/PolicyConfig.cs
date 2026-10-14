using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 12-3c 국가급 핵심 규칙 — 상시 정책 + 사회 위기 선택지 (그레이박스, BALANCE 35번). 메뉴 SpaceStation/Grade Rules/Setup Policies가 만든다.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Policy Config", fileName = "PolicyConfig")]
    public sealed class PolicyConfig : ScriptableObject
    {
        [Header("정책")]
        [SerializeField] private List<PolicyData> _policies = new List<PolicyData>();
        [Tooltip("등급 번호별 정책 슬롯 수 (목록보다 높은 등급은 마지막 값). 0이면 정책 없음")]
        [SerializeField] private List<int> _slotsByGrade = new List<int> { 0, 0, 0, 0, 0, 2, 3, 4 };
        [Tooltip("새 정책이 효과를 내기까지 (초)")]
        [SerializeField, Min(0f)] private float _applyDelay = 30f;
        [Tooltip("한 슬롯을 바꾼 뒤 다시 바꿀 수 있기까지 (초)")]
        [SerializeField, Min(0f)] private float _swapCooldown = 300f;

        [Header("사회 위기 시험 사건: 파업")]
        [Tooltip("이 등급부터 (5 = 국가급)")]
        [SerializeField, Min(0)] private int _crisisMinGrade = 5;
        [Tooltip("만족도가 이 값 아래로")]
        [SerializeField] private float _strikeThreshold = 50f;
        [Tooltip("이 시간(초) 이어지면 경고")]
        [SerializeField, Min(0f)] private float _strikeBrewSeconds = 30f;
        [Tooltip("경고 → 파업까지 (초, 감시 강화 배율)")]
        [SerializeField, Min(1f)] private float _strikeWarningSeconds = 90f;
        [Tooltip("경고 중 만족도가 기준 + 이 값 이상으로 오르면 저절로 풀림")]
        [SerializeField, Min(0f)] private float _strikeRecoverMargin = 10f;
        [Tooltip("파업 지속 (초)")]
        [SerializeField, Min(1f)] private float _strikeDuration = 60f;
        [Tooltip("파업 중 생산 배율")]
        [SerializeField, Range(0f, 1f)] private float _strikeProduction = 0.6f;
        [Tooltip("사건이 끝난 뒤 다시 일어나기까지 최소 (초)")]
        [SerializeField, Min(0f)] private float _crisisCooldown = 300f;
        [Header("선택지")]
        [Tooltip("무마: 자원을 내고 해소 + 만족도")]
        [SerializeField] private List<ResourceAmount> _bribeCost = new List<ResourceAmount> { new ResourceAmount(ResourceType.Metal, 150f), new ResourceAmount(ResourceType.Food, 60f) };
        [SerializeField] private float _bribeSatisfaction = 5f;
        [Tooltip("강경 대응: 즉시 해소, 만족도 감소 + 주민 이탈")]
        [SerializeField] private float _crackdownSatisfaction = -10f;
        [SerializeField, Min(0)] private int _crackdownLeave = 3;
        [Tooltip("일시 정책 '임금 인상': 이 시간 동안 아래 효과")]
        [SerializeField, Min(1f)] private float _wageSeconds = 180f;
        [SerializeField] private float _wageSatisfactionCap = 15f;
        [SerializeField, Range(0f, 2f)] private float _wageProduction = 0.85f;

        public IReadOnlyList<PolicyData> Policies => _policies;
        public float ApplyDelay => _applyDelay;
        public float SwapCooldown => _swapCooldown;
        public int CrisisMinGrade => _crisisMinGrade;
        public float StrikeThreshold => _strikeThreshold;
        public float StrikeBrewSeconds => _strikeBrewSeconds;
        public float StrikeWarningSeconds => _strikeWarningSeconds;
        public float StrikeRecoverMargin => _strikeRecoverMargin;
        public float StrikeDuration => _strikeDuration;
        public float StrikeProduction => _strikeProduction;
        public float CrisisCooldown => _crisisCooldown;
        public IReadOnlyList<ResourceAmount> BribeCost => _bribeCost;
        public float BribeSatisfaction => _bribeSatisfaction;
        public float CrackdownSatisfaction => _crackdownSatisfaction;
        public int CrackdownLeave => _crackdownLeave;
        public float WageSeconds => _wageSeconds;
        public float WageSatisfactionCap => _wageSatisfactionCap;
        public float WageProduction => _wageProduction;

        public int SlotsAt(int grade)
        {
            if (_slotsByGrade.Count == 0 || grade < 0)
                return 0;
            return Mathf.Max(0, _slotsByGrade[Mathf.Min(grade, _slotsByGrade.Count - 1)]);
        }

        /// <summary>에디터 설정 메뉴용.</summary>
        public void SetPolicies(List<PolicyData> policies) => _policies = policies;
    }
}
