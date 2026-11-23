namespace SpaceStation.Interior
{
    public static partial class OrbitalPort
    {
        /// <summary>
        /// 석호 수련 (식물 아틀라스 ④ 2026-11-16 — 샘플 수련 무리 · 작은 수련 · 잎만, 수면 위 5 ~ 11 mm로 층을 나눔): 가운데 석호 남서 · 북서 구석 · 회전목마 섬 동쪽 · 진주 조개 앞 · 동쪽 조개 물길.
        /// Blender에서 지은 모델에 광선을 쏴 고름 — 꼭짓점이 모두 열린 물 위 (깊이 6 cm 넘게 · 위 1.6 m 안에 물체 없음), 배 길에서 1.35 m · 거북 자리에서 2.3 m ·
        /// 날치 활공 길에서 0.9 m · 조개 · 경첩에서 1.6 m · 방 벽에서 0.3 m, 물가 0.3 ~ 2.2 m 안 (얕은 물가 쪽), 무리끼리 2.8 m,
        /// 등이 닿는 곳만 (점광원 세기 × (1 − 거리/범위)² 합 0.22 이상 — 서쪽 바다 띠 · 남서 끝처럼 어두운 물에서는 수련이 검은 점 무늬로 보였음).
        /// 템플릿 빌드가 헤엄 막힘 (SwimBlocker)을 잰 뒤 놓음 — 수련은 거북 헤엄 고리를 바꾸지 않음 (테스트가 고리와 떨어졌는지 봄).
        /// </summary>
        public static readonly GardenPlant[] Lilies =
        {
            new GardenPlant("LilyPadSmall_Mesh", 14.8f, LagoonLevel + 0.008f, 0f, 43.9f, 1f, 1f, 1f),
            new GardenPlant("LilyPadSmall_Mesh", 14.078f, LagoonLevel + 0.011f, 0.911f, 127.4f, 0.786f, 0.786f, 0.786f),
            new GardenPlant("LilyPadSmall_Mesh", 40f, LagoonLevel + 0.008f, 4f, 18.9f, 0.8f, 0.8f, 0.8f),
            new GardenPlant("LilyPadLeaves_Mesh", 40.369f, LagoonLevel + 0.005f, 5.149f, 304.5f, 0.87f, 0.87f, 0.87f),
            new GardenPlant("LilyPadSmall_Mesh", 47.2f, LagoonLevel + 0.008f, 7.6f, 61.6f, 0.8f, 0.8f, 0.8f),
            new GardenPlant("LilyPadSmall_Mesh", 47.892f, LagoonLevel + 0.011f, 6.837f, 215.5f, 0.909f, 0.909f, 0.909f),
            new GardenPlant("LilyPadLeaves_Mesh", 48.696f, LagoonLevel + 0.005f, 7.458f, 193.5f, 0.774f, 0.774f, 0.774f),
            new GardenPlant("LilyPadSmall_Mesh", 27.2f, LagoonLevel + 0.008f, 3.6f, 119.8f, 0.8f, 0.8f, 0.8f),
            new GardenPlant("LilyPadSmall_Mesh", 28.212f, LagoonLevel + 0.011f, 2.941f, 105.5f, 0.949f, 0.949f, 0.949f),
            new GardenPlant("LilyPadLeaves_Mesh", 27.46f, LagoonLevel + 0.005f, 2.081f, 209.2f, 0.858f, 0.858f, 0.858f),
            new GardenPlant("LilyPadSmall_Mesh", 14.8f, LagoonLevel + 0.008f, 8.4f, 167.1f, 1f, 1f, 1f),
            new GardenPlant("LilyPadSmall_Mesh", 13.467f, LagoonLevel + 0.011f, 8.143f, 295.5f, 0.766f, 0.766f, 0.766f),
            new GardenPlant("LilyPadSmall_Mesh", 30.8f, LagoonLevel + 0.008f, 3.6f, 135.7f, 1f, 1f, 1f),
            new GardenPlant("LilyPadSmall_Mesh", 31.382f, LagoonLevel + 0.011f, 4.677f, 188.9f, 0.845f, 0.845f, 0.845f),
        };
    }
}
