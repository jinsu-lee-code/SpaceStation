using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>모듈 종류 하나의 정의. 수치는 전부 이 에셋에서 조정한다.</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Module Data", fileName = "MD_NewModule")]
    public sealed class ModuleData : ScriptableObject
    {
        [SerializeField] private string _displayName = "Module";

        [Tooltip("원점(0,0,0) 기준 점유 셀 오프셋. 회전 0 기준으로 정의한다.")]
        [SerializeField] private List<Vector3Int> _cellOffsets = new List<Vector3Int> { Vector3Int.zero };

        [Tooltip("모듈 원점 셀 중심에 피벗이 있는 프리팹")]
        [SerializeField] private GameObject _prefab;

        [SerializeField] private List<ResourceAmount> _buildCost = new List<ResourceAmount>();
        [SerializeField] private List<ResourceAmount> _production = new List<ResourceAmount>();
        [SerializeField] private List<ResourceAmount> _consumption = new List<ResourceAmount>();

        public string DisplayName => _displayName;
        public IReadOnlyList<Vector3Int> CellOffsets => _cellOffsets;
        public GameObject Prefab => _prefab;
        public IReadOnlyList<ResourceAmount> BuildCost => _buildCost;
        public IReadOnlyList<ResourceAmount> Production => _production;
        public IReadOnlyList<ResourceAmount> Consumption => _consumption;

        private void OnValidate()
        {
            if (_cellOffsets.Count == 0)
                Debug.LogWarning($"{name}: 점유 셀 오프셋이 비어 있음", this);
            else if (!_cellOffsets.Contains(Vector3Int.zero))
                Debug.LogWarning($"{name}: 오프셋에 원점(0,0,0)이 없음. 피벗 셀이 모듈 밖에 있게 됨", this);
        }
    }
}
