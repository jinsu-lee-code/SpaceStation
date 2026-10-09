using System;
using SpaceStation.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 코드로 만드는 창(설정·연구)의 공용 위젯: 홀로그램 패널, 버튼, 글자. HudStyler(에디터)와 같은 모양.
    /// </summary>
    public sealed class HoloUi
    {
        public static readonly Color TextColor = new Color(0.91f, 0.96f, 1f, 1f);
        public static readonly Color MutedColor = new Color(0.68f, 0.76f, 0.85f, 1f);

        private readonly TMP_FontAsset _font;
        private readonly Sprite _fill;
        private readonly Sprite _frame;
        private readonly Sprite _button;

        public HoloUi(TMP_FontAsset font, Sprite fill, Sprite frame, Sprite button)
        {
            _font = font;
            _fill = fill;
            _frame = frame;
            _button = button;
        }

        private readonly HoloArt _art;

        /// <summary>11-13 테마 아트 묶음으로 만들기 (빛 번짐 · 주사선 · 격자까지 사용).</summary>
        public HoloUi(HoloArt art)
            : this(art != null ? art.Font : null, art != null ? art.Fill : null, art != null ? art.Frame : null, art != null ? art.Button : null)
        {
            _art = art;
        }

        public Sprite ButtonSprite => _button;
        public HoloArt Art => _art;

        // ---------------- 11-13 홀로그램 테마 ----------------

        /// <summary>
        /// 투사 화면 바탕: 깊은 바탕 + 격자 + 흐르는 주사선 + 지나가는 스캔 띠, 깜박임 · 켜짐 효과(<see cref="HoloFx"/>, 강도 0~1).
        /// rt 아래에 깔리며 다른 내용은 이 뒤에 만든다(위에 그려짐).
        /// </summary>
        public HoloFx Screen(RectTransform rt, float strength, Color back)
        {
            var bg = Rect("HoloBack", rt);
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = back;
            bgImg.raycastTarget = false;
            var size = rt.rect.size;
            if (_art != null && _art.Grid != null)
            {
                var grid = Rect("HoloGrid", rt);
                Stretch(grid);
                var g = grid.gameObject.AddComponent<RawImage>();
                g.texture = _art.Grid;
                g.color = new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.07f);
                g.uvRect = new Rect(0f, 0f, size.x / _art.Grid.width, size.y / _art.Grid.height);
                g.raycastTarget = false;
            }
            RawImage scan = null;
            if (_art != null && _art.Scan != null)
            {
                var s = Rect("HoloScan", rt);
                Stretch(s);
                scan = s.gameObject.AddComponent<RawImage>();
                scan.texture = _art.Scan;
                scan.color = new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.09f);
                scan.uvRect = new Rect(0f, 0f, 1f, size.y / _art.Scan.height / 1.5f);
                scan.raycastTarget = false;
            }
            RectTransform sweep = null;
            if (_art != null && _art.Sweep != null)
            {
                sweep = Rect("HoloSweep", rt);
                sweep.anchorMin = new Vector2(0f, 1f);
                sweep.anchorMax = new Vector2(1f, 1f);
                sweep.pivot = new Vector2(0.5f, 0.5f);
                sweep.sizeDelta = new Vector2(0f, Mathf.Max(40f, size.y * 0.22f));
                var si = sweep.gameObject.AddComponent<Image>();
                si.sprite = _art.Sweep;
                si.color = new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.08f);
                si.raycastTarget = false;
            }
            var fx = rt.gameObject.AddComponent<HoloFx>();
            fx.Strength = strength;
            fx.Scan = scan;
            fx.Sweep = sweep;
            fx.Group = rt.GetComponent<CanvasGroup>();
            if (fx.Group == null)
                fx.Group = rt.gameObject.AddComponent<CanvasGroup>();
            return fx;
        }

        /// <summary>빛 번지는 패널: 바탕 + 바깥 빛 번짐 + 테두리 · 모서리 브래킷.</summary>
        public void GlowPanel(GameObject go, Color fill, Color frame, float glow = 0.35f)
        {
            Panel(go, fill, frame);
            if (_art == null || _art.Glow == null || glow <= 0f)
                return;
            var g = Rect("Glow", go.transform);
            Stretch(g, new Vector2(-14f, -14f), new Vector2(14f, 14f));
            g.SetAsFirstSibling();
            var gi = g.gameObject.AddComponent<Image>();
            gi.sprite = _art.Glow;
            gi.type = Image.Type.Sliced;
            gi.color = new Color(frame.r, frame.g, frame.b, glow);
            gi.raycastTarget = false;
        }

        /// <summary>작은 꼬리표 (분류 · 상태): 색 바탕 + 같은 색 밝은 글자.</summary>
        public TMP_Text Chip(Transform parent, string text, Color color, float size)
        {
            var rt = Rect("Chip", parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _button;
            img.type = Image.Type.Sliced;
            img.color = new Color(color.r, color.g, color.b, 0.28f);
            img.raycastTarget = false;
            var t = Label(rt, text, size, TextAlignmentOptions.Center);
            Stretch(t.rectTransform);
            t.color = Color.Lerp(color, Color.white, 0.35f);
            return t;
        }

        /// <summary>가는 구분선 + 왼쪽 끝 굵은 눈금.</summary>
        public static RectTransform Divider(Transform parent, Vector2 topLeft, float width, Color color)
        {
            var rt = Rect("Divider", parent);
            Place(rt, topLeft, new Vector2(width, 1.5f));
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            var tick = Rect("Tick", rt);
            Place(tick, new Vector2(0f, 1.5f), new Vector2(Mathf.Min(36f, width), 4.5f));
            var ti = tick.gameObject.AddComponent<Image>();
            ti.color = new Color(color.r, color.g, color.b, Mathf.Min(1f, color.a * 2.2f));
            ti.raycastTarget = false;
            return rt;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>왼쪽 위 기준 배치.</summary>
        public static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }

        public static void Stretch(RectTransform rt, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        public TMP_Text Label(Transform parent, string text, float size, TextAlignmentOptions align, bool wrap = false)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null)
                t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.color = TextColor;
            t.alignment = align;
            t.raycastTarget = false;
            t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            t.richText = true;
            return t;
        }

        public void Panel(GameObject go, Color fill, Color frame)
        {
            var img = go.GetComponent<Image>();
            if (img == null)
                img = go.AddComponent<Image>();
            img.sprite = _fill;
            img.type = Image.Type.Sliced;
            img.color = fill;
            var f = Rect("Frame", go.transform);
            Stretch(f);
            var fi = f.gameObject.AddComponent<Image>();
            fi.sprite = _frame;
            fi.type = Image.Type.Sliced;
            fi.color = frame;
            fi.raycastTarget = false;
        }

        public Button Button(Transform parent, string label, float size, Action onClick, bool clickSound = true)
        {
            var rt = Rect(label, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = _button;
            img.type = Image.Type.Sliced;
            img.color = HudTheme.ButtonNormal;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var colors = button.colors;
            colors.normalColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.6f, 0.6f, 0.6f, 1f);
            colors.selectedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(() =>
            {
                onClick();
                EventSystem.current?.SetSelectedGameObject(null); // 선택 색이 남지 않게
            });
            var sound = rt.gameObject.AddComponent<UiSound>();
            sound.Mode = clickSound ? UiSound.ClickMode.Click : UiSound.ClickMode.Silent;
            var text = Label(rt, label, size, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            return button;
        }

        /// <summary>진행률 막대 (바탕 + 채움). 채움 비율은 반환된 Image.fillAmount 대신 앵커로 조절.</summary>
        public RectTransform Bar(Transform parent, Color back, Color fill, out RectTransform fillRect)
        {
            var bg = Rect("Bar", parent);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = back;
            bgImg.raycastTarget = false;
            fillRect = Rect("Fill", bg);
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fImg = fillRect.gameObject.AddComponent<Image>();
            fImg.color = fill;
            fImg.raycastTarget = false;
            return bg;
        }

        public static void SetBar(RectTransform fillRect, float ratio)
        {
            fillRect.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
        }
    }
}
