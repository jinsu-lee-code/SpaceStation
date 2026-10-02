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

        public Sprite ButtonSprite => _button;

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
