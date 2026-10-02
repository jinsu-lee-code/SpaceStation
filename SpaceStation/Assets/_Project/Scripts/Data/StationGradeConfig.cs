using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>정거장 등급 하나 (GDD 12-3).</summary>
    [Serializable]
    public sealed class StationGrade
    {
        [SerializeField] private string _displayName = "Grade";
        [SerializeField, Min(0)] private int _minPopulation;
        [Tooltip("코어 포함 전체 모듈 수")]
        [SerializeField, Min(0)] private int _minModules;
        [Tooltip("개수 제한 모듈(채굴 도킹) 최대 설치 수")]
        [SerializeField, Min(0)] private int _maxLimitedModules;
        [Tooltip("이 등급에 도달하면 건설 가능해지는 모듈")]
        [SerializeField] private List<ModuleData> _unlocks = new List<ModuleData>();
        [Tooltip("이 등급부터 생기는 거주자 요구 (누적, 4-9)")]
        [SerializeField] private List<ResidentNeed> _newNeeds = new List<ResidentNeed>();

        [Header("Event Scaling (BALANCE 13번)")]
        [Tooltip("운석 1회당 파손 모듈 수 (최소~최대 무작위)")]
        [SerializeField, Min(1)] private int _meteorHitsMin = 1;
        [SerializeField, Min(1)] private int _meteorHitsMax = 1;
        [Tooltip("이벤트 간격에 곱함 (작을수록 자주)")]
        [SerializeField, Min(0.05f)] private float _eventIntervalMultiplier = 1f;
        [Tooltip("이벤트 강도에 곱함: 산소 누출 비율, 태양 폭풍 지속시간, 보급선 자원")]
        [SerializeField, Min(0.05f)] private float _eventIntensityMultiplier = 1f;

        public string DisplayName => _displayName;
        public int MinPopulation => _minPopulation;
        public int MinModules => _minModules;
        public int MaxLimitedModules => _maxLimitedModules;
        public IReadOnlyList<ModuleData> Unlocks => _unlocks;
        public IReadOnlyList<ResidentNeed> NewNeeds => _newNeeds;
        // 직렬화 기본값이 0으로 들어온 경우를 대비해 최소값을 보정한다.
        public int MeteorHitsMin => Mathf.Max(1, _meteorHitsMin);
        public int MeteorHitsMax => Mathf.Max(MeteorHitsMin, _meteorHitsMax);
        public float EventIntervalMultiplier => _eventIntervalMultiplier > 0f ? _eventIntervalMultiplier : 1f;
        public float EventIntensityMultiplier => _eventIntensityMultiplier > 0f ? _eventIntensityMultiplier : 1f;

        internal void SetEventIntervalMultiplier(float value) => _eventIntervalMultiplier = Mathf.Max(0.05f, value);
    }

    /// <summary>
    /// 정거장 등급 목록 (낮은 등급부터). 첫 등급은 시작 등급, 마지막 등급 도달이 클리어.
    /// 등급은 인구·모듈 수로 실시간 판정한다 (BALANCE 11번).
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Station Grade Config", fileName = "StationGrades")]
    public sealed class StationGradeConfig : ScriptableObject
    {
        [SerializeField] private List<StationGrade> _grades = new List<StationGrade>();
        [Tooltip("등급별 최대 설치 수가 적용되는 모듈 (채굴 도킹)")]
        [SerializeField] private ModuleData _limitedModule;
        [Tooltip("8-0: 최고 등급에서 그 등급 최소 모듈 수를 넘는 모듈 N개마다 제한 모듈 최대 수 +1 (0 = 확장 없음)")]
        [SerializeField, Min(0)] private int _extraLimitEveryModules;

        public IReadOnlyList<StationGrade> Grades => _grades;
        public ModuleData LimitedModule => _limitedModule;
        public int ExtraLimitEveryModules => _extraLimitEveryModules;

        /// <summary>난이도 덮어쓰기 — 복사본에만 호출. 음수 항목은 유지.</summary>
        internal void ApplyEventIntervals(IReadOnlyList<float> multipliers)
        {
            if (multipliers == null)
                return;
            for (int i = 0; i < multipliers.Count && i < _grades.Count; i++)
            {
                if (multipliers[i] >= 0f)
                    _grades[i].SetEventIntervalMultiplier(multipliers[i]);
            }
        }
    }
}
