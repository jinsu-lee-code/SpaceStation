using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 식물 아틀라스 (2026-11-16, 사용자 "트림 시트처럼 텍스처 한 장으로 — 우리 식물까지 합쳐서"): 게임의 모든 식물이 텍스처 한 벌 · 재질 하나 (`M_Foliage`, 셰이더 SpaceStation/Foliage)를 씀.
    /// 바탕 4096 (알파 = 잘라내기) · 노멀 · 마스크 2048 (R 윤기 · G 차폐 · B 두께). 칸 (왼쪽 아래 원점, 바탕 px):
    ///   샘플 잎 · 꽃 (Unity URP 3D Sample Garden, 2048²) — 왼쪽 위 / 벚꽃 가지 (1024²) — 가운데 위 / 우리 기하학 식물 색 칸 (8 × 8, 128 px) — 가운데 /
    ///   우리 그림 (팔레트로 바꾼 꽃 · 생울타리 잔가지 등, 1024 × 2048 — 지금은 왼쪽 아래 금빛 등나무 꽃송이뿐) — 오른쪽 위 / 줄기 트림 띠 (가로 끝까지 · 길이 방향 반복, 띠 256 = 둘레 두 바퀴) — 아래.
    /// 톤 = 우리 잎 색 + 조금 만화풍 (사용자 고름 — 밝게 × 1.12 · 초록을 새순 연두 쪽으로 · 채도 × 1.15 · 잎 안쪽 명암 대비 55 % · 밝기 단계 살짝).
    /// 원본 = 저장소 `ArtSource/Plants/URPSample` (Unity가 읽지 않음, Unity Companion License). 메시 UV는 가져올 때 `PlantMeshImporter`가 이 칸으로 옮김.
    /// </summary>
    public static class PlantAtlas
    {
        public const string Folder = "Assets/_Project/Art/Plants/";
        public const string AlbedoPath = Folder + "T_PlantAtlas_A.png";
        public const string NormalPath = Folder + "T_PlantAtlas_N.png";
        public const string MaskPath = Folder + "T_PlantAtlas_M.png";
        public const string MaterialPath = Folder + "M_Foliage.mat";
        /// <summary>살랑임을 끈 같은 재질 (④ 내부 잎 조각 — 조각 원점이 방 원점이라 흔들면 잎이 통째로 움직여 줄기에서 떨어져 보임, InteriorFoliage).</summary>
        public const string StillMaterialPath = Folder + "M_FoliageStill.mat";
        public const string MeshFolder = Folder + "Meshes/";
        public const string ShaderName = "SpaceStation/Foliage";

        public const int Size = 4096;
        public static readonly RectInt Garden = new RectInt(0, 2048, 2048, 2048);
        public static readonly RectInt Cherry = new RectInt(2048, 3072, 1024, 1024);
        public static readonly RectInt Swatches = new RectInt(2048, 2048, 1024, 1024);
        public static readonly RectInt Ours = new RectInt(3072, 2048, 1024, 2048);
        /// <summary>
        /// 금빛 등나무 꽃송이 (2026-11-16 AI 중추 뒤 반구 — 길 찾기 색 앞 보라 · 뒤 금빛): 샘플 잎 · 꽃 텍스처의 빽빽한 꽃송이 칸 (원본 px, <see cref="RacemeSrc"/>)을
        /// 톤을 바꾼 뒤 보라를 밝은 금빛으로 바꿔 '우리 그림' 칸 왼쪽 아래에 같은 크기로 (<see cref="GoldRaceme"/>). 재질 이름에 "wisteriagold"가 든 면이 이 칸을 씀.
        /// </summary>
        public static readonly RectInt RacemeSrc = new RectInt(1252, 488, 108, 144);
        public static readonly RectInt GoldRaceme = new RectInt(3072, 2048, 108, 144);
        /// <summary>보라 등나무 꽃송이 (빽빽한 · 긴 꽃송이) 칸 — 원본 px, 톤 뒤 보라만 밝게.</summary>
        public static readonly RectInt RacemeLift = new RectInt(1228, 389, 308, 370);
        public const int StripHeight = 256;
        public const int BarkTile = 128;
        public const int SwatchCell = 128;

        /// <summary>줄기 트림 띠 (아래부터 0, 1, 2 …).</summary>
        public enum Bark { Wisteria = 0, Bamboo = 1, CherryRough = 2 }

        /// <summary>우리 기하학 식물 색 칸 (Blender 재질 이름 → 재질 경로) — 순서 = 칸 번호 (왼쪽 아래부터 가로로). ④에서 잎 · 꽃잎 UV를 이 칸 가운데로.</summary>
        public static readonly (string Name, string Path)[] SwatchMaterials =
        {
            ("Plant", InteriorSetup.PlantPath), ("PlantLight", InteriorSetup.PlantLightPath), ("Evo3Leaf2", InteriorSetup.Evo3Leaf2Path),
            ("Evo3Flower", InteriorSetup.Evo3FlowerPath), ("Evo3Blossom", InteriorSetup.Evo3BlossomPath), ("Evo3Lilac", InteriorSetup.Evo3LilacPath),
            ("Evo3Lavender", InteriorSetup.Evo3LavenderPath), ("Evo3Coral", InteriorSetup.Evo3CoralPath), ("Evo3Nacre", InteriorSetup.Evo3NacrePath),
            ("Evo3Gold", InteriorSetup.Evo3GoldPath), ("EvoWhite", InteriorSetup.EvoWhitePath), ("PropYellow", InteriorSetup.PropYellowPath),
            ("PropOrange", InteriorSetup.PropOrangePath), ("PropPurple", InteriorSetup.PropPurplePath), ("Evo3Mint", InteriorSetup.Evo3MintPath),
            // 우리 식물 모델 (plant_builder SM_Plant* — FBX 안 재질 색 그대로, ④ 2026-11-16): 가져올 때 PlantMeshImporter가 재질 "M_{이름}" → 이 칸
            ("PlantStem", OurSwatchFolder + "M_PlantStem.mat"), ("PlantLeafDark", OurSwatchFolder + "M_PlantLeafDark.mat"), ("PlantLeaf", OurSwatchFolder + "M_PlantLeaf.mat"),
            ("PlantLeafLight", OurSwatchFolder + "M_PlantLeafLight.mat"), ("PlantDry", OurSwatchFolder + "M_PlantDry.mat"), ("PlantDryDark", OurSwatchFolder + "M_PlantDryDark.mat"),
            ("PlantBark", OurSwatchFolder + "M_PlantBark.mat"), ("PlantNeedle", OurSwatchFolder + "M_PlantNeedle.mat"), ("PlantNeedleLight", OurSwatchFolder + "M_PlantNeedleLight.mat"),
            ("PlantBlossom", OurSwatchFolder + "M_PlantBlossom.mat"), ("PlantBlossomLight", OurSwatchFolder + "M_PlantBlossomLight.mat"), ("PlantPistil", OurSwatchFolder + "M_PlantPistil.mat"),
            // 심우주 통신기지 (ds4_builder, 2026-11-17): 은빛 풀 · 달맞이꽃 · 줄기 · 꽃가루 (끝에 더함 — 앞 칸 번호 그대로)
            ("Evo4SilverGrass", InteriorSetup.Evo4SilverGrassPath), ("Evo4Moonflower", InteriorSetup.Evo4MoonflowerPath), ("Evo4Stem", InteriorSetup.Evo4StemPath),
            ("Evo4Pollen", InteriorSetup.Evo4PollenPath),
        };

        /// <summary>우리 식물 모델 FBX 폴더 (plant_builder 내보내기 — 화분 식물 · 나무 · 소나무 · 꽃나무와 시든 것).</summary>
        public const string OursFolder = "Assets/_Project/Art/Models/Interior/Plants/";
        /// <summary>우리 식물 색 칸의 색 재질 (FBX 안 재질을 꺼낸 것 — 아틀라스 굽기가 색 · 윤기를 읽음, 다른 데는 안 씀).</summary>
        public const string OurSwatchFolder = Folder + "Swatches/";

        /// <summary>색 칸 가운데 UV (0 ~ 1). 없는 이름이면 false.</summary>
        public static bool SwatchUV(string material, out Vector2 uv)
        {
            for (int i = 0; i < SwatchMaterials.Length; i++)
            {
                if (SwatchMaterials[i].Name != material)
                    continue;
                int per = Swatches.width / SwatchCell;
                uv = new Vector2(Swatches.x + (i % per + 0.5f) * SwatchCell, Swatches.y + (i / per + 0.5f) * SwatchCell) / Size;
                return true;
            }
            uv = default;
            return false;
        }

        /// <summary>
        /// 샘플 메시 UV → 아틀라스 UV (FBX 재질 이름으로 칸을 고름): 잎 · 꽃 = 칸 안으로 줄여 옮김, 줄기 = 트림 띠 (원래 v (길이) → 가로 반복, u (둘레 −0.5 ~ 1.5) → 띠 높이).
        /// </summary>
        public static Vector2 ToAtlas(string material, Vector2 uv)
        {
            string m = material.ToLowerInvariant();
            if (m.Contains("bark"))
            {
                var b = m.Contains("bamboo") ? Bark.Bamboo : m.Contains("cherry") ? Bark.CherryRough : Bark.Wisteria;
                return new Vector2(uv.y * BarkTile / Size, ((int)b * StripHeight + (uv.x + 0.5f) * BarkTile) / Size);
            }
            if (m.Contains("wisteriagold"))
                return new Vector2(GoldRaceme.x + Mathf.Clamp(uv.x * Garden.width - RacemeSrc.x, 0f, RacemeSrc.width),
                    GoldRaceme.y + Mathf.Clamp(uv.y * Garden.height - RacemeSrc.y, 0f, RacemeSrc.height)) / Size;
            var r = m.Contains("cherry") ? Cherry : Garden;
            return new Vector2(r.x + uv.x * r.width, r.y + uv.y * r.height) / Size;
        }

        private static string SourceFolder => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../ArtSource/Plants/URPSample/Textures/"));

        private sealed class Img
        {
            public int W, H;
            public Color32[] Px;
            public Color32 At(int x, int y) => Px[Mathf.Clamp(y, 0, H - 1) * W + Mathf.Clamp(x, 0, W - 1)];
            public Color32 Wrap(float u, float v)
            {
                int x = ((int)Mathf.Floor(u * W) % W + W) % W, y = ((int)Mathf.Floor(v * H) % H + H) % H;
                return Px[y * W + x];
            }
        }

        private static Img Load(string file)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            t.LoadImage(System.IO.File.ReadAllBytes(SourceFolder + file));
            var img = new Img { W = t.width, H = t.height, Px = t.GetPixels32() };
            Object.DestroyImmediate(t);
            return img;
        }

        private static float[] Blur(float[] s, int w, int h, int r)
        {
            var tmp = new float[w * h];
            var d = new float[w * h];
            for (int y = 0; y < h; y++)
            {
                float a = 0;
                for (int k = -r; k <= r; k++)
                    a += s[y * w + Mathf.Clamp(k, 0, w - 1)];
                for (int x = 0; x < w; x++)
                {
                    tmp[y * w + x] = a;
                    a += s[y * w + Mathf.Min(x + r + 1, w - 1)] - s[y * w + Mathf.Max(x - r, 0)];
                }
            }
            for (int x = 0; x < w; x++)
            {
                float a = 0;
                for (int k = -r; k <= r; k++)
                    a += tmp[Mathf.Clamp(k, 0, h - 1) * w + x];
                for (int y = 0; y < h; y++)
                {
                    d[y * w + x] = a;
                    a += tmp[Mathf.Min(y + r + 1, h - 1) * w + x] - tmp[Mathf.Max(y - r, 0) * w + x];
                }
            }
            return d;
        }

        /// <summary>톤: 잎 안쪽 명암 대비 55 % (조각마다 둘레 평균 밝기 쪽으로) · 밝기 6단계를 30 % 섞음 · 밝게 · 초록 → 새순 연두 쪽 · 채도 × 1.15.</summary>
        private static Color32[] Tone(Img img)
        {
            int w = img.W, h = img.H, n = w * h;
            var lum = new float[n];
            var cov = new float[n];
            for (int i = 0; i < n; i++)
            {
                var c = img.Px[i];
                float a = c.a > 127 ? 1f : 0f;
                cov[i] = a;
                lum[i] = a * (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
            }
            var bl = Blur(lum, w, h, 6);
            var ba = Blur(cov, w, h, 6);
            var o = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                var c = img.Px[i];
                if (c.a < 8)
                {
                    o[i] = c;
                    continue;
                }
                Color col = c;
                float l = 0.2126f * col.r + 0.7152f * col.g + 0.0722f * col.b;
                float lm = ba[i] > 0.01f ? bl[i] / ba[i] : l;
                float lt = lm + (l - lm) * 0.55f;
                lt = Mathf.Lerp(lt, Mathf.Round(lt * 6f) / 6f, 0.3f);
                lt = Mathf.Clamp01(lt * 1.12f + 0.035f);
                Color.RGBToHSV(col, out float hh, out float ss, out float vv);
                if (hh > 0.15f && hh < 0.47f)
                    hh += (0.265f - hh) * 0.2f;
                ss = Mathf.Clamp01(ss * 1.15f);
                var nc = Color.HSVToRGB(hh, ss, Mathf.Max(vv, 0.001f));
                float nl = 0.2126f * nc.r + 0.7152f * nc.g + 0.0722f * nc.b;
                nc *= nl > 0.001f ? lt / nl : 1f;
                o[i] = new Color32(To8(nc.r), To8(nc.g), To8(nc.b), c.a);
            }
            return o;
        }

        private static byte To8(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        private static void Blit(Color32[] dst, int dw, Color32[] src, int sw, int sh, int x0, int y0)
        {
            for (int y = 0; y < sh; y++)
                System.Array.Copy(src, y * sw, dst, (y0 + y) * dw + x0, sw);
        }

        /// <summary>절반 크기로 (2 × 2 평균).</summary>
        private static Img Half(Img s)
        {
            var o = new Img { W = s.W / 2, H = s.H / 2 };
            o.Px = new Color32[o.W * o.H];
            for (int y = 0; y < o.H; y++)
            {
                for (int x = 0; x < o.W; x++)
                {
                    int r = 0, g = 0, b = 0, a = 0;
                    for (int j = 0; j < 2; j++)
                    {
                        for (int k = 0; k < 2; k++)
                        {
                            var c = s.Px[(y * 2 + j) * s.W + x * 2 + k];
                            r += c.r; g += c.g; b += c.b; a += c.a;
                        }
                    }
                    o.Px[y * o.W + x] = new Color32((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), (byte)(a / 4));
                }
            }
            return o;
        }

        /// <summary>
        /// 투명 자리 색 채우기 (밀고 당기기): 알파가 없는 자리를 둘레 잎 색으로 — 그대로 두면 검은 바탕이 밉맵에서 잎 가장자리에 검은 테로 섞임.
        /// </summary>
        private static void Dilate(Color32[] px, int w, int h)
        {
            var levels = new List<(Color[] c, int w, int h)>();
            var c0 = new Color[w * h];
            for (int i = 0; i < c0.Length; i++)
            {
                var p = px[i];
                float a = p.a > 8 ? 1f : 0f;
                c0[i] = new Color(p.r / 255f * a, p.g / 255f * a, p.b / 255f * a, a);
            }
            levels.Add((c0, w, h));
            while (levels[levels.Count - 1].w > 1)
            {
                var (pc, pw, ph) = levels[levels.Count - 1];
                int nw = pw / 2, nh = ph / 2;
                var nc = new Color[nw * nh];
                for (int y = 0; y < nh; y++)
                    for (int x = 0; x < nw; x++)
                        nc[y * nw + x] = pc[(2 * y) * pw + 2 * x] + pc[(2 * y) * pw + 2 * x + 1] + pc[(2 * y + 1) * pw + 2 * x] + pc[(2 * y + 1) * pw + 2 * x + 1];
                levels.Add((nc, nw, nh));
            }
            for (int l = levels.Count - 2; l >= 0; l--)
            {
                var (cc, cw, ch) = levels[l];
                var (up, uw, _) = levels[l + 1];
                for (int y = 0; y < ch; y++)
                {
                    for (int x = 0; x < cw; x++)
                    {
                        var c = cc[y * cw + x];
                        if (c.a > 0.999f)
                            continue;
                        var u = up[Mathf.Min(y / 2, uw - 1) * uw + Mathf.Min(x / 2, uw - 1)];
                        var un = u.a > 1e-5f ? u / u.a : u;
                        cc[y * cw + x] = c + un * (1f - Mathf.Min(1f, c.a));
                    }
                }
            }
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a > 8)
                    continue;
                var c = c0[i];
                var n = c.a > 1e-5f ? c / c.a : c;
                px[i] = new Color32(To8(n.r), To8(n.g), To8(n.b), px[i].a);
            }
        }

        [MenuItem("SpaceStation/Plants/Bake Plant Atlas")]
        public static void Bake()
        {
            const int S = Size, M = Size / 2;
            var albedo = new Color32[S * S];
            var normal = new Color32[M * M];
            var mask = new Color32[M * M];
            for (int i = 0; i < normal.Length; i++)
            {
                normal[i] = new Color32(128, 128, 255, 255);
                mask[i] = new Color32(80, 255, 0, 255);
            }

            // 샘플 잎 · 꽃 (바탕 1:1, 노멀 · 마스크는 원본이 1024라 1:1로 절반 칸에)
            var gA = Load("GardenVegetation_T_A.png");
            Blit(albedo, S, Tone(gA), gA.W, gA.H, Garden.x, Garden.y);
            var gN = Load("GardenVegetation_T_N.png");
            Blit(normal, M, gN.Px, gN.W, gN.H, Garden.x / 2, Garden.y / 2);
            FillMask(mask, M, Load("GardenVegetation_T_Mask.png"), Load("GardenVegetation_T_SS.png"), Garden.x / 2, Garden.y / 2);

            // 벚꽃 가지 (바탕 1:1, 노멀 · 마스크 절반)
            var cA = Load("CherryTree_01_T_A.png");
            Blit(albedo, S, Tone(cA), cA.W, cA.H, Cherry.x, Cherry.y);
            var cN = Half(Load("CherryTree_01_T_N.png"));
            Blit(normal, M, cN.Px, cN.W, cN.H, Cherry.x / 2, Cherry.y / 2);
            FillMask(mask, M, Half(Load("CherryTree_01_T_Mask.png")), Half(Load("CherryTree_01_T_SS.png")), Cherry.x / 2, Cherry.y / 2);

            // 등나무 꽃송이 밝게 (빽빽한 · 긴 꽃송이 칸의 보라만 — 밤빛 미로에서 잎에 묻혔음, 사용자 "좀 더 잘 보이게")
            for (int y = RacemeLift.yMin; y < RacemeLift.yMax; y++)
            {
                for (int x = RacemeLift.xMin; x < RacemeLift.xMax; x++)
                {
                    int i = (Garden.y + y) * S + Garden.x + x;
                    var c = albedo[i];
                    if (c.a <= 8)
                        continue;
                    Color.RGBToHSV(c, out float hh, out float ss, out float vv);
                    if (hh > 0.6f && hh < 0.95f && ss > 0.15f)
                    {
                        var nc = Color.HSVToRGB(hh, ss * 0.95f, Mathf.Clamp01(vv * 1.22f + 0.06f));
                        albedo[i] = new Color32(To8(nc.r), To8(nc.g), To8(nc.b), c.a);
                    }
                }
            }

            // 금빛 등나무 꽃송이 (보라 → 밝은 금빛 · 노란 무늬 · 줄기는 그대로, 노멀 · 마스크는 같은 칸 복사)
            for (int y = 0; y < RacemeSrc.height; y++)
            {
                for (int x = 0; x < RacemeSrc.width; x++)
                {
                    var c = albedo[(Garden.y + RacemeSrc.y + y) * S + Garden.x + RacemeSrc.x + x];
                    if (c.a > 8)
                    {
                        Color.RGBToHSV(c, out float hh, out float ss, out float vv);
                        if (hh > 0.55f && hh < 0.95f)
                        {
                            var nc = Color.HSVToRGB(0.125f, 0.42f + 0.25f * ss, Mathf.Clamp01(0.68f + 0.4f * vv));   // 밝은 금빛 (밤빛에서 크림은 누런 갈색으로 묻혔음)
                            c = new Color32(To8(nc.r), To8(nc.g), To8(nc.b), c.a);
                        }
                    }
                    albedo[(GoldRaceme.y + y) * S + GoldRaceme.x + x] = c;
                }
            }
            for (int y = 0; y < RacemeSrc.height / 2; y++)
            {
                for (int x = 0; x < RacemeSrc.width / 2; x++)
                {
                    int src = ((Garden.y + RacemeSrc.y) / 2 + y) * M + (Garden.x + RacemeSrc.x) / 2 + x, dst = (GoldRaceme.y / 2 + y) * M + GoldRaceme.x / 2 + x;
                    normal[dst] = normal[src];
                    mask[dst] = mask[src];
                }
            }

            // 우리 기하학 식물 색 칸 (재질 색 그대로 · 윤기 = 재질 윤기 · 두께 0.5)
            int per = Swatches.width / SwatchCell;
            for (int k = 0; k < SwatchMaterials.Length; k++)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(SwatchMaterials[k].Path);
                Color col = mat != null && mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.magenta;
                float sm = mat != null && mat.HasProperty("_Smoothness") ? mat.GetFloat("_Smoothness") : 0.3f;
                int x0 = Swatches.x + (k % per) * SwatchCell, y0 = Swatches.y + (k / per) * SwatchCell;
                var c32 = new Color32(To8(col.r), To8(col.g), To8(col.b), 255);
                for (int y = 0; y < SwatchCell; y++)
                    for (int x = 0; x < SwatchCell; x++)
                        albedo[(y0 + y) * S + x0 + x] = c32;
                for (int y = 0; y < SwatchCell / 2; y++)
                    for (int x = 0; x < SwatchCell / 2; x++)
                        mask[(y0 / 2 + y) * M + x0 / 2 + x] = new Color32(To8(sm), 255, 128, 255);
            }

            // 줄기 트림 띠: 원래 v (길이) → 가로 (128 px마다 반복, 4096 = 32번), 원래 u (둘레) → 띠 높이 (128 px = 한 바퀴, −0.5 ~ 1.5)
            Strip(albedo, S, normal, mask, M, Bark.Wisteria, Load("WisteriaBark_T_A.png"), null);
            Strip(albedo, S, normal, mask, M, Bark.Bamboo, Load("BambooBark_T_A.png"), Load("BambooBark_T_N.png"));
            Strip(albedo, S, normal, mask, M, Bark.CherryRough, Load("CherryTreeBarkRough_T_A.png"), Load("CherryTreeBarkRough_T_N.png"));

            Dilate(albedo, S, S);
            System.IO.Directory.CreateDirectory(Folder);
            Save(AlbedoPath, albedo, S);
            Save(NormalPath, normal, M);
            Save(MaskPath, mask, M);
            AssetDatabase.Refresh();
            Configure(AlbedoPath, TextureImporterType.Default, true, S);
            Configure(NormalPath, TextureImporterType.NormalMap, false, M);
            Configure(MaskPath, TextureImporterType.Default, false, M);
            Debug.Log("[PlantAtlas] 식물 아틀라스 구움");
        }

        private static void FillMask(Color32[] mask, int mw, Img m, Img ss, int x0, int y0)
        {
            for (int y = 0; y < m.H; y++)
            {
                for (int x = 0; x < m.W; x++)
                {
                    var a = m.Px[y * m.W + x];
                    var t = ss.At(x * ss.W / m.W, y * ss.H / m.H);
                    byte thick = To8((0.2126f * t.r + 0.7152f * t.g + 0.0722f * t.b) / 255f * 1.4f);
                    mask[(y0 + y) * mw + x0 + x] = new Color32(a.r, a.g, thick, 255);
                }
            }
        }

        private static void Strip(Color32[] albedo, int s, Color32[] normal, Color32[] mask, int m, Bark bark, Img a, Img n)
        {
            int y0 = (int)bark * StripHeight;
            for (int y = 0; y < StripHeight; y++)
            {
                float u = (float)y / BarkTile - 0.5f;
                for (int x = 0; x < s; x++)
                {
                    float v = (float)x / BarkTile;
                    var c = a.Wrap(u, v);
                    Color col = c;
                    col = col * 1.08f + new Color(0.02f, 0.015f, 0.01f);
                    albedo[(y0 + y) * s + x] = new Color32(To8(col.r), To8(col.g), To8(col.b), 255);
                }
            }
            for (int y = 0; y < StripHeight / 2; y++)
            {
                float u = (float)y / (BarkTile / 2) - 0.5f;
                for (int x = 0; x < m; x++)
                {
                    float v = (float)x / (BarkTile / 2);
                    Color32 nn = n != null ? n.Wrap(u, v) : new Color32(128, 128, 255, 255);
                    normal[(y0 / 2 + y) * m + x] = new Color32(nn.g, nn.r, nn.b, 255);     // 축을 바꿔 붙였으니 기울기도 바꿈 (가로 = 원래 길이 방향)
                    mask[(y0 / 2 + y) * m + x] = new Color32(40, 230, 0, 255);
                }
            }
        }

        private static void Save(string path, Color32[] px, int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            t.SetPixels32(px);
            t.Apply();
            System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
        }

        /// <summary>압축 설정: 바탕 = BC7 (알파 잘라내기 · 밉맵 덮음 유지) · 노멀 = BC5 · 마스크 = 선형 BC7, 가로 반복 (줄기 띠).</summary>
        private static void Configure(string path, TextureImporterType type, bool srgb, int max)
        {
            var im = (TextureImporter)AssetImporter.GetAtPath(path);
            im.textureType = type;
            im.sRGBTexture = srgb;
            im.alphaSource = TextureImporterAlphaSource.FromInput;
            im.alphaIsTransparency = false;
            im.mipmapEnabled = true;
            im.mipMapsPreserveCoverage = type == TextureImporterType.Default && srgb;
            im.alphaTestReferenceValue = 0.45f;
            im.wrapMode = TextureWrapMode.Repeat;
            im.filterMode = FilterMode.Trilinear;
            im.anisoLevel = 4;
            im.maxTextureSize = max;
            im.textureCompression = TextureImporterCompression.CompressedHQ;
            im.SaveAndReimport();
        }

        /// <summary>재질 `M_Foliage` (셰이더 SpaceStation/Foliage + 아틀라스) — 없으면 아틀라스부터 굽고, 끝나면 식물 메시를 다시 가져와 UV · 재질을 붙임.</summary>
        [MenuItem("SpaceStation/Plants/Setup")]
        public static void Setup()
        {
            if (!System.IO.File.Exists(AlbedoPath) || !System.IO.File.Exists(NormalPath) || !System.IO.File.Exists(MaskPath))
                Bake();
            var shader = Shader.Find(ShaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
            mat.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
            mat.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath));
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            var still = AssetDatabase.LoadAssetAtPath<Material>(StillMaterialPath);
            if (still == null)
            {
                still = new Material(mat);
                AssetDatabase.CreateAsset(still, StillMaterialPath);
            }
            still.CopyPropertiesFromMaterial(mat);
            still.SetFloat("_WindStrength", 0f);
            still.enableInstancing = true;
            EditorUtility.SetDirty(still);
            AssetDatabase.SaveAssets();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { MeshFolder.TrimEnd('/') }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            Debug.Log("[PlantAtlas] 식물 재질 · 메시 준비");
        }
    }
}
