namespace SpaceStation.Core
{
    /// <summary>
    /// 게임 조작 키 차단 (5-9 일시정지 메뉴가 열려 있는 동안). 건설·선택·단축키 처리기가 확인한다.
    /// </summary>
    public static class InputGate
    {
        public static bool Blocked;
    }

    /// <summary>빌드 설정의 씬 이름.</summary>
    public static class SceneNames
    {
        public const string MainMenu = "MainMenu";
        public const string Game = "Main";
    }
}
