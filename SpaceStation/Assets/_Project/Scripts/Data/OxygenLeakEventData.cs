using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>산소 누출: 현재 산소 재고의 일정 비율을 즉시 잃는다.</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Events/Oxygen Leak", fileName = "EV_OxygenLeak")]
    public sealed class OxygenLeakEventData : GameEventData
    {
        [Tooltip("현재 산소 재고 대비 손실 비율")]
        [SerializeField, Range(0f, 1f)] private float _stockLossRatio;

        public float StockLossRatio => _stockLossRatio;
    }
}
