using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>태양 폭풍: 지속시간 동안 모든 전력 생산에 배율을 곱한다.</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Events/Solar Storm", fileName = "EV_SolarStorm")]
    public sealed class SolarStormEventData : GameEventData
    {
        [Tooltip("폭풍 중 전력 생산 배율 (0.6 = -40%)")]
        [SerializeField, Range(0f, 1f)] private float _powerSupplyMultiplier = 1f;

        public float PowerSupplyMultiplier => _powerSupplyMultiplier;
    }
}
