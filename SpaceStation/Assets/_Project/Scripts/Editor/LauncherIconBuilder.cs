using System;
using System.Collections.Generic;
using System.IO;
using SpaceStation.UI;
using UnityEditor;
using UnityEngine;

namespace SpaceStation.Editor
{
    /// <summary>
    /// 13-6a 관리 메뉴 아이콘 (메뉴 SpaceStation/UI/Build Launcher Icons). 128px 흰색 + 밝기 단계(선 1 · 면 0.25~0.6)로 그려
    /// 화면에서 홀로그램 색을 입힌다 — 색이 아니라 모양 · 밝기로 구분 (색약 배려). 결과는 HoloArt.LauncherIcons (이름 LI_키).
    /// </summary>
    public static class LauncherIconBuilder
    {
        private const string Dir = "Assets/_Project/Art/UI/Launcher";
        private const string ArtPath = "Assets/_Project/Art/UI/HoloArt.asset";
        private const int Size = 128;
        private const float Line = 0.09f;

        /// <summary>(키, 밝기 함수 -1~1, 위 = +y).</summary>
        private static readonly (string Key, Func<Vector2, float> Shade)[] Icons =
        {
            ("manage", Manage),
            ("research", Research),
            ("roster", Roster),
            ("recipes", Recipes),
            ("policies", Policies),
            ("contact", Contact),
            ("systems", Systems),
        };

