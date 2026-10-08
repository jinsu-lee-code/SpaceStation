using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-11b 주민 텍스처 합성 (CPU, 같은 조합은 한 번만 — 캐시).
    /// 얼굴: 1024 아틀라스 전체를 피부색으로 칠하고 왼쪽 위 사분면(얼굴 앞면 UV)에 조각을 차례로 덮음 —
    /// 볼 홍조 → 점 → 홍채 × 눈동자색 → 눈 → 눈썹 × 머리색 → 입 (Blender `resident2_face.compose`와 같은 순서 · 배율).
    /// 옷: <c>색 = lerp(무늬, 무늬 × 옷색, A)</c> — A = 1 천(색 바꿈), 0 고정색 부품(단추 · 끈 · 지퍼 · 밑창 등).
    /// 원본 텍스처는 읽기 불가여도 되도록 GPU로 복사(Blit)해서 읽는다.
    /// </summary>
    public static class ResidentTextures
    {
        private const int AtlasSize = 1024;
        private const float IrisGain = 1.6f, BrowGain = 2.2f;

        private static readonly Dictionary<Texture, Color32[]> Sources = new Dictionary<Texture, Color32[]>();
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public delegate Texture2D PartLookup(string name);

        public static Texture2D Face(PartLookup part, string eye, string brow, string mouth, string mark, Color skin, Color eyeColor, Color hairColor)
        {
            string key = $"face|{eye}|{brow}|{mouth}|{mark}|{(Color32)skin}|{(Color32)eyeColor}|{(Color32)hairColor}";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var atlas = new Color[AtlasSize * AtlasSize];
            for (int i = 0; i < atlas.Length; i++)
                atlas[i] = skin;
            Over(atlas, part("blush_base"), Color.white);
            if (!string.IsNullOrEmpty(mark) && mark != "none")
                Over(atlas, part("mark_" + mark), Color.white);
            Over(atlas, part($"eye_{eye}_iris"), eyeColor * IrisGain);
            Over(atlas, part("eye_" + eye), Color.white);
            Over(atlas, part("brow_" + brow), hairColor * BrowGain);
            Over(atlas, part("mouth_" + mouth), Color.white);

            var tex = Make("ResidentFace", AtlasSize, atlas);
            Cache[key] = tex;
            return tex;
        }

        public static Texture2D Cloth(Texture2D source, Color color)
        {
            if (source == null)
                return null;
            string key = $"cloth|{source.GetInstanceID()}|{(Color32)color}";
            if (Cache.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var src = Read(source);
            var px = new Color[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var p = src[i];
                float a = p.a / 255f;
                float r = p.r / 255f, g = p.g / 255f, b = p.b / 255f;
                px[i] = new Color(r * (1f - a + a * color.r), g * (1f - a + a * color.g), b * (1f - a + a * color.b), 1f);
            }
            var tex = Make("ResidentCloth", source.width, px);
            Cache[key] = tex;
            return tex;
        }

        /// <summary>조각(정사각, 얼굴 앞면 = 아틀라스 왼쪽 위 사분면)을 알파로 덮음.</summary>
        private static void Over(Color[] atlas, Texture2D part, Color tint)
        {
            if (part == null)
                return;
            var src = Read(part);
            int n = part.width;
            int half = AtlasSize / 2;
            for (int y = 0; y < half; y++)
            {
                int sy = y * n / half;
                for (int x = 0; x < half; x++)
                {
                    var p = src[sy * n + x * n / half];
                    if (p.a == 0)
                        continue;
                    float a = p.a / 255f;
                    int i = (y + half) * AtlasSize + x;   // 텍스처 y는 아래에서부터 → 위쪽 절반
                    var d = atlas[i];
                    atlas[i] = new Color(
                        d.r * (1f - a) + p.r / 255f * tint.r * a,
                        d.g * (1f - a) + p.g / 255f * tint.g * a,
                        d.b * (1f - a) + p.b / 255f * tint.b * a, 1f);
                }
            }
        }

        private static Texture2D Make(string name, int size, Color[] px)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, anisoLevel = 2 };
            tex.SetPixels(px);
            tex.Apply(true, true); // 밉맵 + CPU 복사본 버림
            return tex;
        }

        private static Color32[] Read(Texture texture)
        {
            if (Sources.TryGetValue(texture, out var s))
                return s;
            int w = texture.width, h = texture.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            s = copy.GetPixels32();
            if (Application.isPlaying) Object.Destroy(copy);
            else Object.DestroyImmediate(copy);
            Sources[texture] = s;
            return s;
        }
    }
}
