using UnityEngine;
using UnityEngine.UI;

namespace SpaceStation.UI
{
    /// <summary>
    /// 11-13 홀로그램 화면 움직임: 주사선이 천천히 흐르고, 스캔 띠가 위에서 아래로 지나가고, 바탕 장식이 깜박인다 (글자 · 버튼은 그대로).
    /// <see cref="Strength"/> = 0(효과 없음) ~ 1(패드처럼 진하게). 바깥 HUD는 약하게(0.3 안팎). 시간은 일시정지와 무관(unscaled).
    /// </summary>
    public sealed class HoloFx : MonoBehaviour
    {
        [Range(0f, 1f)] public float Strength = 1f;
        [Tooltip("주사선 흐름 속도 (화면 높이 / 초)")]
        public float ScanSpeed = 0.06f;
        [Tooltip("스캔 띠가 한 번 지나가는 주기 (초)")]
        public float SweepPeriod = 3.4f;
        [Tooltip("바탕 장식 깜박임 세기 배율 (0 = 깜박임 없음). 작은 글자가 많은 화면은 낮게 — 뒤 밝기가 출렁이면 Bloom 번짐이 흔들려 글자가 일렁여 보임")]
        [Range(0f, 1f)] public float Flicker = 1f;

        public RawImage Scan;
        public RectTransform Sweep;
        [Tooltip("켜짐 효과만 (화면 전체) — 글자는 깜박이지 않게 (깜박임을 전체에 걸었더니 Bloom과 함께 글자가 일렁였음)")]
        public CanvasGroup Group;
        [Tooltip("깜박임 · 지지직은 바탕 장식(격자 · 주사선 · 스캔 띠)에만")]
        public CanvasGroup Decor;
        [Tooltip("켜질 때 위에서 아래로 그려지는 효과 (초)")]
        public float RevealSeconds = 0.35f;

        private float _scanAlpha;
        private float _sweepAlpha;
        private Image _sweepImage;
        private float _reveal;
        private float _seed;

        // Start: AddComponent 직후 Awake가 먼저 돌아 Scan · Sweep이 아직 비어 있는 경우가 있음 (11-15 HoloSkin)
        private void Start()
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

        private float _revealMin;   // 켜짐 효과의 가장 어두운 정도 (처음 켜질 때 0, 다시 그리기는 0.85)
        private float _sweepStart = -100f;

        private void OnEnable()
        {
            _reveal = 0f;
            _revealMin = 0f;
        }

        /// <summary>
        /// 내용이 바뀔 때(탭 · 모듈 · 층) 다시 그려지는 느낌: 스캔 띠가 위에서부터 한 번 지나가고 화면이 아주 살짝만 깜박임.
        /// 화면 전체를 투명하게 만들면 뒤의 패드 유리가 드러나 조명이 번쩍였음 (11-13) → 최소 0.85.
        /// </summary>
        public void Replay()
        {
            _reveal = 0f;
            _revealMin = 0.85f;
            _sweepStart = Time.unscaledTime;
        }

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
                float since = Time.unscaledTime - _sweepStart;
                float phase = since < 0.45f ? since / 0.45f // 다시 그리기: 빠르게 한 번
                    : Mathf.Repeat(t / Mathf.Max(0.5f, SweepPeriod), 1f);
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
                Group.alpha = Mathf.Lerp(_revealMin, 1f, RevealCurve(_reveal));
            }
            if (Decor != null)
            {
                // 깜박임: 느린 흔들림 + 가끔 짧게 떨어짐 (강도에 비례, 바탕 장식만)
                float flicker = 1f - s * Flicker * (0.12f * (0.5f + 0.5f * Mathf.Sin(t * 13.7f)) + (Mathf.PerlinNoise(t * 3.1f, 0.37f) > 0.8f ? 0.35f : 0f));
                Decor.alpha = flicker;
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
