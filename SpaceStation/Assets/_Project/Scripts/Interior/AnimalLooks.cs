namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-11d 동물 주민 겉모습 고르기 (순수 규칙): 주민 번호로 종류와 털색 — 같은 주민은 언제 들어가도 같은 모습.
    /// 종류와 털색은 서로 다른 섞기 값을 써서, 종류가 같아도 털색이 고르게 갈림.
    /// </summary>
    public static class AnimalLooks
    {
        public static int Species(int id, int speciesCount)
        {
            if (speciesCount <= 1)
                return 0;
            return Pick(ResidentPlacementRules.Share(id * 7 + 3), speciesCount);
        }

        public static int Fur(int id, int furCount)
        {
            if (furCount <= 1)
                return 0;
            return Pick(ResidentPlacementRules.Share(id * 13 + 5), furCount);
        }

        private static int Pick(float share, int count) => share * count >= count ? count - 1 : (int)(share * count);
    }
}
