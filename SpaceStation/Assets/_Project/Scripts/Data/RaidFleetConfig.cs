using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// REVIEW_1115 6번 외부 위협 1차 (사용자 결정 2026-11-15 · 11-24, CONNECT_1121 X1 · X4) — 단계형 습격 값 (가안, 측정으로 정함).
    /// 정찰(냉담) → 함대(적대) → 기함(적대인데 함대를 몇 번 막아 냄). 함선은 체력이 있고, 포탑 격추마다 반격으로 깎임.
    /// 한 단계 안의 규모 = 정거장 크기(바깥에 드러난 모듈) × 방문 횟수 규모, 합쳐서 상한.
    /// <see cref="ContactConfig.Fleet"/>가 가리키고, 규칙 <see cref="StationRule.FleetRaid"/>가 열려야 동작 (규칙을 정하는 쪽이 없으면 닫힘 — 기존 습격 그대로).
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Raid Fleet Config", fileName = "RaidFleetConfig")]
    public sealed class RaidFleetConfig : ScriptableObject
    {
        [Header("단계 오름")]
        [Tooltip("적대 세력의 함대를 이만큼 막아 내면 다음은 기함")]
        [SerializeField, Min(1)] private int _fleetsToFlagship = 2;
        [Tooltip("기함을 꺾으면 그 세력 습격이 이 시간(초) 동안 멈춤")]
        [SerializeField, Min(0f)] private float _flagshipPause = 1200f;
        [Tooltip("포격 예보 (초)")]
        [SerializeField, Min(1f)] private float _warning = 30f;

        [Header("정찰 (냉담)")]
        [SerializeField, Min(1)] private int _scoutShips = 2;
        [SerializeField, Min(1f)] private float _scoutHp = 3f;
        [SerializeField, Min(1)] private int _scoutWaves = 1;
        [SerializeField, Min(1)] private int _scoutHits = 3;

        [Header("함대 (적대)")]
        [SerializeField, Min(1)] private int _fleetShips = 4;
        [SerializeField, Min(1f)] private float _fleetHp = 4f;
        [SerializeField, Min(1)] private int _fleetWaves = 2;
        [SerializeField, Min(1)] private int _fleetHits = 4;
        [Tooltip("포격이 끝나고 남은 함선이 보내는 침입 드론 (0 = 없음)")]
        [SerializeField, Min(0)] private int _fleetDrones = 2;

        [Header("기함")]
        [SerializeField, Min(1f)] private float _flagshipHp = 40f;
        [SerializeField, Min(0)] private int _flagshipEscorts = 2;
        [Tooltip("한 번 쏠 때 파 수 · 한 파 탄 수 · 다음 쏘기까지 쉬는 시간 (초)")]
        [SerializeField, Min(1)] private int _volleyWaves = 2;
        [SerializeField, Min(1)] private int _volleyHits = 5;
        [SerializeField, Min(1f)] private float _volleyGap = 30f;
        [Tooltip("이 시간(초) 안에 못 꺾으면 물러가며 큰 포격 한 번")]
        [SerializeField, Min(10f)] private float _flagshipSeconds = 420f;
        [SerializeField, Min(1)] private int _retreatHits = 10;
        [Tooltip("체력이 절반 아래로 떨어지면 한 번 보내는 침입 드론 (0 = 없음)")]
        [SerializeField, Min(0)] private int _flagshipDrones = 3;

        [Header("반격 · 침입")]
        [Tooltip("습격 포격을 포탑이 격추할 때마다 함선 체력 감소 (기함 단계 = 기함)")]
        [SerializeField, Min(0f)] private float _counterDamage = 1f;
        [Tooltip("침입 드론 제한 시간 (초)")]
        [SerializeField, Min(10f)] private float _droneSeconds = 150f;

        [Header("규모 (CONNECT X1: 정거장 크기 · 노출 면)")]
        [Tooltip("바깥에 드러난 모듈이 이 수를 넘으면 커지기 시작")]
        [SerializeField, Min(1)] private int _exteriorReference = 40;
        [Tooltip("기준을 넘는 기준 수마다 더하는 배율")]
        [SerializeField, Min(0f)] private float _exteriorStep = 0.5f;
        [SerializeField, Min(1f)] private float _sizeScaleMax = 2f;
        [Tooltip("크기 배율 × 방문 횟수 규모의 상한 (곱으로 끝없이 커지지 않게)")]
        [SerializeField, Min(1f)] private float _totalScaleMax = 3f;

        public int FleetsToFlagship => _fleetsToFlagship;
        public float FlagshipPause => _flagshipPause;
        public float Warning => _warning;
        public int ScoutShips => _scoutShips;
        public float ScoutHp => _scoutHp;
        public int ScoutWaves => _scoutWaves;
        public int ScoutHits => _scoutHits;
        public int FleetShips => _fleetShips;
        public float FleetHp => _fleetHp;
        public int FleetWaves => _fleetWaves;
        public int FleetHits => _fleetHits;
        public int FleetDrones => _fleetDrones;
        public float FlagshipHp => _flagshipHp;
        public int FlagshipEscorts => _flagshipEscorts;
        public int VolleyWaves => _volleyWaves;
        public int VolleyHits => _volleyHits;
        public float VolleyGap => _volleyGap;
        public float FlagshipSeconds => _flagshipSeconds;
        public int RetreatHits => _retreatHits;
        public int FlagshipDrones => _flagshipDrones;
        public float CounterDamage => _counterDamage;
        public float DroneSeconds => _droneSeconds;
        public int ExteriorReference => _exteriorReference;
        public float ExteriorStep => _exteriorStep;
        public float SizeScaleMax => _sizeScaleMax;
        public float TotalScaleMax => _totalScaleMax;

        /// <summary>바깥에 드러난 모듈 수 → 크기 배율 (1 ~ 상한).</summary>
        public float SizeScale(int exteriorModules)
        {
            if (exteriorModules <= _exteriorReference)
                return 1f;
            float over = (exteriorModules - _exteriorReference) / (float)Mathf.Max(1, _exteriorReference);
            return Mathf.Min(_sizeScaleMax, 1f + over * _exteriorStep);
        }

#if UNITY_EDITOR
        /// <summary>시험용: 기함 값만 바꿈.</summary>
        public void EditorSetFlagship(float hp, float seconds, int volleyWaves, int volleyHits, float volleyGap)
        {
            _flagshipHp = hp;
            _flagshipSeconds = seconds;
            _volleyWaves = volleyWaves;
            _volleyHits = volleyHits;
            _volleyGap = volleyGap;
        }
#endif
    }
}
