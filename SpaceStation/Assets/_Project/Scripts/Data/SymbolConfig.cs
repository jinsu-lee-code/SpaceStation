using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// CONNECT_1121 7번 ④ 타이쿤판 지구급 상징 모듈 '항성 프리즘' (사용자 결정 2026-11-28, 추천안): 1종 · 3단계 진화 넷(항성 발전망 · 궤도 도시 · AI 중추 · 바이오스피어)을 합성.
    /// 가동 중이면 정거장 전체 생산 · 만족도 상한 ↑, 처음 완성되면 엔드리스 시작 (단계가 시간마다 올라 방문이 잦아지고 무법 습격이 커짐 · 상인 · 학술 평판이 쌓임).
    /// 엔드리스는 상징 모듈이 부서져도 이어짐 (보너스 · 평판만 멈춤). 값은 가안 (BALANCE "상징 모듈"). 메뉴 SpaceStation/Tycoon/Setup Symbol이 만든다.
    /// </summary>
    [CreateAssetMenu(fileName = "SymbolConfig", menuName = "SpaceStation/Tycoon/Symbol Config")]
    public sealed class SymbolConfig : ScriptableObject
    {
        [Tooltip("상징 모듈 (레시피 결과)")]
        [SerializeField] private ModuleData _symbol;

        [Header("가동 중 보너스 (겹치지 않음)")]
        [Tooltip("정거장 전체 생산 배율에 더함 (0.1 = +10%)")]
        [SerializeField, Range(0f, 1f)] private float _productionBonus = 0.1f;
        [Tooltip("만족도 상한 +N")]
        [SerializeField, Min(0f)] private float _satisfactionBonus = 5f;
        [Tooltip("분마다 상인 · 학술 평판 + (명성)")]
        [SerializeField, Min(0f)] private float _fameReputationPerMinute = 1f;

        [Header("엔드리스 단계")]
        [Tooltip("이 분마다 엔드리스 단계 +1 (완성 = 1단계)")]
        [SerializeField, Min(1f)] private float _levelMinutes = 10f;
        [Tooltip("단계마다 방문 간격 − (0.05 = −5%)")]
        [SerializeField, Range(0f, 0.5f)] private float _intervalStep = 0.05f;
        [Tooltip("방문 간격 배율 바닥")]
        [SerializeField, Range(0.1f, 1f)] private float _minIntervalScale = 0.5f;
        [Tooltip("단계마다 무법 선단 습격 확률 +")]
        [SerializeField, Range(0f, 0.5f)] private float _raidStep = 0.03f;
        [Tooltip("습격 확률 더하기 최대")]
        [SerializeField, Range(0f, 1f)] private float _maxRaidBonus = 0.3f;

        public ModuleData Symbol => _symbol;
        public float ProductionBonus => _productionBonus;
        public float SatisfactionBonus => _satisfactionBonus;
        public float FameReputationPerMinute => _fameReputationPerMinute;
        public float LevelMinutes => _levelMinutes;
        public float IntervalStep => _intervalStep;
        public float MinIntervalScale => _minIntervalScale;
        public float RaidStep => _raidStep;
        public float MaxRaidBonus => _maxRaidBonus;

#if UNITY_EDITOR
        public void EditorSetSymbol(ModuleData symbol) => _symbol = symbol;
#endif
    }
}
