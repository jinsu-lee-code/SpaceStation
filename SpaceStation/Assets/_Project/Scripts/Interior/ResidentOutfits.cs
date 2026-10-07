using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-11 주민 옷 · 머리 색 다양성. LuceedStudio "Little Guys" 텍스처 아틀라스(3×3칸)에서 부위 칸만 다시 칠한 텍스처를 만들어 캐시한다.
    /// 칸 배치 (위에서부터 행, 왼쪽부터 열 — 칸마다 색을 칠해 렌더해서 확인): 셔츠 (0,0) · 바지 (1,1) · 신발 (1,2) · 머리 (1,0)(앞머리 · 눈썹) + (2,2).
    /// 칠하기: 칸 평균 밝기 대비 픽셀 밝기를 목표 색에 곱함 → 바느질 · 주름 · 머리결 음영은 그대로.
    /// 원본 텍스처는 읽기 불가여도 되도록 GPU로 복사(Blit)해서 읽는다. 같은 조합은 한 번만 만든다.
    /// </summary>
    public static class ResidentOutfits
    {
        private const int Cells = 3;

        private enum Part { Shirt, Pants, Shoes, Hair }

        // (행, 열) → 부위
        private static readonly (int Row, int Col, Part Part)[] Regions =
        {
            (0, 0, Part.Shirt), (1, 1, Part.Pants), (1, 2, Part.Shoes), (1, 0, Part.Hair), (2, 2, Part.Hair),
        };

        private sealed class Source
        {
            public int Size;
            public Color32[] Pixels;
            public readonly float[] MeanLum = new float[4];
        }

        private static readonly Dictionary<Texture, Source> Sources = new Dictionary<Texture, Source>();
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(Texture source, Color shirt, Color pants, Color shoes, Color hair)
        {
            if (source == null)
                return null;
            string key = $"{source.GetInstanceID()}|{(Color32)shirt}|{(Color32)pants}|{(Color32)shoes}|{(Color32)hair}";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var src = Read(source);
            var pixels = (Color32[])src.Pixels.Clone();
            var targets = new[] { shirt, pants, shoes, hair };
            int cell = src.Size / Cells;
            foreach (var (row, col, part) in Regions)
            {
                var target = targets[(int)part];
                float mean = Mathf.Max(0.02f, src.MeanLum[(int)part]);
                int x0 = col * cell, x1 = col == Cells - 1 ? src.Size : x0 + cell;
                int yTop = src.Size - row * cell;                       // 텍스처 y는 아래에서부터
                int y0 = row == Cells - 1 ? 0 : yTop - cell, y1 = yTop;
                for (int y = y0; y < y1; y++)
                {
                    int line = y * src.Size;
                    for (int x = x0; x < x1; x++)
                    {
                        var p = pixels[line + x];
                        float k = Lum(p) / mean;
                        pixels[line + x] = new Color32(
                            (byte)Mathf.Clamp(target.r * k * 255f, 0f, 255f),
                            (byte)Mathf.Clamp(target.g * k * 255f, 0f, 255f),
                            (byte)Mathf.Clamp(target.b * k * 255f, 0f, 255f), p.a);
                    }
                }
            }
            var tex = new Texture2D(src.Size, src.Size, TextureFormat.RGBA32, true) { name = "ResidentOutfit", wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(pixels);
            tex.Apply(true, true); // 밉맵 + CPU 복사본 버림
            Cache[key] = tex;
            return tex;
        }

        private static float Lum(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        private static Source Read(Texture texture)
        {
            if (Sources.TryGetValue(texture, out var s))
                return s;
            int size = texture.width;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(size, size, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            s = new Source { Size = size, Pixels = copy.GetPixels32() };
            if (Application.isPlaying) Object.Destroy(copy);
            else Object.DestroyImmediate(copy);

            // 부위별 평균 밝기
            var sum = new float[4];
            var count = new int[4];
            int cell = size / Cells;
            foreach (var (row, col, part) in Regions)
            {
                int x0 = col * cell, x1 = col == Cells - 1 ? size : x0 + cell;
                int yTop = size - row * cell;
                int y0 = row == Cells - 1 ? 0 : yTop - cell;
                for (int y = y0; y < yTop; y += 4)
                {
                    for (int x = x0; x < x1; x += 4)
                    {
                        sum[(int)part] += Lum(s.Pixels[y * size + x]);
                        count[(int)part]++;
                    }
                }
            }
            for (int i = 0; i < 4; i++)
                s.MeanLum[i] = count[i] > 0 ? sum[i] / count[i] : 1f;
            Sources[texture] = s;
            return s;
        }
    }
}
