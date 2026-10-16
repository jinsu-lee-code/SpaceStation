using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>13-7 등급 중간 단계 하나: 그 등급에서 인구 · 모듈 조건을 넘으면 다음 등급 모듈 일부를 먼저 연다.</summary>
    [Serializable]
    public sealed class MidGradeStep
    {
        [Tooltip("보이는 이름 (배너)")]
        public string DisplayName = "중형 확장";
        [Tooltip("이 등급(번호)에 도달한 뒤부터 (0 = 등급 없음, 2 = 중형)")]
        [Min(0)] public int Grade = 2;
        [Min(0)] public int MinPopulation = 45;
        [Tooltip("등급 조건으로 세는 모듈 수")]
        [Min(0)] public int MinModules = 30;
        [Tooltip("먼저 열 모듈")]
        public List<ModuleData> Modules = new List<ModuleData>();
        [Tooltip("받는 연구 포인트")]
        [Min(0)] public int ResearchPoints = 1;
    }

    /// <summary>
    /// 13-7 타이쿤판 등급 중간 단계 (사용자 결정 2026-10-16, BALANCE 47번). 메뉴 SpaceStation/Tycoon/Setup Mid Grade가 만든다.
    /// 봇 측정에서 '새 일 공백'이 가장 긴 구간이 중형(중형 → 대형)이라, 그 가운데에 새 모듈 · 보상을 나눠 둔다. 등급 수는 그대로.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Mid Grade Config", fileName = "MidGradeConfig")]
    public sealed class MidGradeConfig : ScriptableObject
    {
        [SerializeField] private List<MidGradeStep> _steps = new List<MidGradeStep>();

        public IReadOnlyList<MidGradeStep> Steps => _steps;

        /// <summary>에디터 설정 메뉴 · 시험용.</summary>
        public void Set(List<MidGradeStep> steps) => _steps = steps;
    }
}
