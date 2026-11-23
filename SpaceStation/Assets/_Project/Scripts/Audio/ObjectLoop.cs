using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 사운드 개편 5-2 (2026-11-23): 물체에 붙어 도는 3D 루프 — 순찰차 웅 · 용광로 · 번개 정원 · 크레인처럼 체험 물체가 계속 내는 소리.
    /// 물체를 따라 움직이고 물체가 지워지면 같이 사라짐. <see cref="Level"/> (0 ~ 1)을 바꾸면 부드럽게 따라감 (설정 음량도 매 프레임 반영).
    /// 만들기 = <see cref="AudioService.AttachLoop"/>.
    /// </summary>
    public sealed class ObjectLoop : MonoBehaviour
    {
        private AudioSource _source;
        private SoundCue _cue;
        private float _level;

        /// <summary>목표 크기 (0 = 끔, 1 = 큐 음량 그대로).</summary>
        public float Level { get; set; } = 1f;
        /// <summary>재생 높이 배율 (빠르기에 따라 바꿔도 됨).</summary>
        public float Pitch { get; set; } = 1f;
        /// <summary>초당 크기 변화량 (2.5 = 0.4초에 0 → 1).</summary>
        public float FadeSpeed { get; set; } = 2.5f;
        /// <summary>
        /// 내부 음악 박 길이 (초, 0 = 끔) — 음이 있는 루프 (회전목마 오르골)를 켤 때 음악 박에 맞춰 시작: 다음 박에, 고리 안 자리도 음악과 같은 박으로
        /// (고리 길이 = 박의 배수 · 음악 길이의 약수일 것). 음악이 없으면 바로 시작.
        /// </summary>
        public double SyncBeat { get; set; }

        private double _scheduledUntil;

        internal void Init(SoundCue cue, float minDistance, float maxDistance)
        {
            _cue = cue;
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.clip = cue.Pick();
            _source.dopplerLevel = 0f;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = minDistance;
            _source.maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
            _source.volume = 0f;
            // 같은 소리를 여러 물체에 붙여도 한 박자로 겹치지 않게 아무 자리에서 시작
            if (_source.clip != null)
                _source.time = Random.Range(0f, _source.clip.length * 0.9f);
        }

        private void Update()
        {
            if (_source == null || _cue == null)
                return;
            _level = Mathf.MoveTowards(_level, Mathf.Clamp01(Level), FadeSpeed * Time.unscaledDeltaTime);
            if (_level <= 0f)
            {
                if (_source.isPlaying)
                    _source.Stop();
                return;
            }
            if (!_source.isPlaying && _source.clip != null && AudioSettings.dspTime >= _scheduledUntil)
                Begin();
            _source.volume = _level * _cue.Volume * AudioVolumes.For(_cue.Bus);
            _source.pitch = Pitch;
        }

        private void OnDisable()
        {
            // 방을 나가며 꺼진 물체: 다시 켜질 때 0에서 부드럽게 (예전 크기로 잠깐 울리지 않게)
            _level = 0f;
            _scheduledUntil = 0;
        }

        private void Begin()
        {
            var svc = AudioService.Instance;
            double length = _source.clip.length;
            if (SyncBeat > 0 && svc != null && svc.TryNextMusicBeat(SyncBeat, out double beatAt) && svc.TryNextMusicBeat(length, out double loopAt))
            {
                // 다음 박 (beatAt)에 시작 — 그때 음악 격자에서 고리 처음 (loopAt)까지 남은 만큼 앞선 자리에서
                double pos = length - (loopAt - beatAt);
                _source.time = (float)(pos >= length - 0.001 || pos < 0 ? 0 : pos);
                _source.PlayScheduled(beatAt);
                _scheduledUntil = beatAt + 0.05;
                return;
            }
            _source.Play();
        }
    }
}
