using System.Collections.Generic;
using UnityEngine;

namespace SpaceStation.Audio
{
    /// <summary>
    /// 5-8 소리 재생기. 2D AudioSource 풀로 한 번 울리는 소리를, 별도 소스로 반복음(루프)과 음악(교차 페이드) · 음악 층(내부 4곡)을 낸다.
    /// 시간은 실시간(unscaled) 기준 — 일시정지 중에도 UI 소리는 난다.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class AudioService : MonoBehaviour
    {
        [SerializeField] private SoundLibrary _library;
        [SerializeField, Min(4)] private int _voiceCount = 24;
        [Tooltip("곡이 끝나기 이 시간(초) 전부터 다음 곡과 교차 페이드")]
        [SerializeField, Min(0.1f)] private float _musicCrossfade = 4f;

        private sealed class Voice
        {
            public AudioSource Source;
            public SoundCue Cue;
            public float StartedAt;
            public float BusyUntil;
        }

        /// <summary>반복음 하나. Target(0~1)을 바꾸면 부드럽게 따라간다.</summary>
        public sealed class Loop
        {
            internal AudioSource Source;
            internal SoundCue Cue;
            internal float Level;
            public float Target;
            public float Pitch = 1f;
            /// <summary>초당 변화량 (1 = 1초에 0→1).</summary>
            public float FadeSpeed = 1f;
        }

        private sealed class MusicDeck
        {
            public AudioSource Source;
            public float Level;
            public float Target;
        }

        /// <summary>
        /// 사운드 개편 (2026-11-23) 음악 층: 같은 길이 곡 여러 개를 같은 순간에 함께 돌리고 음량만 바꾼다 —
        /// 층을 바꿔도 박 · 선율이 이어짐 (내부 음악 4곡). 바깥 음악(<see cref="PlayMusic"/>)이 나는 동안은 모두 내려감.
        /// </summary>
        public sealed class MusicLayers
        {
            internal AudioSource[] Sources;
            internal SoundCue[] Cues;
            internal float[] Levels;
            internal bool Playing;
            internal double StartDsp;
            /// <summary>올릴 층 번호 (-1 = 모두 내림).</summary>
            public int Active = -1;
            /// <summary>초당 변화량 (0.4 = 2.5초에 0 → 1).</summary>
            public float FadeSpeed = 0.4f;
        }

        public static AudioService Instance { get; private set; }
        public SoundLibrary Library => _library;

        /// <summary>서비스가 있으면 재생 (없으면 무시 — 테스트·소리 없는 씬용).</summary>
        public static void TryPlay(System.Func<SoundLibrary, SoundCue> pick, float volume = 1f)
        {
            var s = Instance;
            if (s != null && s._library != null)
                s.Play(pick(s._library), volume);
        }

        /// <summary>점수 "띵"이 연달아 오를 음 (반음, 기준 = 도6): 도 · 레 · 파 · 솔 · 라 · 도 · 레 · 파 — F 오음계라 내부 음악과 안 부딪힘.</summary>
        private static readonly int[] ScoreSteps = { 0, 2, 5, 7, 9, 12, 14, 17 };

        /// <summary>미니게임 점수 "띵" (5-1) — streak = 연달아 맞힌 수 (1부터), 맞힐수록 한 칸씩 높게 (끝 칸에서 멈춤).</summary>
        public static void TryPlayScore(int streak)
        {
            var s = Instance;
            if (s == null || s._library == null)
                return;
            int k = Mathf.Clamp(streak - 1, 0, ScoreSteps.Length - 1);
            s.Play(s._library.GameScore, 1f, 0f, 0f, Mathf.Pow(2f, ScoreSteps[k] / 12f));
        }

        /// <summary>
        /// 체험 · 미니게임 음 (5-2 — 등불 다섯 · 시냅스 일곱 · 꽃 그네 종처럼 자리마다 다른 음): step = 음계 칸 (0부터 도 · 레 · 파 · 솔 · 라 · 위 도 …, F 오음계),
        /// 그 큐의 기준 음 (소리 파일 높이)에서 올림. 큐의 음높이 흔들림은 Pitch (1, 1)로 꺼 둘 것 — 조가 깨짐.
        /// delay (실시간 초) = 화음을 굴려 칠 때 (큐 쉬는 시간보다 길게 벌릴 것).
        /// </summary>
        public static void TryPlayNote(System.Func<SoundLibrary, SoundCue> pick, int step, float volume = 1f, float delay = 0f)
        {
            var s = Instance;
            if (s != null && s._library != null)
                s.Play(pick(s._library), volume, 0f, delay, NotePitch(step));
        }

        /// <summary><see cref="TryPlayNote"/>을 그 자리 3D로 (2 m까지 그대로 → 15 m에서 0).</summary>
        public static void TryPlayNoteAt(System.Func<SoundLibrary, SoundCue> pick, Vector3 position, int step, float volume = 1f)
        {
            var s = Instance;
            if (s != null && s._library != null)
                s.PlayAt(pick(s._library), position, volume, 2f, 15f, NotePitch(step), 0f);
        }

        /// <summary>오음계 한 옥타브 (도 · 레 · 파 · 솔 · 라) — 칸 5마다 한 옥타브 위.</summary>
        private static readonly int[] Pentatonic = { 0, 2, 5, 7, 9 };

        private static float NotePitch(int step)
        {
            step = Mathf.Max(0, step);
            return Mathf.Pow(2f, (Pentatonic[step % 5] + 12 * (step / 5)) / 12f);
        }

        /// <summary>
        /// 물체에 붙는 3D 루프 (5-2 — 순찰차 · 용광로 · 번개 정원 · 크레인처럼 체험 물체가 계속 내는 소리): at의 자식으로 만들어 따라 움직임 ·
        /// minDistance까지 그대로 → maxDistance에서 0. 돌려준 <see cref="ObjectLoop"/>의 Level · Pitch로 조절, 물체와 함께 지워짐. 서비스 · 클립이 없으면 null.
        /// </summary>
        public static ObjectLoop AttachLoop(System.Func<SoundLibrary, SoundCue> pick, Transform at, float minDistance = 2f, float maxDistance = 15f)
        {
            var s = Instance;
            if (s == null || s._library == null || at == null)
                return null;
            var cue = pick(s._library);
            if (cue == null || !cue.HasClip)
                return null;
            var go = new GameObject("ObjectLoop_" + (cue.Clips[0] != null ? cue.Clips[0].name : "loop"));
            go.transform.SetParent(at, false);
            var loop = go.AddComponent<ObjectLoop>();
            loop.Init(cue, minDistance, maxDistance);
            return loop;
        }

        /// <summary>
        /// 사운드 개편 4-3 (2026-11-25): F를 누르는 동안 손 동작과 맞춰 한 번 울리는 3D 소리 (밸브 · 소화기 분사 · 필터) — at의 자식으로 만들어 따라 움직임.
        /// 돌려준 <see cref="HeldSound"/>의 Begin (누르기 시작) · Release (다 누르기 전에 뗌)로 조절, 물체와 함께 지워짐. 서비스 · 클립이 없으면 null.
        /// </summary>
        public static HeldSound AttachHeld(System.Func<SoundLibrary, SoundCue> pick, Transform at, float minDistance = 2f, float maxDistance = 15f)
        {
            var s = Instance;
            if (s == null || s._library == null || at == null)
                return null;
            var cue = pick(s._library);
            if (cue == null || !cue.HasClip)
                return null;
            var go = new GameObject("HeldSound_" + (cue.Clips[0] != null ? cue.Clips[0].name : "held"));
            go.transform.SetParent(at, false);
            var held = go.AddComponent<HeldSound>();
            held.Init(cue, minDistance, maxDistance);
            return held;
        }

        private readonly Dictionary<SoundCue, Loop> _rideLoops = new Dictionary<SoundCue, Loop>();

        /// <summary>
        /// 체험 루프 (5-2 — 기차 달림 · 관람차 삐걱 · 노 젓기처럼 타고 있는 동안, 탄 사람 귀에 2D): level 0 = 끔 (0.4초에 내려감) · 1 = 큐 음량 그대로.
        /// 큐마다 하나를 처음 켤 때 만들어 다시 씀. 체험을 나가거나 방을 떠날 때 (OnDisable 포함) 꼭 0으로.
        /// </summary>
        public static void SetRideLoop(System.Func<SoundLibrary, SoundCue> pick, float level, float pitch = 1f)
        {
            var s = Instance;
            if (s == null || s._library == null)
                return;
            var cue = pick(s._library);
            if (cue == null || !cue.HasClip)
                return;
            if (!s._rideLoops.TryGetValue(cue, out var loop))
            {
                if (level <= 0f)
                    return;
                loop = s.CreateLoop(cue);
                loop.FadeSpeed = 2.5f;
                s._rideLoops[cue] = loop;
            }
            loop.Target = level;
            loop.Pitch = pitch;
        }

        private readonly List<Voice> _voices = new List<Voice>();
        private readonly List<Loop> _loops = new List<Loop>();
        private readonly List<MusicLayers> _layers = new List<MusicLayers>();
        private readonly MusicDeck[] _decks = new MusicDeck[2];
        private int _activeDeck;
        private SoundCue _music;
        private float _musicDuck = 1f;
        private float _musicDuckTarget = 1f;
        private float _musicDuckSpeed = 0.8f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            for (int i = 0; i < _voiceCount; i++)
                _voices.Add(new Voice { Source = NewSource($"Voice{i}") });
            for (int i = 0; i < 2; i++)
                _decks[i] = new MusicDeck { Source = NewSource($"Music{i}") };
            ApplyMaster();
            AudioVolumes.Changed += ApplyMaster;
        }

