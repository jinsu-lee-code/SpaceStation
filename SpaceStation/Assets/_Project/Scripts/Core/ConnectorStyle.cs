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
        /// <summary>두 단계 중 낮은 쪽 (내부 현관 판정).</summary>
        public readonly int LowStage;

        private ConnectorStyle(int stage, bool lockA, bool lockB, int low = 0)
        {
            Stage = stage;
            LockAtA = lockA;
            LockAtB = lockB;
            LowStage = low;
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

        /// <summary>
        /// 내부 2단계 우주복 현관 (보관함 · 벤치 · 거울): 높은 쪽이 2단계, 낮은 쪽이 1단계 이하 (닫힌 방 → 우주복 정원).
        /// 2 ↔ 2는 양쪽이 트인 문간 (보관함 없음 — 이미 우주복 차림).
        /// </summary>
        public bool IsVestibule => Stage == 2 && LowStage <= 1;

        /// <summary>내부용: 두 단계의 낮은 쪽까지 기억 (현관 판정).</summary>
        public static ConnectorStyle ForInterior(int stageA, int stageB)
        {
            var s = For(stageA, stageB, false);
            int a = Clamp(stageA), b = Clamp(stageB);
            return new ConnectorStyle(s.Stage, s.LockAtA, s.LockAtB, a < b ? a : b);
        }

        /// <summary>
        /// 내부 문 (그 모듈 쪽 문간): Open = 문짝 없이 트임 (3단계 이상 쪽 · 2 ↔ 2), Airlock = 에어록 문짝 + 문틀
        /// (3단계 이상과 붙은 2단계 이하 쪽 · 1단계 이하와 붙은 2단계 쪽 = 정원으로 나가는 현관 끝), Normal = 지금 문.
        /// </summary>
        public static DoorKind DoorAt(int stageThis, int stageOther)
        {
            stageThis = Clamp(stageThis);
            stageOther = Clamp(stageOther);
            int high = stageThis > stageOther ? stageThis : stageOther;
            if (high >= DeckStage)
                return stageThis >= DeckStage ? DoorKind.Open : DoorKind.Airlock;
            if (high == 2)
            {
                if (stageThis == 2)
                    return stageOther == 2 ? DoorKind.Open : DoorKind.Airlock;
                return DoorKind.Normal;
            }
            return DoorKind.Normal;
        }
    }

    public enum DoorKind
    {
        Normal,
        Airlock,
        Open,
    }
}
