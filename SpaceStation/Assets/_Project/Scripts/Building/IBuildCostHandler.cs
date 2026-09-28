using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Building
{
    /// <summary>
    /// 건설 비용 지불/환급 창구. <see cref="StationController"/>는 자원 시스템을 직접 알지 않고 이 인터페이스만 쓴다.
    /// </summary>
    public interface IBuildCostHandler
    {
        bool CanAfford(IReadOnlyList<ResourceAmount> cost);
        bool TrySpend(IReadOnlyList<ResourceAmount> cost);
        void Refund(IReadOnlyList<ResourceAmount> buildCost);
    }
}
