using System.Collections;
using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>로딩 화면에 보여 줄 정거장 (저장 불러오기 · 새 게임). 없으면 진행 막대와 팁만.</summary>
    public sealed class LoadingCard
    {
        public string Title;
        public string Detail;
        /// <summary>저장 썸네일 PNG 경로 (없으면 그림 없이 글만).</summary>
        public string ThumbnailPath;
    }

    /// <summary>
    /// 5-9 씬 전환: 화면을 검게 덮고(음악도 함께 줄임) 새 씬을 불러온 뒤 다시 밝힌다.
    /// 처음 쓸 때 스스로 만들어지고 씬이 바뀌어도 남는다.
    /// 12-3c-opt (사용자 피드백 2026-10-14): 큰 정거장은 불러오기에 시간이 걸려 검은 화면에 로딩 표시.
    /// 2차 (같은 날: "로딩창이 너무 텅텅 비어 있다"): 흐르는 별 배경 + 불러오는 정거장 카드(저장 썸네일 · 등급 · 인구 · 모듈 · 플레이 시간)
    /// + 진행률(%) + 게임 팁. 창 UI와 같은 테크 패널(<see cref="HoloArt"/>)을 쓰고, 끝나면 로딩 화면째로 게임 화면에 녹아든다.
    /// 글꼴 · 아트는 처음 쓸 때 열려 있는 씬의 <see cref="HoloSkin"/>(없으면 아무 TMP 글자)에서 가져온다.
    /// </summary>
    public sealed class SceneFader : MonoBehaviour
    {
        private const float OutSeconds = 0.55f;
        private const float InSeconds = 0.9f;
        private const float LoadingInSeconds = 0.5f;
        private const float LoadingOutSeconds = 0.4f;
        private const float MinLoadingSeconds = 0.9f; // 팁 한 줄은 읽을 수 있게
        private const float TipSeconds = 5f;

        private static readonly Color Muted = new Color(0.68f, 0.76f, 0.85f, 1f);
        private static readonly Color TextColor = new Color(0.91f, 0.96f, 1f, 1f);

        private static SceneFader _instance;
        private CanvasGroup _group;
        private bool _busy;
        private TMP_FontAsset _font;
        private HoloArt _art;

        private RectTransform _loading;
        private CanvasGroup _loadingGroup;
        private RawImage _starsNear, _starsFar;
        private RectTransform _card;
        private RawImage _thumb;
        private TMP_Text _title, _detail;
        private TMP_Text _status, _percent, _tip;
        private Image _bar;
        private float _progress;
        private float _nextTip;
        private int _tipIndex;
        private LoadingCard _pending;
        private Texture2D _thumbTexture;

        public static bool Busy => _instance != null && _instance._busy;

        public static void Load(string scene) => Load(scene, null);

        /// <param name="card">로딩 화면에 보여 줄 정거장 (저장 썸네일 · 정보). null이면 진행 막대와 팁만.</param>
        public static void Load(string scene, LoadingCard card)
        {
            Ensure();
            if (_instance._busy)
                return;
            _instance._pending = card;
            _instance.StartCoroutine(_instance.Run(scene));
        }

        /// <summary>현재 씬 다시 시작.</summary>
        public static void Reload() => Load(SceneManager.GetActiveScene().name);

        private static void Ensure()
        {
            if (_instance != null)
                return;
            var go = new GameObject("SceneFader");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SceneFader>();
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            var img = new GameObject("Black", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(go.transform, false);
            img.color = Color.black;
            Stretch((RectTransform)img.transform);
            _instance._group = go.AddComponent<CanvasGroup>();
            _instance._group.alpha = 0f;
            _instance._group.blocksRaycasts = false;

            var skin = FindFirstObjectByType<HoloSkin>(FindObjectsInactive.Include);
            _instance._art = skin != null ? skin.Art : null;
            _instance._font = _instance._art != null && _instance._art.Font != null ? _instance._art.Font : null;
            if (_instance._font == null)
            {
                var anyText = FindFirstObjectByType<TMP_Text>(FindObjectsInactive.Include);
                _instance._font = anyText != null ? anyText.font : null;
            }
            _instance.BuildLoading(go.transform);
        }

        // ---------------- 화면 구성 (1920×1080 기준) ----------------

        private void BuildLoading(Transform parent)
        {
            _loading = Rect("Loading", parent);
            Stretch(_loading);
            _loadingGroup = _loading.gameObject.AddComponent<CanvasGroup>();

            // 흐르는 별 (두 겹, 먼 쪽은 느리고 어둡게)
            _starsFar = Stars("StarsFar", 7, 26, 0.5f);
            _starsNear = Stars("StarsNear", 11, 9, 0.9f);

            // 왼쪽 위 게임 이름
            var brand = Label(_loading, Application.productName.ToUpperInvariant(), 18f, TextAlignmentOptions.TopLeft);
            Place(brand.rectTransform, new Vector2(0f, 1f), new Vector2(64f, -52f), new Vector2(600f, 30f));
            brand.characterSpacing = 14f;
            brand.color = new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.8f);
            var line = Rect("BrandLine", _loading);
            Place(line, new Vector2(0f, 1f), new Vector2(64f, -88f), new Vector2(220f, 2f));
            AddImage(line, new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.5f));

            // 가운데: 정거장 카드 (썸네일 + 테크 패널 테두리)
            _card = Rect("Card", _loading);
            Place(_card, new Vector2(0.5f, 0.5f), new Vector2(0f, 95f), new Vector2(784f, 449f));
            var cardGo = _card.gameObject;
            var fill = new Color(0.03f, 0.06f, 0.1f, 0.96f);
            var accentLine = new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.85f);
            if (_art != null)
                new HoloUi(_art, tech: true).TechPanel(cardGo, fill, accentLine, "STATION LINK", 0.3f, 0f);
            else
                AddImage(_card, fill);
            var thumbRt = Rect("Thumbnail", _card);
            Stretch(thumbRt, new Vector2(16f, 16f), new Vector2(-16f, -26f));
            _thumb = thumbRt.gameObject.AddComponent<RawImage>();
            _thumb.raycastTarget = false;
            if (_art != null && _art.Scan != null)
            {
                // 화면 위 주사선 (작은 썸네일을 키운 흐림을 가려 줌)
                var scanRt = Rect("Scan", thumbRt);
                Stretch(scanRt);
                var scan = scanRt.gameObject.AddComponent<RawImage>();
                scan.texture = _art.Scan;
                scan.color = new Color(0f, 0f, 0f, 0.35f);
                scan.uvRect = new Rect(0f, 0f, 1f, 407f / _art.Scan.height / 1.2f);
                scan.raycastTarget = false;
            }

            _title = Label(_loading, "", 44f, TextAlignmentOptions.Center);
            Place(_title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(1200f, 56f));
            _title.fontStyle = FontStyles.Bold;
            _title.color = TextColor;
            _detail = Label(_loading, "", 24f, TextAlignmentOptions.Center);
            Place(_detail.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -218f), new Vector2(1200f, 34f));
            _detail.color = Muted;

            // 아래: 상태 · 진행률 · 막대
            const float barWidth = 960f;
            _status = Label(_loading, "", 24f, TextAlignmentOptions.BottomLeft);
            Place(_status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(barWidth, 34f));
            _status.color = TextColor;
            _percent = Label(_loading, "", 24f, TextAlignmentOptions.BottomRight);
            Place(_percent.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(barWidth, 34f));
            _percent.color = HudTheme.Accent;

            var back = Rect("Bar", _loading);
            Place(back, new Vector2(0.5f, 0f), new Vector2(0f, 172f), new Vector2(barWidth, 6f));
            AddImage(back, new Color(1f, 1f, 1f, 0.1f));
            if (_art != null && _art.Glow != null)
            {
                var glow = Rect("Glow", back);
                Stretch(glow, new Vector2(-12f, -12f), new Vector2(12f, 12f));
                var gi = glow.gameObject.AddComponent<Image>();
                gi.sprite = _art.Glow;
                gi.type = Image.Type.Sliced;
                gi.color = new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.18f);
                gi.raycastTarget = false;
            }
            var fillRt = Rect("Fill", back);
            Stretch(fillRt);
            _bar = AddImage(fillRt, HudTheme.Accent);
            _bar.type = Image.Type.Filled;
            _bar.fillMethod = Image.FillMethod.Horizontal;
            _bar.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero); // Filled는 스프라이트가 있어야 채움
            _bar.fillAmount = 0f;

            // 맨 아래: 팁 (꼬리표 + 글)
            var tipRow = Rect("Tip", _loading);
            Place(tipRow, new Vector2(0.5f, 0f), new Vector2(0f, 84f), new Vector2(barWidth, 64f));
            var chip = Rect("Chip", tipRow);
            Place(chip, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(64f, 32f));
            AddImage(chip, new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.22f));
            var chipText = Label(chip, "TIP", 18f, TextAlignmentOptions.Center);
            Stretch(chipText.rectTransform);
            chipText.color = HudTheme.Accent;
            chipText.fontStyle = FontStyles.Bold;
            chipText.characterSpacing = 6f;
            _tip = Label(tipRow, "", 23f, TextAlignmentOptions.TopLeft);
            Stretch(_tip.rectTransform, new Vector2(84f, 0f), new Vector2(0f, -2f));
            _tip.textWrappingMode = TextWrappingModes.Normal;
            _tip.color = TextColor;
            _tipIndex = Random.Range(0, LoadingTips.Count);

            _loading.gameObject.SetActive(false);
        }

        /// <summary>점 별 텍스처 한 장을 화면에 깔고 천천히 흘림.</summary>
        private RawImage Stars(string name, int seed, int count, float alpha)
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = name };
            var clear = new Color32[size * size];
            tex.SetPixels32(clear);
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                int x = rng.Next(size), y = rng.Next(size);
                float b = 0.35f + (float)rng.NextDouble() * 0.65f;
                var c = Color.Lerp(new Color(0.7f, 0.85f, 1f), Color.white, (float)rng.NextDouble()) * b;
                c.a = b;
                tex.SetPixel(x, y, c);
                if (b > 0.85f) // 밝은 별은 십자로 조금 크게
                {
                    var soft = new Color(c.r, c.g, c.b, b * 0.35f);
                    tex.SetPixel((x + 1) % size, y, soft);
                    tex.SetPixel((x + size - 1) % size, y, soft);
                    tex.SetPixel(x, (y + 1) % size, soft);
                    tex.SetPixel(x, (y + size - 1) % size, soft);
                }
            }
            tex.Apply();
            var rt = Rect(name, _loading);
            Stretch(rt);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.color = new Color(1f, 1f, 1f, alpha);
            img.raycastTarget = false;
            return img;
        }

        private void Update()
        {
            if (_loading == null || !_loading.gameObject.activeSelf)
                return;
            float t = Time.unscaledTime;
            Drift(_starsFar, t, 0.004f);
            Drift(_starsNear, t, 0.011f);
            int dots = 1 + (int)(t * 2.5f) % 3;
            _status.SetText("불러오는 중" + new string('.', dots));
            _bar.fillAmount = Mathf.MoveTowards(_bar.fillAmount, _progress, Time.unscaledDeltaTime * 2f);
            _percent.SetText($"{Mathf.RoundToInt(_bar.fillAmount * 100f)}%");
            if (t >= _nextTip)
                ShowTip(t);
        }

        private static void Drift(RawImage stars, float t, float speed)
        {
            // 화면 크기 / 텍스처 크기만큼 반복 (별이 커지지 않게), 오른쪽 위로 천천히
            var size = ((RectTransform)stars.transform).rect.size;
            stars.uvRect = new Rect(t * speed, t * speed * 0.35f, Mathf.Max(1f, size.x / 256f), Mathf.Max(1f, size.y / 256f));
        }

        private void ShowTip(float now)
        {
            _nextTip = now + TipSeconds;
            _tipIndex = (_tipIndex + 1) % LoadingTips.Count;
            _tip.SetText(LoadingTips.Get(_tipIndex));
        }

        private void ApplyCard(LoadingCard card)
        {
            if (_thumbTexture != null)
                Destroy(_thumbTexture);
            _thumbTexture = null;
            if (card != null && !string.IsNullOrEmpty(card.ThumbnailPath) && System.IO.File.Exists(card.ThumbnailPath))
            {
                try
                {
                    _thumbTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (!_thumbTexture.LoadImage(System.IO.File.ReadAllBytes(card.ThumbnailPath)))
                    {
                        Destroy(_thumbTexture);
                        _thumbTexture = null;
                    }
                }
                catch (System.Exception)
                {
                    _thumbTexture = null;
                }
            }
            bool hasThumb = _thumbTexture != null;
            _card.gameObject.SetActive(hasThumb);
            _thumb.texture = _thumbTexture;
            _title.SetText(card != null ? card.Title ?? "" : "");
            _detail.SetText(card != null ? card.Detail ?? "" : "");
            // 썸네일이 없으면 제목을 가운데로
            _title.rectTransform.anchoredPosition = new Vector2(0f, hasThumb ? -170f : 40f);
            _detail.rectTransform.anchoredPosition = new Vector2(0f, hasThumb ? -218f : -8f);
        }

        private IEnumerator Run(string scene)
        {
            _busy = true;
            _group.blocksRaycasts = true;
            if (AudioService.Instance != null)
                AudioService.Instance.DuckMusic(0f, 1f / OutSeconds);
            yield return Fade(0f, 1f, OutSeconds);
            InputGate.Blocked = false;
            ApplyCard(_pending);
            _pending = null;
            _progress = 0.05f;
            _bar.fillAmount = 0f;
            _loadingGroup.alpha = 0f;
            _loading.gameObject.SetActive(true);
            ShowTip(Time.unscaledTime);
            float shownAt = Time.unscaledTime;
            // 검은 화면에서 로딩 화면이 천천히 밝아진 뒤 불러오기 시작 (사용자 요청: 시작 · 끝 페이드로 눈이 적응하게)
            yield return FadeLoading(0f, 1f, LoadingInSeconds);
            var op = SceneManager.LoadSceneAsync(scene);
            if (op != null)
            {
                op.allowSceneActivation = false; // 90%까지 읽은 화면을 한 번 그린 뒤 씬 통합(멈춤)
                while (op.progress < 0.9f)
                {
                    _progress = Mathf.Max(_progress, 0.1f + op.progress * 0.8f);
                    yield return null;
                }
                _progress = 0.82f;
                while (Time.unscaledTime - shownAt < MinLoadingSeconds || _bar.fillAmount < 0.8f)
                    yield return null;
                op.allowSceneActivation = true;
                while (!op.isDone)
                    yield return null;
            }
            _progress = 1f;
            _bar.fillAmount = 1f;
            _percent.SetText("100%");
            yield return null; // 새 씬 Start 한 번
            yield return new WaitForSecondsRealtime(0.15f); // 100%를 잠깐 보여 줌
            // 끝: 로딩 화면 → 검정 → 게임 화면 (밝은 화면이 한꺼번에 바뀌지 않게 두 단계로)
            yield return FadeLoading(1f, 0f, LoadingOutSeconds);
            _loading.gameObject.SetActive(false);
            yield return Fade(1f, 0f, InSeconds);
            _group.blocksRaycasts = false;
            _busy = false;
        }

        private IEnumerator FadeLoading(float from, float to, float seconds)
        {
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / seconds);
                _loadingGroup.alpha = Mathf.Lerp(from, to, t * t * (3f - 2f * t));
                yield return null;
            }
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            float t = 0f;
            while (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.unscaledDeltaTime / seconds);
                _group.alpha = Mathf.Lerp(from, to, t * t * (3f - 2f * t));
                yield return null;
            }
        }

        // ---------------- 작은 도우미 ----------------

        private static RectTransform Rect(string name, Transform parent)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Stretch(RectTransform rt, Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }

        /// <summary>anchor 한 점 기준 배치 (pivot = anchor).</summary>
        private static void Place(RectTransform rt, Vector2 anchor, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static Image AddImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private TMP_Text Label(Transform parent, string text, float size, TextAlignmentOptions align)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null)
                t.font = _font;
            t.fontSize = size;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            t.SetText(text);
            return t;
        }
    }
}
