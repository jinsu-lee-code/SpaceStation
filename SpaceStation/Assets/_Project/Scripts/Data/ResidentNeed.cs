namespace SpaceStation.Data
{
    /// <summary>거주자 요구 (4-9, BALANCE 20번). 등급이 오르면 새 요구가 생기고, 서비스 모듈이 범위 안 주민을 담당한다.</summary>
    public enum ResidentNeed
    {
        None,
        Medical,
        Recreation,
    }

    public static class ResidentNeedExtensions
    {
        public static string DisplayName(this ResidentNeed need)
        {
            switch (need)
            {
                case ResidentNeed.Medical: return "의료";
                case ResidentNeed.Recreation: return "여가";
                default: return "-";
            }
        }
    }
}
