using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 지금 화면에 떠 있는 창 · HUD 패널(홀로 장식 <see cref="HoloFx"/>가 붙은 것)의 화면 사각형.
    /// 월드 라벨(파손 · 열 · 광맥)이 반투명 창 뒤로 비쳐 글이 겹쳐 읽히던 것 (SIMU_FIX U1 · U4, 2026-11-28) — 창과 겹치는 라벨은 숨김.
    /// 라벨 층 자신은 <see cref="Ignore"/>로 빼 둔다. 창 목록은 1초마다 다시 찾고, 사각형은 프레임마다 한 번 계산.
    /// 화면 덮개 캔버스(Screen Space - Overlay)의 창만 — 좌표 = 화면 픽셀.
    /// </summary>
    public static class UiOccluders
    {
        private static readonly List<HoloFx> Fx = new List<HoloFx>();
        private static readonly List<Rect> Rects = new List<Rect>();
        private static readonly List<Transform> Ignored = new List<Transform>();
        private static readonly List<CanvasGroup> Groups = new List<CanvasGroup>();
        private static readonly Vector3[] Corners = new Vector3[4];
        private static int _frame = -1;
        private static float _nextScan;

        /// <summary>라벨 층 (그 아래 패널은 가리개로 치지 않음).</summary>
        public static void Ignore(Transform labelRoot)
        {
            if (labelRoot != null && !Ignored.Contains(labelRoot))
                Ignored.Add(labelRoot);
        }

        /// <summary>화면 사각형(픽셀)이 떠 있는 창과 겹치는지.</summary>
        public static bool Covers(Rect screen)
        {
            Refresh();
            foreach (var r in Rects)
            {
                if (r.Overlaps(screen))
                    return true;
            }
            return false;
        }

        /// <summary>화면 덮개 캔버스 위 사각형이 떠 있는 창과 겹치는지.</summary>
        public static bool Covers(RectTransform rt)
        {
            if (rt == null)
                return false;
            rt.GetWorldCorners(Corners);
            return Covers(Rect.MinMaxRect(Corners[0].x, Corners[0].y, Corners[2].x, Corners[2].y));
        }

        private static void Refresh()
        {
            if (_frame == Time.frameCount)
                return;
            _frame = Time.frameCount;
            if (Time.unscaledTime >= _nextScan || Fx.Count == 0)
            {
                _nextScan = Time.unscaledTime + 1f;
                Fx.Clear();
                Fx.AddRange(Object.FindObjectsByType<HoloFx>(FindObjectsSortMode.None));
                Ignored.RemoveAll(t => t == null);
            }
            Rects.Clear();
            foreach (var fx in Fx)
            {
                if (fx == null || !fx.isActiveAndEnabled || IsIgnored(fx.transform) || Alpha(fx.transform) < 0.5f)
                    continue;
                var canvas = fx.GetComponentInParent<Canvas>();
                if (canvas == null || canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    continue;
                ((RectTransform)fx.transform).GetWorldCorners(Corners);
                Rects.Add(Rect.MinMaxRect(Corners[0].x, Corners[0].y, Corners[2].x, Corners[2].y));
            }
        }

        private static bool IsIgnored(Transform t)
        {
            foreach (var root in Ignored)
            {
                if (root != null && t.IsChildOf(root))
                    return true;
            }
            return false;
        }

        /// <summary>창의 실제 불투명도 (위로 CanvasGroup들을 곱함 — 닫힌 창은 alpha 0).</summary>
        private static float Alpha(Transform t)
        {
            t.GetComponentsInParent(false, Groups);
            float a = 1f;
            foreach (var g in Groups)
            {
                a *= g.alpha;
                if (g.ignoreParentGroups)
                    break;
            }
            return a;
        }
    }
}
