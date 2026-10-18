using System;
using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Core
{
    /// <summary>
    /// 건설 메뉴 탭 계산 (4-5, 순수). 모듈이 하나도 없는 분류는 탭에서 뺀다.
    /// 숫자키 1~9는 현재 탭 안의 순서.
    /// 13-6a (사용자 결정 2026-10-16): 보이는 모듈(열린 것)만 · 순서 = 열리는 등급 → 가격(금속) → 건설 목록 순서.
    /// </summary>
    public static class BuildCategories
    {
        private static readonly ModuleCategory[] All = (ModuleCategory[])Enum.GetValues(typeof(ModuleCategory));

        /// <summary>보이는 모듈이 있는 분류만 enum 순서대로 (visible = null이면 모두).</summary>
        public static void GetAvailable(IReadOnlyList<ModuleData> modules, List<ModuleCategory> results, Func<ModuleData, bool> visible = null)
        {
            results.Clear();
            foreach (var category in All)
            {
                foreach (var m in modules)
                {
                    if (m != null && m.Category == category && (visible == null || visible(m)))
                    {
                        results.Add(category);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 해당 분류의 보이는 모듈. unlockGrade가 있으면 열리는 등급(음수 = 맨 뒤) → 금속 비용 순, 같으면 건설 목록 순서.
        /// 둘 다 null이면 건설 목록 순서 그대로.
        /// </summary>
        public static void Filter(IReadOnlyList<ModuleData> modules, ModuleCategory category, List<ModuleData> results,
            Func<ModuleData, bool> visible = null, Func<ModuleData, int> unlockGrade = null)
        {
            results.Clear();
            foreach (var m in modules)
            {
                if (m != null && m.Category == category && (visible == null || visible(m)))
                    results.Add(m);
            }
            if (unlockGrade == null || results.Count < 2)
                return;
            var order = new Dictionary<ModuleData, int>();
            for (int i = 0; i < results.Count; i++)
                order[results[i]] = i;
            results.Sort((a, b) =>
            {
                int ga = unlockGrade(a), gb = unlockGrade(b);
                ga = ga < 0 ? int.MaxValue : ga;
                gb = gb < 0 ? int.MaxValue : gb;
                if (ga != gb)
                    return ga.CompareTo(gb);
                int c = MetalCost(a).CompareTo(MetalCost(b));
                return c != 0 ? c : order[a].CompareTo(order[b]);
            });
        }

        /// <summary>건설비 중 금속 (13-6a 정렬 기준 — 지금 48종 모두 금속만).</summary>
        public static float MetalCost(ModuleData data)
        {
            float sum = 0f;
            if (data?.BuildCost == null)
                return sum;
            foreach (var c in data.BuildCost)
                if (c.Type == ResourceType.Metal)
                    sum += c.Amount;
            return sum;
        }

        /// <summary>탭 순환 (direction +1 / -1). current가 목록에 없으면 첫 탭.</summary>
        public static ModuleCategory Cycle(IReadOnlyList<ModuleCategory> available, ModuleCategory current, int direction)
        {
            if (available.Count == 0)
                return current;
            int index = -1;
            for (int i = 0; i < available.Count; i++)
            {
                if (available[i] == current)
                    index = i;
            }
            if (index < 0)
                return available[0];
            int count = available.Count;
            return available[((index + direction) % count + count) % count];
        }
    }
}
