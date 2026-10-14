using System;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>진화 단계 하나의 레시피 해금 연구 비용 (12-4).</summary>
    [Serializable]
    public sealed class RecipeResearchCost
    {
        [Tooltip("연구 시작 때 1회 (환급 없음)")]
        public List<ResourceAmount> StartCost = new List<ResourceAmount>();
        [Tooltip("연구 시작 때 쓰는 연구 포인트 (내부 보급 특별 상자)")]
        [Min(0)] public int ResearchPoints = 1;
        [Tooltip("연구 중 전력 수요 추가")]
        [Min(0f)] public float PowerDemand = 10f;
        [Tooltip("전력 100% 기준 소요 시간(초)")]
        [Min(1f)] public float Duration = 120f;
    }

    /// <summary>
    /// 12-4 연구 확장 (사용자 결정 2026-10-14, PHASESET E2, RESEARCH.md 10번, BALANCE 38번):
    /// - 카테고리 Lv.5~8(<see cref="ResearchCategoryData"/>에 레벨 추가)을 연구하면 그 분야 진화 레시피가 그림자로 공개 (Lv.(4 + 단계)).
    /// - 그림자 레시피는 레시피 연구 슬롯(연구소 계통 진화 모듈 <see cref="ModuleData.RecipeSlots"/>)에서 자원 + 연구 포인트로 해금.
    /// - 연계 연구(<see cref="LinkedResearchData"/>): 특정 상위 모듈이 가동 중일 때만 연구 · 효과 (일반 연구 슬롯을 씀).
    /// 메뉴 SpaceStation/Research/Setup Expansion이 만든다.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Research Expansion Config", fileName = "ResearchExpansionConfig")]
    public sealed class ResearchExpansionConfig : ScriptableObject
    {
        [Tooltip("진화 단계(1부터)별 레시피 해금 연구 비용")]
        [SerializeField] private List<RecipeResearchCost> _stageCosts = new List<RecipeResearchCost>();
        [Tooltip("연계 연구 목록")]
        [SerializeField] private List<LinkedResearchData> _linked = new List<LinkedResearchData>();
        [Tooltip("카테고리 레벨이 (이 값 + 단계) 이상이면 그 분야 레시피가 그림자로 공개 (4 → 1단계 = Lv.5)")]
        [SerializeField, Min(0)] private int _shadowLevelOffset = 4;

        public IReadOnlyList<LinkedResearchData> Linked => _linked;
        public int ShadowLevelOffset => _shadowLevelOffset;

        public RecipeResearchCost CostFor(int stage)
        {
            if (_stageCosts.Count == 0)
                return new RecipeResearchCost();
            return _stageCosts[Mathf.Clamp(stage - 1, 0, _stageCosts.Count - 1)];
        }

#if UNITY_EDITOR
        public void EditorSet(List<RecipeResearchCost> costs, List<LinkedResearchData> linked)
        {
            _stageCosts = costs;
            _linked = linked;
        }
#endif
    }
}
