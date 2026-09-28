using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Core
{
    /// <summary>
    /// 정거장 점유 정보의 유일한 저장소. 배치/제거/조회와 6방향 이웃 조회를 담당한다.
    /// 배치 "규칙"(인접 필수, 비용 등)은 여기서 판단하지 않는다. 점유 충돌만 검사한다.
    /// </summary>
    public sealed class StationGrid
    {
        private readonly Dictionary<Vector3Int, ModuleInstance> _occupancy = new Dictionary<Vector3Int, ModuleInstance>();
        private readonly List<ModuleInstance> _modules = new List<ModuleInstance>();
        private int _nextId = 1;

        public event Action<ModuleInstance> ModulePlaced;
        public event Action<ModuleInstance> ModuleRemoved;

        public IReadOnlyList<ModuleInstance> Modules => _modules;
        public int ModuleCount => _modules.Count;

        public bool IsOccupied(Vector3Int cell)
        {
            return _occupancy.ContainsKey(cell);
        }

        public bool TryGetModule(Vector3Int cell, out ModuleInstance module)
        {
            return _occupancy.TryGetValue(cell, out module);
        }

        /// <summary>로컬 오프셋을 회전·이동해 실제 점유할 셀 목록을 계산한다.</summary>
        public static Vector3Int[] ResolveCells(IReadOnlyList<Vector3Int> localOffsets, Vector3Int origin, int rotation)
        {
            var cells = new Vector3Int[localOffsets.Count];
            for (int i = 0; i < localOffsets.Count; i++)
                cells[i] = origin + GridDirections.Rotate(localOffsets[i], rotation);
            return cells;
        }

        public bool CanPlace(IReadOnlyList<Vector3Int> localOffsets, Vector3Int origin, int rotation)
        {
            if (localOffsets == null || localOffsets.Count == 0)
                return false;
            return AreCellsFree(ResolveCells(localOffsets, origin, rotation));
        }

        public bool TryPlace(IReadOnlyList<Vector3Int> localOffsets, Vector3Int origin, int rotation, out ModuleInstance module)
        {
            module = null;
            if (localOffsets == null || localOffsets.Count == 0)
                return false;

            var cells = ResolveCells(localOffsets, origin, rotation);
            if (!AreCellsFree(cells))
                return false;

            module = new ModuleInstance(_nextId++, origin, GridDirections.NormalizeRotation(rotation), cells);
            foreach (var cell in cells)
                _occupancy.Add(cell, module);
            _modules.Add(module);

            ModulePlaced?.Invoke(module);
            return true;
        }

        public bool Remove(ModuleInstance module)
        {
            if (module == null || !_modules.Remove(module))
                return false;

            foreach (var cell in module.Cells)
                _occupancy.Remove(cell);

            ModuleRemoved?.Invoke(module);
            return true;
        }

        /// <summary>모듈의 어느 셀과든 면이 맞닿은 다른 모듈들 (중복 없음).</summary>
        public List<ModuleInstance> GetNeighborModules(ModuleInstance module)
        {
            var result = new List<ModuleInstance>();
            if (module == null)
                return result;

            foreach (var cell in module.Cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    if (_occupancy.TryGetValue(cell + dir, out var other) && other != module && !result.Contains(other))
                        result.Add(other);
                }
            }
            return result;
        }

        /// <summary>셀과 면이 맞닿은 점유 셀 개수 (0~6).</summary>
        public int CountOccupiedFaces(Vector3Int cell)
        {
            int count = 0;
            foreach (var dir in GridDirections.Faces)
            {
                if (_occupancy.ContainsKey(cell + dir))
                    count++;
            }
            return count;
        }

        private bool AreCellsFree(Vector3Int[] cells)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (_occupancy.ContainsKey(cells[i]))
                    return false;
                // 오프셋 목록 자체에 중복 셀이 있으면 잘못된 정의로 본다.
                for (int j = 0; j < i; j++)
                {
                    if (cells[j] == cells[i])
                        return false;
                }
            }
            return true;
        }
    }
}
