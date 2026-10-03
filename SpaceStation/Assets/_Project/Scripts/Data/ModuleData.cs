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

        [Tooltip("건설 메뉴 썸네일 (메뉴 SpaceStation/UI/Build Hud Art가 프리팹을 렌더링해 채움)")]
        [SerializeField] private Sprite _icon;

        [Tooltip("철거 가능 여부. 코어는 false")]
        [SerializeField] private bool _removable = true;

        [Tooltip("도킹 배치 규칙 (8-0, 채굴 도킹): 뒷면으로만 정거장에 붙고, 앞쪽 접근로는 항상 비어 있어야 한다. 옆·위·아래는 자유")]
        [SerializeField] private bool _terminalOnly;
        [Tooltip("도킹 입구(앞) 방향, 모듈 로컬 기준 (회전 0일 때). 뒷면 = 반대쪽")]
        [SerializeField] private Vector3Int _dockFront = new Vector3Int(0, 0, 1);
        [Tooltip("앞쪽 접근로 길이 (칸). 이 칸들에는 아무것도 지을 수 없다")]
        [SerializeField, Min(1)] private int _approachLaneLength = 2;

        [Tooltip("7-7 받침 규칙: 이 모듈 맨 위층의 옆면에 맞닿는 칸은 바로 아래에 모듈이 있어야 설치 가능 (코어 2층 = 연결점 없는 탑). 나머지 칸은 자유")]
        [SerializeField] private bool _upperSidesNeedSupport;

        [Tooltip("5-6 연결 통로: 칸·면 방향별 모델 표면 깊이 (칸 중심에서). 메뉴 SpaceStation/Art/Measure Connector Depths가 채움. 음수 = 연결점 없음")]
        [SerializeField] private List<FaceDepth> _faceDepths = new List<FaceDepth>();

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
        [Tooltip("동시 연구 슬롯 (연구소 1, Phase 6). 활성이고 파손되지 않았을 때만")]
        [SerializeField, Min(0)] private int _researchSlots;
        [Tooltip("8-1: 파손 확산 시간 배율 (1 = 기본 60초, 0.5 = 2배 빨리 번짐 — 핵융합로)")]
        [SerializeField, Min(0.05f)] private float _spreadTimeMultiplier = 1f;

        [Header("Defense (BALANCE 19번)")]
        [Tooltip("실드 반경 (격자 칸, 체비셰프 거리). 0이면 실드 아님")]
        [SerializeField, Min(0)] private int _shieldRadius;
        [Tooltip("범위 안 모듈로 오는 운석 1발당 차단 확률 (0.7 = 피격 -70%). 여러 실드는 중첩 없이 가장 강한 것")]
        [SerializeField, Range(0f, 1f)] private float _shieldReduction;
        [Tooltip("포탑 반경 (격자 칸, 체비셰프 거리). 0이면 포탑 아님")]
        [SerializeField, Min(0)] private int _turretRadius;
        [Tooltip("범위 안으로 오는 운석 1발당 격추 확률 (포탑끼리 합산, 상한은 BalanceConfig)")]
        [SerializeField, Range(0f, 1f)] private float _turretInterceptChance;
        [Tooltip("8-3 손상 통제 반경 (격자 칸, 체비셰프 거리). 0이면 손상 통제 모듈 아님")]
        [SerializeField, Min(0)] private int _controlRadius;
        [Tooltip("범위 안 파손 모듈의 확산 시간 배율 (2 = 2배 늦게 번짐). 여러 통제실은 중첩 없이 가장 강한 것")]
        [SerializeField, Min(1f)] private float _controlSpreadMultiplier = 1f;
        [Tooltip("범위 안 파손 모듈의 방치 파괴 시간 배율 (1.5 = 120초 → 180초)")]
        [SerializeField, Min(1f)] private float _controlDestroyMultiplier = 1f;

        [Tooltip("8-4 회전 링: 가동 중이면 정거장 전체 인구 증가 간격에 곱함 (0.8 = 20% 빨리, 1 = 효과 없음). 여러 개는 중첩 없이 가장 강한 것")]
        [SerializeField, Range(0.1f, 1f)] private float _growthIntervalMultiplier = 1f;

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
        public float GrowthIntervalMultiplier => _growthIntervalMultiplier > 0f ? Mathf.Min(1f, _growthIntervalMultiplier) : 1f;

        public int RepairSlots => _repairSlots;
        public int ResearchSlots => _researchSlots;
        public float SpreadTimeMultiplier => _spreadTimeMultiplier > 0f ? _spreadTimeMultiplier : 1f;
        public int ShieldRadius => _shieldReduction > 0f ? _shieldRadius : 0;
        public float ShieldReduction => _shieldReduction;
        public int TurretRadius => _turretInterceptChance > 0f ? _turretRadius : 0;
        public float TurretInterceptChance => _turretInterceptChance;
        public bool IsShield => ShieldRadius > 0;
        public bool IsTurret => TurretRadius > 0;
        public int ControlRadius => _controlSpreadMultiplier > 1f || _controlDestroyMultiplier > 1f ? _controlRadius : 0;
        public float ControlSpreadMultiplier => Mathf.Max(1f, _controlSpreadMultiplier);
        public float ControlDestroyMultiplier => Mathf.Max(1f, _controlDestroyMultiplier);
        public bool IsDamageControl => ControlRadius > 0;
        public bool IsDefense => IsShield || IsTurret || IsDamageControl;
        public string DisplayName => _displayName;
        public ModuleCategory Category => _category;
        public IReadOnlyList<Vector3Int> CellOffsets => _cellOffsets;
        public GameObject Prefab => _prefab;
        public Sprite Icon => _icon;
        public bool Removable => _removable;
        public bool TerminalOnly => _terminalOnly;
        public Vector3Int DockFront => _dockFront;
        public int ApproachLaneLength => Mathf.Max(1, _approachLaneLength);
        public bool UpperSidesNeedSupport => _upperSidesNeedSupport;
        public IReadOnlyList<FaceDepth> FaceDepths => _faceDepths;

        /// <summary>측정값이 없을 때의 표면 깊이 (셀 92% 상자 기준).</summary>
        public const float DefaultFaceDepth = 0.46f;

        /// <summary>
        /// 회전 0 기준 칸·면 방향의 표면 깊이. 측정값이 없으면 기본값.
        /// false = 그 면에는 연결점이 없음 (모델이 비어 있는 칸).
        /// </summary>
        public bool TryGetFaceDepth(Vector3Int localCell, Vector3Int localDir, out float depth)
        {
            for (int i = 0; i < _faceDepths.Count; i++)
            {
                if (_faceDepths[i].Cell == localCell && _faceDepths[i].Direction == localDir)
                {
                    depth = _faceDepths[i].Depth;
                    return depth >= 0f;
                }
            }
            depth = DefaultFaceDepth;
            return true;
        }

#if UNITY_EDITOR
        /// <summary>측정 도구 전용.</summary>
        public void SetFaceDepths(List<FaceDepth> depths)
        {
            _faceDepths = depths;
        }
#endif
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
