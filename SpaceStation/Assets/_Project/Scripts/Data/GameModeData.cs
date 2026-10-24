using UnityEngine;

namespace SpaceStation.Data
{
    /// <summary>
    /// 메인 메뉴 게임 모드 (13-7d 공통 고리, 사용자 결정 2026-10-25): 서사판 = 캠페인 모드, 타이쿤판 = 엔드리스 모드 — 최종엔 한 게임 두 모드 (SPLIT.md).
    /// 메인 메뉴(<c>MainMenuController._modes</c>)가 모드마다 버튼을 만들고, 고른 모드는 <see cref="GameStartOptions.Mode"/> · 저장 <c>SaveMeta.Mode</c>로 이어진다.
    /// 모드 목록이 비면 메인 메뉴는 예전처럼 [새 게임] → 난이도 선택.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceStation/Game Mode", fileName = "GM_")]
    public sealed class GameModeData : ScriptableObject
    {
        [Tooltip("저장 구분 (SaveMeta.Mode) — 한 번 정하면 바꾸지 않는다 (예: campaign · endless)")]
        [SerializeField] private string _id = "";
        [Tooltip("메인 메뉴 버튼 · 시작 창 제목")]
        [SerializeField] private string _displayName = "새 게임";
        [Tooltip("시작 창 [시작] 버튼 아래 한 줄 (고정 난이도 모드)")]
        [TextArea] [SerializeField] private string _description = "";
        [Tooltip("넣으면 이 난이도로 고정 (난이도 선택 없이 [시작] 하나), 비우면 이지 · 노멀 · 하드에서 고름")]
        [SerializeField] private DifficultyPreset _fixedDifficulty;
        [Tooltip("시작 창에 튜토리얼 켜기/끄기 줄 (설정 '다음 새 게임에서 튜토리얼'과 같은 값)")]
        [SerializeField] private bool _tutorialChoice;
        [SerializeField] private string _tutorialLabel = "튜토리얼 받기";
        [Tooltip("새 게임 로딩 화면 제목")]
        [SerializeField] private string _loadingTitle = "새 정거장";
        [Tooltip("새 게임 로딩 화면 설명 (비면 '난이도 ○○')")]
        [SerializeField] private string _loadingDetail = "";

        public string Id => _id;
        public string DisplayName => _displayName;
        public string Description => _description;
        public DifficultyPreset FixedDifficulty => _fixedDifficulty;
        public bool TutorialChoice => _tutorialChoice;
        public string TutorialLabel => _tutorialLabel;
        public string LoadingTitle => _loadingTitle;
        public string LoadingDetail => _loadingDetail;

#if UNITY_EDITOR
        /// <summary>설정 메뉴 · 시험용.</summary>
        public void EditorSet(string id, string displayName, string description, DifficultyPreset fixedDifficulty,
            bool tutorialChoice, string tutorialLabel, string loadingTitle, string loadingDetail)
        {
            _id = id;
            _displayName = displayName;
            _description = description;
            _fixedDifficulty = fixedDifficulty;
            _tutorialChoice = tutorialChoice;
            _tutorialLabel = tutorialLabel;
            _loadingTitle = loadingTitle;
            _loadingDetail = loadingDetail;
        }
#endif
    }
}
