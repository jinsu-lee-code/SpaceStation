using System.Collections;
using SpaceStation.Audio;
using SpaceStation.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 5-9 씬 전환: 화면을 검게 덮고(음악도 함께 줄임) 새 씬을 불러온 뒤 다시 밝힌다.
    /// 처음 쓸 때 스스로 만들어지고 씬이 바뀌어도 남는다.
    /// </summary>
    public sealed class SceneFader : MonoBehaviour
    {
        private const float OutSeconds = 0.55f;
        private const float InSeconds = 0.7f;

        private static SceneFader _instance;
        private CanvasGroup _group;
        private bool _busy;

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
        }

        private IEnumerator Run(string scene)
        {
            _busy = true;
            _group.blocksRaycasts = true;
            if (AudioService.Instance != null)
                AudioService.Instance.DuckMusic(0f, 1f / OutSeconds);
            yield return Fade(0f, 1f, OutSeconds);
            InputGate.Blocked = false;
            var op = SceneManager.LoadSceneAsync(scene);
            while (op != null && !op.isDone)
                yield return null;
            yield return null; // 새 씬 Start 한 번
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
