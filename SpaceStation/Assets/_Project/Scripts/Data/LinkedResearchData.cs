using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 12-4 연계 연구 (사용자 결정 2026-10-14): 필요한 상위(진화) 모듈이 가동 중일 때만 연구할 수 있는 1회 연구.
    /// 효과(<see cref="ResearchLevel.Modifiers"/>)도 그 모듈이 가동 중일 때만 (잃으면 꺼지고, 다시 지으면 켜짐).
    /// 일반 연구 슬롯(연구소)을 쓰고, 비용 · 전력 · 시간은 <see cref="Level"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Linked Research", fileName = "LR_New")]
    public sealed class LinkedResearchData : ScriptableObject
    {
        [SerializeField] private string _displayName = "연계 연구";
        [Tooltip("연구 창에서 이 카테고리 카드 아래에 보임")]
        [SerializeField] private ResearchCategory _category;
        [Tooltip("이 모듈이 하나라도 가동 중이어야 연구 · 효과")]
        [SerializeField] private ModuleData _requiredModule;
        [SerializeField] private ResearchLevel _level = new ResearchLevel();

        public string DisplayName => _displayName;
        public ResearchCategory Category => _category;
        public ModuleData RequiredModule => _requiredModule;
        public ResearchLevel Level => _level;

#if UNITY_EDITOR
        public void EditorSet(string displayName, ResearchCategory category, ModuleData required, ResearchLevel level)
        {
            _displayName = displayName;
            _category = category;
            _requiredModule = required;
            _level = level;
        }
#endif
    }
}
