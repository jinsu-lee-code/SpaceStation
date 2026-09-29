using SpaceStation.Core;
using SpaceStation.Data;

namespace SpaceStation.Building
{
    /// <summary>
    /// 진행도 기반 건설 제한 (등급 해금, 등급별 최대 설치 수).
    /// 위치와 무관하게 "지금 이 모듈을 하나 더 지을 수 있는가"만 판정한다.
    /// </summary>
    public interface IPlacementPolicy
    {
        /// <returns>Valid, ModuleLocked, LimitReached 중 하나.</returns>
        PlacementResult CheckBuildable(ModuleData data, StationGrid grid);
    }
}
