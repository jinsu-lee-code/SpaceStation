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

    /// <summary>
    /// 인접 규칙 하나: 대상 모듈에 면이 맞닿은 이웃 수만큼 효과를 쌓는다.
    /// CONNECT A2 태그 틀: 대상 · 이웃을 모듈 또는 태그(<see cref="AdjacencyTag"/>)로 정한다 — 둘 다 정하면 둘 다 맞아야 함
    /// (기존 규칙은 태그를 달고 모듈로 좁혀 그대로 재현, 2026-11-27). 이웃 쪽이 둘 다 비면 모든 모듈.
    /// </summary>
    [Serializable]
    public sealed class AdjacencyRule
    {
        [Tooltip("대상 모듈 (비우면 대상 태그만 봄)")]
        [SerializeField] private ModuleData _target;
        [Tooltip("CONNECT A2 대상 태그 (비우면 대상 모듈만 봄, 둘 다 정하면 둘 다 맞아야 함)")]
        [SerializeField] private AdjacencyTag _targetTag;
        [Tooltip("이웃 모듈 (이웃 태그와 함께 비우면 모든 모듈이 이웃으로 인정됨)")]
        [SerializeField] private ModuleData _neighbor;
        [Tooltip("CONNECT A2 이웃 태그 (비우면 이웃 모듈만 봄, 둘 다 정하면 둘 다 맞아야 함)")]
        [SerializeField] private AdjacencyTag _neighborTag;
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

        /// <summary>대상 모듈 (태그만으로 정한 규칙이면 null — 적용된 모듈은 <c>AppliedAdjacency.Target</c>).</summary>
        public ModuleData Target => _target;
        public AdjacencyTag TargetTag => _targetTag;
        /// <summary>이웃 모듈 (태그만 · 모든 모듈이면 null).</summary>
        public ModuleData Neighbor => _neighbor;
        public AdjacencyTag NeighborTag => _neighborTag;
        public AdjacencyEffect Effect => _effect;
        public float ValuePerNeighbor => _valuePerNeighbor;
        public int FreeNeighbors => _freeNeighbors;
        public int MaxStacks => Mathf.Max(1, _maxStacks);
        public string Label => _label;

        public bool ExcludeSameType => _excludeSameType;

        /// <summary>이웃 조건이 없음 (모든 모듈 — 그늘).</summary>
        public bool AnyNeighbor => _neighbor == null && _neighborTag == null;

        /// <summary>이 규칙이 data 모듈을 대상으로 하는지 (대상 모듈 · 대상 태그 둘 다 맞아야 함, 둘 다 비면 아무것도 아님).</summary>
        public bool AppliesTo(ModuleData data)
        {
            if (data == null || (_target == null && _targetTag == null))
                return false;
            if (_target != null && _target != data)
                return false;
            return _targetTag == null || data.HasAdjacencyTag(_targetTag);
        }

        /// <summary>target 모듈의 이웃으로 neighbor를 세는지 (같은 종류 제외 · 이웃 모듈 · 이웃 태그).</summary>
        public bool Matches(ModuleData target, ModuleData neighbor)
        {
            if (neighbor == null || (_excludeSameType && neighbor == target))
                return false;
            return CouldNeighbor(neighbor);
        }

        /// <summary>대상 모듈이 정해진 규칙의 예전 호출 (대상 = <see cref="Target"/>).</summary>
        public bool Matches(ModuleData neighbor) => Matches(_target, neighbor);

        /// <summary>대상과 무관하게 이웃 조건(모듈 · 태그)에 맞는지 — 건설 메뉴 설명 · 미리보기.</summary>
        public bool CouldNeighbor(ModuleData neighbor)
        {
            if (neighbor == null)
                return false;
            if (_neighbor != null && _neighbor != neighbor)
                return false;
            return _neighborTag == null || neighbor.HasAdjacencyTag(_neighborTag);
        }

        /// <summary>화면 이름: 대상 (모듈이 있으면 모듈 이름, 태그만이면 태그 이름).</summary>
        public string TargetName => _target != null ? _target.DisplayName : _targetTag != null ? _targetTag.DisplayName : "-";

        /// <summary>화면 이름: 이웃 (모듈 → 태그 → "모든 모듈").</summary>
        public string NeighborName => _neighbor != null ? _neighbor.DisplayName : _neighborTag != null ? _neighborTag.DisplayName : "모든 모듈";

        /// <summary>태그 관계 이름 ("물 사용 ← 수원") — 태그가 없으면 빈 글.</summary>
        public string TagRelation => _targetTag == null && _neighborTag == null ? string.Empty
            : $"{(_targetTag != null ? _targetTag.DisplayName : TargetName)} ← {(_neighborTag != null ? _neighborTag.DisplayName : NeighborName)}";
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
