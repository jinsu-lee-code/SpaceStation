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

        private static void Configure(string path, bool normalMap)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
        }
    }
}