        private void OnDestroy()
        {
            AudioVolumes.Changed -= ApplyMaster;
            if (Instance == this)
                Instance = null;
        }

        private static void ApplyMaster() => AudioListener.volume = AudioVolumes.Master;

        private AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.dopplerLevel = 0f;
            return s;
        }

        // ---------------- 한 번 울리는 소리 ----------------

        /// <param name="volume">추가 배율</param>
        /// <param name="pan">-1 왼쪽 ~ 1 오른쪽</param>
        /// <param name="delay">지연 (실시간 초)</param>
        /// <param name="pitch">추가 음높이 배율</param>
        public bool Play(SoundCue cue, float volume = 1f, float pan = 0f, float delay = 0f, float pitch = 1f)
            => PlayVoice(cue, volume, pan, delay, pitch, null, 0f, 0f);

        /// <summary>
        /// 사운드 개편 D-4 (2026-11-20): 그 자리에서 3D로 — minDistance까지 그대로, maxDistance에서 들리지 않음 (직선 감쇠).
        /// 리스너 = 메인 카메라 (내부 1인칭도 같은 카메라).
        /// </summary>
        public bool PlayAt(SoundCue cue, Vector3 position, float volume = 1f, float minDistance = 2f, float maxDistance = 15f)
            => PlayVoice(cue, volume, 0f, 0f, 1f, position, minDistance, maxDistance);

        /// <summary>그 자리 3D + 음높이 배율 · 지연(실시간 초) — 소리 풍경의 음 있는 가끔 소리를 음계 높이 · 다음 박에 (2026-11-23).</summary>
        public bool PlayAt(SoundCue cue, Vector3 position, float volume, float minDistance, float maxDistance, float pitch, float delay)
            => PlayVoice(cue, volume, 0f, delay, pitch, position, minDistance, maxDistance);

        /// <summary>내부 음악 층이 돌고 있으면 그 박 격자(beat초)에서 지금 뒤 첫 박의 dsp 시각. 아니면 false (dsp = 곧바로).</summary>
        public bool TryNextMusicBeat(double beat, out double dsp)
        {
            double now = AudioSettings.dspTime + 0.03;
            foreach (var m in _layers)
            {
                if (!m.Playing || beat <= 0)
                    continue;
                dsp = m.StartDsp + System.Math.Ceiling((now - m.StartDsp) / beat) * beat;
                return true;
            }
            dsp = now;
            return false;
        }

        /// <summary>
        /// 이번 프레임에 막 울린 그 큐의 소리를 멈춤 (아직 귀에 닿지 않은 순간) — 한 클릭에 소리 둘이 겹칠 때 하나만 남김
        /// (2026-11-25 UI 한 벌: 취소 · 닫기 버튼이 창을 닫으면 창 닫힘 소리 대신 취소음 하나).
        /// </summary>
        public void StopStartedThisFrame(SoundCue cue)
        {
            if (cue == null)
                return;
            float now = Time.unscaledTime;
            foreach (var v in _voices)
            {
                if (v.Cue != cue || v.StartedAt != now || v.BusyUntil <= now)
                    continue;
                v.Source.Stop();
                v.BusyUntil = 0f;
            }
        }

        /// <summary>
        /// 그 큐의 소리가 이번 프레임에 막 울렸는지 (2026-11-26 정한 것 S-2: 창을 여는 버튼은 확인음 없이 창 열기 소리 하나만 — <see cref="UiSound"/>가 봄).
        /// </summary>
        public bool StartedThisFrame(SoundCue cue)
        {
            if (cue == null)
                return false;
            float now = Time.unscaledTime;
            foreach (var v in _voices)
            {
                if (v.Cue == cue && v.StartedAt == now && v.BusyUntil > now)
                    return true;
            }
            return false;
        }

        public static void TryPlayAt(System.Func<SoundLibrary, SoundCue> pick, Vector3 position, float volume = 1f)
        {
            var s = Instance;
            if (s != null && s._library != null)
                s.PlayAt(pick(s._library), position, volume);
        }

        private bool PlayVoice(SoundCue cue, float volume, float pan, float delay, float pitch, Vector3? position, float minDistance, float maxDistance)
        {
            if (cue == null || !cue.HasClip)
                return false;
            float now = Time.unscaledTime;
            float at = now + Mathf.Max(0f, delay);
            if (Mathf.Abs(at - cue.LastPlayed) < cue.Cooldown)
                return false;

            var clip = cue.Pick();
            if (clip == null)
                return false;
            var voice = Acquire(cue, now);
            cue.LastPlayed = at;

            float p = Random.Range(cue.Pitch.x, cue.Pitch.y) * pitch;
            var s = voice.Source;
            s.Stop();
            s.clip = clip;
            s.loop = false;
            s.pitch = p;
            s.panStereo = Mathf.Clamp(pan, -1f, 1f);
            if (position.HasValue)
            {
                s.transform.position = position.Value;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = minDistance;
                s.maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
            }
            else
            {
                s.spatialBlend = 0f;
            }
            s.volume = cue.Volume * volume * AudioVolumes.For(cue.Bus);
            voice.Cue = cue;
            voice.StartedAt = at;
            voice.BusyUntil = at + clip.length / Mathf.Max(0.05f, Mathf.Abs(p)) + 0.05f;
            if (delay > 0f)
                s.PlayDelayed(delay);
            else
                s.Play();
            return true;
        }

        private Voice Acquire(SoundCue cue, float now)
        {
            if (cue.MaxVoices > 0)
            {
                int count = 0;
                Voice oldest = null;
                foreach (var v in _voices)
                {
                    if (v.Cue != cue || v.BusyUntil <= now)
                        continue;
                    count++;
                    if (oldest == null || v.StartedAt < oldest.StartedAt)
                        oldest = v;
                }
                if (count >= cue.MaxVoices && oldest != null)
                    return oldest;
            }
            Voice free = null;
            Voice oldestAny = null;
            foreach (var v in _voices)
            {
                if (v.BusyUntil <= now)
                {
                    free = v;
                    break;
                }
                if (oldestAny == null || v.StartedAt < oldestAny.StartedAt)
                    oldestAny = v;
            }
            return free ?? oldestAny;
        }

        // ---------------- 반복음 ----------------

        public Loop CreateLoop(SoundCue cue)
        {
            var loop = new Loop { Cue = cue, Source = NewSource("Loop") };
            loop.Source.loop = true;
            loop.Source.clip = cue != null ? cue.Pick() : null;
            loop.Source.volume = 0f;
            _loops.Add(loop);
            return loop;
        }

        // ---------------- 음악 ----------------

        /// <summary>음악 상태 전환. 같은 묶음이면 그대로, 다르면 교차 페이드. null이면 페이드아웃.</summary>
        public void PlayMusic(SoundCue cue)
        {
            if (cue == _music)
                return;
            _music = cue;
            if (cue == null || !cue.HasClip)
            {
                foreach (var d in _decks)
                    d.Target = 0f;
                return;
            }
            StartTrack(cue.Pick());
        }

        public MusicLayers CreateMusicLayers(params SoundCue[] cues)
        {
            var m = new MusicLayers { Cues = cues, Sources = new AudioSource[cues.Length], Levels = new float[cues.Length] };
            for (int i = 0; i < cues.Length; i++)
            {
                var s = NewSource($"MusicLayer{i}");
                s.loop = true;
                s.clip = cues[i] != null ? cues[i].Pick() : null;
                s.volume = 0f;
                m.Sources[i] = s;
            }
            _layers.Add(m);
            return m;
        }

        /// <summary>음악을 잠시 낮춤 (1 = 원래, 0 = 무음).</summary>
        /// <param name="speed">초당 변화량</param>
        public void DuckMusic(float level, float speed = 0.8f)
        {
            _musicDuckTarget = Mathf.Clamp01(level);
            _musicDuckSpeed = Mathf.Max(0.01f, speed);
        }

        private void StartTrack(AudioClip clip)
        {
            var old = _decks[_activeDeck];
            old.Target = 0f;
            _activeDeck = 1 - _activeDeck;
            var deck = _decks[_activeDeck];
            deck.Source.Stop();
            deck.Source.clip = clip;
            deck.Source.loop = false;
            deck.Level = 0f;
            deck.Target = 1f;
            deck.Source.volume = 0f;
            deck.Source.Play();
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            foreach (var loop in _loops)
            {
                loop.Level = Mathf.MoveTowards(loop.Level, Mathf.Clamp01(loop.Target), loop.FadeSpeed * dt);
                var s = loop.Source;
                if (loop.Level <= 0f)
                {
                    if (s.isPlaying)
                        s.Stop();
                    continue;
                }
                if (!s.isPlaying && s.clip != null)
                    s.Play();
                s.volume = loop.Level * loop.Cue.Volume * AudioVolumes.For(loop.Cue.Bus);
                s.pitch = loop.Pitch;
            }

            _musicDuck = Mathf.MoveTowards(_musicDuck, _musicDuckTarget, dt * _musicDuckSpeed);
            float fadeSpeed = 1f / _musicCrossfade;
            float baseVolume = _music != null ? _music.Volume : 0.5f;
            for (int i = 0; i < 2; i++)
            {
                var d = _decks[i];
                d.Level = Mathf.MoveTowards(d.Level, d.Target, fadeSpeed * dt);
                d.Source.volume = d.Level * baseVolume * _musicDuck * AudioVolumes.Music;
                if (d.Level <= 0f && d.Target <= 0f && d.Source.isPlaying)
                    d.Source.Stop();
            }
            foreach (var m in _layers)
                UpdateLayers(m, dt);

            // 곡이 끝나 가면 같은 묶음의 다음 곡으로 교차 페이드
            var active = _decks[_activeDeck];
            if (_music != null && active.Target > 0f && active.Source.clip != null)
            {
                float left = active.Source.clip.length - active.Source.time;
                // 스트리밍 곡은 시작 직후 잠깐 isPlaying이 false일 수 있어, 완전히 올라온 뒤에만 정지로 판단
                if (left <= _musicCrossfade || (!active.Source.isPlaying && active.Level >= 1f))
                    StartTrack(_music.Pick());
            }
        }

        private void UpdateLayers(MusicLayers m, float dt)
        {
            bool any = false;
            for (int i = 0; i < m.Sources.Length; i++)
            {
                float target = _music == null && i == m.Active ? 1f : 0f;
                m.Levels[i] = Mathf.MoveTowards(m.Levels[i], target, m.FadeSpeed * dt);
                any |= m.Levels[i] > 0f || target > 0f;
            }
            if (any && !m.Playing)
            {
                // 모든 층을 같은 dsp 순간에 시작 → 길이가 같으므로 계속 같은 자리를 돎
                double at = AudioSettings.dspTime + 0.1;
                foreach (var s in m.Sources)
                    if (s.clip != null)
                        s.PlayScheduled(at);
                m.Playing = true;
                m.StartDsp = at;
            }
            else if (!any && m.Playing)
            {
                foreach (var s in m.Sources)
                    s.Stop();
                m.Playing = false;
            }
            for (int i = 0; i < m.Sources.Length; i++)
                m.Sources[i].volume = m.Levels[i] * (m.Cues[i] != null ? m.Cues[i].Volume : 0f) * _musicDuck * AudioVolumes.Music;
        }
    }
}
