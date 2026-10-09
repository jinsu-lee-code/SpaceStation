using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>Phase 10 거주자 특성 (BALANCE 26번).</summary>
    public enum ResidentTrait
    {
        Technician,         // 기술자: 수리 시간 −
        Scientist,          // 과학자: 연구 속도 +
        Gardener,           // 원예가: 식량 생산 +
        Mechanic,           // 정비공: 노후 속도 −
        Optimist,           // 낙천가: 만족도 상한 +
        Complainer,         // 불평꾼: 만족도 상한 −, 이탈 우선
        BigEater,           // 대식가: 본인 식량 소비 +
        LightEater,         // 소식가: 본인 식량·물 소비 −
        Sociable,           // 사교적: 여가 시설 범위 안 집 +, 밖 −
        GravityLover,       // 중력 애호가: 회전 링 범위 안 집 +
        RadiationSensitive, // 방사선 민감: 핵융합로 옆 집 −, 이탈 우선
        RadiationTolerant,  // 방사선 내성: 핵융합로 옆 집에 배정 우선 (불만 없음)
        NoiseSensitive,     // 소음 민감: 제련소 옆 집 −
        NatureLover,        // 자연 애호가: 수경 농장 옆 집 +
    }

    /// <summary>특성 하나의 표시·등장·수치.</summary>
    [Serializable]
    public sealed class TraitDefinition
    {
        [SerializeField] private ResidentTrait _trait;
        [SerializeField] private string _displayName;
        [SerializeField, TextArea(1, 3)] private string _description;
        [Tooltip("등장 가중치 (0이면 안 나옴)")]
        [SerializeField, Min(0f)] private float _weight = 1f;
        [Tooltip("UI 색: 좋은 특성이면 true (초록), 나쁘면 false (빨강)")]
        [SerializeField] private bool _positive = true;
        [Tooltip("같이 가질 수 없는 특성 (자기 자신이면 없음)")]
        [SerializeField] private ResidentTrait _conflictsWith;
        [SerializeField] private bool _hasConflict;
        [Tooltip("1명당 효과 (능력: 비율 0.04 = 4%, 만족도: 상한 ±점수, 소비: 본인 배율 변화 0.5 = +50%)")]
        [SerializeField] private float _perResident;
        [Tooltip("조건을 만족하지 못할 때 1명당 효과 (사교적: 여가 시설 밖 −1)")]
        [SerializeField] private float _perResidentOtherwise;
        [Tooltip("정거장 전체 합계의 절댓값 상한 (0 = 상한 없음)")]
        [SerializeField, Min(0f)] private float _maxTotal;
        [Tooltip("이 특성을 가진 주민의 직함 (이름 뒤에 붙음, 예: 두부 기관사). 비우면 직함을 주지 않는 특성")]
        [SerializeField] private string _title = "";

        public ResidentTrait Trait => _trait;
        public string DisplayName => _displayName;
        public string Description => _description;
        public float Weight => _weight;
        public bool Positive => _positive;
        public bool HasConflict => _hasConflict;
        public ResidentTrait ConflictsWith => _conflictsWith;
        public float PerResident => _perResident;
        public float PerResidentOtherwise => _perResidentOtherwise;
        public float MaxTotal => _maxTotal;
        public string Title => _title;

        public TraitDefinition() { }

        public TraitDefinition(ResidentTrait trait, string name, string description, bool positive, float perResident, float maxTotal,
            float weight = 1f, float perResidentOtherwise = 0f)
        {
            _trait = trait;
            _displayName = name;
            _description = description;
            _positive = positive;
            _perResident = perResident;
            _maxTotal = maxTotal;
            _weight = weight;
            _perResidentOtherwise = perResidentOtherwise;
        }

        public TraitDefinition WithConflict(ResidentTrait other)
        {
            _hasConflict = true;
            _conflictsWith = other;
            return this;
        }

        public TraitDefinition WithTitle(string title)
        {
            _title = title;
            return this;
        }

        /// <summary>합계를 상한으로 자름 (부호 유지).</summary>
        public float ClampTotal(float total) => _maxTotal > 0f ? Mathf.Clamp(total, -_maxTotal, _maxTotal) : total;
    }

    /// <summary>
    /// Phase 10 거주자: 특성 표·이름 목록·환경 원인 모듈. 주민은 이동 AI 없이 명단(이름·특성·집)으로만 존재한다.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Resident Config", fileName = "ResidentConfig")]
    public sealed class ResidentConfig : ScriptableObject
    {
        [SerializeField] private List<TraitDefinition> _traits = new List<TraitDefinition>();
        [Tooltip("특성을 2개 가질 확률 (나머지는 1개)")]
        [SerializeField, Range(0f, 1f)] private float _secondTraitChance = 0.35f;

        [Header("환경 (집과 면이 맞닿은 활성 모듈)")]
        [SerializeField] private List<ModuleData> _radiationSources = new List<ModuleData>();
        [SerializeField] private List<ModuleData> _noiseSources = new List<ModuleData>();
        [SerializeField] private List<ModuleData> _natureSources = new List<ModuleData>();

        [Header("이탈 순서 (점수가 높은 주민부터 떠남)")]
        [Tooltip("집이 없는 주민")]
        [SerializeField] private float _homelessDiscontent = 10f;
        [Tooltip("불평꾼·방사선 민감 등 '이탈 우선' 특성")]
        [SerializeField] private float _leaveFirstDiscontent = 3f;

        [Header("이름 (11-11d 동물 주민: 귀여운 짧은 이름 + 직함, 2026-10-10)")]
        [SerializeField] private List<string> _givenNames = new List<string>();
        [Tooltip("비우면 이름만 (예전 국제 승무원 이름은 '이름 성')")]
        [SerializeField] private List<string> _surnames = new List<string>();
        [Tooltip("직함을 주는 특성(TraitDefinition.Title)이 없는 주민의 직함")]
        [SerializeField] private string _defaultTitle = "대원";

        public IReadOnlyList<TraitDefinition> Traits => _traits;
        public float SecondTraitChance => _secondTraitChance;
        public IReadOnlyList<ModuleData> RadiationSources => _radiationSources;
        public IReadOnlyList<ModuleData> NoiseSources => _noiseSources;
        public IReadOnlyList<ModuleData> NatureSources => _natureSources;
        public float HomelessDiscontent => _homelessDiscontent;
        public float LeaveFirstDiscontent => _leaveFirstDiscontent;
        public IReadOnlyList<string> GivenNames => _givenNames;
        public IReadOnlyList<string> Surnames => _surnames;

        public string DefaultTitle => _defaultTitle;

        public TraitDefinition Get(ResidentTrait trait)
        {
            foreach (var t in _traits)
                if (t != null && t.Trait == trait)
                    return t;
            return null;
        }

        /// <summary>직함: 주민 특성 순서대로 처음 나오는 직함 있는 특성 (기술자 → 기관사 등), 없으면 기본 직함 (대원).</summary>
        public string TitleOf(IEnumerable<ResidentTrait> traits)
        {
            if (traits != null)
            {
                foreach (var t in traits)
                {
                    var def = Get(t);
                    if (def != null && !string.IsNullOrEmpty(def.Title))
                        return def.Title;
                }
            }
            return _defaultTitle;
        }

        /// <summary>에디터 설정·테스트용.</summary>
        public void EditorSet(IEnumerable<TraitDefinition> traits, float secondTraitChance, IEnumerable<ModuleData> radiation, IEnumerable<ModuleData> noise,
            IEnumerable<ModuleData> nature, IEnumerable<string> givenNames, IEnumerable<string> surnames, string defaultTitle = "대원")
        {
            _defaultTitle = defaultTitle;
            _traits = new List<TraitDefinition>(traits);
            _secondTraitChance = secondTraitChance;
            _radiationSources = new List<ModuleData>(radiation);
            _noiseSources = new List<ModuleData>(noise);
            _natureSources = new List<ModuleData>(nature);
            _givenNames = new List<string>(givenNames);
            _surnames = new List<string>(surnames);
        }
    }
}
