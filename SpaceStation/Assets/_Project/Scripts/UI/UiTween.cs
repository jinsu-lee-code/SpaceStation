using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 5-8 HUD 등장·퇴장 애니메이션 (게임 시간과 무관하게 unscaled). 알파 + 짧게 미끄러짐.
    /// Play()로 나타나고 Hide()로 사라진다. 대상 RectTransform의 원래 위치를 기억한다.
    /// 11-15 <see cref="Glitch"/>: 나타날 때 · 사라질 때 잠깐 홀로그램 지지직 (깜박임 + 옆으로 튐 + 가로 늘어남).
    /// </summary>
    public sealed class UiTween
    {
        private const float GlitchSeconds = 0.26f;
        private const float GlitchStep = 0.035f;   // 상태가 바뀌는 간격 (프레임과 무관하게 끊겨 보이게)

        private readonly RectTransform _rect;
        private readonly CanvasGroup _group;
        private readonly Vector2 _home;
        private readonly Vector2 _offset;
        private readonly float _inSeconds;
        private readonly float _outSeconds;
        private float _t;      // 0 = 숨김, 1 = 보임
        private int _dir;      // +1 나타나는 중, -1 사라지는 중

        /// <summary>11-15 등장 · 퇴장 때 지지직 효과 (창 · 배너).</summary>
        public bool Glitch { get; set; }

        private float _glitchStart = -100f;
        private float _glitchNext;
        private float _glitchAlpha = 1f;
        private float _glitchX;
        private float _glitchStretch;

        public bool Visible => _t > 0f || _dir > 0;

        public UiTween(RectTransform rect, CanvasGroup group, Vector2 offset, float inSeconds = 0.22f, float outSeconds = 0.3f, bool glitch = false)
        {
            _rect = rect;
            _group = group;
            _home = rect != null ? rect.anchoredPosition : Vector2.zero;
            _offset = offset;
            _inSeconds = inSeconds;
            _outSeconds = outSeconds;
            Glitch = glitch;
            Apply();
        }

        public void Play(bool restart = true)
        {
            if (restart)
                _t = 0f;
            if (_dir <= 0 && (restart || _t < 1f))
                StartGlitch();
            _dir = 1;
        }

        public void Hide()
        {
            if (_dir >= 0 && _t > 0f)
                StartGlitch();
            _dir = -1;
        }

        private void StartGlitch()
        {
            if (!Glitch)
                return;
            _glitchStart = Time.unscaledTime;
            _glitchNext = 0f;
        }

        private bool Glitching => Glitch && Time.unscaledTime - _glitchStart < GlitchSeconds;

        public void Update()
        {
            bool glitching = Glitching;
            if (_dir == 0 && !glitching && _glitchStretch == 0f && _glitchX == 0f && _glitchAlpha >= 1f)
                return;
            if (_dir != 0)
            {
                float dt = Time.unscaledDeltaTime;
                // 사라질 때는 지지직이 보이도록 투명해지는 걸 조금 늦춤
                float seconds = _dir > 0 ? _inSeconds : Mathf.Max(_outSeconds, GlitchSeconds * 0.8f);
                _t = Mathf.Clamp01(_t + _dir * dt / Mathf.Max(0.01f, seconds));
                if (_t <= 0f || _t >= 1f)
                    _dir = 0;
            }
            UpdateGlitch(glitching);
            Apply();
        }

        private void UpdateGlitch(bool glitching)
        {
            if (!glitching)
            {
                _glitchAlpha = 1f;
                _glitchX = 0f;
                _glitchStretch = 0f;
                return;
            }
            float since = Time.unscaledTime - _glitchStart;
            if (since < _glitchNext)
                return;
            _glitchNext = since + GlitchStep;
            float k = 1f - since / GlitchSeconds; // 끝으로 갈수록 약하게
            float r = Random.value;
            _glitchAlpha = r < 0.22f ? 0.15f : r < 0.45f ? 0.55f : 1f;   // 가끔 꺼질 듯 깜박
            _glitchX = Random.value < 0.6f ? Random.Range(-12f, 12f) * k : 0f;
            _glitchStretch = Random.value < 0.35f ? Random.Range(0.015f, 0.05f) * k : 0f;
        }

        private void Apply()
        {
            float e = 1f - (1f - _t) * (1f - _t); // ease out
            if (_group != null)
                _group.alpha = e * _glitchAlpha;
            if (_rect != null)
            {
                _rect.anchoredPosition = _home + _offset * (1f - e) + new Vector2(_glitchX, 0f);
                if (Glitch)
                    _rect.localScale = new Vector3(1f + _glitchStretch, 1f - _glitchStretch * 0.5f, 1f);
            }
        }
    }
}
