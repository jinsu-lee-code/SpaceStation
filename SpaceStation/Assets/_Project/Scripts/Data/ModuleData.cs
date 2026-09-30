using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>모듈 종류 하나의 정의. 수치는 전부 이 에셋에서 조정한다.</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Module Data", fileName = "MD_NewModule")]
    public sealed class ModuleData : ScriptableObject
    {
        [SerializeField] private string _displayName = "Module";

        [Tooltip("건설 메뉴 탭 (4-5)")]
        [SerializeField] private ModuleCategory _category = ModuleCategory.Life;

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

        [Header("Support (BALANCE 17번)")]
        [Tooltip("동시 수리 슬롯 추가 수 (정비 베이 1). 활성이고 파손되지 않았을 때만. 코어 몫은 BalanceConfig.BaseRepairSlots")]
        [SerializeField, Min(0)] private int _repairSlots;

        [Header("Defense (BALANCE 19번)")]
        [Tooltip("실드 반경 (격자 칸, 체비셰프 거리). 0이면 실드 아님")]
        [SerializeField, Min(0)] private int _shieldRadius;
        [Tooltip("범위 안 모듈로 오는 운석 1발당 차단 확률 (0.7 = 피격 -70%). 여러 실드는 중첩 없이 가장 강한 것")]
        [SerializeField, Range(0f, 1f)] private float _shieldReduction;
        [Tooltip("포탑 반경 (격자 칸, 체비셰프 거리). 0이면 포탑 아님")]
        [SerializeField, Min(0)] private int _turretRadius;
        [Tooltip("범위 안으로 오는 운석 1발당 격추 확률 (포탑끼리 합산, 상한은 BalanceConfig)")]
        [SerializeField, Range(0f, 1f)] private float _turretInterceptChance;

        [Header("Resident Service (BALANCE 20번)")]
        [Tooltip("충족하는 거주자 요구. None이면 서비스 모듈 아님")]
        [SerializeField] private ResidentNeed _serviceNeed;
        [Tooltip("서비스 반경 (격자 칸, 체비셰프). 이 안의 거주 모듈 주민만 담당")]
        [SerializeField, Min(0)] private int _serviceRadius;
        [Tooltip("담당 주민 수 (가동률만큼 감소)")]
        [SerializeField, Min(0)] private int _serviceCapacity;

        public ResidentNeed ServiceNeed => _serviceCapacity > 0 ? _serviceNeed : ResidentNeed.None;
        public int ServiceRadius => _serviceRadius;
        public int ServiceCapacity => _serviceCapacity;
        public bool IsService => ServiceNeed != ResidentNeed.None;

        public int RepairSlots => _repairSlots;
        public int ShieldRadius => _shieldReduction > 0f ? _shieldRadius : 0;
        public float ShieldReduction => _shieldReduction;
        public int TurretRadius => _turretInterceptChance > 0f ? _turretRadius : 0;
        public float TurretInterceptChance => _turretInterceptChance;
        public bool IsShield => ShieldRadius > 0;
        public bool IsTurret => TurretRadius > 0;
        public bool IsDefense => IsShield || IsTurret;
        public string DisplayName => _displayName;
        public ModuleCategory Category => _category;
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
