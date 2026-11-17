using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 체험 · 미니게임 안내 줄 (화면 아래 가운데 반투명 띠 한 줄 — 시냅스 따라 켜기 · 드론 기억 등과 같은 모습, 2026-11-17 심우주 통신기지부터 같이 씀).
    /// 같은 글이면 다시 그리지 않음, null = 숨김. 처음 보일 때 만듦.
    /// </summary>
    public sealed class InteriorHintLine
    {
        private readonly Transform _parent;
        private readonly string _name;
        private readonly TMP_FontAsset _font;
        private Canvas _canvas;
        private TextMeshProUGUI _text;
        private string _shown;

        public InteriorHintLine(Transform parent, string name, TMP_FontAsset font)
        {
            _parent = parent;
            _name = name;
            _font = font;
        }

        /// <summary>지금 보이는 글 (없으면 null — 시험용).</summary>
        public string Text => _shown;

        public void Show(string text)
        {
            if (text == _shown)
                return;
            _shown = text;
            if (text == null)
            {
                if (_canvas != null)
                    _canvas.gameObject.SetActive(false);
                return;
            }
            Ensure();
            _canvas.gameObject.SetActive(true);
            _text.SetText(text);
        }

        public void Destroy()
        {
            if (_canvas != null)
                Object.Destroy(_canvas.gameObject);
        }

        private void Ensure()
        {
            if (_canvas != null)
                return;
            var go = new GameObject(_name, typeof(RectTransform));
            go.transform.SetParent(_parent, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 300;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var back = new GameObject("Back", typeof(RectTransform)).AddComponent<Image>();
            back.transform.SetParent(go.transform, false);
            Place(back.rectTransform, new Vector2(1000f, 92f));
            back.color = new Color(0.03f, 0.05f, 0.1f, 0.62f);
            back.raycastTarget = false;
            _text = new GameObject("Hint", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _text.transform.SetParent(go.transform, false);
            Place(_text.rectTransform, new Vector2(980f, 84f));
            if (_font != null)
                _text.font = _font;
            _text.fontSize = 26f;
            _text.enableAutoSizing = true;
            _text.fontSizeMin = 15f;
            _text.fontSizeMax = 26f;
            _text.color = new Color(0.92f, 0.96f, 1f);
            _text.alignment = TextAlignmentOptions.Center;
            _text.raycastTarget = false;
        }

        private static void Place(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, 160f);
            rt.sizeDelta = size;
        }
    }
}
