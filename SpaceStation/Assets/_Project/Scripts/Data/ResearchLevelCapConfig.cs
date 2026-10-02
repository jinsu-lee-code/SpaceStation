using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 연구 레벨 상한 (RESEARCH.md 4번): 레벨마다 필요 등급(StationGrades 인덱스)과 필요 인구. 두 조건을 모두 만족해야 시작 가능.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Research Level Caps", fileName = "ResearchLevelCaps")]
    public sealed class ResearchLevelCapConfig : ScriptableObject
    {
        [Serializable]
        public struct Requirement
        {
            [Tooltip("필요 등급 (0 = 초소형)")]
            [Min(0)] public int Grade;
            [Min(0)] public int MinPopulation;
        }

        [Tooltip("레벨 1부터 순서대로")]
        [SerializeField] private List<Requirement> _levels = new List<Requirement>();

        public IReadOnlyList<Requirement> Levels => _levels;

        /// <summary>레벨(1부터)의 조건. 정의가 없으면 조건 없음.</summary>
        public Requirement Get(int level) => level >= 1 && level <= _levels.Count ? _levels[level - 1] : default;

#if UNITY_EDITOR
        public void EditorSet(List<Requirement> levels) => _levels = levels;
#endif
    }
}
