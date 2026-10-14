using SpaceStation.Core;
using UnityEngine;

namespace SpaceStation.Building
{
    /// <summary>
    /// 12-3c (사용자 결정 2026-10-14): 표기 라벨(열 · 파손)을 누르면 그 모듈로 카메라를 옮기고 선택.
    /// 보는 각도는 그대로, 너무 멀면 가까이. 같은 클릭이 모듈 선택 · 배치로 가지 않게 소비한다.
    /// </summary>
    public static class ModuleFocus
    {
        private const float MaxDistance = 10f;
        private static OrbitCameraController _orbit;
        private static ModuleSelectionController _selection;

        public static void Focus(ModuleInstance module)
        {
            if (module == null)
                return;
            if (_orbit == null)
                _orbit = Object.FindFirstObjectByType<OrbitCameraController>();
            if (_selection == null)
                _selection = Object.FindFirstObjectByType<ModuleSelectionController>();
            if (_orbit != null)
            {
                var rig = _orbit.Rig;
                Vector3 sum = Vector3.zero;
                foreach (var c in module.Cells)
                    sum += GridConfig.CellToWorld(c);
                _orbit.Restore(sum / module.Cells.Count, rig.Yaw, rig.Pitch, Mathf.Min(rig.Distance, MaxDistance));
            }
            if (_selection != null)
                _selection.Select(module);
            InputGate.ConsumeClick();
        }
    }
}
