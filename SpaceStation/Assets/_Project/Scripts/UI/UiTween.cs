using UnityEngine;

namespace SpaceStation.UI
{
    /// <summary>
    /// 5-8 HUD 등장·퇴장 애니메이션 (게임 시간과 무관하게 unscaled). 알파 + 짧게 미끄러짐.
    /// Play()로 나타나고 Hide()로 사라진다. 대상 RectTransform의 원래 위치를 기억한다.
    /// </summary>
    public sealed class UiTween
    {
        private readonly RectTransform _rect;
        private readonly CanvasGroup _group;
        private readonly Vector2 _home;
        private readonly Vector2 _offset;
        private readonly float _inSeconds;
        private readonly float _outSeconds;
        private float _t;      // 0 = 숨김, 1 = 보임
        private int _dir;      // +1 나타나는 중, -1 사라지는 중

        public bool Visible => _t > 0f || _dir > 0;

        public UiTween(RectTransform rect, CanvasGroup group, Vector2 offset, float inSeconds = 0.22f, float outSeconds = 0.3f)
        {
            _rect = rect;
            _group = group;
            _home = rect != null ? rect.anchoredPosition : Vector2.zero;
            _offset = offset;
            _inSeconds = inSeconds;
            _outSeconds = outSeconds;
            Apply();
        }

        public void Play(bool restart = true)
        {
            if (restart)
                _t = 0f;
            _dir = 1;
        }

        public void Hide() => _dir = -1;

        public void Update()
        {
            if (_dir == 0)
                return;
            float dt = Time.unscaledDeltaTime;
            _t = Mathf.Clamp01(_t + _dir * dt / Mathf.Max(0.01f, _dir > 0 ? _inSeconds : _outSeconds));
            if (_t <= 0f || _t >= 1f)
                _dir = 0;
            Apply();
        }

        private void Apply()
        {
            float e = 1f - (1f - _t) * (1f - _t); // ease out
            if (_group != null)
                _group.alpha = e;
            if (_rect != null)
                _rect.anchoredPosition = _home + _offset * (1f - e);
        }
    }
}
