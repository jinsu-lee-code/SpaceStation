using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 11-13 테크 버튼 디테일 (<see cref="HoloUi.TechButton"/>가 붙임, 사용자 레퍼런스 2026-10-10):
    /// - 호버: 바깥 빛 번짐이 커지고, 빛줄기가 버튼을 한 번 가로지르고, 바깥 ㄱ자 모서리 브래킷 네 개가 살짝 벌어짐
    /// - 누름: 하얗게 번쩍 + 글자가 아주 짧게 지지직 (0.1초)
    /// - 비활성: 사선 줄무늬가 덮이고 브래킷 · 하이라이트가 흐려짐
    /// - 위 얇은 하이라이트 선 + 아래 상태 막대 (가능 청록 · 비활성 회색 · <see cref="Status"/>로 바꿈, 예: 자원 부족 노랑)
    /// - 단축키 배지: 버튼 글의 " (F)" 같은 키 표시를 오른쪽 위 작은 상자로 옮김 (글이 바뀔 때마다 자동)
    /// 글자 자체는 누를 때 말고는 움직이지 않는다 (일렁임 방지).
    /// </summary>
    public sealed class HoloButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        private static readonly Regex KeyPattern = new Regex(@"\s\(([^)\s]{1,6})\)");
        private static readonly Color Dim = new Color(0.45f, 0.52f, 0.6f, 1f);
        private static readonly Vector2[] Signs = { new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f) };

        /// <summary>상태 막대 색 덮어쓰기 (null = 가능 청록 · 비활성 회색).</summary>
        public Color? Status;

        private Button _button;
        private TMP_Text _label;
        private RectTransform _rt;
        private Image _glow, _shine, _flash, _highlight, _bar, _badge;
        private TMP_Text _badgeText;
        private RawImage _hatch;
        private readonly RectTransform[] _brackets = new RectTransform[4];
        private readonly Image[] _bracketImages = new Image[4];
        private Vector2 _labelHome;
        private string _seenText;
        private float _hover;     // 0~1 부드럽게
        private bool _pointerIn;
        private float _shineT = 1f;
        private float _flashT;

        public static HoloButtonFx Add(Button button, HoloArt art)
        {
            var fx = button.gameObject.AddComponent<HoloButtonFx>();
            fx.Build(button, art);
            return fx;
        }

        private void Build(Button button, HoloArt art)
        {
            _button = button;
            _rt = (RectTransform)button.transform;
            _label = button.GetComponentInChildren<TMP_Text>(true);
            var accent = HudTheme.Accent;

            if (art != null && art.Glow != null)
            {
                var g = HoloUi.Rect("BtnGlow", _rt);
                HoloUi.Stretch(g, new Vector2(-10f, -10f), new Vector2(10f, 10f));
                g.SetAsFirstSibling();
                _glow = g.gameObject.AddComponent<Image>();
                _glow.sprite = art.Glow;
                _glow.type = Image.Type.Sliced;
                _glow.color = new Color(accent.r, accent.g, accent.b, 0f);
                _glow.raycastTarget = false;
            }

            // 버튼 안쪽 (잘라 그림): 비활성 줄무늬 · 빛줄기 · 번쩍임
            var clip = HoloUi.Rect("BtnClip", _rt);
            HoloUi.Stretch(clip, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            clip.gameObject.AddComponent<RectMask2D>();
            if (_label != null)
                clip.SetSiblingIndex(_label.transform.GetSiblingIndex()); // 글자 뒤
            if (art != null && art.Hatch != null)
            {
                var h = HoloUi.Rect("BtnHatch", clip);
                HoloUi.Stretch(h);
                _hatch = h.gameObject.AddComponent<RawImage>();
                _hatch.texture = art.Hatch;
                _hatch.raycastTarget = false;
                _hatch.color = new Color(Dim.r, Dim.g, Dim.b, 0f);
            }
            if (art != null && art.Sweep != null)
            {
                var s = HoloUi.Rect("BtnShine", clip);
                s.anchorMin = s.anchorMax = new Vector2(0f, 0.5f);
                s.pivot = new Vector2(0.5f, 0.5f);
                s.localRotation = Quaternion.Euler(0f, 0f, -90f); // 세로 그러데이션 → 가로로 지나가는 띠
                _shine = s.gameObject.AddComponent<Image>();
                _shine.sprite = art.Sweep;
                _shine.color = new Color(accent.r, accent.g, accent.b, 0f);
                _shine.raycastTarget = false;
            }
            var fl = HoloUi.Rect("BtnFlash", clip);
            HoloUi.Stretch(fl);
            _flash = fl.gameObject.AddComponent<Image>();
            _flash.color = new Color(1f, 1f, 1f, 0f);
            _flash.raycastTarget = false;

            // 위 하이라이트 선 · 아래 상태 막대
            var hl = HoloUi.Rect("BtnHighlight", _rt);
            hl.anchorMin = new Vector2(0f, 1f);
            hl.anchorMax = new Vector2(1f, 1f);
            hl.pivot = new Vector2(0.5f, 1f);
            hl.offsetMin = new Vector2(14f, -5.5f);
            hl.offsetMax = new Vector2(-10f, -4f);
            _highlight = hl.gameObject.AddComponent<Image>();
            _highlight.raycastTarget = false;
            var bar = HoloUi.Rect("BtnStatus", _rt);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0f, 0f);
            bar.anchoredPosition = new Vector2(8f, 4f);
            bar.sizeDelta = new Vector2(26f, 3f);
            _bar = bar.gameObject.AddComponent<Image>();
            _bar.raycastTarget = false;

            // 모서리 브래킷 (버튼 바깥, 왼쪽 위 → 시계 방향)
            if (art != null && art.Bracket != null)
            {
                var anchors = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 0f) };
                for (int i = 0; i < 4; i++)
                {
                    var b = HoloUi.Rect("Bracket", _rt);
                    b.anchorMin = b.anchorMax = anchors[i];
                    b.pivot = new Vector2(0.5f, 0.5f);
                    b.sizeDelta = new Vector2(12f, 12f);
                    b.localRotation = Quaternion.Euler(0f, 0f, -90f * i);
                    var img = b.gameObject.AddComponent<Image>();
                    img.sprite = art.Bracket;
                    img.raycastTarget = false;
                    _brackets[i] = b;
                    _bracketImages[i] = img;
                }
            }

            // 단축키 배지 (오른쪽 위)
            var badge = HoloUi.Rect("KeyBadge", _rt);
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 1f);
            badge.anchoredPosition = new Vector2(-4f, -4f);
            badge.sizeDelta = new Vector2(19f, 15f);
            _badge = badge.gameObject.AddComponent<Image>();
            _badge.sprite = art != null ? art.TechButton : null;
            _badge.type = Image.Type.Sliced;
            _badge.pixelsPerUnitMultiplier = 3f; // 작은 상자라 모서리를 작게
            _badge.color = new Color(accent.r, accent.g, accent.b, 0.55f);
            _badge.raycastTarget = false;
            var bt = HoloUi.Rect("Key", badge);
            HoloUi.Stretch(bt);
            _badgeText = bt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_label != null)
                _badgeText.font = _label.font;
            _badgeText.fontSize = 10f;
            _badgeText.alignment = TextAlignmentOptions.Center;
            _badgeText.color = new Color(0.9f, 0.98f, 1f, 1f);
            _badgeText.raycastTarget = false;
            badge.gameObject.SetActive(false);

            if (_label != null)
                _labelHome = _label.rectTransform.anchoredPosition;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _pointerIn = true;
            if (_button.IsInteractable())
                _shineT = 0f; // 빛줄기 한 번
        }

        public void OnPointerExit(PointerEventData eventData) => _pointerIn = false;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_button.IsInteractable())
                _flashT = 1f;
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            bool on = _button.IsInteractable();
            _hover = Mathf.MoveTowards(_hover, _pointerIn && on ? 1f : 0f, dt * 7f);
            var accent = HudTheme.Accent;

            MoveKeyToBadge();

            if (_glow != null)
                _glow.color = new Color(accent.r, accent.g, accent.b, (on ? Mathf.Lerp(0.1f, 0.5f, _hover) : 0.03f));
            if (_hatch != null)
            {
                var size = _rt.rect.size;
                _hatch.uvRect = new Rect(0f, 0f, size.x / 12f, size.y / 12f);
                _hatch.color = new Color(Dim.r, Dim.g, Dim.b, on ? 0f : 0.22f);
            }
            if (_shine != null)
            {
                float w = _rt.rect.width, h = _rt.rect.height;
                _shine.rectTransform.sizeDelta = new Vector2(h * 1.4f, 34f); // 돌려서 (34 × 높이)
                if (_shineT < 1f)
                {
                    _shineT = Mathf.MoveTowards(_shineT, 1f, dt / 0.35f);
                    _shine.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-30f, w + 30f, _shineT), 0f);
                    _shine.color = new Color(accent.r, accent.g, accent.b, 0.35f * Mathf.Sin(_shineT * Mathf.PI));
                }
                else if (_shine.color.a > 0f)
                    _shine.color = new Color(accent.r, accent.g, accent.b, 0f);
            }
            if (_flashT > 0f)
            {
                _flashT = Mathf.MoveTowards(_flashT, 0f, dt / 0.15f);
                _flash.color = new Color(1f, 1f, 1f, 0.5f * _flashT);
                if (_label != null)
                    _label.rectTransform.anchoredPosition = _labelHome + (_flashT > 0.4f ? new Vector2(Random.Range(-1.5f, 1.5f), 0f) : Vector2.zero);
            }
            _highlight.color = new Color(accent.r, accent.g, accent.b, on ? Mathf.Lerp(0.45f, 0.9f, _hover) : 0.12f);
            var status = Status ?? (on ? accent : Dim);
            _bar.color = new Color(status.r, status.g, status.b, on ? 0.95f : 0.45f);

            float gap = Mathf.Lerp(2f, 5.5f, _hover);
            for (int i = 0; i < 4; i++)
            {
                if (_brackets[i] == null)
                    continue;
                // 브래킷 ㄱ자 꼭짓점이 버튼 모서리에서 gap만큼 바깥 (브래킷 크기 12 → 가운데는 꼭짓점에서 6 안쪽)
                _brackets[i].anchoredPosition = new Vector2(Signs[i].x * (gap - 6f), Signs[i].y * (gap - 6f));
                _bracketImages[i].color = new Color(accent.r, accent.g, accent.b, on ? Mathf.Lerp(0.4f, 1f, _hover) : 0.12f);
            }
        }

        /// <summary>버튼 글의 " (F)" 같은 키 표시를 배지로 (글이 바뀔 때만).</summary>
        private void MoveKeyToBadge()
        {
            if (_label == null || _label.text == _seenText)
                return;
            string text = _label.text;
            var m = KeyPattern.Match(text);
            bool has = m.Success;
            if (has)
            {
                text = text.Remove(m.Index, m.Length);
                _label.SetText(text);
                _badgeText.SetText(m.Groups[1].Value);
                // 긴 키 이름(Space · Shift 등)은 배지를 옆으로 늘림 (11-15: 'Space'가 두 줄로 깨졌음)
                _badgeText.textWrappingMode = TextWrappingModes.NoWrap;
                float w = Mathf.Max(19f, _badgeText.GetPreferredValues(m.Groups[1].Value).x + 8f);
                _badge.rectTransform.sizeDelta = new Vector2(w, 15f);
            }
            _seenText = text;
            if (_badge.gameObject.activeSelf != has)
                _badge.gameObject.SetActive(has);
        }
    }
}
