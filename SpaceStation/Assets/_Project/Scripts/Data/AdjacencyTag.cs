using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// CONNECT A2 인접 태그 (사용자 결정 2026-11-22): 인접 규칙을 특정 모듈이 아니라 성질에 건다 (예: 수원 · 녹지 · 소음원 · 발열원 · 주거).
    /// 모듈은 <see cref="ModuleData.AdjacencyTags"/>에 태그 1~2개를 갖고, 규칙은 태그 쌍(대상 태그 ← 이웃 태그)으로 쓴다.
    /// 태그 표 = CONNECT_1121 A8 (기본 36종). 진화 모듈의 상속은 다음 단계.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Adjacency Tag", fileName = "AT_")]
    public sealed class AdjacencyTag : ScriptableObject
    {
        [Tooltip("화면 이름 (예: 수원)")]
        [SerializeField] private string _displayName;
        [Tooltip("한 줄 설명 (예: 물을 내놓는 모듈 — 물 재활용기 · 물 추출기)")]
        [SerializeField, TextArea] private string _description;

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
        public string Description => _description;

        /// <summary>설명 끝 괄호 속 모듈 이름 ("시끄러운 모듈 (채굴 도킹 · 제련소 · 방어 포탑)" → "채굴 도킹 · 제련소 · 방어 포탑"), 없으면 빈 글.</summary>
        public string Members
        {
            get
            {
                if (string.IsNullOrEmpty(_description) || !_description.EndsWith(")"))
                    return string.Empty;
                int open = _description.LastIndexOf('(');
                return open < 0 ? string.Empty : _description.Substring(open + 1, _description.Length - open - 2).Trim();
            }
        }

        public void EditorSet(string displayName, string description)
        {
            _displayName = displayName;
            _description = description;
        }
    }
}
