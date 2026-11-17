namespace SpaceStation.Core
{
    /// <summary>
    /// 진화 단계별 연결 통로 모양 (사용자 2026-11-17, TASKS "진화 단계별 연결 통로"):
    /// 두 모듈 중 높은 단계 모양 — 0 기본 팔각 · 1 세라믹 캡슐 · 2 에어록 현관 포드 · 3 · 4 받침을 잇는 다리 데크 (통로 없음).
    /// 다리 데크가 2단계 이하 모듈 (닫힌 방 · 현관)에 닿는 끝에는 기존 문 대신 에어록 문 고리.
    /// 위아래 연결은 데크 · 에어록 없이 단계 기둥만.
    /// </summary>
    public readonly struct ConnectorStyle
    {
        public const int DeckStage = 3;
        public const int MaxStage = 4;

        /// <summary>0 기본 · 1 ~ 4 진화 단계.</summary>
        public readonly int Stage;
        /// <summary>A 쪽 끝 (모듈 A 표면)에 에어록 문 고리.</summary>
        public readonly bool LockAtA;
        /// <summary>B 쪽 끝에 에어록 문 고리.</summary>
        public readonly bool LockAtB;

        private ConnectorStyle(int stage, bool lockA, bool lockB)
        {
            Stage = stage;
            LockAtA = lockA;
            LockAtB = lockB;
        }

        public bool IsDeck => Stage >= DeckStage;

        public static ConnectorStyle For(int stageA, int stageB, bool vertical)
        {
            stageA = Clamp(stageA);
            stageB = Clamp(stageB);
            int stage = stageA > stageB ? stageA : stageB;
            if (vertical || stage < DeckStage)
                return new ConnectorStyle(stage, false, false);
            return new ConnectorStyle(stage, stageA < DeckStage, stageB < DeckStage);
        }

        private static int Clamp(int stage) => stage < 0 ? 0 : stage > MaxStage ? MaxStage : stage;
    }
}
