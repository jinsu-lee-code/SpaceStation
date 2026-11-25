using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 사운드 개편 4-3 (2026-11-25): F를 누르는 동안 손 동작과 맞춰 한 번 울리는 소리 (밸브 돌림 · 소화기 분사 · 필터 교체).
    /// <see cref="Begin"/> = 처음부터 울림, 다 누르기 전에 손을 떼면 <see cref="Release"/> = 0.08초에 줄여 끔, 다 누르면 끝까지 (소리 속 "툭" · "딸깍"이
    /// 누르는 시간 끝에 오게 만든 파일). 물체에 붙어 3D (minDistance까지 그대로 → maxDistance에서 0), 물체와 함께 지워짐. 만들기 = <see cref="AudioService.AttachHeld"/>.
    /// </summary>
    public sealed class HeldSound : MonoBehaviour
    {
        private const float ReleaseSeconds = 0.08f;

        private AudioSource _source;
        private SoundCue _cue;
        private float _level;
        private bool _releasing;

        /// <summary>울리는 중 (손을 떼어 줄어드는 중은 아님).</summary>
        public bool Playing => _source != null && _source.isPlaying && !_releasing;

        internal void Init(SoundCue cue, float minDistance, float maxDistance)
        {
            _cue = cue;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.dopplerLevel = 0f;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = minDistance;
            _source.maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
            _source.volume = 0f;
        }

        /// <summary>처음부터 다시 울림 (누르기 시작).</summary>
        public void Begin()
        {
            if (_source == null || _cue == null)
                return;
            var clip = _cue.Pick();
            if (clip == null)
                return;
            _source.Stop();
            _source.clip = clip;
            _source.time = 0f;
            _source.pitch = Random.Range(_cue.Pitch.x, _cue.Pitch.y);
            _level = 1f;
            _releasing = false;
            _source.volume = _cue.Volume * AudioVolumes.For(_cue.Bus);
            _source.Play();
        }

        /// <summary>다 누르기 전에 손을 뗌 — 짧게 줄여 끔.</summary>
        public void Release()
        {
            if (_source != null && _source.isPlaying)
                _releasing = true;
        }

        private void Update()
        {
            if (_source == null || _cue == null || !_source.isPlaying)
                return;
            if (_releasing)
            {
                _level -= Time.unscaledDeltaTime / ReleaseSeconds;
                if (_level <= 0f)
                {
                    _source.Stop();
                    _releasing = false;
                    return;
                }
            }
            _source.volume = _level * _cue.Volume * AudioVolumes.For(_cue.Bus);
        }

        private void OnDisable()
        {
            if (_source != null)
                _source.Stop();
            _releasing = false;
        }
    }
}
