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

        public string DisplayName => _displayName;
        public int MinPopulation => _minPopulation;
        public int MinModules => _minModules;
        public int MaxLimitedModules => _maxLimitedModules;
        public IReadOnlyList<ModuleData> Unlocks => _unlocks;
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

        public IReadOnlyList<StationGrade> Grades => _grades;
        public ModuleData LimitedModule => _limitedModule;
    }
}
