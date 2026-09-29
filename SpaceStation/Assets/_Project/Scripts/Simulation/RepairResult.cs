namespace SpaceStation.Simulation
{
    public enum RepairResult
    {
        Started,
        NotDamaged,
        AlreadyRepairing,
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
