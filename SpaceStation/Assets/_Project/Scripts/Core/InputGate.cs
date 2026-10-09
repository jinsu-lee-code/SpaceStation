namespace SpaceStation.Core
{
    /// <summary>
    /// 게임 조작 키 차단 (5-9 일시정지 메뉴가 열려 있는 동안). 건설·선택·단축키 처리기가 확인한다.
    /// </summary>
    public static class InputGate
    {
        public static bool Blocked;

        /// <summary>11-12 내부 방문 중: 배속 순환 키(기본 Tab)를 휴대 패드 확대가 씀 — 배속 순환은 건너뜀.</summary>
        public static bool SpeedCycleTaken;

        private static int _escapeConsumedFrame = -1;

        /// <summary>창(연구 등)이 이번 프레임 ESC를 써서 닫혔음을 알린다 — 다른 ESC 처리(배치 취소·일시정지 메뉴)는 건너뛴다.</summary>
        public static void ConsumeEscape() => _escapeConsumedFrame = UnityEngine.Time.frameCount;

        public static bool EscapeConsumedThisFrame => _escapeConsumedFrame == UnityEngine.Time.frameCount;
    }

    /// <summary>빌드 설정의 씬 이름.</summary>
    public static class SceneNames
    {
        public const string MainMenu = "MainMenu";
        public const string Game = "Main";
    }
}
