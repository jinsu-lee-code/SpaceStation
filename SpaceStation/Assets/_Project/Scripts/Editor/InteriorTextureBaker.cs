using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// Phase 11 내부 표면 디테일 텍스처 만들기 (메뉴 SpaceStation/Interior/Bake Panel Textures, Interior/Setup이 없으면 자동):
    /// 한 장 = 2m × 2m 타일(1024px), 패널 2×2 (이음 홈 + 안쪽 판, 패널마다 볼트 / 가로 리브 / 환기 슬롯 / 미끄럼 점), 잔잔한 굴곡 노이즈.
    /// 높이에서 노멀 맵(`T_InteriorPanel_N`)과 마스크(`T_InteriorPanel_M`: R = 틈 어둡게, G = 얼룩)를 만든다. 모든 패턴은 이어 붙여도 끊기지 않는다.
    /// </summary>
    public static class InteriorTextureBaker
    {
        public const string Folder = "Assets/_Project/Art/Textures/Interior/";
        public const string NormalPath = Folder + "T_InteriorPanel_N.png";
        public const string MaskPath = Folder + "T_InteriorPanel_M.png";

        private const int N = 1024;
        private const int Panel = N / 2;

        [MenuItem("SpaceStation/Interior/Bake Panel Textures")]
        public static void Bake()
        {
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                    h[y * N + x] = Height(x, y);
            }

            // 틈 AO: 주변 평균보다 낮은 곳일수록 어둡게
            var blur = BoxBlur(h, 7);
            var normal = new Color32[N * N];
            var mask = new Color32[N * N];
            const float strength = 2.2f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (H(h, x + 1, y) - H(h, x - 1, y)) * strength;
                    float dy = (H(h, x, y + 1) - H(h, x, y - 1)) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    normal[y * N + x] = new Color32(To8(n.x * 0.5f + 0.5f), To8(n.y * 0.5f + 0.5f), To8(n.z * 0.5f + 0.5f), 255);

                    float cavity = Mathf.Clamp01(1f - Mathf.Max(0f, blur[y * N + x] - h[y * N + x]) * 3.5f);
                    float ao = Mathf.Lerp(0.35f, 1f, cavity);
                    float grime = Mathf.Lerp(0.72f, 1f, Noise(x, y, 8, 11) * 0.6f + Noise(x, y, 32, 23) * 0.4f) * Mathf.Lerp(0.8f, 1f, cavity);
                    mask[y * N + x] = new Color32(To8(ao), To8(grime), 255, 255);
                }
            }

            System.IO.Directory.CreateDirectory(Folder);
            Save(NormalPath, normal);
            Save(MaskPath, mask);
            AssetDatabase.Refresh();
            Configure(NormalPath, true);
            Configure(MaskPath, false);
            Debug.Log("[InteriorTextureBaker] 패널 텍스처 생성");
        }

        public const string FabricNormalPath = Folder + "T_InteriorFabric_N.png";
        public const string FabricMaskPath = Folder + "T_InteriorFabric_M.png";
        private const int FabricThreads = 16; // 한 장에 실 16가닥 (재질 타일링 4 → 0.25m 타일, 실 약 1.5cm)

        /// <summary>
        /// 11-5 천 재질(베개·매트리스·담요) 텍스처: 평직(실이 한 가닥씩 위아래로 엇갈림) 높이 → 노멀 + 마스크(R 실 사이 그늘, G 잔잔한 얼룩).
        /// 크기 = 패널과 같은 N, 실 주기 = N / 16 이라 이어 붙여도 끊기지 않는다.
        /// </summary>
        [MenuItem("SpaceStation/Interior/Bake Fabric Textures")]
        public static void BakeFabric()
        {
            var h = new float[N * N];
            int period = N / FabricThreads;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int i = x / period, j = y / period;
                    float u = (x % period + 0.5f) / period, v = (y % period + 0.5f) / period;
                    // 날실(세로): 단면은 둥글고, 씨실을 하나 건너 위로 올라옴
                    float warp = 0.35f * Mathf.Sqrt(Mathf.Sin(Mathf.PI * u)) + 0.3f * (0.5f + 0.5f * Mathf.Cos(Mathf.PI * (y / (float)period) + Mathf.PI * i));
                    float weft = 0.35f * Mathf.Sqrt(Mathf.Sin(Mathf.PI * v)) + 0.3f * (0.5f + 0.5f * Mathf.Cos(Mathf.PI * (x / (float)period) + Mathf.PI * (j + 1)));
                    // 실 사이 틈은 낮게
                    float gapU = Smooth(0f, 0.12f, Mathf.Min(u, 1f - u)), gapV = Smooth(0f, 0.12f, Mathf.Min(v, 1f - v));
                    float hh = Mathf.Max(warp * gapU, weft * gapV);
                    // 섬유 결 (실 방향으로 늘어난 잔 노이즈)
                    hh += (Noise(x, y, 128, 31) - 0.5f) * 0.05f;
                    h[y * N + x] = hh;
                }
            }
            var blur = BoxBlur(h, 4);
            var normal = new Color32[N * N];
            var mask = new Color32[N * N];
            const float strength = 3.0f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (H(h, x + 1, y) - H(h, x - 1, y)) * strength;
                    float dy = (H(h, x, y + 1) - H(h, x, y - 1)) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    normal[y * N + x] = new Color32(To8(n.x * 0.5f + 0.5f), To8(n.y * 0.5f + 0.5f), To8(n.z * 0.5f + 0.5f), 255);
                    float cavity = Mathf.Clamp01(1f - Mathf.Max(0f, blur[y * N + x] - h[y * N + x]) * 4f);
                    float ao = Mathf.Lerp(0.55f, 1f, cavity);
                    float mottle = Mathf.Lerp(0.85f, 1f, Noise(x, y, 4, 41) * 0.6f + Noise(x, y, 16, 43) * 0.4f);
                    mask[y * N + x] = new Color32(To8(ao), To8(mottle), 255, 255);
                }
            }
            System.IO.Directory.CreateDirectory(Folder);
            Save(FabricNormalPath, normal);
            Save(FabricMaskPath, mask);
            AssetDatabase.Refresh();
            Configure(FabricNormalPath, true);
            Configure(FabricMaskPath, false);
            Debug.Log("[InteriorTextureBaker] 천 텍스처 생성");
        }

        public const string ConcreteNormalPath = Folder + "T_InteriorConcrete_N.png";
        public const string ConcreteMaskPath = Folder + "T_InteriorConcrete_M.png";

        /// <summary>
        /// 12-5 콘크리트 재질(방사선 차폐실 벽 · 원통 천장) 텍스처: 한 장 = 2m × 2m (패널과 같은 타일링 0.5) —
        /// 거푸집 이음(1m마다 얕은 홈) · 폼타이 구멍(0.5m 격자, 원뿔 홈) · 작은 기포 구멍 · 잔잔한 굴곡 → 노멀 + 마스크(R 틈 그늘, G 얼룩 — 패널보다 진하게).
        /// </summary>
        [MenuItem("SpaceStation/Interior/Bake Concrete Textures")]
        public static void BakeConcrete()
        {
            var h = new float[N * N];
            const int form = N / 2;     // 거푸집 판 1m
            const int tie = N / 4;      // 폼타이 0.5m
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float hh = 0.5f;
                    // 거푸집 이음 (가는 홈 + 판 가장자리가 살짝 솟음)
                    float ex = Mathf.Min(x % form, form - 1 - x % form), ey = Mathf.Min(y % form, form - 1 - y % form);
                    float e = Mathf.Min(ex, ey);
                    hh -= 0.22f * (1f - Smooth(0f, 2.5f, e));
                    hh += 0.03f * Bump(Mathf.Abs(e - 5f), 3f);
                    // 폼타이 구멍 (거푸집 판 안쪽 0.25m · 0.75m 자리)
                    float tx = Mathf.Repeat(x - tie * 0.5f, tie) - tie * 0.5f, ty = Mathf.Repeat(y - tie * 0.5f, tie) - tie * 0.5f;
                    float d = Mathf.Sqrt(tx * tx + ty * ty);
                    hh -= 0.45f * (1f - Smooth(4f, 11f, d));
                    hh += 0.04f * Bump(Mathf.Abs(d - 12f), 2f);
                    // 기포 구멍 (작은 점, 한 칸에 하나 정도)
                    float pore = Noise(x, y, 160, 71);
                    if (pore > 0.86f)
                        hh -= 0.25f * Smooth(0.86f, 0.95f, pore);
                    // 굴곡 (큰 물결 + 잔결)
                    hh += (Noise(x, y, 8, 73) - 0.5f) * 0.06f + (Noise(x, y, 48, 79) - 0.5f) * 0.025f + (Noise(x, y, 256, 83) - 0.5f) * 0.02f;
                    h[y * N + x] = hh;
                }
            }
            var blur = BoxBlur(h, 6);
            var normal = new Color32[N * N];
            var mask = new Color32[N * N];
            const float strength = 2.0f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (H(h, x + 1, y) - H(h, x - 1, y)) * strength;
                    float dy = (H(h, x, y + 1) - H(h, x, y - 1)) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    normal[y * N + x] = new Color32(To8(n.x * 0.5f + 0.5f), To8(n.y * 0.5f + 0.5f), To8(n.z * 0.5f + 0.5f), 255);
                    float cavity = Mathf.Clamp01(1f - Mathf.Max(0f, blur[y * N + x] - h[y * N + x]) * 4f);
                    float ao = Mathf.Lerp(0.4f, 1f, cavity);
                    // 얼룩: 거푸집 판마다 다른 밝기 + 큰 얼룩 + 잔 얼룩 (모두 타일 주기에 맞음)
                    float panel = Hash(x / form, y / form, 89) * 0.12f;
                    float stain = Noise(x, y, 6, 97) * 0.5f + Noise(x, y, 24, 101) * 0.3f + Noise(x, y, 64, 103) * 0.2f;
                    float grime = Mathf.Lerp(0.62f, 1f, stain) - panel;
                    mask[y * N + x] = new Color32(To8(ao), To8(grime), 255, 255);
                }
            }
            System.IO.Directory.CreateDirectory(Folder);
            Save(ConcreteNormalPath, normal);
            Save(ConcreteMaskPath, mask);
            AssetDatabase.Refresh();
            Configure(ConcreteNormalPath, true);
            Configure(ConcreteMaskPath, false);
            Debug.Log("[InteriorTextureBaker] 콘크리트 텍스처 생성");
        }

        public const string BarkNormalPath = Folder + "T_InteriorBark_N.png";
        public const string BarkMaskPath = Folder + "T_InteriorBark_M.png";

        /// <summary>
        /// 수리 사령부 고목 껍질 (2026-11-13, 사용자 "실제 자연의 나무처럼 디테일 빡세게"): 한 장 = 1.6 m (타일링 0.625) —
        /// 세로로 긴 껍질 판 (가로 10 · 세로 3칸 보로노이, 판 가장자리가 굽이침) 사이 깊은 골 + 가는 버금 골 + 판 위 세로 결 · 잔 굴곡 → 노멀 + 마스크 (R 골 그늘, G 판마다 밝기).
        /// 작은 구멍 · 점 무늬 없음 (환공포증), 골은 이어진 선.
        /// </summary>
        [MenuItem("SpaceStation/Interior/Bake Bark Textures")]
        public static void BakeBark()
        {
            var h = new float[N * N];
            var plate = new float[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float warp = (Noise(x, y, 4, 211) - 0.5f) * 70f + (Noise(x, y, 12, 213) - 0.5f) * 18f;
                    float e1 = Edge(x + warp, y, 10, 3, 217, out float id1);
                    float e2 = Edge(x + warp * 0.6f, y, 22, 6, 223, out _);
                    float furrow = 1f - Smooth(0f, 0.2f, e1);
                    float fine = 1f - Smooth(0f, 0.1f, e2);
                    float hh = 0.62f - 0.55f * furrow - 0.14f * fine * (1f - furrow);
                    hh += 0.035f * Mathf.Sin(2f * Mathf.PI * 64f * x / N + 6f * Noise(x, y, 6, 227));    // 세로 결
                    hh += (Noise(x, y, 24, 229) - 0.5f) * 0.06f + (Noise(x, y, 96, 233) - 0.5f) * 0.03f;
                    hh += (id1 - 0.5f) * 0.05f;
                    h[y * N + x] = hh;
                    plate[y * N + x] = id1;
                }
            }
            var blur = BoxBlur(h, 8);
            var normal = new Color32[N * N];
            var mask = new Color32[N * N];
            const float strength = 2.6f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (H(h, x + 1, y) - H(h, x - 1, y)) * strength;
                    float dy = (H(h, x, y + 1) - H(h, x, y - 1)) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    normal[y * N + x] = new Color32(To8(n.x * 0.5f + 0.5f), To8(n.y * 0.5f + 0.5f), To8(n.z * 0.5f + 0.5f), 255);
                    float cavity = Mathf.Clamp01(1f - Mathf.Max(0f, blur[y * N + x] - h[y * N + x]) * 3.2f);
                    float ao = Mathf.Lerp(0.3f, 1f, cavity);
                    float tone = Mathf.Lerp(0.78f, 1f, plate[y * N + x]) * Mathf.Lerp(0.85f, 1f, Noise(x, y, 8, 239));
                    mask[y * N + x] = new Color32(To8(ao), To8(tone), 255, 255);
                }
            }
            System.IO.Directory.CreateDirectory(Folder);
            Save(BarkNormalPath, normal);
            Save(BarkMaskPath, mask);
            AssetDatabase.Refresh();
            Configure(BarkNormalPath, true);
            Configure(BarkMaskPath, false);
            Debug.Log("[InteriorTextureBaker] 고목 껍질 텍스처 생성");
        }

        public const string SandNormalPath = Folder + "T_InteriorSand_N.png";
        public const string SandMaskPath = Folder + "T_InteriorSand_M.png";

        /// <summary>
        /// 궤도 항만 모래 해변 (2026-11-16, 사용자 "바닥이 모래 느낌이 안 든다"): 한 장 = 1 m (타일링 1) — 바람 물결 (한 장에 열두 줄, 낮은 주파수로 굽이침 · 바람 쪽이 완만한 비대칭)
        /// + 잔 알갱이 결 (아주 높은 주파수 · 낮은 대비) → 노멀 + 마스크 (R 물결 골 그늘, G 큰 얼룩 밝기). 점 · 구멍 무늬 없음 (환공포증 — 알갱이는 이어진 잔결).
        /// </summary>
        [MenuItem("SpaceStation/Interior/Bake Sand Textures")]
        public static void BakeSand()
        {
            var h = new float[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float warp = (Noise(x, y, 3, 241) - 0.5f) * 2.4f + (Noise(x, y, 8, 243) - 0.5f) * 0.8f;
                    float ph = Mathf.Repeat(12f * y / N + warp, 1f);
                    float ripple = ph < 0.72f ? Smooth(0f, 0.72f, ph) : 1f - Smooth(0.72f, 1f, ph);   // 바람 쪽 완만 · 반대쪽 가파름
                    float fade = Mathf.Lerp(0.45f, 1f, Noise(x, y, 4, 247));                          // 물결이 옅은 곳 · 짙은 곳
                    float hh = 0.5f + 0.2f * ripple * fade;
                    hh += (Noise(x, y, 256, 251) - 0.5f) * 0.06f + (Noise(x, y, 512, 253) - 0.5f) * 0.045f;    // 잔 알갱이 결
                    hh += (Noise(x, y, 32, 257) - 0.5f) * 0.04f;
                    h[y * N + x] = hh;
                }
            }
            var blur = BoxBlur(h, 10);
            var normal = new Color32[N * N];
            var mask = new Color32[N * N];
            const float strength = 3.2f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = (H(h, x + 1, y) - H(h, x - 1, y)) * strength;
                    float dy = (H(h, x, y + 1) - H(h, x, y - 1)) * strength;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    normal[y * N + x] = new Color32(To8(n.x * 0.5f + 0.5f), To8(n.y * 0.5f + 0.5f), To8(n.z * 0.5f + 0.5f), 255);
                    float cavity = Mathf.Clamp01(1f - Mathf.Max(0f, blur[y * N + x] - h[y * N + x]) * 5f);
                    float ao = Mathf.Lerp(0.6f, 1f, cavity);
                    float tone = Mathf.Lerp(0.74f, 1f, Noise(x, y, 5, 261) * 0.5f + Noise(x, y, 20, 263) * 0.3f + Noise(x, y, 128, 265) * 0.2f);
                    mask[y * N + x] = new Color32(To8(ao), To8(tone), 255, 255);
                }
            }
            System.IO.Directory.CreateDirectory(Folder);
            Save(SandNormalPath, normal);
            Save(SandMaskPath, mask);
            AssetDatabase.Refresh();
            Configure(SandNormalPath, true);
            Configure(SandMaskPath, false);
            Debug.Log("[InteriorTextureBaker] 모래 텍스처 생성");
        }

        public const string HedgeAlbedoPath = Folder + "T_InteriorHedge_A.png";
        public const string HedgeNormalPath = Folder + "T_InteriorHedge_N.png";
        public const string HedgeMaskPath = Folder + "T_InteriorHedge_M.png";

        /// <summary>
        /// AI 중추 다듬은 생울타리 잎 (2026-11-16 다시 — 사용자 "풀 질감이 아쉽다, 더 풀인 느낌으로": 2 ~ 3 cm 잎 12,000장 한 색 = 몇 걸음만 떨어져도 매끈한 초록 벨벳).
        /// 한 장 = 1.67 m (타일링 0.6, 614 px/m) — 잔가지 1,500개 (가지마다 잎 5 ~ 8장 어긋나기, 끝으로 갈수록 작아지고 가지 방향으로 모임 · 끝잎 하나):
        /// 잎 = 길이 7 ~ 10 cm · 끝이 뾰족한 달걀꼴, 둥근 등 · 가운데 잎맥 (밝은 줄) · 곁맥 홈 · 잎마다 기울기 (빛을 다르게 받음).
        /// 색 = 잔가지마다 짙은 초록 ~ 초록 (안쪽 · 아래 가지일수록 짙게), 가지 끝 절반은 새순 연두, 잎 밑동 → 끝 밝아짐.
        /// 큰 잎 덩이 (약 30 cm, 노이즈 둘) = 덩이 꼭대기 밝게 · 덩이 사이 골 짙게 — 멀리서 보이는 결. 골은 이어진 그물이라 둥근 구멍 · 점 무늬 없음 (환공포증),
        /// 잎이 다 못 덮은 자리 (약 0.1 %)도 검은 점 대신 짙은 초록 안쪽 잎 높이.
        /// 결과 = 색 (sRGB, 셰이더 _DETAIL_ALBEDO) + 노멀 + 마스크 (R 잎 사이 · 덩이 골 그늘, G 잎마다 윤기).
        /// </summary>
        [MenuItem("SpaceStation/Interior/Bake Hedge Textures")]
        public static void BakeHedge()
        {
            var h = new float[N * N];
            var col = new Color[N * N];
            var gloss = new float[N * N];
            var clump = new float[N * N];
            for (int i = 0; i < h.Length; i++)
                h[i] = -999f;
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float v = Noise(x, y, 5, 31) * 0.6f + Noise(x, y, 11, 37) * 0.4f;
                    clump[y * N + x] = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - 0.22f) / 0.56f));
                }
            }
            // 잎 모양 = t^0.55 · (1 − t)^0.85 (t = 밑동 0 → 끝 1: 밑동 둥글고 끝 뾰족, 가장 넓은 곳 = 밑동 쪽 40 %) — 최댓값으로 나눠 폭 W에 맞춤
            float fmax = 0f;
            for (int q = 1; q < 100; q++)
                fmax = Mathf.Max(fmax, Mathf.Pow(q / 100f, 0.55f) * Mathf.Pow(1f - q / 100f, 0.85f));
            var deep = new Color(0.2f, 0.43f, 0.15f);
            var mid = new Color(0.36f, 0.64f, 0.21f);
            var fresh = new Color(0.64f, 0.84f, 0.27f);
            var rng = new System.Random(1116);
            const int sprigs = 1500;
            for (int k = 0; k < sprigs; k++)
            {
                float cx = (float)rng.NextDouble() * N, cy = (float)rng.NextDouble() * N;
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float depth = (float)rng.NextDouble();                 // 위 가지일수록 높고 밝음 (겹침 순서)
                float c0 = clump[((int)cy & (N - 1)) * N + ((int)cx & (N - 1))];
                float sprigH = 60f * c0 + 22f * depth;
                int n = 5 + rng.Next(4);
                float l0 = 52f * (0.85f + 0.3f * (float)rng.NextDouble());
                float twig = n * 0.34f * l0;
                var tw = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                var tint = Color.Lerp(deep, mid, Mathf.Clamp01((float)rng.NextDouble() * (0.35f + 0.65f * depth) + 0.5f * c0 - 0.1f));
                for (int i = 0; i < n; i++)
                {
                    float s = i / (float)(n - 1);
                    var p = new Vector2(cx, cy) + tw * (s - 0.5f) * twig;
                    float side = i % 2 == 0 ? 1f : -1f;
                    float la = i == n - 1 ? ang : ang + side * (0.65f + 0.35f * (float)rng.NextDouble()) * (1f - 0.5f * s);
                    float len = l0 * (1f - 0.32f * s) * (0.9f + 0.2f * (float)rng.NextDouble());
                    float wid = len * (0.5f + 0.1f * (float)rng.NextDouble());
                    float ca = Mathf.Cos(la), sa = Mathf.Sin(la);
                    var c = p + new Vector2(ca, sa) * len * 0.5f;
                    float hl = sprigH + 10f * s;
                    float tiltU = ((float)rng.NextDouble() - 0.3f) * 0.5f, tiltV = ((float)rng.NextDouble() - 0.5f) * 0.55f;
                    var lc = tint * (0.86f + 0.26f * (float)rng.NextDouble());
                    if (s > 0.6f && rng.NextDouble() < 0.5)
                        lc = Color.Lerp(lc, fresh, 0.5f + 0.35f * (float)rng.NextDouble());
                    float gl = 0.72f + 0.28f * (float)rng.NextDouble();
                    float hu = len * 0.5f;
                    int r = Mathf.CeilToInt(hu) + 2;
                    for (int dy = -r; dy <= r; dy++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            float fx = (int)c.x + dx - c.x, fy = (int)c.y + dy - c.y;
                            float u = (fx * ca + fy * sa) / hu, v = -fx * sa + fy * ca;
                            if (u <= -1f || u >= 1f)
                                continue;
                            float t = (u + 1f) * 0.5f;
                            float wv = wid * 0.5f * Mathf.Pow(t, 0.55f) * Mathf.Pow(1f - t, 0.85f) / fmax;
                            if (wv <= 0.3f)
                                continue;
                            float e = v / wv;
                            if (e <= -1f || e >= 1f)
                                continue;
                            float dome = 0.32f * wv * Mathf.Sqrt(1f - e * e);
                            float rib = 0.8f * Mathf.Exp(-e * e * 70f) * (1f - u * u);
                            float vein = 0.25f * Mathf.Exp(-Mathf.Pow(Mathf.Repeat(u * 3.2f - Mathf.Abs(e) * 1.4f, 1f) - 0.5f, 2f) * 90f) * (1f - Mathf.Abs(e));
                            float hh = hl + dome - rib - vein + tiltU * u * hu + tiltV * v;
                            int idx = (((int)c.y + dy) & (N - 1)) * N + (((int)c.x + dx) & (N - 1));
                            if (hh <= h[idx])
                                continue;
                            h[idx] = hh;
                            var cc = lc * ((0.84f + 0.22f * t) * (Mathf.Abs(e) > 0.84f ? 0.88f : 1f));      // 밑동 → 끝 밝게 · 잎 가장자리 살짝 짙게
                            col[idx] = Color.Lerp(cc, cc * 1.15f + new Color(0.03f, 0.03f, 0f), Mathf.Exp(-e * e * 90f) * (1f - u * u) * 0.85f);   // 가운데 잎맥 밝은 줄
                            gloss[idx] = gl;
                        }
                    }
                }
            }
            for (int i = 0; i < h.Length; i++)
            {
                if (h[i] < -900f)
                {
                    h[i] = 60f * clump[i] - 8f;                         // 덮이지 않은 자리 = 짙은 안쪽 잎 (검은 점 아님)
                    col[i] = deep * 0.72f;
                    gloss[i] = 0.6f;
                }
            }
            var wide = BoxBlur(h, 26);
            var near = BoxBlur(h, 4);
            var albedo = new Color32[N * N];
            var normal = new Color32[N * N];
            var mask = new Color32[N * N];
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    float dx = Mathf.Clamp(H(h, x + 1, y) - H(h, x - 1, y), -3f, 3f) * 0.5f;   // 겹친 잎 턱 = 가는 테두리 (기울기 상한)
                    float dy = Mathf.Clamp(H(h, x, y + 1) - H(h, x, y - 1), -3f, 3f) * 0.5f;
                    var n = new Vector3(-dx, -dy, 1f).normalized;
                    normal[i] = new Color32(To8(n.x * 0.5f + 0.5f), To8(n.y * 0.5f + 0.5f), To8(n.z * 0.5f + 0.5f), 255);
                    float occ = Mathf.Clamp01((wide[i] - h[i]) / 30f);      // 덩이 사이 골 · 아래 가지
                    float occNear = Mathf.Clamp01((near[i] - h[i]) / 6f);   // 잎 바로 밑
                    float ao = Mathf.Lerp(1f, 0.58f, occ) * Mathf.Lerp(1f, 0.8f, occNear);
                    // 전체 × 1.28 (처음 굽기는 평균 선형 밝기 0.25 = 예전 한 색 재질 0.44의 절반 — 밤빛 아래에서 너무 어두웠음, 2026-11-16 Unity 확인), 새순 잎맥 끝만 조금 넘침
                    var a = col[i] * Mathf.Lerp(0.76f, 1.06f, clump[i]) * Mathf.Lerp(1f, 0.68f, occ) * 1.28f;
                    albedo[i] = new Color32(To8(a.r), To8(a.g), To8(a.b), 255);
                    mask[i] = new Color32(To8(ao), To8(gloss[i]), 255, 255);
                }
            }
            System.IO.Directory.CreateDirectory(Folder);
            Save(HedgeAlbedoPath, albedo);
            Save(HedgeNormalPath, normal);
            Save(HedgeMaskPath, mask);
            AssetDatabase.Refresh();
            Configure(HedgeAlbedoPath, false, true);
            Configure(HedgeNormalPath, true);
            Configure(HedgeMaskPath, false);
            Debug.Log("[InteriorTextureBaker] 생울타리 잎 텍스처 생성");
        }

        /// <summary>이어지는 보로노이 (가로 cx · 세로 cy칸, 칸 공간 거리): F2 − F1 (판 가장자리까지, 칸 단위) · 가장 가까운 점의 값 (판마다 다른 0~1).</summary>
        private static float Edge(float x, float y, int cx, int cy, int seed, out float id)
        {
            float fx = x * cx / N, fy = y * cy / N;
            int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
            float f1 = 9f, f2 = 9f;
            id = 0f;
            for (int j = -1; j <= 1; j++)
            {
                for (int i = -1; i <= 1; i++)
                {
                    int gx = ix + i, gy = iy + j;
                    int wx = ((gx % cx) + cx) % cx, wy = ((gy % cy) + cy) % cy;
                    float px = gx + 0.15f + 0.7f * Hash(wx, wy, seed), py = gy + 0.15f + 0.7f * Hash(wx, wy, seed + 7);
                    float d = Mathf.Sqrt((px - fx) * (px - fx) + (py - fy) * (py - fy));
                    if (d < f1)
                    {
                        f2 = f1;
                        f1 = d;
                        id = Hash(wx, wy, seed + 13);
                    }
                    else if (d < f2)
                        f2 = d;
                }
            }
            return f2 - f1;
        }

        private static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        private static float H(float[] h, int x, int y) => h[((y % N + N) % N) * N + (x % N + N) % N];

        /// <summary>높이 (대략 0~1). 패널 가장자리 홈 → 안쪽 판 → 패널별 무늬 → 잔잔한 굴곡.</summary>
        private static float Height(int x, int y)
        {
            int px = x / Panel, py = y / Panel;
            int lx = x % Panel, ly = y % Panel;
            float edge = Mathf.Min(Mathf.Min(lx, Panel - 1 - lx), Mathf.Min(ly, Panel - 1 - ly));
            float h = 0.5f;
            // 이음 홈 (가장자리 5px)
            h -= 0.6f * (1f - Smooth(0f, 5f, edge));
            // 안쪽 판: 가장자리에서 36px 안쪽으로 살짝 높은 판 (4px 경사)
            h += 0.12f * Smooth(34f, 38f, edge);
            // 판 둘레 가는 홈 (안쪽 판 경계를 따라)
            h -= 0.15f * Bump(Mathf.Abs(edge - 36f), 2f);

            int type = (px + py * 2) % 4;
            float cx = lx - Panel * 0.5f, cy = ly - Panel * 0.5f;
            switch (type)
            {
                case 0: // 모서리 볼트 4개
                    foreach (var (bx, by) in new[] { (20, 20), (Panel - 20, 20), (20, Panel - 20), (Panel - 20, Panel - 20) })
                    {
                        float d = Mathf.Sqrt((lx - bx) * (lx - bx) + (ly - by) * (ly - by));
                        h += 0.25f * Dome(d, 8f) - 0.1f * Bump(Mathf.Abs(d - 9f), 1.5f);
                    }
                    break;
                case 1: // 가로 리브 6줄
                    if (edge > 50f)
                    {
                        float period = (Panel - 100f) / 6f;
                        float t = Mathf.Repeat(ly - 50f, period) - period * 0.5f;
                        h += 0.12f * Dome(Mathf.Abs(t), 10f);
                    }
                    break;
                case 2: // 환기 슬롯 5개
                    if (Mathf.Abs(cx) < 150f)
                    {
                        for (int k = -2; k <= 2; k++)
                        {
                            float sy = Mathf.Abs(cy - k * 48f);
                            float sx = Mathf.Abs(cx);
                            float slot = (1f - Smooth(5f, 8f, sy)) * (1f - Smooth(140f, 150f, sx));
                            h -= 0.35f * slot;
                        }
                    }
                    break;
                default: // 미끄럼 방지 점
                    if (edge > 50f)
                    {
                        float gx = Mathf.Repeat(lx, 32f) - 16f, gy = Mathf.Repeat(ly, 32f) - 16f;
                        h += 0.08f * Dome(Mathf.Sqrt(gx * gx + gy * gy), 5f);
                    }
                    break;
            }
            // 잔잔한 굴곡 (타일 주기에 맞춘 노이즈)
            h += (Noise(x, y, 16, 5) - 0.5f) * 0.03f + (Noise(x, y, 64, 9) - 0.5f) * 0.015f;
            return h;
        }

        private static float Smooth(float a, float b, float v)
        {
            float t = Mathf.Clamp01((v - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static float Bump(float d, float r) => 1f - Smooth(0f, r, d);

        private static float Dome(float d, float r) => d >= r ? 0f : Mathf.Sqrt(1f - (d / r) * (d / r));

        /// <summary>주기 = N / cells 로 이어지는 값 노이즈 (0~1).</summary>
        private static float Noise(int x, int y, int cells, int seed)
        {
            float fx = x * cells / (float)N, fy = y * cells / (float)N;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float a = Hash(x0 % cells, y0 % cells, seed), b = Hash((x0 + 1) % cells, y0 % cells, seed);
            float c = Hash(x0 % cells, (y0 + 1) % cells, seed), d = Hash((x0 + 1) % cells, (y0 + 1) % cells, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        private static float Hash(int x, int y, int seed)
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }

        private static float[] BoxBlur(float[] src, int r)
        {
            var tmp = new float[N * N];
            var dst = new float[N * N];
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++)
                        s += H(src, x + k, y);
                    tmp[y * N + x] = s * inv;
                }
            }
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++)
                        s += H(tmp, x, y + k);
                    dst[y * N + x] = s * inv;
                }
            }
            return dst;
        }

        private static void Save(string path, Color32[] pixels)
        {
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(pixels);
            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        /// <param name="srgb">색 텍스처 (생울타리 잎 색)만 true — 노멀 · 마스크는 선형 값</param>
        private static void Configure(string path, bool normalMap, bool srgb = false)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
    }
}