        [MenuItem("SpaceStation/UI/Build Launcher Icons")]
        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            var sprites = new List<Sprite>();
            foreach (var (key, shade) in Icons)
            {
                string path = $"{Dir}/LI_{key}.png";
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float a = 0f;
                        for (int sy = 0; sy < 4; sy++)
                            for (int sx = 0; sx < 4; sx++)
                                a += shade(new Vector2((x + (sx + 0.5f) / 4f) / Size * 2f - 1f, (y + (sy + 0.5f) / 4f) / Size * 2f - 1f) * 1.05f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a / 16f));
                    }
                }
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
                sprites.Add(AssetDatabase.LoadAssetAtPath<Sprite>(path));
            }
            var art = AssetDatabase.LoadAssetAtPath<HoloArt>(ArtPath);
            if (art != null)
            {
                art.LauncherIcons = sprites.ToArray();
                EditorUtility.SetDirty(art);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[LauncherIconBuilder] 관리 메뉴 아이콘 {sprites.Count}개" + (art != null ? " · HoloArt 연결" : " · HoloArt 없음"));
        }

        // ---------------- 아이콘 ----------------

        /// <summary>관리: 2×2 타일 (테두리 + 옅은 면, 오른쪽 위만 꽉 참).</summary>
        private static float Manage(Vector2 p)
        {
            float best = 0f;
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector2(i % 2 == 0 ? -0.45f : 0.45f, i < 2 ? 0.45f : -0.45f);
                var half = new Vector2(0.36f, 0.36f);
                if (!RBox(p, c, half, 0.1f))
                    continue;
                bool full = i == 1;
                best = Mathf.Max(best, full || !RBox(p, c, half - new Vector2(Line, Line), 0.05f) ? 1f : 0.3f);
            }
            return best;
        }

        /// <summary>연구: 플라스크 (선) + 아래 액체(면) + 거품 + 병목 테.</summary>
        private static float Research(Vector2 p)
        {
            if (Box(p, new Vector2(0f, 0.82f), new Vector2(0.32f, 0.07f)))
                return 1f; // 병목 테
            if (Ring(p, new Vector2(0.38f, 0.55f), 0.11f, 0.05f) || Disc(p, new Vector2(0.55f, 0.82f), 0.06f))
                return 0.8f; // 위로 오르는 거품
            bool outer = Poly(p, new Vector2(-0.22f, 0.78f), new Vector2(0.22f, 0.78f), new Vector2(0.22f, 0.22f), new Vector2(0.8f, -0.82f), new Vector2(-0.8f, -0.82f), new Vector2(-0.22f, 0.22f));
            if (!outer)
                return 0f;
            bool inner = Poly(p, new Vector2(-0.12f, 0.72f), new Vector2(0.12f, 0.72f), new Vector2(0.12f, 0.19f), new Vector2(0.64f, -0.72f), new Vector2(-0.64f, -0.72f), new Vector2(-0.12f, 0.19f));
            if (!inner)
                return 1f;
            if (p.y < -0.2f)
            {
                if (Ring(p, new Vector2(0.12f, -0.42f), 0.12f, 0.05f) || Disc(p, new Vector2(-0.22f, -0.55f), 0.07f) || Disc(p, new Vector2(0.3f, -0.6f), 0.05f))
                    return 1f;
                return Mathf.Abs(p.y + 0.2f) < 0.04f ? 0.9f : 0.5f; // 액체 (윗면은 밝은 줄)
            }
            return 0.1f;
        }

        /// <summary>주민: 동물 머리 둘 (앞 = 테두리 · 귀 안쪽 · 눈 · 코, 뒤 = 옅은 면).</summary>
        private static float Roster(Vector2 p)
        {
            var head = new Vector2(-0.18f, -0.22f);
            var earL = new Vector2(-0.6f, 0.3f);
            var earR = new Vector2(0.24f, 0.32f);
            bool front = Disc(p, head, 0.58f) || Disc(p, earL, 0.21f) || Disc(p, earR, 0.21f);
            if (front)
            {
                if (Disc(p, new Vector2(-0.4f, -0.12f), 0.08f) || Disc(p, new Vector2(0.04f, -0.12f), 0.08f))
                    return 1f; // 눈
                if (Disc(p, new Vector2(-0.18f, -0.36f), 0.07f) || Capsule(p, new Vector2(-0.3f, -0.5f), new Vector2(-0.06f, -0.5f), 0.03f))
                    return 1f; // 코 · 입
                bool inside = Disc(p, head, 0.58f - Line) || Disc(p, earL, 0.21f - Line) || Disc(p, earR, 0.21f - Line);
                if (!inside)
                    return 1f;
                if (Disc(p, earL, 0.11f) || Disc(p, earR, 0.11f))
                    return 0.7f; // 귀 안쪽
                return 0.35f;
            }
            bool back = Disc(p, new Vector2(0.42f, 0.28f), 0.42f) || Disc(p, new Vector2(0.14f, 0.66f), 0.15f) || Disc(p, new Vector2(0.7f, 0.66f), 0.15f);
            if (back)
                return Disc(p, new Vector2(0.42f, 0.28f), 0.42f - Line * 0.7f) || Disc(p, new Vector2(0.14f, 0.66f), 0.08f) || Disc(p, new Vector2(0.7f, 0.66f), 0.08f) ? 0.22f : 0.55f;
            return 0f;
        }

        /// <summary>합성: 작은 육각 둘(테두리) → 화살표 → 큰 육각(면 + 안쪽 육각) + 반짝임.</summary>
        private static float Recipes(Vector2 p)
        {
            foreach (var c in new[] { new Vector2(-0.6f, 0.45f), new Vector2(-0.6f, -0.45f) })
                if (Hex(p, c, 0.32f))
                    return Hex(p, c, 0.32f - Line * 1.1f) ? (Hex(p, c, 0.12f) ? 0.7f : 0.2f) : 1f;
            if (Capsule(p, new Vector2(-0.24f, 0f), new Vector2(0.02f, 0f), 0.055f) || Poly(p, new Vector2(0f, 0.17f), new Vector2(0f, -0.17f), new Vector2(0.2f, 0f)))
                return 1f;
            var big = new Vector2(0.58f, 0f);
            if (Hex(p, big, 0.4f))
            {
                if (!Hex(p, big, 0.4f - Line * 1.1f))
                    return 1f;
                if (Hex(p, big, 0.16f))
                    return Hex(p, big, 0.08f) ? 0.4f : 1f;
                return 0.45f;
            }
            if (Poly(p, new Vector2(0.58f, 0.86f), new Vector2(0.52f, 0.64f), new Vector2(0.58f, 0.46f), new Vector2(0.64f, 0.64f))
                || Poly(p, new Vector2(0.38f, 0.66f), new Vector2(0.58f, 0.6f), new Vector2(0.78f, 0.66f), new Vector2(0.58f, 0.72f)))
                return 0.9f; // 반짝임
            return 0f;
        }

        /// <summary>정책: 접힌 모서리 문서(테두리 · 옅은 면) + 글 줄 + 도장.</summary>
        private static float Policies(Vector2 p)
        {
            var seal = new Vector2(0.42f, -0.52f);
            if (Disc(p, seal, 0.32f))
            {
                if (!Disc(p, seal, 0.32f - Line))
                    return 1f;
                return Disc(p, seal, 0.11f) || Ring(p, seal, 0.2f, 0.04f) ? 1f : 0.45f;
            }
            if (Poly(p, new Vector2(0.18f, 0.86f), new Vector2(0.18f, 0.54f), new Vector2(0.5f, 0.54f)))
                return 0.75f; // 접힌 모서리
            bool outer = Poly(p, new Vector2(-0.62f, 0.86f), new Vector2(0.18f, 0.86f), new Vector2(0.5f, 0.54f), new Vector2(0.5f, -0.86f), new Vector2(-0.62f, -0.86f));
            if (!outer)
                return 0f;
            bool inner = Poly(p, new Vector2(-0.53f, 0.77f), new Vector2(0.14f, 0.77f), new Vector2(0.41f, 0.5f), new Vector2(0.41f, -0.77f), new Vector2(-0.53f, -0.77f));
            if (!inner)
                return 1f;
            if (Capsule(p, new Vector2(-0.36f, 0.48f), new Vector2(0.02f, 0.48f), 0.045f)
                || Capsule(p, new Vector2(-0.36f, 0.22f), new Vector2(0.22f, 0.22f), 0.045f)
                || Capsule(p, new Vector2(-0.36f, -0.04f), new Vector2(0.22f, -0.04f), 0.045f)
                || Capsule(p, new Vector2(-0.36f, -0.3f), new Vector2(-0.06f, -0.3f), 0.045f))
                return 0.9f;
            return 0.22f;
        }

        /// <summary>외부 접촉: 접시 안테나(초승달 접시 · 받침 · 수신부) + 신호 호 둘.</summary>
        private static float Contact(Vector2 p)
        {
            var feed = new Vector2(0.3f, 0.5f);
            if (Arc(p, feed, 0.3f, 0.07f, -15f, 105f))
                return 1f;
            if (Arc(p, feed, 0.52f, 0.07f, -5f, 95f))
                return 0.6f;
            if (Disc(p, feed, 0.09f) || Capsule(p, new Vector2(-0.2f, 0f), feed, 0.045f))
                return 1f;
            bool bowl = Disc(p, new Vector2(-0.22f, 0.02f), 0.62f) && !Disc(p, new Vector2(0.12f, 0.36f), 0.6f);
            if (bowl)
                return Disc(p, new Vector2(-0.22f, 0.02f), 0.62f - Line) && !Disc(p, new Vector2(0.12f, 0.36f), 0.6f + Line) ? 0.45f : 1f;
            if (Capsule(p, new Vector2(-0.34f, -0.45f), new Vector2(-0.34f, -0.74f), 0.07f) || RBox(p, new Vector2(-0.34f, -0.82f), new Vector2(0.4f, 0.07f), 0.04f))
                return 1f;
            return 0f;
        }

        /// <summary>시스템(타이쿤판 해금 선택): 뿌리 노드 → 갈래 둘 — 연 쪽(꽉 찬 노드 · 밝은 선), 남은 쪽(테두리 노드 · 옅은 선).</summary>
        private static float Systems(Vector2 p)
        {
            var root = new Vector2(0f, -0.62f);
            var open = new Vector2(-0.52f, 0.46f);
            var wait = new Vector2(0.52f, 0.46f);
            if (Disc(p, open, 0.25f))
                return Disc(p, open, 0.1f) ? 0.5f : 1f;
            if (Disc(p, wait, 0.25f))
                return Disc(p, wait, 0.25f - Line) ? (Box(p, wait + new Vector2(0f, -0.04f), new Vector2(0.09f, 0.07f)) || Ring(p, wait + new Vector2(0f, 0.06f), 0.08f, 0.035f) ? 0.9f : 0.15f) : 1f;
            if (Disc(p, root, 0.24f))
                return Disc(p, root, 0.1f) ? 0.5f : 1f;
            var mid = new Vector2(0f, -0.05f);
            if (Capsule(p, root, mid, 0.055f) || Capsule(p, mid, open, 0.055f))
                return 1f;
            if (Capsule(p, mid, wait, 0.045f))
                return 0.45f;
            return 0f;
        }

        // ---------------- 도형 도우미 ----------------

        private static bool Disc(Vector2 p, Vector2 c, float r) => (p - c).sqrMagnitude <= r * r;
        private static bool Ring(Vector2 p, Vector2 c, float r, float t) { float d = (p - c).magnitude; return d <= r && d >= r - t; }
        private static bool Box(Vector2 p, Vector2 c, Vector2 half) => Mathf.Abs(p.x - c.x) <= half.x && Mathf.Abs(p.y - c.y) <= half.y;

        private static bool RBox(Vector2 p, Vector2 c, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) <= r;
        }

        private static bool Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).sqrMagnitude <= r * r;
        }

        /// <summary>각도 범위(도, 0 = 오른쪽, 반시계) 안의 고리.</summary>
        private static bool Arc(Vector2 p, Vector2 c, float r, float t, float from, float to)
        {
            if (!Ring(p, c, r, t))
                return false;
            float a = Mathf.Atan2(p.y - c.y, p.x - c.x) * Mathf.Rad2Deg;
            return a >= from && a <= to;
        }

        private static bool Hex(Vector2 p, Vector2 c, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y));
            return q.y <= r * 0.866f && q.x * 0.866f + q.y * 0.5f <= r * 0.866f;
        }

        private static bool Poly(Vector2 p, params Vector2[] v)
        {
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
            {
                if ((v[i].y > p.y) != (v[j].y > p.y) && p.x < (v[j].x - v[i].x) * (p.y - v[i].y) / (v[j].y - v[i].y) + v[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
