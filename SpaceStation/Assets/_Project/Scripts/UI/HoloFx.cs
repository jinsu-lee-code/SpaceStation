using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 11-13 홀로그램 화면 움직임: 주사선이 천천히 흐르고, 스캔 띠가 위에서 아래로 지나가고, 전체가 아주 약하게 깜박인다.
    /// <see cref="Strength"/> = 0(효과 없음) ~ 1(패드처럼 진하게). 바깥 HUD는 약하게(0.3 안팎). 시간은 일시정지와 무관(unscaled).
    /// </summary>
    public sealed class HoloFx : MonoBehaviour
    {
        [Range(0f, 1f)] public float Strength = 1f;
        [Tooltip("주사선 흐름 속도 (화면 높이 / 초)")]
        public float ScanSpeed = 0.06f;
        [Tooltip("스캔 띠가 한 번 지나가는 주기 (초)")]
        public float SweepPeriod = 3.4f;

        public RawImage Scan;
        public RectTransform Sweep;
        public CanvasGroup Group;
        [Tooltip("켜질 때 위에서 아래로 그려지는 효과 (초)")]
        public float RevealSeconds = 0.35f;

        private float _scanAlpha;
        private float _sweepAlpha;
        private Image _sweepImage;
        private float _reveal;
        private float _seed;

        private void Awake()
        {
            _seed = Random.value * 10f;
            if (Scan != null)
                _scanAlpha = Scan.color.a;
            if (Sweep != null)
            {
                _sweepImage = Sweep.GetComponent<Image>();
                if (_sweepImage != null)
                    _sweepAlpha = _sweepImage.color.a;
            }
        }

        private void OnEnable() => _reveal = 0f;

        /// <summary>위에서 아래로 다시 그려지는 효과 (창 열기 · 층 바꾸기).</summary>
        public void Replay() => _reveal = 0f;

        private void Update()
        {
            float t = Time.unscaledTime + _seed;
            float s = Strength;
            if (Scan != null)
            {
                var uv = Scan.uvRect;
                uv.y = -t * ScanSpeed * uv.height;
                Scan.uvRect = uv;
                var c = Scan.color;
                c.a = _scanAlpha * s;
                Scan.color = c;
            }
            if (Sweep != null)
            {
                var parent = Sweep.parent as RectTransform;
                float h = parent != null ? parent.rect.height : 0f;
                float phase = Mathf.Repeat(t / Mathf.Max(0.5f, SweepPeriod), 1f);
                float travel = h + Sweep.rect.height;
                Sweep.anchoredPosition = new Vector2(Sweep.anchoredPosition.x, -phase * travel + Sweep.rect.height * 0.5f);
                if (_sweepImage != null)
                {
                    var c = _sweepImage.color;
                    c.a = _sweepAlpha * s;
                    _sweepImage.color = c;
                }
            }
            if (Group != null)
            {
                _reveal = Mathf.MoveTowards(_reveal, 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, RevealSeconds));
                // 깜박임: 느린 흔들림 + 가끔 짧게 떨어짐 (강도에 비례)
                float flicker = 1f - s * (0.035f * (0.5f + 0.5f * Mathf.Sin(t * 13.7f)) + (Mathf.PerlinNoise(t * 3.1f, 0.37f) > 0.82f ? 0.12f : 0f));
                float reveal = RevealCurve(_reveal);
                Group.alpha = flicker * reveal;
            }
        }

        /// <summary>켜질 때: 빠르게 두 번 깜박이며 밝아짐.</summary>
        private static float RevealCurve(float x)
        {
            if (x >= 1f)
                return 1f;
            float blink = x < 0.18f ? x / 0.18f : x < 0.3f ? 0.35f : Mathf.Lerp(0.6f, 1f, (x - 0.3f) / 0.7f);
            return Mathf.Clamp01(blink);
        }
    }
}
