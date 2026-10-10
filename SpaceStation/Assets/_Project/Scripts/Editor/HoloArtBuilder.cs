using System;
using System.IO;
using SpaceStation.UI;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 11-13 홀로그램 UI 테마 아트 (메뉴 SpaceStation/UI/Build Holo Theme Art). 모두 코드로 그려 외부 에셋이 필요 없다.
    /// 빛 번짐(9-slice) · 주사선 · 격자(반복) · 스캔 띠를 만들고, 5-8 패널 · 버튼 그림과 함께 <see cref="HoloArt"/> 에셋에 묶는다.
    /// </summary>
    public static class HoloArtBuilder
    {
        private const string Root = "Assets/_Project/Art/UI";
        public const string AssetPath = Root + "/HoloArt.asset";

        [MenuItem("SpaceStation/UI/Build Holo Theme Art")]
        public static HoloArt Build()
        {
            Directory.CreateDirectory(Root);
            // 빛 번짐: 가운데는 비고 가장자리 둘레만 부드럽게 (모서리 깎인 사각형 바깥으로 퍼짐)
            const int g = 96, pad = 24;
            WriteSprite("UI_HoloGlow.png", g, g, (x, y) =>
            {
                float dx = Mathf.Max(pad - x, x - (g - pad), 0f);
                float dy = Mathf.Max(pad - y, y - (g - pad), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float inside = Mathf.Max(pad - x, x - (g - pad), pad - y, y - (g - pad));
                float a = inside < 0f ? Mathf.Clamp01(1f + inside / 6f) * 0.55f : Mathf.Pow(Mathf.Clamp01(1f - d / pad), 2.2f);
                return new Color(1f, 1f, 1f, a);
            }, pad + 8);
            // 스캔 띠: 세로로 가운데가 밝은 그러데이션 (아래쪽 꼬리가 길게)
            WriteSprite("UI_HoloSweep.png", 8, 64, (x, y) =>
            {
                float v = y / 64f;
                float a = v > 0.55f ? Mathf.Pow(1f - (v - 0.55f) / 0.45f, 3f) : Mathf.Pow(v / 0.55f, 1.6f);
                return new Color(1f, 1f, 1f, a);
            }, 0);
            // 주사선: 3px 중 1px 밝음 (반복)
            WriteTexture("UI_HoloScan.png", 4, 3, (x, y) => new Color(1f, 1f, 1f, y < 1f ? 1f : 0f));
            // 격자: 32px 칸, 가는 선 + 교차점 점
            WriteTexture("UI_HoloGrid.png", 32, 32, (x, y) =>
            {
                bool line = x < 1f || y < 1f;
                bool dot = x < 2.5f && y < 2.5f;
                return new Color(1f, 1f, 1f, dot ? 1f : line ? 0.45f : 0f);
            });

            // 테크 테두리 (사용자 레퍼런스 2026-10-10): 왼쪽 위 · 오른쪽 아래 크게, 나머지 작게 깎은 모서리
            const int t = 96, tb = 30;
            float[] cut = { 6f, 18f, 6f, 18f }; // BL, BR, TR, TL
            WriteSprite("UI_TechFill.png", t, t, (x, y) =>
            {
                float sd = Cut(x, y, t, t, cut);
                if (sd > 0f) return Color.clear;
                return new Color(1f, 1f, 1f, Mathf.Lerp(0.82f, 1f, y / t) * Mathf.Clamp01(-sd));
            }, tb);
            WriteSprite("UI_TechFrame.png", t, t, (x, y) =>
            {
                float sd = Cut(x, y, t, t, cut);
                float a = 0f;
                if (sd <= 0f && sd > -1.6f) a = 1f;                       // 바깥 선
                else if (sd <= -5f && sd > -6f) a = 0.22f;                // 안쪽 가는 선
                // 큰 모서리(왼쪽 위 · 오른쪽 아래) 대각선 굵은 강조
                bool tl = x + (t - y) < cut[3] + 3.5f && x + (t - y) > cut[3] - 1f;
                bool br = (t - x) + y < cut[1] + 3.5f && (t - x) + y > cut[1] - 1f;
                if ((tl || br) && sd <= 0f && sd > -3.2f) a = 1f;
                // 이음점: 큰 모서리 대각선 양 끝
                if (Near(x, y, 2.2f, t - cut[3] - 5f, 2.4f) || Near(x, y, cut[3] + 5f, t - 2.2f, 2.4f)
                    || Near(x, y, t - 2.2f, cut[1] + 5f, 2.4f) || Near(x, y, t - cut[1] - 5f, 2.2f, 2.4f))
                    a = 1f;
                return new Color(1f, 1f, 1f, a);
            }, tb);
            // 테크 버튼: 왼쪽 위 · 오른쪽 아래 깎임 + 테두리 + 안쪽 가는 선 + 왼쪽 위 굵은 강조 + 오른쪽 아래 작은 사선 줄무늬 3개
            const int bt = 64, bb = 20;
            float[] bcut = { 3f, 11f, 3f, 11f };
            WriteSprite("UI_TechButton.png", bt, bt, (x, y) =>
            {
                float sd = Cut(x, y, bt, bt, bcut);
                if (sd > 0f) return Color.clear;
                if (sd > -1.4f) return Color.white;                                  // 테두리
                bool tl = x + (bt - y) < bcut[3] + 3f && sd > -3.4f;                 // 왼쪽 위 굵은 강조
                if (tl) return Color.white;
                if (sd <= -4f && sd > -4.8f) return new Color(1f, 1f, 1f, 0.6f);    // 안쪽 가는 선 (바탕보다 밝게)
                bool hatch = y > 5f && y < 9f && x > bt - 34f && x < bt - 15f && Mathf.Repeat(x - y, 6f) < 2.5f;
                if (hatch) return new Color(1f, 1f, 1f, 0.95f);
                return new Color(1f, 1f, 1f, Mathf.Lerp(0.34f, 0.52f, y / bt));     // 바탕 (위가 조금 밝게)
            }, bb);
            // 모서리 브래킷: 왼쪽 위 ㄱ자 (돌려서 네 모서리에 씀) — 굵기 3px, 길이 14px
            WriteSprite("UI_TechBracket.png", 24, 24, (x, y) =>
            {
                bool top = y > 21f && x < 14f;
                bool left = x < 3f && y > 10f;
                return new Color(1f, 1f, 1f, top || left ? 1f : 0f);
            }, 0);
            // 사선 줄무늬: 12px 칸에 5px 굵기 "/" 반복
            WriteTexture("UI_TechHatch.png", 12, 12, (x, y) => new Color(1f, 1f, 1f, Mathf.Repeat(x - y, 12f) < 5f ? 1f : 0f));
            WriteSprite("UI_TechDot.png", 16, 16, (x, y) =>
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(8f, 8f));
                return new Color(1f, 1f, 1f, Mathf.Clamp01(6f - d));
            }, 0);

            var art = AssetDatabase.LoadAssetAtPath<HoloArt>(AssetPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<HoloArt>();
                AssetDatabase.CreateAsset(art, AssetPath);
            }
            art.Fill = HudArtBuilder.Fill;
            art.Frame = HudArtBuilder.Frame;
            art.Button = HudArtBuilder.Button;
            art.Glow = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/UI_HoloGlow.png");
            art.Sweep = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/UI_HoloSweep.png");
            art.Scan = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/UI_HoloScan.png");
            art.Grid = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/UI_HoloGrid.png");
            art.TechFill = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/UI_TechFill.png");
            art.TechFrame = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/UI_TechFrame.png");
            art.Hatch = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/UI_TechHatch.png");
            art.Dot = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/UI_TechDot.png");
            art.TechButton = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/UI_TechButton.png");
            art.Bracket = AssetDatabase.LoadAssetAtPath<Sprite>(Root + "/UI_TechBracket.png");
            if (art.Font == null)
                art.Font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log("[HoloArtBuilder] 홀로그램 테마 아트 생성 → " + AssetPath);
            return art;
        }

        /// <summary>모서리마다 다르게 깎은 사각형의 부호 거리 (안쪽 음수). cuts = BL, BR, TR, TL.</summary>
        private static float Cut(float x, float y, float w, float h, float[] cuts)
        {
            float sd = Mathf.Max(Mathf.Max(-x, x - w), Mathf.Max(-y, y - h));
            const float k = 0.70710678f;
            sd = Mathf.Max(sd, (cuts[0] - x - y) * k);
            sd = Mathf.Max(sd, (cuts[1] - (w - x) - y) * k);
            sd = Mathf.Max(sd, (cuts[2] - (w - x) - (h - y)) * k);
            sd = Mathf.Max(sd, (cuts[3] - x - (h - y)) * k);
            return sd;
        }

        private static bool Near(float x, float y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

        private static string Draw(string file, int w, int h, Func<float, float, Color> shade)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                            sum += shade(x + (sx + 0.5f) / 4f, y + (sy + 0.5f) / 4f);
                    tex.SetPixel(x, y, sum / 16f);
                }
            }
            tex.Apply();
            string path = $"{Root}/{file}";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            return path;
        }

        private static void WriteSprite(string file, int w, int h, Func<float, float, Color> shade, int border)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(Draw(file, w, h, shade));
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spriteBorder = new Vector4(border, border, border, border);
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }

        private static void WriteTexture(string file, int w, int h, Func<float, float, Color> shade)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(Draw(file, w, h, shade));
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
    }
}
