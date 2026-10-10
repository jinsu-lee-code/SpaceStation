using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 11-15 홀로그램 켜짐 · 꺼짐 효과 (사용자 레퍼런스: SF 영화의 홀로그램 투사): 창 주변에 흩어진 가로 빛줄기 · 자홍/청록 잔상 테두리 ·
    /// 가로 찢김 띠 · 노이즈 주사선이 순간적으로 창 자리로 모이며 사라진다 (꺼질 때는 반대로 흩어짐).
    /// uGUI는 창 내용을 복사해 조각낼 수 없으므로 창 위에 덧그리는 층으로 표현한다. 창의 투명도와 무관하게 보이도록 부모 CanvasGroup을 무시.
    /// <see cref="UiTween"/>(Glitch)이 나타날 때 · 사라질 때 <see cref="Play"/>를 부른다.
    /// </summary>
    public sealed class HoloGlitch : MonoBehaviour
    {
        private const int StreakCount = 18;
        private const int TearCount = 5;
        private const float TearStep = 0.04f;

        private static readonly Color Magenta = new Color(1f, 0.3f, 0.75f, 1f);
        private static readonly Color White = new Color(0.9f, 0.98f, 1f, 1f);

        private sealed class Streak
        {
            public RectTransform Rt;
            public Image Img;
            public Vector2 From, To;   // 흩어진 자리 → 모인 자리 (창 기준, 왼쪽 위 원점)
            public float WidthFrom, WidthTo;
            public float Alpha;
        }

        private HoloArt _art;
        private RectTransform _target;
        private RectTransform _layer;
        private Image _ghostA, _ghostB;
        private RawImage _noise;
        private readonly List<Streak> _streaks = new List<Streak>();
        private readonly List<Image> _tears = new List<Image>();
        private float _start = -1f;
        private float _duration;
        private bool _appearing;
        private float _nextTear;

        public static HoloGlitch Add(RectTransform target, HoloArt art)
        {
            if (target == null || art == null)
                return null; // 테마 아트가 없으면 흔들림 · 깜박임만 (UiTween)
            var g = target.GetComponent<HoloGlitch>();
            if (g == null)
                g = target.gameObject.AddComponent<HoloGlitch>();
            g._art = art;
            g._target = target;
            return g;
        }

        /// <param name="appearing">true = 흩어진 상태에서 모이며 켜짐, false = 모인 상태에서 흩어지며 꺼짐</param>
        public void Play(bool appearing, float duration)
        {
            Build();
            Scatter();
            _appearing = appearing;
            _duration = Mathf.Max(0.05f, duration);
            _start = Time.unscaledTime;
            _nextTear = 0f;
            _layer.gameObject.SetActive(true);
            _layer.SetAsLastSibling(); // 창 내용 위
            Apply(0f);
        }

        private void LateUpdate()
        {
            if (_start < 0f)
                return;
            float k = (Time.unscaledTime - _start) / _duration;
            if (k >= 1f)
            {
                _start = -1f;
                _layer.gameObject.SetActive(false);
                return;
            }
            Apply(k);
        }

        private void OnDisable()
        {
            _start = -1f;
            if (_layer != null)
                _layer.gameObject.SetActive(false);
        }

        // ---------------- 구성 ----------------

        private void Build()
        {
            if (_layer != null)
                return;
            _layer = HoloUi.Decorative(HoloUi.Rect("HoloGlitch", _target));
            HoloUi.Stretch(_layer);
            var group = _layer.gameObject.AddComponent<CanvasGroup>();
            group.ignoreParentGroups = true; // 창이 아직 투명할 때도 보이게
            group.blocksRaycasts = false;
            group.interactable = false;

            // 색 번짐 잔상 테두리 (양옆에서 모여 창 테두리와 겹침)
            _ghostA = Ghost("GhostMagenta", Magenta);
            _ghostB = Ghost("GhostCyan", HudTheme.Accent);

            // 노이즈 주사선 (창 전체를 잠깐 덮음)
            if (_art != null && _art.Scan != null)
            {
                var n = HoloUi.Rect("Noise", _layer);
                HoloUi.Stretch(n);
                _noise = n.gameObject.AddComponent<RawImage>();
                _noise.texture = _art.Scan;
                _noise.raycastTarget = false;
            }

            // 가로 찢김 띠 (창 가로로 어긋난 조각처럼)
            for (int i = 0; i < TearCount; i++)
            {
                var t = HoloUi.Rect("Tear", _layer);
                t.anchorMin = t.anchorMax = t.pivot = new Vector2(0f, 1f);
                var img = t.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                _tears.Add(img);
            }

            // 흩어진 빛줄기
            for (int i = 0; i < StreakCount; i++)
            {
                var rt = HoloUi.Rect("Streak", _layer);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                var img = rt.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                _streaks.Add(new Streak { Rt = rt, Img = img });
            }
            _layer.gameObject.SetActive(false);
        }

        private Image Ghost(string name, Color color)
        {
            var g = HoloUi.Rect(name, _layer);
            HoloUi.Stretch(g);
            var img = g.gameObject.AddComponent<Image>();
            img.sprite = _art != null ? (_art.TechFrame != null ? _art.TechFrame : _art.Frame) : null;
            img.type = Image.Type.Sliced;
            img.color = new Color(color.r, color.g, color.b, 0f);
            img.raycastTarget = false;
            return img;
        }

        /// <summary>창 크기에 맞춰 이번 회차의 빛줄기 · 찢김 자리를 새로 뽑음.</summary>
        private void Scatter()
        {
            var size = _target.rect.size;
            float w = Mathf.Max(80f, size.x), h = Mathf.Max(40f, size.y);
            foreach (var s in _streaks)
            {
                float y = -Random.Range(0.04f, 0.96f) * h;
                float toX = Random.Range(0.15f, 0.85f) * w;
                s.To = new Vector2(toX, y);
                // 흩어진 자리: 같은 높이 근처, 가로로 창 밖까지 크게 벗어남
                s.From = new Vector2(toX + Random.Range(-0.7f, 0.7f) * w, y + Random.Range(-18f, 18f));
                s.WidthFrom = Random.Range(0.25f, 0.9f) * w;
                s.WidthTo = Random.Range(0.02f, 0.12f) * w;
                s.Rt.sizeDelta = new Vector2(s.WidthFrom, Random.value < 0.75f ? 1.5f : 3f);
                float r = Random.value;
                var c = r < 0.5f ? HudTheme.Accent : r < 0.78f ? White : Magenta;
                s.Alpha = Random.Range(0.35f, 0.85f);
                s.Img.color = new Color(c.r, c.g, c.b, 0f);
            }
        }

        // ---------------- 움직임 ----------------

        private void Apply(float k)
        {
            // p: 0 = 흩어짐 → 1 = 모임 (꺼질 때는 거꾸로)
            float p = _appearing ? k : 1f - k;
            float gather = 1f - Mathf.Pow(1f - p, 3f);  // 빠르게 모였다가 마지막에 딱 붙음
            float fade = 1f - p;                        // 모일수록 사라짐
            var size = _target.rect.size;
            float flicker = Random.value < 0.25f ? 0.35f : 1f;

            float ghost = Mathf.Lerp(48f, 0f, gather);
            SetGhost(_ghostA, -ghost, 0.75f * fade * flicker);
            SetGhost(_ghostB, ghost, 0.75f * fade * flicker);

            foreach (var s in _streaks)
            {
                s.Rt.anchoredPosition = Vector2.Lerp(s.From, s.To, gather);
                s.Rt.sizeDelta = new Vector2(Mathf.Lerp(s.WidthFrom, s.WidthTo, gather), s.Rt.sizeDelta.y);
                var c = s.Img.color;
                c.a = s.Alpha * fade * (Random.value < 0.2f ? 0.2f : 1f);
                s.Img.color = c;
            }

            if (_noise != null)
            {
                var accent = HudTheme.Accent;
                _noise.color = new Color(accent.r, accent.g, accent.b, 0.28f * fade * flicker); // 첫 프레임에 창 전체가 청록으로 덮여 보여 약하게
                _noise.uvRect = new Rect(0f, Random.value, 1f, Mathf.Max(1f, size.y) / _noise.texture.height / 1.2f);
            }

            float elapsed = Time.unscaledTime - _start;
            if (elapsed >= _nextTear)
            {
                _nextTear = elapsed + TearStep;
                foreach (var t in _tears)
                {
                    bool on = Random.value < 0.7f * fade + 0.05f;
                    if (!on)
                    {
                        t.color = Color.clear;
                        continue;
                    }
                    float th = Random.Range(3f, 14f);
                    t.rectTransform.sizeDelta = new Vector2(size.x * Random.Range(0.4f, 1.1f), th);
                    t.rectTransform.anchoredPosition = new Vector2(Random.Range(-40f, 40f) * fade, -Random.Range(0f, Mathf.Max(1f, size.y - th)));
                    var c = Random.value < 0.6f ? HudTheme.Accent : Magenta;
                    t.color = new Color(c.r, c.g, c.b, Random.Range(0.07f, 0.18f) * fade);
                }
            }
        }

        private static void SetGhost(Image g, float dx, float alpha)
        {
            var rt = g.rectTransform;
            rt.offsetMin = new Vector2(dx, 0f);
            rt.offsetMax = new Vector2(dx, 0f);
            var c = g.color;
            c.a = alpha;
            g.color = c;
        }
    }
}
