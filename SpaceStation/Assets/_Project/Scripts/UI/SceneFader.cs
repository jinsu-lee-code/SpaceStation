using System.Collections;
using SpaceStation.Audio;
using SpaceStation.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 5-9 씬 전환: 화면을 검게 덮고(음악도 함께 줄임) 새 씬을 불러온 뒤 다시 밝힌다.
    /// 처음 쓸 때 스스로 만들어지고 씬이 바뀌어도 남는다.
    /// 12-3c-opt (사용자 피드백 2026-10-14): 큰 정거장은 불러오기에 시간이 걸려 검은 화면에 로딩 표시(글자 · 점 애니메이션 · 진행 막대).
    /// 글꼴은 처음 쓸 때 열려 있는 씬의 글자(TMP)에서 가져온다 (한글 글꼴).
    /// </summary>
    public sealed class SceneFader : MonoBehaviour
    {
        private const float OutSeconds = 0.55f;
        private const float InSeconds = 0.7f;

        private static SceneFader _instance;
        private CanvasGroup _group;
        private bool _busy;
        private RectTransform _loading;
        private TMP_Text _loadingText;
        private Image _bar;
        private float _progress;

        public static bool Busy => _instance != null && _instance._busy;

        public static void Load(string scene)
        {
            Ensure();
            if (_instance._busy)
                return;
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
            var rt = (RectTransform)img.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _instance._group = go.AddComponent<CanvasGroup>();
            _instance._group.alpha = 0f;
            _instance._group.blocksRaycasts = false;
            _instance.BuildLoading(go.transform);
        }

        /// <summary>화면 아래 가운데: "불러오는 중..." + 가는 진행 막대 (게임 강조색).</summary>
        private void BuildLoading(Transform parent)
        {
            _loading = (RectTransform)new GameObject("Loading", typeof(RectTransform)).transform;
            _loading.SetParent(parent, false);
            _loading.anchorMin = _loading.anchorMax = _loading.pivot = new Vector2(0.5f, 0.5f);
            _loading.anchoredPosition = new Vector2(0f, -40f);
            _loading.sizeDelta = new Vector2(520f, 80f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(_loading, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = new Vector2(0f, 0.5f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            _loadingText = textGo.AddComponent<TextMeshProUGUI>();
            var anyText = FindFirstObjectByType<TMP_Text>(FindObjectsInactive.Include);
            if (anyText != null)
                _loadingText.font = anyText.font;
            _loadingText.fontSize = 30f;
            _loadingText.alignment = TextAlignmentOptions.Center;
            _loadingText.color = new Color(0.75f, 0.88f, 1f);
            _loadingText.raycastTarget = false;

            var back = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(_loading, false);
            var brt = (RectTransform)back.transform;
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.25f);
            brt.sizeDelta = new Vector2(360f, 4f);
            var backImg = back.GetComponent<Image>();
            backImg.color = new Color(1f, 1f, 1f, 0.12f);
            backImg.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(back.transform, false);
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            _bar = fill.GetComponent<Image>();
            _bar.color = HudTheme.Accent;
            _bar.raycastTarget = false;
            _bar.type = Image.Type.Filled;
            _bar.fillMethod = Image.FillMethod.Horizontal;
            _bar.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero); // Filled는 스프라이트가 있어야 채움
            _bar.fillAmount = 0f;
            _loading.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_loading == null || !_loading.gameObject.activeSelf)
                return;
            int dots = 1 + (int)(Time.unscaledTime * 2.5f) % 3;
            _loadingText.SetText("불러오는 중" + new string('.', dots));
            _bar.fillAmount = Mathf.MoveTowards(_bar.fillAmount, _progress, Time.unscaledDeltaTime * 2f);
        }

        private IEnumerator Run(string scene)
        {
            _busy = true;
            _group.blocksRaycasts = true;
            if (AudioService.Instance != null)
                AudioService.Instance.DuckMusic(0f, 1f / OutSeconds);
            yield return Fade(0f, 1f, OutSeconds);
            InputGate.Blocked = false;
            _progress = 0.05f;
            _bar.fillAmount = 0f;
            _loading.gameObject.SetActive(true);
            yield return null; // 로딩 표시가 그려진 뒤 불러오기 시작
            var op = SceneManager.LoadSceneAsync(scene);
            while (op != null && !op.isDone)
            {
                _progress = Mathf.Max(_progress, 0.1f + op.progress * 0.8f); // 0.9에서 씬 통합(멈춤) — 남은 막대는 끝난 뒤 채움
                yield return null;
            }
            _progress = 1f;
            _bar.fillAmount = 1f;
            yield return null; // 새 씬 Start 한 번
            _loading.gameObject.SetActive(false);
            yield return Fade(1f, 0f, InSeconds);
            _group.blocksRaycasts = false;
            _busy = false;
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
    }
}
