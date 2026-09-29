using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>보급선 도착: 자원 보너스 (저장 한도 초과분은 버림).</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Events/Supply Ship", fileName = "EV_SupplyShip")]
    public sealed class SupplyShipEventData : GameEventData
    {
        [SerializeField] private List<ResourceAmount> _rewards = new List<ResourceAmount>();

        public IReadOnlyList<ResourceAmount> Rewards => _rewards;
    }
}
