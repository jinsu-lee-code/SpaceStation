using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 11-15 씬에 미리 놓인 바깥 HUD(패널 · 버튼 · 프리팹)에 패드와 같은 테크 홀로그램 모양을 입힌다.
    /// 설정(종류 · 제목 · 세기)은 에디터(SpaceStation/UI/Apply Holo Style)가 붙여 저장하고, 장식 자식은 실행할 때 <see cref="HoloUi"/>로 만든다
    /// (HoloButtonFx · HoloPanelFx는 실행 중에 만든 자식을 참조하므로 씬에 저장하지 않음).
    /// 레이아웃 그룹 안에 만들어지는 장식은 레이아웃에서 뺀다.
    /// </summary>
    public sealed class HoloSkin : MonoBehaviour
    {
        public enum SkinKind { Panel, Button }

        public HoloArt Art;
        public SkinKind Kind;
        [Tooltip("패널 왼쪽 위 작은 제목 (영문 대문자 권장, 비우면 없음)")]
        public string Title;
        [Range(0f, 1f)] public float Glow = 0.22f;
        [Tooltip("테두리 색 (알파 0이면 청록 기본)")]
        public Color Line = new Color(0f, 0f, 0f, 0f);
        [Tooltip("주사선 · 스캔 띠 세기 (0 = 없음, 바깥 HUD는 0.3 안팎)")]
        [Range(0f, 1f)] public float Scan = 0.3f;
        [Tooltip("회로 선 길이 (px, 0이면 없음)")]
        public float Connector;

        /// <summary>새 테크 테두리 (패널, 없으면 null) — 이벤트 배너처럼 테두리 색을 바꾸는 쪽이 씀.</summary>
        public Image Frame { get; private set; }
        public HoloButtonFx ButtonFx { get; private set; }

        private Image _glow;
        private RawImage _hatch;
        private TMP_Text _title;
        private bool _built;

        private void Awake() => Build();

        public void Build()
        {
            if (_built)
                return;
            _built = true;
            if (Kind == SkinKind.Button)
                BuildButton();
            else
                BuildPanel();
        }

        /// <summary>테두리 · 빛 번짐 · 줄무늬 · 제목 색을 한꺼번에 (이벤트 위험도 등).</summary>
        public void SetLine(Color c)
        {
            if (Frame != null)
                Frame.color = c;
            if (_glow != null)
                _glow.color = new Color(c.r, c.g, c.b, Glow);
            if (_hatch != null)
                _hatch.color = new Color(c.r, c.g, c.b, c.a * 0.7f);
            if (_title != null)
                _title.color = Color.Lerp(c, Color.white, 0.25f);
        }

        private void BuildButton()
        {
            var button = GetComponent<Button>();
            if (button == null)
                return;
                        if (Art != null && Art.TechButton != null && button.targetGraphic is Image img)
            {
                img.sprite = Art.TechButton;
                img.type = Image.Type.Sliced;
            }
            HoloUi.Glow(button.GetComponentInChildren<TMP_Text>(true), 0.3f);
            ButtonFx = HoloButtonFx.Add(button, Art);
            IgnoreLayout();
        }

        private void BuildPanel()
        {
            var ui = new HoloUi(Art) { PanelLine = false }; // 바깥 HUD는 지나가는 빛줄기 없음 (눈이 끌림)
            var img = GetComponent<Image>();
            Color fill = img != null ? img.color : HudTheme.PanelFill;
            Color line = Line.a > 0f ? Line : HudTheme.AccentDim;
            // 5-8 옛 테두리(Frame)는 끈다 (새 테크 테두리로 대신)
            var old = transform.Find("Frame");
            if (old != null)
            {
                old.gameObject.SetActive(false);
                old.name = "Frame_Old";
            }
            _title = ui.TechPanel(gameObject, fill, line, string.IsNullOrEmpty(Title) ? null : Title, Glow, Connector);
            if (_title != null)
                _title.name = "PanelTitle"; // 내용 글자("Text")와 구분
            var f = transform.Find("Frame");
            Frame = f != null ? f.GetComponent<Image>() : null;
            var g = transform.Find("Glow");
            _glow = g != null ? g.GetComponent<Image>() : null;
            var h = transform.Find("Hatch");
            _hatch = h != null ? h.GetComponent<RawImage>() : null;
            if (Scan > 0f)
                ui.Decor((RectTransform)transform, Scan, grid: false, inset: 4f); // 은은한 주사선 · 스캔 띠 (격자 없음)
            IgnoreLayout();
            // 제목이 있으면 첫 줄이 제목에 가리지 않게 위 여백 확보
            var layout = GetComponent<LayoutGroup>();
            if (_title != null && layout != null && layout.padding.top < 24)
                layout.padding.top = 24;
        }

        /// <summary>새로 만든 장식 자식은 레이아웃 그룹이 줄 세우지 않게.</summary>
        private void IgnoreLayout()
        {
            if (GetComponent<LayoutGroup>() == null)
                return;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name == "Frame_Old")
                    continue;
                // 새 자식은 SetAsFirstSibling 등으로 앞에 끼어들 수 있으므로 이름으로 가려냄
                if (!IsDecor(child.name))
                    continue;
                var le = child.GetComponent<LayoutElement>();
                if (le == null)
                    le = child.gameObject.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
            }
        }

        private static bool IsDecor(string name)
        {
            switch (name)
            {
                case "Glow": case "Frame": case "Hatch": case "PanelScan": case "Connector": case "PanelTitle": case "HoloDecor":
                case "BtnGlow": case "BtnClip": case "BtnHighlight": case "BtnStatus": case "Bracket": case "KeyBadge":
                    return true;
                default:
                    return false;
            }
        }
    }
}
