using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 13-6a 서사판 사회 설정 (사용자 결정 2026-10-18, REDESIGN V2 · S3).
    /// - 종족 정책 반응: 반응 = 무리 비율로 섞은 종족 값(<see cref="SpeciesData.ControlReaction"/>, + 순응 · − 반발).
    ///   통제형 정책의 만족도 상한 감점 × (1 − 반응 × 통제 배율), 자율형 정책의 좋은 효과 × (1 − 반응 × 자율 배율) — 반발하는 무리가 많으면 자율형이 조금 더 잘 먹힘
    /// - 긴장도(D-10: 값 + 표시만, 결과 = 사회 위기는 6장부터): 예보가 있었는데 입은 피해 · 종족 불만 · 과밀 · 정책 반발로 쌓이고, 오를 이유가 없으면 천천히 내려감
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
        [SerializeField, Min(0f)] private float _forecastHit = 3f;
        [Tooltip("종족이 중시하는 요구가 전혀 안 채워질 때 분당 (무리 비율 × 못 채운 비율만큼)")]
        [SerializeField, Min(0f)] private float _favoredPerMinute = 2f;
        [Tooltip("수용 인원을 100% 넘었을 때 분당 (넘은 비율만큼, 최대 100%)")]
        [SerializeField, Min(0f)] private float _crowdPerMinute = 6f;
        [Tooltip("통제형 정책 하나에 반발하는 무리 100%일 때 분당 (반발 무리 비율 × 반발 크기만큼)")]
        [SerializeField, Min(0f)] private float _backlashPerMinute = 4f;
        [Tooltip("오를 이유가 없을 때 내려감 (분당)")]
        [SerializeField, Min(0f)] private float _calmPerMinute = 1f;
        [Tooltip("표시 단계 경계: 이 값부터 술렁임 · 팽팽함 · 곧 터질 듯")]
        [SerializeField] private float _restless = 25f;
        [SerializeField] private float _tense = 50f;
        [SerializeField] private float _critical = 75f;

        public float ControlScale => _controlScale;
        public float AutonomyScale => _autonomyScale;
        public float Max => Mathf.Max(1f, _max);
        public float ForecastHit => _forecastHit;
        public float FavoredPerMinute => _favoredPerMinute;
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
