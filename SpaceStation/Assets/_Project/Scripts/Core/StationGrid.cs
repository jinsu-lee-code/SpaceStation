using System;
using System.Collections.Generic;
using SpaceStation.Data;
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
        private readonly List<ModuleInstance> _keepClearModules = new List<ModuleInstance>();
        private int _nextId = 1;

        public event Action<ModuleInstance> ModulePlaced;
        public event Action<ModuleInstance> ModuleRemoved;
        /// <summary>12-3b: 같은 모듈이 자리를 옮김 (모듈, 옮기기 전 칸).</summary>
        public event Action<ModuleInstance, IReadOnlyList<Vector3Int>> ModuleMoved;

        public IReadOnlyList<ModuleInstance> Modules => _modules;
        /// <summary>비워 둘 칸(<see cref="ModuleData.KeepClearCells"/>)이 있는 모듈만 — 배치 판정이 칸마다 모든 모듈을 훑지 않게 (봇 자리 탐색).</summary>
        public IReadOnlyList<ModuleInstance> KeepClearModules => _keepClearModules;
        /// <summary>12-3c-opt: 배치 · 철거 · 이동마다 1씩 오름 (모양이 바뀌었는지 캐시가 확인).</summary>
        public int Version { get; private set; }
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

        public bool CanPlace(ModuleData data, Vector3Int origin, int rotation)
        {
            return data != null && CanPlace(data.CellOffsets, origin, rotation);
        }

        public bool TryPlace(ModuleData data, Vector3Int origin, int rotation, out ModuleInstance module)
        {
            if (data == null)
            {
                module = null;
                return false;
            }
            return TryPlace(data.CellOffsets, origin, rotation, out module, data);
        }

        public bool TryPlace(IReadOnlyList<Vector3Int> localOffsets, Vector3Int origin, int rotation, out ModuleInstance module)
        {
            return TryPlace(localOffsets, origin, rotation, out module, null);
        }

        private bool TryPlace(IReadOnlyList<Vector3Int> localOffsets, Vector3Int origin, int rotation, out ModuleInstance module, ModuleData data)
        {
            module = null;
            if (localOffsets == null || localOffsets.Count == 0)
                return false;

            var cells = ResolveCells(localOffsets, origin, rotation);
            if (!AreCellsFree(cells))
                return false;

            module = new ModuleInstance(_nextId++, data, origin, GridDirections.NormalizeRotation(rotation), cells);
            foreach (var cell in cells)
                _occupancy.Add(cell, module);
            _modules.Add(module);
            if (data != null && data.KeepClearCells.Count > 0)
                _keepClearModules.Add(module);
            Version++;

            ModulePlaced?.Invoke(module);
            return true;
        }

        public bool Remove(ModuleInstance module)
        {
            if (module == null || !_modules.Remove(module))
                return false;

            foreach (var cell in module.Cells)
                _occupancy.Remove(cell);
            _keepClearModules.Remove(module);
            Version++;

            ModuleRemoved?.Invoke(module);
            return true;
        }

        /// <summary>12-3b: ignore 모듈이 차지한 칸은 비어 있다고 보고 배치할 수 있는지 (옮길 자리 판정).</summary>
        public bool CanPlace(IReadOnlyList<Vector3Int> localOffsets, Vector3Int origin, int rotation, ModuleInstance ignore)
        {
            if (localOffsets == null || localOffsets.Count == 0)
                return false;
            return AreCellsFree(ResolveCells(localOffsets, origin, rotation), ignore);
        }

        /// <summary>
        /// 12-3b: 모듈을 다른 자리 · 회전으로 옮긴다 (같은 인스턴스, 철거 · 배치 알림 없이 <see cref="ModuleMoved"/> 하나).
        /// 점유 충돌만 검사한다 (자기 칸과 겹치는 자리는 허용). 규칙 판정은 바깥에서.
        /// </summary>
        public bool TryMove(ModuleInstance module, Vector3Int origin, int rotation)
        {
            if (module == null || module.Data == null || !_modules.Contains(module))
                return false;
            var cells = ResolveCells(module.Data.CellOffsets, origin, rotation);
            if (!AreCellsFree(cells, module))
                return false;

            var oldCells = module.Cells;
            foreach (var cell in oldCells)
                _occupancy.Remove(cell);
            module.Relocate(origin, GridDirections.NormalizeRotation(rotation), cells);
            foreach (var cell in cells)
                _occupancy.Add(cell, module);
            Version++;

            ModuleMoved?.Invoke(module, oldCells);
            return true;
        }

        /// <summary>
        /// CONNECT M7 묶음 옮기기: 여러 모듈을 한 번에 새 자리 · 회전으로 (묶음이 차지한 칸은 비어 있다고 봄).
        /// 모두 옮긴 뒤 모듈마다 <see cref="ModuleMoved"/> (받는 쪽은 이미 최종 배치를 봄). 점유 충돌만 검사한다.
        /// </summary>
        public bool TryMoveGroup(IReadOnlyList<ModuleInstance> modules, IReadOnlyList<Vector3Int> origins, IReadOnlyList<int> rotations)
        {
            if (!ResolveGroup(modules, origins, rotations, out var newCells))
                return false;
            var oldCells = new IReadOnlyList<Vector3Int>[modules.Count];
            for (int i = 0; i < modules.Count; i++)
                oldCells[i] = modules[i].Cells;
            ApplyGroup(modules, origins, rotations, newCells);
            Version++;
            for (int i = 0; i < modules.Count; i++)
                ModuleMoved?.Invoke(modules[i], oldCells[i]);
            return true;
        }

        /// <summary>
        /// CONNECT M7 묶음 옮기기 판정용: 묶음을 새 자리에 잠깐 옮겨 둔 채 check를 부르고 되돌린다 (알림 · <see cref="Version"/> 변화 없음).
        /// 칸이 겹치거나 묶음이 잘못됐으면 check 없이 <see cref="PlacementResult.Occupied"/>.
        /// </summary>
        public PlacementResult PeekGroupMove(IReadOnlyList<ModuleInstance> modules, IReadOnlyList<Vector3Int> origins, IReadOnlyList<int> rotations, Func<PlacementResult> check)
        {
            if (check == null || !ResolveGroup(modules, origins, rotations, out var newCells))
                return PlacementResult.Occupied;
            var oldOrigins = new Vector3Int[modules.Count];
            var oldRotations = new int[modules.Count];
            var oldCells = new Vector3Int[modules.Count][];
            for (int i = 0; i < modules.Count; i++)
            {
                oldOrigins[i] = modules[i].Origin;
                oldRotations[i] = modules[i].Rotation;
                oldCells[i] = AsArray(modules[i].Cells); // 같은 배열로 되돌림
            }
            ApplyGroup(modules, origins, rotations, newCells);
            try
            {
                return check();
            }
            finally
            {
                ApplyGroup(modules, oldOrigins, oldRotations, oldCells);
            }
        }

        /// <summary>묶음의 새 칸 계산 + 점유 검사 (묶음 밖 모듈과 겹치거나 묶음끼리 겹치면 false).</summary>
        private bool ResolveGroup(IReadOnlyList<ModuleInstance> modules, IReadOnlyList<Vector3Int> origins, IReadOnlyList<int> rotations, out Vector3Int[][] newCells)
        {
            newCells = null;
            if (modules == null || origins == null || rotations == null || modules.Count == 0
                || origins.Count != modules.Count || rotations.Count != modules.Count)
                return false;
            var group = new HashSet<ModuleInstance>();
            foreach (var m in modules)
            {
                if (m == null || m.Data == null || m.Cells.Count == 0 || !TryGetModule(m.Cells[0], out var at) || at != m || !group.Add(m))
                    return false;
            }
            var taken = new HashSet<Vector3Int>();
            newCells = new Vector3Int[modules.Count][];
            for (int i = 0; i < modules.Count; i++)
            {
                var cells = ResolveCells(modules[i].Data.CellOffsets, origins[i], rotations[i]);
                foreach (var cell in cells)
                {
                    if (!taken.Add(cell))
                        return false;
                    if (_occupancy.TryGetValue(cell, out var occupant) && !group.Contains(occupant))
                        return false;
                }
                newCells[i] = cells;
            }
            return true;
        }

        private static Vector3Int[] AsArray(IReadOnlyList<Vector3Int> cells)
        {
            if (cells is Vector3Int[] array)
                return array;
            var copy = new Vector3Int[cells.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = cells[i];
            return copy;
        }

        private void ApplyGroup(IReadOnlyList<ModuleInstance> modules, IReadOnlyList<Vector3Int> origins, IReadOnlyList<int> rotations, Vector3Int[][] cells)
        {
            foreach (var m in modules)
            {
                foreach (var cell in m.Cells)
                    _occupancy.Remove(cell);
            }
            for (int i = 0; i < modules.Count; i++)
            {
                modules[i].Relocate(origins[i], GridDirections.NormalizeRotation(rotations[i]), cells[i]);
                foreach (var cell in cells[i])
                    _occupancy.Add(cell, modules[i]);
            }
        }

        /// <summary>모듈의 어느 셀과든 면이 맞닿은 다른 모듈들 (중복 없음).</summary>
        public List<ModuleInstance> GetNeighborModules(ModuleInstance module)
        {
            var result = new List<ModuleInstance>();
            GetNeighborModules(module, result);
            return result;
        }

        /// <summary>할당 없는 버전. results를 비우고 채운다.</summary>
        public void GetNeighborModules(ModuleInstance module, List<ModuleInstance> results)
        {
            results.Clear();
            if (module == null)
                return;

            foreach (var cell in module.Cells)
            {
                foreach (var dir in GridDirections.Faces)
                {
                    if (_occupancy.TryGetValue(cell + dir, out var other) && other != module && !results.Contains(other))
                        results.Add(other);
                }
            }
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

        private bool AreCellsFree(Vector3Int[] cells, ModuleInstance ignore = null)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (_occupancy.TryGetValue(cells[i], out var occupant) && occupant != ignore)
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
