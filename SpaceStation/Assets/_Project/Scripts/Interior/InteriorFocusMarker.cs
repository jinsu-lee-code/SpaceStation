using SpaceStation.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.Interior
{
    /// <summary>
    /// CONNECT_1121 3번 ③ 위치 표시 (사용자 결정 2026-11-28 — 테두리 + 화면 끝 화살표 · 상황 동안 늘 · 상황 + 주민 부탁):
    /// - 대상이 화면 안이면 그 둘레에 네 모서리 테두리 (숨 쉬듯 벌어졌다 좁혀지며 밝아졌다 어두워짐 — 색만이 아니라 밝기 · 움직임으로 읽힘, 사용자 색약)
    /// - 화면 밖 · 등 뒤면 가장자리에 그쪽을 가리키는 꺾쇠 화살표
    /// - 글 한 줄 (다른 방을 가리킬 때 "○○ 쪽 문" 등) — 테두리 아래 · 화살표 안쪽
    /// 모든 조각 = 어두운 그림자 막대 위 색 막대 (밝은 벽 앞에서도 보이게). 화면 UI만 (월드에 아무것도 만들지 않음).
    /// </summary>
    public sealed class InteriorFocusMarker
    {
        private const float Thick = 4f, Corner = 22f, Pad = 8f, Breath = 6f, EdgeMargin = 70f, MinBox = 36f, Inset = 48f, LabelMargin = 16f;
        private const float ArrowLen = 34f, ArrowThick = 7f;   // 플레이 확인 2026-11-28: 22 · 5는 화면 끝에서 놓치기 쉬웠음
        private static readonly Color Shadow = new Color(0f, 0f, 0f, 0.55f);

        private readonly RectTransform _root, _box, _arrow;
        private readonly RectTransform[] _bars = new RectTransform[8];
        private readonly Image[] _barImages = new Image[8];
        private readonly Image[] _arrowImages = new Image[2];
        private readonly TMP_Text _label;
        private readonly Vector3[] _corners = new Vector3[8];
        private Canvas _canvas;

        private InteriorFocusMarker(RectTransform root, Sprite fill, HoloUi ui)
        {
            _root = root;
            _box = HoloUi.Rect("Box", root);
            Center(_box);
            for (int i = 0; i < 8; i++)
            {
                _bars[i] = HoloUi.Rect("Bar" + i, _box);
                Center(_bars[i]);
                _barImages[i] = Bar(_bars[i], fill);
            }
            _arrow = HoloUi.Rect("Arrow", root);
            Center(_arrow);
            for (int k = 0; k < 2; k++)
            {
                // 꺾쇠 ">" : 두 막대가 오른쪽 끝(화살 끝)에서 만남 — 컨테이너를 돌려 방향을 맞춤
                var arm = HoloUi.Rect("Arm" + k, _arrow);
                Center(arm);
                arm.pivot = new Vector2(1f, 0.5f);
                arm.sizeDelta = new Vector2(ArrowLen, ArrowThick);
                arm.localRotation = Quaternion.Euler(0f, 0f, k == 0 ? 40f : -40f);
                _arrowImages[k] = Bar(arm, fill);
            }
            _label = ui.Label(root, "", 18f, TextAlignmentOptions.Center);
            _label.rectTransform.anchorMin = _label.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _label.rectTransform.sizeDelta = new Vector2(420f, 26f);
            _label.raycastTarget = false;
            _label.fontStyle = FontStyles.Bold;
            _label.outlineWidth = 0.2f;
            _label.outlineColor = new Color32(0, 0, 0, 200);
            Hide();
        }

        public static InteriorFocusMarker Create(RectTransform parent, Sprite fill, HoloUi ui)
        {
            var root = HoloUi.Rect("FocusMarker", parent);
            HoloUi.Stretch(root);
            root.SetAsFirstSibling();   // 조준점 · 안내 글 · 길게 누르기 막대 아래
            return new InteriorFocusMarker(root, fill, ui);
        }

        public bool Visible => _root.gameObject.activeSelf;

        public void Hide()
        {
            if (_root.gameObject.activeSelf)
                _root.gameObject.SetActive(false);
        }

        /// <summary>대상 범위 (월드)를 가리킴. label이 비면 글 없음.</summary>
        public void Show(Camera cam, Bounds bounds, Color color, string label)
        {
            if (cam == null)
            {
                Hide();
                return;
            }
            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);
            if (_canvas == null)
                _canvas = _root.GetComponentInParent<Canvas>();
            var uiCam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay ? _canvas.worldCamera : null;

            float t = Time.unscaledTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * 0.9f);
            var c = color;
            c.a = 0.6f + 0.4f * pulse;

            // 범위 꼭짓점을 화면으로 — 하나라도 등 뒤면 화살표
            var min = bounds.min;
            var max = bounds.max;
            int n = 0;
            for (int i = 0; i < 8; i++)
                _corners[n++] = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            bool behind = false;
            var sMin = new Vector2(float.MaxValue, float.MaxValue);
            var sMax = new Vector2(float.MinValue, float.MinValue);
            foreach (var w in _corners)
            {
                var s = cam.WorldToScreenPoint(w);
                if (s.z <= 0.05f)
                {
                    behind = true;
                    break;
                }
                sMin = Vector2.Min(sMin, s);
                sMax = Vector2.Max(sMax, s);
            }
                        var centerScreen = cam.WorldToScreenPoint(bounds.center);
            // 가운데가 가장자리 띠(Inset) 안이면 화살표로 — 테두리 · 글이 반쯤 잘려 보이던 것 (문 표시)
            var inner = new Rect(Inset, Inset, Screen.width - 2f * Inset, Screen.height - 2f * Inset);
            bool onScreen = !behind && centerScreen.z > 0f && inner.Contains((Vector2)centerScreen);

            if (onScreen)
            {
                _arrow.gameObject.SetActive(false);
                _box.gameObject.SetActive(true);
                var lo = ToLocal(sMin, uiCam);
                var hi = ToLocal(sMax, uiCam);
                var size = hi - lo;
                var mid = (lo + hi) * 0.5f;
                size = new Vector2(Mathf.Max(size.x, MinBox), Mathf.Max(size.y, MinBox)) + Vector2.one * 2f * (Pad + Breath * pulse);
                _box.anchoredPosition = mid;
                PlaceCorners(size * 0.5f, c);
                PlaceLabel(label, mid + new Vector2(0f, -size.y * 0.5f - 18f), Vector2.zero, c);
                return;
            }

            // 화면 밖: 가운데에서 대상 쪽으로, 가장자리 안쪽에 꺾쇠
            _box.gameObject.SetActive(false);
            _arrow.gameObject.SetActive(true);
            var mid0 = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var dir = (Vector2)centerScreen - mid0;
            if (centerScreen.z < 0f)
                dir = -dir;   // 등 뒤 = 거꾸로 비친 자리
            if (dir.sqrMagnitude < 1f)
                dir = Vector2.down;
            dir.Normalize();
            float hx = Screen.width * 0.5f - EdgeMargin, hy = Screen.height * 0.5f - EdgeMargin;
            float k2 = Mathf.Min(Mathf.Abs(dir.x) > 1e-4f ? hx / Mathf.Abs(dir.x) : float.MaxValue, Mathf.Abs(dir.y) > 1e-4f ? hy / Mathf.Abs(dir.y) : float.MaxValue);
            var edge = mid0 + dir * (k2 - 6f * pulse);
            var local = ToLocal(edge, uiCam);
            _arrow.anchoredPosition = local;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
            foreach (var img in _arrowImages)
                img.color = c;
            PlaceLabel(label, local - dir * (ArrowLen + 10f), -dir, c);   // 화살표 안쪽으로 글 폭만큼 (플레이 확인: 글이 화살표와 겹쳤음)
        }

        private void PlaceCorners(Vector2 half, Color c)
        {
            // 모서리마다 가로 · 세로 막대 (안쪽으로 뻗음)
            for (int k = 0; k < 4; k++)
            {
                float sx = (k & 1) == 0 ? -1f : 1f, sy = (k & 2) == 0 ? -1f : 1f;
                var corner = new Vector2(sx * half.x, sy * half.y);
                var h = _bars[k * 2];
                var v = _bars[k * 2 + 1];
                h.sizeDelta = new Vector2(Corner, Thick);
                h.anchoredPosition = corner + new Vector2(-sx * (Corner * 0.5f - Thick * 0.5f), 0f);
                v.sizeDelta = new Vector2(Thick, Corner);
                v.anchoredPosition = corner + new Vector2(0f, -sy * (Corner * 0.5f - Thick * 0.5f));
                _barImages[k * 2].color = c;
                _barImages[k * 2 + 1].color = c;
            }
        }

        /// <summary>글 한 줄: at에서 push 쪽으로 글 반 폭 · 반 높이만큼 더 밀고 화면 안에 머물게.</summary>
        private void PlaceLabel(string label, Vector2 at, Vector2 push, Color c)
        {
            bool show = !string.IsNullOrEmpty(label);
            if (_label.gameObject.activeSelf != show)
                _label.gameObject.SetActive(show);
            if (!show)
                return;
            if (_label.text != label)
                _label.text = label;
            at += new Vector2(push.x * (_label.preferredWidth * 0.5f + 6f), push.y * 19f);
            // 화면 안에 머물게 (글 폭의 반 + 여백)
            var r = _root.rect;
            float hw = Mathf.Max(0f, r.width * 0.5f - _label.preferredWidth * 0.5f - LabelMargin);
            float hh = Mathf.Max(0f, r.height * 0.5f - 13f - LabelMargin);
            _label.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(at.x, -hw, hw), Mathf.Clamp(at.y, -hh, hh));
            var lc = c;
            lc.a = 1f;
            _label.color = lc;
        }

        private Vector2 ToLocal(Vector2 screen, Camera uiCam)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, uiCam, out var local);
            return local - _root.rect.center;
        }

        private static void Center(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>그림자 막대 (조금 큰 어두운 판) 위에 색 막대.</summary>
        private static Image Bar(RectTransform rt, Sprite fill)
        {
            var shadow = HoloUi.Rect("Shadow", rt);
            shadow.anchorMin = Vector2.zero;
            shadow.anchorMax = Vector2.one;
            shadow.offsetMin = new Vector2(-1.5f, -1.5f);
            shadow.offsetMax = new Vector2(1.5f, 1.5f);
            var si = shadow.gameObject.AddComponent<Image>();
            si.sprite = fill;
            si.color = Shadow;
            si.raycastTarget = false;
            var face = HoloUi.Rect("Face", rt);
            HoloUi.Stretch(face);
            var img = face.gameObject.AddComponent<Image>();
            img.sprite = fill;
            img.raycastTarget = false;
            return img;
        }
    }
}
