using System.Collections.Generic;

namespace SpaceStation.Core
{
    /// <summary>
    /// 모듈 간 연결 판정 규칙. 면 인접(A안)을 사용한다 (포트 방식 B안은 폐기).
    /// </summary>
    public interface IConnectionRule
    {
        /// <summary>module과 연결된 모듈들을 results에 채운다 (results는 비우고 시작).</summary>
        void GetConnectedModules(StationGrid grid, ModuleInstance module, List<ModuleInstance> results);
    }

    /// <summary>A안: 면이 맞닿은 모듈은 6방향 모두 자동 연결.</summary>
    public sealed class FaceAdjacencyConnectionRule : IConnectionRule
    {
        public void GetConnectedModules(StationGrid grid, ModuleInstance module, List<ModuleInstance> results)
        {
            grid.GetNeighborModules(module, results);
        }
    }
}
