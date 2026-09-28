using System;

namespace SpaceStation.Data
{
    public enum ResourceType
    {
        Power,
        Oxygen,
        Water,
        Food,
        Metal,
    }

    /// <summary>자원 종류 + 양. 전력은 초당 플로우, 나머지는 틱당 스톡 변화량으로 해석한다.</summary>
    [Serializable]
    public struct ResourceAmount
    {
        public ResourceType Type;
        public float Amount;

        public ResourceAmount(ResourceType type, float amount)
        {
            Type = type;
            Amount = amount;
        }
    }
}
