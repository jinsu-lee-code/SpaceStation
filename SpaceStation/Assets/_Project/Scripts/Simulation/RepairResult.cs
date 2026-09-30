namespace SpaceStation.Simulation
{
    public enum RepairResult
    {
        Started,
        /// <summary>비용을 내고 수리 대기열에 들어감 (슬롯이 모두 사용 중, 4-6).</summary>
        Queued,
        NotDamaged,
        AlreadyRepairing,
        AlreadyQueued,
        InsufficientResources,
    }

    public enum RebuildResult
    {
        Done,
        NotAllowed,
        /// <summary>현재 등급에서 잠겼거나 설치 한도 초과 (등급 하락 후).</summary>
        Locked,
        InsufficientResources,
    }
}
