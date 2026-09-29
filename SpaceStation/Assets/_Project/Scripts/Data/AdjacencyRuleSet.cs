using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    public enum AdjacencyEffect
    {
        /// <summary>대상 모듈의 생산(전력 발전 포함)에 (1 + 합계)를 곱한다.</summary>
        Production,
        /// <summary>대상 모듈의 전력 외 입력 자원 소비에 (1 + 합계)를 곱한다.</summary>
        Consumption,
        /// <summary>대상 모듈의 수용 인구에 합계를 더한다.</summary>
        Housing,
    }

    /// <summary>인접 규칙 하나: 대상 모듈에 면이 맞닿은 이웃 수만큼 효과를 쌓는다.</summary>
    [Serializable]
    public sealed class AdjacencyRule
    {
        [SerializeField] private ModuleData _target;
        [Tooltip("비우면 모든 모듈이 이웃으로 인정됨")]
        [SerializeField] private ModuleData _neighbor;
        [Tooltip("이웃이 '모든 모듈'일 때 대상과 같은 종류는 세지 않음 (예: 태양광끼리는 그늘 없음)")]
        [SerializeField] private bool _excludeSameType;
        [SerializeField] private AdjacencyEffect _effect;
        [Tooltip("이웃 1개당 값. 배율형(Production/Consumption)은 0.15 = +15%, Housing은 인원")]
        [SerializeField] private float _valuePerNeighbor;
        [Tooltip("이 수만큼의 이웃은 효과 없음 (예: 태양광 그늘은 1개 면제)")]
        [SerializeField, Min(0)] private int _freeNeighbors;
        [Tooltip("최대 중첩 횟수")]
        [SerializeField, Min(1)] private int _maxStacks = 3;
        [Tooltip("UI 표시용 짧은 설명 (예: 수로 공유)")]
        [SerializeField] private string _label;

        public ModuleData Target => _target;
        public ModuleData Neighbor => _neighbor;
        public AdjacencyEffect Effect => _effect;
        public float ValuePerNeighbor => _valuePerNeighbor;
        public int FreeNeighbors => _freeNeighbors;
        public int MaxStacks => Mathf.Max(1, _maxStacks);
        public string Label => _label;

        public bool ExcludeSameType => _excludeSameType;

        public bool Matches(ModuleData neighbor)
        {
            if (_excludeSameType && neighbor == _target)
                return false;
            return _neighbor == null || _neighbor == neighbor;
        }
    }

    /// <summary>공간 인접 효과 규칙 목록 (BALANCE 16번). 추가 규칙은 Phase 6 이후 (GDD 13번).</summary>
    [CreateAssetMenu(menuName = "SpaceStation/Adjacency Rule Set", fileName = "AdjacencyRules")]
    public sealed class AdjacencyRuleSet : ScriptableObject
    {
        [Tooltip("배율형 효과 합산 후 배율의 범위")]
        [SerializeField] private float _minMultiplier = 0f;
        [SerializeField] private float _maxMultiplier = 2f;
        [SerializeField] private List<AdjacencyRule> _rules = new List<AdjacencyRule>();

        public IReadOnlyList<AdjacencyRule> Rules => _rules;
        public float MinMultiplier => _minMultiplier;
        public float MaxMultiplier => Mathf.Max(_minMultiplier, _maxMultiplier);
    }
}
