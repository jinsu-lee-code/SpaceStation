using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>운석 충돌: 외곽 모듈 1개 파손. 파손·수리 수치는 BalanceConfig (BALANCE 10번).</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Events/Meteor", fileName = "EV_Meteor")]
    public sealed class MeteorEventData : GameEventData
    {
    }
}
