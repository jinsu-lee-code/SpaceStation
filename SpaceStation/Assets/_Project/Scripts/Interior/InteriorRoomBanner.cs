using SpaceStation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 방 입장 표시 (REVIEW_1115 1-9, 사용자 2026-11-15 "방에 들어갈 때 화면 가운데에 방 표시를 잠깐"): 다른 방에 들어서면 조준점 위쪽에
    /// 방 이름 + 얇은 빛 선을 띄웠다가 사라짐 (나타남 · 머묾 · 사라짐 합 약 2.8초, 실시간). 위쪽 현위치 창은 그대로.
    /// </summary>
    public sealed class InteriorRoomBanner
    {
        private const float FadeIn = 0.35f;
        private const float Hold = 1.65f;
        private const float FadeOut = 0.8f;
        /// <summary>화면 가운데 (사용자 2026-11-20 "좀 크고 중앙에") — 글자 아래 빛 선이 조준점 바로 위, 알림 줄(+120)과는 안 겹침.</summary>
        private const float CenterY = 52f;
        private const float FontSize = 64f;

        private readonly CanvasGroup _group;
        private readonly TMP_Text _name;
        private readonly RectTransform _line;
        private float _start = -999f;

        public InteriorRoomBanner(HoloUi ui, Sprite fill, Transform parent)
        {
            var root = HoloUi.Rect("RoomBanner", parent);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(0f, CenterY);
            root.sizeDelta = new Vector2(1100f, 88f);
            _group = root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _group.alpha = 0f;

            _name = ui.Label(root, "", FontSize, TextAlignmentOptions.Center);
            HoloUi.Stretch(_name.rectTransform);
            _name.fontStyle = FontStyles.Bold;
            _name.characterSpacing = 6f;
            // 밝은 벽 · 하늘 위에서도 읽히게 글자 둘레에 옅은 그림자 (이 글자만 재질 복제)
            var mat = _name.fontMaterial;
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0.02f, 0.05f, 0.85f));
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.4f);

            _line = HoloUi.Rect("Line", root);
            _line.anchorMin = _line.anchorMax = new Vector2(0.5f, 0f);
            _line.anchoredPosition = new Vector2(0f, -2f);
            var image = _line.gameObject.AddComponent<Image>();
            image.sprite = fill;
            image.color = new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.8f);
            image.raycastTarget = false;
        }

        public void Show(string name)
        {
            _name.SetText(name);
            _name.ForceMeshUpdate();
            _start = Time.unscaledTime;
            Tick();
        }

        public void Hide()
        {
            _start = -999f;
            _group.alpha = 0f;
        }

        /// <summary>매 프레임 (방 안에 있는 동안). 선은 나타나며 글자 폭까지 펼쳐짐.</summary>
        public void Tick()
        {
            float t = Time.unscaledTime - _start;
            if (t > FadeIn + Hold + FadeOut)
            {
                if (_group.alpha > 0f)
                    _group.alpha = 0f;
                return;
            }
            float alpha = t < FadeIn ? t / FadeIn : t < FadeIn + Hold ? 1f : 1f - (t - FadeIn - Hold) / FadeOut;
            _group.alpha = Mathf.SmoothStep(0f, 1f, alpha);
            float spread = Mathf.SmoothStep(0.3f, 1f, Mathf.Clamp01(t / (FadeIn * 2f)));
            _line.sizeDelta = new Vector2((_name.preferredWidth + 200f) * spread, 3f);
        }
    }
}
