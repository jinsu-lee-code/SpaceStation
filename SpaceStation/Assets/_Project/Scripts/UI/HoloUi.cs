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
        /// <param name="tech">11-15 ② 바깥 창: <see cref="Panel"/> · <see cref="Button"/>도 테크 패널 · 테크 버튼으로</param>
        public HoloUi(HoloArt art, bool tech = false)
            : this(art != null ? art.Font : null, art != null ? art.Fill : null, art != null ? art.Frame : null, art != null ? art.Button : null)
        {
            _art = art;
            _tech = tech && art != null;
        }

        private readonly bool _tech;

        /// <summary>11-15 ② 창 아트가 있으면 테크 테마, 없으면 5-8 기본 스프라이트로.</summary>
        public static HoloUi For(HoloArt art, TMP_FontAsset font, Sprite fill, Sprite frame, Sprite button)
            => art != null ? new HoloUi(art, tech: true) : new HoloUi(font, fill, frame, button);

        // ---------------- 11-15 ② 바깥 창 ----------------

        /// <summary>
        /// 창 바탕: 테크 패널(왼쪽 위 작은 영문 꼬리표) + 안쪽 장식(격자 · 주사선 · 스캔 띠 · 장식만 깜박임).
        /// 창을 열 때 반환된 HoloFx의 Replay()로 스캔 띠가 한 번 빠르게 지나감. 켜짐 효과(투명도)는 창의 UiTween이 맡으므로 쓰지 않음.
        /// 테크 그림이 없으면 기본 패널.
        /// </summary>
        public HoloFx Window(GameObject go, Color fill, string tag, float strength = 0.45f)
        {
            if (!_tech)
            {
                PlainPanel(go, fill, HudTheme.Accent);
                return null;
            }
            var accent = HudTheme.Accent;
            TechPanel(go, fill, new Color(accent.r, accent.g, accent.b, 0.85f), tag, 0.3f, 0f);
            return Decor((RectTransform)go.transform, strength, grid: true, inset: 8f);
        }

        /// <summary>
        /// 패널 안쪽 장식: (격자) + 흐르는 주사선 + 지나가는 스캔 띠, 장식만 깜박임 (글자 · 버튼은 그대로). 켜짐 효과 없음.
        /// 바탕 · 빛 번짐 바로 위, 내용 뒤에 깔린다. 11-15 ① HoloSkin(바깥 HUD)과 ② 창이 같이 씀.
        /// </summary>
        public HoloFx Decor(RectTransform rt, float strength, bool grid, float inset)
        {
            if (_art == null || _art.Scan == null || strength <= 0f)
                return null;
            var decor = Decorative(Rect("HoloDecor", rt));
            Stretch(decor, new Vector2(inset, inset), new Vector2(-inset, -inset));
            var glow = rt.Find("Glow");
            decor.SetSiblingIndex(glow != null ? glow.GetSiblingIndex() + 1 : 0);
            var group = decor.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            var accent = HudTheme.Accent;
            var size = rt.rect.size;
            if (grid && _art.Grid != null)
            {
                var gr = Rect("HoloGrid", decor);
                Stretch(gr);
                var g = gr.gameObject.AddComponent<RawImage>();
                g.texture = _art.Grid;
                g.color = new Color(accent.r, accent.g, accent.b, 0.05f);
                g.uvRect = new Rect(0f, 0f, Mathf.Max(1f, size.x) / _art.Grid.width, Mathf.Max(1f, size.y) / _art.Grid.height);
                g.raycastTarget = false;
            }
            var s = Rect("HoloScan", decor);
            Stretch(s);
            var scan = s.gameObject.AddComponent<RawImage>();
            scan.texture = _art.Scan;
            scan.color = new Color(accent.r, accent.g, accent.b, 0.09f);
            // 높이가 레이아웃으로 정해지는 패널은 0 → 대략값 (가는 줄 반복)
            scan.uvRect = new Rect(0f, 0f, 1f, size.y > 1f ? size.y / _art.Scan.height / 1.5f : 6f);
            scan.raycastTarget = false;
            RectTransform sweep = null;
            if (_art.Sweep != null)
            {
                sweep = Rect("HoloSweep", decor);
                sweep.anchorMin = new Vector2(0f, 1f);
                sweep.anchorMax = new Vector2(1f, 1f);
                sweep.pivot = new Vector2(0.5f, 0.5f);
                sweep.sizeDelta = new Vector2(0f, Mathf.Max(40f, size.y * 0.18f));
                var si = sweep.gameObject.AddComponent<Image>();
                si.sprite = _art.Sweep;
                si.color = new Color(accent.r, accent.g, accent.b, 0.08f);
                si.raycastTarget = false;
            }
            var fx = rt.gameObject.AddComponent<HoloFx>();
            fx.Strength = strength;
            fx.Scan = scan;
            fx.Sweep = sweep;
            fx.Decor = group;
            fx.SweepPeriod = 5f + UnityEngine.Random.value * 3f;
            return fx;
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
            // 깜박이는 장식은 한 묶음 (글자 · 버튼은 깜박이지 않게)
            var decor = Decorative(Rect("HoloDecor", rt));
            Stretch(decor);
            var decorGroup = decor.gameObject.AddComponent<CanvasGroup>();
            decorGroup.interactable = false;
            decorGroup.blocksRaycasts = false;
            if (_art != null && _art.Grid != null)
            {
                var grid = Rect("HoloGrid", decor);
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
                var s = Rect("HoloScan", decor);
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
                sweep = Rect("HoloSweep", decor);
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
            fx.Decor = decorGroup;
            fx.Group = rt.GetComponent<CanvasGroup>();
            if (fx.Group == null)
                fx.Group = rt.gameObject.AddComponent<CanvasGroup>();
            return fx;
        }

        /// <summary>빛 번지는 패널: 바탕 + 바깥 빛 번짐 + 테두리 · 모서리 브래킷.</summary>
        public void GlowPanel(GameObject go, Color fill, Color frame, float glow = 0.35f)
        {
            PlainPanel(go, fill, frame);
            if (_art == null || _art.Glow == null || glow <= 0f)
                return;
            var g = Decorative(Rect("Glow", go.transform));
            Stretch(g, new Vector2(-14f, -14f), new Vector2(14f, 14f));
            g.SetAsFirstSibling();
            var gi = g.gameObject.AddComponent<Image>();
            gi.sprite = _art.Glow;
            gi.type = Image.Type.Sliced;
            gi.color = new Color(frame.r, frame.g, frame.b, glow);
            gi.raycastTarget = false;
        }

        /// <summary>
        /// 테크 패널 (사용자 레퍼런스 2026-10-10): 깎인 모서리 바탕 + 테두리(안쪽 가는 선 · 큰 모서리 굵은 강조 · 이음점)
        /// + 오른쪽 위 사선 줄무늬 + 왼쪽 위 제목 + (선택) 아래에서 바깥으로 뻗는 회로 선과 끝 점 + 바깥 빛 번짐.
        /// 테크 그림이 없으면 기본 <see cref="GlowPanel"/>.
        /// </summary>
        /// <param name="connector">회로 선 길이 (px, 0이면 없음) — 왼쪽 아래에서 아래로 뻗음.</param>
        public TMP_Text TechPanel(GameObject go, Color fill, Color line, string title = null, float glow = 0.3f, float connector = 0f)
        {
            if (_art == null || _art.TechFill == null || _art.TechFrame == null)
            {
                GlowPanel(go, fill, line, glow);
                return null;
            }
            var img = go.GetComponent<Image>();
            if (img == null)
                img = go.AddComponent<Image>();
            img.sprite = _art.TechFill;
            img.type = Image.Type.Sliced;
            img.color = fill;
            if (_art.Glow != null && glow > 0f)
            {
                var g = Decorative(Rect("Glow", go.transform));
                Stretch(g, new Vector2(-14f, -14f), new Vector2(14f, 14f));
                g.SetAsFirstSibling();
                var gi = g.gameObject.AddComponent<Image>();
                gi.sprite = _art.Glow;
                gi.type = Image.Type.Sliced;
                gi.color = new Color(line.r, line.g, line.b, glow);
                gi.raycastTarget = false;
            }
            var f = Decorative(Rect("Frame", go.transform));
            Stretch(f);
            var fi = f.gameObject.AddComponent<Image>();
            fi.sprite = _art.TechFrame;
            fi.type = Image.Type.Sliced;
            fi.color = line;
            fi.raycastTarget = false;
            RawImage hatchImage = null;
            if (_art.Hatch != null)
            {
                var h = Decorative(Rect("Hatch", go.transform));
                h.anchorMin = h.anchorMax = h.pivot = new Vector2(1f, 1f);
                h.anchoredPosition = new Vector2(-26f, -5f);
                h.sizeDelta = new Vector2(48f, 7f);
                var hi = h.gameObject.AddComponent<RawImage>();
                hi.texture = _art.Hatch;
                hi.uvRect = new Rect(0f, 0f, 48f / _art.Hatch.width, 7f / _art.Hatch.height);
                hi.color = new Color(line.r, line.g, line.b, line.a * 0.7f);
                hi.raycastTarget = false;
                hatchImage = hi;
            }
            // 패널 안을 가끔 지나가는 가는 빛줄기 + 사선 줄무늬 맥박 (글자보다 먼저 만들어 뒤에 그려짐)
            var scan = Decorative(Rect("PanelScan", go.transform));
            scan.anchorMin = new Vector2(0f, 1f);
            scan.anchorMax = new Vector2(1f, 1f);
            scan.pivot = new Vector2(0.5f, 0.5f);
            scan.offsetMin = new Vector2(6f, 0f);
            scan.offsetMax = new Vector2(-6f, 0f);
            scan.sizeDelta = new Vector2(-12f, 2f);
            var si = scan.gameObject.AddComponent<Image>();
            si.color = new Color(line.r, line.g, line.b, 0.55f);
            si.raycastTarget = false;
            var pfx = go.AddComponent<HoloPanelFx>();
            pfx.Line = si;
            pfx.Hatch = hatchImage;
            pfx.Period = 4f + UnityEngine.Random.value * 3f;
            if (connector > 0f)
            {
                var c = Decorative(Rect("Connector", go.transform));
                c.anchorMin = c.anchorMax = c.pivot = new Vector2(0f, 0f);
                c.anchoredPosition = new Vector2(10f, -connector);
                c.sizeDelta = new Vector2(1.5f, connector);
                var ci = c.gameObject.AddComponent<Image>();
                ci.color = new Color(line.r, line.g, line.b, line.a * 0.8f);
                ci.raycastTarget = false;
                if (_art.Dot != null)
                {
                    var d = Rect("Dot", c);
                    d.anchorMin = d.anchorMax = new Vector2(0.5f, 0f);
                    d.pivot = new Vector2(0.5f, 0.5f);
                    d.anchoredPosition = Vector2.zero;
                    d.sizeDelta = new Vector2(8f, 8f);
                    var di = d.gameObject.AddComponent<Image>();
                    di.sprite = _art.Dot;
                    di.color = line;
                    di.raycastTarget = false;
                }
            }
            if (string.IsNullOrEmpty(title))
                return null;
            var t = Label(go.transform, title, 12f, TextAlignmentOptions.MidlineLeft);
            Decorative(t.rectTransform);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = t.rectTransform.pivot = new Vector2(0f, 1f);
            t.rectTransform.anchoredPosition = new Vector2(22f, -3f);
            t.rectTransform.sizeDelta = new Vector2(220f, 18f);
            t.color = Color.Lerp(line, Color.white, 0.25f);
            t.characterSpacing = 6f;
            Glow(t, 0.5f);
            return t;
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

        /// <summary>장식은 레이아웃 그룹이 줄 세우지 않게 (그룹이 나중에 붙어도 되도록 미리, 11-15 ②).</summary>
        public static RectTransform Decorative(RectTransform rt)
        {
            var le = rt.GetComponent<LayoutElement>();
            if (le == null)
                le = rt.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
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

        private static readonly System.Collections.Generic.Dictionary<(TMP_FontAsset, int), Material> GlowMaterials =
            new System.Collections.Generic.Dictionary<(TMP_FontAsset, int), Material>();

        /// <summary>
        /// 11-13 글자 발광: 글자 뒤에 부드러운 청록 그림자(TMP Underlay)를 깔아 빛나 보이게 (정지 — 깜박이지 않음).
        /// 글꼴 · 세기마다 재질 하나를 나눠 씀. 제목 · 이름 · 버튼에 (작은 본문은 번져 보여서 빼는 편).
        /// </summary>
        public static void Glow(TMP_Text text, float strength = 0.5f)
        {
            if (text == null || text.font == null)
                return;
            var key = (text.font, Mathf.RoundToInt(strength * 100f));
            if (!GlowMaterials.TryGetValue(key, out var m) || m == null)
            {
                m = new Material(text.font.material) { name = text.font.name + " Glow" };
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor("_UnderlayColor", new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, strength));
                m.SetFloat("_UnderlaySoftness", 0.65f);
                m.SetFloat("_UnderlayDilate", 0.35f);
                m.SetFloat("_UnderlayOffsetX", 0f);
                m.SetFloat("_UnderlayOffsetY", 0f);
                GlowMaterials[key] = m;
            }
            text.fontSharedMaterial = m;
        }

        /// <summary>바탕 + 테두리 (창 안 카드 · 확인 상자 등). 테크 테마(11-15 ②)면 테크 패널.</summary>
        public void Panel(GameObject go, Color fill, Color frame)
        {
            if (_tech)
                TechPanel(go, fill, frame, null, 0.15f);
            else
                PlainPanel(go, fill, frame);
        }

        private void PlainPanel(GameObject go, Color fill, Color frame)
        {
            var img = go.GetComponent<Image>();
            if (img == null)
                img = go.AddComponent<Image>();
            img.sprite = _fill;
            img.type = Image.Type.Sliced;
            img.color = fill;
            var f = Decorative(Rect("Frame", go.transform));
            Stretch(f);
            var fi = f.gameObject.AddComponent<Image>();
            fi.sprite = _frame;
            fi.type = Image.Type.Sliced;
            fi.color = frame;
            fi.raycastTarget = false;
        }

        /// <summary>버튼. 테크 테마(11-15 ②)면 <see cref="TechButton"/>.</summary>
        public Button Button(Transform parent, string label, float size, Action onClick, bool clickSound = true)
            => _tech ? TechButton(parent, label, size, onClick, clickSound) : PlainButton(parent, label, size, onClick, clickSound);

        private Button PlainButton(Transform parent, string label, float size, Action onClick, bool clickSound)
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

        /// <summary>
        /// 11-13 테크 버튼 (사용자 레퍼런스): <see cref="Button"/>과 같고 그림만 테크 버튼(깎인 모서리 · 강조 · 사선 줄무늬).
        /// 테크 그림이 없으면 기본 버튼. 바깥 HUD 창은 2단계에서 함께 바꿈.
        /// </summary>
        public Button TechButton(Transform parent, string label, float size, Action onClick, bool clickSound = true)
        {
            var button = PlainButton(parent, label, size, onClick, clickSound);
            if (_art != null && _art.TechButton != null)
                ((Image)button.targetGraphic).sprite = _art.TechButton;
            Glow(button.GetComponentInChildren<TMP_Text>(), 0.3f);
            HoloButtonFx.Add(button, _art); // 호버 · 누름 · 비활성 · 브래킷 · 키 배지 · 상태 막대
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
