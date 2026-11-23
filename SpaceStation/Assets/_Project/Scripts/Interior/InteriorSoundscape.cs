using System.Collections.Generic;
using SpaceStation.Audio;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 사운드 개편 D-7b (2026-11-23): 지금 있는 방의 소리 풍경(<see cref="RoomSoundscape"/>) 재생 — 다른 방은 울리지 않음 (D-4).
    /// 방이 바뀌면 이전 방 소리는 0.7초에 걸쳐 내리고 새 방 것을 올림. 가끔 소리는 AudioService 목소리 풀로 그 자리 3D,
    /// 음 있는 소리 · 대표 선율은 음계 높이로 바꿔 내부 음악의 다음 박에. <see cref="InteriorAudio"/>가 갖고 InteriorMode Update가 Tick.
    /// </summary>
    public sealed class InteriorSoundscape
    {
        private const float FadeSpeed = 1.4f;     // 초당 (0.7초에 1 → 0)
        private const double Beat = 60.0 / 90.0;  // 내부 음악 빠르기 (SOUND_LIST_NEW "내부 음악 이어 붙이기")

        private sealed class Track
        {
            public AudioSource Source;
            public SoundCue Cue;
            public float Level;
            public float Target;
            public float Day;
            public float Night;
        }

        private const float DayFade = 0.2f;     // 낮 ↔ 밤 넘어가기 (5초)
        private float _dayLevel = 1f;           // 1 = 낮, 0 = 밤 (부드럽게)

        private GameObject _root;
        private readonly List<Track> _tracks = new List<Track>(); // 지금 방 + 내려가는 중인 이전 방의 바탕 · 구역 루프
        private readonly Dictionary<RoomSoundscape, float> _motifAt = new Dictionary<RoomSoundscape, float>();
        private RoomSoundscape _scape;
        private Transform _room;
        private float[] _timers = System.Array.Empty<float>();

        public bool Active => _scape != null;

        /// <summary>지금 방의 소리 풍경 (없으면 null — 그 방은 기존 환경음 묶음). prefab = 그 방 템플릿 프리팹 (따라 붙는 물체의 처음 자세).</summary>
        public void SetRoom(RoomSoundscape scape, Transform room, Transform prefab = null)
        {
            if (scape == _scape && room == _room)
                return;
            foreach (var t in _tracks)
                t.Target = 0f;
            _scape = room != null ? scape : null;
            _room = room;
            _timers = System.Array.Empty<float>();
            if (_scape == null)
                return;
            if (_root == null)
                _root = new GameObject("InteriorSoundscape");
            AddTrack(scape.Bed, null, null, 0f, 0f, 1f, scape.BedNight, false);
            foreach (var e in scape.Emitters)
            {
                Vector3? at = e.Spatial ? room.TransformPoint(e.Position) : (Vector3?)null;
                Transform follow = null;
                if (at.HasValue && !string.IsNullOrEmpty(e.Follow) && (follow = FindChild(room, e.Follow)) != null)
                {
                    // 도는 물체(구름 고리)는 들어선 때 이미 돌아가 있음 → 프리팹의 처음 자세 기준 자리를 지금 자세로 옮김
                    var start = prefab != null ? FindChild(prefab, e.Follow) : null;
                    if (start != null)
                        at = follow.TransformPoint((prefab.worldToLocalMatrix * start.localToWorldMatrix).inverse.MultiplyPoint3x4(e.Position));
                }
                AddTrack(e.Cue, at, follow, e.MinDistance, e.MaxDistance, e.Day, e.Night, e.OnBeat);
            }
            _timers = new float[scape.Scatters.Count];
            for (int i = 0; i < _timers.Length; i++)
                _timers[i] = Random.Range(0.2f, 0.8f) * scape.Scatters[i].Interval.x; // 첫 소리는 조금 일찍
            float now = Time.unscaledTime;
            var svc = AudioService.Instance;
            if (svc != null && scape.Motif != null && scape.Motif.HasClip
                && (!_motifAt.TryGetValue(scape, out float last) || now - last > scape.MotifCooldown))
            {
                _motifAt[scape] = now;
                svc.TryNextMusicBeat(Beat, out double dsp);
                svc.Play(scape.Motif, 1f, 0f, (float)(dsp - AudioSettings.dspTime));
            }
        }

        public void Stop() => SetRoom(null, null);

        public void Tick(float dt, bool day)
        {
            _dayLevel = Mathf.MoveTowards(_dayLevel, day ? 1f : 0f, DayFade * dt);
            for (int i = _tracks.Count - 1; i >= 0; i--)
            {
                var t = _tracks[i];
                t.Level = Mathf.MoveTowards(t.Level, t.Target, FadeSpeed * dt);
                if (t.Level <= 0f && t.Target <= 0f)
                {
                    if (t.Source != null)
                        Object.Destroy(t.Source.gameObject);
                    _tracks.RemoveAt(i);
                    continue;
                }
                if (t.Source == null) // 따라 붙은 물체가 방과 함께 지워짐
                {
                    _tracks.RemoveAt(i);
                    continue;
                }
                t.Source.volume = t.Level * t.Cue.Volume * Mathf.Lerp(t.Night, t.Day, _dayLevel) * AudioVolumes.For(t.Cue.Bus);
            }
            var svc = AudioService.Instance;
            if (_scape == null || _room == null || svc == null)
                return;
            for (int i = 0; i < _timers.Length; i++)
            {
                if ((_timers[i] -= dt) > 0f)
                    continue;
                var s = _scape.Scatters[i];
                _timers[i] = Random.Range(s.Interval.x, Mathf.Max(s.Interval.x, s.Interval.y));
                if (s.Cue == null || !s.Cue.HasClip || (s.When == RoomSoundscape.DayPart.Day && !day) || (s.When == RoomSoundscape.DayPart.Night && day))
                    continue;
                var local = s.Center + Vector3.Scale(s.Size, new Vector3(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f));
                var pos = _room.TransformPoint(local);
                if (s.Tonal && s.Notes != null && s.Notes.Length > 0)
                {
                    int note = s.Notes[Random.Range(0, s.Notes.Length)];
                    svc.TryNextMusicBeat(Beat, out double dsp);
                    svc.PlayAt(s.Cue, pos, 1f, s.MinDistance, s.MaxDistance, Mathf.Pow(2f, (note - s.BaseNote) / 12f), (float)(dsp - AudioSettings.dspTime));
                }
                else
                {
                    svc.PlayAt(s.Cue, pos, 1f, s.MinDistance, s.MaxDistance);
                }
            }
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name)
                return root;
            foreach (Transform c in root)
            {
                var found = FindChild(c, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private void AddTrack(SoundCue cue, Vector3? position, Transform follow, float minDistance, float maxDistance, float day, float night, bool onBeat)
        {
            if (cue == null || !cue.HasClip)
                return;
            var clip = cue.Pick();
            var go = new GameObject(clip.name);
            go.transform.SetParent(follow != null && position.HasValue ? follow : _root.transform, true);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.clip = clip;
            s.dopplerLevel = 0f;
            s.volume = 0f;
            if (position.HasValue)
            {
                go.transform.position = position.Value;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = minDistance;
                s.maxDistance = Mathf.Max(minDistance + 0.1f, maxDistance);
            }
            else
            {
                s.spatialBlend = 0f;
            }
            // 맥동 루프 (길이 = 박의 배수)는 음악의 2박 격자에 맞춰 시작, 나머지는 아무 자리에서 (다시 들어와도 같은 곳부터가 아니게)
            var svc = AudioService.Instance;
            if (onBeat && svc != null && svc.TryNextMusicBeat(Beat * 2, out double dsp))
            {
                s.PlayScheduled(dsp);
            }
            else
            {
                s.time = Random.Range(0f, clip.length * 0.9f);
                s.Play();
            }
            _tracks.Add(new Track { Source = s, Cue = cue, Target = 1f, Day = day, Night = night });
        }
    }
}
