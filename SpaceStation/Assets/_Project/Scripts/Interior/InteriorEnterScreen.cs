using SpaceStation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 12-3c-opt 내부 들어가기 화면 (사용자 요청 2026-10-14: 바깥 로딩 화면처럼 퀄리티를).
    /// 검은 페이드 위: 옅은 격자 + "AIRLOCK" 꼬리표 · 들어갈 방 이름 · 상태(글자로) · 에어록 단계 문구와 지나가는 빛 막대 · 내부 팁.
    /// 내용은 페이드와 따로 서서히 나타났다 사라진다 (<see cref="Alpha"/>). InteriorMode가 들어갈 때만 쓴다.
    /// </summary>
    public sealed class InteriorEnterScreen : MonoBehaviour
    {
        private static readonly string[] Tips =
        {
            "{Pad} 키로 휴대 패드를 들면 정거장 지도에서 다른 방으로 빠르게 이동할 수 있어요.",
            "천장 · 바닥의 해치를 바라보고 {Interact} 키를 누르면 위층 · 아래층으로 옮겨 가요.",
            "파손된 방에서는 손상 지점을 {Interact} 키를 길게 눌러 직접 고칠 수 있어요.",
            "주민의 부탁을 들어주면 그 주민의 기분이 좋아져요.",
            "누출 · 차단기 · 화재 같은 사고는 제한 시간 안에 처리해야 손실을 막을 수 있어요.",
            "Shift를 누른 채 걸으면 달릴 수 있어요. ESC를 누르면 언제든 밖으로 나가요.",
            "기본 설정에서는 내부에 있는 동안 시간이 멈춰요. 설정에서 바꿀 수 있어요.",
        };

        private static readonly Color TextColor = new Color(0.91f, 0.96f, 1f, 1f);
        private static readonly Color Muted = new Color(0.68f, 0.76f, 0.85f, 1f);
        private const float BarWidth = 520f;

        private CanvasGroup _group;
        private TMP_Text _name, _state, _step, _tip;
        private RectTransform _sweep;
        private Image _barFill;
        private bool _done;
        private int _tipIndex = -1;

        public float Alpha
        {
            get => _group.alpha;
            set => _group.alpha = value;
        }

        public static InteriorEnterScreen Create(RectTransform parent, HoloArt art, TMP_FontAsset font, Sprite fill)
        {
            var root = HoloUi.Rect("EnterScreen", parent);
            HoloUi.Stretch(root);
            var screen = root.gameObject.AddComponent<InteriorEnterScreen>();
            screen._group = root.gameObject.AddComponent<CanvasGroup>();
            screen._group.blocksRaycasts = false;
            screen._group.interactable = false;
            screen.Build(root, art, font, fill);
            root.gameObject.SetActive(false);
            return screen;
        }

        private void Build(RectTransform root, HoloArt art, TMP_FontAsset font, Sprite fill)
        {
            var accent = HudTheme.Accent;
            // 옅은 격자 (홀로그램 화면 느낌)
            if (art != null && art.Grid != null)
            {
                var g = HoloUi.Rect("Grid", root);
                HoloUi.Stretch(g);
                var grid = g.gameObject.AddComponent<RawImage>();
                grid.texture = art.Grid;
                grid.color = new Color(accent.r, accent.g, accent.b, 0.05f);
                grid.uvRect = new Rect(0f, 0f, 1920f / art.Grid.width, 1080f / art.Grid.height);
                grid.raycastTarget = false;
            }

            // AIRLOCK 꼬리표 (양쪽 가는 선)
            var tag = Label(root, font, "AIRLOCK", 18f, TextAlignmentOptions.Center, new Vector2(0f, 96f), new Vector2(300f, 28f));
            tag.color = accent;
            tag.characterSpacing = 16f;
            foreach (float side in new[] { -1f, 1f })
            {
                var line = HoloUi.Rect("TagLine", root);
                line.anchorMin = line.anchorMax = line.pivot = new Vector2(0.5f, 0.5f);
                line.anchoredPosition = new Vector2(side * 190f, 96f);
                line.sizeDelta = new Vector2(120f, 2f);
                Img(line, fill, new Color(accent.r, accent.g, accent.b, 0.5f));
            }

            _name = Label(root, font, "", 46f, TextAlignmentOptions.Center, new Vector2(0f, 42f), new Vector2(1200f, 60f));
            _name.fontStyle = FontStyles.Bold;
            _name.color = TextColor;
            _state = Label(root, font, "", 24f, TextAlignmentOptions.Center, new Vector2(0f, -4f), new Vector2(1200f, 32f));
            _state.color = Muted;

            // 에어록 단계 문구 + 막대 (막대 위를 빛이 지나감, 다 되면 꽉 참)
            _step = Label(root, font, "", 22f, TextAlignmentOptions.Center, new Vector2(0f, -64f), new Vector2(800f, 30f));
            _step.color = TextColor;
            var bar = HoloUi.Rect("Bar", root);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
            bar.anchoredPosition = new Vector2(0f, -92f);
            bar.sizeDelta = new Vector2(BarWidth, 4f);
            Img(bar, fill, new Color(1f, 1f, 1f, 0.1f));
            bar.gameObject.AddComponent<RectMask2D>();
            _sweep = HoloUi.Rect("Sweep", bar);
            _sweep.anchorMin = _sweep.anchorMax = _sweep.pivot = new Vector2(0f, 0.5f);
            _sweep.sizeDelta = new Vector2(140f, 4f);
            Img(_sweep, fill, accent);
            var fillRt = HoloUi.Rect("Fill", bar);
            HoloUi.Stretch(fillRt);
            _barFill = Img(fillRt, fill, accent);
            _barFill.type = Image.Type.Filled;
            _barFill.fillMethod = Image.FillMethod.Horizontal;
            _barFill.fillAmount = 0f;
            if (_barFill.sprite == null)
                _barFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero); // Filled는 스프라이트가 있어야 채움

            // 아래 팁
            var tipRow = HoloUi.Rect("Tip", root);
            tipRow.anchorMin = tipRow.anchorMax = tipRow.pivot = new Vector2(0.5f, 0f);
            tipRow.anchoredPosition = new Vector2(0f, 84f);
            tipRow.sizeDelta = new Vector2(960f, 64f);
            var chip = HoloUi.Rect("Chip", tipRow);
            chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(0f, 1f);
            chip.sizeDelta = new Vector2(64f, 32f);
            Img(chip, fill, new Color(accent.r, accent.g, accent.b, 0.22f));
            var chipText = Label(chip, font, "TIP", 18f, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            HoloUi.Stretch(chipText.rectTransform);
            chipText.color = accent;
            chipText.fontStyle = FontStyles.Bold;
            chipText.characterSpacing = 6f;
            _tip = Label(tipRow, font, "", 23f, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.zero);
            HoloUi.Stretch(_tip.rectTransform, new Vector2(84f, 0f), new Vector2(0f, -2f));
            _tip.textWrappingMode = TextWrappingModes.Normal;
            _tip.color = TextColor;
        }

        /// <param name="state">상태 글 (정상 · 비활성 · 파손 — 색만이 아니라 글자로)</param>
        public void Show(string roomName, string state)
        {
            _done = false;
            _name.SetText(roomName);
            _state.SetText(state);
            _step.SetText("기압 맞추는 중");
            _barFill.fillAmount = 0f;
            _sweep.gameObject.SetActive(true);
            _tipIndex = (_tipIndex + 1 + Random.Range(0, Tips.Length - 1)) % Tips.Length;
            _tip.SetText(LoadingTips.Format(Tips[_tipIndex]));
            _group.alpha = 0f;
            gameObject.SetActive(true);
        }

        /// <summary>다 지었음: 막대를 채우고 "문 여는 중".</summary>
        public void Complete()
        {
            _done = true;
            _step.SetText("해치 여는 중");
            _sweep.gameObject.SetActive(false);
        }

        public void Hide() => gameObject.SetActive(false);

        private void Update()
        {
            float t = Time.unscaledTime;
            if (!_done)
            {
                // 빛이 막대를 왼쪽 → 오른쪽으로 계속 지나감 + 점 애니메이션
                float x = Mathf.Repeat(t * 1.4f, 1f) * (BarWidth + 140f) - 140f;
                _sweep.anchoredPosition = new Vector2(x, 0f);
                _step.SetText("기압 맞추는 중" + new string('.', 1 + (int)(t * 2.5f) % 3));
            }
            else
                _barFill.fillAmount = Mathf.MoveTowards(_barFill.fillAmount, 1f, Time.unscaledDeltaTime * 4f);
        }

        private static TMP_Text Label(Transform parent, TMP_FontAsset font, string text, float size, TextAlignmentOptions align, Vector2 position, Vector2 sizeDelta)
        {
            var rt = HoloUi.Rect("Text", parent);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = sizeDelta;
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null)
                t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.SetText(text);
            return t;
        }

        private static Image Img(RectTransform rt, Sprite sprite, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
