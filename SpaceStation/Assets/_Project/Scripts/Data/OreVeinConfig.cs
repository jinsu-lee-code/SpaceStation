using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 타이쿤판(엔드리스) 광맥 — CONNECT_1121 1번 M1 ~ M6 (사용자 결정 2026-11-21 · 11-27). 값은 봇 측정으로 정하는 가안 (BALANCE 50번).
    /// 바닥 있는 고갈형: 중형부터 제자리 채굴 도킹은 캔 양이 쌓일수록 줄어 바닥값에서 멈추고, 광맥 칸에 맞닿은 도킹은 더 많이 캔다 (매장량 유한).
    /// 광맥 신호 = 중형부터 등급이 오를 때 + 광맥 하나를 다 캔 뒤 일정 시간. 위치는 바로 보이고 남은 매장량은 감지 모듈이 있어야 숫자로.
    /// 메뉴 SpaceStation/Tycoon/Setup Ore Veins가 만든다.
    /// </summary>
    [CreateAssetMenu(fileName = "OreVeinConfig", menuName = "SpaceStation/Tycoon/Ore Vein Config")]
    public sealed class OreVeinConfig : ScriptableObject
    {
        [Tooltip("탐사 틀 설정 (엔드리스용 — 신호 센서 없음 · 소행성 고정 금속 0, 생산은 도킹 배율로)")]
        [SerializeField] private ExplorationConfig _exploration;
        [Tooltip("채굴 도킹 (고갈 · 광맥 배율이 걸리는 모듈)")]
        [SerializeField] private ModuleData _dock;
        [Tooltip("남은 매장량을 숫자로 보여 주는 감지 모듈 (센서 어레이). 비면 늘 보임")]
        [SerializeField] private ModuleData _sensor;

        [Header("제자리 고갈")]
        [Tooltip("이 등급에 오른 뒤부터 고갈 · 광맥 (2 = 중형 — 시작 · 소형은 금속원이 하나둘뿐이라 건드리면 막힘, BALANCE 12번)")]
        [SerializeField, Min(0)] private int _startGrade = 2;
        [Tooltip("제자리 도킹이 바닥에서 내는 비율 (원래 생산 대비)")]
        [SerializeField, Range(0f, 1f)] private float _floor = 0.4f;
        [Tooltip("제자리에서 이만큼 캐면 바닥값 (금속, 그 사이는 곧게 줄어듦)")]
        [SerializeField, Min(1f)] private float _depletionAmount = 1800f;

        [Header("광맥")]
        [Tooltip("광맥 칸에 맞닿은 도킹의 생산 배율 (원래 생산 대비)")]
        [SerializeField, Min(1f)] private float _veinMultiplier = 2f;
        [Tooltip("시작 등급의 광맥 하나 매장량 (금속)")]
        [SerializeField, Min(1f)] private float _reserveBase = 1500f;
        [Tooltip("등급이 하나 오를 때마다 매장량 증가 (0.5 = +50%)")]
        [SerializeField, Min(0f)] private float _reservePerGrade = 0.5f;
        [Tooltip("한 번에 잡히는 광맥 신호 수 (최소 · 최대)")]
        [SerializeField, Min(1)] private int _veinsMin = 2;
        [SerializeField, Min(1)] private int _veinsMax = 3;
        [Tooltip("정거장 끝에서 광맥까지 칸 수 (시작 등급, 최소 · 최대)")]
        [SerializeField, Min(1)] private int _distanceMin = 3;
        [SerializeField, Min(1)] private int _distanceMax = 5;
        [Tooltip("등급이 하나 오를 때마다 더 멀어지는 칸 수")]
        [SerializeField, Min(0)] private int _distancePerGrade = 1;
        [Tooltip("광맥 하나를 다 캔 뒤 새 신호까지 (초)")]
        [SerializeField, Min(0f)] private float _respawnDelay = 300f;

        public ExplorationConfig Exploration => _exploration;
        public ModuleData Dock => _dock;
        public ModuleData Sensor => _sensor;
        public int StartGrade => _startGrade;
        public float Floor => _floor;
        public float DepletionAmount => _depletionAmount;
        public float VeinMultiplier => _veinMultiplier;
        public int VeinsMin => _veinsMin;
        public int VeinsMax => Mathf.Max(_veinsMin, _veinsMax);
        public float RespawnDelay => _respawnDelay;

        /// <summary>그 등급에서 잡힌 광맥 하나의 매장량.</summary>
        public float ReserveFor(int grade) => _reserveBase * (1f + _reservePerGrade * Mathf.Max(0, grade - _startGrade));

        public int DistanceMinFor(int grade) => _distanceMin + _distancePerGrade * Mathf.Max(0, grade - _startGrade);
        public int DistanceMaxFor(int grade) => Mathf.Max(_distanceMin, _distanceMax) + _distancePerGrade * Mathf.Max(0, grade - _startGrade);

#if UNITY_EDITOR
        public void EditorSet(ExplorationConfig exploration, ModuleData dock, ModuleData sensor)
        {
            _exploration = exploration;
            _dock = dock;
            _sensor = sensor;
        }
#endif
    }
}
