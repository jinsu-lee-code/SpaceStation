namespace SpaceStation.Data
{
    /// <summary>13-1: 이벤트 규모 (운석 피해 개수 · 간격 · 강도). 장 틀이 있으면 장이, 없으면 등급이 정한다 (BALANCE 13번).</summary>
    public interface IEventScale
    {
        int MeteorHitsMin { get; }
        int MeteorHitsMax { get; }
        float EventIntervalMultiplier { get; }
        float EventIntensityMultiplier { get; }
    }
}
