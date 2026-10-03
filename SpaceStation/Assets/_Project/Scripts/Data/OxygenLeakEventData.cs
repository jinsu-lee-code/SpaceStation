using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>산소 누출: 현재 산소 재고의 일정 비율을 즉시 잃는다.</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Events/Oxygen Leak", fileName = "EV_OxygenLeak")]
    public sealed class OxygenLeakEventData : GameEventData
    {
        [Tooltip("현재 산소 재고 대비 손실 비율")]
        [SerializeField, Range(0f, 1f)] private float _stockLossRatio;
        [Tooltip("8-6: 등급 강도 배율을 곱한 뒤의 최대 손실 비율 (대형·초대형에서 재고가 한 번에 전부 날아가지 않게)")]
        [SerializeField, Range(0f, 1f)] private float _maxLossRatio = 1f;

        public float StockLossRatio => _stockLossRatio;
        public float MaxLossRatio => _maxLossRatio;
    }
}
