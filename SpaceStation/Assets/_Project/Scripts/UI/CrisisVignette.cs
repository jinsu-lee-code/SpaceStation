using SpaceStation.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceStation.UI
{
    /// <summary>
    /// 위기 비네팅 (5-2): 실패 조건 경고(산소 고갈·만족도 0·코어 붕괴)가 진행 중이면 비네팅이 붉게 맥박친다.
    /// 남은 시간이 짧을수록 빠르고 진하게. 평소에는 약한 검은 비네팅.
    /// 전역 Volume의 프로필을 런타임 복사본으로 바꿔 쓰므로 에셋은 바뀌지 않는다.
    /// </summary>
    public sealed class CrisisVignette : MonoBehaviour
    {
        [SerializeField] private Volume _volume;
        [SerializeField] private ResourceController _resources;
        [SerializeField] private Color _crisisColor = new Color(0.75f, 0.05f, 0.05f);
        [SerializeField, Range(0f, 1f)] private float _crisisIntensity = 0.42f;
        [Tooltip("맥박 속도 (초당, 남은 시간이 짧아질수록 최대 2배)")]
        [SerializeField, Min(0.1f)] private float _pulseSpeed = 1.2f;
        [SerializeField, Min(0.1f)] private float _fadeSpeed = 2f;

        private Vignette _vignette;
        private Color _baseColor;
        private float _baseIntensity;
        private float _blend;
        private float _phase;

        private void Start()
        {
            if (_volume == null || !_volume.profile.TryGet(out _vignette))
            {
                enabled = false;
                return;
            }
            _baseColor = _vignette.color.value;
            _baseIntensity = _vignette.intensity.value;
            _vignette.color.overrideState = true;
            _vignette.intensity.overrideState = true;
        }

        private void Update()
        {
            var f = _resources.Failure;
            float remaining = Min(f.OxygenRemaining, Min(f.SatisfactionRemaining, f.CoreRemaining));
            bool crisis = remaining >= 0f;
            _blend = Mathf.MoveTowards(_blend, crisis ? 1f : 0f, _fadeSpeed * Time.unscaledDeltaTime);
            if (_blend <= 0f && !crisis)
            {
                Apply(0f, 0f);
                return;
            }
            // 남은 시간 30초 이하부터 맥박이 빨라짐
            float urgency = crisis ? 1f + Mathf.Clamp01(1f - remaining / 30f) : 1f;
            _phase += Time.unscaledDeltaTime * _pulseSpeed * urgency * Mathf.PI * 2f;
            float pulse = 0.65f + 0.35f * Mathf.Sin(_phase);
            Apply(_blend, pulse);
        }

        private void Apply(float blend, float pulse)
        {
            _vignette.color.value = Color.Lerp(_baseColor, _crisisColor, blend);
            _vignette.intensity.value = Mathf.Lerp(_baseIntensity, _crisisIntensity * pulse, blend);
        }

        /// <summary>−1(진행 안 함)을 무시한 최소값.</summary>
        private static float Min(float a, float b)
        {
            if (a < 0f) return b;
            if (b < 0f) return a;
            return Mathf.Min(a, b);
        }
    }
}
