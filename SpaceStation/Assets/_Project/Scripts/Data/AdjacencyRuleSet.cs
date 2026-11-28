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

        // CONNECT_1121 A14-2 효과 종류 14 (2026-11-28, 3번 ④ V1) — 끝에 더함 (번호 유지). 성질 = AdjacencyEffects
        /// <summary>배터리 충방전 속도 × (1 + 합).</summary>
        ChargeRate,
        /// <summary>파손이 이웃으로 번지기까지 시간 × (1 + 합) — 손상 통제실이면 둘레에 주는 통제도 그만큼.</summary>
        SpreadTime,
        /// <summary>서비스 정원 × (1 + 합).</summary>
        ServiceCapacity,
        /// <summary>이 여객 포트로 오는 이민선 승객 + 합 (사람 수).</summary>
        Passengers,
        /// <summary>이민선 역병 유입 확률 × (1 + 합) — 가동 중인 검역소 가운데 가장 센 하나.</summary>
        PlagueChance,
        /// <summary>이 모듈의 수리 시간 × (1 + 합).</summary>
        RepairTime,
        /// <summary>화물선이 가져오는 양 × (1 + 합).</summary>
        CargoAmount,
        /// <summary>정거장 연구 속도 + 합 (0.1 = +10%, 가동 중인 모듈 모두 더함).</summary>
        ResearchSpeed,
        /// <summary>정책 시행 대기 × (1 + 합) — 가동 중인 관제소 가운데 가장 센 하나.</summary>
        PolicyDelay,
        /// <summary>이 포탑의 격추 확률 × (1 + 합).</summary>
        InterceptChance,
        /// <summary>이 모듈이 운석에 맞을 가중치 × (1 + 합).</summary>
        HitWeight,
        /// <summary>이 실드가 튕긴 운석이 다른 모듈에 맞을 확률 × (1 + 합).</summary>
        RicochetChance,
        /// <summary>이 모듈의 통신 세기 × (1 + 합).</summary>
        RelayPower,
        /// <summary>신호 해독 속도 × (1 + 합) — 가동 중인 통신 모듈 가운데 가장 센 하나.</summary>
        DecodeSpeed,
    }

    /// <summary>
    /// 인접 규칙 하나: 대상 모듈에 면이 맞닿은 이웃 수만큼 효과를 쌓는다.
    /// CONNECT A2 태그 틀: 대상 · 이웃을 모듈 또는 태그(<see cref="AdjacencyTag"/>)로 정한다 — 둘 다 정하면 둘 다 맞아야 함
    /// (기존 규칙은 태그를 달고 모듈로 좁혀 그대로 재현, 2026-11-27). 이웃 쪽이 둘 다 비면 모든 모듈.
    /// </summary>
    [Serializable]
    public sealed class AdjacencyRule
    {
        [Tooltip("CONNECT A8 '아무 방 ← …': 대상을 모듈 · 태그로 좁히지 않고 모든 모듈 (효과에 뜻이 있는 모듈만 — AdjacencyEffects.Relevant)")]
        [SerializeField] private bool _anyTarget;
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

        /// <summary>대상을 좁히지 않는 규칙 ("아무 방 ← 정비").</summary>
        public bool AnyTarget => _anyTarget;

        /// <summary>
        /// 이 규칙이 data 모듈을 대상으로 하는지 (대상 모듈 · 대상 태그 둘 다 맞아야 함, 둘 다 비면 아무것도 아님 — <see cref="AnyTarget"/>이면 모든 모듈)
        /// + 그 모듈에 효과가 뜻이 있어야 함 (<see cref="AdjacencyEffects.Relevant"/>, 기존 셋은 늘 참).
        /// </summary>
        public bool AppliesTo(ModuleData data)
        {
            if (data == null || !AdjacencyEffects.Relevant(_effect, data))
                return false;
            if (_anyTarget)
                return true;
            if (_target == null && _targetTag == null)
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
        public string TargetName => _target != null ? _target.DisplayName : _targetTag != null ? _targetTag.DisplayName : _anyTarget ? "모든 모듈" : "-";

        /// <summary>화면 이름: 이웃 (모듈 → 태그 → "모든 모듈").</summary>
        public string NeighborName => _neighbor != null ? _neighbor.DisplayName : _neighborTag != null ? _neighborTag.DisplayName : "모든 모듈";

        /// <summary>태그 관계 이름 ("물 사용 ← 수원") — 태그가 없으면 빈 글.</summary>
        public string TagRelation => _targetTag == null && _neighborTag == null && !_anyTarget ? string.Empty
            : $"{(_targetTag != null ? _targetTag.DisplayName : TargetName)} ← {(_neighborTag != null ? _neighborTag.DisplayName : NeighborName)}";

        /// <summary>편집기 메뉴 · 시험용으로 규칙 하나를 만듦 (CONNECT A8 ④ 관계 설정 · EditMode 시험).</summary>
        public static AdjacencyRule Create(AdjacencyEffect effect, float valuePerNeighbor, int maxStacks, string label,
            ModuleData target = null, AdjacencyTag targetTag = null, ModuleData neighbor = null, AdjacencyTag neighborTag = null,
            bool anyTarget = false, bool excludeSameType = false, int freeNeighbors = 0)
            => new AdjacencyRule
            {
                _effect = effect, _valuePerNeighbor = valuePerNeighbor, _maxStacks = maxStacks, _label = label,
                _target = target, _targetTag = targetTag, _neighbor = neighbor, _neighborTag = neighborTag,
                _anyTarget = anyTarget, _excludeSameType = excludeSameType, _freeNeighbors = freeNeighbors,
            };
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

        /// <summary>편집기 메뉴 · 시험: 규칙 목록을 통째로 바꿈 (CONNECT A8 ④ 관계 설정).</summary>
        public void EditorSetRules(IEnumerable<AdjacencyRule> rules)
        {
            _rules.Clear();
            _rules.AddRange(rules);
        }
        public float MinMultiplier => _minMultiplier;
        public float MaxMultiplier => Mathf.Max(_minMultiplier, _maxMultiplier);
    }
}
