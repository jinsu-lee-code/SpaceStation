using System.Collections.Generic;
using SpaceStation.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// REVIEW_1115 1-8 조준점 그리기 — 1인칭 내부 화면 가운데와 설정 창 [조준점] 탭 미리보기가 같이 씀.
    /// 사각형 (가운데 점 1 + 안쪽 선 4 + 바깥 선 4)과 그 뒤 한 겹 큰 검은 사각형(외곽선)을 화면 픽셀 칸에 칠한 뒤 칸 단위 메시 하나로 그림:
    /// 겹친 외곽선 · 반투명 선이 두 번 칠해져 진해지지 않음 (사각형 Image를 겹쳐 그리면 점 외곽선과 선 외곽선이 겹친 줄이 더 어두웠음).
    /// 크기 = 값 × 캔버스 배율 (해상도 · UI 크기, 2026-11-26 사용자 "UI 크기에 맞춰 같이 커지게" — 1080p · UI 100 %에서 값 = 픽셀)을
    /// 정수 픽셀로 반올림, 기준점을 가장 가까운 픽셀 경계에 맞춰 늘 또렷하게 (가장자리 흐림 없음).
    /// 위치 · 배율이 바뀌거나 (창 열기 연출 · 해상도 변경) 값이 바뀔 때만 메시를 다시 만듦.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CrosshairView : MaskableGraphic
    {
        /// <summary>기준점에서 픽셀 단위 사각형 [X0, X1) × [Y0, Y1).</summary>
        private struct Box
        {
            public int X0, Y0, X1, Y1;
            public float Alpha;

            public Box(int x0, int y0, int x1, int y1, float alpha)
            {
                X0 = x0;
                Y0 = y0;
                X1 = x1;
                Y1 = y1;
                Alpha = alpha;
            }
        }

        private static readonly List<Box> Fills = new List<Box>(9);
        private static readonly List<Box> Outlines = new List<Box>(9);
        private static float[] _fillAlpha = new float[0];
        private static float[] _outlineAlpha = new float[0];
        private static Color32[] _cells = new Color32[0];

        private bool _follow;
        private CrosshairStyle _style = CrosshairStyle.Default;
        private bool _dirty = true;
        private Vector3 _lastPos;
        private Vector3 _lastScale;

        /// <summary>지금 그리는 값. 설정을 따르는 보기(기본)는 <see cref="CrosshairSettings.Changed"/> 때 저절로 바뀜.</summary>
        public CrosshairStyle Style
        {
            get => _style;
            set
            {
                _style = value.Clamped();
                _dirty = true;
            }
        }

        /// <summary>parent의 가운데에 조준점. followSettings = 저장된 지금 조준점을 따라감.</summary>
        public static CrosshairView Create(Transform parent, string name = "Crosshair", bool followSettings = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            var view = go.AddComponent<CrosshairView>();
            view.raycastTarget = false;
            view._follow = followSettings;
            if (followSettings)
            {
                view._style = CrosshairSettings.Current;
                CrosshairSettings.Changed += view.OnSettingsChanged;
            }
            view._dirty = true;
            return view;
        }

        protected override void OnDestroy()
        {
            if (_follow)
                CrosshairSettings.Changed -= OnSettingsChanged;
            base.OnDestroy();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _dirty = true;
        }

        private void OnSettingsChanged()
        {
            _style = CrosshairSettings.Current;
            _dirty = true;
        }

        private void LateUpdate()
        {
            var rt = rectTransform;
            var pos = rt.position;
            var scale = rt.lossyScale;
            if (!_dirty && pos == _lastPos && scale == _lastScale)
                return;
            _dirty = false;
            _lastPos = pos;
            _lastScale = scale;
            // 사각형 크기 = 그리는 범위 (RectMask2D 안에서 걸러지지 않게, 기준점은 가운데 그대로)
            int reach = Collect(_style, Unit());
            if (scale.x > 1e-4f && scale.y > 1e-4f)
            {
                var size = new Vector2(2f * reach / scale.x, 2f * reach / scale.y);
                if (rt.sizeDelta != size)
                    rt.sizeDelta = size;
            }
            SetVerticesDirty();
        }

        /// <summary>
        /// 화면 오버레이 캔버스에선 월드 좌표 = 화면 픽셀이고 lossyScale = 캔버스 배율 → 픽셀 값을 배율로 나눠 놓음.
        /// 기준점 O = 이 오브젝트 위치에서 가장 가까운 픽셀 경계, 칸 경계는 O에서 정수 픽셀만큼 떨어짐.
        /// 칸마다: 색 = 덮은 색 사각형 중 가장 진한 불투명, 외곽선 = 덮은 외곽선 중 가장 진한 것 → 색이 외곽선 위에 얹힌 한 색으로 합침.
        /// </summary>
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rt = rectTransform;
            var pos = rt.position;
            var scale = rt.lossyScale;
            float sx = scale.x, sy = scale.y;
            if (!_style.Visible || sx <= 1e-4f || sy <= 1e-4f)
                return;
            Collect(_style, Unit());
            if (Fills.Count == 0)
                return;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            Bounds(Fills, ref minX, ref minY, ref maxX, ref maxY);
            Bounds(Outlines, ref minX, ref minY, ref maxX, ref maxY);
            int w = maxX - minX, h = maxY - minY;
            int n = w * h;
            if (_cells.Length < n)
            {
                _fillAlpha = new float[n];
                _outlineAlpha = new float[n];
                _cells = new Color32[n];
            }
            System.Array.Clear(_fillAlpha, 0, n);
            System.Array.Clear(_outlineAlpha, 0, n);
            Paint(Fills, _fillAlpha, minX, minY, w);
            Paint(Outlines, _outlineAlpha, minX, minY, w);

            var rgb = ToColor(_style.Color);
            for (int i = 0; i < n; i++)
            {
                float a = _fillAlpha[i];
                float o = _outlineAlpha[i];
                float alpha = a + o * (1f - a);
                if (alpha <= 0f)
                {
                    _cells[i] = default;
                    continue;
                }
                float k = a / alpha; // 외곽선은 검정 → 색 몫만큼만 밝게
                _cells[i] = new Color(rgb.r * k, rgb.g * k, rgb.b * k, alpha);
            }

            float ox = Mathf.Round(pos.x) - pos.x;
            float oy = Mathf.Round(pos.y) - pos.y;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                int x = 0;
                while (x < w)
                {
                    var c = _cells[row + x];
                    if (c.a == 0)
                    {
                        x++;
                        continue;
                    }
                    int end = x + 1;
                    while (end < w && Same(_cells[row + end], c))
                        end++;
                    AddQuad(vh, (ox + minX + x) / sx, (oy + minY + y) / sy, (ox + minX + end) / sx, (oy + minY + y + 1) / sy, c);
                    x = end;
                }
            }
        }

        /// <summary>값 1 = 화면 픽셀 몇 개 — 바탕 캔버스 배율 (해상도 · UI 크기). 창 열기 연출 같은 부모 크기 변화는 넣지 않음.</summary>
        private float Unit()
        {
            var c = canvas;
            float f = c != null ? c.rootCanvas.scaleFactor : 1f;
            return f > 1e-4f ? f : 1f;
        }

        /// <summary>값 × 배율 → 픽셀 (0이 아닌 크기는 적어도 1픽셀).</summary>
        private static int Px(int value, float unit) => value <= 0 ? 0 : Mathf.Max(1, Mathf.RoundToInt(value * unit));

        /// <summary>값 → 픽셀 사각형 (색 · 외곽선 목록). 반환 = 기준점에서 가장 먼 칸까지 거리.</summary>
        private static int Collect(CrosshairStyle s, float unit)
        {
            Fills.Clear();
            Outlines.Clear();
            int outline = Px(s.OutlineThickness, unit);
            if (s.DotOn && s.DotOpacity > 0)
            {
                int d = Px(s.DotSize, unit);
                int a0 = -(d / 2);
                Add(s, outline, a0, a0, a0 + d, a0 + d, s.DotOpacity);
            }
            AddLines(s, s.Inner, unit, outline);
            AddLines(s, s.Outer, unit, outline);
            int reach = 0;
            foreach (var b in Outlines.Count > 0 ? Outlines : Fills)
                reach = Mathf.Max(reach, Mathf.Max(Mathf.Max(-b.X0, b.X1), Mathf.Max(-b.Y0, b.Y1)));
            return reach;
        }

        /// <summary>
        /// 네 줄. 두께가 홀수면 가운데 한 픽셀 줄을 사이에 두고 대칭 (위 줄은 1 + 간격부터, 아래 줄은 -간격까지), 짝수면 기준점에서 대칭.
        /// </summary>
        private static void AddLines(CrosshairStyle s, CrosshairStyle.Lines l, float unit, int outline)
        {
            if (!l.On || l.Opacity <= 0)
                return;
            int t = Px(l.Thickness, unit);
            int len = Px(l.Length, unit);
            int gap = Mathf.RoundToInt(l.Gap * unit);
            int a0 = -(t / 2);
            int a1 = a0 + t;
            int near = (t % 2) + gap;  // 위 · 오른 줄이 시작하는 곳
            int far = near + len;
            Add(s, outline, a0, near, a1, far, l.Opacity);           // 위
            Add(s, outline, a0, -gap - len, a1, -gap, l.Opacity);    // 아래
            Add(s, outline, near, a0, far, a1, l.Opacity);           // 오른
            Add(s, outline, -gap - len, a0, -gap, a1, l.Opacity);    // 왼
        }

        private static void Add(CrosshairStyle s, int outline, int x0, int y0, int x1, int y1, int opacity)
        {
            float a = opacity / 100f;
            Fills.Add(new Box(x0, y0, x1, y1, a));
            if (s.OutlineOn && s.OutlineOpacity > 0)
            {
                int o = outline;
                Outlines.Add(new Box(x0 - o, y0 - o, x1 + o, y1 + o, a * s.OutlineOpacity / 100f));
            }
        }

        private static void Bounds(List<Box> boxes, ref int minX, ref int minY, ref int maxX, ref int maxY)
        {
            foreach (var b in boxes)
            {
                minX = Mathf.Min(minX, b.X0);
                minY = Mathf.Min(minY, b.Y0);
                maxX = Mathf.Max(maxX, b.X1);
                maxY = Mathf.Max(maxY, b.Y1);
            }
        }

        private static void Paint(List<Box> boxes, float[] alpha, int minX, int minY, int w)
        {
            foreach (var b in boxes)
            {
                for (int y = b.Y0; y < b.Y1; y++)
                {
                    int row = (y - minY) * w - minX;
                    for (int x = b.X0; x < b.X1; x++)
                    {
                        if (alpha[row + x] < b.Alpha)
                            alpha[row + x] = b.Alpha;
                    }
                }
            }
        }

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        private static void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), c, Vector4.zero);
            vh.AddVert(new Vector3(x0, y1), c, Vector4.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector4.zero);
            vh.AddVert(new Vector3(x1, y0), c, Vector4.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        public static Color ToColor(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
