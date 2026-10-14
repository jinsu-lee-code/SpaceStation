namespace SpaceStation.Data
{
    /// <summary>건설 메뉴 탭 분류 (4-5). 순서 = 탭 순서.</summary>
    public enum ModuleCategory
    {
        /// <summary>
        /// 생산 탭 (사용자 결정 2026-10-14: 전력 + 생활 자원 생산을 합침) — 태양광 · 배터리 · 발전 · 방열판 + 산소 · 물 · 식량 생산.
        /// 이름은 Power 그대로 (에셋에 숫자로 저장 · 설정 스크립트가 이름으로 찾음), 표시만 "생산".
        /// </summary>
        Power,
        /// <summary>거주, 의료, 휴게, 회전 링, 학교 · 검역 · 상업 · 여객 포트</summary>
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
                case ModuleCategory.Power: return "생산";
                case ModuleCategory.Life: return "생활";
                case ModuleCategory.Industry: return "산업";
                case ModuleCategory.Defense: return "방어";
                default: return category.ToString();
            }
        }
    }
}
