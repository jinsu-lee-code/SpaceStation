using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 월드 위치를 가리키는 화면 라벨 묶음 (12-0 파손 표기에서 분리, 12-3c 열 표기와 공용 — 사용자 피드백 2026-10-14: 표기 UI를 창 UI와 통일).
    /// - 모양: 진한 꽉 찬 바탕 + 홀로그램 창과 같은 테두리(HoloUi.Panel) + 왼쪽 강조 띠 + 글자
    /// - 핀: 라벨을 대상 위로 띄우고 라벨 아래 끝에서 대상까지 연결선 (어느 모듈 것인지 분명하게)
    /// - 겹치면 위로 비켜 쌓기, 화면 밖이면 가장자리에 방향 화살표(선 없음)
    /// - 흐린 라벨(<see cref="Marker.Faint"/>): 작고 옅게 — 가려진 대상 등 보조 표시
    /// 매 프레임: <see cref="Begin"/> → 표시할 것마다 <see cref="Show"/> → <see cref="End"/> (안 보인 키의 라벨은 풀로).
    /// </summary>
    public sealed class WorldMarkerLayer
    {
        public sealed class Marker
        {
            public RectTransform Rect;
            public TMP_Text Text;
            public Image Edge;
            public RectTransform Skin;
            public RectTransform Line;
            public Image LineImage;
            public Vector3 World;
            public bool Seen;
            public bool Faint;
            public bool EdgeArrow = true;
            public int ShownKey = int.MinValue;
            public Color Accent;
            /// <summary>화면 밖일 때 붙는 방향 화살표 ("← " 등, 화면 안이면 빈 문자열). 글자를 만들 때 앞에 붙인다.</summary>
            public string Arrow = "";
            // 이번 프레임 배치
            public bool OnScreen;
            public Vector2 Anchor;
            public Vector2 Position;
            public Vector2 Size;
        }

        private readonly RectTransform _container;
        private readonly Canvas _canvas;
        private readonly Camera _camera;
        private readonly HoloUi _ui;
        private readonly TMP_FontAsset _font;
        private readonly Dictionary<object, Marker> _markers = new Dictionary<object, Marker>();
        private readonly Stack<Marker> _pool = new Stack<Marker>();
        private readonly List<object> _release = new List<object>();
        private readonly List<Marker> _visible = new List<Marker>();
        private readonly List<Marker> _placed = new List<Marker>();

        public float FontSize = 20f;
        public float LiftPixels = 44f;
        public float LineWidth = 2f;
        public float Gap = 4f;
        public float EdgeMargin = 24f;
        public Color Background = new Color(0.04f, 0.06f, 0.09f, 0.94f);

        /// <param name="ui">창 테두리 (없으면 바탕 + 강조 띠만)</param>
        /// <param name="font">null이면 TMP 기본</param>
        public WorldMarkerLayer(RectTransform container, Camera camera, HoloUi ui, TMP_FontAsset font)
        {
            _container = container;
            _canvas = container.GetComponentInParent<Canvas>();
            _camera = camera;
            _ui = ui;
            _font = font;
        }

        private float Scale => _canvas != null ? _canvas.scaleFactor : 1f;

        public void Begin()
        {
            foreach (var m in _markers.Values)
                m.Seen = false;
            _visible.Clear();
        }

        /// <summary>key의 라벨을 이번 프레임에 표시 (없으면 만듦). 반환된 라벨에 글자 · 색을 정한다.</summary>
        public Marker Show(object key, Vector3 world, bool faint = false, bool edgeArrow = true)
        {
            if (!_markers.TryGetValue(key, out var m))
            {
                m = Acquire();
                _markers.Add(key, m);
            }
            m.Seen = true;
            m.World = world;
            m.EdgeArrow = edgeArrow;
            if (m.Faint != faint)
            {
                m.Faint = faint;
                m.ShownKey = int.MinValue;
            }
            Project(m);
            if (!m.OnScreen && !edgeArrow)
            {
                Hide(m);
                return m;
            }
            _visible.Add(m);
            return m;
        }

        /// <summary>글자 · 강조색 (key가 바뀔 때만 갱신 — 크기 계산이 무거움).</summary>
        public void Set(Marker m, int key, string text, Color accent)
        {
            key = key * 8 + ArrowCode(m.Arrow);
            if (key == m.ShownKey && accent == m.Accent)
                return;
            m.ShownKey = key;
            m.Accent = accent;
            m.Text.SetText(text);
            m.Text.fontSize = m.Faint ? FontSize * 0.8f : FontSize;
            var preferred = m.Text.GetPreferredValues();
            m.Rect.sizeDelta = new Vector2(Mathf.Max(56f, preferred.x + 22f), preferred.y + 10f);
            float a = m.Faint ? 0.5f : 1f;
            var line = new Color(accent.r, accent.g, accent.b, accent.a * a);
            m.LineImage.color = line;
            m.Edge.color = line;
            if (m.Skin != null)
                foreach (var img in m.Skin.GetComponentsInChildren<Image>(true))
                    img.color = new Color(accent.r, accent.g, accent.b, img == m.Skin.GetComponent<Image>() ? 0f : 0.85f * a);
            var back = m.Rect.GetComponent<Image>();
            back.color = new Color(Background.r, Background.g, Background.b, Background.a * (m.Faint ? 0.6f : 1f));
            m.Text.alpha = m.Faint ? 0.75f : 1f;
        }

        public void End()
        {
            Layout();
            _release.Clear();
            foreach (var pair in _markers)
                if (!pair.Value.Seen)
                    _release.Add(pair.Key);
            foreach (var key in _release)
            {
                var m = _markers[key];
                _markers.Remove(key);
                Hide(m);
                m.ShownKey = int.MinValue;
                _pool.Push(m);
            }
        }

        public void Clear()
        {
            Begin();
            End();
        }

        private static void Hide(Marker m)
        {
            if (m.Rect.gameObject.activeSelf)
                m.Rect.gameObject.SetActive(false);
            if (m.Line.gameObject.activeSelf)
                m.Line.gameObject.SetActive(false);
        }

        private void Project(Marker m)
        {
            Vector3 screen = _camera.WorldToScreenPoint(m.World);
            float w = Screen.width, h = Screen.height;
            bool behind = screen.z < 0f;
            m.OnScreen = !behind && screen.x >= 0f && screen.x <= w && screen.y >= 0f && screen.y <= h;
            if (m.OnScreen)
            {
                m.Anchor = screen;
                m.Arrow = "";
                return;
            }
            var center = new Vector2(w * 0.5f, h * 0.5f);
            var dir = (Vector2)screen - center;
            if (behind)
                dir = -dir;
            if (dir.sqrMagnitude < 1e-4f)
                dir = Vector2.down;
            float margin = EdgeMargin * Scale;
            float halfW = w * 0.5f - margin, halfH = h * 0.5f - margin;
            float t = Mathf.Min(Mathf.Abs(dir.x) > 1e-4f ? halfW / Mathf.Abs(dir.x) : float.MaxValue,
                                Mathf.Abs(dir.y) > 1e-4f ? halfH / Mathf.Abs(dir.y) : float.MaxValue);
            m.Anchor = center + dir * t;
            m.Arrow = Mathf.Abs(dir.x) * h > Mathf.Abs(dir.y) * w
                ? (dir.x > 0f ? "→ " : "← ")
                : (dir.y > 0f ? "↑ " : "↓ ");
        }

        private void Layout()
        {
            float lift = LiftPixels * Scale;
            float gap = Gap * Scale;
            float w = Screen.width, h = Screen.height;
            foreach (var m in _visible)
            {
                m.Size = m.Rect.sizeDelta * Scale;
                m.Position = m.OnScreen ? m.Anchor + Vector2.up * (m.Faint ? lift * 0.6f : lift) : EdgePosition(m, w, h);
            }
            _visible.Sort((a, b) => a.Position.y.CompareTo(b.Position.y));
            _placed.Clear();
            foreach (var m in _visible)
            {
                if (m.OnScreen)
                {
                    bool moved = true;
                    for (int guard = 0; moved && guard < 32; guard++)
                    {
                        moved = false;
                        foreach (var other in _placed)
                        {
                            if (!Overlaps(m, other, gap))
                                continue;
                            m.Position.y = other.Position.y + other.Size.y + gap;
                            moved = true;
                        }
                    }
                }
                _placed.Add(m);
                Apply(m);
            }
        }

        private static Vector2 EdgePosition(Marker m, float w, float h)
        {
            var p = m.Anchor - new Vector2(0f, m.Size.y * 0.5f);
            p.x = Mathf.Clamp(p.x, m.Size.x * 0.5f, w - m.Size.x * 0.5f);
            p.y = Mathf.Clamp(p.y, 0f, h - m.Size.y);
            return p;
        }

        private static bool Overlaps(Marker a, Marker b, float gap)
        {
            return Mathf.Abs(a.Position.x - b.Position.x) < (a.Size.x + b.Size.x) * 0.5f + gap
                   && a.Position.y < b.Position.y + b.Size.y + gap
                   && b.Position.y < a.Position.y + a.Size.y + gap;
        }

        private void Apply(Marker m)
        {
            if (!m.Rect.gameObject.activeSelf)
                m.Rect.gameObject.SetActive(true);
            m.Rect.position = new Vector3(m.Position.x, m.Position.y, 0f);
            bool line = m.OnScreen;
            if (m.Line.gameObject.activeSelf != line)
                m.Line.gameObject.SetActive(line);
            if (!line)
                return;
            // 라벨 아래 끝 → 대상 (대상 쪽 끝에 닿게)
            var delta = m.Position - m.Anchor;
            m.Line.position = new Vector3(m.Anchor.x, m.Anchor.y, 0f);
            m.Line.sizeDelta = new Vector2(LineWidth, delta.magnitude / Scale);
            m.Line.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg);
        }

        private static int ArrowCode(string arrow)
        {
            switch (arrow)
            {
                case "→ ": return 1;
                case "← ": return 2;
                case "↑ ": return 3;
                case "↓ ": return 4;
                default: return 0;
            }
        }

        private Marker Acquire()
        {
            if (_pool.Count > 0)
                return _pool.Pop();
            // 연결선이 라벨 뒤에 그려지도록 먼저
            var lineGo = new GameObject("MarkerLine", typeof(RectTransform), typeof(Image));
            var line = (RectTransform)lineGo.transform;
            line.SetParent(_container, false);
            line.anchorMin = line.anchorMax = Vector2.zero;
            line.pivot = new Vector2(0.5f, 0f);
            var lineImage = lineGo.GetComponent<Image>();
            lineImage.raycastTarget = false;
            lineGo.SetActive(false);

            var go = new GameObject("Marker", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_container, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0f);
            var back = go.GetComponent<Image>(); // 꽉 찬 바탕 (홀로 스프라이트는 가운데가 비어 밝은 모듈 · 붉은 화면이 비쳤음)
            back.color = Background;
            back.raycastTarget = false;

            RectTransform skin = null;
            if (_ui != null)
            {
                skin = HoloUi.Rect("Skin", rect);
                HoloUi.Stretch(skin);
                _ui.Panel(skin.gameObject, new Color(0f, 0f, 0f, 0f), HudTheme.Accent); // 창과 같은 테두리
                foreach (var img in skin.GetComponentsInChildren<Image>(true))
                    img.raycastTarget = false;
            }

            var edgeGo = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            var edge = (RectTransform)edgeGo.transform;
            edge.SetParent(rect, false);
            edge.anchorMin = Vector2.zero;
            edge.anchorMax = new Vector2(0f, 1f);
            edge.pivot = new Vector2(0f, 0.5f);
            edge.sizeDelta = new Vector2(4f, 0f);
            edge.anchoredPosition = Vector2.zero;
            var edgeImage = edgeGo.GetComponent<Image>();
            edgeImage.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            var tr = (RectTransform)textGo.transform;
            tr.SetParent(rect, false);
            HoloUi.Stretch(tr, new Vector2(8f, 0f), new Vector2(-4f, 0f));
            var text = textGo.AddComponent<TextMeshProUGUI>();
            if (_font != null)
                text.font = _font;
            text.fontSize = FontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.enableWordWrapping = false;

            go.SetActive(false);
            return new Marker { Rect = rect, Text = text, Edge = edgeImage, Skin = skin, Line = line, LineImage = lineImage };
        }
    }
}
