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
        public float DemolishRefundRate => _demolishRefundRate;
        public float RepairCostRate => _repairCostRate;
    }
}
