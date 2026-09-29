using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>게임 전역 밸런스 수치 (BALANCE.md 6번). 모듈별 수치는 <see cref="ModuleData"/>에 둔다.</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Balance Config", fileName = "BalanceConfig")]
    public sealed class BalanceConfig : ScriptableObject
    {
        [Header("Population")]
        [SerializeField, Min(0)] private int _startingPopulation;
        [Tooltip("거주자 1명당 초당 소비량")]
        [SerializeField] private List<ResourceAmount> _consumptionPerResident = new List<ResourceAmount>();

        [Header("Resources")]
        [SerializeField] private List<ResourceAmount> _startingResources = new List<ResourceAmount>();
        [Tooltip("창고 없이 기본으로 주어지는 저장 한도 (스톡 자원만)")]
        [SerializeField] private List<ResourceAmount> _baseStorageCapacity = new List<ResourceAmount>();

        [Header("Power")]
        [Tooltip("전력이 아무리 부족해도 소비 모듈이 유지하는 최소 효율")]
        [SerializeField, Range(0f, 1f)] private float _minPowerEfficiency;

        [Header("Satisfaction (BALANCE 8번)")]
        [SerializeField, Range(0f, 100f)] private float _startingSatisfaction;
        [Tooltip("해당 자원이 고갈된 동안 초당 만족도 감소량 (고갈된 자원끼리 합산)")]
        [SerializeField] private List<ResourceAmount> _satisfactionPenaltyPerSecond = new List<ResourceAmount>();
        [Tooltip("고갈된 자원이 하나도 없을 때 초당 만족도 회복량")]
        [SerializeField, Min(0f)] private float _satisfactionRecoveryPerSecond;

        [Header("Population Growth")]
        [Tooltip("이 만족도 이상이고 수용 인구에 여유가 있을 때만 증가")]
        [SerializeField, Range(0f, 100f)] private float _growthMinSatisfaction;
        [Tooltip("만족도가 최소값일 때 +1까지 걸리는 초")]
        [SerializeField, Min(0.1f)] private float _growthIntervalAtMinSatisfaction = 1f;
        [Tooltip("만족도 100일 때 +1까지 걸리는 초")]
        [SerializeField, Min(0.1f)] private float _growthIntervalAtMaxSatisfaction = 1f;

        [Header("Population Loss")]
        [Tooltip("산소 고갈 상태가 이어질 때 -1 간격(초)")]
        [SerializeField, Min(0.1f)] private float _oxygenDepletedLossInterval = 1f;
        [Tooltip("이 만족도 미만이면 이탈 시작")]
        [SerializeField, Range(0f, 100f)] private float _lowSatisfactionThreshold;
        [Tooltip("만족도 미달 시 -1 간격(초)")]
        [SerializeField, Min(0.1f)] private float _lowSatisfactionLossInterval = 1f;
        [Tooltip("수용 인구 초과 시 -1 간격(초)")]
        [SerializeField, Min(0.1f)] private float _overcrowdedLossInterval = 1f;

        [Header("Events (BALANCE 9번)")]
        [Tooltip("게임 시작 후 이 시간(초) 동안은 랜덤 이벤트가 없다")]
        [SerializeField, Min(0f)] private float _eventGracePeriod;
        [Tooltip("이벤트 사이 간격 최소(초)")]
        [SerializeField, Min(1f)] private float _eventIntervalMin = 1f;
        [Tooltip("이벤트 사이 간격 최대(초)")]
        [SerializeField, Min(1f)] private float _eventIntervalMax = 1f;

        [Header("Damage / Repair (BALANCE 10번)")]
        [Tooltip("파손 모듈의 생산 배율 (소비는 유지)")]
        [SerializeField, Range(0f, 1f)] private float _damagedProductionMultiplier = 1f;
        [Tooltip("파손 모듈 1개당 초당 산소 누출량 (수리 시작 시 멈춤)")]
        [SerializeField, Min(0f)] private float _damagedOxygenLeakPerSecond;
        [Tooltip("수리에 걸리는 시간(초). 수리 중 생산 0")]
        [SerializeField, Min(0.1f)] private float _repairDuration = 1f;
        [Tooltip("파손 후 수리하지 않으면 파괴되기까지 시간(초)")]
        [SerializeField, Min(0.1f)] private float _destroyAfterSeconds = 1f;
        [Tooltip("운석 피격 가중치 기울기: 가중치 = 기울기 × (노출 면 − 1) + 1. 1이면 노출 면 수에 비례")]
        [SerializeField, Min(0f)] private float _meteorExposureSlope = 3f;

        [Header("Day / Night (BALANCE 14번)")]
        [Tooltip("낮+밤 한 주기(초). 0이면 주기 없음(항상 낮)")]
        [SerializeField, Min(0f)] private float _dayNightPeriod;
        [Tooltip("주기 중 낮 길이(초). 나머지가 밤")]
        [SerializeField, Min(0f)] private float _dayLength;
        [Tooltip("해질녘·새벽 전환 시간(초). 이 동안 출력이 서서히 변함. 0이면 계단식")]
        [SerializeField, Min(0f)] private float _dayNightTransition;
        [Tooltip("밤의 태양광 출력 배율")]
        [SerializeField, Range(0f, 1f)] private float _nightSolarMultiplier;

        [Header("Durability / Maintenance (BALANCE 15번)")]
        [Tooltip("내구도 초당 감소량 (최대 100)")]
        [SerializeField, Min(0f)] private float _durabilityDecayPerSecond;
        [Tooltip("이 내구도 이상이면 효율 100%, 아래로는 0까지 선형으로 0%")]
        [SerializeField, Range(0f, 100f)] private float _durabilityEfficiencyThreshold = 50f;
        [Tooltip("정비 비용 = 건설비 × 이 비율 × (최대 − 현재)/100")]
        [SerializeField, Min(0f)] private float _maintenanceCostRate;
        [Tooltip("정비할 때마다 최대 내구도 감소량")]
        [SerializeField, Min(0f)] private float _maintenanceMaxLoss;
        [Tooltip("최대 내구도 하한")]
        [SerializeField, Range(0f, 100f)] private float _maxDurabilityFloor;
        [Tooltip("운석 피격 시 내구도 감소량 (파손과 별개)")]
        [SerializeField, Min(0f)] private float _meteorDurabilityDamage;

        [Header("Cost")]
        [Tooltip("철거 환급 비율 (건설 비용 대비)")]
        [SerializeField, Range(0f, 1f)] private float _demolishRefundRate;
        [Tooltip("수리 비용 비율 (건설 비용 대비, 3-3)")]
        [SerializeField, Range(0f, 1f)] private float _repairCostRate;

        public int StartingPopulation => _startingPopulation;
        public IReadOnlyList<ResourceAmount> ConsumptionPerResident => _consumptionPerResident;
        public IReadOnlyList<ResourceAmount> StartingResources => _startingResources;
        public IReadOnlyList<ResourceAmount> BaseStorageCapacity => _baseStorageCapacity;
        public float MinPowerEfficiency => _minPowerEfficiency;
        public float StartingSatisfaction => _startingSatisfaction;
        public IReadOnlyList<ResourceAmount> SatisfactionPenaltyPerSecond => _satisfactionPenaltyPerSecond;
        public float SatisfactionRecoveryPerSecond => _satisfactionRecoveryPerSecond;
        public float GrowthMinSatisfaction => _growthMinSatisfaction;
        public float GrowthIntervalAtMinSatisfaction => _growthIntervalAtMinSatisfaction;
        public float GrowthIntervalAtMaxSatisfaction => _growthIntervalAtMaxSatisfaction;
        public float OxygenDepletedLossInterval => _oxygenDepletedLossInterval;
        public float LowSatisfactionThreshold => _lowSatisfactionThreshold;
        public float LowSatisfactionLossInterval => _lowSatisfactionLossInterval;
        public float OvercrowdedLossInterval => _overcrowdedLossInterval;
        public float EventGracePeriod => _eventGracePeriod;
        public float EventIntervalMin => _eventIntervalMin;
        public float EventIntervalMax => _eventIntervalMax;
        public float DamagedProductionMultiplier => _damagedProductionMultiplier;
        public float DamagedOxygenLeakPerSecond => _damagedOxygenLeakPerSecond;
        public float RepairDuration => _repairDuration;
        public float DestroyAfterSeconds => _destroyAfterSeconds;
        public float MeteorExposureSlope => _meteorExposureSlope;
        public float DayNightPeriod => _dayNightPeriod;
        public float DayLength => _dayLength;
        public float DayNightTransition => _dayNightTransition;
        public float NightSolarMultiplier => _nightSolarMultiplier;
        public float DurabilityDecayPerSecond => _durabilityDecayPerSecond;
        public float DurabilityEfficiencyThreshold => _durabilityEfficiencyThreshold;
        public float MaintenanceCostRate => _maintenanceCostRate;
        public float MaintenanceMaxLoss => _maintenanceMaxLoss;
        public float MaxDurabilityFloor => _maxDurabilityFloor;
        public float MeteorDurabilityDamage => _meteorDurabilityDamage;
        public float DemolishRefundRate => _demolishRefundRate;
        public float RepairCostRate => _repairCostRate;
    }
}
