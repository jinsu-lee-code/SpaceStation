using System.Collections.Generic;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Core
{
    /// <summary>격자에 배치된 모듈 하나. 점유 셀은 배치 시점에 월드 셀 좌표로 확정된다.</summary>
    public sealed class ModuleInstance
    {
        public int Id { get; }
        /// <summary>모듈 정의. 오프셋만으로 배치한 경우(테스트 등) null.</summary>
        public ModuleData Data { get; }
        public Vector3Int Origin { get; }
        public int Rotation { get; }
        public IReadOnlyList<Vector3Int> Cells { get; }

        internal ModuleInstance(int id, ModuleData data, Vector3Int origin, int rotation, Vector3Int[] cells)
        {
            Id = id;
            Data = data;
            Origin = origin;
            Rotation = rotation;
            Cells = cells;
        }

        public override string ToString()
        {
            return Data != null ? $"{Data.DisplayName}#{Id}@{Origin}" : $"Module#{Id}@{Origin}";
        }
    }
}
