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
            if (art.Font == null)
                art.Font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/_Project/Art/Fonts/Maplestory SDF.asset");
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();
            Debug.Log("[HoloArtBuilder] 홀로그램 테마 아트 생성 → " + AssetPath);
            return art;
        }

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
