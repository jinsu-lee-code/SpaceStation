using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 13-6a 서사판 사회 설정 (사용자 결정 2026-10-18, REDESIGN V2 · S3).
    /// - 종족 정책 반응: 반응 = 무리 비율로 섞은 종족 값(<see cref="SpeciesData.ControlReaction"/>, + 순응 · − 반발).
    ///   통제형 정책의 만족도 상한 감점 × (1 − 반응 × 통제 배율), 자율형 정책의 좋은 효과 × (1 − 반응 × 자율 배율) — 반발하는 무리가 많으면 자율형이 조금 더 잘 먹힘
    /// - 긴장도(D-10: 값 + 표시만, 결과 = 사회 위기는 6장부터): 예보가 있었는데 입은 피해 · 종족 불만 · 과밀 · 정책 반발로 쌓이고, 늘 조금씩 가라앉음 (오르는 속도가 더 작으면 내려감)
    /// 메뉴 SpaceStation/Chapters/Setup Greybox가 만든다. 값은 그레이박스 초안.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Society Config", fileName = "SocietyConfig")]
    public sealed class SocietyConfig : ScriptableObject
    {
        [Header("종족 정책 반응")]
        [Tooltip("통제형 정책의 만족도 상한 감점 × (1 − 반응 × 이 값)")]
        [SerializeField, Min(0f)] private float _controlScale = 1f;
        [Tooltip("자율형 정책의 좋은 효과 × (1 − 반응 × 이 값)")]
        [SerializeField, Min(0f)] private float _autonomyScale = 0.5f;

        [Header("긴장도")]
        [SerializeField, Min(1f)] private float _max = 100f;
        [Tooltip("예보가 있었던 유성우 · 포격(떨어지는 중)에 모듈이 파손될 때마다")]
        [SerializeField, Min(0f)] private float _forecastHit = 1f;
        [Tooltip("종족이 중시하는 요구가 전혀 안 채워질 때 분당 (무리 비율 × 못 채운 비율만큼)")]
        [SerializeField, Min(0f)] private float _favoredPerMinute = 2f;
        [Tooltip("중시하는 요구를 이만큼 못 채운 것까지는 불만으로 치지 않음 (0.15 = 85%만 채워도 괜찮음)")]
        [SerializeField, Range(0f, 1f)] private float _favoredTolerance = 0.15f;
        [Tooltip("수용 인원을 100% 넘었을 때 분당 (넘은 비율만큼, 최대 100%)")]
        [SerializeField, Min(0f)] private float _crowdPerMinute = 6f;
        [Tooltip("통제형 정책 하나에 반발하는 무리 100%일 때 분당 (반발 무리 비율 × 반발 크기만큼)")]
        [SerializeField, Min(0f)] private float _backlashPerMinute = 4f;
        [Tooltip("14-2b 집 환경이 힘든 주민이 100%일 때 분당 (힘든 주민 비율만큼)")]
        [SerializeField, Min(0f)] private float _environmentPerMinute = 3f;
        [Tooltip("14-4 치안이 전혀 안 채워질 때 분당 (못 채운 비율만큼, 중시 요구와 같은 너그러움)")]
        [SerializeField, Min(0f)] private float _securityPerMinute = 4f;
        [Tooltip("늘 조금씩 가라앉음 (분당) — 오르는 속도가 이보다 작으면 내려감 (14-3 플레이: 오를 이유가 하나만 있어도 안 내려가 100에 고정됐음)")]
        [SerializeField, Min(0f)] private float _calmPerMinute = 2f;
        [Tooltip("표시 단계 경계: 이 값부터 술렁임 · 팽팽함 · 곧 터질 듯")]
        [SerializeField] private float _restless = 25f;
        [SerializeField] private float _tense = 50f;
        [SerializeField] private float _critical = 75f;

        [Header("집 환경 (14-2b, REDESIGN S2)")]
        [Tooltip("모든 주민이 힘들 때 만족도 상한 감소 (힘든 주민 비율만큼 — 인구가 늘어도 같은 비율이면 같은 감소)")]
        [SerializeField, Min(0f)] private float _stressMoodMax = 20f;
        [Tooltip("힘든 주민 0명으로 이 시간(초) 버티면 표시 (5장 선택 목표)")]
        [SerializeField, Min(1f)] private float _calmSeconds = 60f;

        [Header("6장 (14-4)")]
        [Tooltip("치안을 주민 이 비율까지 채우면 표시 (6장 선택 목표)")]
        [SerializeField, Range(0f, 1f)] private float _securityGoalRatio = 0.8f;
        [Tooltip("사회 사건 설정 (없으면 사건이 오지 않음)")]
        [SerializeField] private SocialEventConfig _events;

        public float SecurityPerMinute => _securityPerMinute;
        public float SecurityGoalRatio => _securityGoalRatio;
        public SocialEventConfig Events => _events;
        public float StressMoodMax => _stressMoodMax;
        public float CalmSeconds => _calmSeconds;
        public float EnvironmentPerMinute => _environmentPerMinute;
        public float ControlScale => _controlScale;
        public float AutonomyScale => _autonomyScale;
        public float Max => Mathf.Max(1f, _max);
        public float ForecastHit => _forecastHit;
        public float FavoredPerMinute => _favoredPerMinute;
        public float FavoredTolerance => _favoredTolerance;
        public float CrowdPerMinute => _crowdPerMinute;
        public float BacklashPerMinute => _backlashPerMinute;
        public float CalmPerMinute => _calmPerMinute;

        /// <summary>긴장도 단계 말 ("잔잔함" · "술렁임" · "팽팽함" · "곧 터질 듯").</summary>
        public string LevelName(float value)
        {
            if (value >= _critical)
                return "곧 터질 듯";
            if (value >= _tense)
                return "팽팽함";
            if (value >= _restless)
                return "술렁임";
            return "잔잔함";
        }

        /// <summary>단계 (0 잔잔함 ~ 3 곧 터질 듯) — 화면 색.</summary>
        public int Level(float value) => value >= _critical ? 3 : value >= _tense ? 2 : value >= _restless ? 1 : 0;
    }
}
