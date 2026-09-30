namespace SpaceStation.Data
{
    /// <summary>건설 메뉴 탭 분류 (4-5). 순서 = 탭 순서.</summary>
    public enum ModuleCategory
    {
        /// <summary>태양광, 배터리</summary>
        Power,
        /// <summary>거주, 산소, 물, 농장 (+의료·여가 예정)</summary>
        Life,
        /// <summary>창고, 채굴 도킹 (+정비 베이 예정)</summary>
        Industry,
        /// <summary>실드·포탑 예정 (모듈이 없으면 탭 숨김)</summary>
        Defense,
    }

    public static class ModuleCategoryExtensions
    {
        public static string DisplayName(this ModuleCategory category)
        {
            switch (category)
            {
                case ModuleCategory.Power: return "전력";
                case ModuleCategory.Life: return "생활";
                case ModuleCategory.Industry: return "산업";
                case ModuleCategory.Defense: return "방어";
                default: return category.ToString();
            }
        }
    }
}
