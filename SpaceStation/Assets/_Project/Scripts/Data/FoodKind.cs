using System;

namespace SpaceStation.Data
{
    /// <summary>
    /// 식단 다양성 요구의 식품 종류 (남은 요구, 사용자 결정 2026-10-22). 식량을 만드는 모듈에 붙인다 — 여러 종류면 생산량을 똑같이 나눠 셈.
    /// 충족 비율 = 종류마다 생산 비중을 (1 / 종류 수)까지만 더한 값 (채소 · 단백질 반반 = 100%, 한 종류만 = 50%).
    /// </summary>
    [Flags]
    public enum FoodKind
    {
        None = 0,
        /// <summary>채소 (수경 농장 · 생태 계열).</summary>
        Vegetable = 1,
        /// <summary>단백질 (단백질 배양실).</summary>
        Protein = 2,
    }

    public static class FoodKindExtensions
    {
        /// <summary>식단에 세는 종류들 (순서 = 화면 표시 순서).</summary>
        public static readonly FoodKind[] All = { FoodKind.Vegetable, FoodKind.Protein };

        public static string DisplayName(this FoodKind kind)
        {
            switch (kind)
            {
                case FoodKind.Vegetable: return "채소";
                case FoodKind.Protein: return "단백질";
                default: return "-";
            }
        }

        /// <summary>붙은 종류 이름들 ("채소 · 단백질").</summary>
        public static string Text(this FoodKind kinds)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var k in All)
            {
                if ((kinds & k) == 0)
                    continue;
                if (sb.Length > 0)
                    sb.Append(" · ");
                sb.Append(k.DisplayName());
            }
            return sb.ToString();
        }

        public static int Count(this FoodKind kinds)
        {
            int n = 0;
            foreach (var k in All)
                if ((kinds & k) != 0)
                    n++;
            return n;
        }
    }
}
