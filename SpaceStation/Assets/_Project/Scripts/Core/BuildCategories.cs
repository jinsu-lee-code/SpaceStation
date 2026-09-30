using System;
using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Core
{
    /// <summary>
    /// 건설 메뉴 탭 계산 (4-5, 순수). 모듈이 하나도 없는 분류는 탭에서 뺀다.
    /// 숫자키 1~9는 현재 탭 안의 순서(건설 목록 순서 유지).
    /// </summary>
    public static class BuildCategories
    {
        private static readonly ModuleCategory[] All = (ModuleCategory[])Enum.GetValues(typeof(ModuleCategory));

        /// <summary>모듈이 있는 분류만 enum 순서대로.</summary>
        public static void GetAvailable(IReadOnlyList<ModuleData> modules, List<ModuleCategory> results)
        {
            results.Clear();
            foreach (var category in All)
            {
                foreach (var m in modules)
                {
                    if (m != null && m.Category == category)
                    {
                        results.Add(category);
                        break;
                    }
                }
            }
        }

        /// <summary>해당 분류의 모듈을 건설 목록 순서대로.</summary>
        public static void Filter(IReadOnlyList<ModuleData> modules, ModuleCategory category, List<ModuleData> results)
        {
            results.Clear();
            foreach (var m in modules)
            {
                if (m != null && m.Category == category)
                    results.Add(m);
            }
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
