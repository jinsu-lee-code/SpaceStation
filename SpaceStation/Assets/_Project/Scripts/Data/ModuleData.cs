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

        [Tooltip("철거 가능 여부. 코어는 false")]
        [SerializeField] private bool _removable = true;

        [Tooltip("말단 배치 전용 (채굴 도킹): 정거장과 맞닿은 면이 정확히 1개여야 하고, 배치 후 나머지 면에는 다른 모듈을 붙일 수 없다")]
        [SerializeField] private bool _terminalOnly;

        [SerializeField] private List<ResourceAmount> _buildCost = new List<ResourceAmount>();
        [SerializeField] private List<ResourceAmount> _production = new List<ResourceAmount>();
        [Tooltip("Power 항목 = 전력 수요")]
        [SerializeField] private List<ResourceAmount> _consumption = new List<ResourceAmount>();

        [Header("Capacity")]
        [Tooltip("수용 인구")]
        [SerializeField, Min(0)] private int _housingCapacity;
        [Tooltip("스톡 자원(산소/물/식량/금속) 각각의 저장 한도 증가량")]
        [SerializeField, Min(0f)] private float _storageBonus;

        [Header("Power (BALANCE 14번)")]
        [Tooltip("전력 생산이 낮/밤 주기를 따름 (태양광)")]
        [SerializeField] private bool _solarPowered;
        [Tooltip("배터리 저장 용량 (전력×초)")]
        [SerializeField, Min(0f)] private float _batteryCapacity;
        [Tooltip("배터리 충·방전 최대 속도 (초당)")]
        [SerializeField, Min(0f)] private float _batteryRate;

        public string DisplayName => _displayName;
        public IReadOnlyList<Vector3Int> CellOffsets => _cellOffsets;
        public GameObject Prefab => _prefab;
        public bool Removable => _removable;
        public bool TerminalOnly => _terminalOnly;
        public IReadOnlyList<ResourceAmount> BuildCost => _buildCost;
        public IReadOnlyList<ResourceAmount> Production => _production;
        public IReadOnlyList<ResourceAmount> Consumption => _consumption;
        public int HousingCapacity => _housingCapacity;
        public float StorageBonus => _storageBonus;
        public bool SolarPowered => _solarPowered;
        public float BatteryCapacity => _batteryCapacity;
        public float BatteryRate => _batteryRate;

        private void OnValidate()
        {
            if (_cellOffsets.Count == 0)
                Debug.LogWarning($"{name}: 점유 셀 오프셋이 비어 있음", this);
            else if (!_cellOffsets.Contains(Vector3Int.zero))
                Debug.LogWarning($"{name}: 오프셋에 원점(0,0,0)이 없음. 피벗 셀이 모듈 밖에 있게 됨", this);
        }
    }
}
