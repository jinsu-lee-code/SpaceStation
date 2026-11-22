using System.Collections.Generic;
using SpaceStation.Audio;
using SpaceStation.Data;
using UnityEngine;

namespace SpaceStation.Interior
{
    /// <summary>
    /// 11-9 내부 방문 소리: 지금 있는 방의 환경음(성격별 6묶음 루프, 방을 옮기면 교차 페이드) + 발소리. 11-10: 파손 방 경보 루프.
    /// 2026-11-23: 내부 음악 — 방 모듈 단계별 4곡을 함께 돌리고 지금 단계 층만 올림 (<see cref="SetMusicStage"/>).
    /// 루프는 처음 들어갈 때 한 번 만들어 두고(AudioService 루프는 지울 수 없음), 나오면 모두 0으로 내린다.
    /// <see cref="InteriorMode"/>가 방이 바뀔 때 <see cref="SetRoom"/>, 나올 때 <see cref="Stop"/>를 부른다.
    /// </summary>
    public sealed class InteriorAudio
    {
        private const float FadeSpeed = 0.7f;   // 초당 (1.4초에 0 → 1)
        private const float WalkVolume = 0.75f;

        private readonly Dictionary<InteriorAmbience, AudioService.Loop> _loops = new Dictionary<InteriorAmbience, AudioService.Loop>();
        private AudioService.Loop _alarm; // 11-10 파손 방 경보
        private AudioService.MusicLayers _music; // 2026-11-23 내부 음악 (모듈 단계별 4곡, 함께 돌며 음량만 바뀜)
        private SoundCue[] _musicCues;
        private FirstPersonController _player;

        public void Attach(FirstPersonController player)
        {
            Detach();
            _player = player;
            if (_player != null)
                _player.Footstep += HandleFootstep;
        }

        public void Detach()
        {
            if (_player != null)
                _player.Footstep -= HandleFootstep;
            _player = null;
        }

        /// <summary>이 묶음만 올리고 나머지는 내림.</summary>
        public void SetRoom(InteriorAmbience ambience)
        {
            if (!EnsureLoops())
                return;
            foreach (var pair in _loops)
                pair.Value.Target = pair.Key == ambience ? 1f : 0f;
        }

        /// <summary>지금 방 모듈 단계의 음악 층을 올림 (3 = 3단계 이상). 그 단계 곡이 없으면 아래 단계 곡으로.</summary>
        public void SetMusicStage(int stage)
        {
            if (!EnsureLoops() || _music == null)
                return;
            int layer = -1;
            for (int i = Mathf.Clamp(stage, 0, _musicCues.Length - 1); i >= 0 && layer < 0; i--)
                if (_musicCues[i] != null && _musicCues[i].HasClip)
                    layer = i;
            _music.Active = layer;
        }

        /// <summary>11-10 지금 있는 방이 파손이면 경보음을 올린다.</summary>
        public void SetAlarm(bool on)
        {
            if (!EnsureLoops() || _alarm == null)
                return;
            _alarm.Target = on ? 1f : 0f;
        }

        public void Stop()
        {
            foreach (var loop in _loops.Values)
                loop.Target = 0f;
            if (_alarm != null)
                _alarm.Target = 0f;
            if (_music != null)
                _music.Active = -1;
            Detach();
        }

        private bool EnsureLoops()
        {
            if (_loops.Count > 0)
                return true;
            var service = AudioService.Instance;
            if (service == null || service.Library == null)
                return false;
            var lib = service.Library;
            Add(service, InteriorAmbience.Life, lib.RoomLife);
            Add(service, InteriorAmbience.Water, lib.RoomWater);
            Add(service, InteriorAmbience.Machine, lib.RoomMachine);
            Add(service, InteriorAmbience.Heat, lib.RoomHeat);
            Add(service, InteriorAmbience.Electric, lib.RoomElectric);
            Add(service, InteriorAmbience.Hall, lib.RoomHall);
            if (lib.InteriorAlarm != null && lib.InteriorAlarm.HasClip)
            {
                _alarm = service.CreateLoop(lib.InteriorAlarm);
                _alarm.FadeSpeed = 3f; // 방에 들어서면 빠르게
                _alarm.Target = 0f;
            }
            _musicCues = new[] { lib.MusicInterior0, lib.MusicInterior1, lib.MusicInterior2, lib.MusicInterior3 };
            _music = service.CreateMusicLayers(_musicCues);
            return true;
        }

        private void Add(AudioService service, InteriorAmbience kind, SoundCue cue)
        {
            var loop = service.CreateLoop(cue);
            loop.FadeSpeed = FadeSpeed;
            loop.Target = 0f;
            _loops[kind] = loop;
        }

        private static void HandleFootstep(bool run)
        {
            AudioService.TryPlay(l => l.Footstep, run ? 1f : WalkVolume);
        }
    }
}
