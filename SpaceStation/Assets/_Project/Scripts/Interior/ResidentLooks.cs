using System.Collections.Generic;
using SpaceStation.Data;

namespace SpaceStation.Interior
{
    /// <summary>주민 한 명의 외형 (모델 속 이름들 + 색 고르는 번호).</summary>
    public sealed class ResidentLook
    {
        public bool Woman;
        public string Hair;                 // "앞_옆_뒤" (모델 `R2H_앞_옆_뒤_x`)
        public readonly List<string> Garments = new List<string>();
        public string Eye, Brow, Mouth, Mark;
        public int Skin, EyeColor, HairColor, TopColor, BottomColor, ShoeColor;
    }

    /// <summary>
    /// 11-11b 주민 외형 고르기 (순수 규칙). 주민 번호로 결정 — 같은 주민은 언제 봐도 같은 모습.
    /// 머리 = 성별 목록에서 앞 · 옆 · 뒤, 옷 = 상하 일체(작업복, <c>suitShare</c> 비율) 또는 상의 + 하의, 신발, 함께 입는 옷(치마 → 팬티).
    /// </summary>
    public static class ResidentLooks
    {
        public static ResidentLook Choose(int id, bool woman, ResidentLookTables t, float suitShare)
        {
            var look = new ResidentLook { Woman = woman };
            look.Hair = Pick(woman ? t.WomanFront : t.ManFront, id, 1) + "_" + Pick(woman ? t.WomanSide : t.ManSide, id, 2)
                        + "_" + Pick(woman ? t.WomanBack : t.ManBack, id, 3);
            look.Eye = Pick(t.Eyes, id, 4);
            look.Brow = Pick(t.Brows, id, 5);
            look.Mouth = Pick(t.Mouths, id, 6);
            look.Mark = Pick(t.Marks, id, 7);
            look.Skin = Index(id, 8);
            look.EyeColor = Index(id, 9);
            look.HairColor = Index(id, 10);
            look.TopColor = Index(id, 11);
            look.BottomColor = Index(id, 12);
            look.ShoeColor = Index(id, 13);

            bool suit = ResidentPlacementRules.Share(id * 31 + 14) < suitShare && Pool(t, woman, GarmentSlot.Suit).Count > 0;
            if (suit)
                Add(look, t, Pick(Pool(t, woman, GarmentSlot.Suit), id, 15));
            else
            {
                Add(look, t, Pick(Pool(t, woman, GarmentSlot.Top), id, 16));
                Add(look, t, Pick(Pool(t, woman, GarmentSlot.Bottom), id, 17));
            }
            Add(look, t, Pick(Pool(t, woman, GarmentSlot.Shoes), id, 18));
            return look;
        }

        private static void Add(ResidentLook look, ResidentLookTables t, string name)
        {
            if (string.IsNullOrEmpty(name) || look.Garments.Contains(name))
                return;
            look.Garments.Add(name);
            foreach (var g in t.Garments)
            {
                if (g.Name == name && !string.IsNullOrEmpty(g.With))
                    Add(look, t, g.With);
            }
        }

        private static List<string> Pool(ResidentLookTables t, bool woman, GarmentSlot slot)
        {
            var list = new List<string>();
            foreach (var g in t.Garments)
            {
                if (g.Slot == slot && (woman ? g.Woman : g.Man))
                    list.Add(g.Name);
            }
            return list;
        }

        /// <summary>주민 번호 + 항목 번호 → 섞은 수 (항목끼리 겹치지 않게 비트를 고루 섞음 — 곱하기만 하면 번호 간격이 일정할 때 같은 값이 반복됐음).</summary>
        private static int Index(int id, int salt)
        {
            uint h = (uint)id * 0x9E3779B1u ^ (uint)salt * 0x85EBCA77u;
            h ^= h >> 15; h *= 0x2C1B3C6Du;
            h ^= h >> 12; h *= 0x297A2D39u;
            h ^= h >> 15;
            return (int)(h & 0x7FFFFFFF);
        }

        private static string Pick(IReadOnlyList<string> list, int id, int salt)
        {
            if (list == null || list.Count == 0)
                return null;
            return list[(int)((uint)Index(id, salt) % (uint)list.Count)];
        }
    }
}
