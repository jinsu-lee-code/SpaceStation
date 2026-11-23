using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 레시피 창 입체 칸 그림 (사용자 2026-11-06 "레시피 표시 알아보기 너무 불편"): 칸 묶음을 비스듬히 위에서 본 상자 그림으로 텍스처에 그린다.
    /// 윗면 · 오른쪽 · 왼쪽 면을 밝기로만 나누고(색약 사용자), 묶음(재료 하나)마다 다른 밝기 + 면 테두리. 묶음마다 번호를 붙일 자리(윗면 가운데)를 돌려준다.
    /// </summary>
    public static class IsoShapeRenderer
    {
        private const float Cos30 = 0.8660254f;
        private const int Pad = 3;

        /// <summary>
        /// cells[i]를 groups[i] 묶음 색(palette[groups[i]])으로 그린 텍스처 (최대 maxWidth × maxHeight 픽셀, 비율 유지, 칸 한 변은 maxUnit 픽셀까지 —
        /// 칸이 적은 모양이 큰 덩어리로 보이지 않게). labelPoints = 묶음마다 번호 자리 (보이는 윗면 가운데, 텍스처 픽셀 · 왼쪽 위 기준), null 가능.
        /// </summary>
        public static Texture2D Render(IReadOnlyList<Vector3Int> cells, IReadOnlyList<int> groups, IReadOnlyList<Color> palette, Color edge,
            int maxWidth, int maxHeight, List<Vector2> labelPoints = null, float maxUnit = 56f)
        {
            // 단위 크기로 투영 범위
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var c in cells)
                for (int dx = 0; dx <= 1; dx++)
                    for (int dy = 0; dy <= 1; dy++)
                        for (int dz = 0; dz <= 1; dz++)
                        {
                            var p = Project(c.x + dx, c.y + dy, c.z + dz, 1f);
                            minU = Mathf.Min(minU, p.x); maxU = Mathf.Max(maxU, p.x);
                            minV = Mathf.Min(minV, p.y); maxV = Mathf.Max(maxV, p.y);
                        }
            float s = Mathf.Min(maxUnit, Mathf.Min((maxWidth - Pad * 2) / Mathf.Max(0.01f, maxU - minU), (maxHeight - Pad * 2) / Mathf.Max(0.01f, maxV - minV)));
            int w = Mathf.Max(4, Mathf.CeilToInt((maxU - minU) * s) + Pad * 2);
            int h = Mathf.Max(4, Mathf.CeilToInt((maxV - minV) * s) + Pad * 2);
            var pixels = new Color32[w * h];
            Vector2 offset = new Vector2(Pad - minU * s, Pad - minV * s);

            // 뒤에서 앞으로 (x + y + z가 클수록 보는 쪽에 가까움)
            var order = new List<int>(cells.Count);
            for (int i = 0; i < cells.Count; i++)
                order.Add(i);
            order.Sort((a, b) =>
            {
                int sa = cells[a].x + cells[a].y + cells[a].z, sb = cells[b].x + cells[b].y + cells[b].z;
                return sa != sb ? sa.CompareTo(sb) : cells[a].y.CompareTo(cells[b].y);
            });
            var quad = new Vector2[4];
            foreach (int i in order)
            {
                var c = cells[i];
                var baseColor = palette[Mathf.Clamp(groups[i], 0, palette.Count - 1)];
                int x = c.x, y = c.y, z = c.z;
                // 오른쪽 면 (+x)
                Face(pixels, w, h, quad, s, offset, Shade(baseColor, 0.72f), edge,
                    new Vector3(x + 1, y, z), new Vector3(x + 1, y + 1, z), new Vector3(x + 1, y + 1, z + 1), new Vector3(x + 1, y, z + 1));
                // 왼쪽 면 (+z)
                Face(pixels, w, h, quad, s, offset, Shade(baseColor, 0.5f), edge,
                    new Vector3(x, y, z + 1), new Vector3(x + 1, y, z + 1), new Vector3(x + 1, y + 1, z + 1), new Vector3(x, y + 1, z + 1));
                // 윗면 (+y)
                Face(pixels, w, h, quad, s, offset, baseColor, edge,
                    new Vector3(x, y + 1, z), new Vector3(x + 1, y + 1, z), new Vector3(x + 1, y + 1, z + 1), new Vector3(x, y + 1, z + 1));
            }

            if (labelPoints != null)
            {
                // 묶음마다 윗면이 실제로 보이는 칸 (그 자리 픽셀이 그 묶음 윗면 색) 중 가장 높고 앞쪽 — 다른 재료에 가린 칸에 번호가 붙지 않게
                labelPoints.Clear();
                int count = 0;
                foreach (int g in groups)
                    count = Mathf.Max(count, g + 1);
                for (int g = 0; g < count; g++)
                {
                    Color32 top = palette[Mathf.Clamp(g, 0, palette.Count - 1)];
                    Vector2 bestPoint = new Vector2(-1f, -1f);
                    int bestScore = int.MinValue;
                    for (int i = 0; i < cells.Count; i++)
                    {
                        if (groups[i] != g)
                            continue;
                        var c = cells[i];
                        var p = Project(c.x + 0.5f, c.y + 1f, c.z + 0.5f, s) + offset;
                        int score = c.y * 100 + c.x + c.z;
                        int px = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1), py = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
                        var seen = pixels[py * w + px];
                        if (seen.r != top.r || seen.g != top.g || seen.b != top.b)
                            score -= 100000; // 가려짐 — 보이는 칸이 없을 때만
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestPoint = p;
                        }
                    }
                    labelPoints.Add(bestPoint);
                }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "RecipeShape", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            // 텍스처는 아래 줄부터 → 위아래 뒤집어 넣음
            var flipped = new Color32[w * h];
            for (int row = 0; row < h; row++)
                System.Array.Copy(pixels, row * w, flipped, (h - 1 - row) * w, w);
            tex.SetPixels32(flipped);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>격자 좌표 → 그림 좌표 (오른쪽 = +x 쪽, 아래 = 앞쪽, 위 = +y).</summary>
        private static Vector2 Project(float x, float y, float z, float s)
            => new Vector2((x - z) * Cos30 * s, ((x + z) * 0.5f - y) * s);

        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        private static void Face(Color32[] pixels, int w, int h, Vector2[] quad, float s, Vector2 offset, Color fill, Color edge,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            quad[0] = Project(a.x, a.y, a.z, s) + offset;
            quad[1] = Project(b.x, b.y, b.z, s) + offset;
            quad[2] = Project(c.x, c.y, c.z, s) + offset;
            quad[3] = Project(d.x, d.y, d.z, s) + offset;
            float minX = Mathf.Min(Mathf.Min(quad[0].x, quad[1].x), Mathf.Min(quad[2].x, quad[3].x));
            float maxX = Mathf.Max(Mathf.Max(quad[0].x, quad[1].x), Mathf.Max(quad[2].x, quad[3].x));
            float minY = Mathf.Min(Mathf.Min(quad[0].y, quad[1].y), Mathf.Min(quad[2].y, quad[3].y));
            float maxY = Mathf.Max(Mathf.Max(quad[0].y, quad[1].y), Mathf.Max(quad[2].y, quad[3].y));
            Color32 fill32 = fill, edge32 = edge;
            for (int py = Mathf.Max(0, Mathf.FloorToInt(minY)); py <= Mathf.Min(h - 1, Mathf.CeilToInt(maxY)); py++)
                for (int px = Mathf.Max(0, Mathf.FloorToInt(minX)); px <= Mathf.Min(w - 1, Mathf.CeilToInt(maxX)); px++)
                {
                    var p = new Vector2(px + 0.5f, py + 0.5f);
                    if (Inside(quad, p))
                        pixels[py * w + px] = fill32;
                }
            for (int i = 0; i < 4; i++)
                Line(pixels, w, h, quad[i], quad[(i + 1) % 4], edge32);
        }

        /// <summary>볼록 사각형 안 (네 변 모두 같은 쪽).</summary>
        private static bool Inside(Vector2[] q, Vector2 p)
        {
            bool pos = false, neg = false;
            for (int i = 0; i < 4; i++)
            {
                var a = q[i];
                var b = q[(i + 1) % 4];
                float cross = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                pos |= cross > 1e-4f;
                neg |= cross < -1e-4f;
            }
            return !(pos && neg);
        }

        private static void Line(Color32[] pixels, int w, int h, Vector2 a, Vector2 b, Color32 color)
        {
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y))) + 1;
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                int px = Mathf.Clamp(Mathf.FloorToInt(p.x), 0, w - 1), py = Mathf.Clamp(Mathf.FloorToInt(p.y), 0, h - 1);
                pixels[py * w + px] = color;
            }
        }
    }
}
