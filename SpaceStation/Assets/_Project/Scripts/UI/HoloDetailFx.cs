using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 11-13 홀로그램 글자 색 번짐(RGB 어긋남): 글자 바로 뒤에 같은 글을 자홍 · 청록으로 살짝 어긋나게 깐다 (정지 — 일렁이지 않음).
    /// 큰 제목 · 이름에만 쓴다 (작은 본문은 읽기 어려워짐). 글이 바뀌면 따라 바뀐다.
    /// </summary>
    public sealed class HoloChroma : MonoBehaviour
    {
        private TMP_Text _main;
        private TMP_Text _red, _blue;
        private string _shown;

        public static HoloChroma Add(TMP_Text text, float offset = 1.6f)
        {
            var c = text.gameObject.AddComponent<HoloChroma>();
            c._main = text;
            c._red = c.Ghost(new Color(1f, 0.25f, 0.6f, 0.38f), new Vector2(-offset, 0.4f));
            c._blue = c.Ghost(new Color(0.2f, 0.75f, 1f, 0.32f), new Vector2(offset, -0.4f));
            return c;
        }

        private TMP_Text Ghost(Color color, Vector2 offset)
        {
            var rt = (RectTransform)_main.transform;
            var go = new GameObject(_main.name + "_Chroma", typeof(RectTransform));
            var g = (RectTransform)go.transform;
            g.SetParent(rt.parent, false);
            g.SetSiblingIndex(rt.GetSiblingIndex()); // 글자 뒤
            g.anchorMin = rt.anchorMin;
            g.anchorMax = rt.anchorMax;
            g.pivot = rt.pivot;
            g.sizeDelta = rt.sizeDelta;
            g.anchoredPosition = rt.anchoredPosition + offset;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = _main.font;
            t.fontSize = _main.fontSize;
            t.fontStyle = _main.fontStyle;
            t.alignment = _main.alignment;
            t.textWrappingMode = _main.textWrappingMode;
            t.overflowMode = _main.overflowMode;
            t.richText = true;
            t.raycastTarget = false;
            t.color = color;
            return t;
        }

        private void LateUpdate()
        {
            if (_main == null || _main.text == _shown)
                return;
            _shown = _main.text;
            // 색 태그는 빼고 모양만 (잔상이 원래 색을 따라가지 않게)
            string plain = System.Text.RegularExpressions.Regex.Replace(_shown, "<color=[^>]*>|</color>", "");
            _red.SetText(plain);
            _blue.SetText(plain);
        }

        private void OnDisable()
        {
            if (_red != null) _red.gameObject.SetActive(false);
            if (_blue != null) _blue.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (_red != null) _red.gameObject.SetActive(true);
            if (_blue != null) _blue.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 11-13 테크 패널 움직임: 가끔 패널 안을 위 → 아래로 지나가는 가는 빛줄기 + 사선 줄무늬 맥박. 글자 · 테두리는 그대로.
    /// </summary>
    public sealed class HoloPanelFx : MonoBehaviour
    {
        public Image Line;
        public RawImage Hatch;
        public float Period = 5f;

        private float _phase;
        private float _lineAlpha;
        private float _hatchAlpha;

        private void Awake()
        {
            _phase = Random.value * Period;
            if (Line != null)
                _lineAlpha = Line.color.a;
            if (Hatch != null)
                _hatchAlpha = Hatch.color.a;
        }

        private void Update()
        {
            float t = Time.unscaledTime + _phase;
            if (Line != null)
            {
                var parent = (RectTransform)transform;
                float h = parent.rect.height;
                float k = Mathf.Repeat(t, Period) / 0.9f; // 0.9초 동안 지나가고 나머지는 쉼
                bool on = k < 1f;
                if (Line.enabled != on)
                    Line.enabled = on;
                if (on)
                {
                    Line.rectTransform.anchoredPosition = new Vector2(0f, -k * (h - 6f) - 3f);
                    var c = Line.color;
                    c.a = _lineAlpha * Mathf.Sin(k * Mathf.PI);
                    Line.color = c;
                }
            }
            if (Hatch != null)
            {
                var c = Hatch.color;
                c.a = _hatchAlpha * (0.55f + 0.45f * Mathf.Sin(t * 2.4f));
                Hatch.color = c;
            }
        }
    }
}
